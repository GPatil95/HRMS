using System.Text.RegularExpressions;
using Hrms.Application.Abstractions;
using Hrms.Application.Auditing;
using Hrms.Application.Authorization;
using Hrms.Application.Common;
using Hrms.Domain.Common;
using Hrms.Domain.Platform;
using Hrms.Shared.Primitives;
using Hrms.Shared.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hrms.Application.Tenancy;

public sealed record CreateTenantCommand(string Code, string Name, string AdminEmail, string AdminName, string? PlanCode);

/// <summary>Test hook to inject failures into provisioning steps (registered only by tests).</summary>
public interface IProvisioningFaultInjector
{
    void BeforeStep(Guid tenantId, string step, int attempt);
}

public static class ProvisioningSteps
{
    public const string AllocateCluster = "allocate_cluster";
    public const string StoreSecrets = "store_secrets";
    public const string CreateRoles = "create_roles";
    public const string CreateDatabase = "create_database";
    public const string ConfigureDatabase = "configure_database";
    public const string Migrate = "migrate";
    public const string Grants = "grants";
    public const string TenantIdentity = "tenant_identity";
    public const string Seed = "seed";
    public const string CreateAdmin = "create_admin";
    public const string Verify = "verify";
    public const string Activate = "activate";

    public static readonly string[] Ordered =
        [AllocateCluster, StoreSecrets, CreateRoles, CreateDatabase, ConfigureDatabase, Migrate, Grants, TenantIdentity, Seed, CreateAdmin, Verify, Activate];
}

/// <summary>Callback executed after activation (sends the tenant admin invitation inside the tenant context).</summary>
public interface ITenantActivationHandler
{
    Task OnActivatedAsync(Guid tenantId, CancellationToken ct);
}

public sealed partial class TenantProvisioningService(
    IPlatformDbContext db,
    IPlatformSequences sequences,
    ITenantDatabaseProvisioner provisioner,
    ITenantDbContextFactory tenantDbFactory,
    TenantSeedService seeder,
    ISecretStore secrets,
    IDistributedLockProvider locks,
    ISecurityEventWriter securityEvents,
    IEntitlementService entitlements,
    ITenantRegistry registry,
    ITenantActivationHandler activation,
    IRequestContext request,
    IClock clock,
    IOptions<HostingOptions> hosting,
    IOptions<ProvisioningOptions> options,
    ILogger<TenantProvisioningService> logger,
    IEnumerable<IProvisioningFaultInjector> faultInjectors)
{
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal) { "admin", "api", "auth", "www", "app", "platform", "mail", "static" };

    [GeneratedRegex("^[a-z][a-z0-9-]{1,30}[a-z0-9]$")]
    private static partial Regex CodePattern();

    public async Task<Result<(Guid TenantId, Guid OperationId)>> CreateTenantAsync(CreateTenantCommand cmd, CancellationToken ct)
    {
        var code = (cmd.Code ?? string.Empty).Trim().ToLowerInvariant();
        if (!CodePattern().IsMatch(code) || Reserved.Contains(code))
        {
            return Error.Validation("invalid-tenant-code", "Tenant code must be 3-32 lowercase letters, digits or hyphens and not reserved.");
        }

        if (await db.Tenants.AnyAsync(t => t.Code == code, ct))
        {
            return Error.Conflict("tenant-code-taken", "Tenant code is already in use.");
        }

        var planCode = cmd.PlanCode ?? options.Value.DefaultPlanCode;
        var plan = await db.Plans.Include(p => p.Modules).Include(p => p.Features)
            .FirstOrDefaultAsync(p => p.Code == planCode && p.Status == PlanStatus.Published, ct);
        if (plan is null)
        {
            return Error.Validation("plan-not-found", "Subscription plan not found or not published.");
        }

        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var tenant = new Tenant
        {
            TenantNumber = await sequences.NextTenantNumberAsync(ct),
            Code = code,
            Name = cmd.Name.Trim(),
            AdminEmail = Guard.NormalizeEmail(cmd.AdminEmail), AdminName = cmd.AdminName.Trim(),
            Status = TenantStatus.Provisioning,
        };
        tenant.Domains.Add(new TenantDomain { TenantId = tenant.Id, HostName = $"{code}.{hosting.Value.BaseDomain}", IsPrimary = true, CreatedAt = now });
        db.Tenants.Add(tenant);
        db.Subscriptions.Add(new TenantSubscription { TenantId = tenant.Id, PlanId = plan.Id, Status = SubscriptionStatus.Active, StartsOn = today, EmployeeLimit = plan.EmployeeLimit });
        foreach (var m in plan.Modules)
        {
            db.ModuleEntitlements.Add(new TenantModuleEntitlement { TenantId = tenant.Id, ModuleId = m.ModuleId, Source = EntitlementSource.Plan, ValidFrom = today });
        }

        foreach (var f in plan.Features)
        {
            db.FeatureEntitlements.Add(new TenantFeatureEntitlement { TenantId = tenant.Id, FeatureId = f.FeatureId, Value = f.Value, Source = EntitlementSource.Plan, ValidFrom = today });
        }

        var op = new ProvisioningOperation { TenantId = tenant.Id, Operation = "create", CorrelationId = request.CorrelationId };
        db.ProvisioningOperations.Add(op);
        securityEvents.Add(SecurityEventTypes.TenantChanged, true, null, null, new { action = "create", tenant = code, plan = planCode });
        await db.SaveChangesAsync(ct);
        return (tenant.Id, op.Id);
    }

    public async Task<Result<Guid>> PrepareRetryAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        var op = await db.ProvisioningOperations.Where(o => o.TenantId == tenantId).OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync(ct);
        if (tenant is null || op is null)
        {
            return Error.NotFound("tenant-not-found", "Tenant not found.");
        }

        if (op.Status is ProvisioningStatus.Succeeded || tenant.Status is not (TenantStatus.ProvisioningFailed or TenantStatus.Provisioning))
        {
            return Error.Conflict("provisioning-not-retryable", "Provisioning is not in a failed state.");
        }

        op.Status = ProvisioningStatus.Pending;
        op.Error = null;
        tenant.Status = TenantStatus.Provisioning;
        securityEvents.Add(SecurityEventTypes.TenantChanged, true, null, null, new { action = "provisioning_retry", tenant = tenant.Code });
        await db.SaveChangesAsync(ct);
        return op.Id;
    }

    /// <summary>Runs (or resumes) provisioning. Idempotent: completed steps are skipped, every step tolerates re-execution.</summary>
    public async Task<ProvisioningStatus> RunAsync(Guid operationId, CancellationToken ct)
    {
        var op = await db.ProvisioningOperations.FirstOrDefaultAsync(o => o.Id == operationId, ct)
                 ?? throw new InvalidOperationException($"Provisioning operation {operationId} not found.");
        await using var handle = await locks.TryAcquireAsync($"provisioning:{op.TenantId}", TimeSpan.FromMinutes(15), ct);
        if (handle is null)
        {
            logger.LogWarning("Provisioning for tenant {TenantId} already running elsewhere", op.TenantId);
            return op.Status;
        }

        if (op.Status == ProvisioningStatus.Succeeded)
        {
            return op.Status;
        }

        var tenant = await db.Tenants.FirstAsync(t => t.Id == op.TenantId, ct);
        op.Attempt++;
        op.Status = ProvisioningStatus.Running;
        op.StartedAt ??= clock.UtcNow;
        await db.SaveChangesAsync(ct);

        var adminName = string.IsNullOrWhiteSpace(tenant.AdminName) ? tenant.AdminEmail : tenant.AdminName;
        string? current = null;
        try
        {
            foreach (var step in ProvisioningSteps.Ordered)
            {
                if (op.IsStepCompleted(step))
                {
                    continue;
                }

                current = step;
                foreach (var f in faultInjectors)
                {
                    f.BeforeStep(tenant.Id, step, op.Attempt);
                }

                await ExecuteStepAsync(step, tenant, adminName, ct);
                op.MarkStep(step, "completed", clock.UtcNow);
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Provisioning {Tenant}: step {Step} completed", tenant.Code, step);
            }

            op.Status = ProvisioningStatus.Succeeded;
            op.CompletedAt = clock.UtcNow;
            op.CurrentStep = null;
            await db.SaveChangesAsync(ct);
            await registry.InvalidateAsync(tenant.Id, ct);
            await entitlements.InvalidateAsync(tenant.Id, ct);
            await activation.OnActivatedAsync(tenant.Id, ct);
            return op.Status;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Provisioning {Tenant} failed at step {Step}", tenant.Code, current);
            op.MarkStep(current ?? "unknown", "failed", clock.UtcNow, Truncate(ex.Message));
            op.Status = ProvisioningStatus.Failed;
            op.Error = Truncate($"{current}: {ex.Message}");
            if (tenant.Status != TenantStatus.Active)
            {
                tenant.Status = TenantStatus.ProvisioningFailed;
            }

            securityEvents.Add(SecurityEventTypes.TenantChanged, false, null, null, new { action = "provisioning_failed", tenant = tenant.Code, step = current });
            await db.SaveChangesAsync(CancellationToken.None);
            return op.Status;
        }
    }

    private async Task ExecuteStepAsync(string step, Tenant tenant, string adminName, CancellationToken ct)
    {
        var registryRow = await db.TenantDatabases.Include(d => d.Cluster).FirstOrDefaultAsync(d => d.TenantId == tenant.Id, ct);
        switch (step)
        {
            case ProvisioningSteps.AllocateCluster:
                if (registryRow is null)
                {
                    var cluster = await db.DbClusters.Where(c => c.IsActive).OrderBy(c => c.CreatedAt).FirstOrDefaultAsync(ct)
                                  ?? throw new InvalidOperationException("No active database cluster is registered.");
                    db.TenantDatabases.Add(new TenantDatabase
                    {
                        TenantId = tenant.Id,
                        ClusterId = cluster.Id,
                        DatabaseName = Tenant.DatabaseNameFor(tenant.TenantNumber),
                        AppRoleName = $"hrms_t{tenant.TenantNumber}_app",
                        MigratorRoleName = $"hrms_t{tenant.TenantNumber}_migrator",
                        AppSecretRef = secrets.BuildRef($"tenants/{tenant.TenantNumber}/app"),
                        MigratorSecretRef = secrets.BuildRef($"tenants/{tenant.TenantNumber}/migrator"),
                    });
                }

                break;
            case ProvisioningSteps.StoreSecrets:
                foreach (var secretRef in new[] { registryRow!.AppSecretRef, registryRow.MigratorSecretRef })
                {
                    if (await secrets.GetAsync(secretRef, ct) is null)
                    {
                        await secrets.SetAsync(secretRef, SecureTokens.CreatePassword(), ct);
                    }
                }

                break;
            case ProvisioningSteps.CreateRoles:
            {
                var admin = await AdminAsync(registryRow!.Cluster!, ct);
                await provisioner.EnsureRoleAsync(admin, registryRow.MigratorRoleName, await SecretAsync(registryRow.MigratorSecretRef, ct), ct);
                await provisioner.EnsureRoleAsync(admin, registryRow.AppRoleName, await SecretAsync(registryRow.AppSecretRef, ct), ct);
                break;
            }

            case ProvisioningSteps.CreateDatabase:
                await provisioner.EnsureDatabaseAsync(await AdminAsync(registryRow!.Cluster!, ct), registryRow.DatabaseName, registryRow.MigratorRoleName, ct);
                break;
            case ProvisioningSteps.ConfigureDatabase:
                await provisioner.ConfigureDatabaseAsync(await AdminAsync(registryRow!.Cluster!, ct), registryRow.DatabaseName, registryRow.MigratorRoleName, registryRow.AppRoleName, ct);
                break;
            case ProvisioningSteps.Migrate:
                await provisioner.MigrateAsync(await MigratorConnAsync(registryRow!, ct), ct);
                registryRow!.LastMigratedAt = clock.UtcNow;
                registryRow.SchemaVersion = (await provisioner.GetAppliedMigrationsAsync(await MigratorConnAsync(registryRow, ct), ct)).LastOrDefault();
                break;
            case ProvisioningSteps.Grants:
                await provisioner.ApplyGrantsAsync(await MigratorConnAsync(registryRow!, ct), registryRow!.AppRoleName, ct);
                break;
            case ProvisioningSteps.TenantIdentity:
                await provisioner.WriteTenantIdentityAsync(await MigratorConnAsync(registryRow!, ct), tenant.Id, tenant.TenantNumber, ct);
                break;
            case ProvisioningSteps.Seed:
                await seeder.SeedAsync(tenantDbFactory.CreateForConnection(await AppConnAsync(registryRow!, ct)), ct);
                break;
            case ProvisioningSteps.CreateAdmin:
                await TenantSeedService.EnsureTenantAdminAsync(tenantDbFactory.CreateForConnection(await AppConnAsync(registryRow!, ct)), tenant.AdminEmail, adminName, ct);
                break;
            case ProvisioningSteps.Verify:
                if (!await provisioner.VerifyAsync(await AppConnAsync(registryRow!, ct), tenant.Id, ct))
                {
                    throw new InvalidOperationException("Tenant database verification failed.");
                }

                registryRow!.Status = TenantDatabaseStatus.Online;
                registryRow.ProvisionedAt = clock.UtcNow;
                break;
            case ProvisioningSteps.Activate:
                tenant.Status = TenantStatus.Active;
                securityEvents.Add(SecurityEventTypes.TenantChanged, true, null, null, new { action = "activated", tenant = tenant.Code });
                break;
        }
    }

    private async Task<ClusterConnection> AdminAsync(DbCluster c, CancellationToken ct) =>
        new(c.Host, c.Port, c.AdminUsername, await SecretAsync(c.AdminSecretRef, ct));

    private async Task<string> SecretAsync(string secretRef, CancellationToken ct) =>
        await secrets.GetAsync(secretRef, ct) ?? throw new InvalidOperationException("Required secret is missing.");

    private async Task<string> MigratorConnAsync(TenantDatabase r, CancellationToken ct) =>
        provisioner.BuildConnectionString(r.Cluster!, r.DatabaseName, r.MigratorRoleName, await SecretAsync(r.MigratorSecretRef, ct));

    private async Task<string> AppConnAsync(TenantDatabase r, CancellationToken ct) =>
        provisioner.BuildConnectionString(r.Cluster!, r.DatabaseName, r.AppRoleName, await SecretAsync(r.AppSecretRef, ct));

    private static string Truncate(string s) => s.Length > 1000 ? s[..1000] : s;
}
