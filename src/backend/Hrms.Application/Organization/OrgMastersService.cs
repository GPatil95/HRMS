using System.Text.RegularExpressions;
using Hrms.Application.Abstractions;
using Hrms.Application.Auditing;
using Hrms.Application.Files;
using Hrms.Application.Modules;
using Hrms.Domain.Tenant;
using Hrms.Shared.Primitives;
using Hrms.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Organization;

/// <summary>
/// Company / branch / department master data: detail, update, nested statutory/address/contact/bank records,
/// HOD assignment history, logo + documents (through <see cref="FileService"/>) and effective configuration inheritance.
/// </summary>
public sealed partial class OrgMastersService(ITenantDbContext db, FileService files, IAuditWriter audit, IClock clock)
{
    public const string CompanyOwner = "org.company";
    public const string BranchOwner = "org.branch";
    private const int LogoMinPx = 16;
    private const int LogoMaxPx = 4096;

    public static readonly string[] AddressTypes = ["registered", "corporate", "mailing", "billing", "other"];
    public static readonly string[] RegistrationTypes = ["gstin", "pf", "esic", "pt", "shops_establishment", "factory_licence", "labour_licence", "msme", "lwf", "other"];
    public static readonly string[] ContactTypes = ["signatory", "hr", "payroll", "finance", "other"];
    public static readonly string[] DepartmentRoles = ["hod", "alt_hod"];

    // ---------------- Company ----------------

    public async Task<Result<CompanyDetailDto>> GetCompanyAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Companies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null)
        {
            return Error.NotFound("company-not-found", "Company not found.");
        }

        var dto = new CompanyDetailDto();
        OrgMapper.Copy(c, dto);
        var today = Today;
        return dto with
        {
            RowVersion = c.RowVersion,
            ParentCompanyName = c.ParentCompanyId is { } pid ? await db.Companies.Where(x => x.Id == pid).Select(x => x.Name).FirstOrDefaultAsync(ct) : null,
            BranchCount = await db.Branches.CountAsync(b => b.CompanyId == id, ct),
            EmployeeCount = await db.Employees.CountAsync(e => e.CompanyId == id, ct),
            Addresses = await db.CompanyAddresses.AsNoTracking().Where(a => a.CompanyId == id).OrderBy(a => a.AddressType).ThenBy(a => a.CreatedAt)
                .Select(a => new CompanyAddressDto(a.Id, a.AddressType, a.Line1, a.Line2, a.Landmark, a.Country, a.State, a.District, a.City, a.PinCode, a.ContactPerson, a.ContactEmail, a.ContactPhone))
                .ToListAsync(ct),
            Registrations = MapRegistrations(await db.OrgRegistrations.AsNoTracking().Where(r => r.CompanyId == id).OrderBy(r => r.RegistrationType).ThenBy(r => r.CreatedAt).ToListAsync(ct), today),
            Contacts = await db.CompanyContacts.AsNoTracking().Where(x => x.CompanyId == id).OrderBy(x => x.ContactType).ThenBy(x => x.Name)
                .Select(x => new CompanyContactDto(x.Id, x.ContactType, x.Name, x.Designation, x.Email, x.Phone)).ToListAsync(ct),
        };
    }

    public async Task<Result> UpdateCompanyAsync(Guid id, CompanyFields cmd, CancellationToken ct)
    {
        var c = await db.Companies.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null)
        {
            return Error.NotFound("company-not-found", "Company not found.");
        }

        if (cmd.RowVersion is { } rv && rv != c.RowVersion)
        {
            return Error.Conflict("concurrency.conflict", "The company was changed by someone else. Reload and retry.");
        }

        var errors = new Dictionary<string, string[]>();
        var code = Normalize(cmd.Code);
        Require(errors, "code", code, 32);
        Require(errors, "name", cmd.Name, 200);
        Pattern(errors, "pan", cmd.Pan, PanRegex(), "PAN must look like ABCDE1234F.");
        Pattern(errors, "tan", cmd.Tan, TanRegex(), "TAN must look like ABCD12345E.");
        Pattern(errors, "cin", cmd.Cin, CinRegex(), "Enter a valid CIN (21 characters) or LLPIN (AAA-1234).");
        Pattern(errors, "officialEmail", cmd.OfficialEmail, EmailRegex(), "Enter a valid email address.");
        Pattern(errors, "website", cmd.Website, WebsiteRegex(), "Website must start with http:// or https://.");
        Range(errors, "financialYearStartMonth", cmd.FinancialYearStartMonth, 1, 12);
        Range(errors, "salaryPayDay", cmd.SalaryPayDay, 1, 31);
        Range(errors, "graceMinutes", cmd.GraceMinutes, 0, 240);
        WeeklyOff(errors, cmd.WeeklyOffPattern, cmd.WeeklyOffConfigJson);
        if (code.Length > 0 && code != c.Code && await db.Companies.AnyAsync(x => x.Code == code && x.Id != id, ct))
        {
            return Error.Conflict("company-code-taken", "Company code already exists.");
        }

        if (cmd.ParentCompanyId is { } parent)
        {
            if (parent == id)
            {
                errors["parentCompanyId"] = ["A company cannot be its own parent."];
            }
            else if (!await db.Companies.AnyAsync(x => x.Id == parent, ct))
            {
                errors["parentCompanyId"] = ["Parent company not found."];
            }
            else if (await CompanyAncestorsContainAsync(parent, id, ct))
            {
                errors["parentCompanyId"] = ["This parent would create a circular company hierarchy."];
            }
        }

        if (errors.Count > 0)
        {
            return Invalid(errors);
        }

        OrgMapper.Copy(Clean(cmd), c, "RowVersion", "Id");
        c.Code = code;
        c.Name = cmd.Name.Trim();
        c.WeeklyOffConfigJson = WeeklyOffRules.Normalize(cmd.WeeklyOffConfigJson);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // Addresses
    public async Task<Result<Guid>> AddAddressAsync(Guid companyId, CompanyAddressInput input, CancellationToken ct)
    {
        if (!await db.Companies.AnyAsync(c => c.Id == companyId, ct))
        {
            return Error.NotFound("company-not-found", "Company not found.");
        }

        if (ValidateAddress(input) is { } err)
        {
            return err;
        }

        var a = new CompanyAddress { CompanyId = companyId };
        ApplyAddress(a, input);
        db.CompanyAddresses.Add(a);
        await db.SaveChangesAsync(ct);
        return a.Id;
    }

    public async Task<Result> UpdateAddressAsync(Guid companyId, Guid addressId, CompanyAddressInput input, CancellationToken ct)
    {
        var a = await db.CompanyAddresses.FirstOrDefaultAsync(x => x.Id == addressId && x.CompanyId == companyId, ct);
        if (a is null)
        {
            return Error.NotFound("address-not-found", "Address not found.");
        }

        if (ValidateAddress(input) is { } err)
        {
            return err;
        }

        ApplyAddress(a, input);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAddressAsync(Guid companyId, Guid addressId, CancellationToken ct)
    {
        var a = await db.CompanyAddresses.FirstOrDefaultAsync(x => x.Id == addressId && x.CompanyId == companyId, ct);
        if (a is null)
        {
            return Error.NotFound("address-not-found", "Address not found.");
        }

        db.CompanyAddresses.Remove(a);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // Registrations (company or branch owner)
    public async Task<Result<Guid>> AddRegistrationAsync(Guid? companyId, Guid? branchId, RegistrationInput input, CancellationToken ct)
    {
        var ownerError = await EnsureRegistrationOwnerAsync(companyId, branchId, ct);
        if (ownerError is not null)
        {
            return ownerError;
        }

        var (normalized, err) = await ValidateRegistrationAsync(companyId, branchId, null, input, ct);
        if (err is not null)
        {
            return err;
        }

        var r = new OrgRegistration { CompanyId = companyId, BranchId = branchId };
        ApplyRegistration(r, normalized!);
        db.OrgRegistrations.Add(r);
        await db.SaveChangesAsync(ct);
        return r.Id;
    }

    public async Task<Result> UpdateRegistrationAsync(Guid? companyId, Guid? branchId, Guid registrationId, RegistrationInput input, CancellationToken ct)
    {
        var r = await db.OrgRegistrations.FirstOrDefaultAsync(x => x.Id == registrationId && x.CompanyId == companyId && x.BranchId == branchId, ct);
        if (r is null)
        {
            return Error.NotFound("registration-not-found", "Registration not found.");
        }

        var (normalized, err) = await ValidateRegistrationAsync(companyId, branchId, registrationId, input, ct);
        if (err is not null)
        {
            return err;
        }

        ApplyRegistration(r, normalized!);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteRegistrationAsync(Guid? companyId, Guid? branchId, Guid registrationId, CancellationToken ct)
    {
        var r = await db.OrgRegistrations.FirstOrDefaultAsync(x => x.Id == registrationId && x.CompanyId == companyId && x.BranchId == branchId, ct);
        if (r is null)
        {
            return Error.NotFound("registration-not-found", "Registration not found.");
        }

        db.OrgRegistrations.Remove(r);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // Contacts
    public async Task<Result<Guid>> AddContactAsync(Guid companyId, CompanyContactInput input, CancellationToken ct)
    {
        if (!await db.Companies.AnyAsync(c => c.Id == companyId, ct))
        {
            return Error.NotFound("company-not-found", "Company not found.");
        }

        if (ValidateContact(input) is { } err)
        {
            return err;
        }

        var x = new CompanyContact { CompanyId = companyId };
        ApplyContact(x, input);
        db.CompanyContacts.Add(x);
        await db.SaveChangesAsync(ct);
        return x.Id;
    }

    public async Task<Result> UpdateContactAsync(Guid companyId, Guid contactId, CompanyContactInput input, CancellationToken ct)
    {
        var x = await db.CompanyContacts.FirstOrDefaultAsync(c => c.Id == contactId && c.CompanyId == companyId, ct);
        if (x is null)
        {
            return Error.NotFound("contact-not-found", "Contact not found.");
        }

        if (ValidateContact(input) is { } err)
        {
            return err;
        }

        ApplyContact(x, input);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteContactAsync(Guid companyId, Guid contactId, CancellationToken ct)
    {
        var x = await db.CompanyContacts.FirstOrDefaultAsync(c => c.Id == contactId && c.CompanyId == companyId, ct);
        if (x is null)
        {
            return Error.NotFound("contact-not-found", "Contact not found.");
        }

        db.CompanyContacts.Remove(x);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // Bank accounts (edit permission only — enforced at the endpoint)
    public async Task<Result<List<BankAccountDto>>> ListBankAccountsAsync(Guid companyId, CancellationToken ct)
    {
        if (!await db.Companies.AnyAsync(c => c.Id == companyId, ct))
        {
            return Error.NotFound("company-not-found", "Company not found.");
        }

        return await db.CompanyBankAccounts.AsNoTracking().Where(b => b.CompanyId == companyId).OrderByDescending(b => b.IsPrimary).ThenBy(b => b.BankName)
            .Select(b => new BankAccountDto(b.Id, b.BankName, b.BranchName, b.AccountNumber, b.Ifsc, b.IsPrimary)).ToListAsync(ct);
    }

    public async Task<Result<Guid>> AddBankAccountAsync(Guid companyId, BankAccountInput input, CancellationToken ct)
    {
        if (!await db.Companies.AnyAsync(c => c.Id == companyId, ct))
        {
            return Error.NotFound("company-not-found", "Company not found.");
        }

        if (ValidateBank(input) is { } err)
        {
            return err;
        }

        var b = new CompanyBankAccount { CompanyId = companyId };
        await ApplyBankAsync(b, input, ct);
        db.CompanyBankAccounts.Add(b);
        await db.SaveChangesAsync(ct);
        return b.Id;
    }

    public async Task<Result> UpdateBankAccountAsync(Guid companyId, Guid accountId, BankAccountInput input, CancellationToken ct)
    {
        var b = await db.CompanyBankAccounts.FirstOrDefaultAsync(x => x.Id == accountId && x.CompanyId == companyId, ct);
        if (b is null)
        {
            return Error.NotFound("bank-account-not-found", "Bank account not found.");
        }

        if (ValidateBank(input) is { } err)
        {
            return err;
        }

        await ApplyBankAsync(b, input, ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteBankAccountAsync(Guid companyId, Guid accountId, CancellationToken ct)
    {
        var b = await db.CompanyBankAccounts.FirstOrDefaultAsync(x => x.Id == accountId && x.CompanyId == companyId, ct);
        if (b is null)
        {
            return Error.NotFound("bank-account-not-found", "Bank account not found.");
        }

        db.CompanyBankAccounts.Remove(b);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ---------------- Logo ----------------

    public async Task<Result<Guid>> UploadLogoAsync(Guid companyId, string fileName, string? contentType, Stream content, long length, CancellationToken ct)
    {
        var c = await db.Companies.FirstOrDefaultAsync(x => x.Id == companyId, ct);
        if (c is null)
        {
            return Error.NotFound("company-not-found", "Company not found.");
        }

        if (length is <= 0 or > 2 * 1024 * 1024)
        {
            return Error.Validation("file-size-invalid", "Logo must be between 1 byte and 2 MB.");
        }

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (contentType is not null && contentType is not ("image/png" or "image/jpeg" or "image/webp"))
        {
            return Error.Validation("file-type-not-allowed", "Logo must be a PNG, JPEG or WebP image.");
        }

        var size = ImageProbe.TryGetSize(buffer.ToArray());
        if (size is not { } dims)
        {
            return Error.Validation("image-invalid", "The logo image could not be read. Upload a valid PNG, JPEG or WebP file.");
        }

        if (dims.Width < LogoMinPx || dims.Height < LogoMinPx || dims.Width > LogoMaxPx || dims.Height > LogoMaxPx)
        {
            return Error.Validation("image-dimensions", $"Logo must be between {LogoMinPx}×{LogoMinPx} and {LogoMaxPx}×{LogoMaxPx} pixels (got {dims.Width}×{dims.Height}).");
        }

        buffer.Position = 0;
        var uploaded = await files.UploadAsync(new FileUploadRequest(OrganizationModule.LogoPurpose, fileName, contentType, buffer, buffer.Length, CompanyOwner, companyId.ToString()), ct);
        if (!uploaded.IsSuccess)
        {
            return uploaded.Error!;
        }

        var previous = c.LogoFileId;
        c.LogoFileId = uploaded.Value!.Id;
        audit.Add(OrganizationModule.ModuleCode, nameof(Company), c.Id.ToString(), previous is null ? "logo.upload" : "logo.replace",
            new { fileId = c.LogoFileId, previous, dims.Width, dims.Height });
        await db.SaveChangesAsync(ct);

        // Old logo is soft-deleted only after the new reference is committed.
        if (previous is { } old)
        {
            await files.DeleteAsync(old, ct);
        }

        return c.LogoFileId.Value;
    }

    public async Task<Result> RemoveLogoAsync(Guid companyId, CancellationToken ct)
    {
        var c = await db.Companies.FirstOrDefaultAsync(x => x.Id == companyId, ct);
        if (c is null)
        {
            return Error.NotFound("company-not-found", "Company not found.");
        }

        if (c.LogoFileId is not { } fileId)
        {
            return Result.Success();
        }

        c.LogoFileId = null;
        audit.Add(OrganizationModule.ModuleCode, nameof(Company), c.Id.ToString(), "logo.remove", new { fileId });
        await db.SaveChangesAsync(ct);
        await files.DeleteAsync(fileId, ct);
        return Result.Success();
    }

    public async Task<Result<FileDownload>> DownloadLogoAsync(Guid companyId, CancellationToken ct)
    {
        var fileId = await db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.LogoFileId).FirstOrDefaultAsync(ct);
        return fileId is { } id ? await files.DownloadAsync(id, ct) : Error.NotFound("logo-not-found", "This company has no logo.");
    }

    /// <summary>
    /// Branding for a document/screen. Resolves from the employee's (or explicit) company — never from the signed-in user.
    /// </summary>
    public async Task<Result<BrandingDto>> ResolveBrandingAsync(Guid? companyId, Guid? employeeId, CancellationToken ct)
    {
        if (employeeId is { } eid)
        {
            companyId = await db.Employees.AsNoTracking().Where(e => e.Id == eid).Select(e => (Guid?)e.CompanyId).FirstOrDefaultAsync(ct);
            if (companyId is null)
            {
                return Error.NotFound("employee-not-found", "Employee not found.");
            }
        }

        if (companyId is null)
        {
            return Error.Validation("branding-target-required", "Specify a companyId or employeeId.");
        }

        var c = await db.Companies.AsNoTracking().Where(x => x.Id == companyId).Select(x => new { x.Id, x.Name, x.LegalName, x.LogoFileId }).FirstOrDefaultAsync(ct);
        return c is null
            ? Error.NotFound("company-not-found", "Company not found.")
            : new BrandingDto(c.Id, c.Name, c.LegalName, c.LogoFileId, c.LogoFileId is null ? null : $"/api/v1/org/companies/{c.Id}/logo");
    }

    // ---------------- Documents ----------------

    public async Task<Result<List<OrgDocumentDto>>> ListDocumentsAsync(string owner, Guid ownerId, CancellationToken ct)
    {
        if (await EnsureOwnerAsync(owner, ownerId, ct) is { } err)
        {
            return err;
        }

        var purpose = DocumentPurpose(owner);
        var key = ownerId.ToString();
        return await db.Files.AsNoTracking()
            .Where(f => f.Purpose == purpose && f.OwnerEntityType == owner && f.OwnerEntityId == key && !f.IsDeleted && f.Status == FileStatus.Available)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new OrgDocumentDto(f.Id, f.OriginalFileName, f.ContentType, f.SizeBytes, f.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<Result<FileMetadataDto>> UploadDocumentAsync(string owner, Guid ownerId, string fileName, string? contentType, Stream content, long length, CancellationToken ct)
    {
        if (await EnsureOwnerAsync(owner, ownerId, ct) is { } err)
        {
            return err;
        }

        return await files.UploadAsync(new FileUploadRequest(DocumentPurpose(owner), fileName, contentType, content, length, owner, ownerId.ToString()), ct);
    }

    public async Task<Result<FileDownload>> DownloadDocumentAsync(string owner, Guid ownerId, Guid fileId, CancellationToken ct) =>
        await OwnsDocumentAsync(owner, ownerId, fileId, ct) ? await files.DownloadAsync(fileId, ct) : Error.NotFound("file-not-found", "File not found.");

    public async Task<Result> DeleteDocumentAsync(string owner, Guid ownerId, Guid fileId, CancellationToken ct) =>
        await OwnsDocumentAsync(owner, ownerId, fileId, ct) ? await files.DeleteAsync(fileId, ct) : Error.NotFound("file-not-found", "File not found.");

    // ---------------- Branch ----------------

    public async Task<Result<BranchDetailDto>> GetBranchAsync(Guid id, CancellationToken ct)
    {
        var b = await db.Branches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b is null)
        {
            return Error.NotFound("branch-not-found", "Branch not found.");
        }

        var dto = new BranchDetailDto();
        OrgMapper.Copy(b, dto);
        var names = await EmployeeNamesAsync([b.ManagerEmployeeId, b.HrEmployeeId], ct);
        return dto with
        {
            RowVersion = b.RowVersion,
            CompanyName = await db.Companies.Where(c => c.Id == b.CompanyId).Select(c => c.Name).FirstOrDefaultAsync(ct),
            ManagerName = b.ManagerEmployeeId is { } m ? names.GetValueOrDefault(m.ToString()) : null,
            HrName = b.HrEmployeeId is { } h ? names.GetValueOrDefault(h.ToString()) : null,
            EmployeeCount = await db.Employees.CountAsync(e => e.BranchId == id, ct),
            DepartmentCount = await db.Departments.CountAsync(d => d.BranchId == id, ct),
            Registrations = MapRegistrations(await db.OrgRegistrations.AsNoTracking().Where(r => r.BranchId == id).OrderBy(r => r.RegistrationType).ToListAsync(ct), Today),
        };
    }

    public async Task<Result> UpdateBranchAsync(Guid id, BranchFields cmd, CancellationToken ct)
    {
        var b = await db.Branches.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b is null)
        {
            return Error.NotFound("branch-not-found", "Branch not found.");
        }

        if (cmd.RowVersion is { } rv && rv != b.RowVersion)
        {
            return Error.Conflict("concurrency.conflict", "The branch was changed by someone else. Reload and retry.");
        }

        var errors = new Dictionary<string, string[]>();
        var code = Normalize(cmd.Code);
        Require(errors, "code", code, 32);
        Require(errors, "name", cmd.Name, 200);
        Pattern(errors, "contactEmail", cmd.ContactEmail, EmailRegex(), "Enter a valid email address.");
        Pattern(errors, "pinCode", cmd.PinCode, PinRegex(), "PIN code must be 6 digits.");
        RangeD(errors, "latitude", cmd.Latitude, -90, 90);
        RangeD(errors, "longitude", cmd.Longitude, -180, 180);
        Range(errors, "geofenceRadiusMeters", cmd.GeofenceRadiusMeters, 10, 100_000);
        Range(errors, "breakMinutes", cmd.BreakMinutes, 0, 480);
        Range(errors, "graceMinutes", cmd.GraceMinutes, 0, 240);
        Range(errors, "payrollDay", cmd.PayrollDay, 1, 31);
        WeeklyOff(errors, cmd.WeeklyOffPattern, cmd.WeeklyOffConfigJson);
        if (string.IsNullOrWhiteSpace(cmd.TimeZone) || !IsValidTimeZone(cmd.TimeZone))
        {
            errors["timeZone"] = ["Select a valid time zone."];
        }

        if (!await db.Companies.AnyAsync(c => c.Id == cmd.CompanyId, ct))
        {
            errors["companyId"] = ["Company not found."];
        }

        await ValidateEmployeeRefAsync(errors, "managerEmployeeId", cmd.ManagerEmployeeId, ct);
        await ValidateEmployeeRefAsync(errors, "hrEmployeeId", cmd.HrEmployeeId, ct);
        if (code.Length > 0 && code != b.Code && await db.Branches.AnyAsync(x => x.Code == code && x.Id != id, ct))
        {
            return Error.Conflict("branch-code-taken", "Branch code already exists.");
        }

        if (cmd.CompanyId != b.CompanyId && await db.Employees.AnyAsync(e => e.BranchId == id, ct))
        {
            errors["companyId"] = ["A branch with employees cannot be moved to another company."];
        }

        if (errors.Count > 0)
        {
            return Invalid(errors);
        }

        OrgMapper.Copy(Clean(cmd), b, "RowVersion", "Id");
        b.Code = code;
        b.Name = cmd.Name.Trim();
        b.WeeklyOffConfigJson = WeeklyOffRules.Normalize(cmd.WeeklyOffConfigJson);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ---------------- Department ----------------

    public async Task<Result<DepartmentDetailDto>> GetDepartmentAsync(Guid id, CancellationToken ct)
    {
        var d = await db.Departments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null)
        {
            return Error.NotFound("department-not-found", "Department not found.");
        }

        var dto = new DepartmentDetailDto();
        OrgMapper.Copy(d, dto);
        var current = await db.Employees.CountAsync(e => e.DepartmentId == id && (e.Status == EmployeeStatus.Active || e.Status == EmployeeStatus.Notice), ct);
        return dto with
        {
            RowVersion = d.RowVersion,
            BranchName = d.BranchId is { } bid ? await db.Branches.Where(b => b.Id == bid).Select(b => b.Name).FirstOrDefaultAsync(ct) : null,
            ParentDepartmentName = d.ParentDepartmentId is { } pid ? await db.Departments.Where(x => x.Id == pid).Select(x => x.Name).FirstOrDefaultAsync(ct) : null,
            CurrentHeadcount = current,
            Vacancies = d.ApprovedHeadcount is { } approved ? Math.Max(0, approved - current) : null,
            EmployeeNames = await EmployeeNamesAsync(
                [d.HodEmployeeId, d.AltHodEmployeeId, d.ReportingManagerEmployeeId, d.LeaveApproverEmployeeId, d.AltLeaveApproverEmployeeId,
                 d.OdApproverEmployeeId, d.CompOffApproverEmployeeId, d.OtApproverEmployeeId, d.RegularizationApproverEmployeeId], ct),
        };
    }

    public async Task<Result> UpdateDepartmentAsync(Guid id, DepartmentFields cmd, CancellationToken ct)
    {
        var d = await db.Departments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null)
        {
            return Error.NotFound("department-not-found", "Department not found.");
        }

        if (cmd.RowVersion is { } rv && rv != d.RowVersion)
        {
            return Error.Conflict("concurrency.conflict", "The department was changed by someone else. Reload and retry.");
        }

        var errors = new Dictionary<string, string[]>();
        var code = Normalize(cmd.Code);
        Require(errors, "code", code, 32);
        Require(errors, "name", cmd.Name, 200);
        Range(errors, "breakMinutes", cmd.BreakMinutes, 0, 480);
        Range(errors, "graceMinutes", cmd.GraceMinutes, 0, 240);
        Range(errors, "escalationLevel", cmd.EscalationLevel, 1, 10);
        Range(errors, "approvedHeadcount", cmd.ApprovedHeadcount, 0, 1_000_000);
        if (cmd.BudgetAmount is < 0)
        {
            errors["budgetAmount"] = ["Budget cannot be negative."];
        }

        WeeklyOff(errors, cmd.WeeklyOffPattern, cmd.WeeklyOffConfigJson);
        if (cmd.BranchId is { } bid && !await db.Branches.AnyAsync(b => b.Id == bid, ct))
        {
            errors["branchId"] = ["Branch not found."];
        }

        if (cmd.ParentDepartmentId is { } parent)
        {
            if (parent == id)
            {
                errors["parentDepartmentId"] = ["A department cannot be its own parent."];
            }
            else if (!await db.Departments.AnyAsync(x => x.Id == parent, ct))
            {
                errors["parentDepartmentId"] = ["Parent department not found."];
            }
            else if (await DepartmentAncestorsContainAsync(parent, id, ct))
            {
                errors["parentDepartmentId"] = ["This parent would create a circular department hierarchy."];
            }
        }

        foreach (var (field, value) in new (string, Guid?)[]
                 {
                     ("reportingManagerEmployeeId", cmd.ReportingManagerEmployeeId), ("leaveApproverEmployeeId", cmd.LeaveApproverEmployeeId),
                     ("altLeaveApproverEmployeeId", cmd.AltLeaveApproverEmployeeId), ("odApproverEmployeeId", cmd.OdApproverEmployeeId),
                     ("compOffApproverEmployeeId", cmd.CompOffApproverEmployeeId), ("otApproverEmployeeId", cmd.OtApproverEmployeeId),
                     ("regularizationApproverEmployeeId", cmd.RegularizationApproverEmployeeId),
                 })
        {
            await ValidateEmployeeRefAsync(errors, field, value, ct);
        }

        if (cmd.LeaveApproverEmployeeId is { } la && la == cmd.AltLeaveApproverEmployeeId)
        {
            errors["altLeaveApproverEmployeeId"] = ["Alternate approver must differ from the primary approver."];
        }

        if (code.Length > 0 && code != d.Code && await db.Departments.AnyAsync(x => x.Code == code && x.Id != id, ct))
        {
            return Error.Conflict("department-code-taken", "Department code already exists.");
        }

        if (errors.Count > 0)
        {
            return Invalid(errors);
        }

        OrgMapper.Copy(Clean(cmd), d, "RowVersion", "Id");
        d.Code = code;
        d.Name = cmd.Name.Trim();
        d.WeeklyOffConfigJson = WeeklyOffRules.Normalize(cmd.WeeklyOffConfigJson);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<List<DepartmentAssignmentDto>>> ListDepartmentAssignmentsAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Departments.AnyAsync(x => x.Id == id, ct))
        {
            return Error.NotFound("department-not-found", "Department not found.");
        }

        return await (from h in db.DepartmentHodHistory.AsNoTracking()
                      where h.DepartmentId == id
                      join e in db.Employees.AsNoTracking() on h.EmployeeId equals e.Id into emp
                      from e in emp.DefaultIfEmpty()
                      orderby h.EffectiveFrom descending, h.CreatedAt descending
                      select new DepartmentAssignmentDto(h.Id, h.Role, h.EmployeeId, e == null ? null : e.DisplayName, h.EffectiveFrom, h.EffectiveTo, h.Reason, h.CreatedAt))
            .ToListAsync(ct);
    }

    /// <summary>Assigns (or clears) the HOD / alternate HOD, closing the previous open assignment and keeping full history.</summary>
    public async Task<Result> AssignDepartmentRoleAsync(Guid id, AssignDepartmentRoleCommand cmd, CancellationToken ct)
    {
        var d = await db.Departments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null)
        {
            return Error.NotFound("department-not-found", "Department not found.");
        }

        var role = (cmd.Role ?? string.Empty).Trim().ToLowerInvariant();
        if (!DepartmentRoles.Contains(role))
        {
            return Error.Validation("invalid-role", "Role must be 'hod' or 'alt_hod'.");
        }

        var errors = new Dictionary<string, string[]>();
        await ValidateEmployeeRefAsync(errors, "employeeId", cmd.EmployeeId, ct);
        if (errors.Count > 0)
        {
            return Invalid(errors);
        }

        var current = role == "hod" ? d.HodEmployeeId : d.AltHodEmployeeId;
        var other = role == "hod" ? d.AltHodEmployeeId : d.HodEmployeeId;
        if (cmd.EmployeeId is { } eid && eid == current)
        {
            return Error.Conflict("already-assigned", "This employee already holds the role.");
        }

        if (cmd.EmployeeId is { } e2 && e2 == other)
        {
            return Error.Validation("hod-same-as-alternate", "HOD and alternate HOD must be different employees.");
        }

        var open = await db.DepartmentHodHistory.Where(h => h.DepartmentId == id && h.Role == role && h.EffectiveTo == null)
            .OrderByDescending(h => h.EffectiveFrom).FirstOrDefaultAsync(ct);
        if (open is not null)
        {
            if (cmd.EffectiveFrom <= open.EffectiveFrom)
            {
                return Error.Validation("effective-date-invalid", $"Effective date must be after the current assignment start ({open.EffectiveFrom:yyyy-MM-dd}).");
            }

            open.EffectiveTo = cmd.EffectiveFrom.AddDays(-1);
        }
        else if (current is null && cmd.EmployeeId is null)
        {
            return Result.Success();
        }

        if (cmd.EmployeeId is not null)
        {
            db.DepartmentHodHistory.Add(new DepartmentHodHistory
            {
                DepartmentId = id,
                Role = role,
                EmployeeId = cmd.EmployeeId,
                EffectiveFrom = cmd.EffectiveFrom,
                Reason = string.IsNullOrWhiteSpace(cmd.Reason) ? null : cmd.Reason.Trim()[..Math.Min(cmd.Reason.Trim().Length, 400)],
            });
        }

        if (role == "hod")
        {
            d.HodEmployeeId = cmd.EmployeeId;
            d.HodEffectiveFrom = cmd.EmployeeId is null ? null : cmd.EffectiveFrom;
        }
        else
        {
            d.AltHodEmployeeId = cmd.EmployeeId;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<WeeklyOffPreviewDto>> PreviewWeeklyOffAsync(string level, Guid id, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber > 366)
        {
            return Error.Validation("range-invalid", "Preview range must be between 1 and 366 days.");
        }

        var cfg = await ResolveEffectiveAsync(level, id, ct);
        if (!cfg.IsSuccess)
        {
            return cfg.Error!;
        }

        var pattern = cfg.Value!.Settings.First(s => s.Key == "weeklyOffPattern");
        var json = cfg.Value.Settings.First(s => s.Key == "weeklyOffConfig");
        return new WeeklyOffPreviewDto(pattern.Value as string, pattern.Source, WeeklyOffRules.OffDays(pattern.Value as string, json.Value as string, from, to));
    }

    // ---------------- Effective configuration ----------------

    /// <summary>
    /// Resolves the effective working configuration for a company / branch / department / employee.
    /// Precedence: department → branch → company (employee resolves through its department and branch). A null value means "inherit".
    /// </summary>
    public async Task<Result<EffectiveConfigDto>> ResolveEffectiveAsync(string level, Guid id, CancellationToken ct)
    {
        Company? company = null;
        Branch? branch = null;
        Department? dept = null;
        switch (level)
        {
            case "company":
                company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
                if (company is null)
                {
                    return Error.NotFound("company-not-found", "Company not found.");
                }

                break;
            case "branch":
                branch = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
                if (branch is null)
                {
                    return Error.NotFound("branch-not-found", "Branch not found.");
                }

                break;
            case "department":
                dept = await db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
                if (dept is null)
                {
                    return Error.NotFound("department-not-found", "Department not found.");
                }

                if (dept.BranchId is { } db1)
                {
                    branch = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == db1, ct);
                }

                break;
            case "employee":
                var emp = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
                if (emp is null)
                {
                    return Error.NotFound("employee-not-found", "Employee not found.");
                }

                branch = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == emp.BranchId, ct);
                if (emp.DepartmentId is { } did)
                {
                    dept = await db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == did, ct);
                }

                company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == emp.CompanyId, ct);
                break;
            default:
                return Error.Validation("invalid-level", "Level must be company, branch, department or employee.");
        }

        if (company is null && branch is not null)
        {
            company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == branch.CompanyId, ct);
        }

        var settings = new List<EffectiveSettingDto>
        {
            Resolve("shift", "Default shift", level, company?.DefaultShiftName, branch?.DefaultShiftName, dept?.DefaultShiftName, company, branch, dept),
            Resolve("workStart", "Work start", level, null, branch?.WorkStart, dept?.WorkStart, company, branch, dept),
            Resolve("workEnd", "Work end", level, null, branch?.WorkEnd, dept?.WorkEnd, company, branch, dept),
            Resolve("breakMinutes", "Break (minutes)", level, null, branch?.BreakMinutes, dept?.BreakMinutes, company, branch, dept),
            Resolve("weeklyOffPattern", "Weekly-off pattern", level, company?.WeeklyOffPattern, branch?.WeeklyOffPattern, dept?.WeeklyOffPattern, company, branch, dept),
            Resolve("weeklyOffConfig", "Weekly-off rules", level, company?.WeeklyOffPattern is null ? null : company.WeeklyOffConfigJson,
                branch?.WeeklyOffPattern is null ? null : branch.WeeklyOffConfigJson, dept?.WeeklyOffPattern is null ? null : dept.WeeklyOffConfigJson, company, branch, dept),
            Resolve("holidayCalendar", "Holiday calendar", level, company?.DefaultHolidayCalendarCode, branch?.HolidayCalendarCode, dept?.HolidayCalendarCode, company, branch, dept),
            Resolve("graceMinutes", "Grace (minutes)", level, company?.GraceMinutes, branch?.GraceMinutes, dept?.GraceMinutes, company, branch, dept),
            Resolve("overtimeAllowed", "Overtime allowed", level, null, branch?.OvertimeAllowed, dept?.OvertimeAllowed, company, branch, dept),
            Resolve("attendancePolicy", "Attendance policy", level, company?.DefaultAttendancePolicyCode, branch?.AttendancePolicyCode, dept?.AttendancePolicyCode, company, branch, dept),
        };

        // The weekly-off rule set always follows the source of the pattern so the two never mix levels.
        var p = settings.First(s => s.Key == "weeklyOffPattern");
        var ruleIdx = settings.FindIndex(s => s.Key == "weeklyOffConfig");
        settings[ruleIdx] = settings[ruleIdx] with
        {
            Source = p.Source, SourceId = p.SourceId, SourceName = p.SourceName, IsOverride = p.IsOverride,
            Value = p.Source switch
            {
                "department" => dept?.WeeklyOffConfigJson,
                "branch" => branch?.WeeklyOffConfigJson,
                "company" => company?.WeeklyOffConfigJson,
                _ => null,
            },
        };

        return new EffectiveConfigDto(level, id, settings);
    }

    private static EffectiveSettingDto Resolve<T>(string key, string label, string level, T? companyValue, T? branchValue, T? deptValue,
        Company? company, Branch? branch, Department? dept)
    {
        static bool Has(T? v) => v is not null && (v is not string s || !string.IsNullOrWhiteSpace(s));

        var levelRank = level switch { "company" => 0, "branch" => 1, _ => 2 };
        (object? Value, string Source, Guid? Id, string? Name, int Rank)? hit =
            Has(deptValue) && dept is not null ? (deptValue, "department", dept.Id, dept.Name, 2)
            : Has(branchValue) && branch is not null ? (branchValue, "branch", branch.Id, branch.Name, 1)
            : Has(companyValue) && company is not null ? (companyValue, "company", company.Id, company.Name, 0)
            : null;

        return hit is { } h
            ? new EffectiveSettingDto(key, label, h.Value, h.Source, h.Id, h.Name, h.Rank == levelRank && level != "employee")
            : new EffectiveSettingDto(key, label, null, "not_set", null, null, false);
    }

    // ---------------- helpers ----------------

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    private static List<RegistrationDto> MapRegistrations(List<OrgRegistration> rows, DateOnly today) =>
        rows.Select(r => new RegistrationDto(r.Id, r.RegistrationType, r.RegistrationNumber, r.Jurisdiction, r.RegistrationSubtype, r.EffectiveFrom, r.EffectiveTo, r.IsActive,
            r.EffectiveTo is { } to ? to.DayNumber - today.DayNumber : null)).ToList();

    private async Task<Dictionary<string, string>> EmployeeNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0)
        {
            return [];
        }

        return await db.Employees.AsNoTracking().Where(e => list.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id.ToString(), e => $"{e.DisplayName} ({e.EmployeeCode})", ct);
    }

    private async Task ValidateEmployeeRefAsync(Dictionary<string, string[]> errors, string field, Guid? employeeId, CancellationToken ct)
    {
        if (employeeId is not { } eid)
        {
            return;
        }

        // Tenant DBs are physically isolated, so an id that is absent here (incl. another tenant's employee) is rejected as not found.
        var status = await db.Employees.AsNoTracking().Where(e => e.Id == eid).Select(e => (EmployeeStatus?)e.Status).FirstOrDefaultAsync(ct);
        if (status is null)
        {
            errors[field] = ["Employee not found."];
        }
        else if (status is not (EmployeeStatus.Active or EmployeeStatus.Notice))
        {
            errors[field] = ["Only active employees can be assigned."];
        }
    }

    private async Task<bool> DepartmentAncestorsContainAsync(Guid start, Guid target, CancellationToken ct)
    {
        var parents = await db.Departments.AsNoTracking().Select(d => new { d.Id, d.ParentDepartmentId }).ToDictionaryAsync(d => d.Id, d => d.ParentDepartmentId, ct);
        Guid? cur = start;
        for (var i = 0; cur is { } c && i < 10_000; i++)
        {
            if (c == target)
            {
                return true;
            }

            cur = parents.GetValueOrDefault(c);
        }

        return false;
    }

    private async Task<bool> CompanyAncestorsContainAsync(Guid start, Guid target, CancellationToken ct)
    {
        var parents = await db.Companies.AsNoTracking().Select(d => new { d.Id, d.ParentCompanyId }).ToDictionaryAsync(d => d.Id, d => d.ParentCompanyId, ct);
        Guid? cur = start;
        for (var i = 0; cur is { } c && i < 10_000; i++)
        {
            if (c == target)
            {
                return true;
            }

            cur = parents.GetValueOrDefault(c);
        }

        return false;
    }

    private async Task<Error?> EnsureRegistrationOwnerAsync(Guid? companyId, Guid? branchId, CancellationToken ct)
    {
        if (companyId is { } cid && !await db.Companies.AnyAsync(c => c.Id == cid, ct))
        {
            return Error.NotFound("company-not-found", "Company not found.");
        }

        if (branchId is { } bid && !await db.Branches.AnyAsync(b => b.Id == bid, ct))
        {
            return Error.NotFound("branch-not-found", "Branch not found.");
        }

        return null;
    }

    private async Task<(RegistrationInput? Normalized, Error? Error)> ValidateRegistrationAsync(Guid? companyId, Guid? branchId, Guid? selfId, RegistrationInput input, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var type = (input.RegistrationType ?? string.Empty).Trim().ToLowerInvariant();
        var number = (input.RegistrationNumber ?? string.Empty).Trim().ToUpperInvariant();
        if (!RegistrationTypes.Contains(type))
        {
            errors["registrationType"] = ["Select a registration type."];
        }

        Require(errors, "registrationNumber", number, 60);
        var jurisdiction = input.Jurisdiction?.Trim();
        if (type == "gstin" && number.Length > 0)
        {
            if (!GstinRegex().IsMatch(number))
            {
                errors["registrationNumber"] = ["GSTIN must be 15 characters, e.g. 29ABCDE1234F1Z5."];
            }
            else
            {
                var pan = companyId is { } cid
                    ? await db.Companies.Where(c => c.Id == cid).Select(c => c.Pan).FirstOrDefaultAsync(ct)
                    : await (from b in db.Branches where b.Id == branchId join c in db.Companies on b.CompanyId equals c.Id select c.Pan).FirstOrDefaultAsync(ct);
                if (!string.IsNullOrWhiteSpace(pan) && !string.Equals(number[2..12], pan, StringComparison.OrdinalIgnoreCase))
                {
                    errors["registrationNumber"] = ["GSTIN does not contain the company PAN."];
                }

                jurisdiction = string.IsNullOrWhiteSpace(jurisdiction) ? $"State code {number[..2]}" : jurisdiction;
            }
        }

        if (input.EffectiveFrom is { } f && input.EffectiveTo is { } t && t < f)
        {
            errors["effectiveTo"] = ["Valid-to must be on or after valid-from."];
        }

        if (errors.Count > 0)
        {
            return (null, Invalid(errors));
        }

        var duplicate = await db.OrgRegistrations.AnyAsync(r => r.Id != selfId && r.CompanyId == companyId && r.BranchId == branchId
                                                               && r.RegistrationType == type && r.RegistrationNumber == number, ct);
        if (duplicate)
        {
            return (null, Error.Conflict("registration-duplicate", "This registration number already exists."));
        }

        return (input with { RegistrationType = type, RegistrationNumber = number, Jurisdiction = jurisdiction }, null);
    }

    private static void ApplyRegistration(OrgRegistration r, RegistrationInput i)
    {
        r.RegistrationType = i.RegistrationType;
        r.RegistrationNumber = i.RegistrationNumber;
        r.Jurisdiction = Trim(i.Jurisdiction);
        r.RegistrationSubtype = Trim(i.RegistrationSubtype);
        r.EffectiveFrom = i.EffectiveFrom;
        r.EffectiveTo = i.EffectiveTo;
        r.IsActive = i.IsActive;
    }

    private static Error? ValidateAddress(CompanyAddressInput i)
    {
        var errors = new Dictionary<string, string[]>();
        if (!AddressTypes.Contains(i.AddressType))
        {
            errors["addressType"] = ["Select an address type."];
        }

        Require(errors, "line1", i.Line1, 200);
        Pattern(errors, "pinCode", i.PinCode, PinRegex(), "PIN code must be 6 digits.");
        Pattern(errors, "contactEmail", i.ContactEmail, EmailRegex(), "Enter a valid email address.");
        return errors.Count > 0 ? Invalid(errors) : null;
    }

    private static void ApplyAddress(CompanyAddress a, CompanyAddressInput i)
    {
        a.AddressType = i.AddressType;
        a.Line1 = Trim(i.Line1);
        a.Line2 = Trim(i.Line2);
        a.Landmark = Trim(i.Landmark);
        a.Country = Trim(i.Country);
        a.State = Trim(i.State);
        a.District = Trim(i.District);
        a.City = Trim(i.City);
        a.PinCode = Trim(i.PinCode);
        a.ContactPerson = Trim(i.ContactPerson);
        a.ContactEmail = Trim(i.ContactEmail)?.ToLowerInvariant();
        a.ContactPhone = Trim(i.ContactPhone);
    }

    private static Error? ValidateContact(CompanyContactInput i)
    {
        var errors = new Dictionary<string, string[]>();
        if (!ContactTypes.Contains(i.ContactType))
        {
            errors["contactType"] = ["Select a contact type."];
        }

        Require(errors, "name", i.Name, 150);
        Pattern(errors, "email", i.Email, EmailRegex(), "Enter a valid email address.");
        return errors.Count > 0 ? Invalid(errors) : null;
    }

    private static void ApplyContact(CompanyContact x, CompanyContactInput i)
    {
        x.ContactType = i.ContactType;
        x.Name = i.Name.Trim();
        x.Designation = Trim(i.Designation);
        x.Email = Trim(i.Email)?.ToLowerInvariant();
        x.Phone = Trim(i.Phone);
    }

    private static Error? ValidateBank(BankAccountInput i)
    {
        var errors = new Dictionary<string, string[]>();
        Require(errors, "bankName", i.BankName, 120);
        Pattern(errors, "accountNumber", i.AccountNumber, AccountRegex(), "Account number must be 6-20 digits.");
        Require(errors, "accountNumber", i.AccountNumber, 40);
        Pattern(errors, "ifsc", i.Ifsc?.ToUpperInvariant(), IfscRegex(), "IFSC must look like HDFC0001234.");
        return errors.Count > 0 ? Invalid(errors) : null;
    }

    private async Task ApplyBankAsync(CompanyBankAccount b, BankAccountInput i, CancellationToken ct)
    {
        b.BankName = i.BankName.Trim();
        b.BranchName = Trim(i.BranchName);
        b.AccountNumber = i.AccountNumber.Trim();
        b.Ifsc = Trim(i.Ifsc)?.ToUpperInvariant();
        b.IsPrimary = i.IsPrimary;
        if (i.IsPrimary)
        {
            var others = await db.CompanyBankAccounts.Where(x => x.CompanyId == b.CompanyId && x.Id != b.Id && x.IsPrimary).ToListAsync(ct);
            others.ForEach(o => o.IsPrimary = false);
        }
    }

    private async Task<Error?> EnsureOwnerAsync(string owner, Guid ownerId, CancellationToken ct) => owner switch
    {
        CompanyOwner => await db.Companies.AnyAsync(c => c.Id == ownerId, ct) ? null : Error.NotFound("company-not-found", "Company not found."),
        BranchOwner => await db.Branches.AnyAsync(b => b.Id == ownerId, ct) ? null : Error.NotFound("branch-not-found", "Branch not found."),
        _ => Error.Validation("invalid-owner", "Unknown owner type."),
    };

    private async Task<bool> OwnsDocumentAsync(string owner, Guid ownerId, Guid fileId, CancellationToken ct)
    {
        var purpose = DocumentPurpose(owner);
        var key = ownerId.ToString();
        return await db.Files.AnyAsync(f => f.Id == fileId && f.Purpose == purpose && f.OwnerEntityType == owner && f.OwnerEntityId == key && !f.IsDeleted, ct);
    }

    private static string DocumentPurpose(string owner) =>
        owner == BranchOwner ? OrganizationModule.BranchDocumentPurpose : OrganizationModule.CompanyDocumentPurpose;

    private static T Clean<T>(T fields) where T : class
    {
        foreach (var p in typeof(T).GetProperties().Where(p => p.PropertyType == typeof(string) && p.CanWrite))
        {
            if (p.GetValue(fields) is string s)
            {
                p.SetValue(fields, string.IsNullOrWhiteSpace(s) ? null : s.Trim());
            }
        }

        return fields;
    }

    private static bool IsValidTimeZone(string tz)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(tz);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static string Normalize(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static Error Invalid(Dictionary<string, string[]> errors) =>
        Error.Validation("validation-failed", errors.Values.First()[0], errors);

    private static void Require(Dictionary<string, string[]> errors, string field, string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.TryAdd(field, ["This field is required."]);
        }
        else if (value.Trim().Length > max)
        {
            errors.TryAdd(field, [$"Must be at most {max} characters."]);
        }
    }

    private static void Pattern(Dictionary<string, string[]> errors, string field, string? value, Regex regex, string message)
    {
        if (!string.IsNullOrWhiteSpace(value) && !regex.IsMatch(value.Trim()))
        {
            errors.TryAdd(field, [message]);
        }
    }

    private static void Range(Dictionary<string, string[]> errors, string field, int? value, int min, int max)
    {
        if (value is { } v && (v < min || v > max))
        {
            errors.TryAdd(field, [$"Must be between {min} and {max}."]);
        }
    }

    private static void RangeD(Dictionary<string, string[]> errors, string field, double? value, double min, double max)
    {
        if (value is { } v && (v < min || v > max || double.IsNaN(v)))
        {
            errors.TryAdd(field, [$"Must be between {min} and {max}."]);
        }
    }

    private static void WeeklyOff(Dictionary<string, string[]> errors, string? pattern, string? json)
    {
        if (WeeklyOffRules.Validate(pattern, json) is { } msg)
        {
            errors.TryAdd("weeklyOffPattern", [msg]);
        }
    }

    [GeneratedRegex(@"^[A-Za-z]{5}[0-9]{4}[A-Za-z]$")]
    private static partial Regex PanRegex();

    [GeneratedRegex(@"^[A-Za-z]{4}[0-9]{5}[A-Za-z]$")]
    private static partial Regex TanRegex();

    [GeneratedRegex(@"^([LUlu][0-9]{5}[A-Za-z]{2}[0-9]{4}[A-Za-z]{3}[0-9]{6}|[A-Za-z]{3}-[0-9]{4})$")]
    private static partial Regex CinRegex();

    [GeneratedRegex(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$")]
    private static partial Regex GstinRegex();

    [GeneratedRegex(@"^[A-Z]{4}0[A-Z0-9]{6}$")]
    private static partial Regex IfscRegex();

    [GeneratedRegex(@"^[0-9]{6,20}$")]
    private static partial Regex AccountRegex();

    [GeneratedRegex(@"^[1-9][0-9]{5}$")]
    private static partial Regex PinRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^https?://\S+$", RegexOptions.IgnoreCase)]
    private static partial Regex WebsiteRegex();
}
