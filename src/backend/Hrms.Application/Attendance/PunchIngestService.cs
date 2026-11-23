using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hrms.Application.Abstractions;
using Hrms.Application.Authorization;
using Hrms.Domain.Tenant;
using Hrms.Shared.Paging;
using Hrms.Shared.Primitives;
using Hrms.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Attendance;

/// <summary>
/// Raw punch ingestion + viewer (M3.2). Two-layer idempotency: per-punch content dedup
/// (<see cref="Punch.DedupKey"/>, DB-unique) and per-call batch idempotency
/// (<see cref="IngestBatch.IdempotencyKey"/>, DB-unique). Punches are append-only; employee_id
/// starts NULL for unmapped device codes and is back-filled by the attendance.device.map job.
/// </summary>
public sealed class PunchIngestService(ITenantDbContext db, IClock clock, IDataScopeService scopes,
    ICurrentUser user, IRequestContext request, IFileStorageService storage,
    ITenantContextAccessor tenant, IEntitlementService entitlements)
{
    private const int MaxRejectionsStored = 25;
    private static readonly TimeSpan SelfSkewLimit = TimeSpan.FromHours(24);

    // ---------- Timestamp + dedup helpers (unit-tested) ----------

    public static TimeZoneInfo ResolveZone(string? name) =>
        name is { Length: > 0 } ? TimeZoneInfo.FindSystemTimeZoneById(name) : TimeZoneInfo.Utc;

    /// <summary>Canonical dedup key over the physical event identity: device|subject|punchedAt-to-second
    /// for device rows (the channel is ignored so CSV offline-sync dedups against push data), or
    /// source|subject|punchedAt for non-device rows. The subject is the device user code
    /// (device/import rows) or employee id (self/manual rows), so a later employee back-fill can
    /// never change whether a punch is a duplicate.</summary>
    public static string BuildDedupKey(string source, Guid? deviceId, string subject, DateTimeOffset punchedAt)
    {
        var ts = punchedAt.UtcDateTime.ToString("yyyyMMddHHmmss");
        var raw = deviceId is { } d
            ? string.Join('|', "device", d.ToString(), subject, ts)
            : string.Join('|', source, "-", subject, ts);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    /// <summary>Converts a device wall-clock timestamp into (instant, wallTime). Device-local mode
    /// interprets naive/offset-less input in the device timezone; ambiguous (DST overlap) local
    /// times resolve to the first occurrence.</summary>
    public static Result<(DateTimeOffset Instant, DateTime WallTime)> NormalizeTimestamp(string? raw, AttendanceDevice device)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Error.Validation("punch.timestamp", "Timestamp is required.");
        var text = raw.Trim();
        if (device.TimezoneMode == DeviceTimezoneModes.Utc)
        {
            if (!DateTimeOffset.TryParse(text, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var utc))
                return Error.Validation("punch.timestamp", $"Unparseable timestamp '{text}'.");
            return (utc, DateTime.SpecifyKind(utc.UtcDateTime, DateTimeKind.Unspecified));
        }

        var tz = ResolveZone(device.DeviceTimezone);
        if (!DateTime.TryParse(text, null, System.Globalization.DateTimeStyles.None, out var local))
            return Error.Validation("punch.timestamp", $"Unparseable timestamp '{text}'.");
        if (local.Kind != DateTimeKind.Unspecified)
        {
            // Offset/Z-suffixed input: keep the reported instant verbatim — the wall time may
            // be ambiguous (DST fall-back), so the instant must not be re-derived from it.
            var instant = new DateTimeOffset(local);
            var shifted = TimeZoneInfo.ConvertTime(instant, tz);
            return (instant, DateTime.SpecifyKind(shifted.DateTime, DateTimeKind.Unspecified));
        }
        if (tz.IsAmbiguousTime(local))
        {
            var offsets = tz.GetAmbiguousTimeOffsets(local);
            var off = offsets.OrderByDescending(o => o).First(); // first occurrence (DST fall-back)
            return (new DateTimeOffset(local, off), DateTime.SpecifyKind(local, DateTimeKind.Unspecified));
        }
        if (tz.IsInvalidTime(local))
        {
            // DST spring-forward gap: push forward by the DST delta (typ. 1h) then convert.
            var adjusted = local + TimeSpan.FromHours(1);
            return (new DateTimeOffset(adjusted, tz.GetUtcOffset(adjusted)), DateTime.SpecifyKind(local, DateTimeKind.Unspecified));
        }
        return (new DateTimeOffset(local, tz.GetUtcOffset(local)), DateTime.SpecifyKind(local, DateTimeKind.Unspecified));
    }

    /// <summary>Resolves the employee for a device user code effective at the punch's local date
    /// (device-local date where applicable, else UTC date).</summary>
    internal async Task<Guid?> ResolveDeviceEmployeeAsync(Guid deviceId, string deviceUserCode, DateOnly punchDate, CancellationToken ct) =>
        await db.DeviceUserMaps.AsNoTracking()
            .Where(m => m.DeviceId == deviceId && m.DeviceUserCode == deviceUserCode
                && m.EffectiveFrom <= punchDate && (m.EffectiveTo == null || m.EffectiveTo >= punchDate))
            .Select(m => (Guid?)m.EmployeeId)
            .FirstOrDefaultAsync(ct);

    // ---------- Device authentication ----------

    /// <summary>Authenticates an 'Authorization: Device <token>' caller inside the tenant resolved
    /// by host. Isolation is by construction: the token only exists in this tenant's database.</summary>
    public async Task<(AttendanceDevice? Device, Error? Error)> AuthenticateDeviceAsync(string? authHeader, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(authHeader) ||
            !authHeader.StartsWith("Device ", StringComparison.Ordinal) ||
            authHeader["Device ".Length..].Trim() is not { Length: > 0 } token)
            return (null, Error.Unauthorized("device.auth", "Device token required."));

        var hash = DeviceService.HashToken(token);
        var device = await db.AttendanceDevices.FirstOrDefaultAsync(d => d.TokenHash == hash, ct);
        if (device is null || !device.IsActive)
            return (null, Error.Unauthorized("device.auth", "Unknown or inactive device token."));
        device.LastSeenAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return (device, null);
    }

    // ---------- Core ingest ----------

    private sealed record Row(string? DeviceUserCode, string? EmployeeCode, string? TimestampRaw,
        DateTimeOffset Instant, DateTime WallTime, string Direction);

    private async Task<Result<(IngestBatch Batch, bool Raced)>> OpenBatchAsync(
        string source, Guid? deviceId, Guid? fileId, string idempotencyKey, string? payloadHash,
        List<Row> rows, CancellationToken ct)
    {
        // Scope the caller-supplied key to its channel: different devices may legitimately reuse
        // the same external key — only same-channel repeats count as retries. (Imports key on the
        // file's content hash at the call site, so re-uploading the same file replays.)
        var channel = $"{source}:{deviceId?.ToString("N") ?? "-"}";
        var scopedKey = $"{channel}:" + (idempotencyKey.Length > 128 - channel.Length - 1
            ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey))).ToLowerInvariant()
            : idempotencyKey);

        Result<(IngestBatch, bool)> ReplayResult(IngestBatch existing)
        {
            if (payloadHash is not null && existing.PayloadHash is not null && existing.PayloadHash != payloadHash)
                return Error.Conflict("ingest.key_mismatch", "Idempotency-Key was already used with a different payload.");
            return (existing, true); // replay: caller gets stored counts, zero new rows
        }

        var existing = await db.IngestBatches.FirstOrDefaultAsync(b => b.IdempotencyKey == scopedKey, ct);
        if (existing is not null)
            return ReplayResult(existing);

        var batch = new IngestBatch
        {
            Source = source, DeviceId = deviceId, FileId = fileId, IdempotencyKey = scopedKey,
            PayloadHash = payloadHash, ReceivedCount = rows.Count,
        };
        db.IngestBatches.Add(batch);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DeviceService.IsOverlapOrUnique(ex))
        {
            // A concurrent call won the same idempotency key; never touch the tracker again —
            // the replay path performs no further SaveChanges.
            var raced = await db.IngestBatches
                .FirstAsync(b => b.IdempotencyKey == scopedKey, ct);
            return ReplayResult(raced);
        }
        return (batch, false);
    }

    /// <summary>Inserts dedup-checked punch rows for a batch. Valid rows are never silently dropped:
    /// content duplicates are counted, malformed rows land in error_summary.</summary>
    private async Task<IngestResultDto> CommitBatchAsync(IngestBatch batch, List<Row> rows,
        List<IngestReject> rejects, CancellationToken ct)
    {
        // Badge codes and employee codes are different namespaces — tag the subject kind so a
        // badge 'E1' can never dedup-collide with employee code 'E1' on the same device+second.
        var subjectOf = (Row r) => !string.IsNullOrWhiteSpace(r.DeviceUserCode) ? "b:" + r.DeviceUserCode!.Trim()
            : !string.IsNullOrWhiteSpace(r.EmployeeCode) ? "e:" + r.EmployeeCode!.Trim() : "-";
        var dedupKeys = rows.Select(r => BuildDedupKey(batch.Source, batch.DeviceId, subjectOf(r), r.Instant)).ToHashSet();
        var existing = await db.Punches.AsNoTracking().Where(p => dedupKeys.Contains(p.DedupKey))
            .Select(p => new { p.DedupKey, p.IngestBatchId }).ToListAsync(ct);
        var taken = existing.ToDictionary(x => x.DedupKey, x => x.IngestBatchId);
        var claimed = new HashSet<string>(); // keys claimed by this batch's own rows

        // Scoped importers can only write punches for employees inside their import scope.
        HashSet<Guid>? importScope = null;
        if (batch.Source == PunchSources.Import)
        {
            var scope = await scopes.ResolveAsync(Modules.AttendancePermissions.PunchesImport, ct);
            if (!scope.All)
            {
                importScope = (await scopes.ApplyScope(db.Employees.AsNoTracking(), scope)
                    .Select(e => e.Id).ToListAsync(ct)).ToHashSet();
            }
        }

        var accepted = 0;
        var duplicates = 0;
        var pending = new List<Punch>();
        foreach (var (row, i) in rows.Select((r, i) => (r, i)))
        {
            var key = BuildDedupKey(batch.Source, batch.DeviceId, subjectOf(row), row.Instant);
            if (taken.TryGetValue(key, out var ownerBatch))
            {
                // Persisted by a concurrent commit of THIS batch — counts as accepted, not duplicate.
                if (ownerBatch == batch.Id) accepted++;
                else duplicates++;
                continue;
            }

            // The admin-managed device mapping wins over any employee code the payload carries;
            // a payload code only resolves employees when no mapping exists.
            Guid? employeeId = null;
            if (batch.DeviceId is { } devId && row.DeviceUserCode is { Length: > 0 } devCode)
            {
                var punchDate = DateOnly.FromDateTime(row.WallTime);
                employeeId = await ResolveDeviceEmployeeAsync(devId, devCode, punchDate, ct);
            }
            if (employeeId is null && row.EmployeeCode is { Length: > 0 } code)
            {
                employeeId = await db.Employees.AsNoTracking().Where(e => e.EmployeeCode == code)
                    .Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
                if (employeeId is null)
                {
                    rejects.Add(new IngestReject(i, $"unknown employee code '{code}'"));
                    continue;
                }
            }
            if (employeeId is { } eid && importScope is not null && !importScope.Contains(eid))
            {
                rejects.Add(new IngestReject(i, "employee is outside the import scope"));
                continue;
            }

            var punch = new Punch
            {
                // timestamptz requires offset-0 — normalize device-local instants to UTC.
                EmployeeId = employeeId, PunchedAt = row.Instant.ToUniversalTime(), LocalWallTime = row.WallTime,
                Source = batch.Source, Direction = row.Direction, DeviceId = batch.DeviceId,
                DeviceUserCode = row.DeviceUserCode, IngestBatchId = batch.Id,
                DedupKey = key,
                IpAddress = request.IpAddress, UserAgent = request.UserAgent,
            };
            if (!claimed.Add(key)) // claim the key only once the row has passed every check
            {
                duplicates++; // identical row earlier in the same batch
                continue;
            }
            db.Punches.Add(punch);
            pending.Add(punch);
            accepted++;
        }

        batch.AcceptedCount = accepted;
        batch.DuplicateCount = duplicates;
        batch.RejectedCount = rejects.Count;
        batch.Status = IngestBatchStatuses.Completed;
        batch.CompletedAt = clock.UtcNow;
        if (rejects.Count > 0)
        {
            batch.ErrorSummary = JsonSerializer.Serialize(rejects.Take(MaxRejectionsStored));
        }

        // Concurrent pushes can win dedup keys between the pre-check and the save: drop only the
        // punches another batch actually persisted and retry, so new rows here are never lost.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(ct);
                break;
            }
            catch (DbUpdateException ex) when (DeviceService.IsOverlapOrUnique(ex) && attempt <= 3)
            {
                var keys = pending.Select(p => p.DedupKey).ToList();
                var won = await db.Punches.AsNoTracking().Where(p => keys.Contains(p.DedupKey))
                    .Select(p => new { p.DedupKey, p.IngestBatchId }).ToListAsync(ct);
                var wonKeys = won.ToDictionary(x => x.DedupKey, x => x.IngestBatchId);
                var losers = pending.Where(p => wonKeys.ContainsKey(p.DedupKey)).ToList();
                foreach (var loser in losers)
                {
                    ((DbContext)db).Entry(loser).State = EntityState.Detached;
                    pending.Remove(loser);
                }
                // Rows a twin commit persisted FOR this batch still count as accepted.
                var ownLosers = losers.Count(l => wonKeys[l.DedupKey] == batch.Id);
                duplicates += losers.Count - ownLosers;
                accepted = pending.Count + ownLosers;
                batch.AcceptedCount = accepted;
                batch.DuplicateCount = duplicates;
            }
        }
        return new IngestResultDto(batch.Id, batch.ReceivedCount, accepted, duplicates,
            rejects.Count, rejects, batch.Status);
    }

    private static IReadOnlyList<IngestReject> ReadRejects(IngestBatch batch) =>
        batch.ErrorSummary is { Length: > 0 } json
            ? (JsonSerializer.Deserialize<List<IngestReject>>(json) ?? [])
            : [];

    // ---------- Device push ----------

    /// <summary>Entitlement gate for anonymous device endpoints — must run before any att query
    /// so an unentitled (or unmigrated) tenant gets a clean rejection, not a DB error.</summary>
    public async Task<Error?> CheckModuleEnabledAsync(CancellationToken ct) =>
        (await entitlements.GetAsync(tenant.Required.TenantId, ct)).IsModuleEnabled(Modules.AttendanceModule.ModuleCode)
            ? null
            : Error.Forbidden("module-not-enabled", "Attendance is not enabled for your organisation.");

    /// <summary>POST /devices/{code}/punches — device token already authenticated by caller.</summary>
    public async Task<Result<IngestResultDto>> IngestDevicePushAsync(AttendanceDevice device,
        string idempotencyKey, DevicePushRequest req, string payloadHash, CancellationToken ct)
    {
        // The endpoint is anonymous, so it cannot rely on RequirePermission's entitlement gate.
        if (await CheckModuleEnabledAsync(ct) is { } denied)
            return denied;

        if (req.Punches is not { } items || items.Count == 0)
            return Error.Validation("ingest.empty", "Punch list is empty.");
        if (items.Count > 2000)
            return Error.Validation("ingest.too_large", "Batch exceeds 2000 punches; split the call.");

        var rows = new List<Row>();
        var rejects = new List<IngestReject>();
        foreach (var (item, i) in items.Select((x, i) => (x, i)))
        {
            var direction = PunchDirections.All.Contains(item.Direction ?? "") ? item.Direction! : PunchDirections.Unknown;
            var ts = NormalizeTimestamp(item.Timestamp, device);
            if (!ts.IsSuccess)
            {
                rejects.Add(new IngestReject(i, $"bad timestamp '{item.Timestamp}'"));
                continue;
            }
            if (string.IsNullOrWhiteSpace(item.DeviceUserCode))
            {
                rejects.Add(new IngestReject(i, "device_user_code is required"));
                continue;
            }
            var (instant, wall) = ts.Value;
            rows.Add(new Row(item.DeviceUserCode.Trim(), item.EmployeeCode?.Trim(), item.Timestamp, instant, wall, direction));
        }

        var opened = await OpenBatchAsync(PunchSources.Device, device.Id, null, idempotencyKey, payloadHash, rows, ct);
        if (!opened.IsSuccess) return opened.Error!;
        var (batch, raced) = opened.Value;
        if (raced)
        {
            if (batch.Status == IngestBatchStatuses.Processing)
            {
                // An earlier attempt died after opening the batch — finish it with this call's rows.
                return await CommitBatchAsync(batch, rows, rejects, ct);
            }
            // An identical-key batch already exists (replay): return its stored state.
            return new IngestResultDto(batch.Id, batch.ReceivedCount, batch.AcceptedCount,
                batch.DuplicateCount, batch.RejectedCount, ReadRejects(batch), batch.Status);
        }
        return await CommitBatchAsync(batch, rows, rejects, ct);
    }

    // ---------- Self / manual punches ----------

    /// <summary>Employee check-in/out. The client clock is kept as local_wall_time; skew is capped at 24h.</summary>
    public async Task<Result<PunchDto>> SelfPunchAsync(SelfPunchInput input, CancellationToken ct)
    {
        var direction = input.Direction ?? PunchDirections.Unknown;
        if (!PunchDirections.All.Contains(direction))
            return Error.Validation("punch.direction", "Direction must be in, out or unknown.");
        if (user.UserId is not { } uid)
            return Error.Unauthorized("punch.auth", "Sign in required.");
        var employeeId = await db.Users.AsNoTracking().Where(u => u.Id == uid)
            .Select(u => u.EmployeeId).FirstOrDefaultAsync(ct);
        if (employeeId is null)
            return Error.Rule("punch.no_employee", "Your account is not linked to an employee record.");
        var instant = clock.UtcNow;
        var clientWall = DateTime.SpecifyKind(input.ClientTime?.DateTime ?? instant.UtcDateTime, DateTimeKind.Unspecified);
        if (input.ClientTime is { } client && (client - instant).Duration() > SelfSkewLimit)
            return Error.Rule("punch.skew", "Client clock differs from server time by more than 24 hours.");

        var punch = new Punch
        {
            EmployeeId = employeeId, PunchedAt = instant, LocalWallTime = clientWall,
            Source = PunchSources.Self, Direction = direction,
            DedupKey = BuildDedupKey(PunchSources.Self, null, employeeId.Value.ToString(), instant),
            GeoLat = input.GeoLat, GeoLng = input.GeoLng, GeoAccuracyM = input.GeoAccuracyM,
            IpAddress = request.IpAddress, UserAgent = request.UserAgent, Note = input.Note?.Trim(),
        };
        db.Punches.Add(punch);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DeviceService.IsOverlapOrUnique(ex))
        {
            return Error.Conflict("punch.duplicate", "An identical punch was already recorded.");
        }
        return await LoadPunchDtoAsync(punch.Id, ct) is { } dto ? dto : Error.Rule("punch.load", "Saved but could not reload punch.");
    }

    /// <summary>HR/admin manual entry — note mandatory, employee must be inside the caller's data scope.</summary>
    public async Task<Result<PunchDto>> ManualPunchAsync(ManualPunchInput input, CancellationToken ct)
    {
        if (input.EmployeeId is not { } empId)
            return Error.Validation("punch.employee", "employee_id is required.");
        if (string.IsNullOrWhiteSpace(input.Note))
            return Error.Validation("punch.note", "A reason/note is required for manual punches.");
        if (input.PunchedAt is not { } at)
            return Error.Validation("punch.timestamp", "punched_at is required.");
        var direction = input.Direction ?? PunchDirections.Unknown;
        if (!PunchDirections.All.Contains(direction))
            return Error.Validation("punch.direction", "Direction must be in, out or unknown.");
        if (!await scopes.CanAccessEmployeeAsync(Modules.AttendancePermissions.PunchesManual, empId, ct))
            return Error.Forbidden("scope.denied", "Employee is outside your data scope.");
        if (input.RefRequestId is { Length: > 0 } refId &&
            await db.Punches.AnyAsync(p => p.Source == PunchSources.Manual && p.RefRequestId == refId, ct))
            return Error.Conflict("punch.duplicate_ref", "This request reference was already used.");

        var punchedAt = at.ToUniversalTime();
        var punch = new Punch
        {
            EmployeeId = empId, PunchedAt = punchedAt, LocalWallTime = DateTime.SpecifyKind(at.DateTime, DateTimeKind.Unspecified),
            Source = PunchSources.Manual, Direction = direction,
            DedupKey = BuildDedupKey(PunchSources.Manual, null, empId.ToString(), punchedAt),
            RefRequestId = input.RefRequestId?.Trim(), Note = input.Note.Trim(),
            IpAddress = request.IpAddress, UserAgent = request.UserAgent,
        };
        db.Punches.Add(punch);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DeviceService.IsOverlapOrUnique(ex))
        {
            return Error.Conflict("punch.duplicate", "An identical punch was already recorded.");
        }
        return await LoadPunchDtoAsync(punch.Id, ct) is { } dto ? dto : Error.Rule("punch.load", "Saved but could not reload punch.");
    }

    /// <summary>Marks a punch void (row stays; computation ignores it). Scoped like manual entry.</summary>
    public async Task<Result> VoidPunchAsync(Guid punchId, string? reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Error.Validation("void.reason", "A void reason is required.");
        var punch = await db.Punches.FirstOrDefaultAsync(p => p.Id == punchId, ct);
        if (punch is null) return Error.NotFound("punch.not_found", "Punch not found.");
        if (await db.PunchVoids.AnyAsync(v => v.PunchId == punchId, ct))
            return Error.Conflict("void.duplicate", "Punch is already voided.");
        if (punch.EmployeeId is { } empId)
        {
            if (!await scopes.CanAccessEmployeeAsync(Modules.AttendancePermissions.PunchesManual, empId, ct))
                return Error.Forbidden("scope.denied", "Punch is outside your data scope.");
        }
        else if (!(await scopes.ResolveAsync(Modules.AttendancePermissions.PunchesManual, ct)).All)
        {
            // Unmapped punches have no employee to scope to — require tenant-wide scope.
            return Error.Forbidden("scope.denied", "Unmapped punches require tenant-wide scope.");
        }

        db.PunchVoids.Add(new PunchVoid { PunchId = punchId, Reason = reason.Trim() });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DeviceService.IsOverlapOrUnique(ex))
        {
            return Error.Conflict("void.duplicate", "Punch is already voided.");
        }
        return Result.Success();
    }

    // ---------- CSV import (synchronous; bounded by the 10 MB file purpose) ----------

    public async Task<Result<IngestResultDto>> ImportCsvAsync(PunchImportInput input, CancellationToken ct)
    {
        if (input.FileId is not { } fileId)
            return Error.Validation("import.file", "file_id is required.");
        var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
        // Only files uploaded under the punch-import purpose may be ingested — mirroring the
        // file service's authorization checks so an arbitrary file id cannot be read.
        if (file is null || file.IsDeleted || file.Status != FileStatus.Available ||
            !string.Equals(file.Purpose, Modules.AttendancePermissions.PunchImportPurpose, StringComparison.Ordinal))
            return Error.NotFound("file.not_found", "Uploaded file not found.");
        if (input.Mapping is not { } map)
            return Error.Validation("import.mapping", "column mapping is required.");

        AttendanceDevice? device = null;
        if (input.DeviceId is { } devId)
        {
            device = await db.AttendanceDevices.FirstOrDefaultAsync(d => d.Id == devId, ct);
            if (device is null) return Error.Validation("import.device", "Unknown device.");
        }

        // Default key derives from the file's content hash, not its row id: re-uploading the same
        // CSV produces a new file row but the same hash, so the retry replays the original batch.
        var idemKey = input.IdempotencyKey is { Length: > 0 } k ? k.Trim()
            : $"csv:{file.Sha256}:{JsonSerializer.Serialize(map)}:{input.SkipHeader}:{input.DeviceId}";
        var (rows, rejects) = await ParseCsvAsync(file, map, device, input.SkipHeader, ct);
        var opened = await OpenBatchAsync(PunchSources.Import, device?.Id, fileId, idemKey, file.Sha256, rows, ct);
        if (!opened.IsSuccess) return opened.Error!;
        var (batch, raced) = opened.Value;
        if (raced)
        {
            if (batch.Status == IngestBatchStatuses.Processing)
            {
                batch.ReceivedCount = rows.Count + rejects.Count;
                return await CommitBatchAsync(batch, rows, rejects, ct);
            }
            return new IngestResultDto(batch.Id, batch.ReceivedCount, batch.AcceptedCount,
                batch.DuplicateCount, batch.RejectedCount, ReadRejects(batch), batch.Status);
        }
        batch.ReceivedCount = rows.Count + rejects.Count;
        return await CommitBatchAsync(batch, rows, rejects, ct);
    }

    /// <summary>Minimal RFC-4180 reader: quoted fields, embedded commas/CRLF, double-quote escapes.
    /// Rows that fail column checks become rejects; valid rows always ingest.</summary>
    private async Task<(List<Row> Rows, List<IngestReject> Rejects)> ParseCsvAsync(
        FileObject file, CsvColumnMapping map, AttendanceDevice? device, bool skipHeader, CancellationToken ct)
    {
        var rows = new List<Row>();
        var rejects = new List<IngestReject>();
        await using var stream = await storage.OpenReadAsync(file.StorageKey, ct);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lineNo = -1;
        await foreach (var fields in ReadCsvAsync(reader, ct))
        {
            lineNo++;
            if (lineNo == 0 && skipHeader) continue;
            if (fields.Count == 0 || (fields.Count == 1 && string.IsNullOrWhiteSpace(fields[0]))) continue;

            string? Col(int idx) => idx >= 0 && idx < fields.Count ? fields[idx].Trim() : null;
            var deviceUserCode = Col(map.DeviceUserCodeCol);
            var timestampRaw = Col(map.TimestampCol);
            var direction = Col(map.DirectionCol);
            var employeeCode = map.EmployeeCodeCol >= 0 ? Col(map.EmployeeCodeCol) : null;

            direction = PunchDirections.All.Contains(direction ?? "") ? direction! : PunchDirections.Unknown;
            if (string.IsNullOrWhiteSpace(deviceUserCode) && string.IsNullOrWhiteSpace(employeeCode))
            {
                rejects.Add(new IngestReject(lineNo, "no device_user_code or employee_code"));
                continue;
            }
            var tsDevice = device ?? new AttendanceDevice { TimezoneMode = DeviceTimezoneModes.Utc };
            var ts = NormalizeTimestamp(timestampRaw, tsDevice);
            if (!ts.IsSuccess)
            {
                rejects.Add(new IngestReject(lineNo, $"bad timestamp '{timestampRaw}'"));
                continue;
            }
            var (instant, wall) = ts.Value;
            rows.Add(new Row(deviceUserCode, employeeCode, timestampRaw, instant, wall, direction));
        }
        return (rows, rejects);
    }

    private static async IAsyncEnumerable<List<string>> ReadCsvAsync(StreamReader reader,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var field = new StringBuilder();
        var row = new List<string>();
        var inQuotes = false;
        var fieldStarted = false;
        int c;
        while ((c = reader.Read()) != -1)
        {
            ct.ThrowIfCancellationRequested();
            var ch = (char)c;
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (reader.Peek() == '"') { field.Append('"'); reader.Read(); }
                    else inQuotes = false;
                }
                else field.Append(ch);
                continue;
            }
            switch (ch)
            {
                case '"' when !fieldStarted:
                    inQuotes = true; fieldStarted = true; break;
                case ',':
                    row.Add(field.ToString()); field.Clear(); fieldStarted = false; break;
                case '\r':
                    if (reader.Peek() == '\n') reader.Read();
                    row.Add(field.ToString());
                    yield return row;
                    row = []; field.Clear(); fieldStarted = false;
                    break;
                case '\n':
                    row.Add(field.ToString());
                    yield return row;
                    row = []; field.Clear(); fieldStarted = false;
                    break;
                default:
                    field.Append(ch); fieldStarted = true; break;
            }
        }
        if (fieldStarted || field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            yield return row;
        }
    }

    // ---------- Queries ----------

    public async Task<PagedResult<PunchDto>> QueryAsync(PunchQuery q, CancellationToken ct)
    {
        var scope = await scopes.ResolveAsync(Modules.AttendancePermissions.View, ct);
        var scopedEmployees = scopes.ApplyScope(db.Employees.AsNoTracking(), scope).Select(e => (Guid?)e.Id);
        var scopedBranches = scope.BranchIds.ToList();

        var seeAll = scope.All;
        var punches = from p in db.Punches.AsNoTracking()
                      where (p.EmployeeId != null && scopedEmployees.Contains(p.EmployeeId))
                         || (p.EmployeeId == null && (seeAll || (p.DeviceId != null &&
                             db.AttendanceDevices.Any(d => d.Id == p.DeviceId && d.BranchId != null && scopedBranches.Contains(d.BranchId.Value)))))
                      select p;
        if (q.EmployeeId is { } emp) punches = punches.Where(p => p.EmployeeId == emp);
        if (q.DeviceId is { } dev) punches = punches.Where(p => p.DeviceId == dev);
        if (q.Source is { Length: > 0 } src) punches = punches.Where(p => p.Source == src);
        if (q.Direction is { Length: > 0 } dir) punches = punches.Where(p => p.Direction == dir);
        if (q.From is { } from) punches = punches.Where(p => p.PunchedAt >= new DateTimeOffset(from, TimeOnly.MinValue, TimeSpan.Zero));
        if (q.To is { } to) punches = punches.Where(p => p.PunchedAt < new DateTimeOffset(to.AddDays(1), TimeOnly.MinValue, TimeSpan.Zero));
        if (q.UnmappedOnly == true) punches = punches.Where(p => p.EmployeeId == null);
        var voidIds = db.PunchVoids.AsNoTracking().Select(v => v.PunchId);
        if (q.IncludeVoided != true) punches = punches.Where(p => !voidIds.Contains(p.Id));

        var total = await punches.CountAsync(ct);
        var page = Math.Max(q.Page, 1);
        var size = Math.Clamp(q.PageSize, 1, 200);
        var rows = await punches.OrderByDescending(p => p.PunchedAt).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<PunchDto>(await HydrateAsync(rows, ct), page, size, total);
    }

    /// <summary>The signed-in user's own punches (attendance.my.view).</summary>
    public async Task<PagedResult<PunchDto>> MyPunchesAsync(DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken ct)
    {
        if (user.UserId is not { } uid)
            return new PagedResult<PunchDto>([], Math.Max(page, 1), Math.Clamp(pageSize, 1, 200), 0);
        var employeeId = await db.Users.AsNoTracking().Where(u => u.Id == uid)
            .Select(u => u.EmployeeId).FirstOrDefaultAsync(ct);
        if (employeeId is null)
            return new PagedResult<PunchDto>([], Math.Max(page, 1), Math.Clamp(pageSize, 1, 200), 0);

        var punches = db.Punches.AsNoTracking().Where(p => p.EmployeeId == employeeId);
        if (from is { } f) punches = punches.Where(p => p.PunchedAt >= new DateTimeOffset(f, TimeOnly.MinValue, TimeSpan.Zero));
        if (to is { } t) punches = punches.Where(p => p.PunchedAt < new DateTimeOffset(t.AddDays(1), TimeOnly.MinValue, TimeSpan.Zero));

        var total = await punches.CountAsync(ct);
        var size = Math.Clamp(pageSize, 1, 200);
        var pg = Math.Max(page, 1);
        var rows = await punches.OrderByDescending(p => p.PunchedAt).Skip((pg - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<PunchDto>(await HydrateAsync(rows, ct), pg, size, total);
    }

    private async Task<PunchDto?> LoadPunchDtoAsync(Guid punchId, CancellationToken ct)
    {
        var row = await db.Punches.AsNoTracking().FirstOrDefaultAsync(p => p.Id == punchId, ct);
        return row is null ? null : (await HydrateAsync([row], ct)).First();
    }

    private async Task<IReadOnlyList<PunchDto>> HydrateAsync(List<Punch> rows, CancellationToken ct)
    {
        var empIds = rows.Where(r => r.EmployeeId != null).Select(r => r.EmployeeId!.Value).Distinct().ToList();
        var devIds = rows.Where(r => r.DeviceId != null).Select(r => r.DeviceId!.Value).Distinct().ToList();
        var emps = await db.Employees.AsNoTracking().Where(e => empIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => new { e.EmployeeCode, e.DisplayName }, ct);
        var devs = await db.AttendanceDevices.AsNoTracking().Where(d => devIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Code, ct);
        var voids = await db.PunchVoids.AsNoTracking().Where(v => rows.Select(r => r.Id).Contains(v.PunchId))
            .ToDictionaryAsync(v => v.PunchId, ct);
        return rows.Select(p =>
        {
            emps.TryGetValue(p.EmployeeId ?? Guid.Empty, out var e);
            devs.TryGetValue(p.DeviceId ?? Guid.Empty, out var dc);
            var v = voids.GetValueOrDefault(p.Id);
            return new PunchDto(p.Id, p.EmployeeId, e?.EmployeeCode, e?.DisplayName, p.PunchedAt,
                p.LocalWallTime, p.Source, p.Direction, p.DeviceId, dc, p.DeviceUserCode,
                p.IngestBatchId, p.GeoLat, p.GeoLng, p.GeoAccuracyM, p.IpAddress, p.UserAgent,
                p.RefRequestId, p.Note, p.CreatedAt, v is not null, v?.Reason, v?.CreatedAt);
        }).ToList();
    }

    public async Task<PagedResult<IngestBatchDto>> ListBatchesAsync(string? source, int page, int pageSize, CancellationToken ct)
    {
        var q = db.IngestBatches.AsNoTracking().AsQueryable();
        if (source is { Length: > 0 } s) q = q.Where(b => b.Source == s);
        var total = await q.CountAsync(ct);
        var size = Math.Clamp(pageSize, 1, 200);
        // Error summaries can name employee codes from other branches — redact them for scoped viewers.
        var scope = await scopes.ResolveAsync(Modules.AttendancePermissions.View, ct);
        var rows = await q.OrderByDescending(b => b.CreatedAt).Skip((Math.Max(page, 1) - 1) * size).Take(size).ToListAsync(ct);
        var devIds = rows.Where(r => r.DeviceId != null).Select(r => r.DeviceId!.Value).ToList();
        var devs = await db.AttendanceDevices.AsNoTracking().Where(d => devIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Code, ct);
        return new PagedResult<IngestBatchDto>(
            rows.Select(b =>
            {
                var dto = b.ToDto(b.DeviceId is { } id && devs.TryGetValue(id, out var c) ? c : null);
                return scope.All ? dto : dto with { ErrorSummary = null };
            }).ToList(),
            Math.Max(page, 1), size, total);
    }

    /// <summary>attendance.device.map job: back-fills employee_id on unmapped punches whose
    /// device+code now resolve through an effective mapping. The DB trigger permits exactly this update.</summary>
    public async Task<int> BackfillUnmappedAsync(Guid deviceId, CancellationToken ct)
    {
        var unmapped = await db.Punches.Where(p => p.DeviceId == deviceId && p.EmployeeId == null)
            .Select(p => new { p.Id, p.DeviceUserCode, p.LocalWallTime }).ToListAsync(ct);
        var filled = 0;
        foreach (var p in unmapped)
        {
            if (p.DeviceUserCode is not { Length: > 0 } code) continue;
            // Resolve against the same wall-clock date ingestion used — not the UTC date.
            var empId = await ResolveDeviceEmployeeAsync(deviceId, code, DateOnly.FromDateTime(p.LocalWallTime), ct);
            if (empId is null) continue;
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE att.punches SET employee_id = {empId.Value} WHERE id = {p.Id} AND employee_id IS NULL", ct);
            filled++;
        }
        return filled;
    }
}
