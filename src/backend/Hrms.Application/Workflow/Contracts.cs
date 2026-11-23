using Hrms.Shared.Paging;
using Hrms.Shared.Results;

namespace Hrms.Application.Workflow;

/// <summary>A document type that can be routed through the shared engine. Modules register theirs here (or via
/// <see cref="IWorkflowDocumentTypeProvider"/>) and then publish a definition for it; the engine never hard-codes
/// module approval logic.</summary>
/// <param name="Code">Stable code used on definitions and instances, e.g. <c>leave.request</c>.</param>
/// <param name="ModuleCode">Owning module; its entitlement gates starting workflows and inbox visibility.</param>
/// <param name="Name">Display name.</param>
/// <param name="DeepLinkTemplate">Owning-module route; <c>{id}</c> is replaced with the document id.</param>
public sealed record WorkflowDocumentType(string Code, string ModuleCode, string Name, string DeepLinkTemplate);

/// <summary>Lets a module contribute workflow document types without editing core code.</summary>
public interface IWorkflowDocumentTypeProvider
{
    IReadOnlyList<WorkflowDocumentType> DocumentTypes { get; }
}

/// <summary>Document types for the planned consumers of the engine (Leave, Attendance Regularization, Expense,
/// Payroll finalization, Exit/FNF) plus a generic request type owned by core.workflow.</summary>
public sealed class BuiltInWorkflowDocumentTypes : IWorkflowDocumentTypeProvider
{
    public const string GenericRequest = "workflow.generic_request";

    public IReadOnlyList<WorkflowDocumentType> DocumentTypes { get; } =
    [
        new(GenericRequest, "core.workflow", "Generic request", "/my-work?instance={id}"),
        new("leave.request", "leave", "Leave request", "/leave/requests/{id}"),
        new("attendance.regularization", "attendance", "Attendance regularization", "/attendance/regularizations/{id}"),
        new("expense.claim", "expense", "Expense claim", "/expense/claims/{id}"),
        new("payroll.run", "payroll", "Payroll finalization", "/payroll/runs/{id}"),
        new("exit.separation", "employee.exit", "Exit / separation", "/exit/separations/{id}"),
        new("fnf.settlement", "employee.exit", "Full & final settlement", "/exit/fnf/{id}"),
    ];
}

/// <summary>Request to start a workflow for a business document.</summary>
/// <param name="DocumentType">Registered <see cref="WorkflowDocumentType.Code"/>.</param>
/// <param name="DocumentId">Owning module's document id (opaque to the engine).</param>
/// <param name="Title">Non-sensitive inbox title — never include amounts or salary data (doc 19).</param>
/// <param name="SubjectEmployeeId">Employee the document is about; drives manager/department resolution and data scope.</param>
/// <param name="Amount">Optional amount for definition selection and per-step thresholds.</param>
/// <param name="Context">Extra context fields, stored as JSON for consumers; not evaluated as code.</param>
public sealed record StartWorkflowRequest(
    string DocumentType,
    string DocumentId,
    string Title,
    Guid? SubjectEmployeeId,
    decimal? Amount = null,
    IReadOnlyDictionary<string, string>? Context = null);

/// <summary>Task-level action. Supported: approve, reject, send_back, delegate, escalate, claim, comment.</summary>
/// <param name="Action">Action code (see <see cref="WorkflowActions"/>).</param>
/// <param name="Comment">Optional comment; required for reject and send_back.</param>
/// <param name="TargetUserId">Delegate target (delegate only).</param>
/// <param name="SendBackTo">"requester" (default) or "previous" stage (send_back only).</param>
/// <param name="IdempotencyKey">Replays with the same key are no-ops returning the current state.</param>
public sealed record WorkflowTaskAction(
    string Action,
    string? Comment = null,
    Guid? TargetUserId = null,
    string? SendBackTo = null,
    string? IdempotencyKey = null);

public static class WorkflowActions
{
    public const string Start = "start";
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string SendBack = "send_back";
    public const string Resubmit = "resubmit";
    public const string Delegate = "delegate";
    public const string Escalate = "escalate";
    public const string Claim = "claim";
    public const string Comment = "comment";
    public const string Cancel = "cancel";
    public const string Assigned = "assigned";
    public const string Skipped = "skipped";
    public const string Reminder = "reminder";
    public const string Completed = "completed";
    public const string Stalled = "stalled";
    public const string Resumed = "resumed";
    public static readonly string[] TaskActions = [Approve, Reject, SendBack, Delegate, Escalate, Claim, Comment];
}

public sealed record WorkflowTaskDto(
    Guid Id, Guid InstanceId, int StepOrder, string Name, string ApprovalMode, Guid AssigneeUserId, string? AssigneeName,
    Guid? DelegatedFromUserId, Guid? ClaimedByUserId, string Status, DateTimeOffset? DueAt, int EscalationLevel,
    DateTimeOffset? ActedAt, Guid? ActedByUserId, string? Comment, uint RowVersion);

public sealed record WorkflowInstanceDto(
    Guid Id, Guid DefinitionId, string DefinitionCode, int DefinitionVersion, string DocumentType, string DocumentId,
    string ModuleCode, string Title, string? DeepLink, Guid RequesterUserId, string? RequesterName, Guid? SubjectEmployeeId,
    string Status, int CurrentStepOrder, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, uint RowVersion,
    IReadOnlyList<WorkflowTaskDto> Tasks);

public sealed record WorkflowActionDto(
    Guid Id, Guid? InstanceStepId, string Action, Guid? ActorUserId, string? ActorName, Guid? TargetUserId, string? TargetName,
    string? Comment, DateTimeOffset OccurredAt);

public sealed record WorkflowInstanceQuery(string? Status = null, string? DocumentType = null, bool Mine = false, int Page = 1, int PageSize = 25);

/// <summary>The single approval engine (doc 19). Modules call it in-process: <c>StartAsync</c> when a document is
/// submitted, then react to <see cref="WorkflowEvent"/>s delivered through <see cref="IWorkflowEventHandler"/>.
/// All mutations run in one tenant transaction that also writes <c>workflow.actions</c>, a <c>core.audit</c> entry,
/// the work-item projection and an outbox event; notifications are sent after commit.</summary>
public interface IWorkflowEngine
{
    /// <summary>Selects the most specific published definition for the document type, pins the instance to that
    /// version and activates the first applicable stage. Fails when no definition or no approver resolves, or when
    /// the document already has a live instance.</summary>
    Task<Result<WorkflowInstanceDto>> StartAsync(StartWorkflowRequest request, CancellationToken ct);

    /// <summary>Applies an action to a task. The caller must be the task assignee (claim/approve/reject/send_back/
    /// delegate/comment); escalate is also allowed for holders of <c>workflow.instances.manage</c>.</summary>
    Task<Result<WorkflowInstanceDto>> ActAsync(Guid taskId, WorkflowTaskAction action, CancellationToken ct);

    /// <summary>Cancels a live instance (requester withdraw, or <c>workflow.instances.manage</c>).</summary>
    Task<Result<WorkflowInstanceDto>> CancelAsync(Guid instanceId, string? reason, string? idempotencyKey, CancellationToken ct);

    /// <summary>Requester re-submits a sent-back instance; routing restarts at the first stage of the pinned version.</summary>
    Task<Result<WorkflowInstanceDto>> ResubmitAsync(Guid instanceId, string? comment, string? idempotencyKey, CancellationToken ct);

    /// <summary>Administrator correction of a <c>stalled</c> instance: re-runs approver resolution for the stalled stage,
    /// assigning <paramref name="assigneeUserId"/> to steps that still resolve nobody, and resumes it.</summary>
    Task<Result<WorkflowInstanceDto>> ResumeAsync(Guid instanceId, Guid? assigneeUserId, string? comment, string? idempotencyKey, CancellationToken ct);

    /// <summary>Instance visible to the caller (requester, any assignee, or scoped <c>workflow.instances.view</c>).</summary>
    Task<Result<WorkflowInstanceDto>> GetAsync(Guid instanceId, CancellationToken ct);

    /// <summary>Append-only action history for an instance, same visibility as <see cref="GetAsync"/>.</summary>
    Task<Result<IReadOnlyList<WorkflowActionDto>>> HistoryAsync(Guid instanceId, CancellationToken ct);

    /// <summary>Latest live/completed instance for a document, for module detail pages.</summary>
    Task<WorkflowInstanceDto?> FindByDocumentAsync(string documentType, string documentId, CancellationToken ct);

    /// <summary>Lists instances: own requests (<c>Mine</c>) or, with <c>workflow.instances.view</c>, all in scope.</summary>
    Task<PagedResult<WorkflowInstanceDto>> ListAsync(WorkflowInstanceQuery query, bool canViewAll, CancellationToken ct);

    /// <summary>Stalled instances within the caller's <c>workflow.instances.manage</c> data scope, oldest first.</summary>
    Task<PagedResult<WorkflowInstanceDto>> ListStalledAsync(int page, int pageSize, CancellationToken ct);

    /// <summary>SLA sweep: reminds or escalates overdue pending tasks per step policy (invoked by a Hangfire job).</summary>
    Task<int> ProcessOverdueAsync(CancellationToken ct);

    /// <summary>Returns pending tasks routed by a revoked or expired standing delegation to the original approver.</summary>
    Task<int> ReturnEndedDelegationsAsync(CancellationToken ct);
}

/// <summary>State-change event for a workflow instance, delivered from the transactional outbox.</summary>
/// <param name="EventType">e.g. <c>workflow.instance.started</c>, <c>.approved</c>, <c>.rejected</c>, <c>.sent_back</c>, <c>.cancelled</c>.</param>
public sealed record WorkflowEvent(Guid EventId, string EventType, Guid InstanceId, string DocumentType, string DocumentId, string Status, DateTimeOffset OccurredAt);

/// <summary>Module hook for workflow outcomes (e.g. Leave marks the request approved and posts the ledger entry).
/// Handlers must be idempotent: delivery is at-least-once. Register as scoped in DI.</summary>
public interface IWorkflowEventHandler
{
    /// <summary>Document type handled, or <c>*</c> for all.</summary>
    string DocumentType { get; }
    Task HandleAsync(WorkflowEvent evt, CancellationToken ct);
}

/// <summary>Input for a module-published (non-workflow) inbox item, e.g. "Acknowledge policy".</summary>
/// <param name="Title">Non-sensitive title (no amounts/salary).</param>
public sealed record PublishWorkItem(string ModuleCode, string ItemType, string SourceRef, Guid AssigneeUserId, string Title,
    DateTimeOffset? DueAt = null, string Priority = "normal", string? DeepLink = null);

/// <summary>Writes the non-authoritative inbox projection (doc 19 <c>IWorkItemPublisher</c>). Changes are tracked on the
/// current tenant context and saved with the caller's transaction.</summary>
public interface IWorkItemPublisher
{
    /// <summary>Upserts an open item keyed by (item type, source ref, assignee).</summary>
    Task PublishAsync(PublishWorkItem item, CancellationToken ct);
    /// <summary>Marks all open items for the source completed (or cancelled).</summary>
    Task CloseAsync(string itemType, string sourceRef, bool cancelled, CancellationToken ct);
}

public sealed record WorkItemDto(
    Guid Id, string ModuleCode, string ItemType, string SourceRef, Guid? WorkflowInstanceId, string Title, DateTimeOffset? DueAt,
    string Priority, string Status, string? DeepLink, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, bool Overdue,
    bool Claimed, bool ClaimedByMe);

public sealed record WorkItemQuery(string? Status = "open", string? ModuleCode = null, string? Search = null, bool OverdueOnly = false, int Page = 1, int PageSize = 25);

/// <summary>Read/act surface of the unified inbox for the current user. Only items assigned to the caller in enabled
/// modules are returned; acting delegates to <see cref="IWorkflowEngine"/>, which re-checks authorization.</summary>
public interface IWorkItemService
{
    Task<PagedResult<WorkItemDto>> MyItemsAsync(WorkItemQuery query, CancellationToken ct);
    Task<Result<WorkflowInstanceDto>> ClaimAsync(Guid workItemId, string? idempotencyKey, CancellationToken ct);
    /// <summary>Completes a workflow item with approve, reject or send_back.</summary>
    Task<Result<WorkflowInstanceDto>> CompleteAsync(Guid workItemId, WorkflowTaskAction action, CancellationToken ct);
    Task<Result<WorkflowInstanceDto>> DelegateAsync(Guid workItemId, Guid toUserId, string? comment, string? idempotencyKey, CancellationToken ct);
    /// <summary>Active users whose effective permissions include <c>workitems.inbox.view</c> and <c>workitems.inbox.act</c>,
    /// searchable by name or email. Delegation (<paramref name="forInstanceId"/> null) excludes the caller. Stalled-request
    /// reassignment passes the instance: its requester is excluded and the caller may be listed. The engine re-validates the chosen user.</summary>
    Task<PagedResult<WorkflowUserDto>> EligibleDelegatesAsync(string? search, int page, int pageSize, Guid? forInstanceId, CancellationToken ct);
}

public sealed record WorkflowUserDto(Guid Id, string DisplayName, string Email);
