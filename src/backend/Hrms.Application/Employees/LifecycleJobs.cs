using Hrms.Application.Abstractions;
using Hrms.Application.Jobs;
using Hrms.Domain.Platform;
using Hrms.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hrms.Application.Employees;

/// <summary>Tenant job (daily): applies all approved lifecycle events whose effective date has arrived,
/// then marks expired employee documents. Payload: optional ISO 'today' resolved in the tenant's timezone
/// by the sweep; defaults to UTC today.</summary>
[JobHandler(JobTypes.EmployeeLifecycleActivate, true)]
public sealed class EmployeeLifecycleActivateJob(LifecycleEventService lifecycle, EmployeeDocumentService documents, SalaryService salary) : IJobHandler
{
    public string JobType => JobTypes.EmployeeLifecycleActivate;
    public bool TenantScoped => true;

    public async Task ExecuteAsync(string? payload, CancellationToken ct)
    {
        var today = payload is { Length: > 0 } && DateOnly.TryParse(payload, out var d)
            ? d
            : DateOnly.FromDateTime(DateTime.UtcNow.Date);
        await lifecycle.ActivateDueEventsAsync(today, ct);
        await documents.ExpireDueDocumentsAsync(today, ct);
        await salary.ActivateDueAssignmentsAsync(today, ct);
    }
}

/// <summary>Platform sweep: for every active tenant, resolves 'today' in the tenant's timezone and
/// enqueues the per-tenant activation job. Triggered hourly by the recurring sweep worker.</summary>
[JobHandler(JobTypes.EmployeeLifecycleSweep, false)]
public sealed class EmployeeLifecycleSweepJob(IPlatformDbContext platform, IJobScheduler jobs, IClock clock, ILogger<EmployeeLifecycleSweepJob> logger) : IJobHandler
{
    public string JobType => JobTypes.EmployeeLifecycleSweep;
    public bool TenantScoped => false;

    public async Task ExecuteAsync(string? payload, CancellationToken ct)
    {
        var tenants = await platform.Tenants.AsNoTracking()
            .Where(t => t.Status == TenantStatus.Active)
            .Select(t => new { t.Id, t.TimeZone })
            .ToListAsync(ct);

        foreach (var t in tenants)
        {
            var today = TodayIn(t.TimeZone);
            jobs.EnqueueTenant(t.Id, JobTypes.EmployeeLifecycleActivate, today.ToString("yyyy-MM-dd"));
            logger.LogDebug("Lifecycle activation sweep queued for tenant {TenantId} (today {Today})", t.Id, today);
        }
    }

    private DateOnly TodayIn(string? tzName)
    {
        try
        {
            var tz = tzName is { Length: > 0 } ? TimeZoneInfo.FindSystemTimeZoneById(tzName) : TimeZoneInfo.Utc;
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, tz).DateTime);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return DateOnly.FromDateTime(clock.UtcNow.DateTime);
        }
    }
}
