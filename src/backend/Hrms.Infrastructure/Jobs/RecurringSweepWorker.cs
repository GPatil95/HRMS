using Hrms.Application.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hrms.Infrastructure.Jobs;

/// <summary>
/// Periodically enqueues the platform lifecycle-activation sweep, which fans out to per-tenant
/// activation jobs (each resolving 'today' in the tenant's timezone). Runs through IJobScheduler,
/// so it works identically in InProcess and Hangfire modes. Interval: Jobs:SweepMinutes (default 60).
/// </summary>
public sealed class RecurringSweepWorker(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<RecurringSweepWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = Math.Clamp(int.TryParse(cfg["Jobs:SweepMinutes"], out var m) ? m : 60, 1, 1440);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                scope.ServiceProvider.GetRequiredService<IJobScheduler>().EnqueuePlatform(JobTypes.EmployeeLifecycleSweep);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Lifecycle sweep enqueue failed.");
            }
        }
    }
}
