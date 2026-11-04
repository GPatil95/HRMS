using Asp.Versioning;
using Hrms.Api.Security;
using Hrms.Application.Modules;
using Hrms.Domain.Platform;
using Hrms.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace Hrms.Testing;

/// <summary>Test-only optional module used to prove entitlement enforcement without shipping a fake business API.</summary>
public sealed class PayrollProbeModule : ModuleDefinitionBase
{
    public const string ModuleCode = "test.payroll_probe";
    public const string ViewPermission = "test.payroll_probe.payslip.view";

    public override string Code => ModuleCode;
    public override string Name => "Payroll probe (test only)";
    public override ModuleKind Kind => ModuleKind.Optional;
    public override IReadOnlyList<PermissionDefinition> Permissions => [P(ViewPermission, PermissionActions.View, "View probe payslips")];
}

[ApiController, ApiVersion("1.0"), Route("api/v{version:apiVersion}/probe/payroll")]
public sealed class PayrollProbeController : ControllerBase
{
    [HttpGet, RequirePermission(PayrollProbeModule.ModuleCode, PayrollProbeModule.ViewPermission)]
    public IActionResult Get() => Ok(new { ok = true });
}
