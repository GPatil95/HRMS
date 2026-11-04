using Hrms.Application.Abstractions;
using Hrms.Domain.Tenant;
using Hrms.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Organization;

/// <summary>
/// Organization reference masters (M2.1): designations, grades, work locations and cost centres.
/// Simple code/name lookups referenced by the employee module. Masters are soft-deactivated; hard delete
/// is only allowed while nothing references the row.
/// </summary>
public sealed class OrgReferenceMastersService(ITenantDbContext db)
{
    public static readonly string[] LocationTypes = ["office", "floor", "desk_zone", "client_site", "remote", "other"];

    // ---- Designations ----
    public Task<List<DesignationDto>> ListDesignationsAsync(bool includeInactive, CancellationToken ct) =>
        Query(db.Designations, includeInactive,
            d => new DesignationDto(d.Id, d.Code, d.Name, d.Level, d.Description, d.IsActive, d.RowVersion), ct);

    public Task<Result<Guid>> CreateDesignationAsync(DesignationInput input, CancellationToken ct) =>
        CreateAsync(db.Designations, new Designation { Code = input.Code.Trim(), Name = input.Name.Trim(), Level = input.Level, Description = input.Description?.Trim() }, ct);

    public Task<Result> UpdateDesignationAsync(Guid id, DesignationInput input, CancellationToken ct) =>
        UpdateAsync(db.Designations, id, input.RowVersion, input.Name, d => { d.Name = input.Name.Trim(); d.Level = input.Level; d.Description = input.Description?.Trim(); }, ct);

    public Task<Result> SetDesignationActiveAsync(Guid id, bool active, CancellationToken ct) =>
        SetActiveAsync(db.Designations, id, active, ct);

    public Task<Result> DeleteDesignationAsync(Guid id, CancellationToken ct) =>
        DeleteAsync(db.Designations, id, ct);

    // ---- Grades ----
    public Task<List<GradeDto>> ListGradesAsync(bool includeInactive, CancellationToken ct) =>
        Query(db.Grades, includeInactive,
            g => new GradeDto(g.Id, g.Code, g.Name, g.SortOrder, g.Description, g.IsActive, g.RowVersion), ct);

    public Task<Result<Guid>> CreateGradeAsync(GradeInput input, CancellationToken ct) =>
        CreateAsync(db.Grades, new Grade { Code = input.Code.Trim(), Name = input.Name.Trim(), SortOrder = input.SortOrder, Description = input.Description?.Trim() }, ct);

    public Task<Result> UpdateGradeAsync(Guid id, GradeInput input, CancellationToken ct) =>
        UpdateAsync(db.Grades, id, input.RowVersion, input.Name, g => { g.Name = input.Name.Trim(); g.SortOrder = input.SortOrder; g.Description = input.Description?.Trim(); }, ct);

    public Task<Result> SetGradeActiveAsync(Guid id, bool active, CancellationToken ct) =>
        SetActiveAsync(db.Grades, id, active, ct);

    public Task<Result> DeleteGradeAsync(Guid id, CancellationToken ct) =>
        DeleteAsync(db.Grades, id, ct);

    // ---- Work locations ----
    public async Task<List<WorkLocationDto>> ListWorkLocationsAsync(bool includeInactive, CancellationToken ct)
    {
        var rows = await db.WorkLocations.AsNoTracking().Where(x => includeInactive || x.IsActive).OrderBy(x => x.Code).ToListAsync(ct);
        var branchNames = await db.Branches.AsNoTracking().Where(b => rows.Select(x => x.BranchId).Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        return rows.Select(x => new WorkLocationDto(x.Id, x.Code, x.Name, x.BranchId,
            x.BranchId.HasValue && branchNames.TryGetValue(x.BranchId.Value, out var n) ? n : null,
            x.LocationType, x.Description, x.IsActive, x.RowVersion)).ToList();
    }

    public async Task<Result<Guid>> CreateWorkLocationAsync(WorkLocationInput input, CancellationToken ct)
    {
        if (input.LocationType is not null && !LocationTypes.Contains(input.LocationType))
        {
            return Error.Validation("invalid-location-type", $"Location type must be one of: {string.Join(", ", LocationTypes)}.");
        }

        if (input.BranchId is { } bid && !await db.Branches.AnyAsync(b => b.Id == bid, ct))
        {
            return Error.Validation("branch-not-found", "Branch not found.");
        }

        return await CreateAsync(db.WorkLocations, new WorkLocation { Code = input.Code.Trim(), Name = input.Name.Trim(), BranchId = input.BranchId, LocationType = input.LocationType, Description = input.Description?.Trim() }, ct);
    }

    public async Task<Result> UpdateWorkLocationAsync(Guid id, WorkLocationInput input, CancellationToken ct)
    {
        if (input.LocationType is not null && !LocationTypes.Contains(input.LocationType))
        {
            return Error.Validation("invalid-location-type", $"Location type must be one of: {string.Join(", ", LocationTypes)}.");
        }

        if (input.BranchId is { } bid && !await db.Branches.AnyAsync(b => b.Id == bid, ct))
        {
            return Error.Validation("branch-not-found", "Branch not found.");
        }

        return await UpdateAsync(db.WorkLocations, id, input.RowVersion, input.Name, l => { l.Name = input.Name.Trim(); l.BranchId = input.BranchId; l.LocationType = input.LocationType; l.Description = input.Description?.Trim(); }, ct);
    }

    public Task<Result> SetWorkLocationActiveAsync(Guid id, bool active, CancellationToken ct) =>
        SetActiveAsync(db.WorkLocations, id, active, ct);

    public Task<Result> DeleteWorkLocationAsync(Guid id, CancellationToken ct) =>
        DeleteAsync(db.WorkLocations, id, ct);

    // ---- Cost centres ----
    public async Task<List<CostCentreDto>> ListCostCentresAsync(bool includeInactive, CancellationToken ct)
    {
        var rows = await db.CostCentres.AsNoTracking().Where(x => includeInactive || x.IsActive).OrderBy(x => x.Code).ToListAsync(ct);
        var companyNames = await db.Companies.AsNoTracking().Where(c => rows.Select(x => x.CompanyId).Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return rows.Select(x => new CostCentreDto(x.Id, x.Code, x.Name, x.CompanyId,
            x.CompanyId.HasValue && companyNames.TryGetValue(x.CompanyId.Value, out var n) ? n : null,
            x.Description, x.IsActive, x.RowVersion)).ToList();
    }

    public async Task<Result<Guid>> CreateCostCentreAsync(CostCentreInput input, CancellationToken ct)
    {
        if (input.CompanyId is { } cid && !await db.Companies.AnyAsync(c => c.Id == cid, ct))
        {
            return Error.Validation("company-not-found", "Company not found.");
        }

        return await CreateAsync(db.CostCentres, new CostCentre { Code = input.Code.Trim(), Name = input.Name.Trim(), CompanyId = input.CompanyId, Description = input.Description?.Trim() }, ct);
    }

    public async Task<Result> UpdateCostCentreAsync(Guid id, CostCentreInput input, CancellationToken ct)
    {
        if (input.CompanyId is { } cid && !await db.Companies.AnyAsync(c => c.Id == cid, ct))
        {
            return Error.Validation("company-not-found", "Company not found.");
        }

        return await UpdateAsync(db.CostCentres, id, input.RowVersion, input.Name, c => { c.Name = input.Name.Trim(); c.CompanyId = input.CompanyId; c.Description = input.Description?.Trim(); }, ct);
    }

    public Task<Result> SetCostCentreActiveAsync(Guid id, bool active, CancellationToken ct) =>
        SetActiveAsync(db.CostCentres, id, active, ct);

    public Task<Result> DeleteCostCentreAsync(Guid id, CancellationToken ct) =>
        DeleteAsync(db.CostCentres, id, ct);

    // ---- shared ----
    private static async Task<List<TDto>> Query<T, TDto>(DbSet<T> set, bool includeInactive, Func<T, TDto> map, CancellationToken ct)
        where T : class, IReferenceMaster
    {
        var rows = await set.AsNoTracking().Where(x => includeInactive || x.IsActive).OrderBy(x => x.Code).ToListAsync(ct);
        return rows.Select(map).ToList();
    }

    private async Task<Result<Guid>> CreateAsync<T>(DbSet<T> set, T entity, CancellationToken ct) where T : class, IReferenceMaster
    {
        if (string.IsNullOrWhiteSpace(entity.Code) || string.IsNullOrWhiteSpace(entity.Name))
        {
            return Error.Validation("invalid-master", "Code and name are required.");
        }

        if (entity.Code.Length > 32 || entity.Name.Length > 160)
        {
            return Error.Validation("invalid-master-length", "Code must be 32 and name 160 characters or fewer.");
        }

        if (await set.AnyAsync(x => x.Code == entity.Code, ct))
        {
            return Error.Conflict("master-code-taken", $"Code '{entity.Code}' already exists.");
        }

        set.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Code uniqueness is pre-checked above; a DB error here is a concurrent duplicate.
            return Error.Conflict("master-code-taken", $"Code '{entity.Code}' already exists.");
        }

        return entity.Id;
    }

    private async Task<Result> UpdateAsync<T>(DbSet<T> set, Guid id, uint? rowVersion, string name, Action<T> apply, CancellationToken ct) where T : class, IReferenceMaster
    {
        var e = await set.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null)
        {
            return Error.NotFound("master-not-found", "Record not found.");
        }

        if (rowVersion.HasValue && e.RowVersion != rowVersion.Value)
        {
            return Error.Conflict("concurrency.conflict", "The record was changed by someone else. Reload and retry.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("invalid-master", "Name is required.");
        }

        apply(e);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("concurrency.conflict", "The record was changed by someone else. Reload and retry.");
        }

        return Result.Success();
    }

    private async Task<Result> SetActiveAsync<T>(DbSet<T> set, Guid id, bool active, CancellationToken ct) where T : class, IReferenceMaster
    {
        var e = await set.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null)
        {
            return Error.NotFound("master-not-found", "Record not found.");
        }

        e.IsActive = active;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Result> DeleteAsync<T>(DbSet<T> set, Guid id, CancellationToken ct) where T : class, IReferenceMaster
    {
        var e = await set.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null)
        {
            return Error.NotFound("master-not-found", "Record not found.");
        }

        set.Remove(e);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Deletes only fail on a referential restriction (all master FKs are Restrict).
            return Error.Conflict("master-in-use", "This record is referenced and cannot be deleted; deactivate it instead.");
        }

        return Result.Success();
    }

}
