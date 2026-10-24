using System.Linq.Expressions;
using Hrms.Application.Abstractions;
using Hrms.Domain.Tenant;
using Hrms.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Authorization;

/// <summary>Resolved data scope for one permission and one user: union of all grants.</summary>
public sealed class ResolvedDataScope
{
    public bool All { get; init; }
    public bool None { get; init; }
    public Guid? OwnEmployeeId { get; init; }
    public bool IncludeOwn { get; init; }
    public Guid? DirectReportsOf { get; init; }
    public Guid? AllReportsOf { get; init; }
    public IReadOnlySet<Guid> CompanyIds { get; init; } = new HashSet<Guid>();
    public IReadOnlySet<Guid> BranchIds { get; init; } = new HashSet<Guid>();
    public IReadOnlySet<Guid> DepartmentIds { get; init; } = new HashSet<Guid>();
    public IReadOnlySet<Guid> EmployeeIds { get; init; } = new HashSet<Guid>();

    public static ResolvedDataScope Nothing { get; } = new() { None = true };
    public static ResolvedDataScope Everything { get; } = new() { All = true };
}

public interface IDataScopeService
{
    Task<ResolvedDataScope> ResolveAsync(string permissionCode, CancellationToken ct);
    Task<ResolvedDataScope> ResolveForGrantsAsync(Guid userId, IReadOnlyList<ScopeGrant> grants, CancellationToken ct);
    IQueryable<Employee> ApplyScope(IQueryable<Employee> query, ResolvedDataScope scope);
    /// <summary>Filters any entity by an employee reference through the employee scope.</summary>
    IQueryable<T> ApplyEmployeeScope<T>(IQueryable<T> query, Expression<Func<T, Guid?>> employeeSelector, ResolvedDataScope scope);
    Task<bool> CanAccessEmployeeAsync(string permissionCode, Guid employeeId, CancellationToken ct);
}

public sealed class DataScopeService(
    ITenantDbContext db,
    ICurrentUser currentUser,
    IPermissionService permissions) : IDataScopeService
{
    public async Task<ResolvedDataScope> ResolveAsync(string permissionCode, CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ResolvedDataScope.Nothing;
        }

        var set = await permissions.GetEffectiveAsync(userId, ct);
        return await ResolveForGrantsAsync(userId, set.ScopesFor(permissionCode), ct);
    }

    public async Task<ResolvedDataScope> ResolveForGrantsAsync(Guid userId, IReadOnlyList<ScopeGrant> grants, CancellationToken ct)
    {
        if (grants.Count == 0)
        {
            return ResolvedDataScope.Nothing;
        }

        if (grants.Any(g => g.Level == ScopeLevel.Tenant))
        {
            return ResolvedDataScope.Everything;
        }

        var self = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => u.EmployeeId == null ? null : db.Employees.Where(e => e.Id == u.EmployeeId)
                .Select(e => new { e.Id, e.CompanyId, e.BranchId, e.DepartmentId }).FirstOrDefault())
            .FirstOrDefaultAsync(ct);

        var companies = new HashSet<Guid>();
        var branches = new HashSet<Guid>();
        var departments = new HashSet<Guid>();
        var employees = new HashSet<Guid>();
        bool own = false;
        Guid? direct = null, all = null;

        foreach (var g in grants)
        {
            var explicitTargets = g.TargetIds.Count > 0;
            switch (g.Level)
            {
                case ScopeLevel.Own:
                    own = self is not null;
                    break;
                case ScopeLevel.DirectReports:
                    own |= self is not null;
                    direct = self?.Id;
                    break;
                case ScopeLevel.AllReports:
                    own |= self is not null;
                    all = self?.Id;
                    break;
                case ScopeLevel.Company:
                    if (explicitTargets) companies.UnionWith(g.TargetIds);
                    else if (self is not null) companies.Add(self.CompanyId);
                    break;
                case ScopeLevel.Branch:
                    if (explicitTargets) branches.UnionWith(g.TargetIds);
                    else if (self is not null) branches.Add(self.BranchId);
                    break;
                case ScopeLevel.Department:
                    if (explicitTargets) departments.UnionWith(g.TargetIds);
                    else if (self?.DepartmentId is { } d) departments.Add(d);
                    break;
                case ScopeLevel.SelectedEmployees:
                    employees.UnionWith(g.TargetIds);
                    break;
            }
        }

        var nothing = !own && direct is null && all is null && companies.Count == 0 && branches.Count == 0 && departments.Count == 0 && employees.Count == 0;
        return new ResolvedDataScope
        {
            None = nothing,
            IncludeOwn = own,
            OwnEmployeeId = self?.Id,
            DirectReportsOf = direct,
            AllReportsOf = all,
            CompanyIds = companies,
            BranchIds = branches,
            DepartmentIds = departments,
            EmployeeIds = employees,
        };
    }

    public IQueryable<Employee> ApplyScope(IQueryable<Employee> query, ResolvedDataScope scope)
    {
        if (scope.All)
        {
            return query;
        }

        if (scope.None)
        {
            return query.Where(_ => false);
        }

        var own = scope.IncludeOwn ? scope.OwnEmployeeId : null;
        var direct = scope.DirectReportsOf;
        var all = scope.AllReportsOf;
        var companies = scope.CompanyIds.ToList();
        var branches = scope.BranchIds.ToList();
        var departments = scope.DepartmentIds.ToList();
        var employees = scope.EmployeeIds.ToList();
        var closure = db.ReportingClosure;

        return query.Where(e =>
            (own != null && e.Id == own) ||
            (direct != null && closure.Any(c => c.AncestorId == direct && c.DescendantId == e.Id && c.Depth == 1)) ||
            (all != null && closure.Any(c => c.AncestorId == all && c.DescendantId == e.Id && c.Depth >= 1)) ||
            companies.Contains(e.CompanyId) ||
            branches.Contains(e.BranchId) ||
            (e.DepartmentId != null && departments.Contains(e.DepartmentId.Value)) ||
            employees.Contains(e.Id));
    }

    public IQueryable<T> ApplyEmployeeScope<T>(IQueryable<T> query, Expression<Func<T, Guid?>> employeeSelector, ResolvedDataScope scope)
    {
        if (scope.All)
        {
            return query;
        }

        var allowed = ApplyScope(db.Employees, scope).Select(e => (Guid?)e.Id);
        var param = employeeSelector.Parameters[0];
        var contains = Expression.Call(
            typeof(Queryable), nameof(Queryable.Contains), [typeof(Guid?)],
            allowed.Expression, employeeSelector.Body);
        return query.Where(Expression.Lambda<Func<T, bool>>(contains, param));
    }

    public async Task<bool> CanAccessEmployeeAsync(string permissionCode, Guid employeeId, CancellationToken ct)
    {
        var scope = await ResolveAsync(permissionCode, ct);
        return await ApplyScope(db.Employees.AsNoTracking().Where(e => e.Id == employeeId), scope).AnyAsync(ct);
    }
}
