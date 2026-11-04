using System.Security.Claims;
using Hrms.Api.Hosting;
using Hrms.Application.Abstractions;
using Hrms.Application.Auditing;
using Hrms.Application.Authorization;
using Hrms.Application.Identity;
using Hrms.Application.Platform;
using Hrms.Shared.Auditing;
using Hrms.Shared.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Hrms.Api.Security;

public static class AuthSchemes
{
    public const string Selector = "hrms";
    public const string Tenant = "hrms-tenant";
    public const string Platform = "hrms-platform";

    public static string For(string realm) => realm == Realms.Platform ? Platform : Tenant;

    public static void AddHrmsAuthentication(this IServiceCollection s, IWebHostEnvironment env)
    {
        s.AddAuthentication(Selector)
            .AddPolicyScheme(Selector, Selector, o => o.ForwardDefaultSelector = ctx => For(HostRealm.Of(ctx)))
            .AddCookie(Tenant, o => Configure(o, "hrms_t", Realms.Tenant, env))
            .AddCookie(Platform, o => Configure(o, "hrms_p", Realms.Platform, env));
        s.AddAntiforgery(o =>
        {
            o.HeaderName = "X-CSRF-TOKEN";
            o.Cookie.Name = "hrms_csrf";
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
    }

    private static void Configure(CookieAuthenticationOptions o, string name, string realm, IWebHostEnvironment env)
    {
        o.Cookie.Name = name;
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        o.SlidingExpiration = false;
        o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        o.Events.OnValidatePrincipal = c => ValidateAsync(c, realm);
    }

    /// <summary>Every request re-validates the server-side session (revocation, security stamp, expiry) and the tenant binding.</summary>
    private static async Task ValidateAsync(CookieValidatePrincipalContext c, string realm)
    {
        var p = c.Principal!;
        var sp = c.HttpContext.RequestServices;
        var valid = Guid.TryParse(p.FindFirstValue(HrmsClaims.Subject), out var userId)
                    & Guid.TryParse(p.FindFirstValue(HrmsClaims.SessionId), out var sessionId)
                    && p.FindFirstValue(HrmsClaims.Realm) == realm;
        if (valid && realm == Realms.Tenant)
        {
            var tenant = sp.GetRequiredService<ITenantContextAccessor>().Current;
            valid = tenant is not null && p.FindFirstValue(HrmsClaims.TenantId) == tenant.TenantId.ToString();
            if (!valid && tenant is not null)
            {
                sp.GetRequiredService<ISecurityEventWriter>().Add("auth.cross_tenant_attempt", false, userId, null, new { claimedTenant = p.FindFirstValue(HrmsClaims.TenantId) });
                await sp.GetRequiredService<ITenantDbContext>().SaveChangesAsync(c.HttpContext.RequestAborted);
            }
        }

        if (valid)
        {
            valid = await sp.GetRequiredKeyedService<AuthService>(realm).ValidateSessionAsync(userId, sessionId, p.FindFirstValue(HrmsClaims.SecurityStamp) ?? string.Empty, c.HttpContext.RequestAborted);
        }

        if (!valid)
        {
            c.RejectPrincipal();
            await c.HttpContext.SignOutAsync(For(realm));
            return;
        }

        sp.GetRequiredService<ExecutionContextState>().SetUser(userId, realm, sessionId, p.FindFirstValue(HrmsClaims.DisplayName), p.FindFirstValue(HrmsClaims.Email),
            realm == Realms.Platform ? ActorTypes.PlatformUser : ActorTypes.User);
    }

    public static async Task<ClaimsPrincipal> SignInAsync(HttpContext ctx, string realm, AuthenticatedSession s, Guid? tenantId)
    {
        var claims = new List<Claim>
        {
            new(HrmsClaims.Subject, s.UserId.ToString()),
            new(HrmsClaims.SessionId, s.SessionId.ToString()),
            new(HrmsClaims.Realm, realm),
            new(HrmsClaims.SecurityStamp, s.SecurityStamp),
            new(HrmsClaims.AuthMethods, s.AuthMethods),
            new(HrmsClaims.DisplayName, s.DisplayName),
            new(HrmsClaims.Email, s.Email),
        };
        if (tenantId is { } tid)
        {
            claims.Add(new(HrmsClaims.TenantId, tid.ToString()));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, For(realm), HrmsClaims.Subject, null));
        await ctx.SignInAsync(For(realm), principal, new AuthenticationProperties { IsPersistent = true, ExpiresUtc = s.ExpiresAt, AllowRefresh = false });
        ctx.User = principal;
        return principal;
    }
}

/// <summary>Authenticated user of the given realm (no specific permission, e.g. own notifications). Realm must match the host realm.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AuthenticatedAttribute : Attribute, IAsyncActionFilter
{
    /// <summary>Requires a session in the realm served by the current host (for controllers shared by both realms).</summary>
    public AuthenticatedAttribute()
    {
    }

    public AuthenticatedAttribute(string realm) => Realm = realm;

    public string? Realm { get; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        var hostRealm = HostRealm.Of(context.HttpContext);
        var expected = Realm ?? hostRealm;
        if (!user.IsAuthenticated || user.Realm != expected || hostRealm != expected)
        {
            context.Result = ApiResults.Problem(Shared.Results.Error.Unauthorized("auth.required", "Authentication required."));
            return;
        }

        await next();
    }
}

/// <summary>Tenant endpoint guard: authenticated tenant user → module entitlement → permission (data scope applied in services).</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequirePermissionAttribute(string moduleCode, string permissionCode) : Attribute, IAsyncActionFilter
{
    public string ModuleCode { get; } = moduleCode;
    public string PermissionCode { get; } = permissionCode;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var sp = context.HttpContext.RequestServices;
        var user = sp.GetRequiredService<ICurrentUser>();
        if (!user.IsAuthenticated || user.Realm != Realms.Tenant || HostRealm.Of(context.HttpContext) != Realms.Tenant)
        {
            context.Result = ApiResults.Problem(Shared.Results.Error.Unauthorized("auth.required", "Authentication required."));
            return;
        }

        var error = await sp.GetRequiredService<IAccessGuard>().CheckAsync(ModuleCode, PermissionCode, context.HttpContext.RequestAborted);
        if (error is not null)
        {
            context.Result = ApiResults.Problem(error);
            return;
        }

        await next();
    }
}

/// <summary>Platform endpoint guard: authenticated platform user holding the platform permission. Grants no tenant data access.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequirePlatformPermissionAttribute(string permissionCode) : Attribute, IAsyncActionFilter
{
    public string PermissionCode { get; } = permissionCode;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var sp = context.HttpContext.RequestServices;
        var user = sp.GetRequiredService<ICurrentUser>();
        if (!user.IsAuthenticated || user.Realm != Realms.Platform || HostRealm.Of(context.HttpContext) != Realms.Platform)
        {
            context.Result = ApiResults.Problem(Shared.Results.Error.Unauthorized("auth.required", "Authentication required."));
            return;
        }

        if (!await sp.GetRequiredService<IPlatformPermissionService>().HasAsync(user.UserId!.Value, PermissionCode, context.HttpContext.RequestAborted))
        {
            context.Result = ApiResults.Problem(Shared.Results.Error.Forbidden("permission.denied", "You do not have permission to perform this action."));
            return;
        }

        await next();
    }
}
