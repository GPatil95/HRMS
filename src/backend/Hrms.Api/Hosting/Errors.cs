using Hrms.Application.Abstractions;
using Hrms.Infrastructure.Tenancy;
using Hrms.Shared.Results;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Api.Hosting;

public static class ApiResults
{
    public static ObjectResult Problem(Error e)
    {
        var status = e.Code == "rate-limited" ? StatusCodes.Status429TooManyRequests : e.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
            ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest,
        };
        var pd = e.Details is { Count: > 0 }
            ? new ValidationProblemDetails(e.Details.ToDictionary(k => k.Key, v => v.Value)) { Status = status, Title = e.Message }
            : new ProblemDetails { Status = status, Title = e.Message };
        pd.Extensions["code"] = e.Code;
        return new ObjectResult(pd) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }

    public static IActionResult ToActionResult(this Result r) => r.IsSuccess ? new NoContentResult() : Problem(r.Error!);

    public static IActionResult ToActionResult<T>(this Result<T> r, Func<T, IActionResult>? onSuccess = null) =>
        r.IsSuccess ? onSuccess?.Invoke(r.Value!) ?? new OkObjectResult(r.Value) : Problem(r.Error!);
}

/// <summary>Maps unhandled exceptions to RFC 7807 responses without leaking internals; correlation id is returned for support.</summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, code, title) = ex switch
        {
            TenantUnavailableException => (StatusCodes.Status503ServiceUnavailable, "tenant.unavailable", "The organisation is temporarily unavailable."),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "concurrency.conflict", "The record was changed by someone else. Reload and retry."),
            BadHttpRequestException b => (b.StatusCode, "request.invalid", "Invalid request."),
            OperationCanceledException when ctx.RequestAborted.IsCancellationRequested => (499, "request.cancelled", "Request cancelled."),
            _ => (StatusCodes.Status500InternalServerError, "server.error", "An unexpected error occurred."),
        };
        if (status >= 500)
        {
            logger.LogError(ex, "Unhandled exception ({Code})", code);
        }

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Extensions = { ["code"] = code, ["correlationId"] = ctx.RequestServices.GetService<IRequestContext>()?.CorrelationId },
        }, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", ct);
        return true;
    }
}
