# 18. Testing Strategy

## 18.1 Test pyramid

| Level | Tooling | Scope | Runs |
|---|---|---|---|
| Unit | xUnit v3, NSubstitute, Shouldly (FluentAssertions v8+ is commercial) | domain rules, validators, permission/scope calculation, entitlement resolution, hash chain | every PR |
| Architecture | NetArchTest.Rules | layering, module boundaries (only `.Contracts` cross-module), Platform vs Tenant DbContext separation, every endpoint has permission/module metadata, no raw `ExecuteUpdate` on audited entities | every PR |
| Integration | Testcontainers (PostgreSQL 17, Redis, MinIO), Respawn for cleanup | EF mappings, migrations up/down on empty DB, interceptors (audit, audit columns), outbox, file service, provisioning of 2 tenant DBs | every PR |
| API | `WebApplicationFactory<Program>` + real containers | HTTP contracts, ProblemDetails, versioning, auth flows | every PR |
| Authorization matrix | generated: enumerate endpoints from `EndpointDataSource` × test personas | each endpoint: unauthenticated → 401, no permission → 403, wrong module → 403, outside scope → 404, allowed → 2xx | every PR |
| Tenant isolation | dedicated suite with Tenant A and Tenant B DBs | see §18.2 | every PR + nightly against UAT |
| Database | migration test: apply all migrations to empty DB and to a snapshot of previous release; `has-pending-model-changes` check; performance of key scoped queries with `EXPLAIN` on seeded data | PR / nightly |
| Front-end unit/component | Vitest + Testing Library + MSW | components, hooks, permission gating, form error mapping | every PR |
| UI E2E | Playwright (web, admin); Maestro (mobile) | login (+MFA), shell/menu by role, module enable/disable, CRUD on org entities, import wizard, file upload | PR (smoke) / nightly (full) |
| Contract | OpenAPI diff (`oasdiff`), generated client compile | breaking API changes | every PR |
| Security | CodeQL, gitleaks, Trivy, ZAP baseline, dependency audit | | PR / release |
| Performance/load | k6 (test tool only) or NBomber | login burst, navigation/session endpoints, scoped employee list at 50k employees, many-tenant connection behaviour (500 tenant DBs on one cluster), import of 10k rows | before GA, per major release |

## 18.2 Mandatory proof tests (from the brief)

| # | Test | Setup | Expectation |
|---|---|---|---|
| T1 | **Tenant A cannot access Tenant B data** | two provisioned tenant DBs with employees; user A token | (a) A's token against `b.hrms.com` → 401 `TENANT_MISMATCH` + security event; (b) A's token on `api.hrms.com` with `?tenantId=B`/`X-Tenant: B`/body tenantId → ignored, returns only A's data; (c) requesting B's employee ID on A's host → 404; (d) connection factory pointed (by fault injection) at B's DB with A's context → `TenantDatabaseMismatchException`; (e) cache and S3 keys for A never resolvable from B context; (f) A's presigned download URL for B's file cannot be minted |
| T2 | **No Payroll permission → Payroll APIs inaccessible** | tenant entitled to Payroll; user without `payroll.*` permissions | every endpoint under `/api/v1/payroll/**` (enumerated automatically, so new endpoints are covered) → 403 `permission-denied`; also file downloads owned by payroll, exports, SignalR methods |
| T3 | **Branch scope cannot see other branch** | HR user with `employee.profile.view @ branch` targeting Pune; employees in Pune and Mumbai | list returns only Pune; GET Mumbai employee → 404; update/export of Mumbai employee → 404/empty; search by code of Mumbai employee → no result |
| T4 | **Disabled module unreachable via direct API** | tenant not entitled to Recruitment (or entitlement revoked at runtime) | all `/api/v1/recruitment/**` → 403 `module-not-enabled` for every user incl. Tenant Admin; after revoke, within cache TTL (≤30 s) access stops; recruitment jobs for that tenant are skipped; navigation no longer lists it |
| T5 | Platform token rejected by tenant API and vice versa | platform admin token | 401 on tenant endpoints |
| T6 | Support grant expiry | grant expires mid-session | next request 403, audit in both DBs |
| T7 | Audit immutability | app role attempts UPDATE/DELETE on `audit.audit_entries` | permission denied; trigger blocks even with elevated test role; verifier detects tampered row |
| T8 | Deny override wins | role grants, user deny override | 403 |

## 18.3 Test data & environments

- Builders/fixtures in `Hrms.BuildingBlocks.Testing` (`TenantFactory.CreateAsync()` provisions a real tenant DB through the same Migrator code path).
- No production data in non-production environments; synthetic data generator for UAT/performance.
- Coverage targets: domain/application ≥ 80% line coverage; 100% of endpoints in authorization matrix.

## 18.4 Additional mandatory tests (final baseline)

| # | Test | Expectation |
|---|---|---|
| T9 | Grant ceiling | user with `employee.profile.view @ branch` cannot create a role or override granting `@ tenant` or any permission they lack → 403 `grant-exceeds-own-permission` |
| T10 | AI cannot bypass permissions | user without `payroll.*` asks AI for salary data → payroll tools not offered; forced tool call returns forbidden; audit shows denied tool call |
| T11 | AI sensitive write needs confirmation | AI-initiated leave approval without valid confirmation token → rejected |
| T12 | Widget/report consistency | dashboard widget totals and report rows equal the scoped list endpoint for each persona |
| T13 | Scheduled report runs with owner's current permissions | after owner loses permission/exits, schedule suspended, no file produced |
| T14 | Locked financial records | update to finalized payroll/FNF/invoice rejected by domain rule and DB trigger; revision creates new record |
| T15 | Exit revocation | after LWD job: login fails, sessions/refresh tokens revoked, open work items reassigned, biometric revoke command queued |
| T16 | Module independence | for each optional module: disable it and run the full API test suite of every other module → all pass (matrix in CI nightly) |
| T17 | Work inbox is not an authority | user whose scope changed cannot act on a stale work item → module API 404/403 |
| T18 | Import respects scope | importer with branch scope uploading rows for another branch → rows rejected |
| T19 | Mass assignment | POST/PUT with extra fields (`tenantId`, `createdBy`, `status`) → 400 unknown property, no change |
