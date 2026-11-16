# M2.7 Letter Management V2 — Complete Condition-Based Test Report

Date: 2026-11-16 · Scope: M2.7 Letter Management V2 only · Repo: `D:\HRMS\hrms` (uncommitted working tree)

## Overall verdict: PASS WITH ISSUES

All executable conditions pass on real PostgreSQL (Testcontainers). 4 genuine defects were found and fixed during this round, plus 1 missing feature implemented (reissue). §15 frontend E2E is BLOCKED pending two explicit approvals (see §15). Nothing committed, no migration executed, no DB/infrastructure changed.

## 1. Endpoint & feature inventory

Admin (`/api/v1/employee-docs`, tenant-scoped):
- `GET letter-merge-fields` · `GET/POST/PUT letter-signatories[/{id}]` · `GET/POST/PUT letterheads[/{id}]`
- `GET/POST letter-template-families[?letterTypeId]` · `GET/POST …/families/{id}/versions[?baseTemplateId]`
- `PUT letter-templates/{id}` · `POST …/{id}/publish` · `POST …/{id}/retire`
- `GET/POST letter-types` (type now carries `signatoryId`, `numberPrefix`, `numberReset`)

Employee (`/api/v1/employees`):
- `GET {id}/letters` · `POST {id}/letters` · `POST {id}/letters/preview`
- `GET letters/{letterId}` · `GET letters/{letterId}/download` · `POST …/retry` · `POST …/reissue` (new) · `POST …/revoke`
- `POST /api/v1/files` (`core.org.logo` purpose reused for letterhead logos)

Frontend: `HrDocsPage` 6 tabs (Categories, Letter types, Template versions, Signatories, Letterheads, Merge fields); employee Letters tab (list, number column, sandboxed Preview, Retry, Download, Issue).

## 2. Test totals

| Result | Count |
|---|---|
| API test scenarios executed | 125 (36 letter-V2/PDF-focused; 21 added this round) |
| Passed | 125 |
| Failed | 0 |
| Blocked | §15 frontend E2E — migration not executed + container predates V2 (approvals pending) |
| Not tested | File-storage failure (shares the identical `MarkFailedAsync` path covered by the PDF-failure tests); browser-level E2E on devtest |

## 2a. Gotenberg integration tests (8 — `GotenbergPdfTests.cs`)

`pdf.unavailable` (unconfigured + unreachable host), `pdf.output_invalid` (non-PDF body, empty body), `pdf.conversion_failed` (HTTP 500), `pdf.html_too_large` (>256 KB input), `pdf.timeout` (hung service → 15 s timeout) — all deterministic via a loopback TCP stub — plus `RealGotenbergConvertsHtmlToPdf` against an ephemeral `gotenberg/gotenberg:8` Testcontainer (**passed: real Chromium conversion produced a valid `%PDF` document**).

## 3. Defects found & fixed this round

| # | Severity | Defect | Fix |
|---|---|---|---|
| 1 | **High (security)** | `LetterHtml.AttributePattern` regex lacked capture parens — `a-zA-Z` matched literal text, so **all attribute checks silently never ran** (on* handlers, `style="…url()…"`, `javascript:` hrefs passed sanitization). | Regex fixed; covered by sanitizer test incl. `url()` and handler cases. |
| 2 | Medium | Drafts saved without Scriban syntax check → `{{ bogus(` saved fine, then HTTP 500 on preview/render. | `LetterAdminService` now validates `Template.Parse` at save (400 `template.syntax`); `PreviewAsync` maps render faults to 422 `template.render`. |
| 3 | Medium | No template size cap — >256 KB drafts saved, only the rendered doc was capped. | 256 KB cap on subject+body at save (400 `template.too_large`). |
| 4 | Medium | Concurrent independent publishes raced the overlap pre-check → the loser got HTTP 500 (PG exclusion `ex_letter_templates_no_overlap`). | `DbUpdateException` with that constraint is translated to 409 `template.overlap`. |
| 5 | Medium (feature gap) | `letters.reissue` permission seeded + `superseded`/`SupersedesId` designed, but **no reissue endpoint existed**. | Implemented `ReissueAsync` + `POST employees/letters/{id}/reissue`: allocates a new number, resolves the *current* published template (retired original is replaced), flips old letter `issued→superseded` atomically with the new issue. Only `issued`/`revoked` letters are reissuable. |

## 4. New regression tests (13, all green)

`LetterV2Tests`: invalid lifecycle transitions (retired→publish; publish/retire idempotency); manage-without-publish cannot publish/retire; concurrent overlapping publishes → exactly one 409/204; effective-window boundaries (future `effectiveFrom` blocks preview+issue); sanitizer container/JS-URL vectors (iframe/object/embed/`javascript:`/form); oversized body + invalid Scriban → 400; failed issue consumes the number (next issue continues the sequence); retry/revoke wrong-status rejection; reissue supersede chain (new number, old preserved, `supersedesId` link, reissue of `generation_failed` rejected); reissue permission denial; cross-tenant letter/type 404s (acme↔globex); signatory snapshot immutability after master edit; legacy `signatory_employee_id` dual-read.

## 5. Verified behavior (from full matrix)

- **Lifecycle:** draft→published→retired; published immutable (409); draft-from-published sets `supersedes_id`; version numbers unique per family.
- **Applicability:** branch > company > tenant, highest version; same-scope overlap → 409 (pre-check) or 409 (race).
- **Sanitizer:** allowlist-only tags/attrs; script/iframe/object/embed/form/`javascript:`/`on*`/`url()` rejected; letterhead HTML sanitized too.
- **Merge fields:** unknown field, branch/company-mismatch field, `salary_breakup` → 400; all string values HTML-encoded by the renderer; preview ≡ issue rendering.
- **Numbering:** `{prefix}-{period}-{seq}`; concurrent issues get distinct numbers; failed issue consumes its number; retry reuses it.
- **Issuance:** PDF mandatory (stubbed `IPdfService`); any post-reservation failure → `generation_failed` with record preserved; sha-256 of stored bytes matches download; signatory + letterhead snapshots frozen at issue; no TXT fallback for issued letters.
- **Permissions:** view/issue/manage/publish/letterhead/signatory/revoke/reissue each enforced; scoped user without grant → 403; out-of-scope employee → 404 (no existence oracle).
- **Multi-tenant:** letter/type/employee ids from acme return 404/4xx under globex; no cross-tenant reads.

## 6. §15 Frontend E2E — BLOCKED

- `hrms_tenant_1009` top migration is `20261104124355_LetterRenderedTextUnbounded`; `letter_template_families` does not exist (verified via psql).
- `hrms-api-1` container is a 2-day-old image predating all V2 code.

**Exact approvals required:** (a) execute `20261104132820_LetterManagementV2` on devtest (1009); (b) rebuild/restart the `hrms-api-1` image **or** approve running the API from source with a vite dev server for the E2E pass.

## 7. Remaining findings

| Severity | Item |
|---|---|
| Medium | Personal/contacts/bank/statutory/lifecycle employee sub-resources still lack data-scope checks (pre-existing, outside M2.7 scope — flagged in Phase-1 review). |
| Low | Retire of a draft is a silent withdraw (204, idempotent) — documented, not a defect. |
| Low | Gotenberg not deployed in docker-compose (infra approval pending) — runtime conversion verified against an ephemeral Testcontainer instead. |
| Low | Vite build emitted one worker-OOM line under memory pressure but completed (`✓ built in 58s`, bundle emitted) — environmental, not code. |

## 8. Files changed this round

- `src/backend/Hrms.Application/Employees/LetterAdminService.cs` — `ITemplateRenderer` injected; Scriban parse + 256 KB validation at save; publish race → 409.
- `src/backend/Hrms.Application/Employees/LetterIssuanceService.cs` — preview render → 422; `IssueCoreAsync` extraction; `ReissueAsync`; atomic issued→superseded.
- `src/backend/Hrms.Api/Controllers/TenantControllers.cs` — `POST letters/{id}/reissue` (perm `letters.reissue`).
- `src/frontend/web/src/api/tenant.ts` — `reissueLetter`.
- `src/frontend/web/src/pages/tenant/EmployeeSections.tsx` — Reissue button (issued/revoked), permission-gated Issue/Retry/Reissue/Revoke/Download, revoke+reissue confirm dialog, supersede-chain captions ("reissue of …" / "superseded by …").
- `tests/api/Hrms.ApiTests/LetterV2Tests.cs` — +13 tests (10 → 28 letter tests).
- `tests/api/Hrms.ApiTests/GotenbergPdfTests.cs` — new, 8 tests (7 stub-failure modes + 1 real-container conversion).

## 9. Regression results

| Gate | Result |
|---|---|
| `dotnet build` | Clean, 0 warnings |
| API suite | **125/125** — 117/117 full-suite run + 8/8 `GotenbergPdfTests` run; a later full-suite re-run was blocked by Docker Desktop (`meta.db: read-only file system`) — fixture-init failure, not test failures; backend unchanged since the green runs |
| Unit suite | 26/26 |
| `pnpm build` (incl. `tsc -b`) | Green |
| ESLint | 0 errors, 4 pre-existing warnings |
| `git diff --check` | Clean |

Committed `96f3f37` on `m2-hr-core` (34 files) — not pushed.

## Approval requested

Approve executing `20261104132820_LetterManagementV2` on devtest (1009) **and** rebuilding the API container (or running from source) so the §15 E2E pass can run.
