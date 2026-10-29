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

/// <summary>Company / branch / department master detail: addresses, registrations, contacts, bank accounts, HOD history, logo, documents and effective config.</summary>
[ApiController, ApiVersion("1.0"), Route("api/v{version:apiVersion}/org")]
public sealed class OrgMastersController(OrgMastersService masters) : ControllerBase
{
    private const string O = OrganizationModule.ModuleCode;
    private const string C = "org.company";

    // ---- Company detail ----
    [HttpGet("companies/{id:guid}"), RequirePermission(O, P.CompanyView)]
    public async Task<IActionResult> GetCompany(Guid id, CancellationToken ct) => (await masters.GetCompanyAsync(id, ct)).ToActionResult();

    [HttpPut("companies/{id:guid}"), RequirePermission(O, P.CompanyEdit)]
    public async Task<IActionResult> UpdateCompany(Guid id, CompanyFields cmd, CancellationToken ct) => (await masters.UpdateCompanyAsync(id, cmd, ct)).ToActionResult();

    // ---- Company addresses ----
    [HttpPost("companies/{id:guid}/addresses"), RequirePermission(O, P.CompanyEdit)]
    public async Task<IActionResult> AddAddress(Guid id, CompanyAddressInput input, CancellationToken ct) => (await masters.AddAddressAsync(id, input, ct)).ToActionResult(rid => Ok(new { id = rid }));

    [HttpPut("companies/{id:guid}/addresses/{addressId:guid}"), RequirePermission(O, P.CompanyEdit)]
    public async Task<IActionResult> UpdateAddress(Guid id, Guid addressId, CompanyAddressInput input, CancellationToken ct) => (await masters.UpdateAddressAsync(id, addressId, input, ct)).ToActionResult();

    [HttpDelete("companies/{id:guid}/addresses/{addressId:guid}"), RequirePermission(O, P.CompanyEdit)]
    public async Task<IActionResult> DeleteAddress(Guid id, Guid addressId, CancellationToken ct) => (await masters.DeleteAddressAsync(id, addressId, ct)).ToActionResult();

    // ---- Registrations (company + branch) ----
    [HttpPost("companies/{id:guid}/registrations"), RequirePermission(O, P.CompanyEdit)]
    public async Task<IActionResult> AddCompanyRegistration(Guid id, RegistrationInput input, CancellationToken ct) => (await masters.AddRegistrationAsync(id, null, input, ct)).ToActionResult(rid => Ok(new { id = rid }));

    [HttpPut("companies/{id:guid}/registrations/{registrationId:guid}"), RequirePermission(O, P.CompanyEdit)]
    public async Task<IActionResult> UpdateCompanyRegistration(Guid id, Guid registrationId, RegistrationInput input, CancellationToken ct) => (await masters.UpdateRegistrationAsync(id, null, registrationId, input, ct)).ToActionResult();

    [HttpDelete("companies/{id:guid}/registrations/{registrationId:guid}"), RequirePermission(O, P.CompanyEdit)]
    public async Task<IActionResult> DeleteCompanyRegistration(Guid id, Guid registrationId, CancellationToken ct) => (await masters.DeleteRegistrationAsync(id, null, registrationId, ct)).ToActionResult();

    [HttpPost("branches/{id:guid}/registrations"), RequirePermission(O, P.BranchEdit)]
    public async Task<IActionResult> AddBranchRegistration(Guid id, RegistrationInput input, CancellationToken ct) => (await masters.AddRegistrationAsync(null, id, input, ct)).ToActionResult(rid => Ok(new { id = rid }));

    [HttpPut("branches/{id:guid}/registrations/{registrationId:guid}"), RequirePermission(O, P.BranchEdit)]
    public async Task<IActionResult> UpdateBranchRegistration(Guid id, Guid registrationId, RegistrationInput input, CancellationToken ct) => (await masters.UpdateRegistrationAsync(null, id, registrationId, input, ct)).ToActionResult();

    [HttpDelete("branches/{id:guid}/registrations/{registrationId:guid}"), RequirePermission(O, P.BranchEdit)]
    public async Task<IActionResult> DeleteBranchRegistration(Guid id, Guid registrationId, CancellationToken ct) => (await masters.DeleteRegistrationAsync(null, id, registrationId, ct)).ToActionResult();

    // ---- Company contacts ----
    [HttpPost("companies/{id:guid}/contacts"), RequirePermission(O, P.CompanyEdit)]
    public async Task<IActionResult> AddContact(Guid id, CompanyContactInput input, CancellationToken ct) => (await masters.AddContactAsync(id, input, ct)).ToActionResult(rid => Ok(new { id = rid }));

    [HttpPut("companies/{id:guid}/contacts/{contactId:guid}"), RequirePermission(O, P.CompanyEdit)]
    public async Task<IActionResult> UpdateContact(Guid id, Guid contactId, CompanyContactInput input, CancellationToken ct) => (await masters.UpdateContactAsync(id, contactId, input, ct)).ToActionResult();

    [HttpDelete("companies/{id:guid}/contacts/{contactId:guid}"), RequirePermission(O, P.CompanyEdit)]
    public async Task<IActionResult> DeleteContact(Guid id, Guid contactId, CancellationToken ct) => (await masters.DeleteContactAsync(id, contactId, ct)).ToActionResult();

    // ---- Bank accounts (edit permission = sensitive financial access) ----
    [HttpGet("companies/{id:guid}/bank-accounts"), RequirePermission(O, P.CompanyEdit)]
    public async Task<IActionResult> BankAccounts(Guid id, CancellationToken ct) => (await masters.ListBankAccountsAsync(id, ct)).ToActionResult();

    [HttpPost("companies/{id:guid}/bank-accounts"), RequirePermission(O, P.CompanyEdit)]
    public async Task<IActionResult> AddBankAccount(Guid id, BankAccountInput input, CancellationToken ct) => (await masters.AddBankAccountAsync(id, input, ct)).ToActionResult(rid => Ok(new { id = rid }));

    [HttpPut("companies/{id:guid}/bank-accounts/{accountId:guid}"), RequirePermission(O, P.CompanyEdit)]
    public async Task<IActionResult> UpdateBankAccount(Guid id, Guid accountId, BankAccountInput input, CancellationToken ct) => (await masters.UpdateBankAccountAsync(id, accountId, input, ct)).ToActionResult();

    [HttpDelete("companies/{id:guid}/bank-accounts/{accountId:guid}"), RequirePermission(O, P.CompanyEdit)]
    public async Task<IActionResult> DeleteBankAccount(Guid id, Guid accountId, CancellationToken ct) => (await masters.DeleteBankAccountAsync(id, accountId, ct)).ToActionResult();

    // ---- Logo ----
    [HttpPost("companies/{id:guid}/logo"), RequirePermission(O, P.CompanyEdit), RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> UploadLogo(Guid id, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return (await masters.UploadLogoAsync(id, file.FileName, file.ContentType, stream, file.Length, ct)).ToActionResult(fid => Ok(new { fileId = fid }));
    }

    [HttpGet("companies/{id:guid}/logo"), RequirePermission(O, P.CompanyView)]
    public async Task<IActionResult> GetLogo(Guid id, CancellationToken ct) =>
        (await masters.DownloadLogoAsync(id, ct)).ToActionResult(d => File(d.Content, d.ContentType, d.FileName));

    [HttpDelete("companies/{id:guid}/logo"), RequirePermission(O, P.CompanyEdit)]
    public async Task<IActionResult> RemoveLogo(Guid id, CancellationToken ct) => (await masters.RemoveLogoAsync(id, ct)).ToActionResult();

    // ---- Branding resolver (logo for ID cards / payslips / letters) ----
    [HttpGet("branding"), RequirePermission(O, P.CompanyView)]
    public async Task<IActionResult> Branding([FromQuery] Guid? companyId, [FromQuery] Guid? employeeId, CancellationToken ct) =>
        (await masters.ResolveBrandingAsync(companyId, employeeId, ct)).ToActionResult();

    // ---- Documents ----
    [HttpGet("companies/{id:guid}/documents"), RequirePermission(O, P.CompanyView)]
    public async Task<IActionResult> CompanyDocuments(Guid id, CancellationToken ct) => (await masters.ListDocumentsAsync(C, id, ct)).ToActionResult();

    [HttpPost("companies/{id:guid}/documents"), RequirePermission(O, P.CompanyEdit), RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadCompanyDocument(Guid id, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return (await masters.UploadDocumentAsync(C, id, file.FileName, file.ContentType, stream, file.Length, ct)).ToActionResult();
    }

    [HttpGet("companies/{id:guid}/documents/{fileId:guid}/content"), RequirePermission(O, P.CompanyView)]
    public async Task<IActionResult> DownloadCompanyDocument(Guid id, Guid fileId, CancellationToken ct) =>
        (await masters.DownloadDocumentAsync(C, id, fileId, ct)).ToActionResult(d => File(d.Content, d.ContentType, d.FileName));

    [HttpDelete("companies/{id:guid}/documents/{fileId:guid}"), RequirePermission(O, P.CompanyEdit)]
    public async Task<IActionResult> DeleteCompanyDocument(Guid id, Guid fileId, CancellationToken ct) => (await masters.DeleteDocumentAsync(C, id, fileId, ct)).ToActionResult();

    // ---- Branch detail ----
    [HttpGet("branches/{id:guid}"), RequirePermission(O, P.BranchView)]
    public async Task<IActionResult> GetBranch(Guid id, CancellationToken ct) => (await masters.GetBranchAsync(id, ct)).ToActionResult();

    [HttpPut("branches/{id:guid}"), RequirePermission(O, P.BranchEdit)]
    public async Task<IActionResult> UpdateBranch(Guid id, BranchFields cmd, CancellationToken ct) => (await masters.UpdateBranchAsync(id, cmd, ct)).ToActionResult();

    [HttpGet("branches/{id:guid}/documents"), RequirePermission(O, P.BranchView)]
    public async Task<IActionResult> BranchDocuments(Guid id, CancellationToken ct) => (await masters.ListDocumentsAsync("org.branch", id, ct)).ToActionResult();

    [HttpPost("branches/{id:guid}/documents"), RequirePermission(O, P.BranchEdit), RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadBranchDocument(Guid id, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return (await masters.UploadDocumentAsync("org.branch", id, file.FileName, file.ContentType, stream, file.Length, ct)).ToActionResult();
    }

    [HttpGet("branches/{id:guid}/documents/{fileId:guid}/content"), RequirePermission(O, P.BranchView)]
    public async Task<IActionResult> DownloadBranchDocument(Guid id, Guid fileId, CancellationToken ct) =>
        (await masters.DownloadDocumentAsync("org.branch", id, fileId, ct)).ToActionResult(d => File(d.Content, d.ContentType, d.FileName));

    [HttpDelete("branches/{id:guid}/documents/{fileId:guid}"), RequirePermission(O, P.BranchEdit)]
    public async Task<IActionResult> DeleteBranchDocument(Guid id, Guid fileId, CancellationToken ct) => (await masters.DeleteDocumentAsync("org.branch", id, fileId, ct)).ToActionResult();

    // ---- Department detail ----
    [HttpGet("departments/{id:guid}"), RequirePermission(O, P.DepartmentView)]
    public async Task<IActionResult> GetDepartment(Guid id, CancellationToken ct) => (await masters.GetDepartmentAsync(id, ct)).ToActionResult();

    [HttpPut("departments/{id:guid}"), RequirePermission(O, P.DepartmentEdit)]
    public async Task<IActionResult> UpdateDepartment(Guid id, DepartmentFields cmd, CancellationToken ct) => (await masters.UpdateDepartmentAsync(id, cmd, ct)).ToActionResult();

    [HttpGet("departments/{id:guid}/assignments"), RequirePermission(O, P.DepartmentView)]
    public async Task<IActionResult> DepartmentAssignments(Guid id, CancellationToken ct) => (await masters.ListDepartmentAssignmentsAsync(id, ct)).ToActionResult();

    [HttpPost("departments/{id:guid}/hod"), RequirePermission(O, P.DepartmentEdit)]
    public async Task<IActionResult> AssignDepartmentRole(Guid id, AssignDepartmentRoleCommand cmd, CancellationToken ct) => (await masters.AssignDepartmentRoleAsync(id, cmd, ct)).ToActionResult();

    // ---- Effective config + weekly-off preview ----
    [HttpGet("companies/{id:guid}/effective-config"), RequirePermission(O, P.CompanyView)]
    public async Task<IActionResult> CompanyConfig(Guid id, CancellationToken ct) => (await masters.ResolveEffectiveAsync("company", id, ct)).ToActionResult();

    [HttpGet("branches/{id:guid}/effective-config"), RequirePermission(O, P.BranchView)]
    public async Task<IActionResult> BranchConfig(Guid id, CancellationToken ct) => (await masters.ResolveEffectiveAsync("branch", id, ct)).ToActionResult();

    [HttpGet("departments/{id:guid}/effective-config"), RequirePermission(O, P.DepartmentView)]
    public async Task<IActionResult> DepartmentConfig(Guid id, CancellationToken ct) => (await masters.ResolveEffectiveAsync("department", id, ct)).ToActionResult();

    [HttpGet("employees/{id:guid}/effective-config"), RequirePermission(O, P.DepartmentView)]
    public async Task<IActionResult> EmployeeConfig(Guid id, CancellationToken ct) => (await masters.ResolveEffectiveAsync("employee", id, ct)).ToActionResult();

    [HttpGet("companies/{id:guid}/weekly-off-preview"), RequirePermission(O, P.CompanyView)]
    public async Task<IActionResult> CompanyWeeklyOff(Guid id, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        (await masters.PreviewWeeklyOffAsync("company", id, from, to, ct)).ToActionResult();

    [HttpGet("branches/{id:guid}/weekly-off-preview"), RequirePermission(O, P.BranchView)]
    public async Task<IActionResult> BranchWeeklyOff(Guid id, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        (await masters.PreviewWeeklyOffAsync("branch", id, from, to, ct)).ToActionResult();

    [HttpGet("departments/{id:guid}/weekly-off-preview"), RequirePermission(O, P.DepartmentView)]
    public async Task<IActionResult> DepartmentWeeklyOff(Guid id, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        (await masters.PreviewWeeklyOffAsync("department", id, from, to, ct)).ToActionResult();
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
