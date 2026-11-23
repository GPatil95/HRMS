# Help Center / Knowledge Base — Design (in-app)

Version: 1.0 · Last updated: 2026-11-23 · Status: **DESIGNED — not yet implemented**
Module candidate: `core.help` (ships with the Help Center milestone)

## Purpose

An in-app knowledge base inside SWIFTHRM so users find answers without
leaving the product. Same source of truth as the versioned docs in this
repository — articles are authored in `docs/`, published into the Help Center.

## Information architecture

| Element | Behavior |
|---|---|
| **Search** | Full-text over title, body, tags; ranked; scoped by the user's role + entitled modules. |
| **Categories** | Module-wise trees mirroring `docs/` (Platform Admin, Tenant Admin, Employee, Attendance, Leave, Payroll, …). |
| **Articles** | Markdown body, screenshots, embedded videos, `Applies to` version, last-updated stamp, owner. |
| **FAQs** | Per-article FAQ blocks; also a global FAQ view aggregated per category. |
| **Troubleshooting** | Symptom→cause→fix articles tagged `troubleshooting` and surfaced by error codes where the API emits them (e.g. `ingest.too_large` deep-links to its article). |
| **Related articles** | Manual `related[]` links plus same-category suggestions. |

## Visibility model

- **Role-based visibility**: every article carries an audience list
  (`platform-admin`, `tenant-admin`, `hr-admin`, `manager`, `payroll-admin`,
  `recruiter`, `employee`, `implementation`) + optional module entitlement
  requirement. A reader sees an article only when their roles *and* the
  tenant's entitlements allow the underlying feature — the Help Center never
  reveals a screen the user can't reach.
- **Global vs tenant documentation**: Platform Admin authors/publishes the
  **global** knowledge base (all tenants). A tenant may additionally publish
  **tenant-scoped** articles (their own policies, processes) visible only
  inside that tenant. Global publication is owned by Platform Admin
  (`platform.content.manage` — new permission); tenant docs need
  `tenant.content.manage`.

## Data model (planned, tenant DB `help` schema + platform `help` schema)

- `articles` — id, scope (global | tenant), category, title, body markdown,
  audience roles, module code, version, status (draft/published/archived),
  published_by/at, search vector.
- `article_assets` — screenshots/videos via `core.files`.
- `article_versions` — published snapshots per app version; UI offers
  "view docs for my version".
- `article_feedback` — helpful/not-helpful + comment, feeds review.

## User experience

- **? icon** in the top bar opens the Help Center drawer (search-first).
- Context help: screens link to their article ("Learn about Raw Punches").
- Article page: TOC, screenshots with callouts, embedded video player,
  feedback widget, related articles, "Still stuck? contact support" →
  notification to tenant/platform admin.

## Publication workflow

1. Author markdown in `docs/<module>/` (this repository) or via the article
   editor — same review checklist applies.
2. Publish (Platform Admin for global; authorized role for tenant) → snapshot
   stamped with app version + date.
3. Updates bump `last-updated`; readers subscribed to a category get a
   notification on material changes.

## Non-goals / pending decisions

- Ticketing/chat inside Help Center — out of scope.
- Auto-translation — pending (content is English first).
- Approval workflow for tenant articles — reuses `core.workflow` once built.
