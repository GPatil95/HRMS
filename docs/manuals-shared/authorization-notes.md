# Authorization notes and known limitations

> Version 1.0 — 2026-11-23 · Applies to M1–M3.2

## System-role grant synchronization is additive (accepted limitation)

The tenant seeding and catalogue-sync pipeline manages the built-in system roles
(`TENANT_ADMIN`, `MANAGER`, `EMPLOYEE`) **additively**:

- When a new permission is introduced (e.g. `attendance.my.view`, `attendance.my.punch` on
  `EMPLOYEE` in M3.2), `TenantSeedService.SyncRoleGrantsAsync` grants it to the system role
  on the next tenant catalogue sync / migration run.
- **Nothing is ever removed.** The sync only appends missing grant rows.

### Consequence

If an administrator manually removes a seeded grant from a **system role** (e.g. removes
`attendance.my.punch` from `EMPLOYEE`), the next catalogue synchronization will **restore it**.
Manual revocation of system-role grants is therefore **not durable today**.

- **Custom roles are never modified** by seeding or sync — only the three built-in system roles are topped up.
- HR/Admin attendance permissions (`attendance.view`, `punches.manual`, `punches.import`,
  `devices.manage`, `masters.*`, `assignments.manage`) remain separate and are never granted
  to `EMPLOYEE`.
- Employees' own-record access is structural: `punches/my` reads only the caller's linked
  employee, `punches/self` writes only to it, and admin operations require distinct permissions.

### Workaround today

Use a **custom role** (or a deny override on the user/role) instead of editing the system
role's seeded grants when a tenant wants to withhold a default capability.

## Authorization hardening backlog

| # | Item | Status |
|---|------|--------|
| AH-1 | Durable revocation: explicit permission-override / revocation markers on system roles so admin decisions survive catalogue sync (needs a tombstone/marker field `RolePermission` does not currently have). | Open — future authz design |
| AH-2 | Scope-aware seeded grants: `attendance.my.*` seeded at `Own`-equivalent semantics are already enforced structurally in the service; formal `ScopeLevel.Own` support on non-scoped action permissions is cosmetic. | Noted |
