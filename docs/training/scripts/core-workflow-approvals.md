# TA-04 — Approval Workflows & My Work (Video Script)

Status: **Script ready — not recorded** · Version: 1.0 · 2026-11-23
Audience: Tenant Admin / workflow administrator, HR Admin, Managers and approvers
Objective: after this video the viewer can build and publish a scoped approval
chain, act on tasks in My Work, delegate safely, and resume a stalled request.
Prerequisites: tenant with `core.workflow` + `core.workitems`; org masters,
employees linked to users, reporting lines; a role for HR approvers with
branch targets; Workstream B build deployed.
Related manual: [workflow/workflow-approvals.md](../../workflow/workflow-approvals.md)
Estimated duration: ~10 minutes.

## Demo data required

| Item | Value |
|---|---|
| Tenant | `docsdemo` (or any demo tenant) |
| Admin | `admin@docsdemo.test` |
| Requesters | Arjun Shah (HQ, reports to Priya Nair), Kavya Rao (HQ, **no manager**) |
| Approvers | Priya Nair (manager), Meera Iyer (`HR_APPROVER` → HQ), Rohan Das (`HR_APPROVER` → Bengaluru) |
| Definition | `ASSET_REQ`: stage 1 reporting manager, stage 2 role `HR_APPROVER`, scope *Same branch* |

## Demonstration sequence & narration

**0:00–0:40 — Intro.** Show My Work and Workflow definitions side by side.
*Narration:* "Every approval in the product, whether leave, expenses, payroll
or exit, runs on this one engine. Today we build a chain, approve through it,
and see what happens when nobody can approve."
(Manual §1)

**0:40–2:30 — Build the definition.** Workflow definitions → New definition.
Code `ASSET_REQ`, document type *Generic request*. Step 1 *Reporting manager*.
Step 2 *Role* `HR_APPROVER`, **Scope: Same branch**, SLA 48 h. Save draft →
Publish. **Show:** the Scope column is disabled for the manager step.
*Narration:* "Scope stops a branch HR admin from approving another branch's
requests. Whole tenant is for truly tenant-wide approvers only."
(Manual §4, §5.1)

**2:30–3:30 — Raise a request.** Sign in as Arjun → My Work → (API or module
form) raise "Laptop replacement". **Show:** My requests → *in progress*.
(Manual §5.4)

**3:30–4:45 — Approve as manager.** Sign in as Priya → My Work. Open
History, then Approve with a comment.
*Narration:* "Stage 1 done. Stage 2 is the branch HR approver."
(Manual §5.3)

**4:45–5:45 — Scope in action.** Sign in as Meera (HQ): the task is in her
inbox. Sign in as Rohan (Bengaluru): **his inbox is empty.** Open History as
Meera to show who was assigned.
*Narration:* "Same role, different branch, no task. That's the scope rule."
(Manual §6 A)

**5:45–7:00 — Delegate.** As Priya, on another request click Delegate, type
"ka". **Show:** only eligible people appear, with no user-ID box. Pick Kavya Rao.
*Narration:* "You can only hand work to someone who is allowed to act. The
server checks again when you confirm, and the delegate gets this task and
nothing else."
(Manual §5.3, §7)

**7:00–8:45 — Stalled request.** Sign in as Kavya → raise "Ergonomic chair".
**Show:** My requests → *Waiting for an administrator*. Sign in as admin →
bell notification "Workflow stalled (no approver)" → Workflow definitions →
**Stalled requests** tab → Resume → search "pri" → Priya Nair → Resume.
**Show:** History lists `stalled` then `resumed`.
*Narration:* "No approver never means approved. The request waits, the admin is
told, and the fix is recorded in history."
(Manual §5.2, §6 B)

**8:45–10:00 — Wrap.** Recap: scoped role steps, stall → resume, eligible-only
delegation, revoked/expired delegations return tasks to the owner.
(Manual §6 D, §10)

## Expected results checklist (for the trainee)

- [ ] Definition published with a *Same branch* role step
- [ ] Out-of-branch role holder receives no task
- [ ] Delegate list shows only eligible users; no raw ID entry
- [ ] Request with no approver shows **stalled**; admin notified
- [ ] Resume with an assignee returns it to *in progress*; history intact
