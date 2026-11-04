using System.Threading.Channels;
using Hrms.Application.Abstractions;
using Hrms.Application.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hrms.Infrastructure.Jobs;

/// <summary>In-process queue (development/tests, Jobs:Mode=InProcess). Same JobRunner semantics as Hangfire, no persistence.</summary>
public sealed class InProcessJobQueue
{
    private readonly Channel<Func<JobRunner, Task>> _channel = Channel.CreateUnbounded<Func<JobRunner, Task>>();
    private int _pending;

    public int Pending => Volatile.Read(ref _pending);

    public void Enqueue(Func<JobRunner, Task> work)
    {
        Interlocked.Increment(ref _pending);
        _channel.Writer.TryWrite(work);
    }

    internal IAsyncEnumerable<Func<JobRunner, Task>> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);

    internal void Complete() => Interlocked.Decrement(ref _pending);

    public async Task DrainAsync(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (Pending > 0 && DateTime.UtcNow < until)
        {
            await Task.Delay(50);
        }
    }
}

public sealed class InProcessJobScheduler(InProcessJobQueue queue, IRequestContext request) : IJobScheduler
{
    public string EnqueueTenant(Guid tenantId, string jobType, string? payload = null)
    {
        var cid = request.CorrelationId;
        queue.Enqueue(r => r.RunTenantAsync(tenantId, jobType, payload, cid, CancellationToken.None));
        return Guid.NewGuid().ToString("N");
    }

    public string EnqueuePlatform(string jobType, string? payload = null)
    {
        var cid = request.CorrelationId;
        queue.Enqueue(r => r.RunPlatformAsync(jobType, payload, cid, CancellationToken.None));
        return Guid.NewGuid().ToString("N");
    }
}

public sealed class InProcessJobWorker(InProcessJobQueue queue, IServiceScopeFactory scopes, ILogger<InProcessJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await work(scope.ServiceProvider.GetRequiredService<JobRunner>());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "In-process job failed");
            }
            finally
            {
                queue.Complete();
            }
        }
    }
}
