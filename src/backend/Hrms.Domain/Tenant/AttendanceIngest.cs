using Hrms.Shared.Auditing;
using Hrms.Shared.Primitives;

namespace Hrms.Domain.Tenant;

public static class DeviceProtocols
{
    /// <summary>Terminal pushes punches over HTTP (implemented).</summary>
    public const string PushApi = "push_api";
    /// <summary>Gateway polls the device (declared seam, not implemented).</summary>
    public const string PullPoll = "pull_poll";
    /// <summary>Punches arrive via CSV/file import.</summary>
    public const string FileImport = "file_import";
    public static readonly string[] All = [PushApi, PullPoll, FileImport];
}

public static class DeviceTimezoneModes
{
    /// <summary>Punch timestamps in the payload are already UTC instants.</summary>
    public const string Utc = "utc";
    /// <summary>Punch timestamps are wall-clock in <see cref="AttendanceDevice.DeviceTimezone"/>.</summary>
    public const string DeviceLocal = "device_local";
    public static readonly string[] All = [Utc, DeviceLocal];
}

public static class PunchSources
{
    public const string Device = "device";
    public const string Self = "self";
    public const string Manual = "manual";
    public const string Import = "import";
    /// <summary>Reserved for M3.4 regularization; never written by M3.2.</summary>
    public const string Regularization = "regularization";
    public static readonly string[] All = [Device, Self, Manual, Import];
}

public static class PunchDirections
{
    public const string In = "in";
    public const string Out = "out";
    public const string Unknown = "unknown";
    public static readonly string[] All = [In, Out, Unknown];
}

public static class IngestBatchStatuses
{
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

/// <summary>Biometric terminal / punch source master. Bearer token is stored hashed;
/// plaintext is returned once at create/rotate and never persisted.</summary>
[Audited("attendance")]
public sealed class AttendanceDevice : AuditableEntity, IConcurrencyAware
{
    /// <summary>Tenant-unique device code (e.g. BIO-HQ-01).</summary>
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Informational vendor string; no vendor-specific behaviour in M3.2.</summary>
    public string? Vendor { get; set; }
    /// <summary>Physical location; scopes device punches for branch-scoped viewers.</summary>
    public Guid? BranchId { get; set; }
    public string Protocol { get; set; } = DeviceProtocols.PushApi;
    public string TimezoneMode { get; set; } = DeviceTimezoneModes.Utc;
    /// <summary>IANA timezone; required when TimezoneMode = device_local.</summary>
    public string? DeviceTimezone { get; set; }
    /// <summary>SHA-256 hex of the device bearer token.</summary>
    public string TokenHash { get; set; } = string.Empty;
    /// <summary>External secret reference for pull_poll devices (seam only).</summary>
    public string? CredentialRef { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public bool IsActive { get; set; } = true;
    public uint RowVersion { get; set; }
}

/// <summary>Effective-dated mapping of a device's own user/badge code to an employee.
/// Overlap per (device, code) is DB-enforced. Unmapped punches are kept and back-filled
/// when a later map row covers their punch date.</summary>
[Audited("attendance")]
public sealed class DeviceUserMap : AuditableEntity
{
    public Guid DeviceId { get; set; }
    /// <summary>The ID enrolled on the device (e.g. fingerprint enrolment "1002").</summary>
    public string DeviceUserCode { get; set; } = string.Empty;
    public Guid EmployeeId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}

/// <summary>Append-only raw punch event. <see cref="PunchedAt"/> is the canonical UTC instant;
/// <see cref="LocalWallTime"/> preserves exactly what the device/import emitted. Rows are
/// immutable (DB trigger) except a one-way employee_id back-fill on previously unmapped rows.
/// Corrections happen via <see cref="PunchVoid"/> + corrected re-entry, never edits.</summary>
[Audited("attendance")]
public sealed class Punch : AuditableEntity
{
    /// <summary>null = unmapped device punch; back-fillable exactly once.</summary>
    public Guid? EmployeeId { get; set; }
    /// <summary>Canonical instant (UTC).</summary>
    public DateTimeOffset PunchedAt { get; set; }
    /// <summary>Wall-clock as emitted by the source, pre-normalization.</summary>
    public DateTime LocalWallTime { get; set; }
    public string Source { get; set; } = PunchSources.Device;
    public string Direction { get; set; } = PunchDirections.Unknown;
    public Guid? DeviceId { get; set; }
    /// <summary>Raw device-side user code as received (kept even after mapping).</summary>
    public string? DeviceUserCode { get; set; }
    public Guid? IngestBatchId { get; set; }
    /// <summary>Content dedup key: SHA-256(source|device|device_user_code|employee|punched_at-to-second).</summary>
    public string DedupKey { get; set; } = string.Empty;
    public double? GeoLat { get; set; }
    public double? GeoLng { get; set; }
    public int? GeoAccuracyM { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    /// <summary>Caller-supplied correlation id; manual punches dedup on it.</summary>
    public string? RefRequestId { get; set; }
    /// <summary>Mandatory for manual punches.</summary>
    public string? Note { get; set; }
}

/// <summary>Void marker — the punch row stays; computation phases ignore voided punches.</summary>
[Audited("attendance")]
public sealed class PunchVoid : AuditableEntity
{
    /// <summary>One void per punch (unique FK).</summary>
    public Guid PunchId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>One row per ingestion attempt (device push, CSV import, or manual/self batch).
/// Batch-level IdempotencyKey is the call-dedup layer on top of punch-level DedupKey.</summary>
[Audited("attendance")]
public sealed class IngestBatch : AuditableEntity
{
    public string Source { get; set; } = PunchSources.Device;
    public Guid? DeviceId { get; set; }
    /// <summary>CSV import payload uploaded via FileService.</summary>
    public Guid? FileId { get; set; }
    /// <summary>Unique per (device/import call); re-push with same key returns stored counts.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
    /// <summary>SHA-256 of the raw request payload — detects Idempotency-Key reuse with a different body.</summary>
    public string? PayloadHash { get; set; }
    public int ReceivedCount { get; set; }
    public int AcceptedCount { get; set; }
    public int DuplicateCount { get; set; }
    public int RejectedCount { get; set; }
    public string Status { get; set; } = IngestBatchStatuses.Processing;
    /// <summary>First-N reject reasons as a JSON array.</summary>
    public string? ErrorSummary { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
