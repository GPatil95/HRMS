# M2.7 Phase 2 — Design Decisions (authoritative)

Design finalization for Letter Management V2. Incorporates all Phase-2 review refinements. No implementation performed.

## 1. Template versioning & families

**Model:** `letter_template_families` groups versions; `letter_templates` stays one row per version (current behavior) and gains lifecycle columns.

```
letter_template_families: id, letter_type_id (FK), name, created_at
letter_templates: id, family_id (FK, cascade), letter_type_id (FK), version_number int,
  status varchar(20) -- 'draft' | 'published' | 'retired'
  subject, body_html text, body_text (kept for plain fallback/compat),
  effective_from date NOT NULL, effective_to date NULL (null = unbounded),
  company_id uuid NULL, branch_id uuid NULL, letterhead_id uuid NULL,
  supersedes_id uuid NULL (FK -> letter_templates),
  published_at, published_by, retired_at, retired_by, created_at, created_by, modified_at, modified_by
```

Rules:
- `UNIQUE (family_id, version_number)`.
- **Published rows immutable:** service rejects any mutation of a published/retired row (enforced in service; DB guard optional via `modified_*` audit). Editing a published template = `POST /families/{id}/drafts` → new `draft` row, `version_number = max+1`, `supersedes_id` = source version.
- Publish transition: draft → published (validates: Scriban parses, merge fields valid, sanitized HTML, effective dates coherent, no overlap at same applicability level).
- Retire: published → retired (sets `retired_at`; excluded from resolution immediately regardless of dates).
- Only `status='published'` rows participate in resolution and the exclusion constraint.

## 2. Effective-date model

- `effective_from` (required) + `effective_to` (nullable) are `date` columns — **date semantics, not timestamps**. Boundaries: `[from, to)` — `effective_to` is exclusive; `null` = open-ended.
- Evaluation uses **tenant timezone** at issue time (`IClock` → tenant tz date): active iff `from <= issue_date AND (to IS NULL OR issue_date < to)`.
- Date-only storage avoids UTC/tz ambiguity in constraint math — dates are unambiguous per tenant.
- **Ownership validation:** `branch_id` set ⇒ `company_id` must equal that branch's company; both must exist; service validates on save.

**Exclusion constraint** (raw SQL in migration — no fluent EF API):

```sql
ALTER TABLE hr.letter_templates
ADD CONSTRAINT ex_letter_templates_no_overlap
EXCLUDE USING gist (
  letter_type_id               WITH =,
  coalesce(company_id, '00000000-0000-0000-0000-000000000000'::uuid) WITH =,
  coalesce(branch_id,   '00000000-0000-0000-0000-000000000000'::uuid) WITH =,
  daterange(effective_from, coalesce(effective_to, 'infinity'::date), '[)') WITH &&
) WHERE (status = 'published');
```

Semantics: two published templates of the same type at the **same applicability level** (same company, same branch — NULLs equalized by COALESCE) may not have overlapping windows. Different levels (tenant-wide vs company vs branch) coexist — ambiguity is resolved by precedence, not by exclusion. (Requirement: `btree_gist` — already a required tenant extension.) Service also pre-checks overlaps for a friendly error; the constraint is the race-safe guarantee.

**Resolution precedence** at issue/preview: `(type, branch+company)` → `(type, company)` → `(type, tenant)` → `template.missing` error. Ties at same level: highest `version_number`.

## 3. Letter numbering

```
letter_counters: scope_key varchar(120) PK  -- e.g. 'letter.appointment:2026'
  last_number bigint NOT NULL, prefix varchar(40), created_at, updated_at
letter_types: + number_prefix varchar(40) NULL (default 'LTR'), number_reset varchar(10) default 'yearly' ('never'|'yearly')
employee_letters: + letter_number varchar(50) NOT NULL, UNIQUE
```

- **Atomic allocation:** `INSERT INTO letter_counters ... ON CONFLICT (scope_key) DO UPDATE SET last_number = letter_counters.last_number + 1 RETURNING last_number;` — single statement, row-locked, atomic.
- Format: `{prefix}-{period}-{seq:000000}` e.g. `APPT-2026-000007`. `scope_key` = `letter.{type_code}:{period}` (period = year or `all`).
- **Consumption policy (explicit decision):** numbers are allocated in a short committed transaction that also inserts the letter row with `status='issuing'`. If the subsequent PDF/upload phase fails the row flips to `status='failed'` — **gaps are possible and intended** (invoice-style numbering: every allocated number maps to a row with provenance; uniqueness and monotonicity guaranteed). Render/PDF/validation failures before allocation consume nothing.
- `letter_number` is **not a merge field** in v2 — it's stamped metadata (filename, history, optional letterhead footer placeholder resolved at issue).

## 4. Immutable issuance

`employee_letters` gains: `letter_number`, `file_id` (FK → files.files), `rendered_html text` (replaces sole reliance on rendered_text — keep both: rendered_text = plain-text snapshot), `signatory_name`, `signatory_designation`, `letterhead_snapshot jsonb` (header/footer/logo resolution result), `issue_date date`, `issue_tz varchar(50)`, `pdf_sha256 char(64)`, `supersedes_id`, status set = `issuing | issued | failed | revoked | superseded`.

- PDF generated **at issue time**, bytes stored in `files` (purpose `employee.letter`, owner = employee → Phase-1 scope applies to downloads).
- `pdf_sha256` = SHA-256 of stored bytes — integrity check + dedup evidence.
- **Revoke:** flips status + stamps revoker/reason only. Never deletes the file row, storage bytes, or letter row.
- **Reissue/supersede:** new letter with `supersedes_id` → old; old status → `superseded`. History chain intact.

## 5. Transaction & failure handling

Two committed transactions + an external phase (never hold a DB tx across an HTTP call):

```
TX-1: validate (scope, template published+in window, model data complete)
      upsert counter (RETURNING) + INSERT employee_letters(status='issuing')  → commit
EXT : render HTML (sanitized) → Gotenberg → PDF bytes in memory + sha256
      on failure → TX-2a UPDATE letter status='failed' (number stays consumed), done
TX-2: INSERT file row (pending) → commit, then upload bytes to storage
      on upload failure → delete partial storage key, mark file+letter 'failed' → done
TX-3: UPDATE file status='stored', letter status='issued', file_id, pdf_sha256 → commit
```

Orphaned files: impossible — bytes only reach storage for a letter that has a `pending` file row; the compensating path deletes partial bytes and marks rows `failed`. A `pending` file older than the job sweep window is cleaned by the existing file-purge pattern (extend if needed — verify in implementation).

## 6. Signatories (deferred scope)

`letter_signatories`: `id, name, designation, employee_id NULL, is_active, created_*` — **no signature image columns** (deferred; confidential-file handling required).
`letter_types.signatory_id` replaces `signatory_employee_id` (keep legacy column during migration, backfill: employee-linked → create signatory rows, then drop in a later milestone).
At issue: snapshot `signatory_name`/`signatory_designation` onto the letter — immutable even if master changes.

## 7. Merge fields & salary gating

Catalogue stays the single source (`LetterMergeFields.All`): current 14 fields + `letter_date` (issue date, tenant tz), `signatory_name`, `signatory_designation`. Salary fields are **registered but gated**:

- A template containing a salary-gated field renders the field only when the issuer holds `salary.assignment.view` on that employee.
- **Unauthorized → issuance fails** with `letter.salary_field_unauthorized` — never a partial/empty render. Preview returns the same error flag so the UI can warn before issuing.
- v2 defers the `salary_breakup` table itself; the gating mechanism ships now so adding the field later is one catalogue entry.

## 8. Gotenberg hardening

- Service `gotenberg/gotenberg:8` on the internal compose network only — **no published port**; reachable as `http://gotenberg:3000`.
- API calls `POST /forms/chromium/convert/html` multipart with the rendered HTML + letterhead assets as file parts — HTML **never contains remote URLs**: sanitizer strips `src`/`href`/`url()`/`@import` pointing off-origin; only `data:` images and bundled letterhead assets allowed (no `http(s)://` refs at all → zero SSRF surface).
- Limits: conversion timeout 15 s, input ≤ 256 KB rendered HTML, output ≤ 10 MB, single retry on 5xx then fail issuance.
- Fallback while `Pdf:BaseUrl` unset: issue stays supported, stores rendered HTML + `file_id` pointing at a `.txt` download (current behavior) — PDF path flagged `pending_pdf` — keeps rollout unblocked.

## 9. Authorization matrix (defined before any API lands)

Module code `core.employee` (existing). New permission codes seeded into catalogue; role grants: Admin all; HR manager +manage/publish/signatory/revoke; HR ops view/issue/reissue scoped.

| Endpoint | Permission | Scope | Notes |
|---|---|---|---|
| GET/POST/PUT letter-types, families, drafts | `employee.letters.template.manage` | unscoped | draft CRUD only |
| POST templates/{id}/publish | `employee.letters.template.publish` | unscoped, sensitive | |
| POST templates/{id}/retire | `employee.letters.template.publish` | unscoped, sensitive | |
| GET letterheads + CRUD | `employee.letters.letterhead.manage` | unscoped | |
| GET/POST/PUT signatories | `employee.letters.signatory.manage` | unscoped, sensitive | |
| GET merge-fields catalogue | `employee.letters.template.manage` | unscoped | |
| GET employees/{id}/letters | `employee.letters.view` | employee-scoped | Phase 1 ✓ |
| POST employees/{id}/letters/preview | `employee.letters.issue` | employee-scoped | no-write render |
| POST employees/{id}/letters | `employee.letters.issue` | employee-scoped | Phase 1 ✓ |
| GET employees/letters/{id} | `employee.letters.view` | resolves owner first | Phase 1 ✓ |
| GET employees/letters/{id}/download | `employee.letters.view` | owner-scoped file | Phase 1 ✓ |
| POST employees/letters/{id}/revoke | `employee.letters.revoke` (new) | scoped, sensitive | split from issue |
| POST employees/letters/{id}/reissue | `employee.letters.reissue` (new) | employee-scoped | |

Menu/UI gating is cosmetic; every route enforces permission + scope server-side.

## 10. API contracts (new/changed)

```
POST /api/v1/employee-docs/letter-types/{typeId}/families           → {familyId}
POST /api/v1/employee-docs/template-families/{familyId}/drafts    → {templateId, versionNumber}
PUT  /api/v1/employee-docs/templates/{id}                          → draft only; 409 if published
POST /api/v1/employee-docs/templates/{id}/publish|retire           → 200
GET  /api/v1/employee-docs/letter-types/{typeId}/templates         → list incl. status/version/effective
GET  /api/v1/employee-docs/merge-fields                            → [{code,label,example,gated}]
GET/POST/PUT /api/v1/employee-docs/letterheads(/{id})
GET/POST/PUT /api/v1/employee-docs/signatories(/{id})
POST /api/v1/employees/{id}/letters/preview {templateId}           → {html, subject, signatoryName, signatoryDesignation, warnings[], errors[]}
POST /api/v1/employees/letters/{id}/reissue {reason?}              → {letterId, letterNumber}
GET  /api/v1/employees/letters/{id}/download                       → PDF (or txt fallback)
```

`body` field renamed `body_html` on write DTOs; `body_text` kept optional. Errors keep the existing `Error` envelope (`code`, `message`, `correlationId`).

## 11. Migration impact

- One additive migration (`LetterManagementV2`): new tables (`letter_template_families`, `letter_counters`, `letter_signatories`, `letterheads`), column adds on 3 tables, exclusion constraint via raw SQL, backfill (existing templates → families, v1, published if `is_active`), index on `employee_letters.letter_number` UNIQUE.
- `btree_gist` already required by the runner. Down: drops constraint + new tables/columns — data loss documented, rollback only on empty features.
- Execution requires the usual approval + per-tenant runner call (no `--all`).

## 12. Acceptance criteria & test scenarios

**AC:** publish a letterhead+signatory-backed template for a branch → preview for a branch employee shows branded rendered HTML + resolved signatory → issue produces numbered `APPT-2026-…` letter with stored PDF + hash → download returns identical bytes → revoke keeps file+row → reissue produces a new number + supersedes chain → Own-scope user cannot reach any of it.

**Tests (new):**
- Unit: AST field validation, sanitizer allowlist (script/iframe/external-URL stripped), precedence resolution, numbering format/reset, overlap detection, signatory snapshot.
- API (Testcontainers): exclusion constraint rejects overlapping published windows (tenant/company/branch levels independent); ownership validation; publish/retire guards (409 on edit published); preview no-write + salary-gated error; issue → counter atomicity (concurrent issues get distinct numbers); failed-PDF path leaves `failed` letter + no orphaned file; download owner-scope; revoke/reissue chains; cross-tenant 404; permission matrix denies (403/404) per row above.
- Gotenberg: compose-integrated test (skip-if-absent flag for CI).
- Regression: 94 API tests must stay green; Phase-1 scope tests unaffected.

## Open items for sign-off

1. Numbering gap policy (gaps allowed, documented) — confirm.
2. Fallback-to-txt issuance while Gotenberg absent — confirm or make PDF mandatory.
3. `signatory_employee_id` legacy column: backfill now vs. dual-read until a later milestone — recommend dual-read this milestone.
