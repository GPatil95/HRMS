# M3.2 — Punch Ingestion & Raw Attendance Records: Verification Report

Base: `main` @ `5517b3d` (M3.1 squash-merge). Branch: `m3.2-attendance` @ `2ede179`.

## Test & build results

| Suite | Result |
|---|---|
| `dotnet test tests/api/Hrms.ApiTests -c Release` | **150/150 passed** (11 new ingest cases + full regression) |
| `dotnet test tests/unit/Hrms.UnitTests -c Release` | **45/45 passed** (12 new: dedup/idempotency + deferred `shift_based`/`RotationRule`/`WeeklyOffRules`) |
| `pnpm exec tsc -b` | 0 errors |
| `pnpm exec eslint` (changed dirs) | 0 errors |
| `dotnet build Hrms.slnx -c Release` | 0 errors / 0 warnings |
| `pnpm build` | clean |

## Coverage map (M3.2 scope → evidence)

| Requirement | Evidence |
|---|---|
| Device registration + token auth | `devices` table; token sha256-hashed, plaintext shown once, rotatable. `DeviceTokenAuthenticatesPushAndUpdatesLastSeen`, `BadInactiveAndCrossTenantTokensAreRejected` |
| Employee/device mapping | `device_user_map` + exclusion constraint → overlapping effectivity returns 409; `DeviceUserMap` CRUD; back-fill job `attendance.device.map` |
| Punch deduplication | Content `dedup_key` unique per tenant; `SameContentUnderNewKeyCountsAsDuplicates` |
| Batch idempotency | `ingest_batches.idempotency_key` unique + `payload_hash` mismatch → 409 (`SameKeyReplaysSamePayloadAndRejectsDifferentPayload`) |
| CSV import | `POST /attendance/punches/import` via FileService; per-row errors in `ingest_batches.error_rows`; partial accept + file-replay (`CsvImportPartialAcceptsAndReplaysByFile`) |
| Error handling | Standard error envelope (`device.*`, `csv.row`, `409` conflicts); failed rows captured per-row |
| Audit logging | Creates/voids/imports audited via `IAuditWriter` in service layer |
| Tenant isolation | Tokens resolve per-tenant; cross-tenant token rejected; shared-DB isolation tests pass |
| Permissions | `attendance.devices.*`, `attendance.punches.*`, `attendance.my.*`; `PunchViewerEnforcesDataScope`, `ManualPunchRequiresNoteAndPermissionAndCanBeVoided` |
| Immutability | `att.punches_immutable()` trigger: DELETE + UPDATE rejected; only `employee_id` back-fill allowed (`PunchRowsCannotBeUpdatedOrDeletedExceptBackfill`); voids in `punch_voids` |
| Timestamps/timezone/raw evidence | `punched_at` (UTC timestamptz) + `local_wall_time` verbatim + `raw_payload` jsonb; `DeviceLocalTimestampsConvertToUtcAndPreserveWallTime` (IST 09:30 → 04:00 UTC) |
| Offline sync / unmapped punches | Stored unmapped, back-filled on mapping (`UnmappedPunchesAreStoredAndBackfilledWhenMapped`) |

## Migration

`20261123051831_AttendanceRawPunches` — additive `att` schema only (5 tables + trigger + exclusion index). **Generated; not applied to any tenant database.** Application requires explicit approval per tenant.

## Not covered / deferred

- `.xlsx` import — CSV-only for M3.2 (D4 decision: no new parser dependency; FileService supports `.csv` today; `.xlsx` deferred to a later milestone if needed).
- `pull_poll` device protocol — seam in schema/config only; no pull daemon (design choice: push + CSV covers M3.2).
- No attendance calculations, late marks, overtime, day attribution — explicitly out of scope (M3.3+).
- No frontend e2e — manual UI pass needed on a live tenant.

## Working demonstration

Verified via the API suite (Testcontainers postgres:17-alpine): device token auth → push → dedup replay → unmapped→mapped back-fill → manual/self punches → void → CSV import with mixed good/bad rows → cross-tenant rejection → append-only enforcement. A live devtest walkthrough requires applying the migration to devtest (approval needed).
