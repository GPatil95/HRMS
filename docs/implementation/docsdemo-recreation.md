# Demo tenant `docsdemo` — recreation steps

Local development only. `docsdemo` (tenant 1007) is a throwaway tenant used for
documentation screenshots. It contains **dummy data only** — no real employee,
client or production information. Recreate it any time with the steps below.

## 1. Provision

```bash
ConnectionStrings__Platform='Host=localhost;Port=5432;Database=hrms_platform;Username=postgres;Password=postgres' \
ConnectionStrings__Redis=localhost:6379 \
Secrets__MasterKeyFile=/home/ubuntu/.hrms-dev-masterkey \
dotnet run --project src/backend/Hrms.Cli -- create-tenant \
  --code docsdemo --name "Docs Demo Co" \
  --admin-email admin@docsdemo.test --admin-name "Docs Admin" \
  --admin-password "Docs-Demo-9917"
```

Creates its own database `hrms_tenant_<n>` and applies **all** tenant
migrations (including M3.1/M3.2 `att` schema). Touches nothing else.
Host: `http://docsdemo.localhost` (vite) / API on `:5180` for this box.

## 2. Enable modules (platform admin)

`PUT /api/v1/platform/tenants/{tenantId}/modules` as platform admin
(`admin@hrms.local`), body `{"moduleCode":"<code>","enabled":true,"reason":"docs demo"}`
for: `attendance`, `attendance.biometric`, `employee.letters`,
`employee.documents`, `mywork`.

## 3. Seed dummy data (tenant admin API)

Login `admin@docsdemo.test` / `Docs-Demo-9917` at
`http://docsdemo.localhost:5180` (fetch `/api/v1/auth/csrf`, POST
`/api/v1/auth/login` with `X-CSRF-TOKEN`).

1. Org: `POST org/companies` `{code:"DDC", name:"Docs Demo Co"}` →
   `POST org/branches` `{companyId, code:"HQ", name:"Headquarters"}` →
   `POST org/departments` `{code:"ENG", name:"Engineering", branchId}`.
2. Employees: `POST employees` × 3 — `EMP001 Priya Nair`, `EMP002 Arjun Shah`,
   `EMP003 Meera Iyer` (`employmentType:"permanent"`, branch/department above).
3. Masters: `POST attendance/shifts` `{code:"GEN", name:"General 9-6"}` →
   `POST shifts/{id}/versions` (09:00–18:00, 60m break, grace 10) →
   `POST shift-versions/{id}/publish` `{effectiveFrom:"2026-11-01"}`.
   `POST attendance/calendars` `{code:"WC26", name:"Work Calendar 2026", isDefault:true}` →
   `POST calendars/{id}/days` two holidays.
   `POST attendance/policies` `{code:"STD", name:"Standard Policy", isDefault:true}` →
   `POST policies/{id}/versions` (grace 10/30, half-day 240, absent 120,
   punch first_last, missingPunch absent) → `POST policy-versions/{id}/publish`.
   Assign all three employees: `POST attendance/shift-assignments` and
   `attendance/policy-assignments` `{effectiveFrom:"2026-11-01"}`.
4. Device: `POST attendance/devices` `{code:"BIO-HQ-01", name:"HQ Main Door
   Biometric", protocol:"push_api", timezoneMode:"device_local",
   deviceTimezone:"Asia/Kolkata", branchId}` → response includes the
   **one-time** `token`.
5. Badge map: `POST attendance/devices/{id}/user-map` × 2 —
   `{deviceUserCode:"42", employeeId:EMP001}` and `"43" → EMP002`. Badge `77`
   is intentionally left unmapped to demo unmapped-punch capture.
6. Punches:
   - Device push (no cookies, no CSRF):
     `POST /api/v1/attendance/devices/BIO-HQ-01/punches` with
     `Authorization: Device <token>` + `Idempotency-Key` header, body
     `{"punches":[{deviceUserCode,timestamp,direction}...]}` — naive local
     wall times in device-local mode.
   - Manual: `POST attendance/punches/manual` `{employeeId:EMP003, punchedAt,
     direction:"in", note:"Forgot badge at gate"}`.
   - CSV: upload `docs/attendance/samples/demo-punches.csv` via
     `POST /api/v1/files` (form fields `file` +
     `purpose=attendance.punches.import-csv`), then
     `POST attendance/punches/import` `{fileId, deviceId, mapping:
     {deviceUserCodeCol:0, timestampCol:1, directionCol:2, employeeCodeCol:-1},
     skipHeader:true}`. One row dedups against the earlier push (cross-channel
     dedup demo).

## 4. Screenshots

Playwright CDP (`http://localhost:29229`): log in at
`http://docsdemo.localhost:5174/login`, then capture
`/attendance/admin` (+ each tab), `/attendance/punches`, `/attendance/import`,
`/attendance/imports`, `/attendance/devices`, `/attendance/my`, `/employees`.
Outputs land in `docs/attendance/images/` (naming: `m3.x-<screen>.png`).

## 5. Boundaries

`docsdemo` is the only tenant these steps touch. Do **not** run
`migrate-tenants` or any seeding against `permanenttest` or any real tenant.
Delete `hrms_tenant_<n>` to discard the demo entirely.
