using Hrms.Shared.Auditing;
using Hrms.Shared.Primitives;

namespace Hrms.Domain.Common;

public enum UserStatus
{
    Invited = 0,
    Active = 1,
    Inactive = 2,
    Locked = 3,
    Suspended = 4,
}

/// <summary>Credential/account fields shared by tenant users and platform users (separate tables, separate realms).</summary>
public abstract class IdentityUserBase : AuditableEntity, IConcurrencyAware
{
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    [AuditIgnore] public string? PasswordHash { get; set; }
    [AuditIgnore] public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public UserStatus Status { get; set; } = UserStatus.Invited;
    public bool EmailConfirmed { get; set; }
    [AuditIgnore] public int AccessFailedCount { get; set; }
    public DateTimeOffset? LockoutEndUtc { get; set; }
    public bool MfaEnabled { get; set; }
    [AuditIgnore] public string? MfaSecretProtected { get; set; }
    [AuditIgnore] public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset? PasswordChangedAt { get; set; }
    public uint RowVersion { get; set; }

    public bool IsLockedOut(DateTimeOffset now) => LockoutEndUtc is { } end && end > now;

    public void RotateSecurityStamp() => SecurityStamp = Guid.NewGuid().ToString("N");
}

public abstract class SessionBase : Entity
{
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public string Client { get; set; } = "web";
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string AuthMethods { get; set; } = "pwd";

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}

public static class TokenPurposes
{
    public const string PasswordReset = "password_reset";
    public const string EmailVerification = "email_verification";
    public const string Invitation = "invitation";
}

/// <summary>Single-use, hashed, expiring tokens (password reset, email verification, invitation).</summary>
public abstract class UserTokenBase : Entity
{
    public Guid UserId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}
