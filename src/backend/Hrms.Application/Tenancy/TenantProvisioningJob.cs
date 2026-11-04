using Hrms.Application.Jobs;

namespace Hrms.Application.Tenancy;

/// <summary>Runs (or resumes) a provisioning operation. Payload is the operation id; steps are idempotent.</summary>
[JobHandler(JobTypes.TenantProvisioning, false)]
public sealed class TenantProvisioningJob(TenantProvisioningService provisioning) : IJobHandler
{
    public string JobType => JobTypes.TenantProvisioning;
    public bool TenantScoped => false;

    public Task ExecuteAsync(string? payload, CancellationToken ct) =>
        provisioning.RunAsync(Guid.Parse(payload ?? throw new ArgumentNullException(nameof(payload))), ct);
}
