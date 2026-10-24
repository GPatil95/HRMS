using Asp.Versioning;
using Hrms.Api.Hosting;
using Hrms.Api.Security;
using Hrms.Application.Abstractions;
using Hrms.Application.Identity;
using Hrms.Shared.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hrms.Api.Controllers;

public sealed record LoginRequest(string Email, string Password);
public sealed record MfaRequest(string MfaToken, string Code);
public sealed record EmailRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string NewPassword);
public sealed record TokenRequest(string Token);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record CodeRequest(string Code);
public sealed record PasswordRequest(string Password);

/// <summary>Shared cookie/BFF authentication endpoints; each realm has its own store, cookie and route.</summary>
public abstract class AuthControllerBase(string realm, AuthService auth, IAntiforgery antiforgery, ICurrentUser user, ITenantContextAccessor tenant) : ControllerBase
{
    [HttpGet("csrf"), AllowAnonymous]
    public IActionResult Csrf() => Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    [HttpPost("login"), AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest req, CancellationToken ct)
    {
        var r = await auth.LoginAsync(new LoginCommand(req.Email, req.Password), ct);
        if (!r.IsSuccess)
        {
            return ApiResults.Problem(r.Error!);
        }

        return r.Value.MfaRequired ? Ok(new { mfaRequired = true, r.Value.MfaToken }) : await SignedIn(r.Value.Session!);
    }

    [HttpPost("mfa"), AllowAnonymous]
    public async Task<IActionResult> Mfa(MfaRequest req, CancellationToken ct)
    {
        var r = await auth.CompleteMfaAsync(req.MfaToken, req.Code, "web", ct);
        return r.IsSuccess ? await SignedIn(r.Value) : ApiResults.Problem(r.Error!);
    }

    [HttpPost("forgot-password"), AllowAnonymous]
    public async Task<IActionResult> Forgot(EmailRequest req, CancellationToken ct)
    {
        await auth.ForgotPasswordAsync(req.Email, ct);
        return Accepted();
    }

    [HttpPost("reset-password"), AllowAnonymous]
    public async Task<IActionResult> Reset(ResetPasswordRequest req, CancellationToken ct) => (await auth.ResetPasswordAsync(req.Token, req.NewPassword, ct)).ToActionResult();

    [HttpPost("verify-email"), AllowAnonymous]
    public async Task<IActionResult> VerifyEmail(TokenRequest req, CancellationToken ct) => (await auth.VerifyEmailAsync(req.Token, ct)).ToActionResult();

    [HttpGet("me"), AllowAnonymous]
    public IActionResult Me() => !user.IsAuthenticated || user.Realm != realm
        ? ApiResults.Problem(Shared.Results.Error.Unauthorized("auth.required", "Authentication required."))
        : Ok(new { user.UserId, user.Email, user.DisplayName, realm, tenant = tenant.Current is { } t ? new { t.Code, t.Name } : null });

    [HttpPost("logout"), AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (user.IsAuthenticated && user.Realm == realm)
        {
            await auth.LogoutAsync(user.UserId!.Value, user.SessionId!.Value, ct);
        }

        await HttpContext.SignOutAsync(AuthSchemes.For(realm));
        return NoContent();
    }

    [HttpPost("change-password"), Authenticated]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req, CancellationToken ct) => !Authed() ? Unauth()
        : (await auth.ChangePasswordAsync(user.UserId!.Value, user.SessionId!.Value, req.CurrentPassword, req.NewPassword, ct)).ToActionResult();

    [HttpPost("mfa/setup"), Authenticated]
    public async Task<IActionResult> MfaSetup(CancellationToken ct) => !Authed() ? Unauth() : (await auth.BeginMfaSetupAsync(user.UserId!.Value, ct)).ToActionResult();

    [HttpPost("mfa/enable"), Authenticated]
    public async Task<IActionResult> MfaEnable(CodeRequest req, CancellationToken ct) => !Authed() ? Unauth() : (await auth.EnableMfaAsync(user.UserId!.Value, req.Code, ct)).ToActionResult();

    [HttpPost("mfa/disable"), Authenticated]
    public async Task<IActionResult> MfaDisable(PasswordRequest req, CancellationToken ct) => !Authed() ? Unauth() : (await auth.DisableMfaAsync(user.UserId!.Value, req.Password, ct)).ToActionResult();

    private bool Authed() => user.IsAuthenticated && user.Realm == realm;

    private static ObjectResult Unauth() => ApiResults.Problem(Shared.Results.Error.Unauthorized("auth.required", "Authentication required."));

    private async Task<IActionResult> SignedIn(AuthenticatedSession s)
    {
        await AuthSchemes.SignInAsync(HttpContext, realm, s, realm == Realms.Tenant ? tenant.Required.TenantId : null);
        var csrf = antiforgery.GetAndStoreTokens(HttpContext).RequestToken;
        return Ok(new { s.UserId, s.Email, s.DisplayName, s.ExpiresAt, csrfToken = csrf });
    }
}

[ApiController, ApiVersion("1.0"), Route("api/v{version:apiVersion}/auth")]
public sealed class AuthController(
    [FromKeyedServices(Realms.Tenant)] AuthService auth, IAntiforgery af, ICurrentUser user, ITenantContextAccessor tenant)
    : AuthControllerBase(Realms.Tenant, auth, af, user, tenant);

[ApiController, ApiVersion("1.0"), Route("api/v{version:apiVersion}/platform/auth")]
public sealed class PlatformAuthController(
    [FromKeyedServices(Realms.Platform)] AuthService auth, IAntiforgery af, ICurrentUser user, ITenantContextAccessor tenant)
    : AuthControllerBase(Realms.Platform, auth, af, user, tenant);
