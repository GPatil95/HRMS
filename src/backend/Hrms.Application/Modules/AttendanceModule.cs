using Hrms.Domain.Platform;
using Hrms.Shared.Security;

namespace Hrms.Application.Modules;

public static class AttendancePermissions
{
    public const string MastersView = "attendance.masters.view";
    public const string MastersManage = "attendance.masters.manage";
    public const string MastersBackdate = "attendance.masters.backdate";
    public const string AssignmentsManage = "attendance.assignments.manage";
}

/// <summary>Attendance module — optional, entitlement-gated. M3.1 ships masters + config resolver only.</summary>
public sealed class AttendanceModule : ModuleDefinitionBase
{
    public const string ModuleCode = "attendance";
    public override string Code => ModuleCode;
    public override string Name => "Attendance";
    public override ModuleKind Kind => ModuleKind.Optional;
    public override IReadOnlyList<string> RequiredModules => [EmployeeFoundationModule.ModuleCode];

    public override IReadOnlyList<PermissionDefinition> Permissions =>
    [
        P(AttendancePermissions.MastersView, PermissionActions.View, "View attendance masters"),
        P(AttendancePermissions.MastersManage, PermissionActions.Configure, "Manage shifts, calendars and policies", sensitive: true),
        P(AttendancePermissions.MastersBackdate, PermissionActions.Edit, "Publish master versions with backdated effect", sensitive: true),
        P(AttendancePermissions.AssignmentsManage, PermissionActions.Configure, "Manage shift and policy assignments and rosters", scoped: true),
    ];

    public override IReadOnlyList<MenuItemDefinition> Menu =>
    [
        new("attendance", "Attendance", null, "schedule", 500, ModuleCode,
            AnyPermissions: [AttendancePermissions.MastersView, AttendancePermissions.AssignmentsManage]),
        new("attendance.admin", "Attendance Setup", "/attendance/admin", "settings", 10, ModuleCode, "attendance",
            [AttendancePermissions.MastersView]),
    ];
}
