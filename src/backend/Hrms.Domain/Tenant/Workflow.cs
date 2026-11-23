using Hrms.Shared.Auditing;
using Hrms.Shared.Primitives;

namespace Hrms.Domain.Tenant;

public static class WorkflowDefinitionStatuses
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Retired = "retired";
}

public static class WorkflowInstanceStatuses
{
    public const string InProgress = "in_progress";
    public const string SentBack = "sent_back";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
    /// <summary>A required step resolved no approver; an administrator must reassign and resume.</summary>
    public const string Stalled = "stalled";
}

public static class WorkflowTaskStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string SentBack = "sent_back";
    public const string Superseded = "superseded";
    public const string Cancelled = "cancelled";
}

public static class WorkflowApproverTypes
{
    public const string ReportingManager = "reporting_manager";
    public const string DepartmentHead = "department_head";
    public const string DepartmentField = "department_field";
    public const string Role = "role";
    public const string User = "user";
    public static readonly string[] All = [ReportingManager, DepartmentHead, DepartmentField, Role, User];
}

/// <summary>Where role-based approvers must sit relative to the request's subject employee.</summary>
public static class WorkflowApproverScopes
{
    public const string Tenant = "tenant";
    public const string Company = "company";
    public const string Branch = "branch";
    public const string Department = "department";
    public const string Reporting = "reporting";
    public static readonly string[] All = [Tenant, Company, Branch, Department, Reporting];
}

public static class WorkflowEscalationTypes
{
    public const string None = "none";
    public const string AssigneeManager = "assignee_manager";
    public const string Role = "role";
    public const string User = "user";
    public static readonly string[] All = [None, AssigneeManager, Role, User];
}

public static class WorkItemStatuses
{
    public const string Open = "open";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
}

/// <summary>One immutable-once-published version of a workflow for a document type. Versions of the same
/// <see cref="Code"/> share a family; at most one is published per day (gist exclusion).</summary>
[Audited("core.workflow")]
public sealed class WorkflowDefinition : AuditableEntity, IConcurrencyAware
{
    public string Code { get; set; } = string.Empty;
    public int Version { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string ModuleCode { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = WorkflowDefinitionStatuses.Draft;
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    /// <summary>Tie-breaker when two definitions match with equal specificity (higher wins).</summary>
    public int Priority { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? DepartmentId { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public uint RowVersion { get; set; }
    public List<WorkflowDefinitionStep> Steps { get; set; } = [];
}

/// <summary>A stage member. Steps sharing a <see cref="StepOrder"/> run in parallel; orders run sequentially.</summary>
[Audited("core.workflow")]
public sealed class WorkflowDefinitionStep : AuditableEntity
{
    public Guid DefinitionId { get; set; }
    public int StepOrder { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ApproverType { get; set; } = WorkflowApproverTypes.ReportingManager;
    /// <summary>Resolver argument: manager level, department field, role code or user id.</summary>
    public string? ApproverParam { get; set; }
    /// <summary>Role steps only: restricts role holders to the subject's company/branch/department or reporting line.</summary>
    public string ApproverScope { get; set; } = WorkflowApproverScopes.Tenant;
    /// <summary>When several approvers resolve: "any" (first decision wins) or "all".</summary>
    public string ApprovalMode { get; set; } = "any";
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public Guid? FallbackUserId { get; set; }
    public int? SlaHours { get; set; }
    public string EscalationType { get; set; } = WorkflowEscalationTypes.None;
    public string? EscalationParam { get; set; }
    public bool AllowSendBack { get; set; } = true;
    public bool AllowDelegate { get; set; } = true;
}

/// <summary>A running workflow for one document, pinned to the definition version selected at start.</summary>
[Audited("core.workflow")]
public sealed class WorkflowInstance : AuditableEntity, IConcurrencyAware
{
    public Guid DefinitionId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string ModuleCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? DeepLink { get; set; }
    public Guid RequesterUserId { get; set; }
    public Guid? SubjectEmployeeId { get; set; }
    public decimal? Amount { get; set; }
    public string? ContextJson { get; set; }
    public string Status { get; set; } = WorkflowInstanceStatuses.InProgress;
    public int CurrentStepOrder { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset LastActionAt { get; set; }
    public uint RowVersion { get; set; }
}

/// <summary>An approval task: one row per resolved approver of a definition step within an instance.</summary>
[Audited("core.workflow")]
public sealed class WorkflowInstanceStep : AuditableEntity, IConcurrencyAware
{
    public Guid InstanceId { get; set; }
    public Guid DefinitionStepId { get; set; }
    public int StepOrder { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ApprovalMode { get; set; } = "any";
    public Guid AssigneeUserId { get; set; }
    public Guid? DelegatedFromUserId { get; set; }
    public Guid? ClaimedByUserId { get; set; }
    public string Status { get; set; } = WorkflowTaskStatuses.Pending;
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? ReminderSentAt { get; set; }
    public int EscalationLevel { get; set; }
    public DateTimeOffset? ActedAt { get; set; }
    public Guid? ActedByUserId { get; set; }
    public string? Comment { get; set; }
    public uint RowVersion { get; set; }
}

/// <summary>Append-only history of every workflow action (doc 19 <c>workflow.actions</c>).</summary>
public sealed class WorkflowAction : Entity
{
    public Guid InstanceId { get; set; }
    public Guid? InstanceStepId { get; set; }
    public string Action { get; set; } = string.Empty;
    public Guid? ActorUserId { get; set; }
    public Guid? TargetUserId { get; set; }
    public string? Comment { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>Standing out-of-office delegation: new tasks for the delegator go to the delegate in the window.</summary>
[Audited("core.workflow")]
public sealed class WorkflowDelegation : AuditableEntity
{
    public Guid DelegatorUserId { get; set; }
    public Guid DelegateUserId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly ValidTo { get; set; }
    public string? Reason { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Transactional outbox row for instance state changes, dispatched by a tenant job.</summary>
public sealed class WorkflowOutboxEvent : Entity
{
    public string EventType { get; set; } = string.Empty;
    public Guid InstanceId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Non-authoritative inbox projection; acting always goes through the owning module/engine.</summary>
public sealed class WorkItem : Entity
{
    public string ModuleCode { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public string SourceRef { get; set; } = string.Empty;
    public Guid? WorkflowInstanceId { get; set; }
    public Guid AssigneeUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset? DueAt { get; set; }
    public string Priority { get; set; } = "normal";
    public string Status { get; set; } = WorkItemStatuses.Open;
    public string? DeepLink { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
