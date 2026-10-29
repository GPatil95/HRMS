# 10–11. Authorization & Module Entitlement Architecture

## 10.1 Evaluation order (every request, fail-closed)

```
Authentication ─► Tenant resolution ─► Subscription check ─► Module entitlement ─► Feature entitlement
   ─► User permission ─► Data scope ─► Business operation
```

| Step | Implemented by | Failure |
|---|---|---|
| Authentication | ASP.NET Core auth (cookie / JWT) | 401 |
| Tenant resolution | `TenantResolutionMiddleware` + `tid` match | 401 / 404 (unknown host) |
| Subscription | `SubscriptionRequirement` (global policy for all tenant endpoints) | 403 `subscription-inactive` (or read-only) |
| Module | `[RequireModule("payroll")]` — applied **per controller group by convention** from the module definition, not by developers remembering | 403 `module-not-enabled` |
| Feature | `[RequireFeature("payroll.multi_company")]` on specific endpoints; quota features checked in application service (`IEntitlementService.EnsureWithinLimit`) | 403 `feature-not-enabled` / 409 `quota-exceeded` |
| Permission | `[RequirePermission("payroll.run.process")]` → `PermissionAuthorizationHandler` | 403 `permission-denied` |
| Data scope | Application layer: `IDataScopeProvider.For("employee.profile.view")` → query filter / record check | list filtered; single record outside scope → **404** (no existence leak) |

ProblemDetails `type` codes are stable so the UI can show "This module is not part of your subscription" vs "You don't have access".

**Fail-closed guarantees** (enforced by `Hrms.ArchitectureTests` + a startup check):
- Every controller action in a tenant module has `[RequirePermission]`, `[AuthenticatedOnly]` (e.g. `/me`), or `[AllowAnonymous]` (with an allow-list file reviewed by security).
- Every controller in module X has module requirement X (added by convention; test asserts it).
- No tenant endpoint accepts platform-realm tokens and vice versa.

## 10.2 Permission model

**Permission code** = `{module}.{resource}.{action}` — e.g. `org.branch.view`, `employee.profile.edit`, `payroll.run.process`, `payroll.run.finalize`, `payroll.payslip.download`, `employee.document.share`.

Menu/page access: menus and pages are declared in manifests with the permission(s) that make them visible (usually `*.view`); there is no separate "menu permission" to keep in sync, but a dedicated `{module}.menu.access` code exists for top-level module menus where a customer wants to hide a module from a role entirely.

Actions supported: `view`, `create`, `edit`, `delete`, `approve`, `reject`, `import`, `export`, `download`, `process`, `finalize`, `share`, **`configure`** (module settings/masters) (+ module-specific where justified, e.g. `reopen`, `revise`, `unlock`, declared in manifests).

Menu hierarchy: **menu → sub-menu → page** items are declared in manifests; each item lists the permission(s) that make it visible. A menu/sub-menu is shown if any child page is visible, or explicitly via `{module}.menu.access` / `{module}.{area}.menu.access` codes when a customer wants to hide a whole area from a role.

### Grants

```
Role ──(role_permissions: permission + scope_level)──► Permission
User ──(user_role_assignments: valid_from/to)──► Role
         └─(assignment_scopes: which branches/departments/employees)
User ──(user_permission_overrides: grant|deny + scope_level, valid_from/to, reason)──► Permission
```

Example: role **Branch HR Executive** has `employee.profile.view @ branch` and `employee.profile.edit @ branch`. Priya is assigned this role with scope targets *Pune* and *Nashik* branches. Rahul is assigned the same role with no targets → defaults to his own branch.

### Effective permission computation

1. Collect active role assignments → (permission, scope_level, targets).
2. Add active `grant` overrides.
3. Remove permissions with an active `deny` override (**deny wins**, for that permission entirely).
4. Combine multiple grants of the same permission by **union** of scopes (e.g. `own` ∪ `branch[Pune]`).
5. Result `EffectivePermissionSet { code → ScopeSpec }` cached in Redis at `t:{tid}:perm:{userId}:{permVersion}` (tenant-wide `permission_version` bumped on any role/permission/assignment/override change; user-specific changes also bump the user stamp).

Sensitive permissions (`is_sensitive`: payroll, salary view, bank details, bulk export, role management) can require **step-up MFA** (recent `amr=mfa` within N minutes).

### Guard-rails
- System roles (Tenant Admin) cannot be deleted or have their last member removed.
- A user cannot grant permissions they do not hold (no privilege escalation via role editing), and cannot edit their own roles.
- Role/permission/override changes are audited as security events with before/after.

## 10.3 Data scopes

| Scope level | Meaning | Implementation |
|---|---|---|
| `own` | records of the user's own employee | `employee_id = current.EmployeeId` |
| `direct_reports` | own direct reports | `reporting_closure depth = 1` |
| `all_reports` | whole reporting tree | `reporting_closure depth >= 1` |
| `department` | employees in assigned (or own) departments, incl. sub-departments | join on department tree |
| `branch` | employees in assigned (or own) branches | `branch_id = ANY(@branchIds)` |
| `company` | employees of assigned (or own) company / legal entity | `company_id = ANY(@companyIds)` |
| `selected_employees` | explicit employee list | `employee_id = ANY(@ids)` |
| `tenant` | everything in the tenant (all companies) | no filter |

Every business record that belongs to a person carries `employee_id` (and where useful denormalised `branch_id`/`department_id` *at time of record* for historical correctness, e.g. payroll). Scope filters are applied through one shared mechanism:

```csharp
var scope = await dataScope.For(Permissions.Employee.ProfileView, ct);     // ScopeSpec
var query = db.Employees.ApplyScope(scope, e => e.Id);                     // IQueryable extension, translates to SQL
// single record:
if (!await dataScope.CanAccessEmployee(scope, employeeId, ct)) return Result.NotFound();
```

Rules:
- Application handlers for scoped permissions **must** call `ApplyScope`/`CanAccessEmployee` (code review + integration tests for each endpoint in the authorization matrix).
- Scope uses *current* org assignment for "who can see whom", while historical records keep their at-the-time branch for reporting.
- Self-service endpoints (`/me/...`) use `own` scope implicitly.

## 10.4 Dynamic menus

`GET /api/v1/me/navigation` returns the menu tree assembled from module manifests, filtered by: module entitled ∧ feature entitled ∧ user has any of the item's permissions. `GET /api/v1/me/session` returns `{ user, tenant branding, entitledModules, features, permissions[] (codes only), scopes summary }` for UI gating. The frontend renders; the backend enforces.

## 11. Module entitlement

### 11.1 Entities (Platform DB)
`Module`, `Feature`, `SubscriptionPlan` (+ `plan_modules`, `plan_features`), `TenantSubscription`, `TenantModuleEntitlement`, `TenantFeatureEntitlement` — see DB doc §6.

**Effective entitlements** for a tenant = (plan modules/features of the active subscription) ∪ (active add-ons) ∪ (manual/trial grants) − (manual disables), each within `valid_from/valid_to`, with dependency closure validated (cannot enable `tax` without `payroll`).

### 11.2 Runtime
- `IEntitlementService` (in API/Worker) reads a compact `TenantEntitlements { version, modules, features{code→value} }` from memory → Redis `plat:ent:{tenantId}:{version}` → Platform DB.
- Platform admin change → bump `entitlement_versions.version` → Redis pub/sub `entitlements-changed {tenantId}` → all instances drop local cache. Worst-case staleness bounded by local cache TTL (30 s).
- Tenant DB receives an `EntitlementsChanged` notification so modules can react (e.g. pause scheduled jobs of a disabled module, hide widgets).

### 11.3 What "disabled" means
- APIs of the module → 403 `module-not-enabled` (including direct URL calls, exports, file downloads owned by that module, SignalR hub methods, and background jobs — job filter checks entitlement before executing tenant jobs of that module).
- Data is **retained** (not deleted) while disabled; re-enabling restores access. Data deletion follows the contract/offboarding policy.
- Integrations: consumers get the "not available" implementation (see below).

### 11.4 Optional integrations between modules

```csharp
public interface IOptionalIntegration<T> where T : class
{
    bool IsAvailable { get; }    // module entitled for current tenant AND registered
    T? Service { get; }
}

// Payroll application service
var attendance = attendanceIntegration.IsAvailable
    ? await attendanceIntegration.Service!.GetPaidDays(period, employeeIds, ct)
    : await manualInputs.GetPaidDays(period, employeeIds, ct);   // imported/entered by payroll admin
```

- Event consumers in module B for events from module A run only if B is entitled (checked by dispatcher).
- Module settings that reference another module (e.g. "deduct LOP from leave") are only offered when both are entitled.

### 11.5 Attendance without biometric
`attendance.biometric` is a **sub-module** of `attendance`. Attendance capture sources (`web`, `mobile`, `manual`, `import`, `biometric`) are features; biometric is just one source writing the same punch model.

## 10.5 Grant ceiling rule (no privilege escalation)

A user can never grant more than they hold. Enforced in `identity` application services (not UI):

| Operation | Rule |
|---|---|
| Add permission P @ scope S to a role | actor holds `identity.role.edit` **and** holds P with scope ≥ S |
| Assign role R (with targets) to a user | actor holds `identity.user.role.assign` **and** holds **every** permission of R at ≥ the role's scope level, and the assignment targets (branches/departments/employees/companies) are within the actor's own scope |
| Create a `grant` override P @ S | actor holds `identity.override.manage` and P @ ≥ S; expiry required for sensitive permissions |
| Create a `deny` override | actor holds `identity.override.manage` and the target user is within actor's scope (removing access is not escalation) |
| Self-administration | users cannot change their own roles/overrides; the last Tenant Admin cannot be removed; only Tenant Admins assign the Tenant Admin role |
| Platform realm | identical rules for platform roles |

Scope comparison uses the partial order `own ⊂ direct_reports ⊂ all_reports`, `department ⊂ branch ⊂ company ⊂ tenant`, `selected_employees` — and when levels are not comparable, the resolved employee set of the granted scope must be a subset of the actor's resolved set at grant time. Violations return 403 `grant-exceeds-own-permission` and are logged as security events.

## 10.6 Worked examples (from the review brief)

| Case | Configuration | Effective result |
|---|---|---|
| John, role HR Executive | role: `employee.profile.view @ branch`; override: grant `employee.profile.edit @ branch` (expires 31-Mar, reason) | John can view and edit employees of his branch |
| Payroll viewer without export | role: `payroll.register.view @ tenant`; override: **deny** `payroll.register.export` | view allowed, export 403 (deny wins) |
| Manager | `attendance.record.view @ direct_reports` | sees only direct reports' attendance |
| HR Admin | `attendance.record.view @ tenant` | all attendance in the tenant |
| Branch HR | `attendance.record.view @ branch` (targets: Pune) | only Pune employees |
| Employee | `attendance.record.view @ own` | only own attendance |

Every module, widget, report, export, import, work-inbox action, notification payload and AI tool uses this same evaluation — there is one authorization model.
