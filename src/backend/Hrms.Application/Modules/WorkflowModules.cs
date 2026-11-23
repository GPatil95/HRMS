using Hrms.Domain.Platform;
using Hrms.Shared.Security;

namespace Hrms.Application.Modules;

public static class WorkflowPermissions
{
    public const string DefinitionsView = "workflow.definitions.view";
    public const string DefinitionsManage = "workflow.definitions.manage";
    public const string InstancesView = "workflow.instances.view";
    public const string InstancesStart = "workflow.instances.start";
    public const string InstancesManage = "workflow.instances.manage";
    public const string DelegationsManage = "workflow.delegations.manage";
    public const string WorkItemsView = "workitems.inbox.view";
    public const string WorkItemsAct = "workitems.inbox.act";
}

/// <summary>core.workflow — the shared approval engine every module routes approvals through (doc 19).</summary>
public sealed class WorkflowModule : ModuleDefinitionBase
{
    public const string ModuleCode = "core.workflow";
    public override string Code => ModuleCode;
    public override string Name => "Approval Workflow";

    public override IReadOnlyList<PermissionDefinition> Permissions =>
    [
        P(WorkflowPermissions.DefinitionsView, PermissionActions.View, "View workflow definitions"),
        P(WorkflowPermissions.DefinitionsManage, PermissionActions.Configure, "Create, edit and publish workflow definitions", sensitive: true),
        P(WorkflowPermissions.InstancesView, PermissionActions.View, "View workflow instances and history", scoped: true),
        P(WorkflowPermissions.InstancesStart, PermissionActions.Create, "Start workflows for generic requests"),
        P(WorkflowPermissions.InstancesManage, PermissionActions.Process, "Cancel or reassign any workflow instance", sensitive: true),
        P(WorkflowPermissions.DelegationsManage, PermissionActions.Configure, "Manage own out-of-office delegations"),
    ];

    public override IReadOnlyList<MenuItemDefinition> Menu =>
    [
        new("workflow.definitions", "Workflow Definitions", "/workflow/definitions", "account_tree", 910, ModuleCode, null,
            [WorkflowPermissions.DefinitionsView]),
    ];
}

/// <summary>core.workitems — the unified "My Work" inbox projection (doc 19).</summary>
public sealed class WorkItemsModule : ModuleDefinitionBase
{
    public const string ModuleCode = "core.workitems";
    public override string Code => ModuleCode;
    public override string Name => "Unified Work Inbox";

    public override IReadOnlyList<PermissionDefinition> Permissions =>
    [
        P(WorkflowPermissions.WorkItemsView, PermissionActions.View, "View own work inbox"),
        P(WorkflowPermissions.WorkItemsAct, PermissionActions.Approve, "Act on own inbox items (claim, approve, reject, send back, delegate)"),
    ];

    public override IReadOnlyList<MenuItemDefinition> Menu =>
    [
        new("workitems.inbox", "My Work", "/my-work", "inbox", 5, ModuleCode, null, [WorkflowPermissions.WorkItemsView]),
    ];
}
