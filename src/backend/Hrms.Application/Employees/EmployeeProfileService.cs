using Hrms.Application.Abstractions;
using Hrms.Domain.Tenant;
using Hrms.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Employees;

// ---------- DTOs (masked — no raw sensitive values ever leave the API) ----------

public sealed record PersonalInput(
    DateOnly? DateOfBirth, string? Gender, string? MaritalStatus, string? Nationality,
    string? BloodGroup, string? PersonalEmail, string? PersonalPhone,
    object? CurrentAddress, object? PermanentAddress, uint? RowVersion = null);

public sealed record PersonalDto(
    DateOnly? DateOfBirth, string? Gender, string? MaritalStatus, string? Nationality,
    string? BloodGroup, string? PersonalEmail, string? PersonalPhone,
    object? CurrentAddress, object? PermanentAddress, uint RowVersion);

public sealed record ContactInput(
    string Kind, string Name, string? Relation, DateOnly? DateOfBirth, string? Phone,
    string? Email, bool IsPrimary, int? NomineeSharePct, uint? RowVersion = null);

public sealed record ContactDto(
    Guid Id, string Kind, string Name, string? Relation, DateOnly? DateOfBirth,
    string? Phone, string? Email, bool IsPrimary, int? NomineeSharePct, uint RowVersion);

public sealed record EmployeeBankInput(
    string BankName, string? BranchName, string AccountNumber, string Ifsc,
    string? HolderName, bool IsPrimary, DateOnly EffectiveFrom, uint? RowVersion = null);

public sealed record EmployeeBankDto(
    Guid Id, string BankName, string? BranchName, string AccountMasked, string Ifsc,
    string? HolderName, bool IsPrimary, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    string VerificationStatus, string? VerificationNote, uint RowVersion);

public sealed record EmployeeBankVerifyInput(string Status, string? Note = null);

public sealed record StatutoryInput(
    string? Pan, string? Uan, string? PfNumber, bool PfApplicable, string? EsiNumber,
    bool EsiApplicable, string? PtState, bool LwfApplicable, string? AadhaarLast4,
    string? Passport, string? TaxRegime, uint? RowVersion = null);

public sealed record StatutoryDto(
    string? PanMasked, string? Uan, string? PfNumber, bool PfApplicable, string? EsiNumber,
    bool EsiApplicable, string? PtState, bool LwfApplicable, string? AadhaarLast4,
    string? PassportMasked, string? TaxRegime, uint RowVersion);

public sealed record HistoryRowDto(
    Guid Id, string EventType, string EmploymentType, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    Guid CompanyId, Guid BranchId, Guid? DepartmentId, Guid? DesignationId, Guid? GradeId,
    Guid? LocationId, Guid? CostCentreId, Guid? ManagerEmployeeId, string? Reason,
    bool PayrollAffecting, uint RowVersion);

public sealed class EmployeeProfileService(ITenantDbContext db, ISecretProtector protector)
{
    public const string SensitivePurpose = "hrms.employee.sensitive";

    private async Task<Result<Employee>> EmployeeAsync(Guid id, CancellationToken ct)
    {
        var e = await db.Employees.FirstOrDefaultAsync(x => x.Id == id, ct);
        return e is null ? Error.NotFound("employee-not-found", "Employee not found.") : e;
    }

    // ---------- Personal ----------

    public async Task<Result<PersonalDto>> GetPersonalAsync(Guid employeeId, CancellationToken ct)
    {
        var p = await db.EmployeePersonal.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeId == employeeId, ct);
        if (p is null)
        {
            return await db.Employees.AnyAsync(x => x.Id == employeeId, ct)
                ? new PersonalDto(null, null, null, null, null, null, null, null, null, 0)
                : Error.NotFound("employee-not-found", "Employee not found.");
        }

        return new PersonalDto(p.DateOfBirth, p.Gender, p.MaritalStatus, p.Nationality, p.BloodGroup,
            p.PersonalEmail, p.PersonalPhone,
            p.CurrentAddressJson is null ? null : System.Text.Json.JsonDocument.Parse(p.CurrentAddressJson).RootElement,
            p.PermanentAddressJson is null ? null : System.Text.Json.JsonDocument.Parse(p.PermanentAddressJson).RootElement,
            p.RowVersion);
    }

    public async Task<Result> UpdatePersonalAsync(Guid employeeId, PersonalInput input, CancellationToken ct)
    {
        var emp = await EmployeeAsync(employeeId, ct);
        if (!emp.IsSuccess)
        {
            return emp.Error!;
        }

        var p = await db.EmployeePersonal.FirstOrDefaultAsync(x => x.EmployeeId == employeeId, ct)
            ?? db.EmployeePersonal.Add(new EmployeePersonal { EmployeeId = employeeId }).Entity;

        if (p.RowVersion != 0 && input.RowVersion is { } rv && rv != p.RowVersion)
        {
            return Error.Conflict("concurrency.conflict", "The record was modified by someone else — reload and retry.");
        }

        p.DateOfBirth = input.DateOfBirth;
        p.Gender = input.Gender?.Trim();
        p.MaritalStatus = input.MaritalStatus?.Trim();
        p.Nationality = input.Nationality?.Trim();
        p.BloodGroup = input.BloodGroup?.Trim();
        p.PersonalEmail = input.PersonalEmail?.Trim().ToLowerInvariant();
        p.PersonalPhone = input.PersonalPhone?.Trim();
        p.CurrentAddressJson = input.CurrentAddress is null ? null : System.Text.Json.JsonSerializer.Serialize(input.CurrentAddress);
        p.PermanentAddressJson = input.PermanentAddress is null ? null : System.Text.Json.JsonSerializer.Serialize(input.PermanentAddress);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ---------- Contacts (emergency/family/nominee) ----------

    public async Task<List<ContactDto>> ListContactsAsync(Guid employeeId, CancellationToken ct) =>
        await db.EmployeeContacts.AsNoTracking().Where(x => x.EmployeeId == employeeId)
            .OrderBy(x => x.Kind).ThenBy(x => x.Name)
            .Select(x => new ContactDto(x.Id, x.Kind, x.Name, x.Relation, x.DateOfBirth, x.Phone, x.Email,
                x.IsPrimary, x.NomineeSharePct, x.RowVersion))
            .ToListAsync(ct);

    public async Task<Result<ContactDto>> AddContactAsync(Guid employeeId, ContactInput input, CancellationToken ct)
    {
        var emp = await EmployeeAsync(employeeId, ct);
        if (!emp.IsSuccess)
        {
            return emp.Error!;
        }

        var validation = await ValidateContactAsync(employeeId, input, excludeId: null, ct);
        if (!validation.IsSuccess)
        {
            return validation.Error!;
        }

        if (input.IsPrimary && input.Kind == ContactKinds.Emergency)
        {
            await DemotePrimaryEmergencyAsync(employeeId, ct);
        }

        var c = new EmployeeContact
        {
            EmployeeId = employeeId, Kind = input.Kind.Trim().ToLowerInvariant(), Name = input.Name.Trim(),
            Relation = input.Relation?.Trim(), DateOfBirth = input.DateOfBirth, Phone = input.Phone?.Trim(),
            Email = input.Email?.Trim().ToLowerInvariant(), IsPrimary = input.IsPrimary,
            NomineeSharePct = input.NomineeSharePct,
        };
        db.EmployeeContacts.Add(c);
        await db.SaveChangesAsync(ct);
        return new ContactDto(c.Id, c.Kind, c.Name, c.Relation, c.DateOfBirth, c.Phone, c.Email, c.IsPrimary, c.NomineeSharePct, c.RowVersion);
    }

    public async Task<Result> UpdateContactAsync(Guid employeeId, Guid contactId, ContactInput input, CancellationToken ct)
    {
        var c = await db.EmployeeContacts.FirstOrDefaultAsync(x => x.Id == contactId && x.EmployeeId == employeeId, ct);
        if (c is null)
        {
            return Error.NotFound("contact-not-found", "Contact not found.");
        }

        if (input.RowVersion is { } rv && rv != c.RowVersion)
        {
            return Error.Conflict("concurrency.conflict", "The record was modified by someone else — reload and retry.");
        }

        var kind = input.Kind.Trim().ToLowerInvariant();
        var validation = await ValidateContactAsync(employeeId, input with { Kind = kind }, contactId, ct);
        if (!validation.IsSuccess)
        {
            return validation.Error!;
        }

        if (input.IsPrimary && kind == ContactKinds.Emergency && !c.IsPrimary)
        {
            await DemotePrimaryEmergencyAsync(employeeId, ct);
        }

        c.Kind = kind;
        c.Name = input.Name.Trim();
        c.Relation = input.Relation?.Trim();
        c.DateOfBirth = input.DateOfBirth;
        c.Phone = input.Phone?.Trim();
        c.Email = input.Email?.Trim().ToLowerInvariant();
        c.IsPrimary = input.IsPrimary;
        c.NomineeSharePct = input.NomineeSharePct;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteContactAsync(Guid employeeId, Guid contactId, CancellationToken ct)
    {
        var c = await db.EmployeeContacts.FirstOrDefaultAsync(x => x.Id == contactId && x.EmployeeId == employeeId, ct);
        if (c is null)
        {
            return Error.NotFound("contact-not-found", "Contact not found.");
        }

        db.EmployeeContacts.Remove(c);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Result> ValidateContactAsync(Guid employeeId, ContactInput input, Guid? excludeId, CancellationToken ct)
    {
        if (!ContactKinds.All.Contains(input.Kind.Trim().ToLowerInvariant()) || string.IsNullOrWhiteSpace(input.Name))
        {
            return Error.Validation("invalid-contact", "Kind must be emergency/family/nominee and a name is required.");
        }

        if (input.Kind == ContactKinds.Nominee || input.NomineeSharePct is not null)
        {
            if (input.NomineeSharePct is not (> 0 and <= 100))
            {
                return Error.Validation("invalid-share", "Nominee share must be between 1 and 100%.");
            }

            var total = await db.EmployeeContacts
                .Where(x => x.EmployeeId == employeeId && x.Kind == ContactKinds.Nominee && x.Id != excludeId)
                .SumAsync(x => (int?)x.NomineeSharePct, ct) ?? 0;
            if (total + (input.NomineeSharePct ?? 0) > 100)
            {
                return Error.Conflict("nominee-share-exceeded",
                    $"Nominee shares would total {total + (input.NomineeSharePct ?? 0)}% — the maximum is 100%.");
            }
        }

        return Result.Success();
    }

    private async Task DemotePrimaryEmergencyAsync(Guid employeeId, CancellationToken ct)
    {
        var current = await db.EmployeeContacts
            .FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.Kind == ContactKinds.Emergency && x.IsPrimary, ct);
        if (current is not null)
        {
            current.IsPrimary = false;
        }
    }

    // ---------- Bank accounts ----------

    public async Task<List<EmployeeBankDto>> ListBankAccountsAsync(Guid employeeId, CancellationToken ct) =>
        await db.EmployeeBankAccounts.AsNoTracking().Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.EffectiveFrom)
            .Select(x => new EmployeeBankDto(x.Id, x.BankName, x.BranchName, "****" + x.AccountLast4, x.Ifsc,
                x.HolderName, x.IsPrimary, x.EffectiveFrom, x.EffectiveTo,
                x.VerificationStatus.ToString().ToLowerInvariant(), x.VerificationNote, x.RowVersion))
            .ToListAsync(ct);

    /// <summary>Adds an account; when primary, the current active primary is closed in the same transaction.</summary>
    public async Task<Result<EmployeeBankDto>> AddBankAccountAsync(Guid employeeId, EmployeeBankInput input, CancellationToken ct)
    {
        var emp = await EmployeeAsync(employeeId, ct);
        if (!emp.IsSuccess)
        {
            return emp.Error!;
        }

        var digits = new string(input.AccountNumber.Where(char.IsDigit).ToArray());
        if (digits.Length < 8 || digits.Length > 18 || string.IsNullOrWhiteSpace(input.BankName)
            || input.Ifsc.Trim().Length is < 8 or > 16)
        {
            return Error.Validation("invalid-bank", "Bank name, a valid account number (8-18 digits) and IFSC are required.");
        }

        if (input.IsPrimary)
        {
            var active = await db.EmployeeBankAccounts
                .FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.IsPrimary && x.EffectiveTo == null, ct);
            if (active is not null)
            {
                active.EffectiveTo = input.EffectiveFrom; // replacement history retained
            }
        }

        var a = new EmployeeBankAccount
        {
            EmployeeId = employeeId, BankName = input.BankName.Trim(), BranchName = input.BranchName?.Trim(),
            AccountNumberEncrypted = protector.Protect(SensitivePurpose, digits),
            AccountLast4 = digits[^4..], Ifsc = input.Ifsc.Trim().ToUpperInvariant(),
            HolderName = input.HolderName?.Trim(), IsPrimary = input.IsPrimary,
            EffectiveFrom = input.EffectiveFrom,
        };
        db.EmployeeBankAccounts.Add(a);
        await db.SaveChangesAsync(ct);
        return ToDto(a);
    }

    public async Task<Result> VerifyBankAccountAsync(Guid employeeId, Guid accountId, EmployeeBankVerifyInput input,
        Guid verifierId, CancellationToken ct)
    {
        var a = await db.EmployeeBankAccounts.FirstOrDefaultAsync(x => x.Id == accountId && x.EmployeeId == employeeId, ct);
        if (a is null)
        {
            return Error.NotFound("account-not-found", "Bank account not found.");
        }

        a.VerificationStatus = input.Status == "verified" ? BankVerificationStatus.Verified
            : input.Status == "rejected" ? BankVerificationStatus.Rejected
            : BankVerificationStatus.Pending;
        a.VerifiedBy = input.Status is "verified" or "rejected" ? verifierId : null;
        a.VerifiedAt = input.Status is "verified" or "rejected" ? DateTimeOffset.UtcNow : null;
        a.VerificationNote = input.Note?.Trim();
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Replaces an account: closes it and opens a new row (full history retained).</summary>
    public async Task<Result<EmployeeBankDto>> ReplaceBankAccountAsync(Guid employeeId, Guid accountId,
        EmployeeBankInput input, CancellationToken ct)
    {
        var old = await db.EmployeeBankAccounts.FirstOrDefaultAsync(x => x.Id == accountId && x.EmployeeId == employeeId, ct);
        if (old is null)
        {
            return Error.NotFound("account-not-found", "Bank account not found.");
        }

        if (input.RowVersion is { } rv && rv != old.RowVersion)
        {
            return Error.Conflict("concurrency.conflict", "The record was modified by someone else — reload and retry.");
        }

        old.EffectiveTo = input.EffectiveFrom;
        var add = await AddBankAccountAsync(employeeId, input, ct);
        if (!add.IsSuccess)
        {
            old.EffectiveTo = null;
        }

        return add;
    }

    private static EmployeeBankDto ToDto(EmployeeBankAccount a) =>
        new(a.Id, a.BankName, a.BranchName, "****" + a.AccountLast4, a.Ifsc, a.HolderName, a.IsPrimary,
            a.EffectiveFrom, a.EffectiveTo, a.VerificationStatus.ToString().ToLowerInvariant(), a.VerificationNote, a.RowVersion);

    // ---------- Statutory (PAN/passport encrypted; last-4 surfaced) ----------

    public async Task<Result<StatutoryDto>> GetStatutoryAsync(Guid employeeId, CancellationToken ct)
    {
        var s = await db.EmployeeStatutory.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeId == employeeId, ct);
        if (s is null)
        {
            return await db.Employees.AnyAsync(x => x.Id == employeeId, ct)
                ? new StatutoryDto(null, null, null, true, null, false, null, false, null, null, null, 0)
                : Error.NotFound("employee-not-found", "Employee not found.");
        }

        return ToStatDto(s);
    }

    public async Task<Result> UpdateStatutoryAsync(Guid employeeId, StatutoryInput input, CancellationToken ct)
    {
        var emp = await EmployeeAsync(employeeId, ct);
        if (!emp.IsSuccess)
        {
            return emp.Error!;
        }

        var s = await db.EmployeeStatutory.FirstOrDefaultAsync(x => x.EmployeeId == employeeId, ct)
            ?? db.EmployeeStatutory.Add(new EmployeeStatutory { EmployeeId = employeeId }).Entity;

        if (s.RowVersion != 0 && input.RowVersion is { } rv && rv != s.RowVersion)
        {
            return Error.Conflict("concurrency.conflict", "The record was modified by someone else — reload and retry.");
        }

        if (input.Pan is { Length: > 0 } pan && !System.Text.RegularExpressions.Regex.IsMatch(pan.ToUpperInvariant(), "^[A-Z]{5}[0-9]{4}[A-Z]$"))
        {
            return Error.Validation("invalid-pan", "PAN must match AAAAA9999A.");
        }

        if (input.AadhaarLast4 is { Length: not 4 } || input.AadhaarLast4?.Any(c => !char.IsDigit(c)) == true)
        {
            return Error.Validation("invalid-aadhaar", "Aadhaar must be supplied as last-4 digits only.");
        }

        if (input.TaxRegime is { Length: > 0 } tr && tr is not ("old_regime" or "new_regime"))
        {
            return Error.Validation("invalid-regime", "Tax regime must be old_regime or new_regime.");
        }

        if (input.Pan is not null)
        {
            var panVal = input.Pan.Trim().ToUpperInvariant();
            s.PanEncrypted = panVal.Length == 0 ? null : protector.Protect(SensitivePurpose, panVal);
            s.PanLast4 = panVal.Length == 0 ? null : panVal[^4..];
        }

        if (input.Passport is not null)
        {
            var pp = input.Passport.Trim().ToUpperInvariant();
            s.PassportEncrypted = pp.Length == 0 ? null : protector.Protect(SensitivePurpose, pp);
            s.PassportLast4 = pp.Length == 0 ? null : pp[^4..];
        }

        s.Uan = input.Uan?.Trim();
        s.PfNumber = input.PfNumber?.Trim();
        s.PfApplicable = input.PfApplicable;
        s.EsiNumber = input.EsiNumber?.Trim();
        s.EsiApplicable = input.EsiApplicable;
        s.PtState = input.PtState?.Trim();
        s.LwfApplicable = input.LwfApplicable;
        s.AadhaarLast4 = input.AadhaarLast4;
        s.TaxRegime = input.TaxRegime;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static StatutoryDto ToStatDto(EmployeeStatutory s) =>
        new(s.PanLast4 is null ? null : "****" + s.PanLast4, s.Uan, s.PfNumber, s.PfApplicable, s.EsiNumber,
            s.EsiApplicable, s.PtState, s.LwfApplicable, s.AadhaarLast4,
            s.PassportLast4 is null ? null : "****" + s.PassportLast4, s.TaxRegime, s.RowVersion);

    // ---------- Employment history read ----------

    public async Task<List<HistoryRowDto>> ListHistoryAsync(Guid employeeId, CancellationToken ct) =>
        await db.EmploymentHistory.AsNoTracking().Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.EffectiveFrom)
            .Select(x => new HistoryRowDto(x.Id, x.EventType, x.EmploymentType, x.EffectiveFrom, x.EffectiveTo,
                x.CompanyId, x.BranchId, x.DepartmentId, x.DesignationId, x.GradeId, x.LocationId, x.CostCentreId,
                x.ManagerEmployeeId, x.Reason, x.PayrollAffecting, x.RowVersion))
            .ToListAsync(ct);
}
