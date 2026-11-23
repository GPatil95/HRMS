using Hrms.Application.Abstractions;
using Hrms.Application.Authorization;
using Hrms.Application.Modules;
using Hrms.Domain.Common;
using Hrms.Domain.Tenant;
using Hrms.Shared.Paging;
using Hrms.Shared.Primitives;
using Hrms.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Workflow;

public sealed class WorkItemPublisher(ITenantDbContext db, IClock clock) : IWorkItemPublisher
{
    public async Task PublishAsync(PublishWorkItem item, CancellationToken ct)
    {
        var existing = db.WorkItems.Local.FirstOrDefault(w => w.ItemType == item.ItemType && w.SourceRef == item.SourceRef && w.AssigneeUserId == item.AssigneeUserId)
                       ?? await db.WorkItems.FirstOrDefaultAsync(w => w.ItemType == item.ItemType && w.SourceRef == item.SourceRef && w.AssigneeUserId == item.AssigneeUserId, ct);
        if (existing is null)
        {
            existing = new WorkItem { ItemType = item.ItemType, SourceRef = item.SourceRef, AssigneeUserId = item.AssigneeUserId, CreatedAt = clock.UtcNow };
            db.WorkItems.Add(existing);
        }
        existing.ModuleCode = item.ModuleCode;
        existing.Title = item.Title.Length > 200 ? item.Title[..200] : item.Title;
        existing.DueAt = item.DueAt;
        existing.Priority = item.Priority;
        existing.DeepLink = item.DeepLink;
        existing.Status = WorkItemStatuses.Open;
        existing.CompletedAt = null;
    }

    public async Task CloseAsync(string itemType, string sourceRef, bool cancelled, CancellationToken ct)
    {
        var items = await db.WorkItems.Where(w => w.ItemType == itemType && w.SourceRef == sourceRef && w.Status == WorkItemStatuses.Open).ToListAsync(ct);
        foreach (var w in db.WorkItems.Local.Where(w => w.ItemType == itemType && w.SourceRef == sourceRef && w.Status == WorkItemStatuses.Open))
            if (!items.Contains(w)) items.Add(w);
        foreach (var w in items)
        {
            w.Status = cancelled ? WorkItemStatuses.Cancelled : WorkItemStatuses.Completed;
            w.CompletedAt = clock.UtcNow;
        }
    }
}

public sealed class WorkItemService(
    ITenantDbContext db, ICurrentUser user, IClock clock, ITenantContextAccessor tenant, IEntitlementService entitlements, IWorkflowEngine engine)
    : IWorkItemService
{
    private static readonly string[] CompleteActions = [WorkflowActions.Approve, WorkflowActions.Reject, WorkflowActions.SendBack];

    public async Task<PagedResult<WorkItemDto>> MyItemsAsync(WorkItemQuery query, CancellationToken ct)
    {
        var me = user.UserId ?? Guid.Empty;
        var ent = await entitlements.GetAsync(tenant.Required.TenantId, ct);
        var enabled = ent.Modules.ToList();
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, PageRequest.MaxPageSize);
        var now = clock.UtcNow;
        // Items of disabled modules are hidden, never deleted (doc 19).
        var q = db.WorkItems.AsNoTracking().Where(w => w.AssigneeUserId == me && enabled.Contains(w.ModuleCode));
        if (!string.IsNullOrWhiteSpace(query.Status) && query.Status != "all") q = q.Where(w => w.Status == query.Status);
        if (!string.IsNullOrWhiteSpace(query.ModuleCode)) q = q.Where(w => w.ModuleCode == query.ModuleCode);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            q = q.Where(w => w.Title.ToLower().Contains(term));
        }
        if (query.OverdueOnly) q = q.Where(w => w.Status == WorkItemStatuses.Open && w.DueAt != null && w.DueAt < now);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(w => w.Status == WorkItemStatuses.Open ? 0 : 1).ThenBy(w => w.DueAt ?? DateTimeOffset.MaxValue)
            .ThenByDescending(w => w.CreatedAt).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var taskIds = rows.Where(r => r.ItemType == WorkItemTypes.Approval).Select(r => Guid.TryParse(r.SourceRef, out var g) ? g : Guid.Empty).ToList();
        var claims = await db.WorkflowInstanceSteps.AsNoTracking().Where(t => taskIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id.ToString(), t => t.ClaimedByUserId, ct);
        return new PagedResult<WorkItemDto>(rows.Select(w =>
        {
            var claimedBy = claims.GetValueOrDefault(w.SourceRef);
            return new WorkItemDto(w.Id, w.ModuleCode, w.ItemType, w.SourceRef, w.WorkflowInstanceId, w.Title, w.DueAt, w.Priority, w.Status,
                w.DeepLink, w.CreatedAt, w.CompletedAt, w.Status == WorkItemStatuses.Open && w.DueAt < now, claimedBy is not null, claimedBy == me);
        }).ToList(), page, size, total);
    }

    public async Task<Result<WorkflowInstanceDto>> ClaimAsync(Guid workItemId, string? idempotencyKey, CancellationToken ct)
    {
        var task = await TaskOfAsync(workItemId, ct);
        return task.IsSuccess ? await engine.ActAsync(task.Value, new WorkflowTaskAction(WorkflowActions.Claim, IdempotencyKey: idempotencyKey), ct) : task.Error!;
    }

    public async Task<Result<WorkflowInstanceDto>> CompleteAsync(Guid workItemId, WorkflowTaskAction action, CancellationToken ct)
    {
        if (!CompleteActions.Contains(action.Action))
            return Error.Validation("workitems.invalid_action", "Complete supports approve, reject or send_back.");
        var task = await TaskOfAsync(workItemId, ct);
        return task.IsSuccess ? await engine.ActAsync(task.Value, action, ct) : task.Error!;
    }

    public async Task<Result<WorkflowInstanceDto>> DelegateAsync(Guid workItemId, Guid toUserId, string? comment, string? idempotencyKey, CancellationToken ct)
    {
        var task = await TaskOfAsync(workItemId, ct);
        return task.IsSuccess
            ? await engine.ActAsync(task.Value, new WorkflowTaskAction(WorkflowActions.Delegate, comment, toUserId, null, idempotencyKey), ct)
            : task.Error!;
    }

    public async Task<PagedResult<WorkflowUserDto>> EligibleDelegatesAsync(string? search, int page, int pageSize, Guid? forInstanceId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var eligible = WorkflowEligibility.UsersWhoCanAct(db, today);
        var q = db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active && eligible.Contains(u.Id));
        if (forInstanceId is { } instanceId)
        {
            var requester = await db.WorkflowInstances.AsNoTracking().Where(i => i.Id == instanceId).Select(i => (Guid?)i.RequesterUserId).FirstOrDefaultAsync(ct);
            if (requester is null) return new PagedResult<WorkflowUserDto>([], page, pageSize, 0);
            q = q.Where(u => u.Id != requester);
        }
        else
        {
            var me = user.UserId ?? Guid.Empty;
            q = q.Where(u => u.Id != me);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(u => u.DisplayName.ToLower().Contains(term) || u.Email.ToLower().Contains(term));
        }
        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(u => u.DisplayName).ThenBy(u => u.Email).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new WorkflowUserDto(u.Id, u.DisplayName, u.Email)).ToListAsync(ct);
        return new PagedResult<WorkflowUserDto>(items, page, pageSize, total);
    }

    private async Task<Result<Guid>> TaskOfAsync(Guid workItemId, CancellationToken ct)
    {
        var me = user.UserId ?? Guid.Empty;
        var item = await db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workItemId && w.AssigneeUserId == me, ct);
        if (item is null) return Error.NotFound("workitems.not_found", "Work item not found.");
        if (item.ItemType != WorkItemTypes.Approval || !Guid.TryParse(item.SourceRef, out var taskId))
            return Error.Rule("workitems.not_actionable", "This item must be handled in its owning module.");
        if (item.Status != WorkItemStatuses.Open) return Error.Conflict("workitems.closed", "This item is already closed.");
        return taskId;
    }
}
