# 18. Talent, Workplace and Productivity Modules

## 18.1 Recruitment / ATS (`recruitment`)

Job requisition (position, branch, department, budget/CTC band, headcount, justification) → approval workflow → job posting (career page per tenant, job-board integration point) → candidates (dedupe by email/phone, source: referral/portal/agency/career page/import; candidate consent & retention period) → resume (`core.files`, parsing integration point) → pipeline stages (configurable) → screening → interviews (panel, schedule via `ICalendarProvider` optional, feedback scorecards, interviewer sees only assigned candidates) → offer (CTC breakup — uses Payroll structure templates if entitled, else manual; offer letter via `employee.letters` optional) → offer approval workflow → joining → **candidate-to-employee conversion** via `IEmployeeOnboarding` (copies agreed data; candidate record retained/anonymised per retention). Candidates are not employees: separate schema, separate permissions, external candidate portal later. Independent from Payroll.

## 18.2 PMS (`pms`)

Performance cycles (annual/half-yearly/quarterly, phases with dates), goal libraries, KRAs/KPIs, goal assignment (cascading, weights = 100%), check-ins, self appraisal, manager appraisal, reviewer/skip-level, competencies & rating scales, calibration/normalisation, final rating, PIP (plan, milestones, outcome), increment/promotion recommendations → **integration event** `IncrementRecommended` consumed by HR Core lifecycle (and Payroll revision if entitled). Payroll never depends on PMS. Confidentiality: ratings `restricted`, visibility by phase settings.

## 18.3 Calendar & Meetings (`calendar`)

Personal/team calendars, meetings (organizer, participants internal/external, agenda, location/room/online link), invitations (iCalendar `.ics` via email, accept/decline/tentative), recurring meetings (RFC 5545 RRULE, exceptions), reminders (jobs), meeting history & notes. **Calendar feed** aggregates events from entitled modules via integration events (approved leave, holidays, interviews, room bookings, project milestones) — each item filtered by the viewer's permission/scope. External calendar integration architecture: per-user OAuth connections to Microsoft 365 / Google (tokens encrypted with tenant DEK, refresh in Worker), one-way publish first, two-way sync later; webhook subscriptions for changes; disconnect on exit.

## 18.4 Meeting Room Booking (`rooms`)

Office → floor (from `core.org`) → room (capacity, equipment, bookable-by policy, approval-required flag, buffer time, operating hours). Availability search, booking (single/recurring with RRULE), **conflict detection enforced in DB** via exclusion constraint on `(room_id, tstzrange(start, end))`, approval workflow if required, cancellation, check-in window (QR/app), auto-release & no-show marking job, booking history. Optional: create calendar event (`ICalendarProvider`). Visitors can reference a booking.

## 18.5 Workplace / Office / Seat Management (`workplace`)

```
Office (core.org location) → Floor (core.org) → Zone (workplace) → Seat/Desk (workplace)
```

| Concept | Design |
|---|---|
| Configurable layout | tenant defines zones, desk types (hot, reserved, assigned, accessible, standing...), seat numbering pattern, amenities; nothing hard-coded |
| Floor plan metadata | uploaded floor-plan image (`core.files`) + seat coordinates/polygons as JSON (`x,y,w,h`/polygon) for interactive maps |
| Seat states | available, reserved (assigned to employee/team), hot desk, disabled, under maintenance (effective-dated blocks) |
| Booking | daily/half-day/time-slot bookings; exclusion constraint prevents double-booking; booking policies (advance window, max per week, team neighbourhoods) |
| Hybrid / WFH | hybrid schedule policies (e.g. 3 office days/week), planned office days; WFH requests owned by Attendance (optional integration) |
| Office attendance | seat check-in (QR/app/geo) publishes `SeatCheckedIn` → Attendance (optional) records `workplace_checkin` punch; occupancy analytics |
| Exit | `EmployeeExited` releases reserved seats |

## 18.6 Visitor Management (`visitors`)

Pre-registration by host (invite link/QR), walk-in registration at reception, host employee, purpose, visitor category (guest, vendor, interview candidate, contractor), ID verification (type + **masked** number; ID image optional, short retention e.g. 30 days, consent notice), photo (consent, short retention), NDA/policy acknowledgement, check-in/out, visitor pass (printable badge/QR), reception dashboard (expected/in-building/overdue), host notification (in-app/email/SMS), visitor history, watchlist **only where legally appropriate** (feature flag off by default, reason + approver required, periodic review, audited), evacuation list. Personal data of visitors has its own retention policy and deletion job.

## 18.7 Mail / Parcel Management (`mailroom`)

Incoming/outgoing items, courier master, tracking number, recipient employee/department, received date/time, received by, photo/proof (`core.files`), storage location, notification to recipient, acknowledgement/collection (OTP/QR/signature), delivery, return to sender, pending items ageing & reminders, history, audit (`parcel.received`, `parcel.collected`).

## 18.8 Asset Management (`assets`)

Categories (with custom attributes), asset master (asset number from number series, serial number unique per category, model, purchase date/cost/PO/invoice, **vendor from core.org partners**, warranty/AMC expiry, depreciation metadata), lifecycle: in-stock → assigned (employee/branch/department/location allocation) → transferred → returned → damaged/lost (incident, recovery amount) → maintenance → disposed (approval). Full asset history (append-only), acknowledgements by employee, warranty/AMC expiry reminders, import. **Exit clearance**: provides `IAssetClearanceProvider` (allocated assets, recovery amounts) consumed optionally by Exit/FNF; asset return tasks created on `EmployeeExitApproved`.

## 18.9 Project Management (`projects`)

Projects (client from core.org partners, billable flag, budget hours), members & project roles, tasks/sub-tasks, milestones, due dates, priority, status workflow, timesheets & work logs (approval by project manager), daily updates, comments (mentions), attachments (`core.files`), reports (effort, utilisation, burn-down). Provides `ITimesheetProvider` (billable hours) optionally to Client billing. **Independent of Payroll** (no dependency either way). Project tasks assigned to a user surface in the work inbox (`core.workitems`).

## 18.10 My Work / Checklist / Daily Update (`mywork`)

Personal tasks (title, notes, due date, priority, status, follow-up date, attachments), personal checklists (templates + instances, e.g. onboarding checklist owned by HR), daily work updates (date, summary, linked tasks/projects, visible to manager per setting), follow-ups and reminders. **"My Work" view** combines personal tasks with the unified work inbox (`core.workitems`: approvals, workflow tasks, HR activities, recruitment actions, project tasks). Without `mywork`, users still get the core work inbox for approvals.
