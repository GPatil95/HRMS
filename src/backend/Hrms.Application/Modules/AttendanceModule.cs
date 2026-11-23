using Hrms.Domain.Platform;
using Hrms.Shared.Security;

namespace Hrms.Application.Modules;

public static class AttendancePermissions
{
    public const string MastersView = "attendance.masters.view";
    public const string MastersManage = "attendance.masters.manage";
    public const string MastersBackdate = "attendance.masters.backdate";
    public const string AssignmentsManage = "attendance.assignments.manage";
    // M3.2 — raw punches, devices, import
    public const string MyView = "attendance.my.view";
    public const string MyPunch = "attendance.my.punch";
    public const string View = "attendance.view";
    public const string PunchesManual = "attendance.punches.manual";
    public const string PunchesImport = "attendance.punches.import";
    public const string DevicesManage = "attendance.devices.manage";

    public const string PunchImportPurpose = "attendance.punches.import-csv";
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
        P(AttendancePermissions.MyView, PermissionActions.View, "View own punches", kind: PermissionKind.Action),
        P(AttendancePermissions.MyPunch, PermissionActions.Create, "Record own check-in/check-out", kind: PermissionKind.Action),
        P(AttendancePermissions.View, PermissionActions.View, "View raw punches and ingest history", scoped: true),
        P(AttendancePermissions.PunchesManual, PermissionActions.Edit, "Record manual punches and void punches", scoped: true),
        P(AttendancePermissions.PunchesImport, PermissionActions.Import, "Import punches from CSV", scoped: true),
        P(AttendancePermissions.DevicesManage, PermissionActions.Configure, "Manage attendance devices, tokens and user mappings", sensitive: true),
    ];

    public override IReadOnlyList<FilePurposeDefinition> FilePurposes =>
    [
        new(AttendancePermissions.PunchImportPurpose, ModuleCode, [".csv"], 10 * 1024 * 1024,
            AttendancePermissions.PunchesImport, AttendancePermissions.View, AttendancePermissions.PunchesImport, "confidential"),
    ];

    public override IReadOnlyList<MenuItemDefinition> Menu =>
    [
        new("attendance", "Attendance", null, "schedule", 500, ModuleCode,
            AnyPermissions: [AttendancePermissions.MastersView, AttendancePermissions.AssignmentsManage,
                AttendancePermissions.View, AttendancePermissions.MyView, AttendancePermissions.MyPunch,
                AttendancePermissions.PunchesManual, AttendancePermissions.PunchesImport, AttendancePermissions.DevicesManage]),
        new("attendance.my", "My Punches", "/attendance/my", "check_circle", 5, ModuleCode, "attendance",
            [AttendancePermissions.MyView, AttendancePermissions.MyPunch]),
        new("attendance.punches", "Raw Punches", "/attendance/punches", "list", 10, ModuleCode, "attendance",
            [AttendancePermissions.View, AttendancePermissions.PunchesManual]),
        new("attendance.import", "Import Punches", "/attendance/import", "upload", 20, ModuleCode, "attendance",
            [AttendancePermissions.PunchesImport]),
        new("attendance.imports", "Import History", "/attendance/imports", "history", 30, ModuleCode, "attendance",
            [AttendancePermissions.View]),
        new("attendance.devices", "Devices", "/attendance/devices", "memory", 40, ModuleCode, "attendance",
            [AttendancePermissions.DevicesManage]),
        new("attendance.admin", "Attendance Setup", "/attendance/admin", "settings", 50, ModuleCode, "attendance",
            [AttendancePermissions.MastersView]),
    ];
}
