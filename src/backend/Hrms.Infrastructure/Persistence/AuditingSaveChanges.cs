using System.Reflection;
using System.Text.Json;
using Hrms.Application.Abstractions;
using Hrms.Application.Auditing;
using Hrms.Domain.Common;
using Hrms.Shared.Auditing;
using Hrms.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Hrms.Infrastructure.Persistence;

/// <summary>Ambient services for stamping/auditing. Null members (design-time, tooling) fall back to "system".</summary>
public sealed record AuditRuntime(ICurrentUser? User, IRequestContext? Request, IAuditContext? Audit, ITenantContextAccessor? Tenant, IClock Clock);

/// <summary>
/// Applied on every SaveChanges: stamps Created/Modified, writes entity-change audit rows for [Audited] entities in the same
/// transaction (before/after/changed fields, ignoring [AuditIgnore] and masking [AuditMasked]), hashes new audit/security
/// rows, and rejects any modification or deletion of audit records.
/// </summary>
internal static class AuditingSaveChanges
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Masked = "***";

    public static void Apply(DbContext ctx, AuditRuntime rt)
    {
        var now = rt.Clock.UtcNow;
        var userId = rt.User?.UserId;
        var entries = ctx.ChangeTracker.Entries().ToList();

        foreach (var e in entries)
        {
            if (e.Entity is AuditEntry or SecurityEvent or AuditSeal && e.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("Audit records are append-only.");
            }

            if (e.Entity is IAuditableEntity a)
            {
                if (e.State == EntityState.Added)
                {
                    if (a.CreatedAt == default)
                    {
                        a.CreatedAt = now;
                    }

                    a.CreatedBy ??= userId;
                }
                else if (e.State == EntityState.Modified)
                {
                    a.ModifiedAt = now;
                    a.ModifiedBy = userId;
                    e.Property(nameof(IAuditableEntity.CreatedAt)).IsModified = false;
                    e.Property(nameof(IAuditableEntity.CreatedBy)).IsModified = false;
                }
            }
        }

        foreach (var e in entries.Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var attr = e.Entity.GetType().GetCustomAttribute<AuditedAttribute>();
            if (attr is not null)
            {
                var entry = BuildEntry(e, attr.ModuleCode, rt, now);
                if (entry is not null)
                {
                    ctx.Set<AuditEntry>().Add(entry);
                }
            }
        }

        foreach (var e in ctx.ChangeTracker.Entries().Where(x => x.State == EntityState.Added))
        {
            switch (e.Entity)
            {
                case AuditEntry ae when string.IsNullOrEmpty(ae.ContentHash):
                    ae.ContentHash = AuditHashing.ComputeHash(ae);
                    break;
                case SecurityEvent se when string.IsNullOrEmpty(se.ContentHash):
                    se.ContentHash = AuditHashing.ComputeHash(se);
                    break;
            }
        }
    }

    private static AuditEntry? BuildEntry(EntityEntry e, string moduleCode, AuditRuntime rt, DateTimeOffset now)
    {
        var before = new Dictionary<string, object?>();
        var after = new Dictionary<string, object?>();
        var changed = new List<string>();
        foreach (var p in e.Properties)
        {
            var clr = p.Metadata.PropertyInfo;
            if (clr is null || clr.GetCustomAttribute<AuditIgnoreAttribute>() is not null || p.Metadata.Name is "RowVersion"
                || p.Metadata.Name is nameof(IAuditableEntity.CreatedAt) or nameof(IAuditableEntity.CreatedBy) or nameof(IAuditableEntity.ModifiedAt) or nameof(IAuditableEntity.ModifiedBy))
            {
                continue;
            }

            var masked = clr.GetCustomAttribute<AuditMaskedAttribute>() is not null;
            switch (e.State)
            {
                case EntityState.Added:
                    after[p.Metadata.Name] = masked ? Masked : p.CurrentValue;
                    break;
                case EntityState.Deleted:
                    before[p.Metadata.Name] = masked ? Masked : p.OriginalValue;
                    break;
                case EntityState.Modified when p.IsModified && !Equals(p.OriginalValue, p.CurrentValue):
                    changed.Add(p.Metadata.Name);
                    before[p.Metadata.Name] = masked ? Masked : p.OriginalValue;
                    after[p.Metadata.Name] = masked ? Masked : p.CurrentValue;
                    break;
            }
        }

        if (e.State == EntityState.Modified && changed.Count == 0)
        {
            return null;
        }

        var key = e.Metadata.FindPrimaryKey()!.Properties.Select(k => e.Property(k.Name).CurrentValue?.ToString()).ToArray();
        return new AuditEntry
        {
            OccurredAt = now,
            TenantId = rt.Tenant?.Current?.TenantId,
            ModuleCode = moduleCode,
            EntityType = e.Metadata.ClrType.Name,
            EntityId = string.Join(":", key),
            Action = e.State switch { EntityState.Added => AuditActions.Create, EntityState.Deleted => AuditActions.Delete, _ => AuditActions.Update },
            ActorType = rt.User?.ActorType ?? ActorTypes.Service,
            ActorId = rt.User?.UserId,
            ActorDisplay = rt.User?.DisplayName,
            Changes = changed.Count > 0 ? JsonSerializer.Serialize(changed, Json) : null,
            Before = before.Count > 0 ? JsonSerializer.Serialize(before, Json) : null,
            After = after.Count > 0 ? JsonSerializer.Serialize(after, Json) : null,
            Source = rt.Request?.Source ?? AuditSources.System,
            IpAddress = rt.Request?.IpAddress,
            UserAgent = rt.Request?.UserAgent,
            CorrelationId = rt.Request?.CorrelationId,
            Reason = rt.Audit?.Reason,
        };
    }
}
