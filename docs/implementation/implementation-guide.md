# HRMS Implementation & Setup Guide

Version: 1.0 · Last updated: 2026-11-23 · Audience: Platform Admin, Implementation Engineer

Covers everything required to take a tenant from nothing to a working HRMS.
Sections whose module is not yet built are marked **PENDING**.

## 1. Platform Admin initial setup

Prereq: the platform is deployed and `seed-platform` has run (catalog +
cluster + super admin).

> **URLs in this guide** (`http://<code>.localhost:5174` style) are local
> development examples. Every production/staging deployment must serve all
> portals over **HTTPS only** — admin credentials must never travel over
> plain HTTP.

1. Sign in at `https://admin.<base-domain>` (`http://admin.localhost` in local
   dev) with the seeded super admin.
2. Verify **Modules** catalog — module list and permission manifests sync from
   the application manifest automatically.
3. Verify **Plans** — each plan binds a set of modules + features; create or
   adjust plans before onboarding tenants.
4. Check **Clusters** — the DB cluster the tenant databases are provisioned on.
5. Create additional platform admins under **Platform Users**; assign platform
   roles only as needed (platform admins have no implicit tenant data access —
   support access is a separate, audited grant).

## 2. Tenant creation and activation

1. **Tenants → Create tenant**: code (subdomain), name, region, time zone,
   admin email/name, plan.
2. Choose admin credentials mode: *set temporary password* (shown once) or
   *invitation email*.
3. Provisioning runs asynchronously: creates `hrms_tenant_<number>` database,
   migrator/app roles, runs all migrations, seeds tenant permissions/roles,
   registers tenant domains. **Retry** from the tenant detail page if it fails
   — provisioning is idempotent.
4. Activation: tenant moves to active when provisioning succeeds; admin signs
   in at `https://<code>.<base-domain>` (`http://<code>.localhost` in local
   dev).

## 3. Subscription & feature enablement

1. Tenant detail → **Subscription**: assign/upgrade plan; entitlement version
   bumps and the tenant's entitlement cache invalidates.
2. Per-module overrides: enable/disable individual modules and features
   (`tenant_module_entitlements`, `tenant_feature_entitlements`).
3. Entitlement is enforced fail-closed in the API pipeline — a disabled module
   returns an entitlement error to every caller, including devices.

## 4. Company & organization setup (Tenant Admin)

Order matters — employees reference these masters:

1. **Companies** → addresses, contacts, bank accounts, statutory registrations.
2. **Branches** and **work locations** (branch ↔ site).
3. **Cost centres**, **grades**, **designations**, **departments**
   (department → branch, optional HOD with history).
4. Reporting structure derives from departments/managers (`org.reporting_closure`).

## 5. Roles & permissions

1. **Settings → Roles**: clone a seeded role or create one; grant from the
   permission catalog (module-scoped, e.g. `hr.employees.manage`,
   `attendance.devices.manage`).
2. **User permission overrides** for exceptions; a user's grants can never
   exceed the grant ceiling of the assigner.
3. **Data scope**: assign org units to a role/user so they see only their
   branch/department's people. Tenant-wide vs scoped visibility is enforced in
   services, not the UI.

## 6. Employee onboarding

1. **Employees → New employee**: personal details, contacts, statutory IDs,
   bank account, documents (file upload).
2. Employment record: company, branch, department, designation, grade,
   joining date, manager, employment type.
3. Salary: pick a salary structure + effective date → component lines
   generated from the structure version.
4. Letters: generate from letter templates (TipTap editor; issued letters are
   immutable PDFs; reissue/revoke supported).
5. Lifecycle events (confirmation, transfer, promotion, separation) append to
   `org.employment_history` — history is preserved, never overwritten.

## 7. Attendance configuration (M3.1 + M3.2)

1. **Attendance Setup** (`/attendance/admin`): shifts (versioned — draft →
   published → retired), policies (grace, OT permission), calendars (weekly
   offs, holidays), day-type rules.
2. **Assignments**: attach shift/policy/calendar to company → branch →
   department → employee (most specific wins; resolver verifies per date).
3. **Devices** (M3.2): register each biometric device, issue token, set
   timezone mode, map badge codes to employees (see
   [punch-ingestion.md](../attendance/punch-ingestion.md)).
4. Import historical punches via **Import Punches** CSV before cutover.

## 8. Biometric device setup

1. Device must reach `POST /api/v1/attendance/devices/{code}/punches` over
   HTTPS with header `Authorization: Device <token>`.
2. Payload: `{"punches":[{deviceUserCode, timestamp, direction?}],
   "idempotencyKey":"<unique per batch>"}`; max 1 MB per push — batch more.
3. Timestamp contract: naive wall-clock (device timezone mode) or ISO-8601 UTC
   (UTC mode). The server normalizes and stores both.
4. Offline: device buffers and pushes later; idempotency key makes retries
   safe. Push in chronological order when possible.
5. Vendor pull/connector mode: **PENDING** (designed; push + CSV cover M3.2).

## 9. Google Maps / geofencing

**Status: PENDING** — self-punch capture exists; geofence validation is
planned for the field-workforce milestone.

## 10. Bulk upload templates

CSV punch import is live (M3.2). The generic import/export engine (all masters
+ employees) is **PENDING** — track under `bulk-import-export/`.

## 11. Workflow & approval setup

**Status: PENDING** — the shared `core.workflow` engine + unified work inbox
are approved (A3) and precede Leave. When built: define workflow → stages →
approver rules (manager/dept-approver/threshold) → delegate + escalate; all
modules (Leave, Expense, Payroll, Exit) reuse it.

## 12. Reporting configuration

Dashboard framework is live (widgets + assignments per role/user). Module
report packs arrive with the reporting milestone — **PENDING**.

## 13. Integration prerequisites

| Integration | Status | Setup |
|---|---|---|
| Email (SMTP/Mailpit dev) | Live | `Email:Smtp` host/port; templates under Notifications |
| File storage (local/S3) | Live | `FileStorage:Provider`, bucket+prefix per tenant |
| Biometric devices | Live (push) | §8 |
| Payroll/statutory providers | Pending | — |
| Accounting/JV export | Pending | — |

## 14. Troubleshooting

| Symptom | Check |
|---|---|
| Tenant stuck in provisioning | Tenant detail → provisioning log; `retry-tenant` is idempotent |
| 401 on tenant login | wrong realm (tenant host vs admin host), locked user, or entitlement suspended |
| `entitlement` errors after plan change | entitlement cache — version bump propagates in seconds |
| Device pushes fail | token rotated? device active? body ≤ 1 MB? no cookies sent? |
| Import shows all duplicates | identical re-upload — by design; correct mapping creates new batch |
| Letters missing data | template version vs employee fields — issue → inspect merge fields |

## 15. Go-live checklist

- [ ] Platform: catalog synced, plans reviewed, cluster healthy, backups configured
- [ ] Tenant: provisioned, plan + module entitlements verified
- [ ] Org: companies/branches/departments/designations/grades loaded
- [ ] Security: roles reviewed, data scopes assigned, overrides audited
- [ ] People: employees onboarded, managers set, salary structures assigned
- [ ] Attendance: shifts published, calendars current, assignments effective,
      devices registered + mapped, backfill verified
- [ ] Imports: historical punches imported and counted
- [ ] Smoke: admin login, employee login, device push, import, punch viewer —
      each verified on production-like data
- [ ] Runbook: provisioning retry, secret rotation, backup restore rehearsed
