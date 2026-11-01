using Hrms.Domain.Common;
using Hrms.Domain.Platform;
using Hrms.Domain.Tenant;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Abstractions;

public interface IPlatformDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<TenantDomain> TenantDomains { get; }
    DbSet<DbCluster> DbClusters { get; }
    DbSet<TenantDatabase> TenantDatabases { get; }
    DbSet<ProvisioningOperation> ProvisioningOperations { get; }
    DbSet<CatalogModule> Modules { get; }
    DbSet<ModuleDependency> ModuleDependencies { get; }
    DbSet<CatalogFeature> Features { get; }
    DbSet<SubscriptionPlan> Plans { get; }
    DbSet<PlanModule> PlanModules { get; }
    DbSet<PlanFeature> PlanFeatures { get; }
    DbSet<TenantSubscription> Subscriptions { get; }
    DbSet<TenantModuleEntitlement> ModuleEntitlements { get; }
    DbSet<TenantFeatureEntitlement> FeatureEntitlements { get; }
    DbSet<PlatformUser> Users { get; }
    DbSet<PlatformSession> Sessions { get; }
    DbSet<PlatformUserToken> UserTokens { get; }
    DbSet<PlatformRole> Roles { get; }
    DbSet<PlatformPermission> Permissions { get; }
    DbSet<PlatformRolePermission> RolePermissions { get; }
    DbSet<PlatformUserRole> UserRoles { get; }
    DbSet<SupportAccessGrant> SupportGrants { get; }
    DbSet<PlatformSetting> Settings { get; }
    DbSet<AuditEntry> AuditEntries { get; }
    DbSet<SecurityEvent> SecurityEvents { get; }
    DbSet<AuditSeal> AuditSeals { get; }
    DbSet<EmailMessage> EmailMessages { get; }
    DbSet<JobExecution> JobExecutions { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ITenantDbContext
{
    DbSet<TenantIdentityRecord> TenantIdentity { get; }
    DbSet<TenantUser> Users { get; }
    DbSet<TenantSession> Sessions { get; }
    DbSet<TenantUserToken> UserTokens { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<Role> Roles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<UserRoleAssignment> RoleAssignments { get; }
    DbSet<AssignmentScopeTarget> AssignmentScopeTargets { get; }
    DbSet<UserPermissionOverride> PermissionOverrides { get; }
    DbSet<AuthorizationState> AuthorizationState { get; }
    DbSet<Company> Companies { get; }
    DbSet<Branch> Branches { get; }
    DbSet<Department> Departments { get; }
    DbSet<CompanyAddress> CompanyAddresses { get; }
    DbSet<OrgRegistration> OrgRegistrations { get; }
    DbSet<CompanyContact> CompanyContacts { get; }
    DbSet<CompanyBankAccount> CompanyBankAccounts { get; }
    DbSet<DepartmentHodHistory> DepartmentHodHistory { get; }
    DbSet<Designation> Designations { get; }
    DbSet<Grade> Grades { get; }
    DbSet<WorkLocation> WorkLocations { get; }
    DbSet<CostCentre> CostCentres { get; }
    DbSet<Employee> Employees { get; }
    DbSet<ReportingClosure> ReportingClosure { get; }
    DbSet<EmployeeNumberingConfig> EmployeeNumberingConfigs { get; }
    DbSet<EmployeeCodeSeries> EmployeeCodeSeries { get; }
    DbSet<EmploymentHistory> EmploymentHistory { get; }
    DbSet<LifecycleEvent> LifecycleEvents { get; }
    DbSet<EmployeePersonal> EmployeePersonal { get; }
    DbSet<EmployeeContact> EmployeeContacts { get; }
    DbSet<EmployeeBankAccount> EmployeeBankAccounts { get; }
    DbSet<EmployeeStatutory> EmployeeStatutory { get; }
    DbSet<DocumentCategory> DocumentCategories { get; }
    DbSet<EmployeeDocument> EmployeeDocuments { get; }
    DbSet<LetterType> LetterTypes { get; }
    DbSet<LetterTemplate> LetterTemplates { get; }
    DbSet<EmployeeLetter> EmployeeLetters { get; }
    DbSet<FileObject> Files { get; }
    DbSet<FileAccessLog> FileAccessLogs { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<NotificationTemplate> NotificationTemplates { get; }
    DbSet<NotificationPreference> NotificationPreferences { get; }
    DbSet<Dashboard> Dashboards { get; }
    DbSet<DashboardWidget> DashboardWidgets { get; }
    DbSet<DashboardAssignment> DashboardAssignments { get; }
    DbSet<TenantSetting> Settings { get; }
    DbSet<AuditEntry> AuditEntries { get; }
    DbSet<SecurityEvent> SecurityEvents { get; }
    DbSet<AuditSeal> AuditSeals { get; }
    DbSet<EmailMessage> EmailMessages { get; }
    DbSet<JobExecution> JobExecutions { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    /// <summary>Raw relational access (atomic counters, bulk ops). Use sparingly.</summary>
    Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade Database { get; }
}

/// <summary>Creates tenant DbContexts for an explicit connection string (provisioning/migration tooling only).</summary>
public interface ITenantDbContextFactory
{
    ITenantDbContext CreateForConnection(string connectionString);
}
