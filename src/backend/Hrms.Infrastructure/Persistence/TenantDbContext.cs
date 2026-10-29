using Hrms.Application.Abstractions;
using Hrms.Domain.Common;
using Hrms.Domain.Tenant;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Infrastructure.Persistence;

/// <summary>One physical database per tenant; this context never contains a tenant_id discriminator.</summary>
public sealed class TenantDbContext(DbContextOptions<TenantDbContext> options, AuditRuntime runtime) : DbContext(options), ITenantDbContext
{
    public DbSet<TenantIdentityRecord> TenantIdentity => Set<TenantIdentityRecord>();
    public DbSet<TenantUser> Users => Set<TenantUser>();
    public DbSet<TenantSession> Sessions => Set<TenantSession>();
    public DbSet<TenantUserToken> UserTokens => Set<TenantUserToken>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRoleAssignment> RoleAssignments => Set<UserRoleAssignment>();
    public DbSet<AssignmentScopeTarget> AssignmentScopeTargets => Set<AssignmentScopeTarget>();
    public DbSet<UserPermissionOverride> PermissionOverrides => Set<UserPermissionOverride>();
    public DbSet<AuthorizationState> AuthorizationState => Set<AuthorizationState>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<CompanyAddress> CompanyAddresses => Set<CompanyAddress>();
    public DbSet<OrgRegistration> OrgRegistrations => Set<OrgRegistration>();
    public DbSet<CompanyContact> CompanyContacts => Set<CompanyContact>();
    public DbSet<CompanyBankAccount> CompanyBankAccounts => Set<CompanyBankAccount>();
    public DbSet<DepartmentHodHistory> DepartmentHodHistory => Set<DepartmentHodHistory>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<ReportingClosure> ReportingClosure => Set<ReportingClosure>();
    public DbSet<FileObject> Files => Set<FileObject>();
    public DbSet<FileAccessLog> FileAccessLogs => Set<FileAccessLog>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<Dashboard> Dashboards => Set<Dashboard>();
    public DbSet<DashboardWidget> DashboardWidgets => Set<DashboardWidget>();
    public DbSet<DashboardAssignment> DashboardAssignments => Set<DashboardAssignment>();
    public DbSet<TenantSetting> Settings => Set<TenantSetting>();
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
        b.Entity<TenantIdentityRecord>(e =>
        {
            e.ToTable("tenant_identity", "ops");
            e.HasKey(x => x.TenantId);
        });

        CommonModel.ConfigureIdentityUser<TenantUser>(b, "identity", "users");
        CommonModel.ConfigureSession<TenantSession>(b, "identity", "sessions");
        CommonModel.ConfigureToken<TenantUserToken>(b, "identity", "user_tokens");
        b.Entity<TenantUser>(e =>
        {
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.EmployeeId).IsUnique().HasFilter("employee_id IS NOT NULL");
            e.HasMany(x => x.RoleAssignments).WithOne().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TenantSession>().HasOne<TenantUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<TenantUserToken>().HasOne<TenantUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Permission>(e =>
        {
            e.ToTable("permissions", "identity");
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(128);
            e.Property(x => x.ModuleCode).HasMaxLength(64);
            e.Property(x => x.Action).HasMaxLength(32);
        });
        b.Entity<Role>(e =>
        {
            e.ToTable("roles", "identity");
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(64);
            e.HasMany(x => x.Permissions).WithOne().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<RolePermission>(e =>
        {
            e.ToTable("role_permissions", "identity", t => t.HasCheckConstraint("ck_role_permissions_scope", "scope_level BETWEEN 1 AND 8"));
            e.HasKey(x => new { x.RoleId, x.PermissionId });
            e.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<UserRoleAssignment>(e =>
        {
            e.ToTable("user_role_assignments", "identity", t => t.HasCheckConstraint("ck_ura_period", "valid_to IS NULL OR valid_to >= valid_from"));
            e.HasIndex(x => new { x.UserId, x.RoleId }).IsUnique();
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.ScopeTargets).WithOne().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AssignmentScopeTarget>(e =>
        {
            e.ToTable("assignment_scope_targets", "identity", t => t.HasCheckConstraint("ck_ast_kind", "kind BETWEEN 1 AND 4"));
            e.HasIndex(x => new { x.AssignmentId, x.Kind, x.TargetId }).IsUnique();
        });
        b.Entity<UserPermissionOverride>(e =>
        {
            e.ToTable("user_permission_overrides", "identity", t =>
            {
                t.HasCheckConstraint("ck_upo_period", "valid_to IS NULL OR valid_to >= valid_from");
                t.HasCheckConstraint("ck_upo_reason", "length(reason) > 0");
            });
            e.HasIndex(x => new { x.UserId, x.PermissionId });
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<TenantUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AuthorizationState>(e =>
        {
            e.ToTable("authorization_state", "identity", t => t.HasCheckConstraint("ck_authorization_state_singleton", "id = 1"));
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Version).IsConcurrencyToken();
        });

        b.Entity<Company>(e =>
        {
            e.ToTable("companies", "org");
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.LegalName).HasMaxLength(300);
            e.Property(x => x.CompanyType).HasMaxLength(50);
            e.Property(x => x.Industry).HasMaxLength(150);
            e.Property(x => x.Website).HasMaxLength(300);
            e.Property(x => x.OfficialEmail).HasMaxLength(320);
            e.Property(x => x.ContactNumber).HasMaxLength(40);
            e.Property(x => x.Cin).HasMaxLength(25);
            e.Property(x => x.Pan).HasMaxLength(15);
            e.Property(x => x.Tan).HasMaxLength(15);
            e.Property(x => x.PfEstablishmentCode).HasMaxLength(40);
            e.Property(x => x.EsicEmployerCode).HasMaxLength(40);
            e.Property(x => x.ProfessionalTaxRegNo).HasMaxLength(40);
            e.Property(x => x.ShopsEstablishmentRegNo).HasMaxLength(60);
            e.Property(x => x.FactoryLicenceNo).HasMaxLength(60);
            e.Property(x => x.MsmeUdyamRegNo).HasMaxLength(30);
            e.Property(x => x.Currency).HasMaxLength(8);
            e.Property(x => x.PayrollFrequency).HasMaxLength(30);
            e.Property(x => x.DefaultLeavePolicyCode).HasMaxLength(64);
            e.Property(x => x.DefaultHolidayCalendarCode).HasMaxLength(64);
            e.Property(x => x.DefaultAttendancePolicyCode).HasMaxLength(64);
            e.Property(x => x.DefaultShiftName).HasMaxLength(64);
            e.Property(x => x.WeeklyOffPattern).HasMaxLength(30);
            e.Property(x => x.WeeklyOffConfigJson).HasColumnType("jsonb");
            e.HasOne<Company>().WithMany().HasForeignKey(x => x.ParentCompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FileObject>().WithMany().HasForeignKey(x => x.LogoFileId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<CompanyAddress>(e =>
        {
            e.ToTable("company_addresses", "org");
            e.HasIndex(x => x.CompanyId);
            e.Property(x => x.AddressType).HasMaxLength(30);
            e.Property(x => x.Line1).HasMaxLength(200);
            e.Property(x => x.Line2).HasMaxLength(200);
            e.Property(x => x.Landmark).HasMaxLength(200);
            e.Property(x => x.Country).HasMaxLength(80);
            e.Property(x => x.State).HasMaxLength(80);
            e.Property(x => x.District).HasMaxLength(80);
            e.Property(x => x.City).HasMaxLength(80);
            e.Property(x => x.PinCode).HasMaxLength(12);
            e.Property(x => x.ContactPerson).HasMaxLength(150);
            e.Property(x => x.ContactEmail).HasMaxLength(320);
            e.Property(x => x.ContactPhone).HasMaxLength(40);
            e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<OrgRegistration>(e =>
        {
            e.ToTable("org_registrations", "org", t => t.HasCheckConstraint("ck_org_registrations_owner", "(company_id IS NULL) <> (branch_id IS NULL)"));
            e.HasIndex(x => x.CompanyId);
            e.HasIndex(x => x.BranchId);
            e.HasIndex(x => new { x.CompanyId, x.RegistrationType, x.RegistrationNumber }).IsUnique().HasFilter("company_id IS NOT NULL");
            e.Property(x => x.RegistrationType).HasMaxLength(40);
            e.Property(x => x.RegistrationNumber).HasMaxLength(60);
            e.Property(x => x.Jurisdiction).HasMaxLength(120);
            e.Property(x => x.RegistrationSubtype).HasMaxLength(60);
            e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<FileObject>().WithMany().HasForeignKey(x => x.DocumentFileId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<CompanyContact>(e =>
        {
            e.ToTable("company_contacts", "org");
            e.HasIndex(x => x.CompanyId);
            e.Property(x => x.ContactType).HasMaxLength(30);
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Designation).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<CompanyBankAccount>(e =>
        {
            e.ToTable("company_bank_accounts", "org");
            e.HasIndex(x => x.CompanyId);
            e.Property(x => x.BankName).HasMaxLength(120);
            e.Property(x => x.BranchName).HasMaxLength(120);
            e.Property(x => x.AccountNumber).HasMaxLength(40);
            e.Property(x => x.Ifsc).HasMaxLength(15);
            e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<DepartmentHodHistory>(e =>
        {
            e.ToTable("department_hod_history", "org");
            e.HasIndex(x => x.DepartmentId);
            e.Property(x => x.Role).HasMaxLength(30);
            e.Property(x => x.Reason).HasMaxLength(400);
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Branch>(e =>
        {
            e.ToTable("branches", "org");
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.BranchType).HasMaxLength(40);
            e.Property(x => x.ContactPhone).HasMaxLength(40);
            e.Property(x => x.ContactEmail).HasMaxLength(320);
            e.Property(x => x.AddressLine1).HasMaxLength(200);
            e.Property(x => x.AddressLine2).HasMaxLength(200);
            e.Property(x => x.City).HasMaxLength(80);
            e.Property(x => x.District).HasMaxLength(80);
            e.Property(x => x.State).HasMaxLength(80);
            e.Property(x => x.Country).HasMaxLength(80);
            e.Property(x => x.PinCode).HasMaxLength(12);
            e.Property(x => x.WorkLocationType).HasMaxLength(40);
            e.Property(x => x.ClientSiteRef).HasMaxLength(120);
            e.Property(x => x.DefaultShiftName).HasMaxLength(64);
            e.Property(x => x.WeeklyOffPattern).HasMaxLength(30);
            e.Property(x => x.WeeklyOffConfigJson).HasColumnType("jsonb");
            e.Property(x => x.HolidayCalendarCode).HasMaxLength(64);
            e.Property(x => x.AttendanceMode).HasMaxLength(40);
            e.Property(x => x.AttendancePolicyCode).HasMaxLength(64);
            e.Property(x => x.CostCentre).HasMaxLength(64);
            e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.ManagerEmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.HrEmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Department>(e =>
        {
            e.ToTable("departments", "org", t => t.HasCheckConstraint("ck_departments_parent", "parent_department_id IS NULL OR parent_department_id <> id"));
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.DepartmentType).HasMaxLength(60);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.CostCentre).HasMaxLength(64);
            e.Property(x => x.DefaultShiftName).HasMaxLength(64);
            e.Property(x => x.WeeklyOffPattern).HasMaxLength(30);
            e.Property(x => x.WeeklyOffConfigJson).HasColumnType("jsonb");
            e.Property(x => x.HolidayCalendarCode).HasMaxLength(64);
            e.Property(x => x.AttendancePolicyCode).HasMaxLength(64);
            e.Property(x => x.WorkArea).HasMaxLength(120);
            e.Property(x => x.BudgetAmount).HasColumnType("numeric(18,2)");
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.ParentDepartmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.HodEmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.AltHodEmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.ReportingManagerEmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.LeaveApproverEmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.AltLeaveApproverEmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.OdApproverEmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.CompOffApproverEmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.OtApproverEmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.RegularizationApproverEmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Employee>(e =>
        {
            e.ToTable("employees", "org", t =>
            {
                t.HasCheckConstraint("ck_employees_status", "status BETWEEN 0 AND 3");
                t.HasCheckConstraint("ck_employees_manager", "manager_employee_id IS NULL OR manager_employee_id <> id");
            });
            e.HasIndex(x => x.EmployeeCode).IsUnique();
            e.Property(x => x.EmployeeCode).HasMaxLength(32);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.HasIndex(x => x.BranchId);
            e.HasIndex(x => x.DepartmentId);
            e.HasIndex(x => x.ManagerEmployeeId);
            e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.ManagerEmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ReportingClosure>(e =>
        {
            e.ToTable("reporting_closure", "org", t => t.HasCheckConstraint("ck_reporting_closure_depth", "depth >= 0"));
            e.HasKey(x => new { x.AncestorId, x.DescendantId });
            e.HasIndex(x => x.DescendantId);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.AncestorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.DescendantId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<FileObject>(e =>
        {
            e.ToTable("files", "files", t => t.HasCheckConstraint("ck_files_size", "size_bytes > 0"));
            e.HasIndex(x => x.StorageKey).IsUnique();
            e.Property(x => x.StorageKey).HasMaxLength(512);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.ContentType).HasMaxLength(128);
            e.HasIndex(x => new { x.OwnerEntityType, x.OwnerEntityId });
        });
        b.Entity<FileAccessLog>(e =>
        {
            e.ToTable("file_access_log", "files");
            e.HasIndex(x => new { x.FileId, x.OccurredAt });
            e.HasOne<FileObject>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Notification>(e =>
        {
            e.ToTable("notifications", "notify");
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Link).HasMaxLength(1000);
            e.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt });
            e.HasOne<TenantUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<NotificationTemplate>(e =>
        {
            e.ToTable("templates", "notify");
            e.HasIndex(x => new { x.Code, x.Channel, x.Locale }).IsUnique();
            e.Property(x => x.Subject).HasMaxLength(500);
        });
        b.Entity<NotificationPreference>(e =>
        {
            e.ToTable("preferences", "notify");
            e.HasIndex(x => new { x.UserId, x.NotificationType, x.Channel }).IsUnique();
            e.HasOne<TenantUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Dashboard>(e =>
        {
            e.ToTable("dashboards", "dashboard");
            e.HasIndex(x => x.Code).IsUnique();
            e.HasMany(x => x.Widgets).WithOne().HasForeignKey(x => x.DashboardId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<DashboardWidget>(e =>
        {
            e.ToTable("dashboard_widgets", "dashboard", t => t.HasCheckConstraint("ck_dashboard_widgets_size", "width BETWEEN 1 AND 12 AND height BETWEEN 1 AND 12 AND x >= 0 AND y >= 0"));
            e.Property(x => x.Settings).HasColumnType("jsonb");
        });
        b.Entity<DashboardAssignment>(e =>
        {
            e.ToTable("dashboard_assignments", "dashboard");
            e.HasIndex(x => new { x.AssigneeType, x.AssigneeId });
            e.HasOne<Dashboard>().WithMany().HasForeignKey(x => x.DashboardId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TenantSetting>(e =>
        {
            e.ToTable("settings", "config");
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(128);
            e.Property(x => x.Value).HasColumnType("jsonb");
        });

        CommonModel.ConfigureShared(b);
        CommonModel.ApplyConventions(b);
    }
}
