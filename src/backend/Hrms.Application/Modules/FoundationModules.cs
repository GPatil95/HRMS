using Hrms.Shared.Security;

namespace Hrms.Application.Modules;

public static class FoundationPermissions
{
    public const string UserView = "identity.user.view";
    public const string UserCreate = "identity.user.create";
    public const string UserEdit = "identity.user.edit";
    public const string UserManageStatus = "identity.user.status";
    public const string UserAssignRole = "identity.user.assign_role";
    public const string RoleView = "identity.role.view";
    public const string RoleCreate = "identity.role.create";
    public const string RoleEdit = "identity.role.edit";
    public const string RoleDelete = "identity.role.delete";
    public const string OverrideManage = "identity.override.manage";
    public const string SessionRevoke = "identity.session.revoke";
    public const string SupportApprove = "identity.support.approve";
    public const string CompanyView = "org.company.view";
    public const string CompanyCreate = "org.company.create";
    public const string CompanyEdit = "org.company.edit";
    public const string BranchView = "org.branch.view";
    public const string BranchCreate = "org.branch.create";
    public const string BranchEdit = "org.branch.edit";
    public const string DepartmentView = "org.department.view";
    public const string DepartmentCreate = "org.department.create";
    public const string DepartmentEdit = "org.department.edit";
    public const string EmployeeView = "employee.directory.view";
    public const string EmployeeCreate = "employee.directory.create";
    public const string EmployeeEdit = "employee.directory.edit";
    public const string EmployeeExport = "employee.directory.export";
    public const string AuditView = "audit.entry.view";
    public const string AuditExport = "audit.entry.export";
    public const string SecurityEventView = "audit.security.view";
    public const string FileUpload = "files.attachment.create";
    public const string FileDownload = "files.attachment.download";
    public const string FileDelete = "files.attachment.delete";
    public const string TemplateView = "notify.template.view";
    public const string TemplateConfigure = "notify.template.configure";
    public const string DashboardView = "dashboard.home.view";
    public const string DashboardConfigure = "dashboard.layout.configure";
    public const string ReportView = "reporting.report.view";
    public const string ReportExport = "reporting.report.export";
    public const string SettingsView = "config.settings.view";
    public const string SettingsConfigure = "config.settings.configure";
}

public sealed class IdentityModule : ModuleDefinitionBase
{
    public const string ModuleCode = "core.identity";
    public override string Code => ModuleCode;
    public override string Name => "Users & Access";

    public override IReadOnlyList<PermissionDefinition> Permissions =>
    [
        P(FoundationPermissions.UserView, PermissionActions.View, "View users"),
        P(FoundationPermissions.UserCreate, PermissionActions.Create, "Invite users"),
        P(FoundationPermissions.UserEdit, PermissionActions.Edit, "Edit users"),
        P(FoundationPermissions.UserManageStatus, PermissionActions.Edit, "Activate, deactivate, lock, unlock or suspend users", sensitive: true),
        P(FoundationPermissions.UserAssignRole, PermissionActions.Edit, "Assign roles to users", sensitive: true),
        P(FoundationPermissions.RoleView, PermissionActions.View, "View roles"),
        P(FoundationPermissions.RoleCreate, PermissionActions.Create, "Create roles", sensitive: true),
        P(FoundationPermissions.RoleEdit, PermissionActions.Edit, "Edit role permissions", sensitive: true),
        P(FoundationPermissions.RoleDelete, PermissionActions.Delete, "Delete roles", sensitive: true),
        P(FoundationPermissions.OverrideManage, PermissionActions.Configure, "Manage user permission overrides", sensitive: true),
        P(FoundationPermissions.SessionRevoke, PermissionActions.Process, "Revoke user sessions", sensitive: true),
        P(FoundationPermissions.SupportApprove, PermissionActions.Approve, "Approve platform support access", sensitive: true),
        P("identity.menu.access", PermissionActions.Access, "Administration menu", kind: PermissionKind.Menu),
    ];

    public override IReadOnlyList<MenuItemDefinition> Menu =>
    [
        new("admin", "Administration", null, "settings", 900, ModuleCode, AnyPermissions: ["identity.menu.access", FoundationPermissions.UserView, FoundationPermissions.RoleView, FoundationPermissions.AuditView]),
        new("admin.users", "Users", "/admin/users", "people", 10, ModuleCode, "admin", [FoundationPermissions.UserView]),
        new("admin.roles", "Roles & Permissions", "/admin/roles", "security", 20, ModuleCode, "admin", [FoundationPermissions.RoleView]),
    ];
}

public sealed class OrganizationModule : ModuleDefinitionBase
{
    public const string ModuleCode = "core.org";
    public override string Code => ModuleCode;
    public override string Name => "Organization";
    public override IReadOnlyList<string> RequiredModules => [IdentityModule.ModuleCode];

    public override IReadOnlyList<PermissionDefinition> Permissions =>
    [
        P(FoundationPermissions.CompanyView, PermissionActions.View, "View companies"),
        P(FoundationPermissions.CompanyCreate, PermissionActions.Create, "Create companies"),
        P(FoundationPermissions.CompanyEdit, PermissionActions.Edit, "Edit companies"),
        P(FoundationPermissions.BranchView, PermissionActions.View, "View branches"),
        P(FoundationPermissions.BranchCreate, PermissionActions.Create, "Create branches"),
        P(FoundationPermissions.BranchEdit, PermissionActions.Edit, "Edit branches"),
        P(FoundationPermissions.DepartmentView, PermissionActions.View, "View departments"),
        P(FoundationPermissions.DepartmentCreate, PermissionActions.Create, "Create departments"),
        P(FoundationPermissions.DepartmentEdit, PermissionActions.Edit, "Edit departments"),
    ];

    public override IReadOnlyList<MenuItemDefinition> Menu =>
    [
        new("org", "Organization", null, "apartment", 200, ModuleCode, AnyPermissions: [FoundationPermissions.CompanyView, FoundationPermissions.BranchView, FoundationPermissions.DepartmentView]),
        new("org.companies", "Companies", "/org/companies", "business", 10, ModuleCode, "org", [FoundationPermissions.CompanyView]),
        new("org.branches", "Branches", "/org/branches", "store", 20, ModuleCode, "org", [FoundationPermissions.BranchView]),
        new("org.departments", "Departments", "/org/departments", "account_tree", 30, ModuleCode, "org", [FoundationPermissions.DepartmentView]),
    ];
}

public sealed class EmployeeFoundationModule : ModuleDefinitionBase
{
    public const string ModuleCode = "core.employee";
    public override string Code => ModuleCode;
    public override string Name => "Employee Directory (foundation)";
    public override IReadOnlyList<string> RequiredModules => [OrganizationModule.ModuleCode];

    public override IReadOnlyList<PermissionDefinition> Permissions =>
    [
        P(FoundationPermissions.EmployeeView, PermissionActions.View, "View employee directory", scoped: true),
        P(FoundationPermissions.EmployeeCreate, PermissionActions.Create, "Create directory records", scoped: true),
        P(FoundationPermissions.EmployeeEdit, PermissionActions.Edit, "Edit directory records", scoped: true),
        P(FoundationPermissions.EmployeeExport, PermissionActions.Export, "Export employee directory", scoped: true, sensitive: true),
    ];

    public override IReadOnlyList<MenuItemDefinition> Menu =>
    [
        new("employees", "Employees", "/employees", "badge", 300, ModuleCode, AnyPermissions: [FoundationPermissions.EmployeeView]),
    ];
}

public sealed class AuditModule : ModuleDefinitionBase
{
    public const string ModuleCode = "core.audit";
    public override string Code => ModuleCode;
    public override string Name => "Audit";

    public override IReadOnlyList<PermissionDefinition> Permissions =>
    [
        P(FoundationPermissions.AuditView, PermissionActions.View, "View audit trail", sensitive: true),
        P(FoundationPermissions.AuditExport, PermissionActions.Export, "Export audit trail", sensitive: true),
        P(FoundationPermissions.SecurityEventView, PermissionActions.View, "View security events", sensitive: true),
    ];

    public override IReadOnlyList<MenuItemDefinition> Menu =>
    [
        new("admin.audit", "Audit Log", "/admin/audit", "history", 40, IdentityModule.ModuleCode, "admin", [FoundationPermissions.AuditView]),
        new("admin.security-events", "Security Events", "/admin/security-events", "shield", 50, IdentityModule.ModuleCode, "admin", [FoundationPermissions.SecurityEventView]),
    ];
}

public sealed class FilesModule : ModuleDefinitionBase
{
    public const string ModuleCode = "core.files";
    public const string GeneralAttachmentPurpose = "general.attachment";
    public override string Code => ModuleCode;
    public override string Name => "Files";

    public override IReadOnlyList<PermissionDefinition> Permissions =>
    [
        P(FoundationPermissions.FileUpload, PermissionActions.Create, "Upload attachments"),
        P(FoundationPermissions.FileDownload, PermissionActions.Download, "Download attachments"),
        P(FoundationPermissions.FileDelete, PermissionActions.Delete, "Delete attachments"),
    ];

    public override IReadOnlyList<FilePurposeDefinition> FilePurposes =>
    [
        new(GeneralAttachmentPurpose, ModuleCode, [".pdf", ".png", ".jpg", ".jpeg", ".txt", ".csv", ".docx", ".xlsx"], 10 * 1024 * 1024,
            FoundationPermissions.FileUpload, FoundationPermissions.FileDownload, FoundationPermissions.FileDelete),
    ];
}

public sealed class NotificationsModule : ModuleDefinitionBase
{
    public const string ModuleCode = "core.notify";
    public override string Code => ModuleCode;
    public override string Name => "Notifications";

    public override IReadOnlyList<PermissionDefinition> Permissions =>
    [
        P(FoundationPermissions.TemplateView, PermissionActions.View, "View notification templates"),
        P(FoundationPermissions.TemplateConfigure, PermissionActions.Configure, "Edit notification templates"),
    ];

    public override IReadOnlyList<MenuItemDefinition> Menu =>
    [
        new("admin.templates", "Notification Templates", "/admin/templates", "mail", 60, IdentityModule.ModuleCode, "admin", [FoundationPermissions.TemplateView]),
    ];

    public override IReadOnlyList<WidgetDefinition> Widgets =>
    [
        new("core.notifications", "My notifications", ModuleCode, "list", null, null, 6, 3),
    ];
}

public sealed class DashboardModule : ModuleDefinitionBase
{
    public const string ModuleCode = "core.dashboard";
    public override string Code => ModuleCode;
    public override string Name => "Dashboards";

    public override IReadOnlyList<PermissionDefinition> Permissions =>
    [
        P(FoundationPermissions.DashboardView, PermissionActions.View, "View dashboard"),
        P(FoundationPermissions.DashboardConfigure, PermissionActions.Configure, "Configure dashboards and assignments"),
    ];

    public override IReadOnlyList<MenuItemDefinition> Menu =>
    [
        new("dashboard", "Dashboard", "/", "dashboard", 10, ModuleCode, AnyPermissions: [FoundationPermissions.DashboardView]),
    ];

    public override IReadOnlyList<WidgetDefinition> Widgets =>
    [
        new("core.welcome", "Welcome", ModuleCode, "kpi", null, null, 6, 2),
    ];
}

public sealed class ReportingModule : ModuleDefinitionBase
{
    public const string ModuleCode = "core.reporting";
    public override string Code => ModuleCode;
    public override string Name => "Reports";

    public override IReadOnlyList<PermissionDefinition> Permissions =>
    [
        P(FoundationPermissions.ReportView, PermissionActions.View, "Access reports"),
        P(FoundationPermissions.ReportExport, PermissionActions.Export, "Export reports", sensitive: true),
    ];

    public override IReadOnlyList<MenuItemDefinition> Menu =>
    [
        new("reports", "Reports", "/reports", "assessment", 800, ModuleCode, AnyPermissions: [FoundationPermissions.ReportView]),
    ];
}

public sealed class ConfigurationModule : ModuleDefinitionBase
{
    public const string ModuleCode = "core.config";
    public override string Code => ModuleCode;
    public override string Name => "Configuration";

    public override IReadOnlyList<PermissionDefinition> Permissions =>
    [
        P(FoundationPermissions.SettingsView, PermissionActions.View, "View tenant settings"),
        P(FoundationPermissions.SettingsConfigure, PermissionActions.Configure, "Change tenant settings", sensitive: true),
    ];
}

public static class FoundationModules
{
    public static IReadOnlyList<IModuleDefinition> All { get; } =
    [
        new IdentityModule(), new OrganizationModule(), new EmployeeFoundationModule(), new AuditModule(), new FilesModule(),
        new NotificationsModule(), new DashboardModule(), new ReportingModule(), new ConfigurationModule(),
    ];
}
