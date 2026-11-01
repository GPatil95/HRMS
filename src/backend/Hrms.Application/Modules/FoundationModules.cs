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
    public const string MasterView = "org.master.view";
    public const string MasterCreate = "org.master.create";
    public const string MasterEdit = "org.master.edit";
    public const string EmployeeView = "employee.directory.view";
    public const string EmployeeCreate = "employee.directory.create";
    public const string EmployeeEdit = "employee.directory.edit";
    public const string EmployeeExport = "employee.directory.export";
    public const string ProfileView = "employee.profile.view";
    public const string PersonalEdit = "employee.personal.edit";
    public const string ContactsEdit = "employee.contacts.edit";
    public const string BankView = "employee.bank.view";
    public const string BankEdit = "employee.bank.edit";
    public const string BankVerify = "employee.bank.verify";
    public const string StatutoryView = "employee.statutory.view";
    public const string StatutoryEdit = "employee.statutory.edit";
    public const string HistoryView = "employee.history.view";
    public const string HistoryBackdate = "employee.history.backdate";
    public const string LifecycleView = "employee.lifecycle.view";
    public const string LifecycleCreate = "employee.lifecycle.create";
    public const string LifecycleApprove = "employee.lifecycle.approve";
    public const string NumberingManage = "employee.numbering.manage";
    public const string DocumentsView = "employee.documents.view";
    public const string DocumentsUpload = "employee.documents.upload";
    public const string DocumentsVerify = "employee.documents.verify";
    public const string DocumentsManage = "employee.documents.manage";
    public const string LettersView = "employee.letters.view";
    public const string LettersIssue = "employee.letters.issue";
    public const string LettersManage = "employee.letters.manage";
    public const string SalaryComponentView = "salary.component.view";
    public const string SalaryComponentManage = "salary.component.manage";
    public const string SalaryStructureView = "salary.structure.view";
    public const string SalaryStructureManage = "salary.structure.manage";
    public const string SalaryAssignmentView = "salary.assignment.view";
    public const string SalaryAssignmentCreate = "salary.assignment.create";
    public const string SalaryAssignmentApprove = "salary.assignment.approve";
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
        P(FoundationPermissions.MasterView, PermissionActions.View, "View organization reference masters"),
        P(FoundationPermissions.MasterCreate, PermissionActions.Create, "Create organization reference masters"),
        P(FoundationPermissions.MasterEdit, PermissionActions.Edit, "Edit organization reference masters"),
    ];

    public override IReadOnlyList<MenuItemDefinition> Menu =>
    [
        new("org", "Organization", null, "apartment", 200, ModuleCode, AnyPermissions: [FoundationPermissions.CompanyView, FoundationPermissions.BranchView, FoundationPermissions.DepartmentView]),
        new("org.companies", "Companies", "/org/companies", "business", 10, ModuleCode, "org", [FoundationPermissions.CompanyView]),
        new("org.branches", "Branches", "/org/branches", "store", 20, ModuleCode, "org", [FoundationPermissions.BranchView]),
        new("org.departments", "Departments", "/org/departments", "account_tree", 30, ModuleCode, "org", [FoundationPermissions.DepartmentView]),
        new("org.masters", "Masters", "/org/masters", "settings", 40, ModuleCode, "org", [FoundationPermissions.MasterView]),
    ];

    public const string LogoPurpose = "core.org.logo";
    public const string CompanyDocumentPurpose = "core.org.company-document";
    public const string BranchDocumentPurpose = "core.org.branch-document";

    public override IReadOnlyList<FilePurposeDefinition> FilePurposes =>
    [
        new(LogoPurpose, ModuleCode, [".png", ".jpg", ".jpeg", ".webp"], 2 * 1024 * 1024,
            FoundationPermissions.CompanyEdit, FoundationPermissions.CompanyView, FoundationPermissions.CompanyEdit),
        new(CompanyDocumentPurpose, ModuleCode, [".pdf", ".png", ".jpg", ".jpeg"], 10 * 1024 * 1024,
            FoundationPermissions.CompanyEdit, FoundationPermissions.CompanyView, FoundationPermissions.CompanyEdit, "confidential"),
        new(BranchDocumentPurpose, ModuleCode, [".pdf", ".png", ".jpg", ".jpeg"], 10 * 1024 * 1024,
            FoundationPermissions.BranchEdit, FoundationPermissions.BranchView, FoundationPermissions.BranchEdit, "confidential"),
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
        P(FoundationPermissions.ProfileView, PermissionActions.View, "View employee profile sections", scoped: true),
        P(FoundationPermissions.PersonalEdit, PermissionActions.Edit, "Edit personal/contact sections", scoped: true),
        P(FoundationPermissions.ContactsEdit, PermissionActions.Edit, "Edit emergency/family/nominee contacts", scoped: true),
        P(FoundationPermissions.BankView, PermissionActions.View, "View masked bank details", scoped: true, sensitive: true),
        P(FoundationPermissions.BankEdit, PermissionActions.Edit, "Edit bank accounts", scoped: true, sensitive: true),
        P(FoundationPermissions.BankVerify, PermissionActions.Approve, "Verify bank accounts", scoped: true, sensitive: true),
        P(FoundationPermissions.StatutoryView, PermissionActions.View, "View statutory identifiers (masked)", scoped: true, sensitive: true),
        P(FoundationPermissions.StatutoryEdit, PermissionActions.Edit, "Edit statutory identifiers", scoped: true, sensitive: true),
        P(FoundationPermissions.HistoryView, PermissionActions.View, "View employment history", scoped: true),
        P(FoundationPermissions.HistoryBackdate, PermissionActions.Edit, "Backdate/correct employment history", scoped: true, sensitive: true),
        P(FoundationPermissions.LifecycleView, PermissionActions.View, "View lifecycle events", scoped: true),
        P(FoundationPermissions.LifecycleCreate, PermissionActions.Create, "Submit lifecycle events", scoped: true),
        P(FoundationPermissions.LifecycleApprove, "approve", "Approve lifecycle events", scoped: true, sensitive: true),
        P(FoundationPermissions.NumberingManage, PermissionActions.Configure, "Manage employee-code numbering config"),
        P(FoundationPermissions.DocumentsView, PermissionActions.View, "View employee documents", scoped: true, sensitive: true),
        P(FoundationPermissions.DocumentsUpload, PermissionActions.Create, "Upload employee documents", scoped: true, sensitive: true),
        P(FoundationPermissions.DocumentsVerify, PermissionActions.Approve, "Verify employee documents", scoped: true, sensitive: true),
        P(FoundationPermissions.DocumentsManage, PermissionActions.Configure, "Manage document categories"),
        P(FoundationPermissions.LettersView, PermissionActions.View, "View issued letters", scoped: true),
        P(FoundationPermissions.LettersIssue, PermissionActions.Create, "Issue letters to employees", scoped: true),
        P(FoundationPermissions.LettersManage, PermissionActions.Configure, "Manage letter types and templates"),
        P(FoundationPermissions.SalaryComponentView, PermissionActions.View, "View salary components"),
        P(FoundationPermissions.SalaryComponentManage, PermissionActions.Configure, "Manage salary components"),
        P(FoundationPermissions.SalaryStructureView, PermissionActions.View, "View salary structures"),
        P(FoundationPermissions.SalaryStructureManage, PermissionActions.Configure, "Manage salary structures"),
        P(FoundationPermissions.SalaryAssignmentView, PermissionActions.View, "View employee salary", scoped: true, sensitive: true),
        P(FoundationPermissions.SalaryAssignmentCreate, PermissionActions.Create, "Assign or revise employee salary", scoped: true, sensitive: true),
        P(FoundationPermissions.SalaryAssignmentApprove, PermissionActions.Approve, "Approve salary assignments", scoped: true, sensitive: true),
    ];

    public const string EmployeeDocumentPurpose = "hr.employee.document";

    public override IReadOnlyList<FilePurposeDefinition> FilePurposes =>
    [
        new(EmployeeDocumentPurpose, ModuleCode, [".pdf", ".png", ".jpg", ".jpeg", ".webp"], 10 * 1024 * 1024,
            FoundationPermissions.DocumentsUpload, FoundationPermissions.DocumentsView, FoundationPermissions.DocumentsUpload, "confidential"),
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
