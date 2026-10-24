# 20–21. Development Roadmap, Risks and Open Questions

## 20. Roadmap (foundation first; each phase ends with a review gate)

| Phase | Deliverables | Exit criteria |
|---|---|---|
| **0. Architecture sign-off** | this document set reviewed; ADRs accepted/amended; open questions answered | ADRs 001–012 accepted |
| **1. Scaffolding & delivery pipeline** | monorepo, solution + building blocks skeleton, Directory.Build/Packages props, analyzers, docker-compose dev stack, CI (build, test, CodeQL, gitleaks), web/admin/mobile shells boot, OpenAPI → generated client | green CI on `main`; `docker compose up` + one command runs everything |
| **2. Platform core & tenancy** | Platform DB (tenancy, catalog, subscription schemas), DB registry, clusters, Migrator (provision, migrate waves), tenant resolution middleware, TenantContext, connection factory + `meta.tenant_identity` check, admin APIs for tenants/domains | provision 2 tenants end-to-end; **T1 isolation tests pass** |
| **3. Identity & authentication** | tenant identity schema, ASP.NET Core Identity + OpenIddict, web BFF login, mobile PKCE login, MFA (TOTP + passkeys), sessions & revocation, lockout & rate limits, platform realm login, security events | login/logout/MFA/revocation tests; T5 passes |
| **4. Authorization & entitlements** | module manifests & catalog sync, entitlement service + cache invalidation, permission model (roles, scopes, overrides), data scope provider, `/me/session` & `/me/navigation`, admin UI for plans/entitlements, tenant UI for roles/permissions | **T2, T3, T4, T8 pass**; authz matrix covers 100% of endpoints |
| **5. Audit & common services** | audit interceptor, security audit, append-only protection, hash chain + verifier + WORM export; outbox/inbox; Hangfire worker + tenant job filter; file service with scan pipeline; notifications (in-app/email/push) + templates; config & feature flags; import/export framework; workflow engine core + approval inbox; SignalR | T6, T7 pass; demo: import org data via Excel, approve a sample workflow, download audited file |
| **6. Org structure + minimal employee core** | companies, branches, departments, designations, employee core + employment history + reporting closure (needed to prove scopes) | scope tests on real hierarchy; UI CRUD with history panel |
| **7. Hardening** | observability dashboards & alerts, load tests (many tenants, 50k employees), ZAP + external pen test prep, backup/restore drill, runbooks (provisioning, incident, restore, key rotation) | SLOs met in load test; restore drill documented |
| **Gate → first business module** | proposed order: Employee (full) → Attendance (+biometric) → Leave → Payroll → Tax/TDS → others by commercial priority | business module design reviewed separately |

## 21. Risks

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R1 | **Connection explosion** with hundreds of tenant DBs × API instances | High | High | PgBouncer transaction pooling, small Npgsql pools, cluster sharding, load test in phase 7 |
| R2 | **Migration fan-out failures** leave tenants on different schemas | Medium | High | expand/contract, waves, per-tenant version tracking, API refuses incompatible schema, automated retries, dashboards |
| R3 | Operational cost/complexity of DB-per-tenant (backups, monitoring, restores) | High | Medium | automation via Migrator, per-tenant logical backups, cluster capacity planning, managed services |
| R4 | Cross-tenant analytics & platform reporting harder | High | Low | usage events into platform DB; later analytics warehouse fed by CDC with tenant isolation |
| R5 | Data-scope filters forgotten in a new query → data leak within tenant | Medium | High | single `ApplyScope` mechanism, generated authz matrix tests, code review checklist, optional EF global filters for "always own-scope" entities |
| R6 | Module boundary erosion in the monolith | Medium | Medium | Contracts-only references, arch tests, CODEOWNERS |
| R7 | Self-hosted OIDC server security burden | Medium | High | OpenIddict (mature), minimal flows, key rotation, pen test; can switch to external IdP behind same claims model (ADR-007) |
| R8 | Entitlement/permission cache staleness after revocation | Low | Medium | versioned keys + pub/sub invalidation + 30 s TTL ceiling; security-critical revocations (user disable) go through session revocation (immediate) |
| R9 | Statutory/payroll correctness and frequent rule changes | High | High | effective-dated rule packs, snapshots, golden-file regression tests with CA-verified cases (payroll phase) |
| R10 | Sensitive PII compliance (Aadhaar, bank, health) | Medium | High | field encryption, masking, minimal collection, legal review, DPDP processes |
| R11 | Hangfire storage becomes hotspot at scale | Low | Medium | dedicated jobs DB, queue separation, dispatcher pattern; can move to Redis storage (Pro) or Quartz later behind `IJobScheduler` |
| R12 | Licence changes in OSS dependencies | Medium | Medium | prefer Apache/MIT, avoid recently relicensed libs, abstraction at seams, quarterly licence scan |
| R13 | Mobile version fragmentation vs API evolution | Medium | Medium | 12-month version support, forced-upgrade endpoint, OTA updates |
| R14 | Cloud choice not yet fixed (S3 requirement implies AWS or S3-compatible service) | Medium | Medium | decide in phase 0 (OQ1) |

## Open questions for the product owner

| # | Question | Default if no answer |
|---|---|---|
| OQ1 | Cloud provider & region(s)? Any data-residency commitments (India-only)? | AWS ap-south-1, DR in ap-south-2 |
| OQ2 | Initial statutory scope: India only? | India only |
| OQ3 | Scale targets: number of tenants year 1/3, largest tenant size? | A4 in overview |
| OQ4 | Is customer SSO (Entra ID / Google / Okta) required for launch customers? | design now, build after foundation |
| OQ5 | Can one login belong to multiple tenants (e.g. consultants, group companies)? Group companies could instead be *multiple companies within one tenant*. | no — one account per tenant; group companies = multiple `org.companies` in one tenant |
| OQ6 | Kubernetes (EKS) or simpler container service (ECS Fargate)? | ECS Fargate (lower ops for a new team) |
| OQ7 | UI component library preference (MUI vs shadcn/ui)? | MUI core |
| OQ8 | Willing to pay for commercial libs where they save time (e.g. Hangfire Pro, QuestPDF, a commercial grid)? | no — OSS first |
| OQ9 | Self-hosted identity (OpenIddict) vs managed IdP (Auth0 / Entra External ID / Keycloak)? | OpenIddict (ADR-007) |
| OQ10 | Where should the repo be created (GitHub org/name)? | — |
