using Hrms.Application.Abstractions;
using Hrms.Application.Auditing;
using Hrms.Application.Common;
using Hrms.Domain.Common;
using Hrms.Shared.Primitives;
using Hrms.Shared.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using IdentityOptions = Hrms.Application.Abstractions.IdentityOptions;

namespace Hrms.Application.Identity;

public sealed record LoginCommand(string Email, string Password, string Client = "web");

public sealed record AuthenticatedSession(Guid UserId, Guid SessionId, string Email, string DisplayName, string SecurityStamp, string AuthMethods, DateTimeOffset ExpiresAt);

public sealed record LoginResult(AuthenticatedSession? Session, bool MfaRequired, string? MfaToken);

public sealed record MfaSetup(string Secret, string ProvisioningUri);

/// <summary>Realm-agnostic authentication logic (tenant users and platform users share it, with separate stores).</summary>
public sealed class AuthService(
    IIdentityRealmStore store,
    IAuthEmailSender emails,
    ISecurityEventWriter securityEvents,
    IPasswordPolicy passwordPolicy,
    ITotpService totp,
    ISecretProtector protector,
    ISessionCache sessionCache,
    IRateLimiter rateLimiter,
    IRequestContext request,
    IClock clock,
    IOptions<IdentityOptions> options,
    ILogger<AuthService> logger)
{
    private static readonly PasswordHasher<IdentityUserBase> Hasher = new();
    private static readonly Error InvalidCredentials = Error.Unauthorized("invalid-credentials", "Invalid email or password.");
    private IdentityOptions O => options.Value;
    private string MfaPurpose => $"mfa-login:{store.Realm}:{store.TenantId}";
    private const string MfaSecretPurpose = "mfa-secret";

    public static string HashPassword(string password) => Hasher.HashPassword(null!, password);

    public async Task<Result<LoginResult>> LoginAsync(LoginCommand cmd, CancellationToken ct)
    {
        var email = Guard.NormalizeEmail(cmd.Email ?? string.Empty);
        var ipKey = $"rl:login:{store.Realm}:{store.TenantId}:ip:{request.IpAddress}";
        var acctKey = $"rl:login:{store.Realm}:{store.TenantId}:acct:{AuditHashing.Sha256(email)}";
        if (!await rateLimiter.TryAcquireAsync(ipKey, O.LoginRateLimitPerIpPerMinute, TimeSpan.FromMinutes(1), ct) ||
            !await rateLimiter.TryAcquireAsync(acctKey, O.LoginRateLimitPerAccountPerMinute, TimeSpan.FromMinutes(1), ct))
        {
            return Error.Rule("rate-limited", "Too many login attempts. Try again later.");
        }

        var user = await store.FindByEmailAsync(email, ct);
        var now = clock.UtcNow;
        if (user is null || user.PasswordHash is null)
        {
            // Equalise timing to avoid user enumeration.
            Hasher.VerifyHashedPassword(null!, DummyHash, cmd.Password ?? string.Empty);
            securityEvents.Add(SecurityEventTypes.LoginFailed, false, user?.Id, email, new { reason = "unknown_user_or_no_password" });
            await store.SaveChangesAsync(ct);
            return InvalidCredentials;
        }

        if (user.IsLockedOut(now))
        {
            securityEvents.Add(SecurityEventTypes.LoginFailed, false, user.Id, email, new { reason = "locked_out" });
            await store.SaveChangesAsync(ct);
            return Error.Rule("account-locked", "Account is temporarily locked. Try again later or reset your password.");
        }

        var verify = Hasher.VerifyHashedPassword(user, user.PasswordHash, cmd.Password ?? string.Empty);
        if (verify == PasswordVerificationResult.Failed)
        {
            user.AccessFailedCount++;
            if (user.AccessFailedCount >= O.MaxFailedAccessAttempts)
            {
                user.LockoutEndUtc = now.AddMinutes(O.LockoutMinutes);
                user.AccessFailedCount = 0;
                securityEvents.Add(SecurityEventTypes.AccountLocked, true, user.Id, email, new { minutes = O.LockoutMinutes });
            }

            securityEvents.Add(SecurityEventTypes.LoginFailed, false, user.Id, email, new { reason = "bad_password" });
            await store.SaveChangesAsync(ct);
            return InvalidCredentials;
        }

        if (verify == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = Hasher.HashPassword(user, cmd.Password!);
        }

        if (user.Status != UserStatus.Active)
        {
            securityEvents.Add(SecurityEventTypes.LoginFailed, false, user.Id, email, new { reason = "status_" + user.Status.ToString().ToLowerInvariant() });
            await store.SaveChangesAsync(ct);
            return Error.Forbidden("account-not-active", "Your account is not active. Contact your administrator.");
        }

        user.AccessFailedCount = 0;
        if (user.MfaEnabled)
        {
            var token = protector.ProtectTimed(MfaPurpose, $"{user.Id}|{user.SecurityStamp}", TimeSpan.FromMinutes(5));
            await store.SaveChangesAsync(ct);
            return new LoginResult(null, true, token);
        }

        var session = await CreateSessionAsync(user, cmd.Client, "pwd", ct);
        return new LoginResult(session, false, null);
    }

    public async Task<Result<AuthenticatedSession>> CompleteMfaAsync(string mfaToken, string code, string client, CancellationToken ct)
    {
        var payload = protector.UnprotectTimed(MfaPurpose, mfaToken ?? string.Empty);
        if (payload is null || payload.Split('|') is not [var idText, var stamp] || !Guid.TryParse(idText, out var userId))
        {
            return Error.Unauthorized("mfa-token-invalid", "MFA session expired. Sign in again.");
        }

        var user = await store.FindByIdAsync(userId, ct);
        if (user is null || user.SecurityStamp != stamp || user.Status != UserStatus.Active || !user.MfaEnabled || user.MfaSecretProtected is null)
        {
            return Error.Unauthorized("mfa-token-invalid", "MFA session expired. Sign in again.");
        }

        var secret = protector.Unprotect(MfaSecretPurpose, user.MfaSecretProtected);
        if (secret is null || !totp.Verify(secret, code ?? string.Empty))
        {
            user.AccessFailedCount++;
            if (user.AccessFailedCount >= O.MaxFailedAccessAttempts)
            {
                user.LockoutEndUtc = clock.UtcNow.AddMinutes(O.LockoutMinutes);
                user.AccessFailedCount = 0;
                securityEvents.Add(SecurityEventTypes.AccountLocked, true, user.Id, user.Email, new { reason = "mfa_failures" });
            }

            securityEvents.Add(SecurityEventTypes.MfaChallengeFailed, false, user.Id, user.Email);
            await store.SaveChangesAsync(ct);
            return Error.Unauthorized("mfa-code-invalid", "Invalid verification code.");
        }

        user.AccessFailedCount = 0;
        return await CreateSessionAsync(user, client, "pwd mfa", ct);
    }

    private async Task<AuthenticatedSession> CreateSessionAsync(IdentityUserBase user, string client, string amr, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var session = store.AddSession(user.Id);
        session.CreatedAt = now;
        session.LastSeenAt = now;
        session.ExpiresAt = now.AddHours(O.SessionAbsoluteHours);
        session.Client = client is "mobile" or "api" ? client : "web";
        session.IpAddress = request.IpAddress;
        session.UserAgent = request.UserAgent;
        session.AuthMethods = amr;
        user.LastLoginAt = now;
        securityEvents.Add(SecurityEventTypes.LoginSucceeded, true, user.Id, user.Email, new { amr, sessionId = session.Id });
        await store.SaveChangesAsync(ct);
        await sessionCache.MarkActiveAsync(store.Realm, store.TenantId, session.Id, TimeSpan.FromMinutes(5), ct);
        return new AuthenticatedSession(user.Id, session.Id, user.Email, user.DisplayName, user.SecurityStamp, amr, session.ExpiresAt);
    }

    /// <summary>Validates a session on each request (cached for a few minutes; revocation is immediate via the cache).</summary>
    public async Task<bool> ValidateSessionAsync(Guid userId, Guid sessionId, string securityStamp, CancellationToken ct)
    {
        var cached = await sessionCache.IsActiveAsync(store.Realm, store.TenantId, sessionId, ct);
        if (cached == false)
        {
            return false;
        }

        if (cached == true)
        {
            return true;
        }

        var session = await store.FindSessionAsync(sessionId, ct);
        var user = await store.FindByIdAsync(userId, ct);
        var now = clock.UtcNow;
        var valid = session is not null && session.UserId == userId && session.IsActive(now)
                    && user is not null && user.Status == UserStatus.Active && user.SecurityStamp == securityStamp;
        if (valid)
        {
            session!.LastSeenAt = now;
            await store.SaveChangesAsync(ct);
            await sessionCache.MarkActiveAsync(store.Realm, store.TenantId, sessionId, TimeSpan.FromMinutes(5), ct);
        }
        else
        {
            await sessionCache.MarkRevokedAsync(store.Realm, store.TenantId, sessionId, ct);
        }

        return valid;
    }

    public async Task LogoutAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var session = await store.FindSessionAsync(sessionId, ct);
        if (session is not null && session.UserId == userId && session.RevokedAt is null)
        {
            session.RevokedAt = clock.UtcNow;
            session.RevokedReason = "logout";
        }

        var user = await store.FindByIdAsync(userId, ct);
        securityEvents.Add(SecurityEventTypes.Logout, true, userId, user?.Email, new { sessionId });
        await store.SaveChangesAsync(ct);
        await sessionCache.MarkRevokedAsync(store.Realm, store.TenantId, sessionId, ct);
    }

    public async Task<int> RevokeAllSessionsAsync(Guid userId, string reason, CancellationToken ct)
    {
        var sessions = await store.ActiveSessionsAsync(userId, ct);
        foreach (var s in sessions)
        {
            s.RevokedAt = clock.UtcNow;
            s.RevokedReason = reason;
        }

        if (sessions.Count > 0)
        {
            securityEvents.Add(SecurityEventTypes.SessionRevoked, true, userId, null, new { reason, count = sessions.Count });
        }

        await store.SaveChangesAsync(ct);
        foreach (var s in sessions)
        {
            await sessionCache.MarkRevokedAsync(store.Realm, store.TenantId, s.Id, ct);
        }

        return sessions.Count;
    }

    public async Task<Result> ChangePasswordAsync(Guid userId, Guid currentSessionId, string currentPassword, string newPassword, CancellationToken ct)
    {
        var user = await store.FindByIdAsync(userId, ct);
        if (user?.PasswordHash is null || Hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword ?? string.Empty) == PasswordVerificationResult.Failed)
        {
            return Error.Validation("invalid-current-password", "Current password is incorrect.");
        }

        var errors = passwordPolicy.Validate(newPassword, user.Email);
        if (errors.Count > 0)
        {
            return PolicyError(errors);
        }

        SetPassword(user, newPassword);
        securityEvents.Add(SecurityEventTypes.PasswordChanged, true, user.Id, user.Email);
        await store.SaveChangesAsync(ct);
        await RevokeOtherSessionsAsync(userId, currentSessionId, ct);
        return Result.Success();
    }

    /// <summary>Always succeeds from the caller's perspective (no account enumeration).</summary>
    public async Task ForgotPasswordAsync(string email, CancellationToken ct)
    {
        var normalized = Guard.NormalizeEmail(email ?? string.Empty);
        if (!await rateLimiter.TryAcquireAsync($"rl:forgot:{store.Realm}:{store.TenantId}:{AuditHashing.Sha256(normalized)}", 3, TimeSpan.FromMinutes(15), ct))
        {
            return;
        }

        var user = await store.FindByEmailAsync(normalized, ct);
        if (user is null || user.Status is UserStatus.Inactive or UserStatus.Suspended)
        {
            securityEvents.Add(SecurityEventTypes.PasswordResetRequested, false, user?.Id, normalized, new { reason = "no_eligible_account" });
            await store.SaveChangesAsync(ct);
            return;
        }

        var token = await IssueTokenAsync(user, TokenPurposes.PasswordReset, TimeSpan.FromMinutes(O.PasswordResetTokenMinutes), ct);
        securityEvents.Add(SecurityEventTypes.PasswordResetRequested, true, user.Id, user.Email);
        await store.SaveChangesAsync(ct);
        await emails.SendPasswordResetAsync(user, token, ct);
    }

    /// <summary>Issues an invitation token (new user) and sends the invitation email.</summary>
    public async Task SendInvitationAsync(IdentityUserBase user, CancellationToken ct)
    {
        var token = await IssueTokenAsync(user, TokenPurposes.Invitation, TimeSpan.FromHours(O.InvitationTokenHours), ct);
        await store.SaveChangesAsync(ct);
        await emails.SendInvitationAsync(user, token, ct);
    }

    /// <summary>Completes a password reset or an invitation (sets password, activates invited users, confirms email).</summary>
    public async Task<Result> ResetPasswordAsync(string token, string newPassword, CancellationToken ct)
    {
        var hash = SecureTokens.Hash(token ?? string.Empty);
        var record = await store.FindTokenAsync(TokenPurposes.PasswordReset, hash, ct)
                     ?? await store.FindTokenAsync(TokenPurposes.Invitation, hash, ct);
        var now = clock.UtcNow;
        if (record is null || record.UsedAt is not null || record.ExpiresAt <= now)
        {
            return Error.Validation("token-invalid", "This link is invalid or has expired.");
        }

        var user = await store.FindByIdAsync(record.UserId, ct);
        if (user is null || user.Status is UserStatus.Inactive or UserStatus.Suspended)
        {
            return Error.Validation("token-invalid", "This link is invalid or has expired.");
        }

        var errors = passwordPolicy.Validate(newPassword, user.Email);
        if (errors.Count > 0)
        {
            return PolicyError(errors);
        }

        record.UsedAt = now;
        SetPassword(user, newPassword);
        user.LockoutEndUtc = null;
        user.AccessFailedCount = 0;
        if (record.Purpose == TokenPurposes.Invitation)
        {
            user.EmailConfirmed = true;
            if (user.Status == UserStatus.Invited)
            {
                user.Status = UserStatus.Active;
                securityEvents.Add(SecurityEventTypes.UserActivated, true, user.Id, user.Email, new { via = "invitation" });
            }
        }

        securityEvents.Add(SecurityEventTypes.PasswordResetCompleted, true, user.Id, user.Email, new { purpose = record.Purpose });
        await store.SaveChangesAsync(ct);
        await RevokeAllSessionsAsync(user.Id, "password_reset", ct);
        return Result.Success();
    }

    public async Task SendEmailVerificationAsync(Guid userId, CancellationToken ct)
    {
        var user = await store.FindByIdAsync(userId, ct);
        if (user is null || user.EmailConfirmed)
        {
            return;
        }

        var token = await IssueTokenAsync(user, TokenPurposes.EmailVerification, TimeSpan.FromHours(O.EmailVerificationTokenHours), ct);
        await store.SaveChangesAsync(ct);
        await emails.SendEmailVerificationAsync(user, token, ct);
    }

    public async Task<Result> VerifyEmailAsync(string token, CancellationToken ct)
    {
        var record = await store.FindTokenAsync(TokenPurposes.EmailVerification, SecureTokens.Hash(token ?? string.Empty), ct);
        if (record is null || record.UsedAt is not null || record.ExpiresAt <= clock.UtcNow)
        {
            return Error.Validation("token-invalid", "This link is invalid or has expired.");
        }

        var user = await store.FindByIdAsync(record.UserId, ct);
        if (user is null)
        {
            return Error.Validation("token-invalid", "This link is invalid or has expired.");
        }

        record.UsedAt = clock.UtcNow;
        user.EmailConfirmed = true;
        securityEvents.Add(SecurityEventTypes.EmailVerified, true, user.Id, user.Email);
        await store.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<MfaSetup>> BeginMfaSetupAsync(Guid userId, CancellationToken ct)
    {
        var user = await store.FindByIdAsync(userId, ct);
        if (user is null)
        {
            return Error.NotFound("user-not-found", "User not found.");
        }

        if (user.MfaEnabled)
        {
            return Error.Conflict("mfa-already-enabled", "MFA is already enabled.");
        }

        var secret = totp.GenerateSecret();
        user.MfaSecretProtected = protector.Protect(MfaSecretPurpose, secret);
        await store.SaveChangesAsync(ct);
        return new MfaSetup(secret, totp.BuildProvisioningUri(O.MfaIssuer, user.Email, secret));
    }

    public async Task<Result> EnableMfaAsync(Guid userId, string code, CancellationToken ct)
    {
        var user = await store.FindByIdAsync(userId, ct);
        var secret = user?.MfaSecretProtected is null ? null : protector.Unprotect(MfaSecretPurpose, user.MfaSecretProtected);
        if (user is null || secret is null)
        {
            return Error.Validation("mfa-not-initialised", "Start MFA setup first.");
        }

        if (!totp.Verify(secret, code ?? string.Empty))
        {
            return Error.Validation("mfa-code-invalid", "Invalid verification code.");
        }

        user.MfaEnabled = true;
        securityEvents.Add(SecurityEventTypes.MfaEnabled, true, user.Id, user.Email);
        await store.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DisableMfaAsync(Guid userId, string password, CancellationToken ct)
    {
        var user = await store.FindByIdAsync(userId, ct);
        if (user?.PasswordHash is null || Hasher.VerifyHashedPassword(user, user.PasswordHash, password ?? string.Empty) == PasswordVerificationResult.Failed)
        {
            return Error.Validation("invalid-current-password", "Password is incorrect.");
        }

        user.MfaEnabled = false;
        user.MfaSecretProtected = null;
        user.RotateSecurityStamp();
        securityEvents.Add(SecurityEventTypes.MfaDisabled, true, user.Id, user.Email);
        await store.SaveChangesAsync(ct);
        return Result.Success();
    }

    private void SetPassword(IdentityUserBase user, string password)
    {
        user.PasswordHash = Hasher.HashPassword(user, password);
        user.PasswordChangedAt = clock.UtcNow;
        user.RotateSecurityStamp();
    }

    private async Task<string> IssueTokenAsync(IdentityUserBase user, string purpose, TimeSpan lifetime, CancellationToken ct)
    {
        foreach (var open in await store.OpenTokensAsync(user.Id, purpose, ct))
        {
            open.UsedAt = clock.UtcNow; // invalidate previous tokens of the same purpose
        }

        var token = SecureTokens.Create();
        var record = store.AddToken(user.Id, purpose);
        record.TokenHash = SecureTokens.Hash(token);
        record.CreatedAt = clock.UtcNow;
        record.ExpiresAt = clock.UtcNow.Add(lifetime);
        logger.LogInformation("Issued {Purpose} token for user {UserId}", purpose, user.Id);
        return token;
    }

    private async Task RevokeOtherSessionsAsync(Guid userId, Guid keep, CancellationToken ct)
    {
        var sessions = await store.ActiveSessionsAsync(userId, ct);
        foreach (var s in sessions.Where(s => s.Id != keep))
        {
            s.RevokedAt = clock.UtcNow;
            s.RevokedReason = "password_changed";
            await sessionCache.MarkRevokedAsync(store.Realm, store.TenantId, s.Id, ct);
        }

        await store.SaveChangesAsync(ct);
    }

    private static Error PolicyError(IReadOnlyList<string> errors) =>
        Error.Validation("password-policy", "Password does not meet the policy.", new Dictionary<string, string[]> { ["newPassword"] = errors.ToArray() });

    private static readonly string DummyHash = Hasher.HashPassword(null!, "dummy-password-for-timing");
}
