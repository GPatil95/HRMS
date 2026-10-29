namespace Hrms.Domain.Common;

/// <summary>Append-only data-change/business audit record. Used in both platform and tenant databases.</summary>
public sealed class AuditEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? TenantId { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string ActorType { get; set; } = string.Empty;
    public Guid? ActorId { get; set; }
    public string? ActorDisplay { get; set; }
    public Guid? OnBehalfOf { get; set; }
    public string? Changes { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }
    public string? Reason { get; set; }
    public string ContentHash { get; set; } = string.Empty;
}

public static class SecurityEventTypes
{
    public const string LoginSucceeded = "LOGIN_SUCCEEDED";
    public const string LoginFailed = "LOGIN_FAILED";
    public const string MfaChallengeFailed = "MFA_FAILED";
    public const string AccountLocked = "ACCOUNT_LOCKED";
    public const string Logout = "LOGOUT";
    public const string SessionRevoked = "SESSION_REVOKED";
    public const string PasswordChanged = "PASSWORD_CHANGED";
    public const string PasswordResetRequested = "PASSWORD_RESET_REQUESTED";
    public const string PasswordResetCompleted = "PASSWORD_RESET_COMPLETED";
    public const string EmailVerified = "EMAIL_VERIFIED";
    public const string MfaEnabled = "MFA_ENABLED";
    public const string MfaDisabled = "MFA_DISABLED";
    public const string UserCreated = "USER_CREATED";
    public const string UserActivated = "USER_ACTIVATED";
    public const string UserDeactivated = "USER_DEACTIVATED";
    public const string UserLocked = "USER_LOCKED";
    public const string UserUnlocked = "USER_UNLOCKED";
    public const string UserSuspended = "USER_SUSPENDED";
    public const string RoleAssigned = "ROLE_ASSIGNED";
    public const string RoleUnassigned = "ROLE_UNASSIGNED";
    public const string RolePermissionsChanged = "ROLE_PERMISSIONS_CHANGED";
    public const string PermissionOverrideChanged = "PERMISSION_OVERRIDE_CHANGED";
    public const string GrantCeilingViolation = "GRANT_EXCEEDS_OWN_PERMISSION";
    public const string TenantMismatch = "TENANT_MISMATCH";
    public const string TenantChanged = "TENANT_CHANGED";
    public const string SubscriptionChanged = "SUBSCRIPTION_CHANGED";
    public const string EntitlementChanged = "ENTITLEMENT_CHANGED";
    public const string SupportAccessRequested = "SUPPORT_ACCESS_REQUESTED";
    public const string SupportAccessApproved = "SUPPORT_ACCESS_APPROVED";
    public const string SupportAccessRevoked = "SUPPORT_ACCESS_REVOKED";
    public const string ImpersonationStarted = "IMPERSONATION_STARTED";
    public const string SensitiveExport = "SENSITIVE_EXPORT";
    public const string ApiAuthFailed = "API_AUTH_FAILED";
}

public sealed class SecurityEvent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? TenantId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public bool Success { get; set; }
    public Guid? UserId { get; set; }
    public string? UserEmail { get; set; }
    public Guid? ActorId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }
    public string? Details { get; set; }
    public string ContentHash { get; set; } = string.Empty;
}

/// <summary>Periodic hash-chain seal over audit + security records (tamper evidence).</summary>
public sealed class AuditSeal
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset SealedAt { get; set; }
    public DateTimeOffset RangeStart { get; set; }
    public DateTimeOffset RangeEnd { get; set; }
    public int EntryCount { get; set; }
    public string PreviousSealHash { get; set; } = string.Empty;
    public string ChainHash { get; set; } = string.Empty;
}

public enum EmailStatus
{
    Queued = 0,
    Sending = 1,
    Sent = 2,
    Failed = 3,
    Cancelled = 4,
    Retrying = 5,
}

public sealed class EmailMessage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; set; }
    public string[] To { get; set; } = [];
    public string[] Cc { get; set; } = [];
    public string Subject { get; set; } = string.Empty;
    public string? HtmlBody { get; set; }
    public string? TextBody { get; set; }
    public Guid[] AttachmentFileIds { get; set; } = [];
    public string? TemplateCode { get; set; }
    public EmailStatus Status { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? CorrelationId { get; set; }
}

public enum JobExecutionStatus
{
    Running = 0,
    Completed = 1,
    Failed = 2,
    Skipped = 3,
}

public sealed class JobExecution
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string JobType { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public JobExecutionStatus Status { get; set; }
    public int Attempt { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Error { get; set; }
    public string? CorrelationId { get; set; }
}
