# Workstream B — core.workflow + core.workitems sign-off

Branch `core-workflow` (base `main` @ 5517b3d). Tenant DB only, additive.

## Tables (migration `20261119111229_CoreWorkflow`, generated, not applied)

| Table | Purpose |
|---|---|
| `workflow.definitions` | Versioned definitions (`code`+`version` unique), draft → published (immutable) → retired; selection conditions: document type, effective dates, amount range, company/branch/department, priority. GiST exclusion `ex_definitions_published_no_overlap` stops two published versions of one code overlapping in time. |
| `workflow.definition_steps` | Ordered steps. Steps sharing `step_order` form a parallel stage (`approval_mode` any/all). Approver type + param, amount band, fallback user, SLA hours, escalation, allowed actions. |
| `workflow.instances` | One per document submission, pinned to a definition version; status, current stage, `xmin` row version. Partial unique index: one active instance per document. |
| `workflow.instance_steps` | Tasks (assignee, delegated-from, claimed-by, due, escalation level, outcome), optimistic concurrency. |
| `workflow.actions` | Append-only action history (actor, on-behalf-of, target, comment, from/to stage); unique idempotency key. |
| `workflow.delegations` | Standing out-of-office delegations; GiST exclusion `ex_delegations_no_overlap` per delegator. |
| `workflow.outbox_events` | Transactional outbox for instance state changes, dispatched to `IWorkflowEventHandler`s by a Hangfire job. |
| `workitems.work_items` | Non-authoritative inbox projection (doc 19.2 columns); unique per item type + source + assignee. |

## Endpoints (`/api/v1`)

| Method | Path | Permission |
|---|---|---|
| GET | `workflow/document-types` | `workflow.definitions.view` |
| GET | `workflow/definitions`, `workflow/definitions/{id}` | `workflow.definitions.view` |
| POST / PUT / DELETE | `workflow/definitions`, `workflow/definitions/{id}` | `workflow.definitions.manage` |
| POST | `workflow/definitions/{id}/publish`, `/retire` | `workflow.definitions.manage` |
| POST | `workflow/instances` | `workflow.instances.start` |
| GET | `workflow/instances` (own; all with `workflow.instances.view`), `workflow/instances/{id}`, `/history` | `workitems.inbox.view` |
| POST | `workflow/instances/{id}/cancel`, `/resubmit` | `workitems.inbox.view` (requester / `workflow.instances.manage`) |
| POST | `workflow/tasks/{taskId}/actions` (approve, reject, send_back, delegate, escalate, claim, comment) | `workitems.inbox.act` |
| GET / POST / DELETE | `workflow/delegations`, `workflow/delegations/{id}` | `workflow.delegations.manage` |
| GET | `workitems/mine` | `workitems.inbox.view` |
| POST | `workitems/{id}/claim`, `/complete`, `/delegate` | `workitems.inbox.act` |

All POST actions accept an `Idempotency-Key` header. Manager and Employee system roles are granted inbox view/act + delegations.

## Contracts

`IWorkflowEngine`, `IWorkItemService`, `IWorkItemPublisher`, `IWorkflowDocumentTypeProvider`, `IWorkflowEventHandler` (Hrms.Application/Workflow/Contracts.cs, XML-documented). Built-in document types: `workflow.generic_request`, `leave.request`, `attendance.regularization`, `expense.claim`, `payroll.run`, `exit.separation`, `fnf.settlement`.

## Frontend

`/my-work` (inbox with status/module/search/overdue filters, claim, approve, reject, send back, delegate, history; "My requests" tab with withdraw/resubmit) and `/workflow/definitions` (draft editor with steps, publish, retire, delete).

## Tests

- New: `WorkflowEngineTests` — 8 API tests (sequential + parallel stages, reporting manager / role / user resolution, amount thresholds, self-approval prevention, send-back + resubmit, reject, idempotency, delegation, claim conflicts, manual + SLA escalation, definition versioning/immutability, permissions, tenant isolation, audit/outbox/notification emission).
- Full suite: 33 unit + 147 API tests pass (includes the 8 new tests). `dotnet build -c Release` has 0 warnings and 0 errors. `pnpm typecheck`, `pnpm lint` (no errors; the 4 warnings were already there) and `pnpm build` all pass.

## Deviations from doc 19 / limitations

- Definitions are stored relationally (`definitions` + `definition_steps`), not as a versioned JSON blob. This gives constraints and indexes and supports the same draft/publish/immutable lifecycle.
- Conditions are fixed columns (amount, company, branch, department, effective dates). The sandboxed expression evaluator that doc 19 shares with payroll formulas does not exist yet.
- Parallel approval supports any/all only. N-of-M, user groups, branch head and module custom resolvers are not built yet. The reporting-manager level comes from `approver_param` (via `reporting_closure`).
- No reminders, auto-approve/auto-reject policies, definition maker-checker, or SignalR count push.
- Engine authorization checks assignee, delegate or claimer. Modules must still re-check document permission and scope in their own APIs, as doc 19.2 requires.
- `PostgresTenantDatabaseProvisioner.Schemas` gets `workflow` and `workitems` so the tenant app role can use them.
