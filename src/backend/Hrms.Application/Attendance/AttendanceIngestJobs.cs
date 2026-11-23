using Hrms.Application.Abstractions;
using Hrms.Application.Jobs;
using Hrms.Application.Notifications;
using Hrms.Domain.Platform;
using Hrms.Domain.Tenant;
using Hrms.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hrms.Application.Attendance;

/// <summary>Tenant job: back-fills employee_id on unmapped punches for one device after a
/// device_user_map change. Payload: device id (Guid). Idempotent — only NULL→populated updates
/// are performed, which is also the only UPDATE the append-only trigger permits.</summary>
[JobHandler(JobTypes.AttendanceDeviceMap, true)]
public sealed class AttendanceDeviceMapJob(PunchIngestService ingest, ILogger<AttendanceDeviceMapJob> logger) : IJobHandler
{
    public string JobType => JobTypes.AttendanceDeviceMap;
    public bool TenantScoped => true;

    public async Task ExecuteAsync(string? payload, CancellationToken ct)
    {
        if (!Guid.TryParse(payload, out var deviceId))
        {
            logger.LogWarning("attendance.device.map: invalid payload '{Payload}'", payload);
            return;
        }
        var filled = await ingest.BackfillUnmappedAsync(deviceId, ct);
        logger.LogInformation("attendance.device.map: back-filled {Count} punches for device {DeviceId}", filled, deviceId);
    }
}

/// <summary>Platform sweep: enqueues the per-tenant offline-device check for every active tenant.</summary>
[JobHandler(JobTypes.AttendanceDeviceOfflineSweep, false)]
public sealed class AttendanceDeviceOfflineSweepJob(IPlatformDbContext platform, IJobScheduler jobs) : IJobHandler
{
    public string JobType => JobTypes.AttendanceDeviceOfflineSweep;
    public bool TenantScoped => false;

    public async Task ExecuteAsync(string? payload, CancellationToken ct)
    {
        var tenants = await platform.Tenants.AsNoTracking()
            .Where(t => t.Status == TenantStatus.Active)
            .Select(t => t.Id)
            .ToListAsync(ct);
        foreach (var t in tenants)
        {
            jobs.EnqueueTenant(t, JobTypes.AttendanceDeviceOfflineCheck);
        }
    }
}

/// <summary>Tenant job (hourly via sweep): notifies device managers about active devices that
/// have not been seen for over 24 hours (or never reported). Notifications are per-device and
/// self-deduplicating via the link in the body.</summary>
[JobHandler(JobTypes.AttendanceDeviceOfflineCheck, true)]
public sealed class AttendanceDeviceOfflineCheckJob(ITenantDbContext db, INotificationService notify,
    IClock clock, ILogger<AttendanceDeviceOfflineCheckJob> logger) : IJobHandler
{
    public string JobType => JobTypes.AttendanceDeviceOfflineCheck;
    public bool TenantScoped => true;
    private static readonly TimeSpan OfflineAfter = TimeSpan.FromHours(24);

    public async Task ExecuteAsync(string? payload, CancellationToken ct)
    {
        var cutoff = clock.UtcNow - OfflineAfter;
        var stale = await db.AttendanceDevices.AsNoTracking()
            .Where(d => d.IsActive && d.Protocol == DeviceProtocols.PushApi &&
                        (d.LastSeenAt == null || d.LastSeenAt < cutoff))
            .ToListAsync(ct);
        if (stale.Count == 0)
        {
            return;
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.DateTime);
        var managerIds = await (from rp in db.RolePermissions.AsNoTracking()
                                join p in db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
                                join ra in db.RoleAssignments.AsNoTracking() on rp.RoleId equals ra.RoleId
                                where p.Code == Modules.AttendancePermissions.DevicesManage &&
                                      ra.ValidFrom <= today && (ra.ValidTo == null || ra.ValidTo >= today)
                                select ra.UserId).Distinct().ToListAsync(ct);
        foreach (var device in stale)
        {
            var lastSeen = device.LastSeenAt?.ToString("u") ?? "never";
            foreach (var uid in managerIds)
            {
                await notify.NotifyAsync(new NotifyCommand(uid, "attendance.device.offline",
                    $"Attendance device '{device.Code}' offline",
                    $"Device {device.Code} ({device.Name}) last reported {lastSeen}. Check connectivity and clock sync.",
                    "/attendance/devices"), ct);
            }
            logger.LogInformation("attendance.devices.offline: device {DeviceCode} last seen {LastSeen}", device.Code, lastSeen);
        }
    }
}
