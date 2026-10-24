using Asp.Versioning;
using Hrms.Api.Hosting;
using Hrms.Api.Security;
using Hrms.Application.Abstractions;
using Hrms.Application.Authorization;
using Hrms.Application.Dashboards;
using Hrms.Application.Files;
using Hrms.Application.Identity;
using Hrms.Application.Modules;
using Hrms.Application.Notifications;
using Hrms.Application.Organization;
using Hrms.Application.Reporting;
using Hrms.Domain.Common;
using Hrms.Shared.Paging;
using Hrms.Shared.Security;
using Microsoft.AspNetCore.Mvc;
using P = Hrms.Application.Modules.FoundationPermissions;

namespace Hrms.Api.Controllers;

public sealed record UserStatusRequest(UserStatusAction Action, string? Reason);

[ApiController, ApiVersion("1.0"), Route("api/v{version:apiVersion}/me")]
public sealed class MeController(INavigationService navigation, IPermissionService permissions, ICurrentUser user) : ControllerBase
{
    [HttpGet("navigation"), Authenticated(Realms.Tenant)]
    public async Task<IActionResult> Navigation(CancellationToken ct) => Ok(await navigation.GetTenantNavigationAsync(user.UserId!.Value, ct));

    [HttpGet("permissions"), Authenticated(Realms.Tenant)]
    public async Task<IActionResult> Permissions(CancellationToken ct) => Ok(await permissions.GetEffectiveAsync(user.UserId!.Value, ct));
}

[ApiController, ApiVersion("1.0"), Route("api/v{version:apiVersion}/users")]
public sealed class UsersController(UserAdministrationService users) : ControllerBase
{
    [HttpGet, RequirePermission(IdentityModule.ModuleCode, P.UserView)]
    public async Task<IActionResult> List([FromQuery] PageRequest page, string? search, UserStatus? status, CancellationToken ct) => Ok(await users.ListAsync(page, search, status, ct));

    [HttpGet("{id:guid}"), RequirePermission(IdentityModule.ModuleCode, P.UserView)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => (await users.GetAsync(id, ct)).ToActionResult();

    [HttpPost, RequirePermission(IdentityModule.ModuleCode, P.UserCreate)]
    public async Task<IActionResult> Create(CreateUserCommand cmd, CancellationToken ct) => (await users.CreateAsync(cmd, ct)).ToActionResult(id => Created($"api/v1/users/{id}", new { id }));

    [HttpPost("{id:guid}/status"), RequirePermission(IdentityModule.ModuleCode, P.UserManageStatus)]
    public async Task<IActionResult> Status(Guid id, UserStatusRequest req, CancellationToken ct) => (await users.ChangeStatusAsync(id, req.Action, req.Reason, ct)).ToActionResult();

    [HttpPost("{id:guid}/roles"), RequirePermission(IdentityModule.ModuleCode, P.UserAssignRole)]
    public async Task<IActionResult> AssignRole(Guid id, AssignRoleCommand cmd, CancellationToken ct) => (await users.AssignRoleAsync(id, cmd, ct)).ToActionResult(a => Ok(new { id = a }));

    [HttpDelete("{id:guid}/roles/{assignmentId:guid}"), RequirePermission(IdentityModule.ModuleCode, P.UserAssignRole)]
    public async Task<IActionResult> RemoveRole(Guid id, Guid assignmentId, CancellationToken ct) => (await users.RemoveRoleAsync(id, assignmentId, ct)).ToActionResult();

    [HttpPost("{id:guid}/overrides"), RequirePermission(IdentityModule.ModuleCode, P.OverrideManage)]
    public async Task<IActionResult> AddOverride(Guid id, AddOverrideCommand cmd, CancellationToken ct) => (await users.AddOverrideAsync(id, cmd, ct)).ToActionResult(o => Ok(new { id = o }));

    [HttpDelete("{id:guid}/overrides/{overrideId:guid}"), RequirePermission(IdentityModule.ModuleCode, P.OverrideManage)]
    public async Task<IActionResult> RemoveOverride(Guid id, Guid overrideId, CancellationToken ct) => (await users.RemoveOverrideAsync(id, overrideId, ct)).ToActionResult();

    [HttpPost("{id:guid}/sessions/revoke"), RequirePermission(IdentityModule.ModuleCode, P.SessionRevoke)]
    public async Task<IActionResult> RevokeSessions(Guid id, CancellationToken ct) => (await users.RevokeSessionsAsync(id, ct)).ToActionResult(n => Ok(new { revoked = n }));

    [HttpGet("{id:guid}/effective-permissions"), RequirePermission(IdentityModule.ModuleCode, P.UserView)]
    public async Task<IActionResult> Effective(Guid id, CancellationToken ct) => Ok(await users.GetEffectivePermissionsAsync(id, ct));
}

[ApiController, ApiVersion("1.0"), Route("api/v{version:apiVersion}/roles")]
public sealed class RolesController(RoleService roles) : ControllerBase
{
    [HttpGet("permissions"), RequirePermission(IdentityModule.ModuleCode, P.RoleView)]
    public async Task<IActionResult> Permissions(CancellationToken ct) => Ok(await roles.ListPermissionsAsync(ct));

    [HttpGet, RequirePermission(IdentityModule.ModuleCode, P.RoleView)]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await roles.ListAsync(ct));

    [HttpGet("{id:guid}"), RequirePermission(IdentityModule.ModuleCode, P.RoleView)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => (await roles.GetAsync(id, ct)).ToActionResult();

    [HttpPost, RequirePermission(IdentityModule.ModuleCode, P.RoleCreate)]
    public async Task<IActionResult> Create(UpsertRoleCommand cmd, CancellationToken ct) => (await roles.CreateAsync(cmd, ct)).ToActionResult(id => Created($"api/v1/roles/{id}", new { id }));

    [HttpPut("{id:guid}"), RequirePermission(IdentityModule.ModuleCode, P.RoleEdit)]
    public async Task<IActionResult> Update(Guid id, UpsertRoleCommand cmd, CancellationToken ct) => (await roles.UpdateAsync(id, cmd, ct)).ToActionResult();

    [HttpDelete("{id:guid}"), RequirePermission(IdentityModule.ModuleCode, P.RoleDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) => (await roles.DeleteAsync(id, ct)).ToActionResult();
}

[ApiController, ApiVersion("1.0"), Route("api/v{version:apiVersion}/org")]
public sealed class OrganizationController(OrganizationService org) : ControllerBase
{
    [HttpGet("companies"), RequirePermission(OrganizationModule.ModuleCode, P.CompanyView)]
    public async Task<IActionResult> Companies(CancellationToken ct) => Ok(await org.ListCompaniesAsync(ct));

    [HttpPost("companies"), RequirePermission(OrganizationModule.ModuleCode, P.CompanyCreate)]
    public async Task<IActionResult> CreateCompany(CreateCompanyCommand cmd, CancellationToken ct) => (await org.CreateCompanyAsync(cmd, ct)).ToActionResult(id => Ok(new { id }));

    [HttpGet("branches"), RequirePermission(OrganizationModule.ModuleCode, P.BranchView)]
    public async Task<IActionResult> Branches(CancellationToken ct) => Ok(await org.ListBranchesAsync(ct));

    [HttpPost("branches"), RequirePermission(OrganizationModule.ModuleCode, P.BranchCreate)]
    public async Task<IActionResult> CreateBranch(CreateBranchCommand cmd, CancellationToken ct) => (await org.CreateBranchAsync(cmd, ct)).ToActionResult(id => Ok(new { id }));

    [HttpGet("departments"), RequirePermission(OrganizationModule.ModuleCode, P.DepartmentView)]
    public async Task<IActionResult> Departments(CancellationToken ct) => Ok(await org.ListDepartmentsAsync(ct));

    [HttpPost("departments"), RequirePermission(OrganizationModule.ModuleCode, P.DepartmentCreate)]
    public async Task<IActionResult> CreateDepartment(CreateDepartmentCommand cmd, CancellationToken ct) => (await org.CreateDepartmentAsync(cmd, ct)).ToActionResult(id => Ok(new { id }));
}

[ApiController, ApiVersion("1.0"), Route("api/v{version:apiVersion}/employees")]
public sealed class EmployeeDirectoryController(OrganizationService org) : ControllerBase
{
    [HttpGet, RequirePermission(EmployeeFoundationModule.ModuleCode, P.EmployeeView)]
    public async Task<IActionResult> List([FromQuery] PageRequest page, string? search, CancellationToken ct) => Ok(await org.ListEmployeesAsync(page, search, ct));

    [HttpGet("{id:guid}"), RequirePermission(EmployeeFoundationModule.ModuleCode, P.EmployeeView)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => (await org.GetEmployeeAsync(id, ct)).ToActionResult();

    [HttpPost, RequirePermission(EmployeeFoundationModule.ModuleCode, P.EmployeeCreate)]
    public async Task<IActionResult> Create(CreateEmployeeCommand cmd, CancellationToken ct) => (await org.CreateEmployeeAsync(cmd, ct)).ToActionResult(id => Ok(new { id }));
}

[ApiController, ApiVersion("1.0"), Route("api/v{version:apiVersion}/files")]
public sealed class FilesController(FileService files) : ControllerBase
{
    [HttpPost, RequirePermission(FilesModule.ModuleCode, P.FileUpload), RequestSizeLimit(26 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file, [FromForm] string purpose, [FromForm] string? ownerEntityType, [FromForm] string? ownerEntityId, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return (await files.UploadAsync(new FileUploadRequest(purpose, file.FileName, file.ContentType, stream, file.Length, ownerEntityType, ownerEntityId), ct)).ToActionResult();
    }

    [HttpGet("{id:guid}"), RequirePermission(FilesModule.ModuleCode, P.FileDownload)]
    public async Task<IActionResult> Metadata(Guid id, CancellationToken ct) => (await files.GetMetadataAsync(id, ct)).ToActionResult();

    [HttpGet("{id:guid}/content"), RequirePermission(FilesModule.ModuleCode, P.FileDownload)]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct) => (await files.DownloadAsync(id, ct)).ToActionResult(d => File(d.Content, d.ContentType, d.FileName));

    [HttpDelete("{id:guid}"), RequirePermission(FilesModule.ModuleCode, P.FileDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) => (await files.DeleteAsync(id, ct)).ToActionResult();
}

[ApiController, ApiVersion("1.0"), Route("api/v{version:apiVersion}/notifications")]
public sealed class NotificationsController(NotificationService notifications) : ControllerBase
{
    [HttpGet, Authenticated(Realms.Tenant)]
    public async Task<IActionResult> List([FromQuery] PageRequest page, bool unreadOnly, CancellationToken ct) => Ok(await notifications.ListMineAsync(page, unreadOnly, ct));

    [HttpGet("unread-count"), Authenticated(Realms.Tenant)]
    public async Task<IActionResult> Unread(CancellationToken ct) => Ok(new { count = await notifications.UnreadCountAsync(ct) });

    [HttpPost("{id:guid}/read"), Authenticated(Realms.Tenant)]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct) => (await notifications.MarkReadAsync(id, ct)).ToActionResult();

    [HttpPost("read-all"), Authenticated(Realms.Tenant)]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(ct);
        return NoContent();
    }
}

[ApiController, ApiVersion("1.0"), Route("api/v{version:apiVersion}/dashboard")]
public sealed class DashboardController(DashboardService dashboards) : ControllerBase
{
    [HttpGet, RequirePermission(DashboardModule.ModuleCode, P.DashboardView)]
    public async Task<IActionResult> Mine(CancellationToken ct) => await dashboards.GetMineAsync(ct) is { } d ? Ok(d) : NotFound();

    [HttpGet("widgets/{code}"), RequirePermission(DashboardModule.ModuleCode, P.DashboardView)]
    public async Task<IActionResult> Widget(string code, CancellationToken ct)
    {
        var (found, data) = await dashboards.GetWidgetDataAsync(code, ct);
        return found ? Ok(data) : NotFound();
    }
}

[ApiController, ApiVersion("1.0"), Route("api/v{version:apiVersion}/reports")]
public sealed class ReportsController(ReportingService reports) : ControllerBase
{
    [HttpGet, RequirePermission(ReportingModule.ModuleCode, P.ReportView)]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await reports.ListAvailableAsync(ct));

    [HttpGet("{code}"), RequirePermission(ReportingModule.ModuleCode, P.ReportView)]
    public async Task<IActionResult> Run(string code, CancellationToken ct) => (await reports.RunAsync(code, ct)).ToActionResult();

    [HttpGet("{code}/export"), RequirePermission(ReportingModule.ModuleCode, P.ReportExport)]
    public async Task<IActionResult> Export(string code, CancellationToken ct) => (await reports.ExportAsync(code, ct)).ToActionResult(e => File(e.Content, e.ContentType, e.FileName));
}
