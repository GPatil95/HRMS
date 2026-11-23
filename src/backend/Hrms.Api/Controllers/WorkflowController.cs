using Asp.Versioning;
using Hrms.Api.Hosting;
using Hrms.Api.Security;
using Hrms.Application.Authorization;
using Hrms.Application.Modules;
using Hrms.Application.Workflow;
using Hrms.Shared.Results;
using Microsoft.AspNetCore.Mvc;

namespace Hrms.Api.Controllers;

public sealed record PublishDefinitionRequest(DateOnly? EffectiveFrom);
public sealed record WorkflowCommentRequest(string? Comment);
public sealed record WorkItemCompleteRequest(string Action, string? Comment, string? SendBackTo);
public sealed record WorkItemDelegateRequest(Guid ToUserId, string? Comment);
public sealed record ResumeWorkflowRequest(Guid? AssigneeUserId, string? Comment);

/// <summary>core.workflow — definitions admin, instance lifecycle and history (doc 19).</summary>
[ApiController, ApiVersion("1.0"), Route("api/v{version:apiVersion}/workflow")]
public sealed class WorkflowController(WorkflowDefinitionService definitions, IWorkflowEngine engine, IWorkItemService items, IAccessGuard access) : ControllerBase
{
    private const string M = WorkflowModule.ModuleCode;
    private string? IdempotencyKey => Request.Headers.TryGetValue("Idempotency-Key", out var v) ? v.ToString() : null;

    // ---------- Definitions ----------
    [HttpGet("document-types"), RequirePermission(M, WorkflowPermissions.DefinitionsView)]
    public IActionResult DocumentTypes() => Ok(definitions.DocumentTypes.OrderBy(d => d.Code));

    [HttpGet("definitions"), RequirePermission(M, WorkflowPermissions.DefinitionsView)]
    public async Task<IActionResult> Definitions([FromQuery] string? documentType, [FromQuery] string? status, CancellationToken ct) =>
        Ok(await definitions.ListAsync(documentType, status, ct));

    [HttpGet("definitions/{id:guid}"), RequirePermission(M, WorkflowPermissions.DefinitionsView)]
    public async Task<IActionResult> Definition(Guid id, CancellationToken ct) =>
        (await definitions.GetAsync(id, ct)).ToActionResult(d => Ok(d));

    [HttpPost("definitions"), RequirePermission(M, WorkflowPermissions.DefinitionsManage)]
    public async Task<IActionResult> CreateDefinition(WorkflowDefinitionInput input, CancellationToken ct) =>
        (await definitions.CreateAsync(input, ct)).ToActionResult(d => Ok(d));

    [HttpPut("definitions/{id:guid}"), RequirePermission(M, WorkflowPermissions.DefinitionsManage)]
    public async Task<IActionResult> UpdateDefinition(Guid id, WorkflowDefinitionInput input, CancellationToken ct) =>
        (await definitions.UpdateAsync(id, input, ct)).ToActionResult(d => Ok(d));

    [HttpDelete("definitions/{id:guid}"), RequirePermission(M, WorkflowPermissions.DefinitionsManage)]
    public async Task<IActionResult> DeleteDefinition(Guid id, CancellationToken ct) =>
        (await definitions.DeleteAsync(id, ct)).ToActionResult();

    [HttpPost("definitions/{id:guid}/publish"), RequirePermission(M, WorkflowPermissions.DefinitionsManage)]
    public async Task<IActionResult> Publish(Guid id, PublishDefinitionRequest? body, CancellationToken ct) =>
        (await definitions.PublishAsync(id, body?.EffectiveFrom, ct)).ToActionResult(d => Ok(d));

    [HttpPost("definitions/{id:guid}/retire"), RequirePermission(M, WorkflowPermissions.DefinitionsManage)]
    public async Task<IActionResult> Retire(Guid id, CancellationToken ct) =>
        (await definitions.RetireAsync(id, ct)).ToActionResult(d => Ok(d));

    // ---------- Instances ----------
    /// <summary>Starts a workflow. Modules normally call <see cref="IWorkflowEngine"/> in-process; this endpoint serves
    /// generic requests and integration testing.</summary>
    [HttpPost("instances"), RequirePermission(M, WorkflowPermissions.InstancesStart)]
    public async Task<IActionResult> Start(StartWorkflowRequest request, CancellationToken ct)
    {
        // Module documents must be started by their owning module, which authorizes the submitter.
        if (definitions.DocumentTypes.FirstOrDefault(d => d.Code == request.DocumentType) is { } type && type.ModuleCode != M)
            return ((Result<WorkflowInstanceDto>)Error.Rule("workflow.module_document", "This document type is started by its owning module.")).ToActionResult(i => Ok(i));
        return (await engine.StartAsync(request, ct)).ToActionResult(i => Ok(i));
    }

    /// <summary>Own requests for everyone with inbox access; all in-scope instances with workflow.instances.view.</summary>
    [HttpGet("instances"), RequirePermission(WorkItemsModule.ModuleCode, WorkflowPermissions.WorkItemsView)]
    public async Task<IActionResult> Instances([FromQuery] WorkflowInstanceQuery query, CancellationToken ct)
    {
        var canViewAll = await access.CheckAsync(M, WorkflowPermissions.InstancesView, ct) is null;
        return Ok(await engine.ListAsync(query, canViewAll, ct));
    }

    /// <summary>Stalled requests for workflow administrators (scope of <c>workflow.instances.manage</c>).</summary>
    [HttpGet("instances/stalled"), RequirePermission(M, WorkflowPermissions.InstancesManage)]
    public async Task<IActionResult> Stalled([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        Ok(await engine.ListStalledAsync(page, pageSize, ct));

    /// <summary>Users a workflow administrator can assign when resuming a stalled request (includes the caller).</summary>
    [HttpGet("eligible-assignees"), RequirePermission(M, WorkflowPermissions.InstancesManage)]
    public async Task<IActionResult> EligibleAssignees([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) => Ok(await items.EligibleDelegatesAsync(search, page, pageSize, false, ct));

    [HttpGet("instances/{id:guid}"), RequirePermission(WorkItemsModule.ModuleCode, WorkflowPermissions.WorkItemsView)]
    public async Task<IActionResult> Instance(Guid id, CancellationToken ct) =>
        (await engine.GetAsync(id, ct)).ToActionResult(i => Ok(i));

    [HttpGet("instances/{id:guid}/history"), RequirePermission(WorkItemsModule.ModuleCode, WorkflowPermissions.WorkItemsView)]
    public async Task<IActionResult> History(Guid id, CancellationToken ct) =>
        (await engine.HistoryAsync(id, ct)).ToActionResult(h => Ok(h));

    [HttpPost("instances/{id:guid}/cancel"), RequirePermission(WorkItemsModule.ModuleCode, WorkflowPermissions.WorkItemsView)]
    public async Task<IActionResult> Cancel(Guid id, WorkflowCommentRequest? body, CancellationToken ct) =>
        (await engine.CancelAsync(id, body?.Comment, IdempotencyKey, ct)).ToActionResult(i => Ok(i));

    [HttpPost("instances/{id:guid}/resubmit"), RequirePermission(WorkItemsModule.ModuleCode, WorkflowPermissions.WorkItemsView)]
    public async Task<IActionResult> Resubmit(Guid id, WorkflowCommentRequest? body, CancellationToken ct) =>
        (await engine.ResubmitAsync(id, body?.Comment, IdempotencyKey, ct)).ToActionResult(i => Ok(i));

    /// <summary>Resumes a stalled instance; <c>assigneeUserId</c> fills steps that still resolve no approver.</summary>
    [HttpPost("instances/{id:guid}/resume"), RequirePermission(M, WorkflowPermissions.InstancesManage)]
    public async Task<IActionResult> Resume(Guid id, ResumeWorkflowRequest? body, CancellationToken ct) =>
        (await engine.ResumeAsync(id, body?.AssigneeUserId, body?.Comment, IdempotencyKey, ct)).ToActionResult(i => Ok(i));

    /// <summary>Task action (approve, reject, send_back, delegate, escalate, claim, comment). Honors Idempotency-Key.</summary>
    [HttpPost("tasks/{taskId:guid}/actions"), RequirePermission(WorkItemsModule.ModuleCode, WorkflowPermissions.WorkItemsAct)]
    public async Task<IActionResult> Act(Guid taskId, WorkflowTaskAction action, CancellationToken ct) =>
        (await engine.ActAsync(taskId, action with { IdempotencyKey = action.IdempotencyKey ?? IdempotencyKey }, ct)).ToActionResult(i => Ok(i));

    // ---------- Delegations ----------
    [HttpGet("delegations"), RequirePermission(M, WorkflowPermissions.DelegationsManage)]
    public async Task<IActionResult> Delegations(CancellationToken ct) => Ok(await definitions.MyDelegationsAsync(ct));

    [HttpPost("delegations"), RequirePermission(M, WorkflowPermissions.DelegationsManage)]
    public async Task<IActionResult> CreateDelegation(WorkflowDelegationInput input, CancellationToken ct) =>
        (await definitions.CreateDelegationAsync(input, ct)).ToActionResult(d => Ok(d));

    [HttpDelete("delegations/{id:guid}"), RequirePermission(M, WorkflowPermissions.DelegationsManage)]
    public async Task<IActionResult> RevokeDelegation(Guid id, CancellationToken ct) =>
        (await definitions.RevokeDelegationAsync(id, ct)).ToActionResult();
}

/// <summary>core.workitems — the caller's unified inbox. Acting re-enters the workflow engine, which re-authorizes.</summary>
[ApiController, ApiVersion("1.0"), Route("api/v{version:apiVersion}/workitems")]
public sealed class WorkItemsController(IWorkItemService items) : ControllerBase
{
    private const string M = WorkItemsModule.ModuleCode;
    private string? IdempotencyKey => Request.Headers.TryGetValue("Idempotency-Key", out var v) ? v.ToString() : null;

    [HttpGet("mine"), RequirePermission(M, WorkflowPermissions.WorkItemsView)]
    public async Task<IActionResult> Mine([FromQuery] WorkItemQuery query, CancellationToken ct) => Ok(await items.MyItemsAsync(query, ct));

    [HttpGet("eligible-delegates"), RequirePermission(M, WorkflowPermissions.WorkItemsAct)]
    public async Task<IActionResult> EligibleDelegates([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) => Ok(await items.EligibleDelegatesAsync(search, page, pageSize, true, ct));

    [HttpPost("{id:guid}/claim"), RequirePermission(M, WorkflowPermissions.WorkItemsAct)]
    public async Task<IActionResult> Claim(Guid id, CancellationToken ct) =>
        (await items.ClaimAsync(id, IdempotencyKey, ct)).ToActionResult(i => Ok(i));

    [HttpPost("{id:guid}/complete"), RequirePermission(M, WorkflowPermissions.WorkItemsAct)]
    public async Task<IActionResult> Complete(Guid id, WorkItemCompleteRequest body, CancellationToken ct) =>
        (await items.CompleteAsync(id, new WorkflowTaskAction(body.Action, body.Comment, null, body.SendBackTo, IdempotencyKey), ct)).ToActionResult(i => Ok(i));

    [HttpPost("{id:guid}/delegate"), RequirePermission(M, WorkflowPermissions.WorkItemsAct)]
    public async Task<IActionResult> Delegate(Guid id, WorkItemDelegateRequest body, CancellationToken ct) =>
        (await items.DelegateAsync(id, body.ToUserId, body.Comment, IdempotencyKey, ct)).ToActionResult(i => Ok(i));
}
