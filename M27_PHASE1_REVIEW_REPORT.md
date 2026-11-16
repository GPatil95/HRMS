# M2.7 Phase 1 — Independent Review Report (read-only)

**Overall result: PASS** — approve the diff and the migration for dev tenants 1008/1009. Two Low findings, one Medium gap outside the declared Phase-1 scope (flagged, not fixed).

Reviewed the complete uncommitted diff on `m2-hr-core` plus the generated migration. Nothing executed; migration **not applied**; no code/DB/git changes.

## Files reviewed

| File | Change |
|---|---|
| `src/backend/Hrms.Application/Employees/EmployeeDocumentService.cs` | +IDataScopeService, `RequireEmployeeScopeAsync`, scope on all doc/letter ops |
| `src/backend/Hrms.Application/Employees/SalaryService.cs` | same gate on all employee-bound salary ops |
| `src/backend/Hrms.Application/Files/FileService.cs` | owner-scope on `LoadAuthorizedAsync` (employee-owned files) |
| `src/backend/Hrms.Api/Controllers/TenantControllers.cs` | Result returns + sanitized download filename |
| `src/backend/Hrms.Infrastructure/Persistence/TenantDbContext.cs` | `RenderedText` → `HasColumnType("text")` |
| `TenantDbContextModelSnapshot.cs` | 3-line diff: `varchar(256)` → `text`, nothing else |
| `Persistence/Migrations/Tenant/20261104124355_LetterRenderedTextUnbounded.*` | new, reviewed below |
| `tests/api/Hrms.ApiTests/EmployeeDocumentTests.cs`, `EmployeeSalaryTests.cs` | 4 new tests + ctor updates |

## 1. Authorization coverage — verified

- **Owner resolution before authz:** `GetLetterAsync`/`RevokeAsync` fetch `letter.EmployeeId` then scope-check (`EmployeeDocumentService.cs:379-437`); `ApproveAsync`/`RejectAsync` fetch `a.EmployeeId` (`SalaryService.cs`); employee-owned files resolve `OwnerEntityId` stamped at upload (`EmployeeDocumentService.cs:~121`, `FileService.cs:146-151`).
- **Every endpoint covered:** list/upload/verify/missing documents, list/issue/get/download/revoke letters — all employee-bound ops gated. Controller routes unchanged; authorization now added at service level.
- **Scope semantics:** Own → 404 on other employees (asserted on 12 endpoints in the new test); DirectReports → reports only (asserted); Tenant → unchanged behavior (grant ceiling respected — grants can't exceed role ceiling).
- **Cross-tenant:** letter/file IDs don't exist in the other tenant's DB → `letter.not_found`/`file-not-found` (asserted with a globex login).
- **No existence oracle:** out-of-scope and nonexistent both return 404 `employee.not_found`/`letter.not_found`/`file-not-found` — consistent with the `OrganizationService` convention.
- **No alternate download path:** the only employee-file download route is `files/{id}/content` → `LoadAuthorizedAsync` → covered (asserted in test). `documents/{docId}/download` doesn't exist; no photo endpoints exist.
- **Internal callers:** `LifecycleJobs` invokes only unscoped methods (`ExpireDueDocumentsAsync`, `ActivateDueAssignmentsAsync`) — no bypass, no null-scope crash path (`LifecycleJobs.cs:25-26`). DI lifetimes consistent (`DependencyInjection.cs:213-218`, all scoped).
- **Non-employee files unaffected:** owner check only fires when `OwnerEntityType == "employee"` **and** `OwnerEntityId` parses as GUID — org/company/branch/tenant documents unchanged (file isolation tests still pass).

## 2. Migration `20261104124355_LetterRenderedTextUnbounded` — safe

- `Up`: `ALTER COLUMN hr.employee_letters.rendered_text → text` — one column, nothing else.
- `Down`: reverts to `varchar(256)`. If any row exceeds 256 chars at rollback time, Postgres **fails loudly** — no silent truncation. Acceptable.
- Snapshot matches the model exactly (3-line diff).
- `HasColumnType("text")` correctly wins over the global `CommonModel.ApplyConventions` 256 default (convention skips explicitly-typed columns — `CommonModel.cs:99`).
- No existing migration modified; the earlier empty scaffold was removed and regenerated.
- **Ready for dev tenants 1008/1009** (and any future new tenant). Safe operation: `text` is a widening change, no data rewrite needed.

## 3. Test quality — real behavior, real Postgres

All four tests run through the live API against a real PostgreSQL container (Testcontainers fixture), with real permission/scope evaluation and real persistence:

| Test | What it exercises |
|---|---|
| `LongLetterContentRoundTrips` | ~4,900-char template body **persisted and read back through the API** — real DB round-trip, not in-memory. Also asserts sanitized `Content-Disposition` filename. *Caveat: green because tenants run the migrated schema; on an unmigrated tenant DB the >256 write would fail — expected, migration pending.* |
| `DocumentAndLetterEndpointsEnforceDataScope` | Own-scope user denied 404 on 12 endpoints incl. generic `files/{id}/content`; own record still accessible |
| `DirectReportsScopeSeesOnlyReports` | reports visible, unrelated employee 404 |
| `LetterFromAnotherTenantIsNotFound` | acme letter 404 from globex |

Coverage gaps: no test for `documents/{docId}` verify on nonexistent doc ID ordering (minor), and employee-bound files uploaded by paths other than `EmployeeDocumentService` aren't exercised (none exist today).

## 4. Regression risks — low

- **Contracts:** response shapes unchanged (same DTOs; Result wraps only add proper 404s). Frontend already renders error states on 404 (prior M2 fix) — out-of-scope employee → error card, not a broken empty list.
- **Salary:** still gated by permission + now scope; approve/reject resolve owner first; confidential data protected.
- **Behavior change (intended):** a scoped user now gets 404 on other employees' doc/letter/salary pages instead of leaked data. HR admins (Tenant scope) unaffected.

## Findings

| Severity | Finding |
|---|---|
| Medium | Personal/contacts/bank/statutory/history/lifecycle employee sub-resources remain unscoped (same bug class, outside this phase). Recommend a follow-up fix or a route-inventory test that fails CI on uncovered employee routes — already called out in the Phase-1 report. |
| Low | `FileService` owner-scope relies on `OwnerEntityType == "employee"` being stamped at upload. Today only `EmployeeDocumentService.UploadAsync` does this; a future upload path that skips the stamp silently bypasses scope. Mitigation: convention test or a `FilePurpose`-level owner requirement. |
| Low | `Down` migration fails loudly if long content exists — correct, just note it in rollback runbooks. |

**Required fixes before approval: none.** Migration ready for tenants 1008/1009 on your approval.
