using Hrms.Application.Abstractions;
using Hrms.Application.Jobs;
using Hrms.Domain.Platform;
using Hrms.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hrms.Application.Workflow;

/// <summary>Delivers pending workflow outbox events to module <see cref="IWorkflowEventHandler"/>s (at-least-once).</summary>
[JobHandler(WorkflowJobTypes.OutboxDispatch, true)]
public sealed class WorkflowOutboxDispatchJob(ITenantDbContext db, IEnumerable<IWorkflowEventHandler> handlers, IClock clock, ILogger<WorkflowOutboxDispatchJob> logger) : IJobHandler
{
    public string JobType => WorkflowJobTypes.OutboxDispatch;
    public bool TenantScoped => true;

    public async Task ExecuteAsync(string? payload, CancellationToken ct)
    {
        var pending = await db.WorkflowOutboxEvents.Where(e => e.ProcessedAt == null && e.Attempts < 10)
            .OrderBy(e => e.OccurredAt).Take(200).ToListAsync(ct);
        foreach (var e in pending)
        {
            var evt = new WorkflowEvent(e.Id, e.EventType, e.InstanceId, e.DocumentType, e.DocumentId, e.Status, e.OccurredAt);
            try
            {
                foreach (var h in handlers.Where(h => h.DocumentType == "*" || h.DocumentType == e.DocumentType))
                    await h.HandleAsync(evt, ct);
                e.ProcessedAt = clock.UtcNow;
                e.LastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                e.Attempts++;
                e.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                logger.LogWarning(ex, "Workflow outbox event {EventId} failed", e.Id);
            }
            await db.SaveChangesAsync(ct);
        }
    }
}

/// <summary>Per-tenant SLA pass: reminders and auto-escalation for overdue tasks.</summary>
[JobHandler(WorkflowJobTypes.SlaTenant, true)]
public sealed class WorkflowSlaJob(IWorkflowEngine engine, ILogger<WorkflowSlaJob> logger) : IJobHandler
{
    public string JobType => WorkflowJobTypes.SlaTenant;
    public bool TenantScoped => true;

    public async Task ExecuteAsync(string? payload, CancellationToken ct)
    {
        var n = await engine.ProcessOverdueAsync(ct);
        if (n > 0) logger.LogInformation("Workflow SLA processed {Count} overdue tasks", n);
    }
}

/// <summary>Platform sweep fanning the SLA pass out to every active tenant (scheduled by the recurring sweep worker).</summary>
[JobHandler(WorkflowJobTypes.SlaSweep, false)]
public sealed class WorkflowSlaSweepJob(IPlatformDbContext platform, IJobScheduler jobs) : IJobHandler
{
    public string JobType => WorkflowJobTypes.SlaSweep;
    public bool TenantScoped => false;

    public async Task ExecuteAsync(string? payload, CancellationToken ct)
    {
        var tenants = await platform.Tenants.AsNoTracking().Where(t => t.Status == TenantStatus.Active).Select(t => t.Id).ToListAsync(ct);
        foreach (var t in tenants) jobs.EnqueueTenant(t, WorkflowJobTypes.SlaTenant);
    }
}
