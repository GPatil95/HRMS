using Hrms.Application.Abstractions;
using Hrms.Application.Auditing;
using Hrms.Application.Authorization;
using Hrms.Application.Modules;
using Hrms.Domain.Common;
using Hrms.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Reporting;

public sealed record ReportDescriptor(string Code, string Title, string ModuleCode, string ViewPermission, string ExportPermission);

public sealed record ReportResult(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows, int TotalCount);

public sealed record ReportExport(string FileName, string ContentType, byte[] Content);

/// <summary>A code-defined report. Implementations must apply data scope themselves through <see cref="IDataScopeService"/>.</summary>
public interface IReportDefinition
{
    ReportDescriptor Descriptor { get; }
    Task<ReportResult> RunAsync(int maxRows, CancellationToken ct);
}

/// <summary>Report framework: entitlement + permission + scope + export audit, one code path for view and export.</summary>
public sealed class ReportingService(IEnumerable<IReportDefinition> reports, IAccessGuard access, IExcelWriter excel, IAuditWriter audit, ITenantDbContext db)
{
    public const int MaxViewRows = 500;
    public const int MaxExportRows = 50_000;

    public async Task<IReadOnlyList<ReportDescriptor>> ListAvailableAsync(CancellationToken ct)
    {
        var list = new List<ReportDescriptor>();
        foreach (var r in reports)
        {
            if (await access.CheckAsync(r.Descriptor.ModuleCode, r.Descriptor.ViewPermission, ct) is null)
            {
                list.Add(r.Descriptor);
            }
        }

        return list;
    }

    public async Task<Result<ReportResult>> RunAsync(string code, CancellationToken ct)
    {
        var r = reports.FirstOrDefault(x => x.Descriptor.Code == code);
        if (r is null)
        {
            return Error.NotFound("report-not-found", "Report not found.");
        }

        if (await access.CheckAsync(r.Descriptor.ModuleCode, r.Descriptor.ViewPermission, ct) is { } denied)
        {
            return denied;
        }

        return await r.RunAsync(MaxViewRows, ct);
    }

    public async Task<Result<ReportExport>> ExportAsync(string code, CancellationToken ct)
    {
        var r = reports.FirstOrDefault(x => x.Descriptor.Code == code);
        if (r is null)
        {
            return Error.NotFound("report-not-found", "Report not found.");
        }

        if (await access.CheckAsync(r.Descriptor.ModuleCode, r.Descriptor.ExportPermission, ct) is { } denied)
        {
            return denied;
        }

        var data = await r.RunAsync(MaxExportRows, ct);
        var bytes = excel.Write(r.Descriptor.Title.Length > 31 ? r.Descriptor.Title[..31] : r.Descriptor.Title, data.Columns, data.Rows);
        audit.Add(r.Descriptor.ModuleCode, "report", r.Descriptor.Code, "export", new { rows = data.Rows.Count });
        await db.SaveChangesAsync(ct);
        return new ReportExport($"{r.Descriptor.Code}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", bytes);
    }
}

public sealed class EmployeeDirectoryReport(ITenantDbContext db, IDataScopeService scopes) : IReportDefinition
{
    public ReportDescriptor Descriptor { get; } = new("employee-directory", "Employee directory", EmployeeFoundationModule.ModuleCode, FoundationPermissions.EmployeeView, FoundationPermissions.EmployeeExport);

    public async Task<ReportResult> RunAsync(int maxRows, CancellationToken ct)
    {
        var scope = await scopes.ResolveAsync(FoundationPermissions.EmployeeView, ct);
        var q = scopes.ApplyScope(db.Employees.AsNoTracking(), scope);
        var total = await q.CountAsync(ct);
        var rows = await (from e in q
                          join b in db.Branches on e.BranchId equals b.Id
                          join d in db.Departments on e.DepartmentId equals d.Id into dj
                          from d in dj.DefaultIfEmpty()
                          orderby e.EmployeeCode
                          select new { e.EmployeeCode, e.DisplayName, e.WorkEmail, Branch = b.Name, Department = d != null ? d.Name : null, e.Status })
            .Take(maxRows).ToListAsync(ct);
        return new ReportResult(["Employee code", "Name", "Work email", "Branch", "Department", "Status"],
            rows.Select(r => (IReadOnlyList<string?>)[r.EmployeeCode, r.DisplayName, r.WorkEmail, r.Branch, r.Department, r.Status.ToString()]).ToList(), total);
    }
}

public sealed class AuditTrailReport(ITenantDbContext db) : IReportDefinition
{
    public ReportDescriptor Descriptor { get; } = new("audit-trail", "Audit trail", AuditModule.ModuleCode, FoundationPermissions.AuditView, FoundationPermissions.AuditExport);

    public async Task<ReportResult> RunAsync(int maxRows, CancellationToken ct)
    {
        var total = await db.AuditEntries.CountAsync(ct);
        var rows = await db.AuditEntries.AsNoTracking().OrderByDescending(a => a.OccurredAt).Take(maxRows)
            .Select(a => new { a.OccurredAt, a.ModuleCode, a.EntityType, a.EntityId, a.Action, a.ActorDisplay, a.Source, a.CorrelationId }).ToListAsync(ct);
        return new ReportResult(["When (UTC)", "Module", "Entity", "Record", "Action", "Actor", "Source", "Correlation"],
            rows.Select(r => (IReadOnlyList<string?>)[r.OccurredAt.UtcDateTime.ToString("u"), r.ModuleCode, r.EntityType, r.EntityId, r.Action, r.ActorDisplay, r.Source, r.CorrelationId]).ToList(), total);
    }
}
