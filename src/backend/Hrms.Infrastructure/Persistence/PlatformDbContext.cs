using Hrms.Application.Abstractions;
using Hrms.Domain.Common;
using Hrms.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Infrastructure.Persistence;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options, AuditRuntime runtime) : DbContext(options), IPlatformDbContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantDomain> TenantDomains => Set<TenantDomain>();
    public DbSet<DbCluster> DbClusters => Set<DbCluster>();
    public DbSet<TenantDatabase> TenantDatabases => Set<TenantDatabase>();
    public DbSet<ProvisioningOperation> ProvisioningOperations => Set<ProvisioningOperation>();
    public DbSet<CatalogModule> Modules => Set<CatalogModule>();
    public DbSet<ModuleDependency> ModuleDependencies => Set<ModuleDependency>();
    public DbSet<CatalogFeature> Features => Set<CatalogFeature>();
    public DbSet<SubscriptionPlan> Plans => Set<SubscriptionPlan>();
    public DbSet<PlanModule> PlanModules => Set<PlanModule>();
    public DbSet<PlanFeature> PlanFeatures => Set<PlanFeature>();
    public DbSet<TenantSubscription> Subscriptions => Set<TenantSubscription>();
    public DbSet<TenantModuleEntitlement> ModuleEntitlements => Set<TenantModuleEntitlement>();
    public DbSet<TenantFeatureEntitlement> FeatureEntitlements => Set<TenantFeatureEntitlement>();
    public DbSet<PlatformUser> Users => Set<PlatformUser>();
    public DbSet<PlatformSession> Sessions => Set<PlatformSession>();
    public DbSet<PlatformUserToken> UserTokens => Set<PlatformUserToken>();
    public DbSet<PlatformRole> Roles => Set<PlatformRole>();
    public DbSet<PlatformPermission> Permissions => Set<PlatformPermission>();
    public DbSet<PlatformRolePermission> RolePermissions => Set<PlatformRolePermission>();
    public DbSet<PlatformUserRole> UserRoles => Set<PlatformUserRole>();
    public DbSet<SupportAccessGrant> SupportGrants => Set<SupportAccessGrant>();
    public DbSet<PlatformSetting> Settings => Set<PlatformSetting>();
    public DbSet<SecretEntry> Secrets => Set<SecretEntry>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();
    public DbSet<AuditSeal> AuditSeals => Set<AuditSeal>();
    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();
    public DbSet<JobExecution> JobExecutions => Set<JobExecution>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        AuditingSaveChanges.Apply(this, runtime);
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        AuditingSaveChanges.Apply(this, runtime);
        return base.SaveChanges();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasSequence<int>("tenant_number_seq", "platform").StartsAt(1001);

        b.Entity<Tenant>(e =>
        {
            e.ToTable("tenants", "platform", t =>
            {
                t.HasCheckConstraint("ck_tenants_status", "status BETWEEN 0 AND 4");
                t.HasCheckConstraint("ck_tenants_code", "code ~ '^[a-z][a-z0-9-]{1,30}[a-z0-9]$'");
                t.HasCheckConstraint("ck_tenants_number", "tenant_number >= 1000");
            });
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.TenantNumber).IsUnique();
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.AdminEmail).HasMaxLength(256);
            e.Property(x => x.Region).HasMaxLength(32);
            e.Property(x => x.TimeZone).HasMaxLength(64);
            e.HasMany(x => x.Domains).WithOne().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TenantDomain>(e =>
        {
            e.ToTable("tenant_domains", "platform");
            e.Property(x => x.HostName).HasMaxLength(253);
            e.HasIndex(x => x.HostName).IsUnique();
        });
        b.Entity<DbCluster>(e =>
        {
            e.ToTable("db_clusters", "platform", t => t.HasCheckConstraint("ck_db_clusters_port", "port BETWEEN 1 AND 65535"));
            e.HasIndex(x => x.Name).IsUnique();
        });
        b.Entity<TenantDatabase>(e =>
        {
            e.ToTable("tenant_databases", "platform", t => t.HasCheckConstraint("ck_tenant_databases_status", "status BETWEEN 0 AND 3"));
            e.HasIndex(x => x.TenantId).IsUnique();
            e.HasIndex(x => new { x.ClusterId, x.DatabaseName }).IsUnique();
            e.Property(x => x.DatabaseName).HasMaxLength(63);
            e.Property(x => x.AppRoleName).HasMaxLength(63);
            e.Property(x => x.MigratorRoleName).HasMaxLength(63);
            e.HasOne(x => x.Cluster).WithMany().HasForeignKey(x => x.ClusterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ProvisioningOperation>(e =>
        {
            e.ToTable("provisioning_operations", "platform", t => t.HasCheckConstraint("ck_provisioning_status", "status BETWEEN 0 AND 3"));
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
            e.Property(x => x.Error).HasMaxLength(1000);
            e.OwnsMany(x => x.Steps, s => s.ToJson("steps"));
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<CatalogModule>(e =>
        {
            e.ToTable("modules", "catalog");
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.ParentCode).HasMaxLength(64);
        });
        b.Entity<ModuleDependency>(e =>
        {
            e.ToTable("module_dependencies", "catalog", t => t.HasCheckConstraint("ck_module_dependencies_self", "module_id <> depends_on_module_id"));
            e.HasKey(x => new { x.ModuleId, x.DependsOnModuleId });
            e.HasOne<CatalogModule>().WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CatalogModule>().WithMany().HasForeignKey(x => x.DependsOnModuleId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CatalogFeature>(e =>
        {
            e.ToTable("features", "catalog");
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(128);
            e.HasOne<CatalogModule>().WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SubscriptionPlan>(e =>
        {
            e.ToTable("plans", "catalog", t => t.HasCheckConstraint("ck_plans_employee_limit", "employee_limit IS NULL OR employee_limit > 0"));
            e.HasIndex(x => new { x.Code, x.Version }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(64);
            e.HasMany(x => x.Modules).WithOne().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Features).WithOne().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PlanModule>(e =>
        {
            e.ToTable("plan_modules", "catalog");
            e.HasKey(x => new { x.PlanId, x.ModuleId });
            e.HasOne<CatalogModule>().WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PlanFeature>(e =>
        {
            e.ToTable("plan_features", "catalog");
            e.HasKey(x => new { x.PlanId, x.FeatureId });
            e.HasOne<CatalogFeature>().WithMany().HasForeignKey(x => x.FeatureId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TenantSubscription>(e =>
        {
            e.ToTable("tenant_subscriptions", "platform", t => t.HasCheckConstraint("ck_tenant_subscriptions_period", "ends_on IS NULL OR ends_on >= starts_on"));
            e.HasIndex(x => new { x.TenantId, x.StartsOn });
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TenantModuleEntitlement>(e =>
        {
            e.ToTable("tenant_module_entitlements", "platform", t => t.HasCheckConstraint("ck_tme_period", "valid_to IS NULL OR valid_to >= valid_from"));
            e.HasIndex(x => new { x.TenantId, x.ModuleId }).IsUnique();
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CatalogModule>().WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TenantFeatureEntitlement>(e =>
        {
            e.ToTable("tenant_feature_entitlements", "platform", t => t.HasCheckConstraint("ck_tfe_period", "valid_to IS NULL OR valid_to >= valid_from"));
            e.HasIndex(x => new { x.TenantId, x.FeatureId }).IsUnique();
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CatalogFeature>().WithMany().HasForeignKey(x => x.FeatureId).OnDelete(DeleteBehavior.Restrict);
        });

        CommonModel.ConfigureIdentityUser<PlatformUser>(b, "identity", "platform_users");
        CommonModel.ConfigureSession<PlatformSession>(b, "identity", "platform_sessions");
        CommonModel.ConfigureToken<PlatformUserToken>(b, "identity", "platform_user_tokens");
        b.Entity<PlatformSession>().HasOne<PlatformUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PlatformUserToken>().HasOne<PlatformUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PlatformRole>(e =>
        {
            e.ToTable("platform_roles", "identity");
            e.HasIndex(x => x.Code).IsUnique();
            e.HasMany(x => x.Permissions).WithOne().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PlatformPermission>(e =>
        {
            e.ToTable("platform_permissions", "identity");
            e.HasIndex(x => x.Code).IsUnique();
        });
        b.Entity<PlatformRolePermission>(e =>
        {
            e.ToTable("platform_role_permissions", "identity");
            e.HasKey(x => new { x.RoleId, x.PermissionId });
            e.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PlatformUserRole>(e =>
        {
            e.ToTable("platform_user_roles", "identity");
            e.HasKey(x => new { x.UserId, x.RoleId });
            e.HasOne<PlatformUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<PlatformRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SupportAccessGrant>(e =>
        {
            e.ToTable("support_access_grants", "platform", t =>
            {
                t.HasCheckConstraint("ck_support_grants_duration", "duration_minutes BETWEEN 5 AND 240");
                t.HasCheckConstraint("ck_support_grants_reason", "length(reason) > 0");
            });
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.Property(x => x.TicketRef).HasMaxLength(64);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PlatformUser>().WithMany().HasForeignKey(x => x.PlatformUserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PlatformSetting>(e =>
        {
            e.ToTable("settings", "platform");
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(128);
            e.Property(x => x.Value).HasColumnType("jsonb");
        });
        b.Entity<SecretEntry>(e =>
        {
            e.ToTable("secrets", "ops");
            e.HasKey(x => x.Ref);
            e.Property(x => x.Ref).HasMaxLength(256);
        });

        CommonModel.ConfigureShared(b);
        CommonModel.ApplyConventions(b);
    }
}
