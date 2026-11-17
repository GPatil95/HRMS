using Hrms.Application.Abstractions;
using Hrms.Domain.Common;
using Hrms.Domain.Platform;
using Hrms.Domain.Tenant;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Identity;

/// <summary>Persistence for one identity realm (tenant DB or platform DB). Keeps AuthService realm-agnostic.</summary>
public interface IIdentityRealmStore
{
    string Realm { get; }
    Guid? TenantId { get; }
    Task<IdentityUserBase?> FindByEmailAsync(string normalizedEmail, CancellationToken ct);
    Task<IdentityUserBase?> FindByIdAsync(Guid id, CancellationToken ct);
    SessionBase AddSession(Guid userId);
    Task<SessionBase?> FindSessionAsync(Guid id, CancellationToken ct);
    Task<List<SessionBase>> ActiveSessionsAsync(Guid userId, CancellationToken ct);
    UserTokenBase AddToken(Guid userId, string purpose);
    Task<UserTokenBase?> FindTokenAsync(string purpose, string tokenHash, CancellationToken ct);
    Task<List<UserTokenBase>> OpenTokensAsync(Guid userId, string purpose, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

public sealed class TenantIdentityRealmStore(ITenantDbContext db, ITenantContextAccessor tenant) : IIdentityRealmStore
{
    public string Realm => Shared.Security.Realms.Tenant;
    public Guid? TenantId => tenant.Required.TenantId;

    public async Task<IdentityUserBase?> FindByEmailAsync(string normalizedEmail, CancellationToken ct) =>
        await db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

    public async Task<IdentityUserBase?> FindByIdAsync(Guid id, CancellationToken ct) => await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public SessionBase AddSession(Guid userId)
    {
        var s = new TenantSession { UserId = userId };
        db.Sessions.Add(s);
        return s;
    }

    public async Task<SessionBase?> FindSessionAsync(Guid id, CancellationToken ct) => await db.Sessions.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<List<SessionBase>> ActiveSessionsAsync(Guid userId, CancellationToken ct) =>
        [.. await db.Sessions.Where(s => s.UserId == userId && s.RevokedAt == null).ToListAsync(ct)];

    public UserTokenBase AddToken(Guid userId, string purpose)
    {
        var t = new TenantUserToken { UserId = userId, Purpose = purpose };
        db.UserTokens.Add(t);
        return t;
    }

    public async Task<UserTokenBase?> FindTokenAsync(string purpose, string tokenHash, CancellationToken ct) =>
        await db.UserTokens.FirstOrDefaultAsync(t => t.Purpose == purpose && t.TokenHash == tokenHash, ct);

    public async Task<List<UserTokenBase>> OpenTokensAsync(Guid userId, string purpose, CancellationToken ct) =>
        [.. await db.UserTokens.Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAt == null).ToListAsync(ct)];

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public sealed class PlatformIdentityRealmStore(IPlatformDbContext db) : IIdentityRealmStore
{
    public string Realm => Shared.Security.Realms.Platform;
    public Guid? TenantId => null;

    public async Task<IdentityUserBase?> FindByEmailAsync(string normalizedEmail, CancellationToken ct) =>
        await db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

    public async Task<IdentityUserBase?> FindByIdAsync(Guid id, CancellationToken ct) => await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public SessionBase AddSession(Guid userId)
    {
        var s = new PlatformSession { UserId = userId };
        db.Sessions.Add(s);
        return s;
    }

    public async Task<SessionBase?> FindSessionAsync(Guid id, CancellationToken ct) => await db.Sessions.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<List<SessionBase>> ActiveSessionsAsync(Guid userId, CancellationToken ct) =>
        [.. await db.Sessions.Where(s => s.UserId == userId && s.RevokedAt == null).ToListAsync(ct)];

    public UserTokenBase AddToken(Guid userId, string purpose)
    {
        var t = new PlatformUserToken { UserId = userId, Purpose = purpose };
        db.UserTokens.Add(t);
        return t;
    }

    public async Task<UserTokenBase?> FindTokenAsync(string purpose, string tokenHash, CancellationToken ct) =>
        await db.UserTokens.FirstOrDefaultAsync(t => t.Purpose == purpose && t.TokenHash == tokenHash, ct);

    public async Task<List<UserTokenBase>> OpenTokensAsync(Guid userId, string purpose, CancellationToken ct) =>
        [.. await db.UserTokens.Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAt == null).ToListAsync(ct)];

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

/// <summary>Sends realm-specific authentication emails (links point at the realm's host).</summary>
public interface IAuthEmailSender
{
    Task SendInvitationAsync(IdentityUserBase user, string token, CancellationToken ct);
    Task SendPasswordResetAsync(IdentityUserBase user, string token, CancellationToken ct);
    Task SendEmailVerificationAsync(IdentityUserBase user, string token, CancellationToken ct);
}
