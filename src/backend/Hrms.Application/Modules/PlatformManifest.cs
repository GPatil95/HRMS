using Hrms.Domain.Platform;

namespace Hrms.Application.Modules;

public static class PlatformPermissions
{
    public const string TenantView = "platform.tenant.view";
    public const string TenantCreate = "platform.tenant.create";
    public const string TenantActivate = "platform.tenant.activate";
    public const string TenantSuspend = "platform.tenant.suspend";
    public const string TenantDeactivate = "platform.tenant.deactivate";
    public const string TenantDbManage = "platform.tenant.db.manage";
    public const string CatalogView = "platform.catalog.view";
    public const string PlanView = "platform.plan.view";
    public const string PlanManage = "platform.plan.manage";
    public const string SubscriptionManage = "platform.subscription.manage";
    public const string ModuleEntitlementManage = "platform.entitlement.module.manage";
    public const string FeatureEntitlementManage = "platform.entitlement.feature.manage";
    public const string UserView = "platform.user.view";
    public const string UserManage = "platform.user.manage";
    public const string RoleManage = "platform.role.manage";
    public const string SupportRequest = "platform.support.request";
    public const string SupportManage = "platform.support.manage";
    public const string AuditView = "platform.audit.view";
    public const string HealthView = "platform.health.view";
    public const string ConfigManage = "platform.config.manage";

    public static IReadOnlyList<(string Code, string Description)> All { get; } =
    [
        (TenantView, "View tenants"), (TenantCreate, "Create tenants"), (TenantActivate, "Activate tenants"),
        (TenantSuspend, "Suspend tenants"), (TenantDeactivate, "Deactivate tenants"), (TenantDbManage, "Manage tenant databases/provisioning"),
        (CatalogView, "View module catalogue"), (PlanView, "View plans"), (PlanManage, "Manage plans"),
        (SubscriptionManage, "Manage subscriptions"), (ModuleEntitlementManage, "Enable/disable tenant modules"),
        (FeatureEntitlementManage, "Manage tenant feature entitlements"), (UserView, "View platform users"),
        (UserManage, "Manage platform users"), (RoleManage, "Manage platform roles"), (SupportRequest, "Request support access"),
        (SupportManage, "Manage support access"), (AuditView, "View platform audit"), (HealthView, "View system health"),
        (ConfigManage, "Manage global configuration"),
    ];
}

public sealed record PlatformRoleSeed(string Code, string Name, IReadOnlyList<string> Permissions);

public static class PlatformManifest
{
    public const string SuperAdminRole = "PLATFORM_SUPER_ADMIN";

    public static IReadOnlyList<PlatformRoleSeed> Roles { get; } =
    [
        new(SuperAdminRole, "Platform Super Admin", PlatformPermissions.All.Select(p => p.Code).ToList()),
        new("TENANT_OPERATIONS", "Tenant Operations",
            [PlatformPermissions.TenantView, PlatformPermissions.TenantCreate, PlatformPermissions.TenantActivate, PlatformPermissions.TenantSuspend, PlatformPermissions.TenantDbManage, PlatformPermissions.CatalogView, PlatformPermissions.HealthView]),
        new("COMMERCIAL", "Commercial / Billing",
            [PlatformPermissions.TenantView, PlatformPermissions.CatalogView, PlatformPermissions.PlanView, PlatformPermissions.PlanManage, PlatformPermissions.SubscriptionManage, PlatformPermissions.ModuleEntitlementManage, PlatformPermissions.FeatureEntitlementManage]),
        new("SUPPORT_AGENT", "Support Agent", [PlatformPermissions.TenantView, PlatformPermissions.SupportRequest]),
        new("SECURITY_AUDITOR", "Security & Compliance Auditor", [PlatformPermissions.AuditView, PlatformPermissions.HealthView, PlatformPermissions.TenantView]),
    ];

    public static IReadOnlyList<MenuItemDefinition> Menu { get; } =
    [
        new("p.dashboard", "Dashboard", "/admin", "dashboard", 10, "platform"),
        new("p.tenants", "Tenants", "/admin/tenants", "domain", 20, "platform", AnyPermissions: [PlatformPermissions.TenantView]),
        new("p.plans", "Plans & Modules", "/admin/plans", "inventory", 30, "platform", AnyPermissions: [PlatformPermissions.PlanView, PlatformPermissions.CatalogView]),
        new("p.users", "Platform Users", "/admin/platform-users", "admin_panel_settings", 40, "platform", AnyPermissions: [PlatformPermissions.UserView]),
        new("p.audit", "Platform Audit", "/admin/audit", "history", 50, "platform", AnyPermissions: [PlatformPermissions.AuditView]),
    ];

    public static IReadOnlyList<WidgetDefinition> Widgets { get; } =
    [
        new("platform.tenant-status", "Tenants by status", "platform", "kpi", PlatformPermissions.TenantView, null, 6, 2, Shared.Security.Realms.Platform),
    ];
}

public sealed record CatalogModuleSeed(string Code, string Name, ModuleKind Kind, string? ParentCode, int SortOrder, IReadOnlyList<string> Requires);

/// <summary>Commercial module catalogue (architecture doc 14). Business modules exist here before their code exists.</summary>
public static class ModuleCatalogSeed
{
    public static IReadOnlyList<CatalogModuleSeed> Modules { get; } =
    [
        new("core.identity", "Users & Access", ModuleKind.Core, null, 1, []),
        new("core.org", "Organization", ModuleKind.Core, null, 2, ["core.identity"]),
        new("core.employee", "Employee / HR Core", ModuleKind.Core, null, 3, ["core.org"]),
        new("core.workflow", "Approval Workflow", ModuleKind.Core, null, 4, []),
        new("core.workitems", "Unified Work Inbox", ModuleKind.Core, null, 5, []),
        new("core.notify", "Notifications & Email", ModuleKind.Core, null, 6, []),
        new("core.files", "Files", ModuleKind.Core, null, 7, []),
        new("core.audit", "Audit", ModuleKind.Core, null, 8, []),
        new("core.config", "Configuration", ModuleKind.Core, null, 9, []),
        new("core.io", "Import / Export", ModuleKind.Core, null, 10, []),
        new("core.reporting", "Reports", ModuleKind.Core, null, 11, []),
        new("core.dashboard", "Dashboards", ModuleKind.Core, null, 12, []),
        new("employee.documents", "Employee Documents", ModuleKind.SubModule, "core.employee", 20, ["core.employee"]),
        new("employee.letters", "Letter Management", ModuleKind.SubModule, "core.employee", 21, ["core.employee"]),
        new("employee.exit", "Exit & FNF", ModuleKind.SubModule, "core.employee", 22, ["core.employee"]),
        new("attendance", "Attendance", ModuleKind.Optional, null, 30, []),
        new("attendance.biometric", "Biometric Integration", ModuleKind.SubModule, "attendance", 31, ["attendance"]),
        new("leave", "Leave", ModuleKind.Optional, null, 32, []),
        new("payroll", "Payroll", ModuleKind.Optional, null, 33, []),
        new("payroll.tax", "Income Tax / TDS", ModuleKind.SubModule, "payroll", 34, ["payroll"]),
        new("loans", "Loans & Advances", ModuleKind.Optional, null, 35, []),
        new("expense", "Expense & Reimbursement", ModuleKind.Optional, null, 36, []),
        new("recruitment", "Recruitment / ATS", ModuleKind.Optional, null, 40, []),
        new("pms", "Performance Management", ModuleKind.Optional, null, 41, []),
        new("assets", "Assets", ModuleKind.Optional, null, 42, []),
        new("projects", "Projects", ModuleKind.Optional, null, 43, []),
        new("mywork", "My Work", ModuleKind.Optional, null, 44, []),
        new("calendar", "Calendar & Meetings", ModuleKind.Optional, null, 45, []),
        new("rooms", "Meeting Rooms", ModuleKind.Optional, null, 46, []),
        new("workplace", "Workplace & Seats", ModuleKind.Optional, null, 47, []),
        new("visitors", "Visitor Management", ModuleKind.Optional, null, 48, []),
        new("mailroom", "Mail & Parcels", ModuleKind.Optional, null, 49, []),
        new("clientbilling", "Client Billing", ModuleKind.Optional, null, 50, []),
        new("ai.assistant", "AI Assistant", ModuleKind.Optional, null, 60, []),
    ];
}
