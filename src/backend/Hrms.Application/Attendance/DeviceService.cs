using System.Security.Cryptography;
using System.Text;
using Hrms.Application.Abstractions;
using Hrms.Application.Jobs;
using Hrms.Domain.Tenant;
using Hrms.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Attendance;

/// <summary>Attendance device master + employee↔device-code mappings (M3.2). Device bearer
/// tokens are generated server-side, stored only as SHA-256 hex, and returned exactly once.</summary>
public sealed class DeviceService(ITenantDbContext db, ITenantContextAccessor tenant, IJobScheduler jobs)
{
    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string NewToken() => "dev_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    public async Task<IReadOnlyList<DeviceDto>> ListAsync(Guid? branchId, string? protocol, bool? active, CancellationToken ct)
    {
        var q = db.AttendanceDevices.AsNoTracking().AsQueryable();
        if (branchId is { } b) q = q.Where(d => d.BranchId == b);
        if (protocol is { Length: > 0 } p) q = q.Where(d => d.Protocol == p);
        if (active is { } a) q = q.Where(d => d.IsActive == a);
        var rows = await q.OrderBy(d => d.Code).ToListAsync(ct);
        var names = await db.Branches.AsNoTracking().Where(b => rows.Select(d => d.BranchId).Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        return rows.Select(d => d.ToDto(d.BranchId is { } bid && names.TryGetValue(bid, out var n) ? n : null)).ToList();
    }

    public async Task<Result<DeviceCreatedDto>> CreateAsync(DeviceInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Code) || input.Code.Length > 64)
            return Error.Validation("device.code", "Code is required (max 64 chars).");
        var validation = await ValidateShapeAsync(input, ct);
        if (validation is not null) return validation;
        var code = input.Code.Trim().ToUpperInvariant();
        if (await db.AttendanceDevices.AnyAsync(d => d.Code == code, ct))
            return Error.Conflict("device.duplicate_code", $"Device code '{code}' already exists.");
        var token = NewToken();
        var device = new AttendanceDevice
        {
            Code = code, Name = input.Name?.Trim() ?? code, Vendor = input.Vendor?.Trim(),
            BranchId = input.BranchId, Protocol = input.Protocol ?? DeviceProtocols.PushApi,
            TimezoneMode = input.TimezoneMode ?? DeviceTimezoneModes.Utc,
            DeviceTimezone = input.DeviceTimezone?.Trim(),
            TokenHash = HashToken(token), IsActive = input.IsActive ?? true,
        };
        db.AttendanceDevices.Add(device);
        await db.SaveChangesAsync(ct);
        return new DeviceCreatedDto(device.ToDto(), token);
    }

    public async Task<Result> UpdateAsync(Guid id, DeviceInput input, CancellationToken ct)
    {
        var device = await db.AttendanceDevices.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is null) return Error.NotFound("device.not_found", "Device not found.");
        var validation = await ValidateShapeAsync(input, ct);
        if (validation is not null) return validation;
        if (input.Name is { Length: > 0 } name) device.Name = name.Trim();
        if (input.Vendor is not null) device.Vendor = input.Vendor.Trim();
        if (input.BranchId is { } b) device.BranchId = b;
        if (input.Protocol is { Length: > 0 } p) device.Protocol = p;
        if (input.TimezoneMode is { Length: > 0 } m) device.TimezoneMode = m;
        if (input.DeviceTimezone is not null) device.DeviceTimezone = input.DeviceTimezone.Trim();
        if (input.IsActive is { } active) device.IsActive = active;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Re-issues the device bearer token; the previous hash is invalidated immediately.</summary>
    public async Task<Result<DeviceCreatedDto>> RotateTokenAsync(Guid id, CancellationToken ct)
    {
        var device = await db.AttendanceDevices.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is null) return Error.NotFound("device.not_found", "Device not found.");
        var token = NewToken();
        device.TokenHash = HashToken(token);
        await db.SaveChangesAsync(ct);
        return new DeviceCreatedDto(device.ToDto(), token);
    }

    private async Task<Error?> ValidateShapeAsync(DeviceInput input, CancellationToken ct)
    {
        var protocol = input.Protocol ?? DeviceProtocols.PushApi;
        if (!DeviceProtocols.All.Contains(protocol))
            return Error.Validation("device.protocol", "Protocol must be push_api, pull_poll or file_import.");
        var tzMode = input.TimezoneMode ?? DeviceTimezoneModes.Utc;
        if (!DeviceTimezoneModes.All.Contains(tzMode))
            return Error.Validation("device.tz_mode", "Timezone mode must be utc or device_local.");
        if (tzMode == DeviceTimezoneModes.DeviceLocal)
        {
            if (string.IsNullOrWhiteSpace(input.DeviceTimezone))
                return Error.Validation("device.tz_required", "device_timezone is required when timezone_mode is device_local.");
            try { _ = PunchIngestService.ResolveZone(input.DeviceTimezone); }
            catch (Exception) { return Error.Validation("device.tz_invalid", $"Unknown timezone '{input.DeviceTimezone}'."); }
        }
        if (input.BranchId is { } b && !await db.Branches.AnyAsync(x => x.Id == b, ct))
            return Error.Validation("device.branch", "Unknown branch.");
        return null;
    }

    // ---------- User map ----------

    public async Task<Result<IReadOnlyList<DeviceUserMapDto>>> ListMapAsync(Guid deviceId, CancellationToken ct)
    {
        if (!await db.AttendanceDevices.AnyAsync(d => d.Id == deviceId, ct))
            return Error.NotFound("device.not_found", "Device not found.");
        var rows = await db.DeviceUserMaps.AsNoTracking().Where(m => m.DeviceId == deviceId)
            .OrderBy(m => m.DeviceUserCode).ThenBy(m => m.EffectiveFrom).ToListAsync(ct);
        var emps = await db.Employees.AsNoTracking()
            .Where(e => rows.Select(m => m.EmployeeId).Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => new { e.EmployeeCode, e.DisplayName }, ct);
        return rows.Select(m =>
        {
            emps.TryGetValue(m.EmployeeId, out var e);
            return m.ToDto(e?.EmployeeCode ?? "?", e?.DisplayName ?? "(removed)");
        }).ToList();
    }

    /// <summary>Creates or updates one mapping. Overlap is DB-enforced; on success the
    /// attendance.device.map job back-fills unmapped punches for this device+code.</summary>
    public async Task<Result<DeviceUserMapDto>> UpsertMapAsync(Guid deviceId, DeviceUserMapInput input, CancellationToken ct)
    {
        if (!await db.AttendanceDevices.AnyAsync(d => d.Id == deviceId, ct))
            return Error.NotFound("device.not_found", "Device not found.");
        if (string.IsNullOrWhiteSpace(input.DeviceUserCode) || input.DeviceUserCode.Length > 64)
            return Error.Validation("map.code", "Device user code is required (max 64 chars).");
        if (input.EmployeeId is not { } empId)
            return Error.Validation("map.employee", "employee_id is required.");
        var emp = await db.Employees.AsNoTracking().Where(e => e.Id == empId)
            .Select(e => new { e.Id, e.EmployeeCode, e.DisplayName }).FirstOrDefaultAsync(ct);
        if (emp is null) return Error.Validation("map.employee", "Unknown employee.");
        if (input.EffectiveTo is { } to && to < input.EffectiveFrom)
            return Error.Validation("map.window", "effective_to cannot precede effective_from.");

        var map = new DeviceUserMap
        {
            DeviceId = deviceId, DeviceUserCode = input.DeviceUserCode.Trim(),
            EmployeeId = empId, EffectiveFrom = input.EffectiveFrom, EffectiveTo = input.EffectiveTo,
        };
        db.DeviceUserMaps.Add(map);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsOverlapOrUnique(ex))
        {
            return Error.Conflict("map.overlap", "An overlapping mapping already exists for this device user code.");
        }

        jobs.EnqueueTenant(tenant.Required.TenantId, JobTypes.AttendanceDeviceMap, deviceId.ToString());
        return map.ToDto(emp.EmployeeCode, emp.DisplayName);
    }

    public async Task<Result> DeleteMapAsync(Guid deviceId, Guid mapId, CancellationToken ct)
    {
        var map = await db.DeviceUserMaps.FirstOrDefaultAsync(m => m.DeviceId == deviceId && m.Id == mapId, ct);
        if (map is null) return Error.NotFound("map.not_found", "Mapping not found.");
        db.DeviceUserMaps.Remove(map);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    internal static bool IsOverlapOrUnique(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException pg &&
        (pg.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation ||
         pg.SqlState == Npgsql.PostgresErrorCodes.ExclusionViolation);
}
