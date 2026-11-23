# HRMS Documentation Standards

Version: 1.0 · Last updated: 2026-11-23 · Owner: engineering (docs maintained per milestone)

## Purpose

Every HRMS module ships with user-facing documentation in the same milestone
that delivers the code. Documentation is part of milestone acceptance, not
optional follow-up work. This file defines the structure, conventions and
quality bar every manual must meet.

## Repository layout

```
docs/
  platform-admin/          # Platform Admin / Super Admin manuals
  tenant-admin/            # Tenant Admin manuals
  employee-management/     # HR admin: employees, onboarding, lifecycle, letters
  attendance/              # Attendance: masters, punch ingestion, calculation
  field-workforce/         # Mobile punches, geofencing, devices in the field
  leave/                   # Leave management
  payroll/                 # Payroll, salary, payslips
  tax/                     # Statutory: PF/ESI/PT/TDS
  loans-expenses/          # Loans and expense claims
  exit-full-final/         # Resignation, exit, full & final settlement
  recruitment/             # Recruitment and onboarding handoff
  assets/                  # Asset allocation and returns
  performance/             # Performance management
  reporting/               # Dashboards, reports, exports
  bulk-import-export/      # Bulk upload templates and jobs
  integrations/            # Email, storage, biometric, payroll-provider integrations
  troubleshooting/         # Cross-module troubleshooting and go-live checklist
  implementation/          # Implementation & setup guide for implementers
  training/
    video-index.md         # All planned videos by audience track
    scripts/               # One file per video script
  manuals-shared/          # This file, checklists, conventions
```

## Manual structure

Every module manual must contain, in this order:

1. **Introduction** — what the module does, who uses it (business language first).
2. **Prerequisites** — entitlements, masters, data that must exist first.
3. **Required permissions** — permission codes and the roles that typically hold them.
4. **Initial setup and configuration** — tenant-level settings.
5. **Screens** — one subsection per screen with a real screenshot, numbered
   callouts, field-by-field table, and step-by-step operating instructions.
6. **Worked examples** — realistic end-to-end scenarios with expected results.
7. **Validation rules** — every enforced rule and the message the user sees.
8. **Common errors and troubleshooting** — symptom → cause → fix.
9. **Reports and exports** — available outputs, formats, filters.
10. **FAQ** — at least five questions a first-time user would ask.

## Status markers

- A section covering functionality that does not exist yet is marked
  `**Status: PENDING — not yet implemented.**` Do not describe pending screens
  as if they work.
- Screenshots may only come from the running application. Never use mockups,
  wireframes or edited images as evidence of implemented functionality.
- Screenshots use demo tenants and demo people only. No real client or
  employee personal data may appear.

## Screenshot conventions

- Capture at 1280×800 or larger, full browser window unless a detail crop is
  clearer.
- Annotate with numbered callouts (① ② ③ …) matching the steps that reference
  them.
- Filename: `docs/<module>/images/<milestone>-<screen>-<step>.png`,
  e.g. `attendance/images/m32-devices-01.png`.
- Show at least one validation/error state per module where one exists.

## Versioning

- Each manual carries `Version`, `Last updated`, and `Applies to` (the
  milestone/release it documents) in a header line.
- Docs change on the same branch/PR as the milestone code so they stay in sync
  with what ships.

## Role-based manuals

Role manuals live under `platform-admin/` and `tenant-admin/` and aggregate
the module manuals: they explain *what this role can do and the order to do it
in*, linking into module manuals for screen detail. Roles covered: Platform
Admin/Super Admin, Tenant Admin, HR Admin, Department/Branch Manager, Payroll
Admin, Recruiter, Employee/ESS, Implementation Engineer.

## Video scripts

- One file per planned video under `training/scripts/`.
- A script is not a video: files are named `-script.md` and the video index
  lists status `Script ready` vs `Recorded`. Never mark a video recorded when
  only the script exists.
- Each script states: title, audience, objective, prerequisites, demo data,
  timed demonstration sequence, narration, estimated duration, and the manual
  section it corresponds to.
