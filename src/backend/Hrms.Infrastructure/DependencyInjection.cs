using Amazon.S3;
using Hangfire;
using Hrms.Application.Abstractions;
using Hrms.Application.Auditing;
using Hrms.Application.Authorization;
using Hrms.Application.Dashboards;
using Hrms.Application.Files;
using Hrms.Application.Identity;
using Hrms.Application.Jobs;
using Hrms.Application.Notifications;
using Hrms.Application.Organization;
using Hrms.Application.Platform;
using Hrms.Application.Reporting;
using Hrms.Application.Tenancy;
using Hrms.Application.Modules;
using Microsoft.AspNetCore.DataProtection;
using Hrms.Infrastructure.Caching;
using Hrms.Infrastructure.Jobs;
using Hrms.Infrastructure.Messaging;
using Hrms.Infrastructure.Persistence;
using Hrms.Infrastructure.Security;
using Hrms.Infrastructure.Storage;
using Hrms.Infrastructure.Tenancy;
using Hrms.Shared.Primitives;
using Hrms.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Hrms.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddHrmsInfrastructure(this IServiceCollection s, IConfiguration cfg)
    {
        s.Configure<HostingOptions>(cfg.GetSection(HostingOptions.Section));
        s.Configure<Application.Abstractions.IdentityOptions>(cfg.GetSection(Application.Abstractions.IdentityOptions.Section));
        s.Configure<ProvisioningOptions>(cfg.GetSection(ProvisioningOptions.Section));
        s.Configure<FileStorageOptions>(cfg.GetSection(FileStorageOptions.Section));
        s.Configure<SecretStoreOptions>(cfg.GetSection(SecretStoreOptions.Section));
        s.Configure<SmtpOptions>(cfg.GetSection(SmtpOptions.Section));

        s.TryAddSingleton<IClock, SystemClock>();
        s.AddScoped<ExecutionContextState>();
        s.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<ExecutionContextState>());
        s.AddScoped<IRequestContext>(sp => sp.GetRequiredService<ExecutionContextState>());
        s.AddScoped<IAuditContext>(sp => sp.GetRequiredService<ExecutionContextState>());
        s.AddScoped<TenantContextAccessor>();
        s.AddScoped<ITenantContextAccessor>(sp => sp.GetRequiredService<TenantContextAccessor>());
        s.AddScoped(sp => new AuditRuntime(
            sp.GetRequiredService<ICurrentUser>(), sp.GetRequiredService<IRequestContext>(), sp.GetRequiredService<IAuditContext>(),
            sp.GetRequiredService<ITenantContextAccessor>(), sp.GetRequiredService<IClock>()));

        var platformCs = cfg.GetConnectionString("Platform") ?? throw new InvalidOperationException("ConnectionStrings:Platform is required.");
        s.AddDbContext<PlatformDbContext>(o => DbOptions.Configure(o, platformCs));
        s.AddScoped<IPlatformDbContext>(sp => sp.GetRequiredService<PlatformDbContext>());

        s.AddScoped<TenantConnectionHolder>();
        s.AddScoped(sp =>
        {
            var holder = sp.GetRequiredService<TenantConnectionHolder>();
            var cs = holder.ConnectionString ?? throw new InvalidOperationException("Tenant database has not been resolved for this scope.");
            var b = new DbContextOptionsBuilder<TenantDbContext>();
            DbOptions.Configure(b, cs);
            return new TenantDbContext(b.Options, sp.GetRequiredService<AuditRuntime>());
        });
        s.AddScoped<ITenantDbContext>(sp => sp.GetRequiredService<TenantDbContext>());
        s.AddScoped<ITenantDbContextFactory, TenantDbContextConnectionFactory>();

        s.AddMemoryCache();
        s.AddHybridCache();
        s.AddScoped<ISecretStore, DbSecretStore>();
        s.AddSingleton<ITenantDatabaseProvisioner, PostgresTenantDatabaseProvisioner>();
        s.AddScoped<ITenantDatabaseResolver, TenantDatabaseResolver>();
        s.AddSingleton<ITenantRegistry, TenantRegistry>();
        s.AddScoped<IPlatformSequences, PlatformSequences>();

        var redisCs = cfg.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisCs))
        {
            s.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisCs));
            s.AddSingleton<IDistributedLockProvider, RedisDistributedLockProvider>();
            s.AddSingleton<IRateLimiter, RedisRateLimiter>();
            s.AddSingleton<ISessionCache, RedisSessionCache>();
            s.AddSingleton<ICacheInvalidationBus, RedisCacheInvalidationBus>();
        }
        else
        {
            s.AddSingleton<IDistributedLockProvider, InMemoryDistributedLockProvider>();
            s.AddSingleton<IRateLimiter, InMemoryRateLimiter>();
            s.AddSingleton<ISessionCache, InMemorySessionCache>();
            s.AddSingleton<ICacheInvalidationBus, InMemoryCacheInvalidationBus>();
        }

        var storage = cfg.GetSection(FileStorageOptions.Section).Get<FileStorageOptions>() ?? new FileStorageOptions();
        if (string.Equals(storage.Provider, "S3", StringComparison.OrdinalIgnoreCase))
        {
            s.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(new AmazonS3Config
            {
                ServiceURL = storage.S3ServiceUrl,
                ForcePathStyle = storage.S3ForcePathStyle,
                AuthenticationRegion = storage.S3Region,
            }));
            s.AddSingleton<IFileStorageService, S3FileStorageService>();
        }
        else
        {
            s.AddSingleton<IFileStorageService, LocalFileStorageService>();
        }

        s.AddSingleton<IMalwareScanner, SignatureMalwareScanner>();
        s.AddSingleton<IEmailTransport, SmtpEmailTransport>();
        s.AddSingleton<ITemplateRenderer, ScribanTemplateRenderer>();
        s.AddSingleton<IPushNotificationSender, NoopPushNotificationSender>();
        s.TryAddSingleton<IRealtimeNotifier, NoopRealtimeNotifier>();
        s.AddSingleton<ITotpService, TotpService>();
        s.AddDataProtection().SetApplicationName("hrms");
        s.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        s.AddSingleton<IExcelWriter, ClosedXmlExcelWriter>();

        if (string.Equals(cfg["Jobs:Mode"], "InProcess", StringComparison.OrdinalIgnoreCase))
        {
            s.AddSingleton<InProcessJobQueue>();
            s.AddScoped<IJobScheduler, InProcessJobScheduler>();
            s.AddHostedService<InProcessJobWorker>();
        }
        else
        {
            s.AddScoped<IJobScheduler, HangfireJobScheduler>();
        }

        s.AddScoped<JobRunner>();
        s.TryAddSingleton<IProvisioningFaultInjector, NoopProvisioningFaultInjector>();
        s.TryAddScoped<ITenantActivationHandler, NoopTenantActivationHandler>();

        return s.AddHrmsApplication();
    }

    private static IServiceCollection AddHrmsApplication(this IServiceCollection s)
    {
        var asm = typeof(ModuleCatalog).Assembly;
        foreach (var t in asm.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
        {
            if (typeof(IModuleDefinition).IsAssignableFrom(t))
            {
                s.AddSingleton(typeof(IModuleDefinition), t);
            }

            if (typeof(IJobHandler).IsAssignableFrom(t))
            {
                s.AddScoped(typeof(IJobHandler), t);
            }

            if (typeof(IReportDefinition).IsAssignableFrom(t))
            {
                s.AddScoped(typeof(IReportDefinition), t);
            }

            if (typeof(IWidgetDataProvider).IsAssignableFrom(t))
            {
                s.AddScoped(typeof(IWidgetDataProvider), t);
            }
        }

        s.AddSingleton<IModuleCatalog, ModuleCatalog>();
        s.AddSingleton<IPasswordPolicy, PasswordPolicy>();
        s.AddScoped<IPermissionService, PermissionService>();
        s.AddScoped<IEntitlementService, EntitlementService>();
        s.AddScoped<IDataScopeService, DataScopeService>();
        s.AddScoped<IGrantCeilingGuard, GrantCeilingGuard>();
        s.AddScoped<INavigationService, NavigationService>();
        s.AddScoped<IAccessGuard, AccessGuard>();
        s.AddScoped<RealmAuditWriter>();
        s.AddScoped<IAuditWriter>(sp => sp.GetRequiredService<RealmAuditWriter>());
        s.AddScoped<ISecurityEventWriter>(sp => sp.GetRequiredService<RealmAuditWriter>());

        s.AddScoped<TenantEmailOutbox>();
        s.AddScoped<PlatformEmailOutbox>();
        s.AddScoped<IEmailOutbox>(sp => sp.GetRequiredService<ITenantContextAccessor>().Current is not null
            ? sp.GetRequiredService<TenantEmailOutbox>()
            : sp.GetRequiredService<PlatformEmailOutbox>());
        s.AddScoped<TenantIdentityRealmStore>();
        s.AddScoped<PlatformIdentityRealmStore>();
        s.AddKeyedScoped(Realms.Tenant, (sp, _) => ActivatorUtilities.CreateInstance<AuthService>(sp,
            sp.GetRequiredService<TenantIdentityRealmStore>(),
            ActivatorUtilities.CreateInstance<TenantAuthEmailSender>(sp, sp.GetRequiredService<TenantEmailOutbox>())));
        s.AddKeyedScoped(Realms.Platform, (sp, _) => ActivatorUtilities.CreateInstance<AuthService>(sp,
            sp.GetRequiredService<PlatformIdentityRealmStore>(),
            ActivatorUtilities.CreateInstance<PlatformAuthEmailSender>(sp, sp.GetRequiredService<PlatformEmailOutbox>())));

        s.AddScoped<IPlatformPermissionService, PlatformPermissionService>();
        s.AddScoped<TenantManagementService>();
        s.AddScoped<SupportAccessService>();
        s.AddScoped<TenantProvisioningService>();
        s.AddScoped<PlatformSeedService>();
        s.AddScoped<TenantSeedService>();
        s.AddScoped<RoleService>();
        s.AddScoped<UserAdministrationService>();
        s.AddScoped<OrganizationService>();
        s.AddScoped<FileService>();
        s.AddScoped<NotificationService>();
        s.AddScoped<INotificationService>(sp => sp.GetRequiredService<NotificationService>());
        s.AddScoped<DashboardService>();
        s.AddScoped<ReportingService>();
        return s;
    }
}

public sealed class NoopRealtimeNotifier : IRealtimeNotifier
{
    public Task NotifyUserAsync(Guid tenantId, Guid userId, string eventName, object payload, CancellationToken ct) => Task.CompletedTask;
}

public sealed class NoopProvisioningFaultInjector : IProvisioningFaultInjector
{
    public void BeforeStep(Guid tenantId, string step, int attempt)
    {
    }
}

public sealed class NoopTenantActivationHandler : ITenantActivationHandler
{
    public Task OnActivatedAsync(Guid tenantId, CancellationToken ct) => Task.CompletedTask;
}
