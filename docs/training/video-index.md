# HRMS Video Training Index

Version: 1.0 · Last updated: 2026-11-23

Status legend: **Script ready** = written, not recorded · **Recorded** = video exists.
No video is marked recorded until a real screen recording ships.

## Platform Admin Training

| # | Title | Objective | Script | Status |
|---|---|---|---|---|
| PA-01 | Platform overview & tenancy model | Understand platform DB vs tenant DBs, realms | pending | Not started |
| PA-02 | Plans, modules & entitlements | Create plans, bind modules/features | pending | Not started |
| PA-03 | Tenant provisioning & lifecycle | Create tenant, watch provisioning, retry, suspend | pending | Not started |
| PA-04 | Platform users, roles & support access | Grant platform roles; audited impersonation | pending | Not started |

## Tenant Admin Training

| # | Title | Objective | Script | Status |
|---|---|---|---|---|
| TA-01 | First-login & company/org setup | Companies → branches → departments → masters | pending | Not started |
| TA-02 | Roles, permissions & data scope | Build roles, scoped access, overrides | pending | Not started |
| TA-03 | Attendance masters (M3.1) | Shifts, calendars, policies, assignments | pending | Not started |

## HR Operations Training

| # | Title | Objective | Script | Status |
|---|---|---|---|---|
| HR-01 | Employee onboarding end-to-end | Personal → employment → salary → letter | pending | Not started |
| HR-02 | Lifecycle events & letters | Transfer, promotion, issue/reissue letters | pending | Not started |
| HR-03 | Punch ingestion operations | Register devices, map badges, import CSV, void | [m3.2 script](scripts/m3.2-attendance-punch-ingestion.md) | **Script ready** |

## Payroll Training

| # | Title | Objective | Script | Status |
|---|---|---|---|---|
| PAY-01 | Salary structures & assignments | Components, structures, effective dating | pending | Not started |
| PAY-02 | Payroll run end-to-end | Attendance + leave inputs → net pay → payslip | pending | Not started |

## Employee Self-Service Training

| # | Title | Objective | Script | Status |
|---|---|---|---|---|
| ESS-01 | Employee portal tour & My Punches | Login, profile, self-punch, own punch history | pending (folds into HR-03) | Not started |

## Field Workforce Training

| # | Title | Objective | Script | Status |
|---|---|---|---|---|
| FW-01 | Field punches & offline sync | Device/offline punch capture, geofence | pending (module pending) | Not started |

## Implementation & Support Training

| # | Title | Objective | Script | Status |
|---|---|---|---|---|
| IM-01 | Deploy & seed a fresh environment | Compose stack, migrate, seed, first tenant | pending | Not started |
| IM-02 | Biometric device onboarding | Token issuance, push contract, offline, troubleshooting | folds into HR-03 | **Script ready** (shared) |
