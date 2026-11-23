using Hrms.Application.Abstractions;
using Hrms.Application.Authorization;
using Hrms.Application.Modules;
using Hrms.Domain.Tenant;
using Hrms.Shared.Primitives;
using Hrms.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Workflow;

public sealed record WorkflowStepInput(
    int StepOrder, string Name, string ApproverType, string? ApproverParam, string ApprovalMode = "any",
    decimal? MinAmount = null, decimal? MaxAmount = null, Guid? FallbackUserId = null, int? SlaHours = null,
    string EscalationType = WorkflowEscalationTypes.None, string? EscalationParam = null, bool AllowSendBack = true, bool AllowDelegate = true,
    string? ApproverScope = null);

public sealed record WorkflowDefinitionInput(
    string Code, string Name, string DocumentType, string? Description, int Priority,
    decimal? MinAmount, decimal? MaxAmount, Guid? CompanyId, Guid? BranchId, Guid? DepartmentId,
    IReadOnlyList<WorkflowStepInput> Steps, uint? RowVersion = null);

public sealed record WorkflowStepDto(
    Guid Id, int StepOrder, string Name, string ApproverType, string? ApproverParam, string ApprovalMode, decimal? MinAmount,
    decimal? MaxAmount, Guid? FallbackUserId, int? SlaHours, string EscalationType, string? EscalationParam, bool AllowSendBack, bool AllowDelegate,
    string ApproverScope);

public sealed record WorkflowDefinitionDto(
    Guid Id, string Code, int Version, string Name, string DocumentType, string ModuleCode, string? Description, string Status,
    DateOnly? EffectiveFrom, DateOnly? EffectiveTo, int Priority, decimal? MinAmount, decimal? MaxAmount, Guid? CompanyId,
    Guid? BranchId, Guid? DepartmentId, DateTimeOffset? PublishedAt, uint RowVersion, IReadOnlyList<WorkflowStepDto> Steps);

public sealed record WorkflowDelegationInput(Guid DelegateUserId, DateOnly ValidFrom, DateOnly ValidTo, string? Reason);
public sealed record WorkflowDelegationDto(Guid Id, Guid DelegatorUserId, Guid DelegateUserId, string? DelegateName, DateOnly ValidFrom, DateOnly ValidTo, string? Reason, bool IsActive);

/// <summary>Definition admin (draft → publish → immutable) and personal out-of-office delegations.</summary>
public sealed class WorkflowDefinitionService(ITenantDbContext db, IClock clock, ICurrentUser user, WorkflowDocumentTypeRegistry registry, IPermissionService permissions,
    IWorkflowEngine engine)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    public IReadOnlyCollection<WorkflowDocumentType> DocumentTypes => registry.All;

    public async Task<IReadOnlyList<WorkflowDefinitionDto>> ListAsync(string? documentType, string? status, CancellationToken ct)
    {
        var q = db.WorkflowDefinitions.AsNoTracking().Include(d => d.Steps).AsQueryable();
        if (!string.IsNullOrWhiteSpace(documentType)) q = q.Where(d => d.DocumentType == documentType);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(d => d.Status == status);
        return (await q.OrderBy(d => d.Code).ThenByDescending(d => d.Version).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<Result<WorkflowDefinitionDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var d = await db.WorkflowDefinitions.AsNoTracking().Include(x => x.Steps).FirstOrDefaultAsync(x => x.Id == id, ct);
        return d is null ? Error.NotFound("workflow_definition.not_found", "Workflow definition not found.") : ToDto(d);
    }

    /// <summary>Creates a draft; a new version of an existing code when the code is already in use.</summary>
    public async Task<Result<WorkflowDefinitionDto>> CreateAsync(WorkflowDefinitionInput input, CancellationToken ct)
    {
        var err = Validate(input, out var docType);
        if (err is not null) return err;
        var code = input.Code.Trim();
        var family = await db.WorkflowDefinitions.AsNoTracking().Where(d => d.Code == code).Select(d => new { d.Version, d.DocumentType }).ToListAsync(ct);
        if (family.Count > 0 && family.Any(f => f.DocumentType != input.DocumentType))
            return Error.Conflict("workflow_definition.code_in_use", "This code is used by a definition for another document type.");
        var def = new WorkflowDefinition { Code = code, Version = family.Count == 0 ? 1 : family.Max(f => f.Version) + 1, ModuleCode = docType!.ModuleCode };
        Apply(def, input);
        db.WorkflowDefinitions.Add(def);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Error.Conflict("workflow_definition.version_conflict", "A concurrent version was created; retry."); }
        return ToDto(def);
    }

    public async Task<Result<WorkflowDefinitionDto>> UpdateAsync(Guid id, WorkflowDefinitionInput input, CancellationToken ct)
    {
        var def = await db.WorkflowDefinitions.Include(d => d.Steps).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null) return Error.NotFound("workflow_definition.not_found", "Workflow definition not found.");
        if (def.Status != WorkflowDefinitionStatuses.Draft)
            return Error.Conflict("workflow_definition.immutable", "Published definitions are immutable; create a new version.");
        if (input.RowVersion is { } rv && rv != def.RowVersion)
            return Error.Conflict("workflow_definition.stale", "The definition was modified by someone else.");
        var err = Validate(input, out _);
        if (err is not null) return err;
        if (input.Code.Trim() != def.Code || input.DocumentType != def.DocumentType)
            return Error.Validation("workflow_definition.identity", "Code and document type cannot change.");
        db.WorkflowDefinitionSteps.RemoveRange(def.Steps);
        def.Steps = [];
        Apply(def, input);
        db.WorkflowDefinitionSteps.AddRange(def.Steps);
        def.ModifiedAt = clock.UtcNow; // always update the row so RowVersion (xmin) advances on step-only edits
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Error.Conflict("workflow_definition.stale", "The definition was modified by someone else."); }
        return ToDto(def);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        var def = await db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null) return Error.NotFound("workflow_definition.not_found", "Workflow definition not found.");
        if (def.Status != WorkflowDefinitionStatuses.Draft) return Error.Conflict("workflow_definition.immutable", "Only drafts can be deleted.");
        db.WorkflowDefinitions.Remove(def);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Publishes a draft from <paramref name="effectiveFrom"/>, closing the previous published version of the
    /// same code the day before. Running instances stay pinned to the version they started on.</summary>
    public async Task<Result<WorkflowDefinitionDto>> PublishAsync(Guid id, DateOnly? effectiveFrom, CancellationToken ct)
    {
        var from = effectiveFrom ?? Today;
        if (from < Today) return Error.Validation("workflow_definition.backdate", "Definitions cannot be published with a past effective date.");
        var def = await db.WorkflowDefinitions.AsNoTracking().Include(d => d.Steps).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null) return Error.NotFound("workflow_definition.not_found", "Workflow definition not found.");
        if (def.Status != WorkflowDefinitionStatuses.Draft) return Error.Conflict("workflow_definition.not_draft", "Only drafts can be published.");
        if (def.Steps.Count == 0) return Error.Validation("workflow_definition.no_steps", "A definition needs at least one step.");

        var ok = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            ((DbContext)db).ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var draft = await db.WorkflowDefinitions.FirstAsync(d => d.Id == id, ct);
            if (draft.Status != WorkflowDefinitionStatuses.Draft) return "not_draft";
            var published = await db.WorkflowDefinitions
                .Where(d => d.Code == draft.Code && d.Status == WorkflowDefinitionStatuses.Published && (d.EffectiveTo == null || d.EffectiveTo >= from))
                .ToListAsync(ct);
            if (published.Any(p => p.EffectiveFrom >= from)) return "overlap";
            foreach (var p in published) p.EffectiveTo = from.AddDays(-1);
            if (published.Count > 0) await db.SaveChangesAsync(ct);
            draft.Status = WorkflowDefinitionStatuses.Published;
            draft.EffectiveFrom = from;
            draft.EffectiveTo = null;
            draft.PublishedAt = clock.UtcNow;
            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateConcurrencyException) { return "not_draft"; }
            return "ok";
        });
        return ok switch
        {
            "ok" => (await GetAsync(id, ct)),
            "overlap" => Error.Conflict("workflow_definition.overlap", "A published version already starts on or after that date."),
            _ => Error.Conflict("workflow_definition.not_draft", "The definition was published concurrently."),
        };
    }

    public async Task<Result<WorkflowDefinitionDto>> RetireAsync(Guid id, CancellationToken ct)
    {
        var def = await db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null) return Error.NotFound("workflow_definition.not_found", "Workflow definition not found.");
        if (def.Status != WorkflowDefinitionStatuses.Published) return Error.Conflict("workflow_definition.not_published", "Only published versions can be retired.");
        def.Status = WorkflowDefinitionStatuses.Retired;
        def.EffectiveTo ??= Today;
        if (def.EffectiveTo < def.EffectiveFrom) def.EffectiveTo = def.EffectiveFrom;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // ---------------- Delegations ----------------
    public async Task<IReadOnlyList<WorkflowDelegationDto>> MyDelegationsAsync(CancellationToken ct)
    {
        var me = user.UserId ?? Guid.Empty;
        return await db.WorkflowDelegations.AsNoTracking().Where(d => d.DelegatorUserId == me)
            .Join(db.Users.AsNoTracking(), d => d.DelegateUserId, u => u.Id, (d, u) => new { d, u.DisplayName })
            .OrderByDescending(x => x.d.ValidFrom)
            .Select(x => new WorkflowDelegationDto(x.d.Id, x.d.DelegatorUserId, x.d.DelegateUserId, x.DisplayName, x.d.ValidFrom, x.d.ValidTo, x.d.Reason, x.d.IsActive))
            .ToListAsync(ct);
    }

    public async Task<Result<WorkflowDelegationDto>> CreateDelegationAsync(WorkflowDelegationInput input, CancellationToken ct)
    {
        var me = user.UserId ?? Guid.Empty;
        if (input.DelegateUserId == me) return Error.Validation("workflow_delegation.self", "You cannot delegate to yourself.");
        if (input.ValidTo < input.ValidFrom || input.ValidTo < Today)
            return Error.Validation("workflow_delegation.window", "Choose a valid, non-past delegation window.");
        var target = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == input.DelegateUserId && u.Status == Domain.Common.UserStatus.Active, ct);
        if (target is null) return Error.Validation("workflow_delegation.target", "Delegate must be an active user.");
        if (!await permissions.HasAsync(input.DelegateUserId, WorkflowPermissions.WorkItemsAct, ct))
            return Error.Validation("workflow_delegation.target", "Delegate cannot act on approvals.");
        if (await db.WorkflowDelegations.AnyAsync(d => d.DelegatorUserId == me && d.IsActive && d.ValidFrom <= input.ValidTo && d.ValidTo >= input.ValidFrom, ct))
            return Error.Conflict("workflow_delegation.overlap", "You already have an active delegation in that window.");
        var d = new WorkflowDelegation { DelegatorUserId = me, DelegateUserId = input.DelegateUserId, ValidFrom = input.ValidFrom, ValidTo = input.ValidTo, Reason = input.Reason };
        db.WorkflowDelegations.Add(d);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Error.Conflict("workflow_delegation.overlap", "You already have an active delegation in that window."); }
        return new WorkflowDelegationDto(d.Id, me, d.DelegateUserId, target.DisplayName, d.ValidFrom, d.ValidTo, d.Reason, true);
    }

    public async Task<Result> RevokeDelegationAsync(Guid id, CancellationToken ct)
    {
        var me = user.UserId ?? Guid.Empty;
        var d = await db.WorkflowDelegations.FirstOrDefaultAsync(x => x.Id == id && x.DelegatorUserId == me, ct);
        if (d is null) return Error.NotFound("workflow_delegation.not_found", "Delegation not found.");
        d.IsActive = false;
        await db.SaveChangesAsync(ct);
        await engine.ReturnEndedDelegationsAsync(ct);
        return Result.Success();
    }

    // ---------------- Helpers ----------------
    private Error? Validate(WorkflowDefinitionInput input, out WorkflowDocumentType? docType)
    {
        docType = registry.Find(input.DocumentType ?? string.Empty);
        if (docType is null) return Error.Validation("workflow_definition.document_type", $"Unknown document type '{input.DocumentType}'.");
        if (string.IsNullOrWhiteSpace(input.Code) || input.Code.Trim().Length > 64 || string.IsNullOrWhiteSpace(input.Name))
            return Error.Validation("workflow_definition.invalid", "Code (≤64 chars) and name are required.");
        if (input.MinAmount is { } mn && input.MaxAmount is { } mx && mx < mn)
            return Error.Validation("workflow_definition.amounts", "Max amount must be ≥ min amount.");
        if (input.Steps is null || input.Steps.Count == 0) return Error.Validation("workflow_definition.no_steps", "At least one step is required.");
        if (input.Steps.Count > 50) return Error.Validation("workflow_definition.too_many_steps", "At most 50 steps are allowed.");
        foreach (var s in input.Steps)
        {
            if (s.StepOrder < 1 || string.IsNullOrWhiteSpace(s.Name))
                return Error.Validation("workflow_definition.step", "Each step needs an order ≥ 1 and a name.");
            if (!WorkflowApproverTypes.All.Contains(s.ApproverType))
                return Error.Validation("workflow_definition.approver_type", $"Unknown approver type '{s.ApproverType}'.");
            if (s.ApprovalMode is not ("any" or "all"))
                return Error.Validation("workflow_definition.approval_mode", "Approval mode must be 'any' or 'all'.");
            if (s.ApproverType == WorkflowApproverTypes.DepartmentField && !WorkflowEngine.DepartmentFields.Contains(s.ApproverParam))
                return Error.Validation("workflow_definition.department_field", $"Unknown department field '{s.ApproverParam}'.");
            if (s.ApproverType is WorkflowApproverTypes.Role && string.IsNullOrWhiteSpace(s.ApproverParam))
                return Error.Validation("workflow_definition.role", "Role steps need a role code.");
            if (s.ApproverType is WorkflowApproverTypes.User && !Guid.TryParse(s.ApproverParam, out _))
                return Error.Validation("workflow_definition.user", "User steps need a user id.");
            var scope = s.ApproverScope ?? WorkflowApproverScopes.Tenant;
            if (!WorkflowApproverScopes.All.Contains(scope))
                return Error.Validation("workflow_definition.approver_scope", $"Unknown approver scope '{scope}'.");
            if (scope != WorkflowApproverScopes.Tenant && s.ApproverType != WorkflowApproverTypes.Role)
                return Error.Validation("workflow_definition.approver_scope", "Approver scope applies to role steps only.");
            if (!WorkflowEscalationTypes.All.Contains(s.EscalationType))
                return Error.Validation("workflow_definition.escalation", $"Unknown escalation type '{s.EscalationType}'.");
            if (s.SlaHours is <= 0) return Error.Validation("workflow_definition.sla", "SLA hours must be positive.");
            if (s.MinAmount is { } a && s.MaxAmount is { } b && b < a)
                return Error.Validation("workflow_definition.amounts", "Step max amount must be ≥ min amount.");
        }
        return null;
    }

    private static void Apply(WorkflowDefinition def, WorkflowDefinitionInput input)
    {
        def.Name = input.Name.Trim();
        def.DocumentType = input.DocumentType;
        def.Description = input.Description;
        def.Priority = input.Priority;
        def.MinAmount = input.MinAmount;
        def.MaxAmount = input.MaxAmount;
        def.CompanyId = input.CompanyId;
        def.BranchId = input.BranchId;
        def.DepartmentId = input.DepartmentId;
        foreach (var s in input.Steps.OrderBy(s => s.StepOrder))
            def.Steps.Add(new WorkflowDefinitionStep
            {
                DefinitionId = def.Id,
                StepOrder = s.StepOrder,
                Name = s.Name.Trim(),
                ApproverType = s.ApproverType,
                ApproverParam = s.ApproverParam,
                ApproverScope = s.ApproverScope ?? WorkflowApproverScopes.Tenant,
                ApprovalMode = s.ApprovalMode,
                MinAmount = s.MinAmount,
                MaxAmount = s.MaxAmount,
                FallbackUserId = s.FallbackUserId,
                SlaHours = s.SlaHours,
                EscalationType = s.EscalationType,
                EscalationParam = s.EscalationParam,
                AllowSendBack = s.AllowSendBack,
                AllowDelegate = s.AllowDelegate,
            });
    }

    private static WorkflowDefinitionDto ToDto(WorkflowDefinition d) => new(
        d.Id, d.Code, d.Version, d.Name, d.DocumentType, d.ModuleCode, d.Description, d.Status, d.EffectiveFrom, d.EffectiveTo, d.Priority,
        d.MinAmount, d.MaxAmount, d.CompanyId, d.BranchId, d.DepartmentId, d.PublishedAt, d.RowVersion,
        d.Steps.OrderBy(s => s.StepOrder).ThenBy(s => s.Name).Select(s => new WorkflowStepDto(s.Id, s.StepOrder, s.Name, s.ApproverType,
            s.ApproverParam, s.ApprovalMode, s.MinAmount, s.MaxAmount, s.FallbackUserId, s.SlaHours, s.EscalationType, s.EscalationParam,
            s.AllowSendBack, s.AllowDelegate, s.ApproverScope)).ToList());
}
