# Milestone 1 — Platform Foundation: checkpoint report

Branch `m1-platform-foundation` (local repository; no Git remote has been provided, so nothing is pushed and CI has not run on GitHub).
Architecture is unchanged: **one application + one platform database + one PostgreSQL database per tenant**. No business modules were implemented; the employee directory is the minimal org/data-scope directory only.

## 1. Already implemented before this resume
- Architecture baseline in `docs/` (28 + 11 ADRs, module catalogue, threat model, roadmap).
- WIP scaffolding commit `f6f8e4e` (did not compile): solution layout, domain model, partial application layer.

## 2. Implemented in this milestone
| Area | What exists now |
|---|---|
| Solution | `src/backend` Api / Application / Domain / Infrastructure / Shared / Cli; central package versions; warnings-as-errors |
| Platform DB | `PlatformDbContext`, tenants, tenant DB registry, module/feature catalogue, plans, entitlements, platform users/roles/permissions, support grants, platform audit + security events, provisioning operations |
| Tenant DB | `TenantDbContext` (per physical DB, no shared `tenant_id`), identity, roles/permissions/overrides, org (companies/branches/departments), minimal employee directory + reporting closure, files, notifications, email outbox, dashboards, audit/security events, `ops.tenant_identity` stamp |
| Provisioning | Idempotent, step-recorded, retryable provisioning (DB + login + migrate + seed + identity stamp + admin invite); platform API and CLI (`migrate-platform`, `seed-platform`, `create-tenant`, `retry-tenant`) |
| Tenant resolution | Host → tenant registry → status check → DB resolution → identity-stamp verification, before any controller; client tenant IDs ignored |
| Authentication | Separate tenant (`hrms_t`) and platform (`hrms_p`) cookie realms, server-side sessions + security stamp, PBKDF2 hashing + policy, lockout, TOTP MFA, reset, invitation, email verification, login/reset rate limiting, CSRF |
| Authorization | Role permissions, user grant/deny overrides (deny wins, sensitive grants need expiry), grant ceiling (permissions, scopes, targets), 8 data scopes, module + feature entitlements checked first, dynamic navigation, platform permissions in their own realm |
| Common services | Append-only audit (app + triggers + grants, hash seal job), security events, file storage (local / S3-compatible, signature/extension/EICAR checks), notifications, email outbox + SMTP/Scriban, Hangfire + in-process jobs with trusted tenant re-establishment, Redis/in-memory cache, dashboards framework, reporting/Excel export |
| API standards | Correlation IDs, RFC 7807 errors (no internals leaked), validation, OpenTelemetry tracing, `/health/live` + `/health/ready`, OpenAPI, URL API versioning, security headers |
| Frontend | `src/frontend/web`: Vite + React + TS + MUI shell — login (+MFA step), forgot/reset password, accept invitation, platform admin realm on `admin.*`, dynamic sidebar from `/api/v1/me/navigation`, layouts, 404/403/error boundary, cookie + CSRF API client, generated OpenAPI types (`pnpm gen:api`) |
| Dev/CI | `infrastructure/docker/api.Dockerfile` (api + cli targets), `docker-compose.yml` (Postgres 17, Redis 7, Mailpit, optional api), `.github/workflows/ci.yml` (backend build/test, frontend lint/typecheck/build, docker builds) |

## 3. Files changed
All under `src/backend/**`, `tests/**`, `src/frontend/web/**`, `infrastructure/docker/**`, `.github/workflows/ci.yml`, `.dockerignore`, `.gitignore`, `docs/milestones/**`. See `git log 685e7af..HEAD --stat`.

## 4. Database migrations
- Platform: `20261024174156_InitialPlatform`, `20261024175630_TenantAdminName`
- Tenant: `20261024174159_InitialTenant` (includes append-only audit triggers)

## 5. APIs (all `/api/v1`)
- Tenant host: `auth` (csrf, login, mfa, logout, me, forgot/reset password, change password, MFA setup/enable/disable, verify email), `me` (permissions, navigation), `users` (CRUD, status, roles, overrides, session revoke, effective permissions), `roles` (+ permission catalogue), `org` (companies, branches, departments), `employees` (minimal directory), `files`, `notifications`, `dashboard`, `reports` (+ export)
- Admin host: `platform/auth`, `platform/tenants` (create/provision/retry/status/modules), `platform/support-grants`, platform catalogue
- `/health/live`, `/health/ready`, OpenAPI (Development)

## 6. Security controls
Host-only tenant resolution + DB identity stamp; per-tenant DB login with secrets encrypted by master key; realm-separated cookies (HttpOnly, SameSite, Secure outside dev); server-side session revocation; CSRF on all unsafe `/api` calls; lockout + rate limiting (429); MFA/TOTP; deny-by-default endpoint guards enforced by an architecture test; entitlement → permission → scope order; grant ceiling; append-only audit enforced in the DB; file type/signature/malware-signature checks and tenant-isolated keys; no secrets/passwords/tokens logged; security headers.

## 7–10. Automated tests (`tests/api/Hrms.ApiTests`, Testcontainers PostgreSQL 17, two real tenants)
**Tenant isolation** (`TenantIsolationTests`)
- unknown host is rejected
- platform api is not served on tenant host and vice versa
- session cookie from one tenant is rejected on another
- record id from another tenant is not found
- tenant id in query header and route is ignored
- tenant id in request body is ignored
- tenant databases are physically separate and identity stamped

**Tenant isolation — files** (`FileIsolationTests`)
- file from another tenant cannot be read or downloaded
- download requires permission
- spoofed signature path traversal and malware are rejected

**Authentication** (`AuthenticationTests`)
- login without csrf token is rejected
- wrong password is rejected
- account locks after repeated failures
- logout revokes the server side session
- tenant credentials do not authenticate platform realm
- platform endpoints require platform session

**Authorization** (`AuthorizationTests`)
- user without permission is forbidden
- grant override allows access
- deny override beats role grant
- grant ceiling prevents privilege escalation
- navigation only contains permitted entries

**Authorization — data scopes** (`DataScopeTests`)
- every scope level filters the directory
- out of scope record is not readable and no grant is forbidden

**Module entitlements** (`EntitlementTests`)
- disabled module denies access even when permission is held
- entitlement is per tenant
- core modules cannot be disabled
- tenant user cannot change entitlements

**AuditTests**
- changes are audited with actor source and correlation
- audit rows cannot be updated or deleted in the database
- failed login writes security event

**ProvisioningTests**
- failed step is retried idempotently
- duplicate tenant code is rejected
- tenant created through platform api is provisioned by background job

**ReportIsolationTests**
- reports and exports only contain current tenant data
- export requires export permission

**ApiSurfaceArchitectureTests**
- every endpoint declares an authorization guard

## 11. Build result
`dotnet build Hrms.slnx -c Release`: **0 errors, 0 warnings**. Frontend `pnpm lint`, `pnpm typecheck`, `pnpm build`: pass. `docker build --target api`: pass.

## 12. Test result
`dotnet test Hrms.slnx`: **36 passed, 0 failed, 0 skipped** (API tests, ~25 s). Unit/integration projects contain no tests yet.

## 13. Known issues
- No Git remote → CI workflow only verified locally (same commands); not yet run on GitHub Actions.
- `tests/unit` and `tests/integration` projects are empty; all coverage is API-level integration tests.
- No API to link a user to an employee yet; data-scope tests set the link directly in the tenant DB.
- Untested so far: MFA enable/verify flow, email-verification flow, feature-level (sub-module) entitlements, tenant-scoped background jobs for a suspended tenant.
- Malware scanning is an EICAR-signature placeholder; production secret store (AWS Secrets Manager) not wired; Hangfire with PostgreSQL storage + Redis not verified end-to-end; tenant DB resolver's static verified-connection cache needs an invalidation policy for credential rotation.
- Frontend not UI-tested and has no unit tests; API client uses hand-written types alongside the generated schema.
- `docker compose --profile app` not run end-to-end (image builds).

## 14. Remaining foundation work
1. Push to a GitHub repo, open the PR, get CI green.
2. User↔employee link API; tests for MFA, email verification, feature entitlements, suspended-tenant jobs; unit tests for permission/grant-ceiling/scope resolution.
3. Production secret store, real malware scanning, Hangfire/Redis production config, resolver cache invalidation.
4. Deployment infrastructure (AWS ap-south-1 / ECS Fargate IaC) and OpenIddict tokens for mobile (deferred to the mobile milestone per decision).
5. Frontend UI tests once approved.
