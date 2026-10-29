using System.Reflection;

namespace Hrms.Application.Organization;

/// <summary>Editable company master fields (input for PUT, base of the detail DTO).</summary>
public record CompanyFields
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? LegalName { get; init; }
    public string? CompanyType { get; init; }
    public string? Industry { get; init; }
    public Guid? ParentCompanyId { get; init; }
    public DateOnly? IncorporationDate { get; init; }
    public string? Website { get; init; }
    public string? OfficialEmail { get; init; }
    public string? ContactNumber { get; init; }
    public string? Cin { get; init; }
    public string? Pan { get; init; }
    public string? Tan { get; init; }
    public string? PfEstablishmentCode { get; init; }
    public string? EsicEmployerCode { get; init; }
    public string? ProfessionalTaxRegNo { get; init; }
    public string? ShopsEstablishmentRegNo { get; init; }
    public string? FactoryLicenceNo { get; init; }
    public string? MsmeUdyamRegNo { get; init; }
    public string? Currency { get; init; }
    public int? FinancialYearStartMonth { get; init; }
    public string? PayrollFrequency { get; init; }
    public int? SalaryPayDay { get; init; }
    public string? DefaultLeavePolicyCode { get; init; }
    public string? DefaultHolidayCalendarCode { get; init; }
    public string? DefaultAttendancePolicyCode { get; init; }
    public string? DefaultShiftName { get; init; }
    public string? WeeklyOffPattern { get; init; }
    public string? WeeklyOffConfigJson { get; init; }
    public int? GraceMinutes { get; init; }
    public bool IsActive { get; init; } = true;
    public uint? RowVersion { get; init; }
}

public sealed record CompanyDetailDto : CompanyFields
{
    public Guid Id { get; init; }
    public Guid? LogoFileId { get; init; }
    public string? ParentCompanyName { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ModifiedAt { get; init; }
    public int BranchCount { get; init; }
    public int EmployeeCount { get; init; }
    public List<CompanyAddressDto> Addresses { get; init; } = [];
    public List<RegistrationDto> Registrations { get; init; } = [];
    public List<CompanyContactDto> Contacts { get; init; } = [];
}

public record BranchFields
{
    public Guid CompanyId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string TimeZone { get; init; } = "Asia/Kolkata";
    public string? BranchType { get; init; }
    public DateOnly? OpeningDate { get; init; }
    public Guid? ManagerEmployeeId { get; init; }
    public Guid? HrEmployeeId { get; init; }
    public string? ContactPhone { get; init; }
    public string? ContactEmail { get; init; }
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? District { get; init; }
    public string? State { get; init; }
    public string? Country { get; init; }
    public string? PinCode { get; init; }
    public string? WorkLocationType { get; init; }
    public string? ClientSiteRef { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public int? GeofenceRadiusMeters { get; init; }
    public string? DefaultShiftName { get; init; }
    public TimeOnly? WorkStart { get; init; }
    public TimeOnly? WorkEnd { get; init; }
    public int? BreakMinutes { get; init; }
    public string? WeeklyOffPattern { get; init; }
    public string? WeeklyOffConfigJson { get; init; }
    public string? HolidayCalendarCode { get; init; }
    public int? GraceMinutes { get; init; }
    public bool? OvertimeAllowed { get; init; }
    public string? AttendanceMode { get; init; }
    public string? AttendancePolicyCode { get; init; }
    public int? PayrollDay { get; init; }
    public string? CostCentre { get; init; }
    public bool IsActive { get; init; } = true;
    public uint? RowVersion { get; init; }
}

public sealed record BranchDetailDto : BranchFields
{
    public Guid Id { get; init; }
    public string? CompanyName { get; init; }
    public string? ManagerName { get; init; }
    public string? HrName { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ModifiedAt { get; init; }
    public int EmployeeCount { get; init; }
    public int DepartmentCount { get; init; }
    public List<RegistrationDto> Registrations { get; init; } = [];
}

public record DepartmentFields
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public Guid? BranchId { get; init; }
    public Guid? ParentDepartmentId { get; init; }
    public string? DepartmentType { get; init; }
    public string? Description { get; init; }
    public string? CostCentre { get; init; }
    public DateOnly? EffectiveDate { get; init; }
    public Guid? ReportingManagerEmployeeId { get; init; }
    public string? DefaultShiftName { get; init; }
    public TimeOnly? WorkStart { get; init; }
    public TimeOnly? WorkEnd { get; init; }
    public int? BreakMinutes { get; init; }
    public string? WeeklyOffPattern { get; init; }
    public string? WeeklyOffConfigJson { get; init; }
    public string? HolidayCalendarCode { get; init; }
    public int? GraceMinutes { get; init; }
    public bool? OvertimeAllowed { get; init; }
    public bool? WfhAllowed { get; init; }
    public string? AttendancePolicyCode { get; init; }
    public Guid? LeaveApproverEmployeeId { get; init; }
    public Guid? AltLeaveApproverEmployeeId { get; init; }
    public Guid? OdApproverEmployeeId { get; init; }
    public Guid? CompOffApproverEmployeeId { get; init; }
    public Guid? OtApproverEmployeeId { get; init; }
    public Guid? RegularizationApproverEmployeeId { get; init; }
    public int? EscalationLevel { get; init; }
    public int? ApprovedHeadcount { get; init; }
    public decimal? BudgetAmount { get; init; }
    public string? WorkArea { get; init; }
    public bool IsActive { get; init; } = true;
    public uint? RowVersion { get; init; }
}

public sealed record DepartmentDetailDto : DepartmentFields
{
    public Guid Id { get; init; }
    public string? BranchName { get; init; }
    public string? ParentDepartmentName { get; init; }
    public Guid? HodEmployeeId { get; init; }
    public Guid? AltHodEmployeeId { get; init; }
    public DateOnly? HodEffectiveFrom { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ModifiedAt { get; init; }
    public int CurrentHeadcount { get; init; }
    public int? Vacancies { get; init; }
    public Dictionary<string, string> EmployeeNames { get; init; } = [];
}

public sealed record CompanyAddressInput(string AddressType, string? Line1, string? Line2, string? Landmark, string? Country, string? State,
    string? District, string? City, string? PinCode, string? ContactPerson, string? ContactEmail, string? ContactPhone);

public sealed record CompanyAddressDto(Guid Id, string AddressType, string? Line1, string? Line2, string? Landmark, string? Country, string? State,
    string? District, string? City, string? PinCode, string? ContactPerson, string? ContactEmail, string? ContactPhone);

public sealed record RegistrationInput(string RegistrationType, string RegistrationNumber, string? Jurisdiction, string? RegistrationSubtype,
    DateOnly? EffectiveFrom, DateOnly? EffectiveTo, bool IsActive = true);

public sealed record RegistrationDto(Guid Id, string RegistrationType, string RegistrationNumber, string? Jurisdiction, string? RegistrationSubtype,
    DateOnly? EffectiveFrom, DateOnly? EffectiveTo, bool IsActive, int? ExpiresInDays);

public sealed record CompanyContactInput(string ContactType, string Name, string? Designation, string? Email, string? Phone);

public sealed record CompanyContactDto(Guid Id, string ContactType, string Name, string? Designation, string? Email, string? Phone);

public sealed record BankAccountInput(string BankName, string? BranchName, string AccountNumber, string? Ifsc, bool IsPrimary);

public sealed record BankAccountDto(Guid Id, string BankName, string? BranchName, string AccountNumber, string? Ifsc, bool IsPrimary);

public sealed record AssignDepartmentRoleCommand(string Role, Guid? EmployeeId, DateOnly EffectiveFrom, string? Reason);

public sealed record DepartmentAssignmentDto(Guid Id, string Role, Guid? EmployeeId, string? EmployeeName, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? Reason, DateTimeOffset CreatedAt);

public sealed record EffectiveSettingDto(string Key, string Label, object? Value, string Source, Guid? SourceId, string? SourceName, bool IsOverride);

public sealed record EffectiveConfigDto(string Level, Guid Id, List<EffectiveSettingDto> Settings);

public sealed record OrgDocumentDto(Guid Id, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedAt);

public sealed record BrandingDto(Guid CompanyId, string CompanyName, string? LegalName, Guid? LogoFileId, string? LogoUrl);

public sealed record WeeklyOffPreviewDto(string? Pattern, string Source, List<DateOnly> OffDays);

internal static class OrgMapper
{
    /// <summary>Copies same-named, same-typed public properties (used to keep DTO ⇄ entity in lock-step without hand-written field lists).</summary>
    public static void Copy(object source, object target, params string[] skip)
    {
        var tProps = target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite).ToDictionary(p => p.Name);
        foreach (var sp in source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (skip.Contains(sp.Name) || !tProps.TryGetValue(sp.Name, out var tp) || !sp.CanRead)
            {
                continue;
            }

            var underlying = Nullable.GetUnderlyingType(sp.PropertyType) ?? sp.PropertyType;
            var tUnderlying = Nullable.GetUnderlyingType(tp.PropertyType) ?? tp.PropertyType;
            if (underlying != tUnderlying || underlying.IsGenericType && underlying != typeof(Nullable<>))
            {
                continue;
            }

            var value = sp.GetValue(source);
            if (value is null && tp.PropertyType.IsValueType && Nullable.GetUnderlyingType(tp.PropertyType) is null)
            {
                continue;
            }

            tp.SetValue(target, value);
        }
    }
}
