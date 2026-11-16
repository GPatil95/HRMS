# Letter Template Module — Enhancement Implementation Plan

Status: **PLAN ONLY, awaiting review.** No code, migrations or data have been changed.
Branch inspected: `m2-hr-core` (HEAD `8de547d`). File paths are relative to `src/`.

---

## 0. Current implementation (baseline, verified in source)

| Layer | What exists |
|---|---|
| Entities | `Domain/Tenant/EmployeeDocuments.cs`: `LetterType` (Code, Name, Description, `SignatoryEmployeeId?`, IsActive), `LetterTemplate` (LetterTypeId, Name, Version, Subject, Body, IsActive), `EmployeeLetter` (EmployeeId, LetterTypeId, LetterTemplateId, TemplateVersion, Subject, RenderedText, Status issued/revoked, IssuedBy/At, RevokedBy/At/Reason) |
| DB | `hr.letter_types`, `hr.letter_templates` (unique `letter_type_id, version`; `body text`; `subject varchar(200)`), `hr.employee_letters` (`rendered_text varchar(256)`). This was created in migration `20261101100814_EmployeeDocuments` (commit `be86feb`), and `TenantDbContext` was aligned to it with `HasMaxLength(256)` in `8de547d` |
| Service | `Application/Employees/EmployeeDocumentService.cs`: `CreateTemplateAsync` (always inserts a new version, max + 1, so there is no edit path), `IssueAsync` (chosen template, or the highest active version), `BuildModelAsync` (14 flat fields), `RevokeAsync`, `ListLettersAsync`, `GetLetterAsync` |
| Renderer | `ITemplateRenderer` → `Infrastructure/Messaging/Messaging.cs` `ScribanTemplateRenderer`. Sandboxed: `LoopLimit 1000`, `RecursiveLimit 20`, no loader. Top-level string values are HTML-encoded, but **nested objects/collections are not**, so the current `.txt` output shows `&amp;` for `&` |
| API | Admin endpoints under `api/v1/employee-docs`: `GET/POST letter-types`, `GET letter-types/{id}/templates`, `POST letter-types/{typeId}/templates`. Employee endpoints under `api/v1/employees`: `GET/POST {id}/letters`, `GET letters/{id}`, `GET letters/{id}/download` (`text/plain`, filename built from the **unsanitised subject**), `POST letters/{id}/revoke` |
| Permissions | `employee.letters.view` (scoped), `employee.letters.issue` (scoped), `employee.letters.manage` (configure); module `hr`. Nav item `hr.docs` → `/hr-docs` |
| Data scope | `RequirePermissionAttribute` checks module + permission only; the comment says "data scope applied in services". **`EmployeeDocumentService` does not call `IDataScopeService.CanAccessEmployeeAsync`**, so letter endpoints do not enforce data scope. A user holding `letters.view` at `Own` could read any employee's letters by id. Only `OrganizationService` get/update employee calls it |
| Frontend | `pages/tenant/HrDocsPage.tsx`: letter-type dialog, template dialog (multiline `TextField` × 10 rows, hard-coded `MERGE_FIELDS`). `EmployeeSections.tsx` Letters tab: issue dialog (type + optional template), view dialog shows `renderedText`, download `.txt`, revoke |
| Files | `Application/Files/FileService.cs` (`UploadAsync(FileUploadRequest)`, `DownloadAsync`, purposes registered per module with extensions, size limit and permissions), e.g. `hr.employee.document`. Company logo: `Company.LogoFileId` + `OrgMastersService.ResolveBrandingAsync(companyId, employeeId)` → `BrandingDto(CompanyId, CompanyName, LegalName, LogoFileId, LogoUrl)` |
| Salary | `EmployeeSalaryAssignment` + resolved `EmployeeSalaryLine` snapshot (component, calc type, `ResolvedAmount`) with `SalaryService.AssignmentAsOfAsync`. `percent_of_gross`/`formula` lines stay unresolved |
| Original design | `docs/milestones/M2-hr-core-design.md` §M2.4 planned `letter_template_versions` (company_id, body_html, header/footer, is_published), letters with `number`, `file_id`, `supersedes_id`, and "PDF decision deferred (recommend QuestPDF evaluation next phase)". Most of this was not built |

### Library availability
| Need | Available? | Evidence |
|---|---|---|
| HTML template renderer | **Yes**: Scriban (`Hrms.Infrastructure.csproj` → `<PackageReference Include="Scriban" />`), already used for email + letters | `ScribanTemplateRenderer` |
| HTML → PDF | **No.** No QuestPDF, PuppeteerSharp, Playwright, iText, DinkToPdf, wkhtmltopdf or Gotenberg in any csproj, props or compose file | grep of all `*.csproj`/`*.props` |
| HTML sanitiser (server) | **No** (no HtmlSanitizer/AngleSharp) | grep |
| Rich-text editor (frontend) | **No** (no TipTap, Quill, CKEditor, Slate, Lexical, TinyMCE) | `package.json`: MUI 9, React 19, TanStack Query |
| HTML sanitiser (client) | **No** (no DOMPurify) | `package.json` |
| Spreadsheet export | ClosedXML (not relevant here) | — |

---

## 1. Key decisions needed before implementation

| # | Decision | Options | Recommendation |
|---|---|---|---|
| D1 | PDF engine | (a) **Gotenberg** sidecar container (Chromium HTML→PDF over HTTP, MIT); (b) **PuppeteerSharp** with Chromium inside the API image (about +300 MB, extra apt dependencies, a process per render); (c) **QuestPDF** (code-first layout, **cannot render arbitrary HTML**; Community licence has a revenue threshold) | **(a) Gotenberg** behind an `IPdfRenderer` abstraction. Keeps Chromium out of the API image, runs with no outbound network, fits `docker-compose.yml`, and gives full fidelity for admin-authored HTML |
| D2 | Editor | (a) **TipTap** (ProseMirror, MIT, headless and styleable with MUI); (b) raw HTML textarea with live preview | **(a) TipTap** with a restricted extension set, plus a "source" toggle for admins |
| D3 | Server sanitiser | `HtmlSanitizer` (Ganss.Xss, MIT, AngleSharp-based) | Add it. Server-side sanitisation is authoritative |
| D4 | Template publish approval | Single-step publish, or maker-checker (`publish` permission separate from `manage`) | A separate `publish` permission; maker-checker optional per tenant later |
| D5 | Letter numbering | None, or a per-type series (design doc §M2.4 reuses `employee_code_series`) | Include it (`APT/2026/0001`); low extra cost |
| D6 | Salary table when lines are unresolved (`percent_of_gross`/`formula`) | Block issue, or render "as per policy" | **Block with a validation error** until payroll resolves these lines |

---

## 2. Requirement-by-requirement technical assessment

### R1. Long letter content
- **Existing:** `employee_letters.rendered_text varchar(256)`; `letter_templates.body text` (already unlimited). Any rendered body over 256 characters fails at `SaveChangesAsync`.
- **Backend:** remove `HasMaxLength(256)` from `EmployeeLetter.RenderedText` in `TenantDbContext.cs`. Add `RenderedHtml` (text). Enforce application limits: template body ≤ 200 KB, header/footer ≤ 20 KB each, rendered output ≤ 1 MB.
- **DB:** new migration with `ALTER COLUMN rendered_text TYPE text` (widening; no data loss, no rewrite risk) and `ADD rendered_html text NULL`. **Do not edit existing migrations.**
- **API:** none, apart from the new fields in DTOs.
- **Validation/security:** size limits return 400 `template.too_large`; the request-body size limit on template endpoints is about 512 KB.
- **Tests:** an API test issues a letter of about 10 KB and checks it round-trips; a migration test on a copy of a tenant with existing letters checks they are preserved.

### R2. HTML-based template editor
- **Existing:** a plain multiline `TextField`; the body is rendered as text.
- **Backend:**
  - Add a `Format` column (`text` | `html`); legacy rows become `text`.
  - On save, parse with Scriban (reject syntax errors with line/column), then sanitise the HTML with an allowlist:
    - Tags: `p, br, hr, h1–h4, strong, b, em, i, u, s, span, div, ul, ol, li, table, thead, tbody, tfoot, tr, th, td, img, blockquote, sup, sub`.
    - Attributes: `style` (only text-align, font-weight, font-size, width, border, padding, margin, color, background-color, vertical-align), `colspan`, `rowspan`, `class`.
    - `img src` only for internal placeholders (`{{ company_logo }}`, `{{ signatory_signature }}`), resolved to data URIs at render time.
    - Strip scripts, event handlers, iframes, `<link>`, `<style>` with `@import`, and external URLs. Sanitise **again after rendering** because merge values are inserted then.
- **Frontend:**
  - New full-page editor route `/hr-docs/templates/:id` (the dialog is too small).
  - TipTap with StarterKit, Underline, TextAlign, Table (+Row/Cell/Header), and a restricted Image node for placeholders.
  - Merge-field palette (R3), source-view toggle, and a live preview pane (R8).
  - New dependencies: `@tiptap/react`, `@tiptap/pm`, `@tiptap/starter-kit`, the table/align/underline extensions, and `dompurify` for defence-in-depth in the preview iframe.
- **DB:** `letter_templates.format varchar(10) NOT NULL DEFAULT 'text'`.
- **API:** `PUT employee-docs/letter-templates/{id}` (draft only, RowVersion), `GET employee-docs/letter-templates/{id}`.
- **Security:** XSS through admin-authored HTML viewed by other users. Mitigations: server sanitisation, viewing in a sandboxed `<iframe sandbox srcdoc>`, and a CSP on the API response. SSRF through the PDF engine is covered in R10.
- **Tests:** unit tests for the sanitiser allowlist (script, onerror, `javascript:`, external img and CSS `url()` are removed); Scriban syntax errors give 400.

### R3. Dynamic merge fields
- **Existing:** `BuildModelAsync` builds 14 flat fields; the frontend list is hard-coded; templates are not validated against the fields.
- **Backend:**
  - Add a `LetterMergeFieldCatalog`: code, label, group, data type, sample value, and required permission.
  - Keep the 14 existing names unchanged (backward compatible).
  - Add fields from data that already exists in source:
    - **company:** `company_legal_name` (Company.LegalName), `company_cin`, `company_pan`, `company_registered_address` (CompanyAddress with type registered), `company_logo`.
    - **branch:** `branch_address` (Branch address fields), `branch_city`.
    - **employee:** `middle_name`, `work_phone`, `probation_end_date`, `confirmation_date`, `grade_name`, `location_name`, `cost_centre_name`, `manager_name` (ManagerEmployeeId → DisplayName).
    - **signatory:** `signatory_name`, `signatory_designation`, `signatory_signature` (R7).
    - **letter:** `letter_number`, `issue_date`, `effective_date` (as-of).
    - **salary:** see R6.
  - On save, walk the Scriban AST (`ScriptVariableGlobal` / member access) and reject unknown fields with 400 `template.unknown_field`.
  - Use a recursive HTML-encoding model builder, so nested values (salary lines) are escaped too. This fixes the top-level-only encoding.
  - Format all dates as `dd MMM yyyy` and money as `en-IN` with ₹.
- **Frontend:** palette loaded from the API, grouped, click to insert `{{ field }}`; shows a sample value on hover; remove the hard-coded `MERGE_FIELDS`.
- **API:** `GET employee-docs/letter-merge-fields`.
- **Security:** salary fields are available only when the caller has `SalaryAssignmentView` for that employee (checked at preview/issue); masked statutory values (PAN etc.) are not exposed.
- **Tests:** unit tests for the catalogue/AST validator and encoding (company name `A&B <x>` renders escaped); an API test for an unknown-field rejection.

### R4. Company- and branch-specific applicability
- **Existing:** none; templates are tenant-wide per letter type.
- **Backend:** templates get optional `CompanyId` and `BranchId`. The branch must belong to the company (validated).
  - **Resolution** in `ResolveTemplateAsync(type, employee, asOf)`: published templates whose effective window contains `asOf`. Pick the most specific match: branch = employee.BranchId, then company = employee.CompanyId, then tenant-wide. A tie goes to the highest version.
  - An explicitly chosen template must be applicable to the employee, otherwise 400.
- **DB:** `letter_templates.company_id uuid NULL FK org.companies RESTRICT`, `branch_id uuid NULL FK org.branches RESTRICT`, and an index `(letter_type_id, company_id, branch_id, status)`.
- **API:** applicability fields on template create/update; `GET employees/{id}/letters/resolve-template?letterTypeId=&asOf=` returns the template that will be used and why.
- **Frontend:** company and branch selects (branch filtered by company) in the editor. List columns: Scope (Tenant / Company / Branch) and Effective dates.
- **Tests:** unit tests for the precedence matrix; an API test where two companies get different templates.

### R5. Header, footer and company logo
- **Existing:** company logo storage and branding resolver exist; letters do not use them.
- **Backend:** add a new **letterhead** entity (reusable per company or branch): `HeaderHtml`, `FooterHtml`, `ShowLogo`, `LogoPosition`, `PaperSize` (A4/Letter), margins in mm, optional page numbering. Templates reference `LetterheadId?`. Without one, the company default letterhead is used, then a plain layout.
  - The logo is loaded server-side through `FileService.DownloadAsync(company.LogoFileId)` and embedded as a data URI, so the PDF engine never fetches URLs.
  - Header and footer are sent to Gotenberg as separate `header.html`/`footer.html` (Chromium supports `pageNumber`/`totalPages` classes).
- **DB:** new `hr.letterheads` (id, company_id NULL, branch_id NULL, code, name, header_html, footer_html, show_logo, logo_position, paper_size, margin_top/bottom/left/right_mm, is_default, is_active, row_version); `letter_templates.letterhead_id NULL FK`.
- **API:** `GET/POST/PUT employee-docs/letterheads`, `PATCH …/{id}/status`.
- **Frontend:** a "Letterheads" tab in `/hr-docs` (TipTap mini-editors for header and footer, margin inputs, logo toggle, preview).
- **Security:** header and footer go through the same sanitiser; the only image source is the internal logo placeholder.
- **Tests:** the PDF contains the logo image object; the default-letterhead fallback works.

### R6. Salary breakup table
- **Existing:** resolved salary lines exist on approved assignments; letters have no salary fields.
- **Backend:**
  - Model fields from `SalaryService.AssignmentAsOfAsync(employeeId, asOf)`, or an explicitly chosen `salaryAssignmentId` (it must belong to the employee and be approved/active/closed): `ctc_annual`, `ctc_monthly`, `salary_effective_from`, and `salary_lines`, a list of `{ component_name (PayslipName ?? Name), type, monthly, annual }` ordered by `DisplayOrder`.
  - Pre-computed sub-totals `gross_monthly`, `deductions_monthly` and `employer_contributions_monthly`, based on component `Type` and `PartOfGross`.
  - Template authors write `{{ for line in salary_lines }}…{{ end }}` inside a `<table>`.
  - Also provide a built-in snippet `{{ salary_table }}`: a pre-rendered, sanitised standard table, so admins do not need loops.
  - Unresolved lines (`percent_of_gross`/`formula`) block the issue with 400 `salary.unresolved_lines` (D6). A missing assignment gives 400 `salary.not_found` when the template references salary fields.
- **DB:** `employee_letters.salary_assignment_id uuid NULL FK RESTRICT` (traceability).
- **API:** `salaryAssignmentId?` on preview/issue input.
- **Frontend:** salary group in the merge palette and an "Insert salary table" button. The issue dialog shows a salary-assignment selector when the template uses salary fields.
- **Security:** requires `employee.salary_assignment.view` within data scope; letter content is sensitive, so it is audited and download is permission-checked (R12).
- **Tests:** table rendering and totals; unresolved-line blocking; 403 for a user without salary permission; the chosen assignment must belong to the employee.

### R7. Signatory name, designation and signature
- **Existing:** `LetterType.SignatoryEmployeeId` gives only `signatory_name` (employee display name).
- **Backend:** new **signatory** master: `Name`, `Designation` (free text, or taken from the linked employee's designation), `EmployeeId?`, `CompanyId?`, `SignatureFileId?`, `IsActive`.
  - New file purpose `hr.letter.signature` (`.png/.jpg`, ≤ 500 KB, confidential, permission `letters.manage` to upload and `letters.view` to read).
  - Resolution order: issue-time override → template default → letter type default → legacy `SignatoryEmployeeId` (fallback, kept for compatibility).
  - Signature embedded as a data URI.
  - A snapshot of the signatory (name, designation, file id) is stored on the letter so later changes do not alter issued letters.
- **DB:** new `hr.letter_signatories`; `letter_types.default_signatory_id NULL FK`, `letter_templates.signatory_id NULL FK`; `employee_letters.signatory_name varchar(160)`, `signatory_designation varchar(160)`, `signatory_signature_file_id uuid NULL`.
- **API:** `GET/POST/PUT employee-docs/signatories`, `POST/DELETE employee-docs/signatories/{id}/signature` (multipart), `PATCH …/status`.
- **Frontend:** a "Signatories" tab (list, form, signature upload with preview); signatory selects on the letter type, the template and the issue dialog.
- **Security:** signature images are sensitive. They are never public URLs, only embedded server-side. Upload and remove actions are audited.
- **Tests:** resolution precedence; snapshot immutability after a signatory changes; file-purpose extension and size checks.

### R8. Template version management and effective dates
- **Existing:** auto-incrementing version per type, IsActive only, no edit, no effective dates; `IssueAsync` picks the highest active version.
- **Backend:**
  - Each row stays one version. Add a lifecycle `Status`: `draft` → `published` → `retired`.
  - Drafts are editable (`PUT`, RowVersion). Published rows are **immutable**; "Edit" clones them into a new draft (`POST …/{id}/clone`).
  - `Publish` sets `EffectiveFrom` (required) and an optional `EffectiveTo`, and records `PublishedBy/At`.
  - Overlapping published windows within the same (type, company, branch) scope are rejected. Use an exclusion constraint like the salary-assignment one (`btree_gist` is already used by salary), or close the previous version automatically when the new one publishes (set its `EffectiveTo`).
  - Add version `Notes` (change summary).
- **DB:** add to `letter_templates`: `status varchar(20) NOT NULL DEFAULT 'published'` (check constraint), `effective_from date NULL`, `effective_to date NULL`, `notes varchar(500)`, `published_by uuid NULL`, `published_at timestamptz NULL`, plus the exclusion constraint on published rows.
  - Backfill: existing active rows become `published`, `effective_from = created_at::date`; inactive rows become `retired`.
  - `IsActive` is kept and derived for compatibility, or deprecated.
- **API:** `PUT letter-templates/{id}` (draft), `POST …/{id}/publish {effectiveFrom, effectiveTo?}`, `POST …/{id}/retire`, `POST …/{id}/clone`, `GET letter-types/{id}/templates?status=`.
- **Frontend:** version list with status chips, effective window and published by/at; Clone / Publish / Retire actions; a side-by-side diff (optional, later).
- **Security:** publish requires `employee.letters.template.publish` (D4); full audit of before/after for template changes.
- **Tests:** a published row cannot be edited (409); overlap rejection; resolution by `asOf`; the backfill migration on existing rows.

### R9. Preview before generation
- **Existing:** none; issuing renders and saves immediately.
- **Backend:** `PreviewAsync` reuses the same pipeline (resolve → model → render → sanitise → wrap in letterhead) **with no persistence**. Two variants:
  - (a) Template preview with sample data from the catalogue, or a chosen employee.
  - (b) Employee issue preview.
  - Returns `{ html, subject, templateId, version, warnings[] }`. Warnings cover empty merge values, missing signatory, missing logo and the like. Optional `?format=pdf` returns the PDF stream for a print preview.
- **API:** `POST employee-docs/letter-templates/{id}/preview {employeeId?, asOf?}` (requires `letters.manage`); `POST employees/{id}/letters/preview {letterTypeId, letterTemplateId?, asOf?, salaryAssignmentId?, signatoryId?}` (requires `letters.issue` + data scope + salary permission if needed).
- **Frontend:** the editor preview pane uses a sandboxed iframe and an employee picker. The issue dialog becomes a two-step stepper: Configure → Preview (iframe + warnings) → Generate.
- **Security:** preview must not leak data outside scope; it is rate-limited (rendering is CPU-heavy); preview of an employee with salary content is audited.
- **Tests:** preview writes nothing (row counts unchanged); a scope violation gives 404 or 403; warnings are reported for empty fields.

### R10. PDF download
- **Existing:** `.txt` only.
- **Backend:**
  - Add `IPdfRenderer` to `Application/Abstractions` with `Task<byte[]> RenderAsync(string html, PdfOptions(paper, margins, headerHtml, footerHtml), ct)`.
  - Infrastructure `GotenbergPdfRenderer` (HttpClient, timeout about 30 s, Polly-style retry ×1). Tests use a `NoopPdfRenderer`/fake.
  - At issue time, generate the PDF **once** and store it with `FileService.UploadAsync` under a new purpose `hr.employee.letter` (`.pdf`, ≤ 10 MB, confidential, owner = employee). Save `file_id` and `pdf_sha256` on the letter.
  - Download streams the stored file and never re-renders, so the bytes stay the same. Legacy letters with no `file_id` fall back to `.txt`.
  - Filename sanitisation: `{letter_number or type}-{employee_code}.pdf`, ASCII-safe, with RFC 5987 `filename*`. This fixes the current use of the raw subject.
- **Infra:** `gotenberg/gotenberg:8` service in `infrastructure/docker/docker-compose.yml`, internal network only, with `--chromium-deny-list=^.*`/`--chromium-allow-list` to block all URL fetches, and no published host port. Config: `Pdf__GotenbergUrl`; health check.
- **API:** `GET employees/letters/{id}/download?format=pdf|txt` (default `pdf` when available).
- **Frontend:** the download button gets PDF and TXT options; inline view via the PDF blob in a new tab.
- **Security:** Chromium has no network access (SSRF); no JavaScript runs in the PDF (`skipNetworkIdleEvent`, sanitised HTML has no scripts); files are stored confidential; downloads are audited.
- **Tests:** the issued PDF starts with `%PDF`; downloading twice gives an identical hash; one integration test against a Gotenberg testcontainer (the API test fixture already uses Testcontainers), with the fake renderer elsewhere.

### R11. Generated-letter history linked to the employee
- **Existing:** `employee_letters` rows linked by `employee_id` (cascade), listed in the Letters tab; issued/revoked statuses.
- **Backend:** add `LetterNumber` (D5, series `letter:{type_code}` unique), `CompanyId`/`BranchId` snapshots, `AsOfDate`, `MergeSnapshot jsonb` (the resolved values, for audit and reproducibility; salary values included only when the letter used them), `SupersedesId` (self-FK) for re-issue, and `file_id`.
  - Re-issue: `POST letters/{id}/reissue` creates a new letter and marks the old one `superseded`. Add a `superseded` status to the check constraint.
  - Revoke stays a status-only change.
- **DB:** columns on `employee_letters` as above; FK `file_id → files.file_objects RESTRICT`; indexes `(employee_id, issued_at desc)` and unique `letter_number`.
- **API:** `GET employees/{id}/letters?status=&type=`; `POST letters/{id}/reissue`.
- **Frontend:** Letters tab table columns: Number, Type, Template version, Issued by/at, Status, PDF; a filter; a "Superseded by" link.
- **Tests:** re-issue chain; revoke keeps the file; list ordering and filtering.

### R12. Permissions
- **Existing:** `employee.letters.view|issue|manage`. **There is a data-scope gap in all letter (and document) employee endpoints** (§0).
- **Backend:**
  - **Prerequisite fix:** call `IDataScopeService.CanAccessEmployeeAsync(LettersView|LettersIssue, employeeId)` in list, get, download, preview, issue, revoke and reissue. For letter-id routes, resolve the employee first and return 404 when out of scope.
  - New permissions in `FoundationModules.cs`, synchronised into tenants by the existing catalogue sync (`TenantSeedService` / `migrate-tenants`). Tenant Admin receives them automatically:
    - `employee.letters.template.publish` (configure)
    - `employee.letters.revoke` (approve, scoped; split from `issue`)
    - `employee.letters.signatory.manage` (configure; optional, otherwise covered by `manage`)
  - Salary merge fields additionally require `employee.salary_assignment.view` in scope.
  - Audit events: template create/update/publish/retire, signatory changes, letter preview with salary, issue, download, revoke, reissue.
- **Frontend:** hide or disable actions by effective permission (the existing `useMe`/permission helper pattern); the nav item `hr.docs` stays visible with `letters.manage` or `documents.manage`.
- **Tests:** permission matrix (403 for each missing permission); data-scope tests (a Manager at DirectReports cannot preview or issue for a non-report); tenant isolation for templates, signatories, letterheads and files.

---

## 3. Database change summary (one new tenant migration, e.g. `LetterTemplatesV2`)

All changes add or widen; nothing is destructive. Existing migrations are not touched.

| Table | Change |
|---|---|
| `hr.employee_letters` | `rendered_text` varchar(256) → **text**. Add `rendered_html text`, `letter_number varchar(40) UNIQUE NULL`, `company_id`, `branch_id`, `as_of_date date`, `merge_snapshot jsonb`, `signatory_name`, `signatory_designation`, `signatory_signature_file_id`, `salary_assignment_id`, `file_id`, `pdf_sha256 char(64)`, `supersedes_id`. Status check gains `superseded` |
| `hr.letter_templates` | Add `format`, `status`, `effective_from`, `effective_to`, `notes`, `published_by/at`, `company_id`, `branch_id`, `letterhead_id`, `signatory_id`, `header_override_html?` (optional). Exclusion constraint on published windows per scope. Backfill status and effective_from |
| `hr.letter_types` | Add `default_signatory_id`, `number_prefix varchar(20)` |
| new `hr.letter_signatories` | As in R7 |
| new `hr.letterheads` | As in R5 |
| numbering | Reuse `org.employee_numbering_config` series logic, or a new `hr.letter_number_series(type_id, year, next_value)` row-locked counter |

**Rollout:**
1. Generate the migration with `dotnet ef migrations add` (never hand-edit or regenerate existing ones).
2. Back up each tenant DB (same procedure as `backups\pre-m2-migration-*`).
3. `migrate-tenants --tenant-id <id> --dry-run`, then apply per tenant (no `--all` in dev without approval).
4. Catalogue sync adds the new permissions.

---

## 4. Recommended implementation sequence

| Phase | Scope | Depends on | Complexity | Est. effort |
|---|---|---|---|---|
| P0 | **Fixes:** letter/document data-scope enforcement; widen `rendered_text` (migration); safe download filename | — | S | 0.5–1 d |
| P1 | Data model + migration `LetterTemplatesV2` (templates, letters, signatories, letterheads) + backfill + DbContext config | P0 | M | 1.5 d |
| P2 | Rendering pipeline: merge-field catalogue, recursive-escaping model builder, AST field validation, HtmlSanitizer, size limits | P1, D3 | M | 2 d |
| P3 | Template lifecycle API: draft/update/clone/publish/retire, applicability + effective-date resolution, overlap constraint | P1, P2 | M | 2 d |
| P4 | Signatories + signature file purpose; letterheads + logo embedding | P1 | M | 1.5 d |
| P5 | PDF: `IPdfRenderer`, Gotenberg compose service, issue-time PDF storage, download endpoint | P2, D1 | L | 2–3 d |
| P6 | Salary breakup fields, `salary_table` snippet, permission gating | P2 | M | 1.5 d |
| P7 | Preview endpoints + issue v2 (asOf, signatory override, salary assignment, numbering, reissue) | P3–P6, D5 | M | 1.5 d |
| P8 | Frontend admin: template list/versions, TipTap editor page, merge palette, preview pane, signatories tab, letterheads tab | P3, P4, P7, D2 | L | 3–4 d |
| P9 | Frontend employee Letters tab: stepper issue + preview iframe, history table, PDF download, reissue/revoke | P7 | M | 1.5 d |
| P10 | Tests + hardening: unit, API (permission/scope/isolation/migration), Gotenberg integration test, manual E2E on devtest, docs update | all | M | 2 d |

**Total: about 19–22 developer-days.** P0 can be delivered separately and first, because it fixes a real defect and a security gap. P5 is the main infrastructure risk (adding a container to compose and CI).

---

## 5. Validation & security checklist
- The server-side HTML sanitiser runs at template save **and** after rendering; client-side DOMPurify is only defence-in-depth.
- Scriban stays sandboxed (no loader/imports, `LoopLimit`, `RecursiveLimit`, output-length cap); syntax errors return 400 with position.
- Merge values are HTML-encoded recursively. Only `salary_table`, `company_logo` and `signatory_signature` emit trusted HTML, generated server-side.
- The PDF engine has no network access (allow/deny list) and sees no secrets; images are data URIs only.
- Data scope is enforced on every employee-bound letter operation; salary content also requires salary permission.
- Published templates and issued letters are immutable; revoke and reissue are status changes or new rows; full audit trail.
- Files: confidential purposes, size and extension limits, malware scan through the existing `IMalwareScanner` path in `FileService`, sanitised download filenames.
- RowVersion optimistic concurrency on drafts, letterheads and signatories (`stale-version` 409, the existing convention).
- Tenant isolation: all new tables live in tenant DBs; file keys are tenant-prefixed by `FileService`.

## 6. Testing requirements (summary)
- **Unit** (`tests/unit`): template resolution precedence + effective dates; merge-field validator; escaping; sanitiser allowlist; salary table totals and unresolved blocking; signatory precedence; filename sanitiser.
- **API** (`tests/api`, Testcontainers): migration on populated data; template lifecycle (draft edit, published immutable, overlap 409); preview writes nothing; issue → PDF stored (`%PDF`, stable hash); download PDF/TXT; reissue/revoke; permission 403 matrix; data-scope 404; salary-permission gating; cross-tenant isolation; long content (≥ 10 KB).
- **Integration:** a Gotenberg container renders the header/footer/logo sample.
- **Frontend:** `tsc`, ESLint, `vite build`; manual or testing-agent E2E on `devtest`: create letterhead + signatory → author an HTML appointment letter with a salary table → publish with an effective date → preview for EMP-TEST-001 → generate → download PDF → reissue → revoke.
- **Regression:** existing 26 unit and 90 API tests stay green; legacy text templates and letters still list, view and download.

## 7. Acceptance criteria
1. A full appointment letter (≥ 5,000 rendered characters, with headings, lists and a table) can be authored in the HTML editor, previewed, issued and downloaded as a PDF without errors.
2. The merge palette lists only fields the backend supports; saving a template with an unknown field fails with a clear message; all 14 legacy fields still work.
3. Templates can be tenant-wide, company-specific or branch-specific; for an employee and as-of date, the most specific published template in its effective window is used, and the UI shows which one.
4. Published templates cannot be edited; editing creates a new draft version; overlapping effective windows in the same scope are rejected.
5. The PDF shows the company letterhead (logo, header, footer, page numbers) and the signatory's name, designation and signature image; later changes to the signatory or template do not change issued letters.
6. A salary-breakup table renders from the employee's approved salary assignment, with correct monthly/annual totals; it is blocked when lines are unresolved or the user lacks salary permission.
7. Preview creates no database rows or files.
8. Every issued letter appears in the employee's Letters history with number, template version, issuer, status and a downloadable PDF whose bytes never change; revoke and reissue keep the history.
9. Users outside an employee's data scope cannot list, preview, issue, download or revoke that employee's letters; new permissions appear in the Roles UI and Tenant Admin has them after catalogue sync.
10. The new migration applies cleanly to existing tenants with no data loss (legacy letters intact), using dry-run + backup + per-tenant execution.
11. All builds and tests pass: solution build, unit, API, ESLint, tsc, vite build.

## 8. Out of scope / follow-ups
- Word (.docx) export (requires DocumentFormat.OpenXml or LibreOffice; Gotenberg's LibreOffice module could convert HTML→DOCX later).
- E-signature / employee acknowledgement workflow, email delivery of letters (the email infrastructure exists), bulk generation through Hangfire jobs, and multilingual templates.
- Payroll-derived fields beyond the resolved salary snapshot (these depend on a future payroll engine).
