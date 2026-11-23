using Hrms.Domain.Tenant;

namespace Hrms.Application.Attendance;

public sealed record DeviceDto(Guid Id, string Code, string Name, string? Vendor, Guid? BranchId, string? BranchName,
    string Protocol, string TimezoneMode, string? DeviceTimezone, DateTimeOffset? LastSeenAt, bool IsActive);

/// <summary>Plaintext token is returned exactly once (create/rotate); only its hash is stored.</summary>
public sealed record DeviceCreatedDto(DeviceDto Device, string Token);

public sealed record DeviceInput(string? Code, string? Name, string? Vendor, Guid? BranchId,
    string? Protocol, string? TimezoneMode, string? DeviceTimezone, bool? IsActive);

public sealed record DeviceUserMapDto(Guid Id, Guid DeviceId, string DeviceUserCode, Guid EmployeeId,
    string EmployeeCode, string EmployeeName, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

public sealed record DeviceUserMapInput(string? DeviceUserCode, Guid? EmployeeId, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

public sealed record PunchDto(Guid Id, Guid? EmployeeId, string? EmployeeCode, string? EmployeeName,
    DateTimeOffset PunchedAt, DateTime LocalWallTime, string Source, string Direction,
    Guid? DeviceId, string? DeviceCode, string? DeviceUserCode, Guid? IngestBatchId,
    double? GeoLat, double? GeoLng, int? GeoAccuracyM, string? IpAddress, string? UserAgent,
    string? RefRequestId, string? Note, DateTimeOffset CreatedAt, bool IsVoided, string? VoidReason, DateTimeOffset? VoidedAt);

public sealed record PunchQuery(Guid? EmployeeId, Guid? DeviceId, string? Source, string? Direction,
    DateOnly? From, DateOnly? To, bool? UnmappedOnly, bool? IncludeVoided, int Page = 1, int PageSize = 50);

public sealed record DevicePushItem(string? DeviceUserCode, string? Timestamp, string? Direction, string? EmployeeCode);
public sealed record DevicePushRequest(IReadOnlyList<DevicePushItem>? Punches);
public sealed record IngestReject(int Index, string Reason);
public sealed record IngestResultDto(Guid BatchId, int Received, int Accepted, int Duplicates, int Rejected,
    IReadOnlyList<IngestReject> Rejections, string Status);

public sealed record SelfPunchInput(string? Direction, DateTimeOffset? ClientTime, double? GeoLat, double? GeoLng, int? GeoAccuracyM, string? Note);
public sealed record ManualPunchInput(Guid? EmployeeId, DateTimeOffset? PunchedAt, string? Direction, string? Note, string? RefRequestId);
public sealed record VoidPunchInput(string? Reason);

public sealed record CsvColumnMapping(int DeviceUserCodeCol, int TimestampCol, int DirectionCol, int EmployeeCodeCol = -1);
public sealed record PunchImportInput(Guid? FileId, Guid? DeviceId, CsvColumnMapping? Mapping, string? IdempotencyKey, bool SkipHeader = true);

public sealed record IngestBatchDto(Guid Id, string Source, Guid? DeviceId, string? DeviceCode, Guid? FileId,
    int Received, int Accepted, int Duplicates, int Rejected, string Status, string? ErrorSummary,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

internal static class IngestMappings
{
    public static DeviceDto ToDto(this AttendanceDevice d, string? branchName = null) =>
        new(d.Id, d.Code, d.Name, d.Vendor, d.BranchId, branchName, d.Protocol, d.TimezoneMode, d.DeviceTimezone, d.LastSeenAt, d.IsActive);

    public static DeviceUserMapDto ToDto(this DeviceUserMap m, string employeeCode, string employeeName) =>
        new(m.Id, m.DeviceId, m.DeviceUserCode, m.EmployeeId, employeeCode, employeeName, m.EffectiveFrom, m.EffectiveTo);

    public static IngestBatchDto ToDto(this IngestBatch b, string? deviceCode = null) =>
        new(b.Id, b.Source, b.DeviceId, deviceCode, b.FileId, b.ReceivedCount, b.AcceptedCount,
            b.DuplicateCount, b.RejectedCount, b.Status, b.ErrorSummary, b.CreatedAt, b.CompletedAt);
}
