namespace Hrms.Application.Jobs;

/// <summary>Background job scheduling abstraction (Hangfire implementation in Infrastructure). Tenant jobs carry the tenant id
/// in the job arguments, and the runner re-establishes a validated tenant context before executing.</summary>
public interface IJobScheduler
{
    string EnqueueTenant(Guid tenantId, string jobType, string? payload = null);
    string EnqueuePlatform(string jobType, string? payload = null);
}

public interface IJobHandler
{
    string JobType { get; }
    bool TenantScoped { get; }
    Task ExecuteAsync(string? payload, CancellationToken ct);
}

public static class JobTypes
{
    public const string TenantEmailDispatch = "email.dispatch.tenant";
    public const string PlatformEmailDispatch = "email.dispatch.platform";
    public const string TenantAuditSeal = "audit.seal.tenant";
    public const string PlatformAuditSeal = "audit.seal.platform";
    public const string TenantProvisioning = "tenant.provision";
}
