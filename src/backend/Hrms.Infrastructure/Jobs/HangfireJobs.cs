using Hangfire;
using Hrms.Application.Abstractions;
using Hrms.Application.Jobs;
using Hrms.Domain.Platform;
using Hrms.Shared.Auditing;
using Microsoft.Extensions.DependencyInjection;

namespace Hrms.Infrastructure.Jobs;

/// <summary>Enqueues jobs carrying only the trusted tenant id; the payload never selects a database.</summary>
public sealed class HangfireJobScheduler(IBackgroundJobClient client, IRequestContext request) : IJobScheduler
{
    public string EnqueueTenant(Guid tenantId, string jobType, string? payload = null)
    {
        var correlationId = request.CorrelationId;
        return client.Enqueue<JobRunner>(r => r.RunTenantAsync(tenantId, jobType, payload, correlationId, CancellationToken.None));
    }

    public string EnqueuePlatform(string jobType, string? payload = null)
    {
        var correlationId = request.CorrelationId;
        return client.Enqueue<JobRunner>(r => r.RunPlatformAsync(jobType, payload, correlationId, CancellationToken.None));
    }
}

/// <summary>
/// Executes a job in a fresh DI scope. Tenant jobs re-establish the tenant context from the Platform registry (status must be
/// Active) and resolve/verify the tenant database before the handler runs.
/// </summary>
public sealed class JobRunner(IServiceScopeFactory scopes)
{
    [AutomaticRetry(Attempts = 3)]
    public async Task RunTenantAsync(Guid tenantId, string jobType, string? payload, string correlationId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var tenant = await sp.GetRequiredService<ITenantRegistry>().FindByIdAsync(tenantId, ct);
        if (tenant is null || tenant.Status != TenantStatus.Active)
        {
            return;
        }

        sp.GetRequiredService<ITenantContextAccessor>().Set(tenant);
        await sp.GetRequiredService<ITenantDatabaseResolver>().EnsureResolvedAsync(tenant, ct);
        sp.GetRequiredService<ExecutionContextState>().SetRequest(correlationId, null, null, AuditSources.BackgroundJob);
        await Handler(sp, jobType, tenantScoped: true).ExecuteAsync(payload, ct);
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task RunPlatformAsync(string jobType, string? payload, string correlationId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<ExecutionContextState>().SetRequest(correlationId, null, null, AuditSources.BackgroundJob);
        await Handler(sp, jobType, tenantScoped: false).ExecuteAsync(payload, ct);
    }

    private static IJobHandler Handler(IServiceProvider sp, string jobType, bool tenantScoped) =>
        sp.GetServices<IJobHandler>().SingleOrDefault(h => h.JobType == jobType && h.TenantScoped == tenantScoped)
        ?? throw new InvalidOperationException($"No handler for job type '{jobType}'.");
}
