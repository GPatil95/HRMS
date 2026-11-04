using System.Diagnostics;
using Hrms.Application.Abstractions;
using Hrms.Domain.Platform;
using Hrms.Shared.Auditing;
using Hrms.Shared.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;

namespace Hrms.Api.Hosting;

public static class HostRealm
{
    public const string ItemKey = "hrms.realm";

    public static string Of(HttpContext ctx) => ctx.Items[ItemKey] as string ?? Realms.Tenant;
}

/// <summary>Correlation id, client IP/user agent and audit source for the request scope. Never logs bodies or credentials.</summary>
public sealed class RequestContextMiddleware(RequestDelegate next, ILogger<RequestContextMiddleware> logger)
{
    public const string CorrelationHeader = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext ctx, ExecutionContextState state)
    {
        var cid = ctx.Request.Headers[CorrelationHeader].FirstOrDefault();
        if (string.IsNullOrEmpty(cid) || cid.Length > 64 || !cid.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            cid = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        }

        var source = string.Equals(ctx.Request.Headers["X-Client"].FirstOrDefault(), "mobile", StringComparison.OrdinalIgnoreCase) ? AuditSources.Mobile : AuditSources.Web;
        state.SetRequest(cid, ctx.Connection.RemoteIpAddress?.ToString(), ctx.Request.Headers.UserAgent.ToString(), source);
        ctx.Response.Headers[CorrelationHeader] = cid;
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = cid }))
        {
            await next(ctx);
        }
    }
}

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        h.XContentTypeOptions = "nosniff";
        h.XFrameOptions = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            h.CacheControl = "no-store";
            h.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        }

        return next(ctx);
    }
}

/// <summary>
/// Establishes the realm and trusted tenant context from the request host only (verified domain registry). Tenant identifiers
/// supplied in routes, query strings, bodies or headers are never consulted. Admin hosts serve only the platform API; tenant
/// hosts never serve it. Tenant must be Active and its database is resolved and identity-verified before authentication.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next, IOptions<HostingOptions> hosting)
{
    public async Task InvokeAsync(HttpContext ctx, ITenantRegistry registry, ITenantContextAccessor accessor, ITenantDatabaseResolver resolver)
    {
        var path = ctx.Request.Path;
        if (path.StartsWithSegments("/health") || path.StartsWithSegments("/openapi") || path.StartsWithSegments("/swagger"))
        {
            await next(ctx);
            return;
        }

        var host = ctx.Request.Host.Host.ToLowerInvariant();
        var isAdmin = hosting.Value.AdminHosts.Contains(host, StringComparer.OrdinalIgnoreCase);
        var isPlatformPath = path.StartsWithSegments("/api/v1/platform");
        ctx.Items[HostRealm.ItemKey] = isAdmin ? Realms.Platform : Realms.Tenant;

        if (isAdmin != isPlatformPath)
        {
            await Problem(ctx, StatusCodes.Status404NotFound, "route.not_found", "Not found.");
            return;
        }

        if (isAdmin)
        {
            await next(ctx);
            return;
        }

        var tenant = await registry.FindByHostAsync(host, ctx.RequestAborted);
        if (tenant is null)
        {
            await Problem(ctx, StatusCodes.Status404NotFound, "tenant.not_found", "Unknown tenant.");
            return;
        }

        if (tenant.Status != TenantStatus.Active)
        {
            await Problem(ctx, StatusCodes.Status403Forbidden, "tenant.inactive", "This organisation is not active.");
            return;
        }

        accessor.Set(tenant);
        await resolver.EnsureResolvedAsync(tenant, ctx.RequestAborted);
        await next(ctx);
    }

    internal static Task Problem(HttpContext ctx, int status, string code, string title)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails { Status = status, Title = title, Extensions = { ["code"] = code } }, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
    }
}

/// <summary>Double-submit antiforgery token required on every unsafe API request (header X-CSRF-TOKEN).</summary>
public sealed class AntiforgeryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, IAntiforgery antiforgery)
    {
        if (ctx.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method)
            && !await antiforgery.IsRequestValidAsync(ctx))
        {
            await TenantResolutionMiddleware.Problem(ctx, StatusCodes.Status400BadRequest, "csrf.invalid", "Missing or invalid CSRF token.");
            return;
        }

        await next(ctx);
    }
}
