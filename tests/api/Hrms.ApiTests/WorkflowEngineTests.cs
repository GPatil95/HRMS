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

    private sealed record Actor(Guid UserId, HrmsClient Client, string Email = "");

    private sealed record World(HrmsClient Admin, Actor Requester, Actor Manager, Actor Hr1, Actor Hr2, Actor Other, string HrRole, Guid RequesterEmployee);

    private async Task<Guid> RoleAsync(HrmsClient admin, string code, params string[] perms) =>
        await admin.PostForIdAsync("/api/v1/roles", new
        {
            code, name = code, description = "wf test",
            permissions = perms.Select(p => new { permissionCode = p, scopeLevel = "Tenant" }).ToArray(),
        });

    private async Task<Actor> UserAsync(HrmsClient admin, params Guid[] roles)
    {
        var email = $"wf-{Guid.NewGuid():N}@acme.test";
        var (id, client) = await f.CreateTenantUserAsync(admin, "acme", email, roles);
        return new Actor(id, client, email);
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
        int? sla = null, string esc = "none", string? escParam = null, string scope = "tenant") => new
    {
        stepOrder = order, name, approverType = type, approverParam = param, approvalMode = mode, minAmount = min, maxAmount = (decimal?)null,
        fallbackUserId = (Guid?)null, slaHours = sla, escalationType = esc, escalationParam = escParam, allowSendBack = true, allowDelegate = true,
        approverScope = scope,
    };

    private static async Task AssignRoleAsync(HrmsClient admin, Guid userId, Guid roleId, string? targetKind = null, Guid? targetId = null)
    {
        var r = await admin.Http.PostAsJsonAsync($"/api/v1/users/{userId}/roles", new
        {
            roleId,
            targets = targetKind is null ? Array.Empty<object>() : new object[] { new { kind = targetKind, targetId } },
            validFrom = (DateOnly?)null, validTo = (DateOnly?)null,
        });
        r.IsSuccessStatusCode.ShouldBeTrue(await r.Content.ReadAsStringAsync());
    }

    private static Guid[] Assignees(JsonElement inst) =>
        inst.GetProperty("tasks").EnumerateArray().Where(t => t.GetProperty("status").GetString() == "pending")
            .Select(t => t.GetProperty("assigneeUserId").GetGuid()).ToArray();

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

        // A step whose only approver is the requester resolves to nobody → the request stalls (no self-approval, no skip).
        var self = await PublishAsync(w.Admin, Step(1, "Self", "user", w.Requester.UserId.ToString()));
        try
        {
            var inst = await StartAsync(w);
            Status(inst).ShouldBe("stalled");
            inst.GetProperty("tasks").GetArrayLength().ShouldBe(0);
        }
        finally { await RetireAsync(w.Admin, self); }
    }

    [Fact]
    public async Task MissingApproverStallsNotifiesAdminsAndResumes()
    {
        var w = await WorldAsync();
        var def = await PublishAsync(w.Admin, Step(1, "Manager", "reporting_manager"), Step(2, "Nobody", "role", TestData.Unique("NOROLE")));
        try
        {
            var inst = await StartAsync(w);
            var after1 = await CompleteAsync(w.Manager, Id(inst), "approve");
            Status(after1).ShouldBe("stalled");
            after1.GetProperty("currentStepOrder").GetInt32().ShouldBe(2);
            var history = await w.Admin.GetJsonAsync($"/api/v1/workflow/instances/{Id(inst)}/history");
            history.EnumerateArray().Select(h => h.GetProperty("action").GetString()).ShouldContain("stalled");
            (await w.Admin.GetJsonAsync("/api/v1/notifications?pageSize=50")).GetRawText().ShouldContain("Workflow stalled (no approver)");
            (await w.Admin.GetJsonAsync("/api/v1/workflow/instances?status=stalled&pageSize=200")).GetProperty("items").EnumerateArray()
                .ShouldContain(i => Id(i) == Id(inst));

            // A workflow manager without inbox or instance-view permissions still finds the request and can pick themselves.
            var wfAdmin = await UserAsync(w.Admin, await RoleAsync(w.Admin, TestData.Unique("WFMG"), P.InstancesManage));
            (await wfAdmin.Client.GetJsonAsync("/api/v1/workflow/instances/stalled?pageSize=200")).GetProperty("items").EnumerateArray()
                .ShouldContain(i => Id(i) == Id(inst));
            (await w.Requester.Client.Http.GetAsync("/api/v1/workflow/instances/stalled")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await wfAdmin.Client.GetJsonAsync($"/api/v1/workflow/eligible-assignees?search={Uri.EscapeDataString(w.Other.Email)}"))
                .GetProperty("items").EnumerateArray().Select(Id).ShouldBe([w.Other.UserId]);
            (await w.Admin.GetJsonAsync("/api/v1/workflow/eligible-assignees?search=admin@acme.test")).GetProperty("totalCount").GetInt32().ShouldBe(1);
            (await w.Requester.Client.Http.GetAsync("/api/v1/workflow/eligible-assignees")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

            // Only workflow administrators can resume; without a resolvable approver or an assignee it stays stalled.
            (await w.Requester.Client.Http.PostAsJsonAsync($"/api/v1/workflow/instances/{Id(inst)}/resume", new { }))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            var still = await w.Admin.Http.PostAsJsonAsync($"/api/v1/workflow/instances/{Id(inst)}/resume", new { comment = "retry" });
            still.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            Status(await w.Admin.GetJsonAsync($"/api/v1/workflow/instances/{Id(inst)}")).ShouldBe("stalled");
            (await w.Admin.Http.PostAsJsonAsync($"/api/v1/workflow/instances/{Id(inst)}/resume", new { assigneeUserId = w.Requester.UserId }))
                .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

            var resumed = await w.Admin.Http.PostAsJsonAsync($"/api/v1/workflow/instances/{Id(inst)}/resume", new { assigneeUserId = w.Other.UserId, comment = "Covering" });
            resumed.StatusCode.ShouldBe(HttpStatusCode.OK, await resumed.Content.ReadAsStringAsync());
            var body = await resumed.Content.ReadFromJsonAsync<JsonElement>();
            Status(body).ShouldBe("in_progress");
            Assignees(body).ShouldBe([w.Other.UserId]);
            Status(await CompleteAsync(w.Other, Id(inst), "approve")).ShouldBe("approved");
            (await w.Admin.GetJsonAsync($"/api/v1/workflow/instances/{Id(inst)}/history")).EnumerateArray()
                .Select(h => h.GetProperty("action").GetString()).ShouldContain("resumed");
        }
        finally { await RetireAsync(w.Admin, def); }
    }

    [Fact]
    public async Task RoleApproversAreResolvedWithinStepScope()
    {
        var w = await WorldAsync();
        var role = TestData.Unique("WFSC");
        var roleId = await RoleAsync(w.Admin, role, P.WorkItemsView, P.WorkItemsAct);
        var reqBranch = (await w.Admin.GetJsonAsync($"/api/v1/employees/{w.RequesterEmployee}")).GetProperty("branchId").GetGuid();
        var company = await w.Admin.PostForIdAsync("/api/v1/org/companies", new { code = TestData.Unique("C"), name = "Co2", legalName = (string?)null });
        var otherBranch = await w.Admin.PostForIdAsync("/api/v1/org/branches", new { companyId = company, code = TestData.Unique("B"), name = "Other" });

        var inBranch = await UserAsync(w.Admin);       // assignment targeted at the requester's branch
        var outBranch = await UserAsync(w.Admin);      // assignment targeted at another branch
        var homeBranch = await UserAsync(w.Admin);     // untargeted assignment, own employee record in the requester's branch
        var homeElsewhere = await UserAsync(w.Admin);  // untargeted assignment, own employee record elsewhere
        await AssignRoleAsync(w.Admin, inBranch.UserId, roleId, "Branch", reqBranch);
        await AssignRoleAsync(w.Admin, outBranch.UserId, roleId, "Branch", otherBranch);
        await AssignRoleAsync(w.Admin, homeBranch.UserId, roleId);
        await AssignRoleAsync(w.Admin, homeElsewhere.UserId, roleId);
        await AssignRoleAsync(w.Admin, w.Manager.UserId, roleId, "Branch", otherBranch);
        var homeEmp = await w.Admin.PostForIdAsync("/api/v1/employees", new { employeeCode = TestData.Unique("E"), displayName = "Home", workEmail = (string?)null, branchId = reqBranch, departmentId = (Guid?)null, managerEmployeeId = (Guid?)null });
        var elsewhereEmp = await w.Admin.PostForIdAsync("/api/v1/employees", new { employeeCode = TestData.Unique("E"), displayName = "Away", workEmail = (string?)null, branchId = otherBranch, departmentId = (Guid?)null, managerEmployeeId = (Guid?)null });
        await LinkAsync(w.Admin, homeEmp, homeBranch.UserId);
        await LinkAsync(w.Admin, elsewhereEmp, homeElsewhere.UserId);

        var branchDef = await PublishAsync(w.Admin, Step(1, "Branch HR", "role", role, mode: "all", scope: "branch"));
        try
        {
            Assignees(await StartAsync(w)).OrderBy(x => x).ShouldBe(new[] { inBranch.UserId, homeBranch.UserId }.OrderBy(x => x));
        }
        finally { await RetireAsync(w.Admin, branchDef); }

        var tenantDef = await PublishAsync(w.Admin, Step(1, "Any HR", "role", role, mode: "all"));
        try
        {
            Assignees(await StartAsync(w)).Length.ShouldBe(5);
        }
        finally { await RetireAsync(w.Admin, tenantDef); }

        // Department scope: only holders targeted at (or employed in) the subject's department; out-of-department holders cannot act.
        var deptA = await w.Admin.PostForIdAsync("/api/v1/org/departments", new { code = TestData.Unique("D"), name = "DeptA", branchId = reqBranch });
        var deptB = await w.Admin.PostForIdAsync("/api/v1/org/departments", new { code = TestData.Unique("D"), name = "DeptB", branchId = reqBranch });
        var deptRole = TestData.Unique("WFDP");
        var deptRoleId = await RoleAsync(w.Admin, deptRole, P.WorkItemsView, P.WorkItemsAct);
        var inDept = await UserAsync(w.Admin);
        var outDept = await UserAsync(w.Admin);
        await AssignRoleAsync(w.Admin, inDept.UserId, deptRoleId, "Department", deptA);
        await AssignRoleAsync(w.Admin, outDept.UserId, deptRoleId, "Department", deptB);
        var deptSubject = await w.Admin.PostForIdAsync("/api/v1/employees", new { employeeCode = TestData.Unique("E"), displayName = "DeptSubject", workEmail = (string?)null, branchId = reqBranch, departmentId = deptA, managerEmployeeId = (Guid?)null });
        var deptDef = await PublishAsync(w.Admin, Step(1, "Dept HR", "role", deptRole, scope: "department"));
        try
        {
            var r = await w.Requester.Client.Http.PostAsJsonAsync("/api/v1/workflow/instances", new
            {
                documentType = DocType, documentId = Guid.NewGuid().ToString(), title = "Dept request", subjectEmployeeId = deptSubject, amount = (decimal?)null,
            });
            r.StatusCode.ShouldBe(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
            var inst = await r.Content.ReadFromJsonAsync<JsonElement>();
            Assignees(inst).ShouldBe([inDept.UserId]);
            var taskId = inst.GetProperty("tasks")[0].GetProperty("id").GetGuid();
            (await outDept.Client.Http.PostAsJsonAsync($"/api/v1/workflow/tasks/{taskId}/actions", new { action = "approve" }))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await InboxAsync(outDept)).ShouldBeEmpty();
        }
        finally { await RetireAsync(w.Admin, deptDef); }

        // Cross-branch: the out-of-branch holder cannot act on a branch-scoped task either.
        var crossDef = await PublishAsync(w.Admin, Step(1, "Branch HR", "role", role, scope: "branch"));
        try
        {
            var inst = await StartAsync(w);
            foreach (var t in inst.GetProperty("tasks").EnumerateArray())
                (await outBranch.Client.Http.PostAsJsonAsync($"/api/v1/workflow/tasks/{t.GetProperty("id").GetGuid()}/actions", new { action = "approve" }))
                    .StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await InboxAsync(outBranch)).ShouldNotContain(i => i.GetProperty("workflowInstanceId").GetGuid() == Id(inst));
        }
        finally { await RetireAsync(w.Admin, crossDef); }

        // Reporting scope: only role holders in the subject's reporting line (the manager), whatever their assignment targets.
        var reportingDef = await PublishAsync(w.Admin, Step(1, "Line HR", "role", role, scope: "reporting"));
        try
        {
            Assignees(await StartAsync(w)).ShouldBe([w.Manager.UserId]);
        }
        finally { await RetireAsync(w.Admin, reportingDef); }

        // Holders exist but none is in scope → stalled rather than routed to an out-of-scope approver.
        var outRole = TestData.Unique("WFOUT");
        var outRoleId = await RoleAsync(w.Admin, outRole, P.WorkItemsView, P.WorkItemsAct);
        await AssignRoleAsync(w.Admin, outBranch.UserId, outRoleId, "Branch", otherBranch);
        var outDef = await PublishAsync(w.Admin, Step(1, "Out HR", "role", outRole, scope: "branch"));
        try
        {
            Status(await StartAsync(w)).ShouldBe("stalled");
        }
        finally { await RetireAsync(w.Admin, outDef); }

        var bad = await w.Admin.Http.PostAsJsonAsync("/api/v1/workflow/definitions", new
        {
            code = TestData.Unique("WFX"), name = "bad", documentType = DocType, description = (string?)null, priority = 0,
            minAmount = (decimal?)null, maxAmount = (decimal?)null, companyId = (Guid?)null, branchId = (Guid?)null, departmentId = (Guid?)null,
            steps = new[] { Step(1, "M", "reporting_manager", scope: "branch") },
        });
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EligibleDelegateLookupIsSearchablePaginatedAndRestricted()
    {
        var w = await WorldAsync();
        var noPerms = await UserAsync(w.Admin);
        (await noPerms.Client.Http.GetAsync("/api/v1/workitems/eligible-delegates")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        async Task<JsonElement> Lookup(string search, int page = 1, int pageSize = 20) =>
            await w.Manager.Client.GetJsonAsync($"/api/v1/workitems/eligible-delegates?search={Uri.EscapeDataString(search)}&page={page}&pageSize={pageSize}");
        Guid[] Ids(JsonElement r) => r.GetProperty("items").EnumerateArray().Select(Id).ToArray();

        Ids(await Lookup(w.Other.Email)).ShouldBe([w.Other.UserId]);
        Ids(await Lookup(w.Other.Email.ToUpperInvariant())).ShouldBe([w.Other.UserId]);
        Ids(await Lookup(noPerms.Email)).ShouldBeEmpty();      // no workitems.inbox.act
        Ids(await Lookup(w.Manager.Email)).ShouldBeEmpty();    // never the caller

        var p1 = await Lookup("@acme.test", 1, 1);
        var p2 = await Lookup("@acme.test", 2, 1);
        p1.GetProperty("totalCount").GetInt32().ShouldBeGreaterThanOrEqualTo(2);
        Ids(p1).Length.ShouldBe(1);
        Ids(p2).Length.ShouldBe(1);
        Ids(p1)[0].ShouldNotBe(Ids(p2)[0]);
        (await Lookup("", 1, 500)).GetProperty("pageSize").GetInt32().ShouldBe(50);
        var first = (await Lookup(w.Other.Email)).GetProperty("items")[0];
        first.GetProperty("displayName").GetString().ShouldNotBeNullOrEmpty();
        first.TryGetProperty("email", out _).ShouldBeTrue();
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

    private async Task SqlAsync(string sql, params (string Name, object Value)[] args)
    {
        await using var conn = new NpgsqlConnection(f.ServerCs(await f.TenantDatabaseAsync("acme")));
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task RunSlaJobAsync()
    {
        using (var scope = f.Factory.Services.CreateScope())
            scope.ServiceProvider.GetRequiredService<IJobScheduler>().EnqueueTenant(f.AcmeId, WorkflowJobTypes.SlaTenant);
        await f.DrainAsync();
    }

    private static string[] Actions(JsonElement history) =>
        history.EnumerateArray().Select(h => h.GetProperty("action").GetString()!).ToArray();

    [Fact]
    public async Task StandingDelegationsHonourEligibilityWindowAndRevocation()
    {
        var w = await WorldAsync();
        var viewOnly = await UserAsync(w.Admin, await RoleAsync(w.Admin, TestData.Unique("WFVO"), P.WorkItemsView));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        async Task<HttpResponseMessage> Delegate(Guid to, DateOnly from, DateOnly until) =>
            await w.Manager.Client.Http.PostAsJsonAsync("/api/v1/workflow/delegations", new { delegateUserId = to, validFrom = from, validTo = until, reason = "Leave" });
        Task Sql(string sql, Guid id) => SqlAsync(sql, ("u", w.Other.UserId), ("d", id));

        // Invalid delegations are rejected: no act permission, self, past window.
        (await Delegate(viewOnly.UserId, today, today.AddDays(3))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Delegate(w.Manager.UserId, today, today.AddDays(3))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Delegate(w.Other.UserId, today.AddDays(-5), today.AddDays(-1))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var def = await PublishAsync(w.Admin, Step(1, "Manager", "reporting_manager"));
        try
        {
            // Active delegation routes new tasks to the delegate.
            var ok = await Delegate(w.Other.UserId, today, today.AddDays(3));
            ok.StatusCode.ShouldBe(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
            var delegationId = Id(await ok.Content.ReadFromJsonAsync<JsonElement>());
            var viaDelegation = await StartAsync(w);
            Assignees(viaDelegation).ShouldBe([w.Other.UserId]);

            // The delegate gains the task only — no workflow administration or visibility beyond it.
            (await w.Other.Client.Http.GetAsync("/api/v1/workflow/definitions")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await w.Other.Client.Http.PostAsJsonAsync($"/api/v1/workflow/instances/{Id(viaDelegation)}/resume", new { }))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await w.Other.Client.GetJsonAsync("/api/v1/workflow/instances?pageSize=200")).GetProperty("items").EnumerateArray()
                .ShouldNotContain(i => Id(i) == Id(viaDelegation));

            // A delegate who loses the act permission no longer receives tasks.
            await Sql("update identity.user_role_assignments set valid_to = current_date - 1, valid_from = current_date - 2 where user_id = @u", delegationId);
            Assignees(await StartAsync(w)).ShouldBe([w.Manager.UserId]);
            await Sql("update identity.user_role_assignments set valid_to = null where user_id = @u", delegationId);

            // Expired delegation: new tasks skip it, the delegate cannot act on tasks it routed, and the SLA job returns them.
            await Sql("update workflow.delegations set valid_from = current_date - 10, valid_to = current_date - 1 where id = @d", delegationId);
            Assignees(await StartAsync(w)).ShouldBe([w.Manager.UserId]);
            var otherTask = await ItemForAsync(w.Other, Id(viaDelegation));
            (await CompleteRawAsync(w.Other, Id(otherTask), "approve")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
            await RunSlaJobAsync();
            (await InboxAsync(w.Other)).ShouldNotContain(i => i.GetProperty("workflowInstanceId").GetGuid() == Id(viaDelegation));
            Status(await CompleteAsync(w.Manager, Id(viaDelegation), "approve")).ShouldBe("approved");

            // Revoked delegation: pending tasks return to the delegator immediately and new tasks skip it.
            await Sql("update workflow.delegations set valid_from = current_date, valid_to = current_date + 3 where id = @d", delegationId);
            var beforeRevoke = await StartAsync(w);
            Assignees(beforeRevoke).ShouldBe([w.Other.UserId]);
            (await w.Manager.Client.Http.DeleteAsync($"/api/v1/workflow/delegations/{delegationId}")).IsSuccessStatusCode.ShouldBeTrue();
            Assignees(await w.Admin.GetJsonAsync($"/api/v1/workflow/instances/{Id(beforeRevoke)}")).ShouldBe([w.Manager.UserId]);
            (await InboxAsync(w.Other)).ShouldNotContain(i => i.GetProperty("workflowInstanceId").GetGuid() == Id(beforeRevoke));
            Assignees(await StartAsync(w)).ShouldBe([w.Manager.UserId]);
            (await w.Manager.Client.GetJsonAsync("/api/v1/workflow/delegations")).EnumerateArray()
                .Single(d => Id(d) == delegationId).GetProperty("isActive").GetBoolean().ShouldBeFalse();
            Actions(await w.Admin.GetJsonAsync($"/api/v1/workflow/instances/{Id(beforeRevoke)}/history")).Count(a => a == "assigned").ShouldBe(2);
        }
        finally { await RetireAsync(w.Admin, def); }
    }

    [Fact]
    public async Task SendBackAndResubmitStallWithoutApproverAndResumeKeepsHistory()
    {
        var w = await WorldAsync();
        var def = await PublishAsync(w.Admin, Step(1, "Manager", "reporting_manager"), Step(2, "HR", "role", w.HrRole));
        try
        {
            var sentBackToPrevious = await StartAsync(w);
            await CompleteAsync(w.Manager, Id(sentBackToPrevious), "approve");
            var resubmitted = await StartAsync(w);
            await CompleteAsync(w.Manager, Id(resubmitted), "send_back", "Attach the quote");

            // The manager becomes unavailable: stage 1 has no approver any more.
            await SqlAsync("update identity.users set status = 0 where id = @u", ("u", w.Manager.UserId));

            var back = await CompleteAsync(w.Hr1, Id(sentBackToPrevious), "send_back", "Manager must re-check", sendBackTo: "previous");
            Status(back).ShouldBe("stalled");
            Assignees(back).ShouldBeEmpty();
            var res = await w.Requester.Client.Http.PostAsJsonAsync($"/api/v1/workflow/instances/{Id(resubmitted)}/resubmit", new { comment = "Quote attached" });
            res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
            Status(await res.Content.ReadFromJsonAsync<JsonElement>()).ShouldBe("stalled");

            // Stalled requests are never auto-approved or auto-progressed by the SLA sweep.
            await RunSlaJobAsync();
            foreach (var id in new[] { Id(sentBackToPrevious), Id(resubmitted) })
            {
                var now = await w.Admin.GetJsonAsync($"/api/v1/workflow/instances/{id}");
                Status(now).ShouldBe("stalled");
                Assignees(now).ShouldBeEmpty();
            }

            // Correction (manager reactivated) + resume re-resolves the original approver; history is preserved in order.
            await SqlAsync("update identity.users set status = 1 where id = @u", ("u", w.Manager.UserId));
            var resumed = await w.Admin.Http.PostAsJsonAsync($"/api/v1/workflow/instances/{Id(sentBackToPrevious)}/resume", new { comment = "Manager back" });
            resumed.StatusCode.ShouldBe(HttpStatusCode.OK, await resumed.Content.ReadAsStringAsync());
            Assignees(await resumed.Content.ReadFromJsonAsync<JsonElement>()).ShouldBe([w.Manager.UserId]);
            var actions = Actions(await w.Admin.GetJsonAsync($"/api/v1/workflow/instances/{Id(sentBackToPrevious)}/history"));
            var order = new[] { "approve", "send_back", "stalled", "resumed" }.Select(a => Array.IndexOf(actions, a)).ToArray();
            order.ShouldAllBe(i => i >= 0);
            order.ShouldBeInOrder();
        }
        finally
        {
            await SqlAsync("update identity.users set status = 1 where id = @u", ("u", w.Manager.UserId));
            await RetireAsync(w.Admin, def);
        }
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
