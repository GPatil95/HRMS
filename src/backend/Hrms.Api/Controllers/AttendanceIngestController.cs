using System.Security.Cryptography;
using System.Text;
using Asp.Versioning;
using Hrms.Api.Hosting;
using Hrms.Api.Security;
using Hrms.Application.Attendance;
using Hrms.Application.Authorization;
using Hrms.Application.Modules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hrms.Api.Controllers;

/// <summary>
/// M3.2 punch ingestion: device management (user session), device push (Device token),
/// self/manual punches, CSV import, raw punch viewer and ingest-batch monitoring.
/// </summary>
[ApiController, ApiVersion("1.0"), Route("api/v{version:apiVersion}/attendance")]
public sealed class AttendanceIngestController(DeviceService devices, PunchIngestService ingest) : ControllerBase
{
    private const string M = AttendanceModule.ModuleCode;

    // ---------- Devices (user session) ----------
    [HttpGet("devices"), RequirePermission(M, AttendancePermissions.DevicesManage)]
    public async Task<IActionResult> ListDevices([FromQuery] Guid? branchId, [FromQuery] string? protocol,
        [FromQuery] bool? active, CancellationToken ct) => Ok(await devices.ListAsync(branchId, protocol, active, ct));

    [HttpPost("devices"), RequirePermission(M, AttendancePermissions.DevicesManage)]
    public async Task<IActionResult> CreateDevice(DeviceInput input, CancellationToken ct) =>
        (await devices.CreateAsync(input, ct)).ToActionResult(v => Ok(v));

    [HttpPut("devices/{id:guid}"), RequirePermission(M, AttendancePermissions.DevicesManage)]
    public async Task<IActionResult> UpdateDevice(Guid id, DeviceInput input, CancellationToken ct) =>
        (await devices.UpdateAsync(id, input, ct)).ToActionResult();

    [HttpPost("devices/{id:guid}/rotate-token"), RequirePermission(M, AttendancePermissions.DevicesManage)]
    public async Task<IActionResult> RotateToken(Guid id, CancellationToken ct) =>
        (await devices.RotateTokenAsync(id, ct)).ToActionResult(v => Ok(v));

    [HttpGet("devices/{id:guid}/user-map"), RequirePermission(M, AttendancePermissions.DevicesManage)]
    public async Task<IActionResult> ListMaps(Guid id, CancellationToken ct) =>
        (await devices.ListMapAsync(id, ct)).ToActionResult(v => Ok(v));

    [HttpPost("devices/{id:guid}/user-map"), RequirePermission(M, AttendancePermissions.DevicesManage)]
    public async Task<IActionResult> UpsertMap(Guid id, DeviceUserMapInput input, CancellationToken ct) =>
        (await devices.UpsertMapAsync(id, input, ct)).ToActionResult(v => Ok(v));

    [HttpDelete("devices/{id:guid}/user-map/{mapId:guid}"), RequirePermission(M, AttendancePermissions.DevicesManage)]
    public async Task<IActionResult> DeleteMap(Guid id, Guid mapId, CancellationToken ct) =>
        (await devices.DeleteMapAsync(id, mapId, ct)).ToActionResult();

    // ---------- Device push (device token; no user session) ----------
    /// <summary>Bulk punch push from a biometric device/gateway. Authentication:
    /// 'Authorization: Device &lt;one-time-provisioned-token&gt;' resolved inside the tenant host —
    /// a token from another tenant can never match here.</summary>
    [HttpPost("devices/{code}/punches"), AllowAnonymous] // device-token auth in-body, no session
    public async Task<IActionResult> DevicePush(string code, CancellationToken ct)
    {
        var (device, error) = await ingest.AuthenticateDeviceAsync(Request.Headers.Authorization.ToString(), ct);
        if (error is not null || device is null || !device.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
        {
            return ApiResults.Problem(error ?? Shared.Results.Error.Unauthorized("device.auth", "Device token required."));
        }

        // Bound the body before buffering/deserializing — a device credential must not be able to
        // allocate arbitrary memory. 1 MB comfortably holds the 2000-item batch limit (~200 B/row).
        const long MaxPushBytes = 1_000_000;
        if (Request.ContentLength > MaxPushBytes)
        {
            return ApiResults.Problem(Shared.Results.Error.Validation("ingest.too_large", "Payload exceeds 1 MB; split the batch."));
        }

        Request.EnableBuffering();
        string body;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            body = await reader.ReadToEndAsync(ct);
            Request.Body.Position = 0;
        }
        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var req = System.Text.Json.JsonSerializer.Deserialize<DevicePushRequest>(body,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new DevicePushRequest(null);
        var idempotencyKey = Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return ApiResults.Problem(Shared.Results.Error.Validation("ingest.idempotency", "Idempotency-Key header is required."));
        }

        return (await ingest.IngestDevicePushAsync(device, idempotencyKey.Trim(), req, payloadHash, ct))
            .ToActionResult(v => Ok(v));
    }

    // ---------- Self / manual punches ----------
    [HttpPost("punches/self"), RequirePermission(M, AttendancePermissions.MyPunch)]
    public async Task<IActionResult> SelfPunch(SelfPunchInput input, CancellationToken ct) =>
        (await ingest.SelfPunchAsync(input, ct)).ToActionResult(v => Ok(v));

    [HttpPost("punches/manual"), RequirePermission(M, AttendancePermissions.PunchesManual)]
    public async Task<IActionResult> ManualPunch(ManualPunchInput input, CancellationToken ct) =>
        (await ingest.ManualPunchAsync(input, ct)).ToActionResult(v => Ok(v));

    [HttpPost("punches/{id:guid}/void"), RequirePermission(M, AttendancePermissions.PunchesManual)]
    public async Task<IActionResult> VoidPunch(Guid id, VoidPunchInput input, CancellationToken ct) =>
        (await ingest.VoidPunchAsync(id, input.Reason, ct)).ToActionResult();

    [HttpPost("punches/import"), RequirePermission(M, AttendancePermissions.PunchesImport)]
    public async Task<IActionResult> Import(PunchImportInput input, CancellationToken ct) =>
        (await ingest.ImportCsvAsync(input, ct)).ToActionResult(v => Ok(v));

    // ---------- Raw punch viewer ----------
    [HttpGet("punches"), RequirePermission(M, AttendancePermissions.View)]
    public async Task<IActionResult> Punches([FromQuery] Guid? employeeId, [FromQuery] Guid? deviceId,
        [FromQuery] string? source, [FromQuery] string? direction, [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to, [FromQuery] bool? unmappedOnly, [FromQuery] bool? includeVoided,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        Ok(await ingest.QueryAsync(new PunchQuery(employeeId, deviceId, source, direction, from, to,
            unmappedOnly, includeVoided, page, pageSize), ct));

    /// <summary>Own punches — any signed-in employee; manager/admin scope not required.</summary>
    [HttpGet("punches/my"), RequirePermission(M, AttendancePermissions.MyView)]
    public async Task<IActionResult> MyPunches([FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        Ok(await ingest.MyPunchesAsync(from, to, page, pageSize, ct));

    // ---------- Ingest batch monitoring ----------
    [HttpGet("ingest-batches"), RequirePermission(M, AttendancePermissions.View)]
    public async Task<IActionResult> Batches([FromQuery] string? source, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        Ok(await ingest.ListBatchesAsync(source, page, pageSize, ct));
}
