using System.Text.Json;
using Hrms.Application.Abstractions;
using Hrms.Domain.Common;
using Hrms.Shared.Auditing;
using Hrms.Shared.Primitives;
using Microsoft.Extensions.DependencyInjection;

namespace Hrms.Application.Auditing;

public interface ISecurityEventWriter
{
    /// <summary>Adds a security event to the current realm's database context. Caller saves (same transaction).</summary>
    void Add(string eventType, bool success, Guid? userId, string? email, object? details = null);
}

public interface IAuditWriter
{
    /// <summary>Records an explicit business action (download, export, approve...) in the current realm's audit trail.</summary>
    void Add(string moduleCode, string entityType, string entityId, string action, object? details = null);
}

/// <summary>Routes audit writes to the tenant DB when a tenant context exists, otherwise to the platform DB.</summary>
public sealed class RealmAuditWriter(
    IServiceProvider services,
    ITenantContextAccessor tenant,
    ICurrentUser user,
    IRequestContext request,
    IAuditContext auditContext,
    IClock clock) : ISecurityEventWriter, IAuditWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Add(string eventType, bool success, Guid? userId, string? email, object? details = null)
    {
        var e = new SecurityEvent
        {
            OccurredAt = clock.UtcNow,
            TenantId = tenant.Current?.TenantId,
            EventType = eventType,
            Success = success,
            UserId = userId,
            UserEmail = email is { Length: > 256 } ? email[..256] : email,
            ActorId = user.UserId,
            IpAddress = request.IpAddress,
            UserAgent = request.UserAgent,
            CorrelationId = request.CorrelationId,
            Details = details is null ? null : JsonSerializer.Serialize(details, Json),
        };

        if (tenant.Current is not null)
        {
            services.GetRequiredService<ITenantDbContext>().SecurityEvents.Add(e);
        }
        else
        {
            services.GetRequiredService<IPlatformDbContext>().SecurityEvents.Add(e);
        }
    }

    public void Add(string moduleCode, string entityType, string entityId, string action, object? details = null)
    {
        var entry = new AuditEntry
        {
            OccurredAt = clock.UtcNow,
            TenantId = tenant.Current?.TenantId,
            ModuleCode = moduleCode,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            ActorType = user.ActorType,
            ActorId = user.UserId,
            ActorDisplay = user.DisplayName,
            After = details is null ? null : JsonSerializer.Serialize(details, Json),
            Source = request.Source,
            IpAddress = request.IpAddress,
            UserAgent = request.UserAgent,
            CorrelationId = request.CorrelationId,
            Reason = auditContext.Reason,
        };

        if (tenant.Current is not null)
        {
            services.GetRequiredService<ITenantDbContext>().AuditEntries.Add(entry);
        }
        else
        {
            services.GetRequiredService<IPlatformDbContext>().AuditEntries.Add(entry);
        }
    }
}

public static class AuditHashing
{
    public static string ComputeHash(AuditEntry e) => Sha256(string.Join('|',
        e.Id, e.OccurredAt.ToUnixTimeMilliseconds(), e.TenantId, e.ModuleCode, e.EntityType, e.EntityId, e.Action,
        e.ActorType, e.ActorId, e.Changes, e.Before, e.After, e.Source, e.IpAddress, e.CorrelationId, e.Reason));

    public static string ComputeHash(SecurityEvent e) => Sha256(string.Join('|',
        e.Id, e.OccurredAt.ToUnixTimeMilliseconds(), e.TenantId, e.EventType, e.Success, e.UserId, e.UserEmail,
        e.ActorId, e.IpAddress, e.CorrelationId, e.Details));

    public static string Chain(string previous, IEnumerable<string> hashes)
    {
        var current = previous;
        foreach (var h in hashes)
        {
            current = Sha256(current + h);
        }

        return current;
    }

    public static string Sha256(string value) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
