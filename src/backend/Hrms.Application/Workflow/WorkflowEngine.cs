using System.Text.Json;
using Hrms.Application.Abstractions;
using Hrms.Application.Auditing;
using Hrms.Application.Authorization;
using Hrms.Application.Identity;
using Hrms.Application.Jobs;
using Hrms.Application.Modules;
using Hrms.Application.Notifications;
using Hrms.Domain.Common;
using Hrms.Domain.Tenant;
using Hrms.Shared.Paging;
using Hrms.Shared.Primitives;
using Hrms.Shared.Results;
using Hrms.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Workflow;

public static class WorkflowJobTypes
{
    public const string OutboxDispatch = "workflow.outbox.dispatch";
    public const string SlaTenant = "workflow.sla.tenant";
    public const string SlaSweep = "workflow.sla.sweep";
}

public static class WorkItemTypes
{
    public const string Approval = "workflow.approval";
    public const string Resubmit = "workflow.resubmit";
}

/// <summary>Registry of all <see cref="WorkflowDocumentType"/>s contributed by providers.</summary>
public sealed class WorkflowDocumentTypeRegistry(IEnumerable<IWorkflowDocumentTypeProvider> providers)
{
    private readonly Dictionary<string, WorkflowDocumentType> types = providers
        .SelectMany(p => p.DocumentTypes)
        .GroupBy(t => t.Code, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    public IReadOnlyCollection<WorkflowDocumentType> All => types.Values;
    public WorkflowDocumentType? Find(string code) => types.GetValueOrDefault(code);
}

public sealed class WorkflowEngine(
    ITenantDbContext db,
    ICurrentUser user,
    IClock clock,
    IAuditWriter audit,
    INotificationService notifications,
    IJobScheduler jobs,
    ITenantContextAccessor tenant,
    IAccessGuard access,
    IDataScopeService scopes,
    IEntitlementService entitlements,
    WorkflowDocumentTypeRegistry registry,
    IWorkItemPublisher workItems,
    IPermissionService permissions) : IWorkflowEngine
{
    private readonly List<NotifyCommand> pendingNotifications = [];
    private bool outboxWritten;
    private DbContext Ctx => (DbContext)db;
    private DateTimeOffset Now => clock.UtcNow;
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    // ---------------- Start ----------------
    public async Task<Result<WorkflowInstanceDto>> StartAsync(StartWorkflowRequest request, CancellationToken ct)
    {
        if (user.UserId is not { } me) return Error.Unauthorized("workflow.unauthenticated", "Sign in required.");
        if (string.IsNullOrWhiteSpace(request.DocumentId) || string.IsNullOrWhiteSpace(request.Title))
            return Error.Validation("workflow.invalid_request", "Document id and title are required.");
        var docType = registry.Find(request.DocumentType);
        if (docType is null) return Error.Validation("workflow.unknown_document_type", $"Unknown document type '{request.DocumentType}'.");
        var ent = await entitlements.GetAsync(tenant.Required.TenantId, ct);
        if (!ent.IsModuleEnabled(docType.ModuleCode))
            return Error.Forbidden("workflow.module_disabled", "The owning module is not enabled for this tenant.");

        Employee? subject = null;
        if (request.SubjectEmployeeId is { } sid)
        {
            subject = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == sid, ct);
            if (subject is null) return Error.NotFound("workflow.subject_not_found", "Subject employee not found.");
        }

        var definition = await SelectDefinitionAsync(request.DocumentType, request.Amount, subject, ct);
        if (definition is null)
            return Error.Rule("workflow.no_definition", $"No published workflow definition matches '{request.DocumentType}'.");

        Guid instanceId = Guid.Empty;
        var result = await RunAsync(async () =>
        {
            if (await db.WorkflowInstances.AnyAsync(i => i.DocumentType == request.DocumentType && i.DocumentId == request.DocumentId
                    && (i.Status == WorkflowInstanceStatuses.InProgress || i.Status == WorkflowInstanceStatuses.SentBack
                        || i.Status == WorkflowInstanceStatuses.Stalled), ct))
                return Error.Conflict("workflow.already_active", "This document already has an active workflow.");

            var instance = new WorkflowInstance
            {
                DefinitionId = definition.Id,
                DocumentType = request.DocumentType,
                DocumentId = request.DocumentId,
                ModuleCode = docType.ModuleCode,
                Title = request.Title.Trim(),
                DeepLink = docType.DeepLinkTemplate.Replace("{id}", Uri.EscapeDataString(request.DocumentId), StringComparison.Ordinal),
                RequesterUserId = me,
                SubjectEmployeeId = request.SubjectEmployeeId,
                Amount = request.Amount,
                ContextJson = request.Context is { Count: > 0 } ? JsonSerializer.Serialize(request.Context) : null,
                Status = WorkflowInstanceStatuses.InProgress,
                StartedAt = Now,
                LastActionAt = Now,
            };
            if (instance.DeepLink.Contains("{id}", StringComparison.Ordinal) || docType.Code == BuiltInWorkflowDocumentTypes.GenericRequest)
                instance.DeepLink = $"/my-work?instance={instance.Id}";
            db.WorkflowInstances.Add(instance);
            instanceId = instance.Id;

            var steps = definition.Steps.OrderBy(s => s.StepOrder).ToList();

            Log(instance, null, WorkflowActions.Start, me, null, null, null);
            Audit(instance, WorkflowActions.Start, new { definition.Code, definition.Version });
            AddOutbox(instance, "workflow.instance.started");
            var tasks = new List<WorkflowInstanceStep>();
            if (await ActivateStageAsync(instance, steps, tasks, 1, ct) == StageOutcome.Completed)
                return Error.Rule("workflow.no_applicable_step", "The workflow has no applicable step for this document.");
            return Result.Success();
        }, ct);
        return result.IsSuccess ? await LoadDtoAsync(instanceId, ct) : result.Error!;
    }

    private async Task<WorkflowDefinition?> SelectDefinitionAsync(string documentType, decimal? amount, Employee? subject, CancellationToken ct)
    {
        var today = Today;
        var candidates = await db.WorkflowDefinitions.AsNoTracking().Include(d => d.Steps)
            .Where(d => d.DocumentType == documentType && d.Status == WorkflowDefinitionStatuses.Published
                        && d.EffectiveFrom <= today && (d.EffectiveTo == null || d.EffectiveTo >= today))
            .ToListAsync(ct);
        return candidates
            .Where(d => (d.MinAmount is null || (amount ?? 0) >= d.MinAmount)
                        && (d.MaxAmount is null || (amount ?? 0) <= d.MaxAmount)
                        && (d.CompanyId is null || d.CompanyId == subject?.CompanyId)
                        && (d.BranchId is null || d.BranchId == subject?.BranchId)
                        && (d.DepartmentId is null || d.DepartmentId == subject?.DepartmentId))
            .OrderByDescending(Specificity).ThenByDescending(d => d.Priority).ThenByDescending(d => d.PublishedAt)
            .FirstOrDefault();
    }

    private static int Specificity(WorkflowDefinition d) =>
        (d.MinAmount is null ? 0 : 1) + (d.MaxAmount is null ? 0 : 1) + (d.CompanyId is null ? 0 : 1)
        + (d.BranchId is null ? 0 : 2) + (d.DepartmentId is null ? 0 : 3);

    private static bool ThresholdApplies(WorkflowDefinitionStep s, decimal? amount) =>
        (s.MinAmount is null || (amount is { } a && a >= s.MinAmount)) && (s.MaxAmount is null || (amount ?? 0) <= s.MaxAmount);

    // ---------------- Stage activation ----------------
    private enum StageOutcome { Activated, Stalled, Completed }

    /// <summary>Activates the first stage at or after <paramref name="fromOrder"/> that has an applicable step. If any
    /// applicable step of that stage resolves no approver the instance becomes <c>stalled</c> (no step is skipped);
    /// <paramref name="overrideAssignee"/> (admin resume) fills steps that still resolve nobody.</summary>
    private async Task<StageOutcome> ActivateStageAsync(WorkflowInstance instance, List<WorkflowDefinitionStep> steps, List<WorkflowInstanceStep> tasks,
        int fromOrder, CancellationToken ct, Guid? overrideAssignee = null)
    {
        foreach (var order in steps.Select(s => s.StepOrder).Where(o => o >= fromOrder).Distinct().Order())
        {
            var resolved = new List<(WorkflowDefinitionStep Step, List<Guid> Approvers)>();
            foreach (var s in steps.Where(s => s.StepOrder == order))
            {
                if (!ThresholdApplies(s, instance.Amount))
                {
                    Log(instance, null, WorkflowActions.Skipped, null, null, $"Step '{s.Name}' not applicable for amount.", null);
                    continue;
                }
                var approvers = await ResolveApproversAsync(s, instance, ct);
                if (approvers.Count == 0 && overrideAssignee is { } manual) approvers = [manual];
                resolved.Add((s, approvers));
            }
            if (resolved.Count == 0) continue;

            var missing = resolved.Where(r => r.Approvers.Count == 0).Select(r => r.Step.Name).ToList();
            if (missing.Count > 0)
            {
                await StallAsync(instance, order, missing, ct);
                return StageOutcome.Stalled;
            }

            foreach (var (s, approvers) in resolved)
            {
                foreach (var approver in approvers)
                {
                    var (assignee, from, delegationId) = await ApplyStandingDelegationAsync(approver, instance.RequesterUserId, ct);
                    var task = new WorkflowInstanceStep
                    {
                        InstanceId = instance.Id,
                        DefinitionStepId = s.Id,
                        StepOrder = order,
                        Name = s.Name,
                        ApprovalMode = s.ApprovalMode,
                        AssigneeUserId = assignee,
                        DelegatedFromUserId = from,
                        DelegationId = delegationId,
                        DueAt = s.SlaHours is { } h ? Now.AddHours(h) : null,
                    };
                    db.WorkflowInstanceSteps.Add(task);
                    tasks.Add(task);
                    await PublishTaskItemAsync(instance, task, ct);
                    Log(instance, task.Id, WorkflowActions.Assigned, null, assignee, null, null);
                    Notify(assignee, "workflow.task_assigned", $"Approval needed: {instance.Title}", "/my-work");
                }
            }
            instance.CurrentStepOrder = order;
            return StageOutcome.Activated;
        }
        return StageOutcome.Completed;
    }

    private async Task StallAsync(WorkflowInstance instance, int order, List<string> stepNames, CancellationToken ct)
    {
        instance.Status = WorkflowInstanceStatuses.Stalled;
        instance.CurrentStepOrder = order;
        var reason = $"No approver could be resolved for: {string.Join(", ", stepNames)}.";
        Log(instance, null, WorkflowActions.Stalled, null, null, reason, null);
        Audit(instance, WorkflowActions.Stalled, new { stage = order, steps = stepNames });
        AddOutbox(instance, "workflow.instance.stalled");
        foreach (var admin in await WorkflowAdministratorsAsync(ct))
            Notify(admin, "workflow.instance_stalled", $"Workflow stalled (no approver): {instance.Title}", "/workflow/definitions?tab=stalled");
    }

    /// <summary>Tenant admins and holders of <c>workflow.instances.manage</c> — the people who can resume a stalled request.</summary>
    private async Task<List<Guid>> WorkflowAdministratorsAsync(CancellationToken ct)
    {
        // Tenant-wide managers only: scoped managers see in-scope stalled requests in their list, without out-of-scope alerts.
        var managers = WorkflowEligibility.UsersWithPermission(db, WorkflowPermissions.InstancesManage, Today, tenantWideOnly: true);
        return await db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active && managers.Contains(u.Id))
            .Select(u => u.Id).ToListAsync(ct);
    }

    private async Task<(Guid Assignee, Guid? From, Guid? DelegationId)> ApplyStandingDelegationAsync(Guid approver, Guid requester, CancellationToken ct)
    {
        var today = Today;
        var delegation = await db.WorkflowDelegations.AsNoTracking()
            .Where(d => d.DelegatorUserId == approver && d.IsActive && d.ValidFrom <= today && d.ValidTo >= today)
            .Select(d => new { d.Id, d.DelegateUserId }).FirstOrDefaultAsync(ct);
        if (delegation is { } del && del.DelegateUserId != requester && await IsActiveUserAsync(del.DelegateUserId, ct)
            && await HoldsActPermissionAsync(del.DelegateUserId, today, ct))
            return (del.DelegateUserId, approver, del.Id);
        return (approver, null, null);
    }

    private Task<bool> DelegationEndedAsync(Guid delegationId, CancellationToken ct)
    {
        var today = Today;
        return db.WorkflowDelegations.AsNoTracking()
            .AnyAsync(d => d.Id == delegationId && (!d.IsActive || d.ValidFrom > today || d.ValidTo < today), ct);
    }

    // Direct role query: IPermissionService may hit HybridCache, which cannot run inside the retrying transaction.
    private Task<bool> HoldsActPermissionAsync(Guid userId, DateOnly today, CancellationToken ct) =>
        WorkflowEligibility.UsersWhoCanAct(db, today).AnyAsync(u => u == userId, ct);

    // ---------------- Approver resolution ----------------
    private async Task<List<Guid>> ResolveApproversAsync(WorkflowDefinitionStep s, WorkflowInstance instance, CancellationToken ct)
    {
        var users = new List<Guid>();
        switch (s.ApproverType)
        {
            case WorkflowApproverTypes.ReportingManager when instance.SubjectEmployeeId is { } emp:
                var level = int.TryParse(s.ApproverParam, out var l) && l > 0 ? l : 1;
                var managerId = await db.ReportingClosure.AsNoTracking()
                    .Where(c => c.DescendantId == emp && c.Depth == level).Select(c => (Guid?)c.AncestorId).FirstOrDefaultAsync(ct);
                if (managerId is null && level == 1)
                    managerId = await db.Employees.AsNoTracking().Where(e => e.Id == emp).Select(e => e.ManagerEmployeeId).FirstOrDefaultAsync(ct);
                await AddEmployeeUserAsync(users, managerId, ct);
                break;
            case WorkflowApproverTypes.DepartmentHead or WorkflowApproverTypes.DepartmentField when instance.SubjectEmployeeId is { } emp:
                var dept = await db.Employees.AsNoTracking().Where(e => e.Id == emp && e.DepartmentId != null)
                    .Join(db.Departments.AsNoTracking(), e => e.DepartmentId, d => d.Id, (e, d) => d).FirstOrDefaultAsync(ct);
                if (dept is not null)
                {
                    var target = s.ApproverType == WorkflowApproverTypes.DepartmentHead
                        ? dept.HodEmployeeId ?? dept.AltHodEmployeeId
                        : DepartmentField(dept, s.ApproverParam);
                    if (target != emp) await AddEmployeeUserAsync(users, target, ct);
                }
                break;
            case WorkflowApproverTypes.Role when !string.IsNullOrWhiteSpace(s.ApproverParam):
                var today = Today;
                var holders = await db.RoleAssignments.AsNoTracking()
                    .Where(a => a.Role!.Code == s.ApproverParam && a.ValidFrom <= today && (a.ValidTo == null || a.ValidTo >= today)
                                && db.Users.Any(u => u.Id == a.UserId && u.Status == UserStatus.Active))
                    .Select(a => new RoleHolder(a.UserId, a.ScopeTargets.Select(t => new ScopeTarget(t.Kind, t.TargetId)).ToList()))
                    .ToListAsync(ct);
                users.AddRange(await RoleHoldersInScopeAsync(holders, s.ApproverScope, instance, ct));
                break;
            case WorkflowApproverTypes.User when Guid.TryParse(s.ApproverParam, out var uid):
                if (await IsActiveUserAsync(uid, ct)) users.Add(uid);
                break;
        }
        users = users.Distinct().Where(u => u != instance.RequesterUserId).ToList(); // no self-approval
        if (users.Count == 0 && s.FallbackUserId is { } fb && fb != instance.RequesterUserId && await IsActiveUserAsync(fb, ct))
            users.Add(fb);
        return users;
    }

    private sealed record ScopeTarget(ScopeTargetKind Kind, Guid Id);
    private sealed record RoleHolder(Guid UserId, List<ScopeTarget> Targets);

    /// <summary>Keeps role holders whose role assignment covers the subject employee's company/branch/department
    /// (assignment targets of that kind, or — when the assignment has none of that kind — the holder's own employee
    /// record), or who are in the subject's reporting line. Tenant scope keeps every holder. The requester's employee
    /// record is the subject when the request has none.</summary>
    private async Task<List<Guid>> RoleHoldersInScopeAsync(List<RoleHolder> holders, string scope, WorkflowInstance instance, CancellationToken ct)
    {
        var userIds = holders.Select(h => h.UserId).Distinct().ToList();
        if (scope == WorkflowApproverScopes.Tenant || userIds.Count == 0) return userIds;
        var subjectId = instance.SubjectEmployeeId
                        ?? await db.Users.AsNoTracking().Where(u => u.Id == instance.RequesterUserId).Select(u => u.EmployeeId).FirstOrDefaultAsync(ct);
        var subject = subjectId is null ? null : await db.Employees.AsNoTracking().Where(e => e.Id == subjectId)
            .Select(e => new { e.Id, e.CompanyId, e.BranchId, e.DepartmentId, e.ManagerEmployeeId }).FirstOrDefaultAsync(ct);
        if (subject is null) return [];
        var homes = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id) && u.EmployeeId != null)
            .Join(db.Employees.AsNoTracking(), u => u.EmployeeId, e => e.Id,
                (u, e) => new { UserId = u.Id, EmployeeId = e.Id, e.CompanyId, e.BranchId, e.DepartmentId })
            .ToListAsync(ct);

        if (scope == WorkflowApproverScopes.Reporting)
        {
            var line = await db.ReportingClosure.AsNoTracking().Where(c => c.DescendantId == subject.Id && c.Depth > 0)
                .Select(c => c.AncestorId).ToListAsync(ct);
            if (subject.ManagerEmployeeId is { } mgr) line.Add(mgr);
            return homes.Where(h => line.Contains(h.EmployeeId)).Select(h => h.UserId).Distinct().ToList();
        }

        var (kind, value) = scope switch
        {
            WorkflowApproverScopes.Company => (ScopeTargetKind.Company, (Guid?)subject.CompanyId),
            WorkflowApproverScopes.Branch => (ScopeTargetKind.Branch, (Guid?)subject.BranchId),
            _ => (ScopeTargetKind.Department, subject.DepartmentId),
        };
        if (value is null) return [];
        Guid? HomeValue(Guid userId) => homes.Where(h => h.UserId == userId).Select(h => kind switch
        {
            ScopeTargetKind.Company => (Guid?)h.CompanyId,
            ScopeTargetKind.Branch => h.BranchId,
            _ => h.DepartmentId,
        }).FirstOrDefault();
        return holders.Where(h => h.Targets.Any(t => t.Kind == kind)
                ? h.Targets.Any(t => t.Kind == kind && t.Id == value)
                : HomeValue(h.UserId) == value)
            .Select(h => h.UserId).Distinct().ToList();
    }

    public static readonly string[] DepartmentFields =
        ["hod", "alt_hod", "reporting_manager", "leave_approver", "alt_leave_approver", "od_approver", "comp_off_approver", "ot_approver", "regularization_approver"];

    private static Guid? DepartmentField(Department d, string? field) => field switch
    {
        "hod" => d.HodEmployeeId,
        "alt_hod" => d.AltHodEmployeeId,
        "reporting_manager" => d.ReportingManagerEmployeeId,
        "leave_approver" => d.LeaveApproverEmployeeId,
        "alt_leave_approver" => d.AltLeaveApproverEmployeeId,
        "od_approver" => d.OdApproverEmployeeId,
        "comp_off_approver" => d.CompOffApproverEmployeeId,
        "ot_approver" => d.OtApproverEmployeeId,
        "regularization_approver" => d.RegularizationApproverEmployeeId,
        _ => null,
    };

    private async Task AddEmployeeUserAsync(List<Guid> users, Guid? employeeId, CancellationToken ct)
    {
        if (employeeId is null) return;
        var uid = await db.Employees.AsNoTracking().Where(e => e.Id == employeeId).Select(e => e.UserId).FirstOrDefaultAsync(ct);
        if (uid is { } u && await IsActiveUserAsync(u, ct)) users.Add(u);
    }

    private Task<bool> IsActiveUserAsync(Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.Status == UserStatus.Active, ct);

    private async Task<Guid?> ResolveEscalationTargetAsync(WorkflowDefinitionStep s, WorkflowInstanceStep task, Guid requester, CancellationToken ct)
    {
        var candidates = new List<Guid>();
        switch (s.EscalationType)
        {
            case WorkflowEscalationTypes.AssigneeManager:
                var empId = await db.Users.AsNoTracking().Where(u => u.Id == task.AssigneeUserId).Select(u => u.EmployeeId).FirstOrDefaultAsync(ct);
                if (empId is not null)
                {
                    var mgr = await db.Employees.AsNoTracking().Where(e => e.Id == empId).Select(e => e.ManagerEmployeeId).FirstOrDefaultAsync(ct);
                    await AddEmployeeUserAsync(candidates, mgr, ct);
                }
                break;
            case WorkflowEscalationTypes.Role when !string.IsNullOrWhiteSpace(s.EscalationParam):
                var probe = new WorkflowDefinitionStep { ApproverType = WorkflowApproverTypes.Role, ApproverParam = s.EscalationParam };
                candidates.AddRange(await ResolveApproversAsync(probe, new WorkflowInstance { RequesterUserId = requester }, ct));
                break;
            case WorkflowEscalationTypes.User when Guid.TryParse(s.EscalationParam, out var uid):
                if (await IsActiveUserAsync(uid, ct)) candidates.Add(uid);
                break;
        }
        return candidates.Where(c => c != requester && c != task.AssigneeUserId).Select(c => (Guid?)c).FirstOrDefault();
    }

    // ---------------- Actions ----------------
    public async Task<Result<WorkflowInstanceDto>> ActAsync(Guid taskId, WorkflowTaskAction action, CancellationToken ct)
    {
        if (user.UserId is not { } me) return Error.Unauthorized("workflow.unauthenticated", "Sign in required.");
        if (!WorkflowActions.TaskActions.Contains(action.Action))
            return Error.Validation("workflow.unknown_action", $"Unknown action '{action.Action}'.");
        if (action.Action is WorkflowActions.Reject or WorkflowActions.SendBack or WorkflowActions.Comment && string.IsNullOrWhiteSpace(action.Comment))
            return Error.Validation("workflow.comment_required", "A comment is required for this action.");
        var canManage = action.Action == WorkflowActions.Escalate
                        && await access.CheckAsync(WorkflowModule.ModuleCode, WorkflowPermissions.InstancesManage, ct) is null;

        // Resolved before the transaction: the permission cache runs its factory outside the execution strategy.
        var targetCanAct = action.Action == WorkflowActions.Delegate && action.TargetUserId is { } delegateTo
                           && await permissions.HasAsync(delegateTo, WorkflowPermissions.WorkItemsAct, ct)
                           && await permissions.HasAsync(delegateTo, WorkflowPermissions.WorkItemsView, ct);

        Guid instanceId = Guid.Empty;
        var result = await RunAsync(async () =>
        {
            var task = await db.WorkflowInstanceSteps.FirstOrDefaultAsync(t => t.Id == taskId, ct);
            if (task is null) return Error.NotFound("workflow.task_not_found", "Task not found.");
            instanceId = task.InstanceId;
            if (task.AssigneeUserId != me && !canManage) return Error.NotFound("workflow.task_not_found", "Task not found.");
            var instance = await db.WorkflowInstances.FirstAsync(i => i.Id == task.InstanceId, ct);
            if (await IsReplayAsync(instance.Id, action.IdempotencyKey, ct)) return Result.Success();
            if (task.Status != WorkflowTaskStatuses.Pending || instance.Status != WorkflowInstanceStatuses.InProgress)
                return Error.Conflict("workflow.task_closed", "This task is no longer pending.");
            var tasks = await db.WorkflowInstanceSteps.Where(t => t.InstanceId == instance.Id).ToListAsync(ct);
            task = tasks.First(t => t.Id == taskId);
            if (task.DelegationId is { } via && task.AssigneeUserId == me && await DelegationEndedAsync(via, ct))
                return Error.Conflict("workflow.delegation_ended", "The delegation that routed this task to you has expired or been revoked.");
            if (task.ClaimedByUserId is { } claimer && claimer != me && action.Action != WorkflowActions.Escalate)
                return Error.Conflict("workflow.task_claimed", "Another approver has claimed this task.");
            var defStep = await db.WorkflowDefinitionSteps.AsNoTracking().FirstAsync(s => s.Id == task.DefinitionStepId, ct);
            instance.LastActionAt = Now;

            switch (action.Action)
            {
                case WorkflowActions.Claim:
                    var group = task.ApprovalMode == "all" ? [task]
                        : tasks.Where(t => t.DefinitionStepId == task.DefinitionStepId && t.StepOrder == task.StepOrder && t.Status == WorkflowTaskStatuses.Pending).ToList();
                    foreach (var g in group) g.ClaimedByUserId = me;
                    Log(instance, task.Id, WorkflowActions.Claim, me, null, action.Comment, action.IdempotencyKey);
                    Audit(instance, WorkflowActions.Claim, new { taskId });
                    break;
                case WorkflowActions.Comment:
                    Log(instance, task.Id, WorkflowActions.Comment, me, null, action.Comment, action.IdempotencyKey);
                    Notify(instance.RequesterUserId, "workflow.comment", $"New comment on: {instance.Title}", instance.DeepLink);
                    break;
                case WorkflowActions.Approve:
                    Close(task, WorkflowTaskStatuses.Approved, me, action.Comment);
                    await workItems.CloseAsync(WorkItemTypes.Approval, task.Id.ToString(), false, ct);
                    Log(instance, task.Id, WorkflowActions.Approve, me, null, action.Comment, action.IdempotencyKey);
                    Audit(instance, WorkflowActions.Approve, new { taskId, step = task.Name });
                    if (task.ApprovalMode == "any")
                        foreach (var sib in tasks.Where(t => t.Id != task.Id && t.DefinitionStepId == task.DefinitionStepId && t.StepOrder == task.StepOrder && t.Status == WorkflowTaskStatuses.Pending))
                        {
                            Close(sib, WorkflowTaskStatuses.Superseded, null, null);
                            await workItems.CloseAsync(WorkItemTypes.Approval, sib.Id.ToString(), true, ct);
                        }
                    if (StageComplete(tasks, task))
                    {
                        var steps = await LoadStepsAsync(instance.DefinitionId, ct);
                        if (await ActivateStageAsync(instance, steps, tasks, task.StepOrder + 1, ct) == StageOutcome.Completed)
                            Finish(instance, WorkflowInstanceStatuses.Approved, "workflow.instance.approved", "approved");
                    }
                    break;
                case WorkflowActions.Reject:
                    Close(task, WorkflowTaskStatuses.Rejected, me, action.Comment);
                    await workItems.CloseAsync(WorkItemTypes.Approval, task.Id.ToString(), false, ct);
                    await CancelPendingAsync(tasks, ct);
                    Log(instance, task.Id, WorkflowActions.Reject, me, null, action.Comment, action.IdempotencyKey);
                    Audit(instance, WorkflowActions.Reject, new { taskId, step = task.Name });
                    Finish(instance, WorkflowInstanceStatuses.Rejected, "workflow.instance.rejected", "rejected");
                    break;
                case WorkflowActions.SendBack:
                    if (!defStep.AllowSendBack) return Error.Rule("workflow.send_back_not_allowed", "Send-back is not allowed at this step.");
                    Close(task, WorkflowTaskStatuses.SentBack, me, action.Comment);
                    await workItems.CloseAsync(WorkItemTypes.Approval, task.Id.ToString(), false, ct);
                    await CancelPendingAsync(tasks, ct);
                    Log(instance, task.Id, WorkflowActions.SendBack, me, null, action.Comment, action.IdempotencyKey);
                    Audit(instance, WorkflowActions.SendBack, new { taskId, to = action.SendBackTo ?? "requester" });
                    var previous = tasks.Where(t => t.StepOrder < task.StepOrder).Select(t => t.StepOrder).DefaultIfEmpty(0).Max();
                    if (action.SendBackTo == "previous" && previous > 0)
                    {
                        var steps = await LoadStepsAsync(instance.DefinitionId, ct);
                        await ActivateStageAsync(instance, steps, tasks, previous, ct);
                    }
                    else
                    {
                        instance.Status = WorkflowInstanceStatuses.SentBack;
                        AddOutbox(instance, "workflow.instance.sent_back");
                        await workItems.PublishAsync(new PublishWorkItem(instance.ModuleCode, WorkItemTypes.Resubmit, instance.Id.ToString(),
                            instance.RequesterUserId, $"Returned for changes: {instance.Title}", null, "high", instance.DeepLink), ct);
                        await TagInstanceItemAsync(instance, WorkItemTypes.Resubmit, instance.Id.ToString(), ct);
                        Notify(instance.RequesterUserId, "workflow.sent_back", $"Returned for changes: {instance.Title}", instance.DeepLink);
                    }
                    break;
                case WorkflowActions.Delegate:
                    if (!defStep.AllowDelegate) return Error.Rule("workflow.delegate_not_allowed", "Delegation is not allowed at this step.");
                    if (action.TargetUserId is not { } target || target == me)
                        return Error.Validation("workflow.delegate_target", "Choose another user to delegate to.");
                    if (target == instance.RequesterUserId)
                        return Error.Rule("workflow.self_approval", "A request cannot be delegated to its requester.");
                    if (!await IsActiveUserAsync(target, ct)) return Error.Validation("workflow.delegate_target", "Delegate must be an active user.");
                    if (!targetCanAct)
                        return Error.Validation("workflow.delegate_target", "Delegate cannot act on approvals.");
                    if (tasks.Any(t => t.Status == WorkflowTaskStatuses.Pending && t.AssigneeUserId == target && t.DefinitionStepId == task.DefinitionStepId && t.StepOrder == task.StepOrder))
                        return Error.Conflict("workflow.delegate_duplicate", "That user already holds this step.");
                    await ReassignAsync(instance, task, target, ct);
                    Log(instance, task.Id, WorkflowActions.Delegate, me, target, action.Comment, action.IdempotencyKey);
                    Audit(instance, WorkflowActions.Delegate, new { taskId, from = me, to = target });
                    break;
                case WorkflowActions.Escalate:
                    var escalateTo = await ResolveEscalationTargetAsync(defStep, task, instance.RequesterUserId, ct);
                    if (escalateTo is null) return Error.Rule("workflow.no_escalation_target", "No escalation target could be resolved.");
                    var fromUser = task.AssigneeUserId;
                    task.DueAt = defStep.SlaHours is { } h ? Now.AddHours(h) : null;
                    await ReassignAsync(instance, task, escalateTo.Value, ct);
                    task.EscalationLevel++;
                    task.ReminderSentAt = null;
                    Log(instance, task.Id, WorkflowActions.Escalate, me, escalateTo, action.Comment, action.IdempotencyKey);
                    Audit(instance, WorkflowActions.Escalate, new { taskId, from = fromUser, to = escalateTo, auto = false });
                    break;
            }
            return Result.Success();
        }, ct);
        return result.IsSuccess ? await LoadDtoAsync(instanceId, ct) : result.Error!;
    }

    public async Task<Result<WorkflowInstanceDto>> CancelAsync(Guid instanceId, string? reason, string? idempotencyKey, CancellationToken ct)
    {
        if (user.UserId is not { } me) return Error.Unauthorized("workflow.unauthenticated", "Sign in required.");
        var canManage = await access.CheckAsync(WorkflowModule.ModuleCode, WorkflowPermissions.InstancesManage, ct) is null;
        var result = await RunAsync(async () =>
        {
            var instance = await db.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == instanceId, ct);
            if (instance is null || (instance.RequesterUserId != me && !canManage))
                return Error.NotFound("workflow.instance_not_found", "Workflow instance not found.");
            if (await IsReplayAsync(instance.Id, idempotencyKey, ct)) return Result.Success();
            if (instance.Status is not (WorkflowInstanceStatuses.InProgress or WorkflowInstanceStatuses.SentBack or WorkflowInstanceStatuses.Stalled))
                return Error.Conflict("workflow.instance_closed", "Only active workflows can be cancelled.");
            var tasks = await db.WorkflowInstanceSteps.Where(t => t.InstanceId == instance.Id).ToListAsync(ct);
            await CancelPendingAsync(tasks, ct);
            await workItems.CloseAsync(WorkItemTypes.Resubmit, instance.Id.ToString(), true, ct);
            instance.LastActionAt = Now;
            Log(instance, null, WorkflowActions.Cancel, me, null, reason, idempotencyKey);
            Audit(instance, WorkflowActions.Cancel, new { reason });
            Finish(instance, WorkflowInstanceStatuses.Cancelled, "workflow.instance.cancelled", "cancelled", notifyRequester: instance.RequesterUserId != me);
            return Result.Success();
        }, ct);
        return result.IsSuccess ? await LoadDtoAsync(instanceId, ct) : result.Error!;
    }

    public async Task<Result<WorkflowInstanceDto>> ResubmitAsync(Guid instanceId, string? comment, string? idempotencyKey, CancellationToken ct)
    {
        if (user.UserId is not { } me) return Error.Unauthorized("workflow.unauthenticated", "Sign in required.");
        var result = await RunAsync(async () =>
        {
            var instance = await db.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == instanceId && i.RequesterUserId == me, ct);
            if (instance is null) return Error.NotFound("workflow.instance_not_found", "Workflow instance not found.");
            if (await IsReplayAsync(instance.Id, idempotencyKey, ct)) return Result.Success();
            if (instance.Status != WorkflowInstanceStatuses.SentBack)
                return Error.Conflict("workflow.not_sent_back", "Only sent-back workflows can be resubmitted.");
            instance.Status = WorkflowInstanceStatuses.InProgress;
            instance.LastActionAt = Now;
            await workItems.CloseAsync(WorkItemTypes.Resubmit, instance.Id.ToString(), false, ct);
            Log(instance, null, WorkflowActions.Resubmit, me, null, comment, idempotencyKey);
            Audit(instance, WorkflowActions.Resubmit, null);
            AddOutbox(instance, "workflow.instance.resubmitted");
            var tasks = await db.WorkflowInstanceSteps.Where(t => t.InstanceId == instance.Id).ToListAsync(ct);
            if (await ActivateStageAsync(instance, await LoadStepsAsync(instance.DefinitionId, ct), tasks, 1, ct) == StageOutcome.Completed)
                return Error.Rule("workflow.no_applicable_step", "The workflow has no applicable step for this document.");
            return Result.Success();
        }, ct);
        return result.IsSuccess ? await LoadDtoAsync(instanceId, ct) : result.Error!;
    }

    public async Task<Result<WorkflowInstanceDto>> ResumeAsync(Guid instanceId, Guid? assigneeUserId, string? comment, string? idempotencyKey, CancellationToken ct)
    {
        if (user.UserId is not { } me) return Error.Unauthorized("workflow.unauthenticated", "Sign in required.");
        if (await access.CheckAsync(WorkflowModule.ModuleCode, WorkflowPermissions.InstancesManage, ct) is not null)
            return Error.Forbidden("workflow.forbidden", "Only workflow administrators can resume stalled requests.");
        var subject = await db.WorkflowInstances.AsNoTracking().Where(i => i.Id == instanceId)
            .Select(i => new { i.SubjectEmployeeId }).FirstOrDefaultAsync(ct);
        if (subject is null || !await InManageScopeAsync(subject.SubjectEmployeeId, ct))
            return Error.NotFound("workflow.instance_not_found", "Workflow instance not found.");
        // Resolved before the transaction: the permission cache runs its factory outside the execution strategy.
        if (assigneeUserId is { } candidate && (!await permissions.HasAsync(candidate, WorkflowPermissions.WorkItemsAct, ct)
                                                || !await permissions.HasAsync(candidate, WorkflowPermissions.WorkItemsView, ct)))
            return Error.Validation("workflow.assignee", "The assignee cannot act on approvals.");

        var result = await RunAsync(async () =>
        {
            var instance = await db.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == instanceId, ct);
            if (instance is null) return Error.NotFound("workflow.instance_not_found", "Workflow instance not found.");
            if (await IsReplayAsync(instance.Id, idempotencyKey, ct)) return Result.Success();
            if (instance.Status != WorkflowInstanceStatuses.Stalled)
                return Error.Conflict("workflow.not_stalled", "Only stalled workflows can be resumed.");
            if (assigneeUserId is { } to)
            {
                if (to == instance.RequesterUserId) return Error.Rule("workflow.self_approval", "A request cannot be assigned to its requester.");
                if (!await IsActiveUserAsync(to, ct)) return Error.Validation("workflow.assignee", "The assignee must be an active user.");
            }
            instance.Status = WorkflowInstanceStatuses.InProgress;
            instance.LastActionAt = Now;
            Log(instance, null, WorkflowActions.Resumed, me, assigneeUserId, comment, idempotencyKey);
            Audit(instance, WorkflowActions.Resumed, new { stage = instance.CurrentStepOrder, assignee = assigneeUserId });
            AddOutbox(instance, "workflow.instance.resumed");
            var tasks = await db.WorkflowInstanceSteps.Where(t => t.InstanceId == instance.Id).ToListAsync(ct);
            var outcome = await ActivateStageAsync(instance, await LoadStepsAsync(instance.DefinitionId, ct), tasks, instance.CurrentStepOrder, ct, assigneeUserId);
            if (outcome == StageOutcome.Stalled)
                return Error.Rule("workflow.still_no_approver", "Still no approver for this stage. Choose an assignee to resume.");
            if (outcome == StageOutcome.Completed)
                Finish(instance, WorkflowInstanceStatuses.Approved, "workflow.instance.approved", "approved");
            return Result.Success();
        }, ct);
        return result.IsSuccess ? await LoadDtoAsync(instanceId, ct) : result.Error!;
    }

    public async Task<int> ReturnEndedDelegationsAsync(CancellationToken ct)
    {
        var today = Today;
        var ended = await db.WorkflowInstanceSteps.AsNoTracking()
            .Where(t => t.Status == WorkflowTaskStatuses.Pending && t.DelegationId != null && t.DelegatedFromUserId != null)
            .Join(db.WorkflowDelegations.AsNoTracking().Where(d => !d.IsActive || d.ValidFrom > today || d.ValidTo < today),
                t => t.DelegationId, d => (Guid?)d.Id, (t, d) => t.Id)
            .Take(500).ToListAsync(ct);
        var returned = 0;
        foreach (var taskId in ended)
        {
            var r = await RunAsync(async () =>
            {
                var task = await db.WorkflowInstanceSteps.FirstOrDefaultAsync(t => t.Id == taskId && t.Status == WorkflowTaskStatuses.Pending, ct);
                if (task?.DelegatedFromUserId is not { } original || task.DelegationId is null) return Result.Success();
                var instance = await db.WorkflowInstances.FirstAsync(i => i.Id == task.InstanceId, ct);
                var from = task.AssigneeUserId;
                await workItems.CloseAsync(WorkItemTypes.Approval, task.Id.ToString(), true, ct);
                task.AssigneeUserId = original;
                task.DelegatedFromUserId = null;
                task.DelegationId = null;
                if (task.ClaimedByUserId == from) task.ClaimedByUserId = null;
                await PublishTaskItemAsync(instance, task, ct);
                Log(instance, task.Id, WorkflowActions.Assigned, null, original, "Returned to the original approver: delegation ended.", null);
                Audit(instance, "delegation_ended", new { taskId, from, to = original });
                Notify(original, "workflow.task_assigned", $"Approval needed: {instance.Title}", "/my-work");
                returned++;
                return Result.Success();
            }, ct);
            _ = r;
        }
        return returned;
    }

    // ---------------- SLA ----------------
    public async Task<int> ProcessOverdueAsync(CancellationToken ct)
    {
        var now = Now;
        var overdue = await db.WorkflowInstanceSteps.AsNoTracking()
            .Where(t => t.Status == WorkflowTaskStatuses.Pending && t.DueAt != null && t.DueAt < now)
            .Select(t => t.Id).Take(500).ToListAsync(ct);
        var processed = 0;
        foreach (var taskId in overdue)
        {
            var r = await RunAsync(async () =>
            {
                var task = await db.WorkflowInstanceSteps.FirstOrDefaultAsync(t => t.Id == taskId && t.Status == WorkflowTaskStatuses.Pending, ct);
                if (task is null) return Result.Success();
                var instance = await db.WorkflowInstances.FirstAsync(i => i.Id == task.InstanceId, ct);
                if (instance.Status != WorkflowInstanceStatuses.InProgress) return Result.Success();
                var defStep = await db.WorkflowDefinitionSteps.AsNoTracking().FirstAsync(s => s.Id == task.DefinitionStepId, ct);
                var target = defStep.EscalationType == WorkflowEscalationTypes.None ? null
                    : await ResolveEscalationTargetAsync(defStep, task, instance.RequesterUserId, ct);
                instance.LastActionAt = Now;
                if (target is { } to)
                {
                    var from = task.AssigneeUserId;
                    task.DueAt = defStep.SlaHours is { } h ? Now.AddHours(h) : null;
                    await ReassignAsync(instance, task, to, ct);
                    task.EscalationLevel++;
                    task.ReminderSentAt = null;
                    Log(instance, task.Id, WorkflowActions.Escalate, null, to, "Auto-escalated after SLA breach.", null);
                    Audit(instance, WorkflowActions.Escalate, new { taskId, from, to, auto = true });
                }
                else if (task.ReminderSentAt is null)
                {
                    task.ReminderSentAt = Now;
                    Log(instance, task.Id, WorkflowActions.Reminder, null, task.AssigneeUserId, "SLA overdue reminder.", null);
                    Audit(instance, WorkflowActions.Reminder, new { taskId });
                    Notify(task.AssigneeUserId, "workflow.task_overdue", $"Overdue approval: {instance.Title}", "/my-work");
                }
                else return Result.Success();
                processed++;
                return Result.Success();
            }, ct);
            _ = r;
        }
        return processed;
    }

    // ---------------- Queries ----------------
    public async Task<Result<WorkflowInstanceDto>> GetAsync(Guid instanceId, CancellationToken ct)
    {
        if (!await CanViewAsync(instanceId, ct)) return Error.NotFound("workflow.instance_not_found", "Workflow instance not found.");
        return await LoadDtoAsync(instanceId, ct);
    }

    public async Task<Result<IReadOnlyList<WorkflowActionDto>>> HistoryAsync(Guid instanceId, CancellationToken ct)
    {
        if (!await CanViewAsync(instanceId, ct)) return Error.NotFound("workflow.instance_not_found", "Workflow instance not found.");
        var rows = await db.WorkflowActions.AsNoTracking().Where(a => a.InstanceId == instanceId)
            .OrderBy(a => a.OccurredAt).ThenBy(a => a.Id).ToListAsync(ct);
        var names = await UserNamesAsync(rows.SelectMany(r => new[] { r.ActorUserId, r.TargetUserId }), ct);
        return rows.Select(r => new WorkflowActionDto(r.Id, r.InstanceStepId, r.Action, r.ActorUserId, Name(names, r.ActorUserId),
            r.TargetUserId, Name(names, r.TargetUserId), r.Comment, r.OccurredAt)).ToList();
    }

    public async Task<WorkflowInstanceDto?> FindByDocumentAsync(string documentType, string documentId, CancellationToken ct)
    {
        var id = await db.WorkflowInstances.AsNoTracking().Where(i => i.DocumentType == documentType && i.DocumentId == documentId)
            .OrderByDescending(i => i.StartedAt).Select(i => (Guid?)i.Id).FirstOrDefaultAsync(ct);
        return id is null ? null : (await LoadDtoAsync(id.Value, ct)).Value;
    }

    public async Task<PagedResult<WorkflowInstanceDto>> ListAsync(WorkflowInstanceQuery query, bool canViewAll, CancellationToken ct)
    {
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, PageRequest.MaxPageSize);
        var q = db.WorkflowInstances.AsNoTracking();
        if (query.Mine || !canViewAll)
        {
            var me = user.UserId ?? Guid.Empty;
            q = q.Where(i => i.RequesterUserId == me);
        }
        else
        {
            var scope = await scopes.ResolveAsync(WorkflowPermissions.InstancesView, ct);
            var scoped = scopes.ApplyEmployeeScope(q.Where(i => i.SubjectEmployeeId != null), i => i.SubjectEmployeeId, scope);
            q = scope.All ? q : scoped;
        }
        if (!string.IsNullOrWhiteSpace(query.Status)) q = q.Where(i => i.Status == query.Status);
        if (!string.IsNullOrWhiteSpace(query.DocumentType)) q = q.Where(i => i.DocumentType == query.DocumentType);
        var total = await q.CountAsync(ct);
        var ids = await q.OrderByDescending(i => i.StartedAt).Skip((page - 1) * size).Take(size).Select(i => i.Id).ToListAsync(ct);
        var items = new List<WorkflowInstanceDto>();
        foreach (var id in ids) items.Add((await LoadDtoAsync(id, ct)).Value!);
        return new PagedResult<WorkflowInstanceDto>(items, page, size, total);
    }

    public async Task<PagedResult<WorkflowInstanceDto>> ListStalledAsync(int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        var size = Math.Clamp(pageSize, 1, PageRequest.MaxPageSize);
        var q = db.WorkflowInstances.AsNoTracking().Where(i => i.Status == WorkflowInstanceStatuses.Stalled);
        var scope = await scopes.ResolveAsync(WorkflowPermissions.InstancesManage, ct);
        if (!scope.All) q = scopes.ApplyEmployeeScope(q.Where(i => i.SubjectEmployeeId != null), i => i.SubjectEmployeeId, scope);
        var total = await q.CountAsync(ct);
        var ids = await q.OrderBy(i => i.LastActionAt).Skip((page - 1) * size).Take(size).Select(i => i.Id).ToListAsync(ct);
        var items = new List<WorkflowInstanceDto>();
        foreach (var id in ids) items.Add((await LoadDtoAsync(id, ct)).Value!);
        return new PagedResult<WorkflowInstanceDto>(items, page, size, total);
    }

    private async Task<bool> InManageScopeAsync(Guid? subjectEmployeeId, CancellationToken ct) =>
        subjectEmployeeId is { } emp
            ? await scopes.CanAccessEmployeeAsync(WorkflowPermissions.InstancesManage, emp, ct)
            : (await scopes.ResolveAsync(WorkflowPermissions.InstancesManage, ct)).All;

    private async Task<bool> CanViewAsync(Guid instanceId, CancellationToken ct)
    {
        var me = user.UserId ?? Guid.Empty;
        var inst = await db.WorkflowInstances.AsNoTracking().Where(i => i.Id == instanceId)
            .Select(i => new { i.RequesterUserId, i.SubjectEmployeeId }).FirstOrDefaultAsync(ct);
        if (inst is null) return false;
        if (inst.RequesterUserId == me) return true;
        if (await db.WorkflowInstanceSteps.AsNoTracking().AnyAsync(t => t.InstanceId == instanceId && (t.AssigneeUserId == me || t.DelegatedFromUserId == me), ct))
            return true;
        if (await access.CheckAsync(WorkflowModule.ModuleCode, WorkflowPermissions.InstancesView, ct) is not null) return false;
        return inst.SubjectEmployeeId is { } emp
            ? await scopes.CanAccessEmployeeAsync(WorkflowPermissions.InstancesView, emp, ct)
            : (await scopes.ResolveAsync(WorkflowPermissions.InstancesView, ct)).All;
    }

    private async Task<Result<WorkflowInstanceDto>> LoadDtoAsync(Guid instanceId, CancellationToken ct)
    {
        var i = await db.WorkflowInstances.AsNoTracking().FirstOrDefaultAsync(x => x.Id == instanceId, ct);
        if (i is null) return Error.NotFound("workflow.instance_not_found", "Workflow instance not found.");
        var def = await db.WorkflowDefinitions.AsNoTracking().Where(d => d.Id == i.DefinitionId).Select(d => new { d.Code, d.Version }).FirstAsync(ct);
        var tasks = await db.WorkflowInstanceSteps.AsNoTracking().Where(t => t.InstanceId == instanceId)
            .OrderBy(t => t.StepOrder).ThenBy(t => t.CreatedAt).ToListAsync(ct);
        var names = await UserNamesAsync(tasks.Select(t => (Guid?)t.AssigneeUserId).Append(i.RequesterUserId), ct);
        return new WorkflowInstanceDto(i.Id, i.DefinitionId, def.Code, def.Version, i.DocumentType, i.DocumentId, i.ModuleCode, i.Title,
            i.DeepLink, i.RequesterUserId, Name(names, i.RequesterUserId), i.SubjectEmployeeId, i.Status, i.CurrentStepOrder, i.StartedAt,
            i.CompletedAt, i.RowVersion,
            tasks.Select(t => new WorkflowTaskDto(t.Id, t.InstanceId, t.StepOrder, t.Name, t.ApprovalMode, t.AssigneeUserId,
                Name(names, t.AssigneeUserId), t.DelegatedFromUserId, t.ClaimedByUserId, t.Status, t.DueAt, t.EscalationLevel, t.ActedAt,
                t.ActedByUserId, t.Comment, t.RowVersion)).ToList());
    }

    private async Task<Dictionary<Guid, string>> UserNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var set = ids.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => set.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }

    private static string? Name(Dictionary<Guid, string> names, Guid? id) => id is { } g && names.TryGetValue(g, out var n) ? n : null;

    // ---------------- Helpers ----------------
    private async Task<List<WorkflowDefinitionStep>> LoadStepsAsync(Guid definitionId, CancellationToken ct) =>
        await db.WorkflowDefinitionSteps.AsNoTracking().Where(s => s.DefinitionId == definitionId).OrderBy(s => s.StepOrder).ToListAsync(ct);

    /// <summary>Tasks of one stage activation share <c>CreatedAt</c> (single SaveChanges), so earlier visits of a
    /// reopened stage are ignored.</summary>
    private static bool StageComplete(List<WorkflowInstanceStep> tasks, WorkflowInstanceStep current)
    {
        var stage = tasks.Where(t => t.StepOrder == current.StepOrder && t.CreatedAt == current.CreatedAt && t.Status is not (WorkflowTaskStatuses.Cancelled or WorkflowTaskStatuses.SentBack)).ToList();
        if (stage.Any(t => t.Status == WorkflowTaskStatuses.Pending && t.ApprovalMode == "all")) return false;
        return stage.GroupBy(t => t.DefinitionStepId).All(g =>
            g.First().ApprovalMode == "all"
                ? g.Where(t => t.Status != WorkflowTaskStatuses.Superseded).All(t => t.Status == WorkflowTaskStatuses.Approved)
                : g.Any(t => t.Status == WorkflowTaskStatuses.Approved));
    }

    private void Close(WorkflowInstanceStep task, string status, Guid? actor, string? comment)
    {
        task.Status = status;
        task.ActedAt = Now;
        task.ActedByUserId = actor;
        if (comment is not null) task.Comment = comment;
    }

    private async Task CancelPendingAsync(List<WorkflowInstanceStep> tasks, CancellationToken ct)
    {
        foreach (var t in tasks.Where(t => t.Status == WorkflowTaskStatuses.Pending))
        {
            Close(t, WorkflowTaskStatuses.Cancelled, null, null);
            await workItems.CloseAsync(WorkItemTypes.Approval, t.Id.ToString(), true, ct);
        }
    }

    private async Task ReassignAsync(WorkflowInstance instance, WorkflowInstanceStep task, Guid to, CancellationToken ct)
    {
        await workItems.CloseAsync(WorkItemTypes.Approval, task.Id.ToString(), true, ct);
        task.DelegatedFromUserId = task.AssigneeUserId;
        task.DelegationId = null;
        task.AssigneeUserId = to;
        task.ClaimedByUserId = null;
        await PublishTaskItemAsync(instance, task, ct);
        Notify(to, "workflow.task_assigned", $"Approval needed: {instance.Title}", "/my-work");
    }

    private async Task PublishTaskItemAsync(WorkflowInstance instance, WorkflowInstanceStep task, CancellationToken ct)
    {
        await workItems.PublishAsync(new PublishWorkItem(instance.ModuleCode, WorkItemTypes.Approval, task.Id.ToString(), task.AssigneeUserId,
            instance.Title, task.DueAt, "normal", instance.DeepLink), ct);
        await TagInstanceItemAsync(instance, WorkItemTypes.Approval, task.Id.ToString(), ct);
    }

    private async Task TagInstanceItemAsync(WorkflowInstance instance, string itemType, string sourceRef, CancellationToken ct)
    {
        await Task.CompletedTask;
        foreach (var item in db.WorkItems.Local.Where(w => w.ItemType == itemType && w.SourceRef == sourceRef))
            item.WorkflowInstanceId = instance.Id;
        _ = ct;
    }

    private void Finish(WorkflowInstance instance, string status, string eventType, string verb, bool notifyRequester = true)
    {
        instance.Status = status;
        instance.CompletedAt = Now;
        Log(instance, null, WorkflowActions.Completed, null, null, status, null);
        AddOutbox(instance, eventType);
        if (notifyRequester) Notify(instance.RequesterUserId, $"workflow.{verb}", $"Request {verb}: {instance.Title}", instance.DeepLink);
    }

    private void Log(WorkflowInstance instance, Guid? taskId, string action, Guid? actor, Guid? target, string? comment, string? key) =>
        db.WorkflowActions.Add(new WorkflowAction
        {
            InstanceId = instance.Id,
            InstanceStepId = taskId,
            Action = action,
            ActorUserId = actor,
            TargetUserId = target,
            Comment = comment,
            IdempotencyKey = string.IsNullOrWhiteSpace(key) ? null : key,
            OccurredAt = Now,
        });

    private void Audit(WorkflowInstance instance, string action, object? details) =>
        audit.Add(WorkflowModule.ModuleCode, instance.DocumentType, instance.DocumentId, $"workflow.{action}",
            new { instanceId = instance.Id, details });

    private void AddOutbox(WorkflowInstance instance, string eventType)
    {
        db.WorkflowOutboxEvents.Add(new WorkflowOutboxEvent
        {
            EventType = eventType,
            InstanceId = instance.Id,
            DocumentType = instance.DocumentType,
            DocumentId = instance.DocumentId,
            Status = instance.Status,
            OccurredAt = Now,
        });
        outboxWritten = true;
    }

    private void Notify(Guid userId, string type, string title, string? link) =>
        pendingNotifications.Add(new NotifyCommand(userId, type, title, null, link));

    private async Task<bool> IsReplayAsync(Guid instanceId, string? key, CancellationToken ct) =>
        !string.IsNullOrWhiteSpace(key) && await db.WorkflowActions.AsNoTracking().AnyAsync(a => a.InstanceId == instanceId && a.IdempotencyKey == key, ct);

    /// <summary>Runs one mutation in a retriable tenant transaction; notifications and outbox dispatch fire after commit.</summary>
    private async Task<Result> RunAsync(Func<Task<Result>> work, CancellationToken ct)
    {
        Result outcome;
        try
        {
            outcome = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                Ctx.ChangeTracker.Clear(); // each retry starts from committed state
                pendingNotifications.Clear();
                outboxWritten = false;
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var r = await work();
                if (!r.IsSuccess)
                {
                    Ctx.ChangeTracker.Clear();
                    return r;
                }
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return r;
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            Ctx.ChangeTracker.Clear();
            return Error.Conflict("workflow.concurrent_update", "The workflow changed concurrently. Reload and try again.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            Ctx.ChangeTracker.Clear();
            return Error.Conflict("workflow.conflict", "The workflow changed concurrently or the document already has an active workflow.");
        }
        if (!outcome.IsSuccess) return outcome;
        foreach (var n in pendingNotifications.ToList()) await notifications.NotifyAsync(n, ct);
        pendingNotifications.Clear();
        if (outboxWritten) jobs.EnqueueTenant(tenant.Required.TenantId, WorkflowJobTypes.OutboxDispatch);
        return outcome;
    }
}
