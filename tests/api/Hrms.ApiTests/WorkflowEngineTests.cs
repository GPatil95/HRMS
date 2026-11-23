using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hrms.Application.Jobs;
using Hrms.Application.Workflow;
using Hrms.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using P = Hrms.Application.Modules.WorkflowPermissions;

namespace Hrms.ApiTests;

/// <summary>core.workflow + core.workitems: definitions lifecycle, sequential/parallel routing, thresholds, self-approval,
/// send-back/resubmit, delegation, claim, escalation (manual + SLA job), idempotency, outbox, permissions, tenant isolation.</summary>
[Collection(HrmsCollection.Name)]
public sealed class WorkflowEngineTests(HrmsFixture f)
{
    private const string DocType = BuiltInWorkflowDocumentTypes.GenericRequest;

    private sealed record Actor(Guid UserId, HrmsClient Client);

    private sealed record World(HrmsClient Admin, Actor Requester, Actor Manager, Actor Hr1, Actor Hr2, Actor Other, string HrRole, Guid RequesterEmployee);

    private async Task<Guid> RoleAsync(HrmsClient admin, string code, params string[] perms) =>
        await admin.PostForIdAsync("/api/v1/roles", new
        {
            code, name = code, description = "wf test",
            permissions = perms.Select(p => new { permissionCode = p, scopeLevel = "Tenant" }).ToArray(),
        });

    private async Task<Actor> UserAsync(HrmsClient admin, params Guid[] roles)
    {
        var (id, client) = await f.CreateTenantUserAsync(admin, "acme", $"wf-{Guid.NewGuid():N}@acme.test", roles);
        return new Actor(id, client);
    }

    private static async Task LinkAsync(HrmsClient admin, Guid employeeId, Guid userId)
    {
        var r = await admin.Http.PutAsJsonAsync($"/api/v1/employees/{employeeId}/user", new { userId });
        r.StatusCode.ShouldBe(HttpStatusCode.NoContent, await r.Content.ReadAsStringAsync());
    }

    private async Task<World> WorldAsync()
    {
        var admin = await f.AcmeAdminAsync();
        var inbox = await RoleAsync(admin, TestData.Unique("WFIN"), P.WorkItemsView, P.WorkItemsAct, P.InstancesStart, P.DelegationsManage);
        var hrRole = TestData.Unique("WFHR");
        var hr = await RoleAsync(admin, hrRole, P.WorkItemsView, P.WorkItemsAct);
        var requester = await UserAsync(admin, inbox);
        var manager = await UserAsync(admin, inbox);
        var hr1 = await UserAsync(admin, hr);
        var hr2 = await UserAsync(admin, hr);
        var other = await UserAsync(admin, inbox);

        var company = await admin.PostForIdAsync("/api/v1/org/companies", new { code = TestData.Unique("C"), name = "Co", legalName = (string?)null });
        var branch = await admin.PostForIdAsync("/api/v1/org/branches", new { companyId = company, code = TestData.Unique("B"), name = "Br" });
        var mgrEmp = await admin.PostForIdAsync("/api/v1/employees", new { employeeCode = TestData.Unique("E"), displayName = "Mgr", workEmail = (string?)null, branchId = branch, departmentId = (Guid?)null, managerEmployeeId = (Guid?)null });
        var reqEmp = await admin.PostForIdAsync("/api/v1/employees", new { employeeCode = TestData.Unique("E"), displayName = "Req", workEmail = (string?)null, branchId = branch, departmentId = (Guid?)null, managerEmployeeId = mgrEmp });
        await LinkAsync(admin, mgrEmp, manager.UserId);
        await LinkAsync(admin, reqEmp, requester.UserId);
        return new World(admin, requester, manager, hr1, hr2, other, hrRole, reqEmp);
    }

    private static object Step(int order, string name, string type, string? param = null, string mode = "any", decimal? min = null,
        int? sla = null, string esc = "none", string? escParam = null) => new
    {
        stepOrder = order, name, approverType = type, approverParam = param, approvalMode = mode, minAmount = min, maxAmount = (decimal?)null,
        fallbackUserId = (Guid?)null, slaHours = sla, escalationType = esc, escalationParam = escParam, allowSendBack = true, allowDelegate = true,
    };

    /// <summary>Publishes a definition. A unique department condition keeps it from colliding with other tests' definitions;
    /// the requester's employee has no department so we use priority + amount bands instead.</summary>
    private static async Task<JsonElement> PublishAsync(HrmsClient admin, params object[] steps) =>
        await PublishWithAsync(admin, null, steps);

    private static async Task<JsonElement> PublishWithAsync(HrmsClient admin, decimal? minAmount, params object[] steps)
    {
        var res = await admin.Http.PostAsJsonAsync("/api/v1/workflow/definitions", new
        {
            code = TestData.Unique("WF"), name = "Test flow", documentType = DocType, description = (string?)null,
            priority = Random.Shared.Next(1000, int.MaxValue), minAmount, maxAmount = (decimal?)null,
            companyId = (Guid?)null, branchId = (Guid?)null, departmentId = (Guid?)null, steps,
        });
        res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var def = await res.Content.ReadFromJsonAsync<JsonElement>();
        var pub = await admin.Http.PostAsJsonAsync($"/api/v1/workflow/definitions/{def.GetProperty("id").GetGuid()}/publish", new { effectiveFrom = (string?)null });
        pub.StatusCode.ShouldBe(HttpStatusCode.OK, await pub.Content.ReadAsStringAsync());
        return await pub.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task RetireAsync(HrmsClient admin, JsonElement def) =>
        (await admin.Http.PostAsync($"/api/v1/workflow/definitions/{def.GetProperty("id").GetGuid()}/retire", null)).EnsureSuccessStatusCode();

    private static async Task<HttpResponseMessage> StartRawAsync(World w, decimal? amount = null) =>
        await w.Requester.Client.Http.PostAsJsonAsync("/api/v1/workflow/instances", new
        {
            documentType = DocType, documentId = Guid.NewGuid().ToString(), title = "Laptop request", subjectEmployeeId = w.RequesterEmployee, amount,
        });

    private static async Task<JsonElement> StartAsync(World w, decimal? amount = null)
    {
        var r = await StartRawAsync(w, amount);
        r.StatusCode.ShouldBe(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement[]> InboxAsync(Actor a, string status = "open") =>
        (await a.Client.GetJsonAsync($"/api/v1/workitems/mine?status={status}&pageSize=200")).GetProperty("items").EnumerateArray().ToArray();

    private static async Task<JsonElement> ItemForAsync(Actor a, Guid instanceId)
    {
        var items = await InboxAsync(a);
        return items.Single(i => i.GetProperty("workflowInstanceId").GetGuid() == instanceId);
    }

    private static async Task<HttpResponseMessage> CompleteRawAsync(Actor a, Guid itemId, string action, string? comment = null, string? key = null, string? sendBackTo = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/workitems/{itemId}/complete")
        { Content = JsonContent.Create(new { action, comment, sendBackTo }) };
        if (key is not null) req.Headers.Add("Idempotency-Key", key);
        return await a.Client.Http.SendAsync(req);
    }

    private static async Task<JsonElement> CompleteAsync(Actor a, Guid instanceId, string action, string? comment = null, string? sendBackTo = null)
    {
        var item = await ItemForAsync(a, instanceId);
        var r = await CompleteRawAsync(a, item.GetProperty("id").GetGuid(), action, comment, sendBackTo: sendBackTo);
        r.StatusCode.ShouldBe(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Status(JsonElement e) => e.GetProperty("status").GetString()!;

    [Fact]
    public async Task SequentialThenParallelStagesApproveAndEmitHistoryAuditAndOutbox()
    {
        var w = await WorldAsync();
        var def = await PublishAsync(w.Admin,
            Step(1, "Manager", "reporting_manager"),
            Step(2, "HR", "role", w.HrRole),
            Step(2, "Specific", "user", w.Other.UserId.ToString()));
        try
        {
            var inst = await StartAsync(w);
            var id = Id(inst);
            Status(inst).ShouldBe("in_progress");
            inst.GetProperty("tasks").GetArrayLength().ShouldBe(1);
            (await InboxAsync(w.Hr1)).ShouldNotContain(i => i.GetProperty("workflowInstanceId").GetGuid() == id);

            var after1 = await CompleteAsync(w.Manager, id, "approve");
            after1.GetProperty("currentStepOrder").GetInt32().ShouldBe(2);
            // Stage 2: role group (hr1, hr2 — any) + specific user, in parallel.
            after1.GetProperty("tasks").EnumerateArray().Count(t => t.GetProperty("status").GetString() == "pending").ShouldBe(3);

            var afterHr = await CompleteAsync(w.Hr1, id, "approve");
            Status(afterHr).ShouldBe("in_progress");
            (await InboxAsync(w.Hr2)).ShouldNotContain(i => i.GetProperty("workflowInstanceId").GetGuid() == id); // superseded
            var done = await CompleteAsync(w.Other, id, "approve");
            Status(done).ShouldBe("approved");

            var history = (await w.Requester.Client.GetJsonAsync($"/api/v1/workflow/instances/{id}/history")).EnumerateArray()
                .Select(h => h.GetProperty("action").GetString()).ToList();
            history.ShouldContain("start");
            history.Count(a => a == "approve").ShouldBe(3);
            history.ShouldContain("completed");

            await f.DrainAsync();
            await using var conn = new NpgsqlConnection(f.ServerCs(await f.TenantDatabaseAsync("acme")));
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "select count(*) filter (where processed_at is not null), count(*) from workflow.outbox_events where instance_id = @i", conn);
            cmd.Parameters.AddWithValue("i", id);
            await using (var rd = await cmd.ExecuteReaderAsync())
            {
                (await rd.ReadAsync()).ShouldBeTrue();
                rd.GetInt64(1).ShouldBe(2); // started + approved
                rd.GetInt64(0).ShouldBe(2);
            }
            await using var audit = new NpgsqlCommand("select count(*) from audit.audit_entries where entity_id = @d and action like 'workflow.%'", conn);
            audit.Parameters.AddWithValue("d", inst.GetProperty("documentId").GetString()!);
            ((long)(await audit.ExecuteScalarAsync())!).ShouldBeGreaterThanOrEqualTo(4);

            var notes = await w.Requester.Client.GetJsonAsync("/api/v1/notifications?pageSize=50");
            notes.GetRawText().ShouldContain("Request approved");
        }
        finally { await RetireAsync(w.Admin, def); }
    }

    [Fact]
    public async Task AmountThresholdSkipsStepAndSelfApprovalIsPrevented()
    {
        var w = await WorldAsync();
        var def = await PublishAsync(w.Admin,
            Step(1, "Manager", "reporting_manager"),
            Step(2, "Finance", "role", w.HrRole, min: 1000m));
        try
        {
            var small = await StartAsync(w, 500m);
            Status(await CompleteAsync(w.Manager, Id(small), "approve")).ShouldBe("approved");

            var big = await StartAsync(w, 5000m);
            var mid = await CompleteAsync(w.Manager, Id(big), "approve");
            Status(mid).ShouldBe("in_progress");
            mid.GetProperty("currentStepOrder").GetInt32().ShouldBe(2);
        }
        finally { await RetireAsync(w.Admin, def); }

        // A step whose only approver is the requester resolves to nobody → start fails (no self-approval).
        var self = await PublishAsync(w.Admin, Step(1, "Self", "user", w.Requester.UserId.ToString()));
        try
        {
            var r = await StartRawAsync(w);
            r.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await r.Content.ReadAsStringAsync()).ShouldContain("workflow.no_approver");
        }
        finally { await RetireAsync(w.Admin, self); }
    }

    [Fact]
    public async Task SendBackResubmitRejectAndIdempotency()
    {
        var w = await WorldAsync();
        var def = await PublishAsync(w.Admin, Step(1, "Manager", "reporting_manager"));
        try
        {
            var inst = await StartAsync(w);
            var id = Id(inst);
            var item = await ItemForAsync(w.Manager, id);
            (await CompleteRawAsync(w.Manager, Id(item), "send_back")).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // comment required
            var back = await CompleteAsync(w.Manager, id, "send_back", "Attach quote");
            Status(back).ShouldBe("sent_back");
            (await InboxAsync(w.Requester)).ShouldContain(i => i.GetProperty("itemType").GetString() == "workflow.resubmit");

            var resub = await w.Requester.Client.Http.PostAsJsonAsync($"/api/v1/workflow/instances/{id}/resubmit", new { comment = "Added" });
            resub.StatusCode.ShouldBe(HttpStatusCode.OK, await resub.Content.ReadAsStringAsync());
            (await InboxAsync(w.Requester)).ShouldNotContain(i => i.GetProperty("itemType").GetString() == "workflow.resubmit");

            var again = await ItemForAsync(w.Manager, id);
            var key = Guid.NewGuid().ToString();
            var r1 = await CompleteRawAsync(w.Manager, Id(again), "reject", "No budget", key);
            r1.StatusCode.ShouldBe(HttpStatusCode.OK);
            var r1Body = await r1.Content.ReadFromJsonAsync<JsonElement>();
            Status(r1Body).ShouldBe("rejected");

            // Replays through the engine with the same key are no-ops; without a key the closed task conflicts.
            var taskId = r1Body.GetProperty("tasks").EnumerateArray().Last().GetProperty("id").GetGuid();
            using var replay = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/workflow/tasks/{taskId}/actions")
            { Content = JsonContent.Create(new { action = "reject", comment = "No budget" }) };
            replay.Headers.Add("Idempotency-Key", key);
            var r2 = await w.Manager.Client.Http.SendAsync(replay);
            r2.StatusCode.ShouldBe(HttpStatusCode.OK, await r2.Content.ReadAsStringAsync());
            var r3 = await w.Manager.Client.Http.PostAsJsonAsync($"/api/v1/workflow/tasks/{taskId}/actions", new { action = "approve" });
            r3.StatusCode.ShouldBe(HttpStatusCode.Conflict);

            var history = (await w.Requester.Client.GetJsonAsync($"/api/v1/workflow/instances/{id}/history")).EnumerateArray()
                .Select(h => h.GetProperty("action").GetString()).ToList();
            history.Count(a => a == "reject").ShouldBe(1);
            history.ShouldContain("send_back");
            history.ShouldContain("resubmit");
        }
        finally { await RetireAsync(w.Admin, def); }
    }

    [Fact]
    public async Task DelegateClaimEscalateAndCancel()
    {
        var w = await WorldAsync();
        var def = await PublishAsync(w.Admin,
            Step(1, "Manager", "reporting_manager", esc: "user", escParam: w.Hr2.UserId.ToString(), sla: 4),
            Step(2, "HR", "role", w.HrRole));
        try
        {
            var inst = await StartAsync(w);
            var id = Id(inst);
            var item = await ItemForAsync(w.Manager, id);
            // Delegation to the requester is blocked (self-approval).
            var bad = await w.Manager.Client.Http.PostAsJsonAsync($"/api/v1/workitems/{Id(item)}/delegate", new { toUserId = w.Requester.UserId, comment = "x" });
            bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            var del = await w.Manager.Client.Http.PostAsJsonAsync($"/api/v1/workitems/{Id(item)}/delegate", new { toUserId = w.Other.UserId, comment = "OOO" });
            del.StatusCode.ShouldBe(HttpStatusCode.OK, await del.Content.ReadAsStringAsync());
            (await InboxAsync(w.Manager)).ShouldNotContain(i => i.GetProperty("workflowInstanceId").GetGuid() == id);
            var otherItem = await ItemForAsync(w.Other, id);

            // Old work item can no longer be acted on by the original assignee.
            (await CompleteRawAsync(w.Manager, Id(item), "approve")).StatusCode.ShouldBe(HttpStatusCode.Conflict);

            // Manual escalation by the current assignee → configured escalation user.
            var taskId = Guid.Parse(otherItem.GetProperty("sourceRef").GetString()!);
            var esc = await w.Other.Client.Http.PostAsJsonAsync($"/api/v1/workflow/tasks/{taskId}/actions", new { action = "escalate", comment = "Need senior" });
            esc.StatusCode.ShouldBe(HttpStatusCode.OK, await esc.Content.ReadAsStringAsync());
            var escTask = (await esc.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tasks").EnumerateArray().Single();
            escTask.GetProperty("assigneeUserId").GetGuid().ShouldBe(w.Hr2.UserId);
            escTask.GetProperty("escalationLevel").GetInt32().ShouldBe(1);

            await CompleteAsync(w.Hr2, id, "approve");
            // Stage 2 (role, any): hr1 claims; hr2 cannot act while claimed.
            var hr1Item = await ItemForAsync(w.Hr1, id);
            (await w.Hr1.Client.Http.PostAsync($"/api/v1/workitems/{Id(hr1Item)}/claim", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
            var hr2Item = await ItemForAsync(w.Hr2, id);
            hr2Item.GetProperty("claimed").GetBoolean().ShouldBeTrue();
            hr2Item.GetProperty("claimedByMe").GetBoolean().ShouldBeFalse();
            (await CompleteRawAsync(w.Hr2, Id(hr2Item), "approve")).StatusCode.ShouldBe(HttpStatusCode.Conflict);

            // Non-requester without manage cannot cancel; the requester can.
            (await w.Other.Client.Http.PostAsJsonAsync($"/api/v1/workflow/instances/{id}/cancel", new { comment = "x" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            var cancel = await w.Requester.Client.Http.PostAsJsonAsync($"/api/v1/workflow/instances/{id}/cancel", new { comment = "No longer needed" });
            cancel.StatusCode.ShouldBe(HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync());
            Status(await cancel.Content.ReadFromJsonAsync<JsonElement>()).ShouldBe("cancelled");
            (await InboxAsync(w.Hr1)).ShouldNotContain(i => i.GetProperty("workflowInstanceId").GetGuid() == id);
        }
        finally { await RetireAsync(w.Admin, def); }
    }

    [Fact]
    public async Task SlaJobAutoEscalatesOverdueTasks()
    {
        var w = await WorldAsync();
        var def = await PublishAsync(w.Admin, Step(1, "Manager", "reporting_manager", sla: 1, esc: "user", escParam: w.Hr1.UserId.ToString()));
        try
        {
            var inst = await StartAsync(w);
            var id = Id(inst);
            await using (var conn = new NpgsqlConnection(f.ServerCs(await f.TenantDatabaseAsync("acme"))))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand("update workflow.instance_steps set due_at = now() - interval '2 hours' where instance_id = @i", conn);
                cmd.Parameters.AddWithValue("i", id);
                (await cmd.ExecuteNonQueryAsync()).ShouldBe(1);
            }
            using (var scope = f.Factory.Services.CreateScope())
                scope.ServiceProvider.GetRequiredService<IJobScheduler>().EnqueueTenant(f.AcmeId, WorkflowJobTypes.SlaTenant);
            await f.DrainAsync();

            var now = await w.Requester.Client.GetJsonAsync($"/api/v1/workflow/instances/{id}");
            var task = now.GetProperty("tasks").EnumerateArray().Single();
            task.GetProperty("assigneeUserId").GetGuid().ShouldBe(w.Hr1.UserId);
            task.GetProperty("escalationLevel").GetInt32().ShouldBe(1);
            (await InboxAsync(w.Hr1)).ShouldContain(i => i.GetProperty("workflowInstanceId").GetGuid() == id);
            (await InboxAsync(w.Manager)).ShouldNotContain(i => i.GetProperty("workflowInstanceId").GetGuid() == id);
        }
        finally { await RetireAsync(w.Admin, def); }
    }

    [Fact]
    public async Task DefinitionVersioningPublishIsImmutableAndClosesPrior()
    {
        using var admin = await f.AcmeAdminAsync();
        var code = TestData.Unique("WFV");
        object Body(string name) => new
        {
            code, name, documentType = DocType, description = (string?)null, priority = 0, minAmount = 999999999m, maxAmount = (decimal?)null,
            companyId = (Guid?)null, branchId = (Guid?)null, departmentId = (Guid?)null,
            steps = new[] { Step(1, "Manager", "reporting_manager") },
        };
        var v1 = await (await admin.Http.PostAsJsonAsync("/api/v1/workflow/definitions", Body("v1"))).Content.ReadFromJsonAsync<JsonElement>();
        v1.GetProperty("version").GetInt32().ShouldBe(1);
        (await admin.Http.PostAsJsonAsync($"/api/v1/workflow/definitions/{Id(v1)}/publish", new { effectiveFrom = (string?)null })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.Http.PutAsJsonAsync($"/api/v1/workflow/definitions/{Id(v1)}", Body("edit"))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.Http.DeleteAsync($"/api/v1/workflow/definitions/{Id(v1)}")).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var v2 = await (await admin.Http.PostAsJsonAsync("/api/v1/workflow/definitions", Body("v2"))).Content.ReadFromJsonAsync<JsonElement>();
        v2.GetProperty("version").GetInt32().ShouldBe(2);
        var edited = await admin.Http.PutAsJsonAsync($"/api/v1/workflow/definitions/{Id(v2)}", Body("v2-edit"));
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
        (await edited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString().ShouldBe("v2-edit");
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)).ToString("yyyy-MM-dd");
        (await admin.Http.PostAsJsonAsync($"/api/v1/workflow/definitions/{Id(v2)}/publish", new { effectiveFrom = tomorrow })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var v1After = await admin.GetJsonAsync($"/api/v1/workflow/definitions/{Id(v1)}");
        v1After.GetProperty("effectiveTo").GetString().ShouldBe(DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"));

        var bad = await admin.Http.PostAsJsonAsync("/api/v1/workflow/definitions", new
        {
            code = TestData.Unique("WFB"), name = "bad", documentType = "nope.type", description = (string?)null, priority = 0,
            minAmount = (decimal?)null, maxAmount = (decimal?)null, companyId = (Guid?)null, branchId = (Guid?)null, departmentId = (Guid?)null,
            steps = new[] { Step(1, "X", "reporting_manager") },
        });
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var draft = await (await admin.Http.PostAsJsonAsync("/api/v1/workflow/definitions", Body("v3"))).Content.ReadFromJsonAsync<JsonElement>();
        (await admin.Http.DeleteAsync($"/api/v1/workflow/definitions/{Id(draft)}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await RetireAsync(admin, v1);
        await RetireAsync(admin, v2);
    }

    [Fact]
    public async Task PermissionsAreEnforced()
    {
        var w = await WorldAsync();
        var noPerms = await UserAsync(w.Admin);
        // Seeded Manager/Employee system roles get inbox access on fresh provisioning.
        var roles = (await w.Admin.GetJsonAsync("/api/v1/roles")).EnumerateArray().ToList();
        foreach (var sys in new[] { "MANAGER", "EMPLOYEE" })
        {
            var roleId = roles.First(r => r.GetProperty("code").GetString() == sys).GetProperty("id").GetGuid();
            var seeded = await UserAsync(w.Admin, roleId);
            (await seeded.Client.Http.GetAsync("/api/v1/workitems/mine")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        (await noPerms.Client.Http.GetAsync("/api/v1/workitems/mine")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await noPerms.Client.Http.GetAsync("/api/v1/workflow/definitions")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.Requester.Client.Http.GetAsync("/api/v1/workflow/definitions")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.Hr1.Client.Http.PostAsJsonAsync("/api/v1/workflow/instances", new { documentType = DocType, documentId = "x", title = "t" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var viewRole = await RoleAsync(w.Admin, TestData.Unique("WFDV"), P.DefinitionsView);
        var viewer = await UserAsync(w.Admin, viewRole);
        (await viewer.Client.Http.GetAsync("/api/v1/workflow/definitions")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.Client.Http.PostAsJsonAsync("/api/v1/workflow/definitions",
            new { code = "X", name = "X", documentType = DocType, priority = 0, steps = new[] { new { stepOrder = 1, name = "M", approverType = "reporting_manager" } } }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var def = await PublishAsync(w.Admin, Step(1, "Manager", "reporting_manager"));
        try
        {
            var inst = await StartAsync(w);
            var taskId = inst.GetProperty("tasks")[0].GetProperty("id").GetGuid();
            // Delegates must be able to act on approvals.
            (await w.Manager.Client.Http.PostAsJsonAsync($"/api/v1/workflow/tasks/{taskId}/actions", new { action = "delegate", targetUserId = noPerms.UserId }))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            // Module document types can only be started by their owning module.
            (await w.Admin.Http.PostAsJsonAsync("/api/v1/workflow/instances", new { documentType = "leave.request", documentId = Guid.NewGuid().ToString(), title = "t" }))
                .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            // A user with inbox-act but not the assignee cannot act on or view the instance.
            (await w.Other.Client.Http.PostAsJsonAsync($"/api/v1/workflow/tasks/{taskId}/actions", new { action = "approve" }))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await w.Other.Client.Http.GetAsync($"/api/v1/workflow/instances/{Id(inst)}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            // Requester sees only own requests; the admin (workflow.instances.view) sees it in the global list.
            (await w.Other.Client.GetJsonAsync("/api/v1/workflow/instances?mine=true")).GetProperty("items").EnumerateArray()
                .ShouldNotContain(i => Id(i) == Id(inst));
            (await w.Admin.GetJsonAsync($"/api/v1/workflow/instances?pageSize=200&documentType={DocType}")).GetProperty("items").EnumerateArray()
                .ShouldContain(i => Id(i) == Id(inst));
        }
        finally { await RetireAsync(w.Admin, def); }
    }

    [Fact]
    public async Task TenantIsolation()
    {
        var w = await WorldAsync();
        var def = await PublishAsync(w.Admin, Step(1, "Manager", "reporting_manager"));
        try
        {
            var inst = await StartAsync(w);
            var taskId = inst.GetProperty("tasks")[0].GetProperty("id").GetGuid();
            var item = await ItemForAsync(w.Manager, Id(inst));
            using var globex = await f.GlobexAdminAsync();
            (await globex.Http.GetAsync($"/api/v1/workflow/instances/{Id(inst)}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await globex.Http.GetAsync($"/api/v1/workflow/instances/{Id(inst)}/history")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await globex.Http.GetAsync($"/api/v1/workflow/definitions/{Id(def)}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await globex.Http.PostAsJsonAsync($"/api/v1/workflow/tasks/{taskId}/actions", new { action = "approve" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await globex.Http.PostAsJsonAsync($"/api/v1/workitems/{Id(item)}/complete", new { action = "approve" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await globex.GetJsonAsync("/api/v1/workflow/definitions")).EnumerateArray().ShouldNotContain(d => Id(d) == Id(def));
            (await globex.GetJsonAsync("/api/v1/workflow/instances?pageSize=200")).GetProperty("items").EnumerateArray().ShouldNotContain(i => Id(i) == Id(inst));
            (await globex.GetJsonAsync("/api/v1/workitems/mine?status=all&pageSize=200")).GetProperty("items").EnumerateArray()
                .ShouldNotContain(i => Id(i) == Id(item));
        }
        finally { await RetireAsync(w.Admin, def); }
    }
}
