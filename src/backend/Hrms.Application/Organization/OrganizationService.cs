using Hrms.Application.Abstractions;
using Hrms.Application.Authorization;
using Hrms.Application.Modules;
using Hrms.Domain.Tenant;
using Hrms.Shared.Paging;
using Hrms.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Organization;

public sealed record CompanyDto(Guid Id, string Code, string Name, bool IsActive);
public sealed record BranchDto(Guid Id, Guid CompanyId, string Code, string Name, bool IsActive);
public sealed record DepartmentDto(Guid Id, string Code, string Name, Guid? BranchId, bool IsActive);
public sealed record EmployeeDirectoryDto(Guid Id, string EmployeeCode, string DisplayName, string? WorkEmail, Guid CompanyId, Guid BranchId, Guid? DepartmentId, Guid? ManagerEmployeeId, EmployeeStatus Status);

public sealed record CreateCompanyCommand(string Code, string Name, string? LegalName);
public sealed record CreateBranchCommand(Guid CompanyId, string Code, string Name);
public sealed record CreateDepartmentCommand(string Code, string Name, Guid? BranchId);
public sealed record CreateEmployeeCommand(string EmployeeCode, string DisplayName, string? WorkEmail, Guid BranchId, Guid? DepartmentId, Guid? ManagerEmployeeId);

/// <summary>
/// Organisation foundation and the minimal employee directory used by data scopes and user linking.
/// This is NOT the Employee module; personal/job/statutory data arrives in the Employee milestone.
/// </summary>
public sealed class OrganizationService(ITenantDbContext db, IDataScopeService scopes)
{
    public Task<List<CompanyDto>> ListCompaniesAsync(CancellationToken ct) =>
        db.Companies.AsNoTracking().OrderBy(c => c.Code).Select(c => new CompanyDto(c.Id, c.Code, c.Name, c.IsActive)).ToListAsync(ct);

    public Task<List<BranchDto>> ListBranchesAsync(CancellationToken ct) =>
        db.Branches.AsNoTracking().OrderBy(c => c.Code).Select(c => new BranchDto(c.Id, c.CompanyId, c.Code, c.Name, c.IsActive)).ToListAsync(ct);

    public Task<List<DepartmentDto>> ListDepartmentsAsync(CancellationToken ct) =>
        db.Departments.AsNoTracking().OrderBy(c => c.Code).Select(c => new DepartmentDto(c.Id, c.Code, c.Name, c.BranchId, c.IsActive)).ToListAsync(ct);

    public async Task<Result<Guid>> CreateCompanyAsync(CreateCompanyCommand cmd, CancellationToken ct)
    {
        var code = Normalize(cmd.Code);
        if (code.Length == 0 || string.IsNullOrWhiteSpace(cmd.Name))
        {
            return Error.Validation("invalid-company", "Code and name are required.");
        }

        if (await db.Companies.AnyAsync(c => c.Code == code, ct))
        {
            return Error.Conflict("company-code-taken", "Company code already exists.");
        }

        var c = new Company { Code = code, Name = cmd.Name.Trim(), LegalName = cmd.LegalName };
        db.Companies.Add(c);
        await db.SaveChangesAsync(ct);
        return c.Id;
    }

    public async Task<Result<Guid>> CreateBranchAsync(CreateBranchCommand cmd, CancellationToken ct)
    {
        var code = Normalize(cmd.Code);
        if (code.Length == 0 || string.IsNullOrWhiteSpace(cmd.Name))
        {
            return Error.Validation("invalid-branch", "Code and name are required.");
        }

        if (!await db.Companies.AnyAsync(c => c.Id == cmd.CompanyId, ct))
        {
            return Error.Validation("company-not-found", "Company not found.");
        }

        if (await db.Branches.AnyAsync(c => c.Code == code, ct))
        {
            return Error.Conflict("branch-code-taken", "Branch code already exists.");
        }

        var b = new Branch { CompanyId = cmd.CompanyId, Code = code, Name = cmd.Name.Trim() };
        db.Branches.Add(b);
        await db.SaveChangesAsync(ct);
        return b.Id;
    }

    public async Task<Result<Guid>> CreateDepartmentAsync(CreateDepartmentCommand cmd, CancellationToken ct)
    {
        var code = Normalize(cmd.Code);
        if (code.Length == 0 || string.IsNullOrWhiteSpace(cmd.Name))
        {
            return Error.Validation("invalid-department", "Code and name are required.");
        }

        if (await db.Departments.AnyAsync(c => c.Code == code, ct))
        {
            return Error.Conflict("department-code-taken", "Department code already exists.");
        }

        if (cmd.BranchId is { } bid && !await db.Branches.AnyAsync(b => b.Id == bid, ct))
        {
            return Error.Validation("branch-not-found", "Branch not found.");
        }

        var d = new Department { Code = code, Name = cmd.Name.Trim(), BranchId = cmd.BranchId };
        db.Departments.Add(d);
        await db.SaveChangesAsync(ct);
        return d.Id;
    }

    public async Task<PagedResult<EmployeeDirectoryDto>> ListEmployeesAsync(PageRequest page, string? search, CancellationToken ct)
    {
        var scope = await scopes.ResolveAsync(FoundationPermissions.EmployeeView, ct);
        var q = scopes.ApplyScope(db.Employees.AsNoTracking(), scope);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(e => e.DisplayName.ToLower().Contains(s) || e.EmployeeCode.ToLower().Contains(s));
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(e => e.EmployeeCode).Skip(page.Skip).Take(page.SafePageSize).Select(Map).ToListAsync(ct);
        return new PagedResult<EmployeeDirectoryDto>(items, page.SafePage, page.SafePageSize, total);
    }

    public async Task<Result<EmployeeDirectoryDto>> GetEmployeeAsync(Guid id, CancellationToken ct)
    {
        // Out-of-scope and non-existent records are indistinguishable (no existence oracle).
        if (!await scopes.CanAccessEmployeeAsync(FoundationPermissions.EmployeeView, id, ct))
        {
            return Error.NotFound("employee-not-found", "Employee not found.");
        }

        return await db.Employees.AsNoTracking().Where(e => e.Id == id).Select(Map).FirstAsync(ct);
    }

    public async Task<Result<Guid>> CreateEmployeeAsync(CreateEmployeeCommand cmd, CancellationToken ct)
    {
        var code = Normalize(cmd.EmployeeCode);
        if (code.Length == 0 || string.IsNullOrWhiteSpace(cmd.DisplayName))
        {
            return Error.Validation("invalid-employee", "Employee code and name are required.");
        }

        var branch = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == cmd.BranchId, ct);
        if (branch is null)
        {
            return Error.Validation("branch-not-found", "Branch not found.");
        }

        var scope = await scopes.ResolveAsync(FoundationPermissions.EmployeeCreate, ct);
        if (!scope.All && !scope.BranchIds.Contains(cmd.BranchId) && !scope.CompanyIds.Contains(branch.CompanyId))
        {
            return Error.Forbidden("out-of-scope", "You cannot create employees in this branch.");
        }

        if (await db.Employees.AnyAsync(e => e.EmployeeCode == code, ct))
        {
            return Error.Conflict("employee-code-taken", "Employee code already exists.");
        }

        if (cmd.ManagerEmployeeId is { } mid && !await db.Employees.AnyAsync(e => e.Id == mid, ct))
        {
            return Error.Validation("manager-not-found", "Manager not found.");
        }

        var e = new Employee
        {
            EmployeeCode = code,
            DisplayName = cmd.DisplayName.Trim(),
            WorkEmail = cmd.WorkEmail?.Trim().ToLowerInvariant(),
            CompanyId = branch.CompanyId,
            BranchId = branch.Id,
            DepartmentId = cmd.DepartmentId,
            ManagerEmployeeId = cmd.ManagerEmployeeId,
        };
        db.Employees.Add(e);
        db.ReportingClosure.Add(new ReportingClosure { AncestorId = e.Id, DescendantId = e.Id, Depth = 0 });
        if (cmd.ManagerEmployeeId is { } managerId)
        {
            var ancestors = await db.ReportingClosure.AsNoTracking().Where(r => r.DescendantId == managerId).ToListAsync(ct);
            foreach (var a in ancestors)
            {
                db.ReportingClosure.Add(new ReportingClosure { AncestorId = a.AncestorId, DescendantId = e.Id, Depth = a.Depth + 1 });
            }
        }

        await db.SaveChangesAsync(ct);
        return e.Id;
    }

    private static string Normalize(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    private static readonly System.Linq.Expressions.Expression<Func<Employee, EmployeeDirectoryDto>> Map =
        e => new EmployeeDirectoryDto(e.Id, e.EmployeeCode, e.DisplayName, e.WorkEmail, e.CompanyId, e.BranchId, e.DepartmentId, e.ManagerEmployeeId, e.Status);
}
