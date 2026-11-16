# M2.7 Letter Management V2 — Phase 2+ Implementation Breakdown (revised)

Status: **design finalized — awaiting approval to implement.** Design details: `M27_PHASE2_DESIGN_DECISIONS.md` (authoritative). Full context: `LETTER_TEMPLATE_IMPLEMENTATION_PLAN.md`.

## Phase 1 status — COMPLETE ✅

- Independent review: **PASS** (`M27_PHASE1_REVIEW_REPORT.md`).
- Migration `20261104124355_LetterRenderedTextUnbounded` **applied and verified** on dev tenants 1008 (milind) and 1009 (devtest): `rendered_text` = `text`, 8 history rows, 5,000-char round-trip OK, data intact.
- Tests: unit 26/26, API **94/94** (incl. 4 new scope/long-content tests).
- Delivered: employee data-scope on all letter/document/salary employee-bound ops, owner-scoped file downloads, sanitized filenames.

## Design refinements incorporated (from Phase-2 design review)

- **Versioning:** template families + explicit `version_number`; published versions immutable; editing a published template creates a new draft; `supersedes_id` chain.
- **Effective dates:** PostgreSQL exclusion constraint with `COALESCE`d company/branch keys so applicability levels can't ambiguously overlap; date-only boundaries in tenant timezone; branch↔company ownership validated.
- **Numbering:** atomic counter-row upsert inside a short committed tx; uniqueness constraint; failed issuance marks the letter `failed` — numbers may have gaps by design (audit provenance).
- **Immutable issuance:** full snapshot (version, rendered HTML, signatory, letterhead/branding, issue metadata), PDF bytes at issue time, `pdf_sha256` content hash; revoke never deletes files/history.
- **Signatures:** name + designation snapshot only; signature-image management **deferred** (needs confidential-file handling).
- **Salary breakup:** **deferred to Payroll readiness**. No salary data in preview/issue without `salary.assignment.view`; unauthorized salary requirement → issuance fails hard, never partial.
- **Gotenberg:** internal-only, SSRF-hardened, size/time limits, two-phase transaction strategy — no orphaned files.
- **Permissions:** all defined before APIs (full matrix in design doc); no reliance on menu visibility.

## Revised sequencing (per review)

| Order | Item | Deliverable |
|---|---|---|
| 1 | P2 schema + migration proposal (docs reviewed → implement on approval) | entities, exclusion constraint, counters |
| 2 | Permission definitions + secure backend foundations | seed catalogue codes, authz matrix wired |
| 3 | Template lifecycle, merge-field catalogue, HTML sanitization | draft→publish→retire, AST validation |
| 4 | Company/branch applicability | resolution precedence, ownership validation |
| 5 | Letterhead + signatory management | masters, snapshots (no signature images) |
| 6 | Gotenberg integration | `IPdfService`, sidecar compose, limits |
| 7 | Preview, numbering, issuance | preview endpoint, counter, two-phase issue |
| 8 | Frontend template editor + admin UI | TipTap, palette, lifecycle UI |
| 9 | Employee letter history + downloads | PDF download, reissue, documents tab surface |
| 10 | Full security + regression testing | scope, SSRF, transaction, isolation tests |

## Effort estimate (revised)

| Block | Est. |
|---|---|
| Schema + migration | 2 d |
| Permissions + foundations | 0.5 d |
| Lifecycle + catalogue + sanitizer | 3 d |
| Applicability | 1.5 d |
| Letterhead + signatory (names only) | 2 d |
| Gotenberg | 1.5 d |
| Preview + numbering + issuance tx | 2.5 d |
| Frontend editor + admin | 3 d |
| History/downloads UX | 1 d |
| Tests + validation | 2.5 d |

**~20 dev-days** (signature-image and salary-breakup deferrals offset the new versioning/immutability work).

## Deferred (explicit, per review)

- Signature images (confidential file handling + immutable snapshot required first)
- `salary_breakup` merge field (Payroll readiness; hard-fail semantics specified in design doc)
- Word/DOCX export
- Bulk/batch issuance, ESS self-service letter requests

## Migration impact summary

One tenant-DB migration: alter `letter_templates` (+family/version/status/dates/letterhead/applicability), alter `letter_types` (+signatory_id, number prefix/reset), alter `employee_letters` (+letter_number, file_id, snapshots, sha256, issue metadata, `failed`/`superseded` statuses), new `letterheads`, `letter_signatories`, `letter_counters`, exclusion constraint (raw SQL). Backfill: existing templates → one family each, version 1, status published if active. Down is supported (drops new objects; column drops lose data — documented).

## Approval required to proceed

1. Sign-off on `M27_PHASE2_DESIGN_DECISIONS.md` (schema shape, numbering gap policy, Gotenberg two-phase strategy).
2. Approval to implement (code + generate migration; execution still requires separate approval).
3. Compose change adds a `gotenberg` service (infrastructure change — explicit approval needed before it lands).
