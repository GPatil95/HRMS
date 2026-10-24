using Hrms.Application.Abstractions;
using Hrms.Application.Jobs;
using Hrms.Domain.Common;
using Hrms.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Auditing;

/// <summary>Seals audit entries since the last seal into a hash chain (tamper evidence). Each realm has its own chain.</summary>
public static class AuditSealer
{
    public static async Task<AuditSeal?> SealAsync(DbSet<AuditEntry> entries, DbSet<AuditSeal> seals, Func<CancellationToken, Task<int>> save, IClock clock, CancellationToken ct)
    {
        var last = await seals.OrderByDescending(s => s.RangeEnd).FirstOrDefaultAsync(ct);
        var from = last?.RangeEnd ?? DateTimeOffset.MinValue;
        var to = clock.UtcNow;
        var hashes = await entries.AsNoTracking().Where(e => e.OccurredAt > from && e.OccurredAt <= to).OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).Select(e => e.ContentHash).ToListAsync(ct);
        if (hashes.Count == 0)
        {
            return null;
        }

        var previous = last?.ChainHash ?? string.Empty;
        var seal = new AuditSeal { SealedAt = to, RangeStart = from == DateTimeOffset.MinValue ? to : from, RangeEnd = to, EntryCount = hashes.Count, PreviousSealHash = previous, ChainHash = AuditHashing.Chain(previous, hashes) };
        seals.Add(seal);
        await save(ct);
        return seal;
    }
}

public sealed class TenantAuditSealJob(ITenantDbContext db, IClock clock) : IJobHandler
{
    public string JobType => JobTypes.TenantAuditSeal;
    public bool TenantScoped => true;
    public Task ExecuteAsync(string? payload, CancellationToken ct) => AuditSealer.SealAsync(db.AuditEntries, db.AuditSeals, db.SaveChangesAsync, clock, ct);
}

public sealed class PlatformAuditSealJob(IPlatformDbContext db, IClock clock) : IJobHandler
{
    public string JobType => JobTypes.PlatformAuditSeal;
    public bool TenantScoped => false;
    public Task ExecuteAsync(string? payload, CancellationToken ct) => AuditSealer.SealAsync(db.AuditEntries, db.AuditSeals, db.SaveChangesAsync, clock, ct);
}
