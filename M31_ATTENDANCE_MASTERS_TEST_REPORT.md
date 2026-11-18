# M3.1 — Attendance Masters: Test Report

Date: 2026-11-18 · Branch: `m3-attendance` (based on merged `main` @ `47503bc`, PR #2)

## Scope delivered

Attendance configuration masters under a dedicated `att` schema, per the approved
M3 design (Rev 3). No punch processing, no calculation engine, no regularization, no payroll.

- **Domain**: `Shift`/`ShiftVersion`, `WorkCalendar`/`CalendarDay`, `AttendancePolicy`/`PolicyVersion`,
  `ShiftAssignment`, `PolicyAssignment`, `AttendanceRoster` — all `[Audited("attendance")]`,
  concurrency tokens on editable roots.
- **EF/migration**: 9 tables in `att`, check constraints, `UNIQUE(calendar_id,date)`,
  `UNIQUE(employee_id,work_date)`, gist exclusion constraints on all four versioned/assignment
  windows (draft versions exempt via partial `WHERE status='published'`). Migration
  `20261117123034_AttendanceMasters` generated, not applied to dev databases.
- **Permissions** (seeded via module catalogue): `attendance.masters.view`, `.manage`
  (sensitive), `.backdate` (sensitive), `attendance.assignments.manage` (scoped).
- **API** `/api/v1/attendance`: shifts+versions+publish/retire, calendars+bulk day upsert+delete,
  policies+versions+publish/retire, shift/policy assignments (create, end), roster bulk upsert,
  `GET config/resolve`, `GET config/resolve-range`, `POST masters/impact-preview`.
- **Resolver**: org as-of via `EmploymentHistory` → branch timezone → shift (roster →
  assignment → org DefaultShiftName chain) → published version → policy (assignment →
  dept/branch/company code → default) → calendar → day-type precedence
  (roster_off > working_override > holiday/optional/half_day > weekly_off > working) →
  planned instants in UTC (overnight + DST-safe).
- **Frontend**: `/attendance/admin` — Shifts, Calendars, Policies, Assignments (incl. roster),
  Config Resolver tabs; menu gated by `attendance` module entitlement + permissions.
- **Infra**: `att` added to tenant grant schema list (`PostgresTenantDatabaseProvisioner.Schemas`)
  — required for the app role to access the new schema; additive only.

## Test results

| Suite | Result |
|---|---|
| Unit (`Hrms.UnitTests`) | **33/33 pass** (7 new `AttendanceRulesTests`) |
| API (`Hrms.ApiTests`, Testcontainers) | **136/136 pass** (11 new `AttendanceMastersTests`) |
| Frontend `tsc -b` | 0 errors |
| Frontend ESLint | 0 errors (4 pre-existing warnings in unrelated files) |
| Frontend `pnpm build` | success (2m57s) |
| `git diff --check` | clean |

### New unit tests (7 — `AttendanceRulesTests`)
Defaults valid; null rules → `$` path; enum/range violations → exact json paths
(`punch.mode`, `late.count_from`, `overtime.*`, `payable_days_basis`, …); per-index penalty
validation; absent≤half-day threshold; `ParseValidated` bad-JSON/schema/valid paths; JSON round-trip.

### New API tests (11 — `AttendanceMastersTests`)
- `PublishClosesPriorVersionAndRejectsOverlap` — publish closes prior window (`effective_to = from-1`); backfill before a later published version → 409.
- `PublishedVersionIsImmutableDraftEditable` — PUT on published → 409; draft editable.
- `BackdateRequiresDedicatedPermission` — `masters.manage`-only user backdated publish → 403, non-backdated → 200; admin (with `masters.backdate`) backdated → 200.
- `PolicyRulesValidationReturnsFieldPaths` — invalid rules → 400 with `errors` keyed by field path; defaults-valid rules publish fine.
- `ShiftAssignmentsClosePriorAndRejectSameStart` — same-start → 409; later start closes prior; `end` colliding with next assignment → 409, within window → 204.
- `AssignmentScopeFiltersListForScopedUser` — `Own`-scoped user sees empty list.
- `RosterBulkUpsertAndOffDays` — 2 upserts counted, off-day round-trips.
- `ResolverPrecedenceAndOvernightInstants` — working day resolves assignment shift; 22:00 IST → 16:30Z start, 06:00 IST+1 → 00:30Z end (overnight); Sunday → `weekly_off` (branch `fixed` pattern); holiday → `holiday`; `working_override` honored; rostered-off beats all.
- `EntitlementOffDeniesModule` — module disabled → 403; enabled → 200.
- `CrossTenantWritesReturnNotFound` — globex cannot read/update acme's shift (404).
- `AuditEntriesWrittenAndDataStaysInTenantDb` — `Shift` create audited; row present in acme DB, absent in globex DB.

## Issues found and fixed during testing

1. **Tenant grants** — `att` schema missing from `PostgresTenantDatabaseProvisioner.Schemas`
   → `42501 permission denied for schema att`. Added; grants are per-schema additive.
2. **`end` assignment 500** — extending `effective_to` past a later assignment's start
   hit the exclusion constraint as an unhandled error. `EndShiftAssignmentAsync` /
   `EndPolicyAssignmentAsync` now pre-check and return 409 `assignment.overlap`.
3. Test fixes: non-unique branch code (`branch-code-taken` in full-suite ordering),
   204-vs-200 assertions on `end`/draft-update endpoints.

## Not covered / deferred

- Migration application to devtest (1009) — needs separate approval (backup → dry-run → apply → verify).
- `shift_based` weekly-off resolution path and `RotationRule` boundary dates are implemented
  in the resolver but covered only indirectly (existing `WeeklyOffRules` is exercised through
  the `fixed` pattern); dedicated rotation cases land with M3.2 punch/day work.
- Frontend e2e needs your authenticated devtest session.

## Boundaries kept

No changes to M1/M2 behaviour (single additive line in grant schema list). No punches,
no day computation, no jobs, no requests/approvals, no periods. Masters are versioned +
effective-dated; published versions immutable; every write audited.
