# 14. Module Catalogue and Dependency Model

## 14.1 Dependency classes

| Class | Meaning | Technical mechanism | Enable/disable rule |
|---|---|---|---|
| **Hard (H)** | Module cannot function without the provider | direct use of provider `.Contracts`; FKs allowed only to `core.*` | Platform refuses to enable consumer unless provider enabled; disabling provider requires disabling consumer (UI shows impact) |
| **Optional / soft (O)** | Consumer uses provider data if entitled; otherwise falls back to manual/imported input | `IOptionalIntegration<T>` from provider `.Contracts`; fallback implementation in consumer | Independent; settings that depend on provider are hidden/ignored when provider disabled |
| **Integration (I)** | Loosely coupled reaction to events or contribution to a shared surface (work inbox, calendar, dashboard, documents) | integration events via outbox; shared core surfaces (`core.workitems`, `core.calendar-feed`, `core.files`) | Independent; handlers skip when consumer not entitled; no runtime calls |
| **None (—)** | No relationship allowed | architecture test forbids reference | — |

Rules (unchanged from §1.6, now formalised):
1. Hard dependencies may target only `core.*` or a parent module (sub-module → parent).
2. An optional module never hard-depends on another optional module.
3. Payroll results, FNF, invoices etc. snapshot the optional inputs they consumed, so later disabling a provider never changes historical results.
4. The dependency graph is declared in module manifests, validated at startup and in CI, and mirrored into `catalog.module_dependencies`.

## 14.2 Module catalogue

| Code | Name | Kind | Sold as | DB schema |
|---|---|---|---|---|
| `core.identity` | Users, roles, permissions, auth | Core | always included | `identity` |
| `core.org` | Company, branch, department, designation, grade, location/office/floor, cost centre, work calendars (holidays, weekly off), business partners (clients/vendors) | Core | always included | `org` |
| `core.employee` | **Employee / HR Core** (full master, effective-dated history, reporting hierarchy, custom fields) | Core | always included | `employee` |
| `core.workflow` | Approval workflow engine | Core | always included | `workflow` |
| `core.workitems` | Unified work inbox (pending approvals/tasks projection) | Core | always included | `workitems` |
| `core.notify` | Notifications, email, push, SMS point, templates | Core | always included (volume limits by plan) | `notify` |
| `core.files` | File/document storage service | Core | always included (storage limit by plan) | `files` |
| `core.audit` | Audit & security events | Core | always included | `audit` |
| `core.config` | Tenant settings, feature flags, number series, compliance (retention, legal hold, export) | Core | always included | `config` |
| `core.io` | Import/export engine | Core | always included | `io` |
| `core.reporting` | Report framework, scheduled reports | Core | always included | `reporting` |
| `core.dashboard` | Dashboard framework & widgets host | Core | always included | `dash` |
| `employee.documents` | Employee document management | Sub-module of core.employee | included in base plan by default; toggleable | `empdocs` |
| `employee.letters` | Letter management | Sub-module | add-on or base (commercial decision) | `letters` |
| `employee.exit` | Exit management + FNF | Sub-module | included in base plan by default; toggleable | `exit` |
| `attendance` | Attendance, shifts, rosters, regularisation, OD, WFH | Optional | module | `attendance` |
| `attendance.biometric` | Biometric device integration | Sub-module of attendance | add-on | `biometric` |
| `leave` | Leave management | Optional | module | `leave` |
| `payroll` | Payroll engine | Optional | module | `payroll` |
| `payroll.tax` | Income tax projection / TDS / declarations | Sub-module of payroll | included with payroll or add-on | `tax` |
| `loans` | Loan & salary advance | Optional | module | `loans` |
| `expense` | Expense & reimbursement | Optional | module | `expense` |
| `recruitment` | Recruitment / ATS | Optional | module | `recruitment` |
| `pms` | Performance management | Optional | module | `pms` |
| `assets` | Asset management | Optional | module | `assets` |
| `projects` | Project management & timesheets | Optional | module | `projects` |
| `mywork` | My Work: personal tasks, checklists, daily updates, follow-ups | Optional | module | `mywork` |
| `calendar` | Calendar & meetings | Optional | module | `calendar` |
| `rooms` | Meeting room booking | Optional | module | `rooms` |
| `workplace` | Zones/seats/desks, hot-desking, hybrid schedules | Optional | module | `workplace` |
| `visitors` | Reception / visitor management | Optional | module | `visitors` |
| `mailroom` | Mail / parcel management | Optional | module | `mailroom` |
| `clientbilling` | Client billing / invoice management (tenant's own customers) | Optional | module | `clientbilling` |
| `ai.assistant` | Personal AI assistant | Optional premium add-on | add-on (AI plan) | `ai` |

Shared masters moved into core to avoid optional↔optional coupling: **Office/Building/Floor** (used by rooms, workplace, visitors, mailroom), **holiday calendars & weekly offs** (attendance, leave, payroll, workplace), **business partners** (clients for projects & client billing, vendors for assets & expense).

## 14.3 Dependency matrix

Rows = consumer, columns = provider. **H** hard · **O** optional/soft · **I** integration (events / shared surface) · **—** none. Every module has **H** on `core.*` (omitted).

| Consumer ↓ / Provider → | ATT | BIO | LEV | PAY | TAX | LOAN | EXP | REC | PMS | AST | PRJ | MYW | CAL | ROOM | WRK | VIS | MAIL | DOCS | LTR | EXIT | CBILL | AI |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **Attendance (ATT)** | · | O | O | — | — | — | — | — | — | — | — | — | — | — | I | — | — | — | — | I | — | — |
| **Biometric (BIO)** | **H** | · | — | — | — | — | — | — | — | — | — | — | — | — | — | — | — | — | — | I | — | — |
| **Leave (LEV)** | O | — | · | — | — | — | — | — | — | — | — | — | I | — | — | — | — | — | — | I | — | — |
| **Payroll (PAY)** | O | — | O | · | O | O | O | — | — | — | — | — | — | — | — | — | — | — | — | I | — | — |
| **Tax** | — | — | — | **H** | · | — | O | — | — | — | — | — | — | — | — | — | — | O | — | — | — | — |
| **Loans** | — | — | — | O | — | · | — | — | — | — | — | — | — | — | — | — | — | — | — | I | — | — |
| **Expense** | — | — | O | O | — | — | · | — | — | — | O | — | — | — | — | — | — | — | — | I | — | — |
| **Recruitment** | — | — | — | — | — | — | — | · | — | — | — | — | O | — | — | — | — | O | O | — | — | — |
| **PMS** | — | — | — | — | — | — | — | — | · | — | O | — | — | — | — | — | — | — | O | — | — | — |
| **Assets** | — | — | — | — | — | — | — | — | — | · | — | — | — | — | — | — | — | — | — | I | — | — |
| **Projects** | — | — | — | — | — | — | — | — | — | — | · | I | O | — | — | — | — | — | — | — | — | — |
| **My Work** | — | — | — | — | — | — | — | I | I | — | I | · | O | — | — | — | — | — | — | — | — | — |
| **Calendar** | I | — | I | — | — | — | — | I | I | — | I | — | · | O | — | — | — | — | — | — | — | — |
| **Rooms** | — | — | — | — | — | — | — | — | — | — | — | — | O | · | — | — | — | — | — | — | — | — |
| **Workplace** | O | — | O | — | — | — | — | — | — | — | — | — | — | — | · | — | — | — | — | — | — | — |
| **Visitors** | — | — | — | — | — | — | — | — | — | — | — | — | — | O | — | · | — | — | — | — | — | — |
| **Mailroom** | — | — | — | — | — | — | — | — | — | — | — | — | — | — | — | — | · | — | — | — | — | — |
| **Documents** | — | — | — | — | — | — | — | — | — | — | — | — | — | — | — | — | — | · | — | — | — | — |
| **Letters** | — | — | — | O | — | — | — | O | — | — | — | — | — | — | — | — | — | O | · | O | — | — |
| **Exit / FNF** | O | O | O | O | O | O | O | — | — | O | — | — | — | — | — | — | — | O | O | · | — | — |
| **Client billing** | O | — | — | O | — | — | — | — | — | — | O | — | — | — | — | — | — | — | — | — | · | — |
| **AI assistant** | O | — | O | O | O | O | O | O | O | O | O | O | O | O | O | O | O | O | O | O | O | · |

Reading examples (the brief's cases):

| Case | Result |
|---|---|
| Payroll → Employee | **H** (core, always present) |
| Payroll → Attendance / Leave / Loans / Expense | **O** — paid days, LOP, EMI deductions, reimbursements consumed if entitled; otherwise payroll admin enters/imports them (`payroll.inputs` tables) |
| Payroll → Recruitment / PMS / Visitors / Workplace | **—** (forbidden by architecture test) |
| Leave disabled | Payroll uses manual LOP inputs; Attendance shows leave days as absent/regularisation; nothing else affected |
| Recruitment disabled | Employee module unaffected (employees created directly or via import) |
| Biometric disabled | Attendance sources `web`, `mobile`, `manual`, `import` continue |
| AI disabled | No AI endpoints, jobs or UI; rest of HRMS unaffected (AI is a pure consumer) |
| Payroll disabled while Tax enabled | not possible: Tax has **H** on Payroll; disabling Payroll prompts to disable Tax |

## 14.4 Optional-integration contracts (initial list)

| Provider contract | Methods (illustrative) | Consumers | Fallback when provider not entitled |
|---|---|---|---|
| `IAttendanceSummaryProvider` | `GetPaidDays(period, employees)`, `GetOvertime(...)`, `GetLastWorkingDayAttendance(...)` | Payroll, Exit/FNF, Client billing, Workplace | `payroll.inputs` manual/imported values |
| `ILeaveLedgerProvider` | `GetLopDays`, `GetEncashableBalance`, `GetLeavesInRange` | Payroll, Exit/FNF, Attendance, Workplace, Expense (LTA eligibility) | manual inputs |
| `ILoanRecoveryProvider` | `GetDueInstalments(period)`, `GetOutstanding(employee)`, `RecordRecovery(...)` | Payroll, Exit/FNF | manual deductions |
| `IReimbursementPayoutProvider` | `GetApprovedForPayroll(period)`, `MarkPaid(...)` | Payroll, Exit/FNF, Tax (exempt components) | manual earnings |
| `IPayrollCalculator` | `CalculateFinalSettlement(...)`, `GetCtcBreakup(...)`, `GetSalaryVariables(...)` | Exit/FNF, Letters, Client billing | manual FNF salary lines / CTC from Employee core compensation history |
| `ITdsCalculator` | `ProjectAnnualTax(...)`, `CalculateMonthlyTds(...)`, `CalculateFinalTds(...)` | Payroll, Exit/FNF | manual TDS input |
| `IAssetClearanceProvider` | `GetAllocatedAssets(employee)`, `GetRecoveryAmounts(...)` | Exit/FNF | manual clearance checklist line |
| `IBiometricAccessProvider` | `RevokeEmployeeAccess(employee)` | Exit | manual "revoke at device" clearance item |
| `IDocumentProvider` / `ILetterGenerator` | attach/generate | Recruitment, Exit, PMS, Tax (proofs) | uses plain `core.files` attachment / no letter generation |
| `ICalendarProvider` | `CreateEvent`, `GetFreeBusy` | Recruitment (interviews), Rooms, Projects, My Work | no calendar event; booking/interview still saved |
| `IRoomBookingProvider` | `Reserve`, `Release` | Calendar, Visitors | meetings without room |
| `ITimesheetProvider` | `GetBillableHours(client, period)` | Client billing, PMS, Expense | manual billable quantities |
