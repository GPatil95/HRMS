using Asp.Versioning;
using Hrms.Api.Hosting;
using Hrms.Api.Security;
using Hrms.Application.Abstractions;
using Hrms.Application.Jobs;
using Hrms.Application.Modules;
using Hrms.Application.Platform;
using Hrms.Application.Tenancy;
using Hrms.Domain.Platform;
using Hrms.Shared.Paging;
using Microsoft.AspNetCore.Mvc;
using PP = Hrms.Application.Modules.PlatformPermissions;

namespace Hrms.Api.Controllers.Platform;

[ApiController, ApiVersion("1.0"), Route("api/v{version:apiVersion}/platform/tenants")]
public sealed class TenantsController(TenantManagementService tenants, TenantProvisioningService provisioning, IJobScheduler jobs, IPlatformPermissionService perms, ICurrentUser user) : ControllerBase
{
    [HttpGet, RequirePlatformPermission(PP.TenantView)]
    public async Task<IActionResult> List([FromQuery] PageRequest page, string? search, CancellationToken ct) => Ok(await tenants.ListAsync(page, search, ct));

    [HttpGet("{id:guid}"), RequirePlatformPermission(PP.TenantView)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => (await tenants.GetAsync(id, ct)).ToActionResult();

    [HttpPost, RequirePlatformPermission(PP.TenantCreate)]
    public async Task<IActionResult> Create(CreateTenantCommand cmd, CancellationToken ct)
    {
        var r = await provisioning.CreateTenantAsync(cmd, ct);
        if (!r.IsSuccess)
        {
            return ApiResults.Problem(r.Error!);
        }

        jobs.EnqueuePlatform(JobTypes.TenantProvisioning, r.Value.OperationId.ToString());
        return Accepted(new { r.Value.TenantId, r.Value.OperationId });
    }

    [HttpPost("{id:guid}/retry-provisioning"), RequirePlatformPermission(PP.TenantCreate)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct)
    {
        var r = await provisioning.PrepareRetryAsync(id, ct);
        if (!r.IsSuccess)
        {
            return ApiResults.Problem(r.Error!);
        }

        jobs.EnqueuePlatform(JobTypes.TenantProvisioning, r.Value.ToString());
        return Accepted(new { operationId = r.Value });
    }

    /// <summary>The required permission depends on the target status (activate / suspend / deactivate).</summary>
    [HttpPost("{id:guid}/status"), RequirePlatformPermission(PP.TenantView)]
    public async Task<IActionResult> Status(Guid id, ChangeTenantStatusCommand cmd, CancellationToken ct)
    {
        var required = cmd.Status switch
        {
            TenantStatus.Active => PP.TenantActivate,
            TenantStatus.Suspended => PP.TenantSuspend,
            _ => PP.TenantDeactivate,
        };
        if (!await perms.HasAsync(user.UserId!.Value, required, ct))
        {
            return ApiResults.Problem(Shared.Results.Error.Forbidden("permission.denied", "You do not have permission to perform this action."));
        }

        return (await tenants.ChangeStatusAsync(id, cmd, ct)).ToActionResult();
    }

    /// <summary>Resets the tenant admin's credentials. Empty body → sends the tenant password-reset email;
    /// { "password": "..." } → sets the admin password directly (hashed) and revokes sessions.</summary>
    [HttpPost("{id:guid}/admin/password"), RequirePlatformPermission(PP.TenantDbManage)]
    public async Task<IActionResult> ResetAdminPassword(Guid id, ResetAdminPasswordCommand cmd, CancellationToken ct)
        => (await tenants.ResetAdminPasswordAsync(id, cmd.Password, ct)).ToActionResult();

    [HttpPut("{id:guid}/modules"), RequirePlatformPermission(PP.ModuleEntitlementManage)]
    public async Task<IActionResult> Module(Guid id, SetModuleEntitlementCommand cmd, CancellationToken ct) => (await tenants.SetModuleEntitlementAsync(id, cmd, ct)).ToActionResult();

    [HttpPut("{id:guid}/features"), RequirePlatformPermission(PP.FeatureEntitlementManage)]
    public async Task<IActionResult> Feature(Guid id, SetFeatureEntitlementCommand cmd, CancellationToken ct) => (await tenants.SetFeatureEntitlementAsync(id, cmd, ct)).ToActionResult();

    [HttpPut("{id:guid}/subscription"), RequirePlatformPermission(PP.SubscriptionManage)]
    public async Task<IActionResult> Subscription(Guid id, ChangeSubscriptionCommand cmd, CancellationToken ct) => (await tenants.ChangeSubscriptionAsync(id, cmd, ct)).ToActionResult();
}

[ApiController, ApiVersion("1.0"), Route("api/v{version:apiVersion}/platform")]
public sealed class PlatformCatalogController(TenantManagementService tenants) : ControllerBase
{
    [HttpGet("catalog/modules"), RequirePlatformPermission(PP.CatalogView)]
    public async Task<IActionResult> Modules(CancellationToken ct) => Ok(await tenants.ListCatalogAsync(ct));

    [HttpGet("plans"), RequirePlatformPermission(PP.PlanView)]
    public async Task<IActionResult> Plans(CancellationToken ct) => Ok(await tenants.ListPlansAsync(ct));

    [HttpGet("audit"), RequirePlatformPermission(PP.AuditView)]
    public async Task<IActionResult> Audit(CancellationToken ct) => Ok(await tenants.RecentAuditAsync(100, ct));

    [HttpGet("security-events"), RequirePlatformPermission(PP.AuditView)]
    public async Task<IActionResult> SecurityEvents(CancellationToken ct) => Ok(await tenants.RecentSecurityEventsAsync(100, ct));
}

[ApiController, ApiVersion("1.0"), Route("api/v{version:apiVersion}/platform/support-grants")]
public sealed class SupportGrantsController(SupportAccessService support) : ControllerBase
{
    [HttpGet, RequirePlatformPermission(PP.SupportRequest)]
    public async Task<IActionResult> List(Guid? tenantId, CancellationToken ct) => Ok(await support.ListAsync(tenantId, ct));

    [HttpPost, RequirePlatformPermission(PP.SupportRequest)]
    public async Task<IActionResult> RequestAccess(RequestSupportAccessCommand cmd, CancellationToken ct) => (await support.RequestAsync(cmd, ct)).ToActionResult(id => Ok(new { id }));

    [HttpPost("{id:guid}/approve"), RequirePlatformPermission(PP.SupportManage)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct) => (await support.DecideAsync(id, true, ct)).ToActionResult();

    [HttpPost("{id:guid}/reject"), RequirePlatformPermission(PP.SupportManage)]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct) => (await support.DecideAsync(id, false, ct)).ToActionResult();

    [HttpPost("{id:guid}/revoke"), RequirePlatformPermission(PP.SupportManage)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct) => (await support.RevokeAsync(id, ct)).ToActionResult();
}
