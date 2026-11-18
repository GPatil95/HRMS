using Asp.Versioning;
using Hrms.Api.Hosting;
using Hrms.Api.Security;
using Hrms.Application.Abstractions;
using Hrms.Application.Attendance;
using Hrms.Application.Authorization;
using Hrms.Application.Modules;
using Microsoft.AspNetCore.Mvc;

namespace Hrms.Api.Controllers;

public sealed record PublishVersionRequest(DateOnly EffectiveFrom);
public sealed record RetireVersionRequest(DateOnly EffectiveTo);
public sealed record EndAssignmentRequest(DateOnly EffectiveTo);
public sealed record ImpactPreviewRequest(string Kind, Guid VersionId, DateOnly EffectiveFrom);

/// <summary>M3.1 attendance masters + effective-configuration resolver.</summary>
[ApiController, ApiVersion("1.0"), Route("api/v{version:apiVersion}/attendance")]
public sealed class AttendanceController(
    AttendanceMasterService masters, AttendanceConfigResolver resolver, IAccessGuard access) : ControllerBase
{
    private const string M = AttendanceModule.ModuleCode;

    // ---------- Shifts ----------
    [HttpGet("shifts"), RequirePermission(M, AttendancePermissions.MastersView)]
    public async Task<IActionResult> Shifts(CancellationToken ct) => Ok(await masters.ListShiftsAsync(ct));

    [HttpPost("shifts"), RequirePermission(M, AttendancePermissions.MastersManage)]
    public async Task<IActionResult> CreateShift(ShiftInput input, CancellationToken ct) =>
        (await masters.CreateShiftAsync(input, ct)).ToActionResult(s => Ok(s));

    [HttpPut("shifts/{id:guid}"), RequirePermission(M, AttendancePermissions.MastersManage)]
    public async Task<IActionResult> UpdateShift(Guid id, ShiftInput input, CancellationToken ct) =>
        (await masters.UpdateShiftAsync(id, input, ct)).ToActionResult();

    [HttpGet("shifts/{id:guid}/versions"), RequirePermission(M, AttendancePermissions.MastersView)]
    public async Task<IActionResult> ShiftVersions(Guid id, CancellationToken ct) =>
        (await masters.ListShiftVersionsAsync(id, ct)).ToActionResult(v => Ok(v));

    [HttpPost("shifts/{id:guid}/versions"), RequirePermission(M, AttendancePermissions.MastersManage)]
    public async Task<IActionResult> CreateShiftVersion(Guid id, ShiftVersionInput input, CancellationToken ct) =>
        (await masters.CreateShiftVersionAsync(id, input, ct)).ToActionResult(v => Ok(v));

    [HttpPut("shift-versions/{id:guid}"), RequirePermission(M, AttendancePermissions.MastersManage)]
    public async Task<IActionResult> UpdateShiftVersion(Guid id, ShiftVersionInput input, CancellationToken ct) =>
        (await masters.UpdateShiftVersionAsync(id, input, ct)).ToActionResult();

    [HttpPost("shift-versions/{id:guid}/publish"), RequirePermission(M, AttendancePermissions.MastersManage)]
    public async Task<IActionResult> PublishShiftVersion(Guid id, PublishVersionRequest req, CancellationToken ct) =>
        (await masters.PublishShiftVersionAsync(id, req.EffectiveFrom, await CanBackdate(ct), ct)).ToActionResult(v => Ok(v));

    [HttpPost("shift-versions/{id:guid}/retire"), RequirePermission(M, AttendancePermissions.MastersManage)]
    public async Task<IActionResult> RetireShiftVersion(Guid id, RetireVersionRequest req, CancellationToken ct) =>
        (await masters.RetireShiftVersionAsync(id, req.EffectiveTo, ct)).ToActionResult();

    // ---------- Calendars ----------
    [HttpGet("calendars"), RequirePermission(M, AttendancePermissions.MastersView)]
    public async Task<IActionResult> Calendars(CancellationToken ct) => Ok(await masters.ListCalendarsAsync(ct));

    [HttpPost("calendars"), RequirePermission(M, AttendancePermissions.MastersManage)]
    public async Task<IActionResult> CreateCalendar(WorkCalendarInput input, CancellationToken ct) =>
        (await masters.CreateCalendarAsync(input, ct)).ToActionResult(c => Ok(c));

    [HttpPut("calendars/{id:guid}"), RequirePermission(M, AttendancePermissions.MastersManage)]
    public async Task<IActionResult> UpdateCalendar(Guid id, WorkCalendarInput input, CancellationToken ct) =>
        (await masters.UpdateCalendarAsync(id, input, ct)).ToActionResult();

    [HttpGet("calendars/{id:guid}/days"), RequirePermission(M, AttendancePermissions.MastersView)]
    public async Task<IActionResult> CalendarDays(Guid id, DateOnly? from, DateOnly? to, CancellationToken ct) =>
        (await masters.ListCalendarDaysAsync(id, from, to, ct)).ToActionResult(d => Ok(d));

    /// <summary>Bulk-upsert exception days. Frontend CSV import parses to this shape.</summary>
    [HttpPost("calendars/{id:guid}/days"), RequirePermission(M, AttendancePermissions.MastersManage)]
    public async Task<IActionResult> UpsertCalendarDays(Guid id, IReadOnlyList<CalendarDayInput> days, CancellationToken ct) =>
        (await masters.UpsertCalendarDaysAsync(id, days, ct)).ToActionResult(n => Ok(new { upserted = n }));

    [HttpDelete("calendars/{id:guid}/days/{date}"), RequirePermission(M, AttendancePermissions.MastersManage)]
    public async Task<IActionResult> DeleteCalendarDay(Guid id, DateOnly date, CancellationToken ct) =>
        (await masters.DeleteCalendarDayAsync(id, date, ct)).ToActionResult();

    // ---------- Policies ----------
    [HttpGet("policies"), RequirePermission(M, AttendancePermissions.MastersView)]
    public async Task<IActionResult> Policies(CancellationToken ct) => Ok(await masters.ListPoliciesAsync(ct));

    [HttpPost("policies"), RequirePermission(M, AttendancePermissions.MastersManage)]
    public async Task<IActionResult> CreatePolicy(AttendancePolicyInput input, CancellationToken ct) =>
        (await masters.CreatePolicyAsync(input, ct)).ToActionResult(p => Ok(p));

    [HttpPut("policies/{id:guid}"), RequirePermission(M, AttendancePermissions.MastersManage)]
    public async Task<IActionResult> UpdatePolicy(Guid id, AttendancePolicyInput input, CancellationToken ct) =>
        (await masters.UpdatePolicyAsync(id, input, ct)).ToActionResult();

    [HttpGet("policies/{id:guid}/versions"), RequirePermission(M, AttendancePermissions.MastersView)]
    public async Task<IActionResult> PolicyVersions(Guid id, CancellationToken ct) =>
        (await masters.ListPolicyVersionsAsync(id, ct)).ToActionResult(v => Ok(v));

    [HttpPost("policies/{id:guid}/versions"), RequirePermission(M, AttendancePermissions.MastersManage)]
    public async Task<IActionResult> CreatePolicyVersion(Guid id, PolicyVersionInput input, CancellationToken ct) =>
        (await masters.CreatePolicyVersionAsync(id, input, ct)).ToActionResult(v => Ok(v));

    [HttpPut("policy-versions/{id:guid}"), RequirePermission(M, AttendancePermissions.MastersManage)]
    public async Task<IActionResult> UpdatePolicyVersion(Guid id, PolicyVersionInput input, CancellationToken ct) =>
        (await masters.UpdatePolicyVersionAsync(id, input, ct)).ToActionResult();

    [HttpPost("policy-versions/{id:guid}/publish"), RequirePermission(M, AttendancePermissions.MastersManage)]
    public async Task<IActionResult> PublishPolicyVersion(Guid id, PublishVersionRequest req, CancellationToken ct) =>
        (await masters.PublishPolicyVersionAsync(id, req.EffectiveFrom, await CanBackdate(ct), ct)).ToActionResult(v => Ok(v));

    [HttpPost("policy-versions/{id:guid}/retire"), RequirePermission(M, AttendancePermissions.MastersManage)]
    public async Task<IActionResult> RetirePolicyVersion(Guid id, RetireVersionRequest req, CancellationToken ct) =>
        (await masters.RetirePolicyVersionAsync(id, req.EffectiveTo, ct)).ToActionResult();

    // ---------- Assignments ----------
    [HttpGet("shift-assignments"), RequirePermission(M, AttendancePermissions.AssignmentsManage)]
    public async Task<IActionResult> ShiftAssignments(Guid? employeeId, bool current, CancellationToken ct) =>
        (await masters.ListShiftAssignmentsAsync(employeeId, current, ct)).ToActionResult(a => Ok(a));

    [HttpPost("shift-assignments"), RequirePermission(M, AttendancePermissions.AssignmentsManage)]
    public async Task<IActionResult> CreateShiftAssignment(ShiftAssignmentInput input, CancellationToken ct) =>
        (await masters.CreateShiftAssignmentAsync(input, ct)).ToActionResult(id => Ok(new { id }));

    [HttpPost("shift-assignments/{id:guid}/end"), RequirePermission(M, AttendancePermissions.AssignmentsManage)]
    public async Task<IActionResult> EndShiftAssignment(Guid id, EndAssignmentRequest req, CancellationToken ct) =>
        (await masters.EndShiftAssignmentAsync(id, req.EffectiveTo, ct)).ToActionResult();

    [HttpGet("policy-assignments"), RequirePermission(M, AttendancePermissions.AssignmentsManage)]
    public async Task<IActionResult> PolicyAssignments(Guid? employeeId, CancellationToken ct) =>
        (await masters.ListPolicyAssignmentsAsync(employeeId, ct)).ToActionResult(a => Ok(a));

    [HttpPost("policy-assignments"), RequirePermission(M, AttendancePermissions.AssignmentsManage)]
    public async Task<IActionResult> CreatePolicyAssignment(PolicyAssignmentInput input, CancellationToken ct) =>
        (await masters.CreatePolicyAssignmentAsync(input, ct)).ToActionResult(id => Ok(new { id }));

    [HttpPost("policy-assignments/{id:guid}/end"), RequirePermission(M, AttendancePermissions.AssignmentsManage)]
    public async Task<IActionResult> EndPolicyAssignment(Guid id, EndAssignmentRequest req, CancellationToken ct) =>
        (await masters.EndPolicyAssignmentAsync(id, req.EffectiveTo, ct)).ToActionResult();

    // ---------- Rosters ----------
    [HttpGet("rosters"), RequirePermission(M, AttendancePermissions.AssignmentsManage)]
    public async Task<IActionResult> Roster(Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct) =>
        (await masters.ListRosterAsync(employeeId, from, to, ct)).ToActionResult(r => Ok(r));

    [HttpPost("rosters"), RequirePermission(M, AttendancePermissions.AssignmentsManage)]
    public async Task<IActionResult> UpsertRoster(RosterUpsertInput input, CancellationToken ct) =>
        (await masters.UpsertRosterAsync(input, ct)).ToActionResult(r => Ok(r));

    // ---------- Resolver ----------
    /// <summary>Preview which config applies to an employee on a date (masters.debug view — masters.view).</summary>
    [HttpGet("config/resolve"), RequirePermission(M, AttendancePermissions.MastersView)]
    public async Task<IActionResult> Resolve(Guid employeeId, DateOnly date, CancellationToken ct) =>
        (await resolver.ResolveAsync(employeeId, date, ct)).ToActionResult(r => Ok(r));

    [HttpGet("config/resolve-range"), RequirePermission(M, AttendancePermissions.MastersView)]
    public async Task<IActionResult> ResolveRange(Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct) =>
        (await resolver.ResolveRangeAsync(employeeId, from, to, ct)).ToActionResult(r => Ok(r));

    /// <summary>Which employees does publishing this version touch?</summary>
    [HttpPost("masters/impact-preview"), RequirePermission(M, AttendancePermissions.MastersManage)]
    public async Task<IActionResult> ImpactPreview(ImpactPreviewRequest req, CancellationToken ct) =>
        Ok(req.Kind.Equals("shift", StringComparison.OrdinalIgnoreCase)
            ? await resolver.PreviewShiftPublishAsync(req.VersionId, req.EffectiveFrom, ct)
            : await resolver.PreviewPolicyPublishAsync(req.VersionId, req.EffectiveFrom, ct));

    private async Task<bool> CanBackdate(CancellationToken ct) =>
        await access.CheckAsync(M, AttendancePermissions.MastersBackdate, ct) is null;
}
