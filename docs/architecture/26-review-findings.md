# 26. Architecture Review Findings (final baseline)

Review of the initial foundation plan against the 44-section review brief. Nothing useful from the earlier documents was removed. Sections whose content changed now start with a "Final baseline note" or end with an "Amendment" section.

## 26.1 What was already correct (kept as is)

| Area | Where |
|---|---|
| One application + one platform DB + one DB per tenant; no shared business DB; no per-tenant codebase | 03, 04, ADR-003 |
| Modular monolith, Contracts-only module references, schema + DbContext per module | 01, 02, ADR-001/004 |
| Server-side tenant resolution (verified host + signed `tid`), immutable TenantContext, per-tenant DB roles, `meta.tenant_identity` check | 04, ADR-005/006 |
| Separate Platform Admin and Tenant realms; no implicit tenant data access; time-bound, approved, dual-audited support grants | 05, 09, ADR-009/010 |
| Fail-closed pipeline: auth → tenant → subscription → module → feature → permission → scope | 06, ADR-012 |
| Permission codes, role grants with scope, user overrides with deny-wins | 06, ADR-011 |
| Audit framework (same-transaction interceptor, append-only, hash chain, WORM) | 07, ADR-014 |
| Outbox, Hangfire jobs with tenant filter, file service with scan-before-use, API versioning, observability, CI/CD with migration waves | 08, 11, ADR-015..021 |
| Immutability of finalized payroll, rule-pack snapshots, effective dating | 03 §7.1, ADR-020 |
| Mandatory proof tests (tenant isolation, payroll permission, branch scope, disabled module) | 10 |

## 26.2 What changed (decisions refined, not reversed)

| # | Change | Reason | Where |
|---|---|---|---|
| CH1 | Added action **`configure`**; menu → sub-menu → page hierarchy made explicit | brief §6 | 06 §10.2, ADR-011 amendment |
| CH2 | Added scope level **`company`** (separate from `tenant`) | brief §7 "Entire company" vs "Entire tenant" | 06 §10.3 |
| CH3 | **Grant ceiling rule** formalised: covers scopes, role assignment and assignment targets (before, it only covered "cannot grant what you don't hold") | brief §6 | 06 §10.5 |
| CH4 | Dependency model formalised: **Hard / Optional / Integration / None** + full matrix; module codes finalised (`payroll.tax`, `attendance.biometric`, `employee.documents/letters/exit`) | brief §5 | 14, ADR-013/029 |
| CH5 | `core.employee` changed from "minimal skeleton" to **full HR Core module** (still always on). The foundation phase builds only the skeleton. | brief §8 | 15, ADR-030 |
| CH6 | Shared masters moved into `core.org`: office/building/floor, holiday calendars and weekly offs, business partners (clients/vendors) | avoids optional↔optional coupling | 13, 14, ADR-029 |
| CH7 | New core services: **`core.workitems`** (unified inbox) and **`core.reporting`** | brief §31, §34 | 19, 20, ADR-035 |
| CH8 | Workflow engine: finalised actions (Send Back, Delegate, Escalate, Cancel), condition fields and resolvers | brief §21 | 19, ADR-027 amendment |
| CH9 | Roadmap re-sequenced. The foundation phase now includes email, workflow, inbox, reporting, dashboards, retention and a platform billing MVP. The business module order is defined. | brief §43–44 | 12 |
| CH10 | Platform DB extended with billing, AI, incidents, access reviews and backup evidence. The tenant DB schema map now covers all modules. | brief §2, §37, §40 | 03 §6.1/§7.2 |

## 26.3 What was missing (now added)

Platform admin capability → permission catalogue and tenant limits (13) · Tenant admin capability map and org model (13) · Full HR Core data model (15) · Employee documents (15) · Letter management (15) · **Exit & FNF** with lock/revision and access revocation (16) · Attendance, Biometric connector, Leave, Payroll lifecycle, Tax, Loans, Expense (17) · Recruitment, PMS, Calendar, Rooms, Workplace, Visitors, Mailroom, Assets, Projects, My Work (18) · Central email incl. tenant SMTP, bulk/scheduled, delivery status (19) · Unified work inbox (19) · Dashboard visibility rule and reporting framework with scheduled reports (20) · Complete import pipeline (20) · **AI assistant architecture** (21) · **Two billing domains** (22) · Threat model incl. mass assignment, SSRF in PDF rendering, AI, backups (23) · Compliance controls: access reviews, legal hold, erasure, incidents, change management (24) · Tenant-level restore, DR tiers, deployment topology (24) · Consolidated API surfaces (25) · Tests T9–T19 (10).

## 26.4 Conflicts discovered and how they were resolved

| # | Conflict | Resolution | Approval needed? |
|---|---|---|---|
| C1 | Brief names DBs `HRMS_Tenant_1001`, but PostgreSQL folds unquoted identifiers to lowercase | physical `hrms_tenant_1001`; mixed-case shown as display name (ADR-037) | **Yes (D1)** |
| C2 | "Salary history" sits in the Employee module, but salary structures belong to Payroll | Employee owns CTC-level compensation history. Payroll owns the component breakdown and writes revisions through the Employee contract. One history, and it works without Payroll. | No (documented) |
| C3 | Comp-off appears in both Attendance and Leave | Attendance records earned comp-off and Leave owns the balance and its use. Without Leave, comp-off is report-only. | **Yes (D4)** |
| C4 | WFH appears in both Attendance and Workplace | Attendance owns WFH requests and status. Workplace only plans hybrid office days. | No |
| C5 | Holiday calendar and weekly off appear in both Attendance and Leave | moved to `core.org` work calendars | No |
| C6 | A client master is needed by Projects and Client Billing, and vendors by Assets | shared `core.org` business partners | No |
| C7 | Expense "leave reimbursement" vs leave encashment | interpreted as LTA/leave-travel claims. Encashment stays in Leave/Payroll/FNF. | **Yes (D5)** |
| C8 | FNF needs payroll calculations but must work without Payroll | delegates to `IPayrollCalculator`/`ITdsCalculator` when entitled, otherwise uses manual lines with the same lock and revision flow | No |
| C9 | "Dedicated Tax module" vs payroll TDS | Tax is a sub-module with a hard dependency on Payroll. Payroll without Tax takes manual/imported TDS. | **Yes (D3)** |
| C10 | "Manage platform/tenant permissions" vs permissions defined in code | admins manage role↔permission mapping, overrides and scopes. Permission **codes** come from module manifests because enforcement needs code. | No (clarified) |
| C11 | Platform dashboards in the same framework as tenant dashboards | same framework, separate widget registry in the platform realm, no tenant business data | No |
| C12 | "Office configuration" spans several modules | offices and floors live in `core.org`; zones and seats in `workplace`; rooms in `rooms`. Each is shown only when entitled. | No |
| C13 | "Users send HRMS emails directly" vs abuse and data leakage | recipients limited to the sender's scope, quotas, template/attachment permission checks, audit | No |
| C14 | Visitor ID/photo and watchlist vs privacy law | masked IDs, short retention, consent, watchlist feature off by default | **Yes (D16)** |
| C15 | Aadhaar listed in the employee master | store masked/reference only, pending legal sign-off | **Yes (D11)** |
| C16 | Tenant domain example `abc.hrms.com` plus mobile without subdomain | `api.hrms.com` with the tenant taken from `tid` only (already in 04). Recorded here because the brief does not mention mobile. | No |

## 26.5 Architectural risks

See [12 §21.1](12-roadmap-and-risks.md#211-risks). New this review: R15 scope breadth, R16 AI, R17 workflow complexity, R18 biometric vendor diversity, R19 email deliverability/abuse, R20 GST/e-invoicing, R21 dashboard/report performance, R22 calendar sync.

## 26.6 Decisions still requiring approval

OQ1–OQ10 and D1–D16 in [12 §21.3](12-roadmap-and-risks.md#213-decisions-still-requiring-approval). Highest priority before Phase 1: **OQ1 (cloud), OQ9 (identity), D1 (DB naming), OQ10 (repo), OQ6 (container platform)**.

## 26.7 Recommended implementation order

Foundation phases 0–7, then business modules in this order: HR Core → Documents/Letters → Attendance (+Biometric) → Leave → Payroll + Tax → Loans, Expense → Exit/FNF → Recruitment, PMS → Assets → Calendar/Rooms/Workplace/Visitors/Mailroom → Projects/My Work → Client Billing → AI Assistant. The platform billing MVP lands in Phase 7, before the first paying tenant. Details in [12 §20](12-roadmap-and-risks.md).

## 26.8 Internal consistency checks performed

- Every module in the catalogue (14) has an owner document (15–22) and a schema in the tenant schema map (03 §7.2).
- Every optional (O) cell in the matrix has a named contract and fallback (14 §14.4).
- No optional→optional hard dependency exists. The only hard dependencies outside core are sub-module → parent (Biometric→Attendance, Tax→Payroll, Documents/Letters/Exit→HR Core).
- Every surface that exposes data (API, widgets, reports, exports, imports, work inbox, notifications, AI tools, scheduled jobs) uses the same entitlement → permission → scope evaluation (06 §10.6).
- Every financial result (payroll, FNF, tax, invoices) follows ADR-031 (immutable + revisions).
