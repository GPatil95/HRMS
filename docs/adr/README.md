# Architecture Decision Records

Template: Context, Decision, Why, Alternatives considered, Advantages, Disadvantages, Scalability impact, Security impact, Operational impact.

All ADRs are **Proposed** until reviewed. Items affecting tenant isolation, database, security, payroll, permissions, audit, subscription, module dependency, AI or compliance are marked in the *Area* column.

| ADR | Title | Area | Status |
|---|---|---|---|
| [ADR-001](001-modular-monolith-as-the-initial-architecture.md) | Modular monolith as the initial architecture | Architecture | Proposed |
| [ADR-002](002-net-10-lts-asp-net-core-and-ef-core-10-for-the-backend.md) | .NET 10 LTS, ASP.NET Core and EF Core 10 for the backend | Technology | Proposed |
| [ADR-003](003-one-platform-database-plus-one-database-per-tenant.md) | One platform database plus one database per tenant | Tenant isolation / Database | Proposed |
| [ADR-004](004-schema-per-module-and-dbcontext-per-module-inside-each-tenant-db-all-schemas.md) | Schema per module and DbContext per module inside each tenant DB; all schemas in every tenant DB | Database / Modularity | Proposed |
| [ADR-005](005-server-side-tenant-resolution-from-verified-host-signed-token-claim.md) | Server-side tenant resolution from verified host + signed token claim | Tenant isolation / Security | Proposed |
| [ADR-006](006-per-tenant-database-roles-and-secrets-held-only-in-a-secret-manager.md) | Per-tenant database roles and secrets held only in a secret manager | Security / Database | Proposed |
| [ADR-007](007-asp-net-core-identity-openiddict-tenant-users-stored-in-each-tenant-db.md) | ASP.NET Core Identity + OpenIddict, tenant users stored in each tenant DB | Security / Authentication | Proposed |
| [ADR-008](008-web-uses-bff-cookie-sessions-mobile-uses-pkce-with-short-access-tokens-and-r.md) | Web uses BFF cookie sessions; mobile uses PKCE with short access tokens and rotating refresh tokens | Security / Authentication | Proposed |
| [ADR-009](009-separate-platform-admin-realm-portal-and-audience-no-implicit-tenant-data-ac.md) | Separate platform-admin realm, portal and audience; no implicit tenant data access | Security / Platform admin | Proposed |
| [ADR-010](010-support-impersonation-via-time-bound-approved-dual-audited-grants-and-token.md) | Support/impersonation via time-bound, approved, dual-audited grants and token exchange | Security / Platform admin | Proposed |
| [ADR-011](011-permission-codes-role-scope-levels-assignment-scope-targets-user-overrides-d.md) | Permission codes + role scope levels + assignment scope targets + user overrides (deny wins) | Authorization | Proposed |
| [ADR-012](012-entitlement-enforced-in-the-api-pipeline-fail-closed-with-versioned-cache.md) | Entitlement enforced in the API pipeline, fail-closed, with versioned cache | Subscription / Module dependency | Proposed |
| [ADR-013](013-module-contracts-and-optional-integrations.md) | Module Contracts and optional integrations | Module dependency | Proposed |
| [ADR-014](014-audit-via-same-transaction-ef-interceptor-explicit-business-events-append-on.md) | Audit via same-transaction EF interceptor + explicit business events; append-only with hash chain and WORM export | Audit / Compliance | Proposed |
| [ADR-015](015-transactional-outbox-for-domain-integration-events-in-process-dispatch-first.md) | Transactional outbox for domain/integration events, in-process dispatch first | Architecture / Reliability | Proposed |
| [ADR-016](016-hangfire-with-postgresql-storage-in-a-dedicated-jobs-database.md) | Hangfire with PostgreSQL storage in a dedicated jobs database | Background jobs | Proposed |
| [ADR-017](017-shared-s3-bucket-per-environment-with-tenant-prefixes-presigned-urls-after-a.md) | Shared S3 bucket per environment with tenant prefixes, presigned URLs after authorization, scan-before-use | File storage / Security | Proposed |
| [ADR-018](018-url-segment-api-versioning-with-12-month-support-window.md) | URL-segment API versioning with 12-month support window | API | Proposed |
| [ADR-019](019-uuidv7-primary-keys-business-codes-as-separate-unique-columns.md) | UUIDv7 primary keys, business codes as separate unique columns | Database | Proposed |
| [ADR-020](020-effective-dating-with-postgresql-exclusion-constraints-statutory-rules-as-ve.md) | Effective dating with PostgreSQL exclusion constraints; statutory rules as versioned reference data | Database / Payroll | Proposed |
| [ADR-021](021-expand-contract-migrations-orchestrated-by-a-dedicated-migrator-in-waves.md) | Expand/contract migrations orchestrated by a dedicated Migrator in waves | Database / CI/CD | Proposed |
| [ADR-022](022-monorepo-with-pnpm-workspace-front-ends-and-generated-api-client.md) | Monorepo with pnpm workspace front-ends and generated API client | Repository / Frontend | Proposed |
| [ADR-023](023-react-19-vite-tanstack-router-query-table-server-driven-navigation-and-plug.md) | React 19 + Vite + TanStack (Router/Query/Table), server-driven navigation and plug-in module registry | Frontend | Proposed |
| [ADR-024](024-opentelemetry-for-logs-traces-and-metrics.md) | OpenTelemetry for logs, traces and metrics | Observability | Proposed |
| [ADR-025](025-prefer-permissive-oss-avoid-recently-relicensed-libraries.md) | Prefer permissive OSS; avoid recently relicensed libraries | Technology / Legal | Proposed |
| [ADR-026](026-application-level-field-encryption-for-highly-sensitive-personal-data.md) | Application-level field encryption for highly sensitive personal data | Security / Compliance | Proposed |
| [ADR-027](027-in-house-approval-workflow-engine-not-a-general-bpm-engine.md) | In-house approval workflow engine (not a general BPM engine) | Workflow | Proposed |
| [ADR-028](028-hosting-aws-ap-south-1-with-managed-services-proposed-needs-decision.md) | Hosting: AWS (ap-south-1) with managed services — PROPOSED, needs decision | Infrastructure | Proposed |

| [ADR-029](029-formal-module-dependency-classes-and-shared-masters-in-core.md) | Formal module dependency classes and shared masters in core | Module dependency | Proposed |
| [ADR-030](030-employee-hr-core-always-on-documents-letters-exit-fnf-as-toggleable-sub-mo.md) | Employee/HR Core always on; Documents, Letters, Exit/FNF as toggleable sub-modules | Module packaging | Proposed |
| [ADR-031](031-finalized-financial-results-are-immutable-corrections-via-revisions.md) | Finalized financial results are immutable; corrections via revisions | Payroll / Compliance | Proposed |
| [ADR-032](032-biometric-integration-via-outbound-connector-ingest-api-no-biometric-templ.md) | Biometric integration via outbound connector/ingest API; no biometric templates stored | Integration / Security | Proposed |
| [ADR-033](033-ai-assistant-through-a-tool-gateway-executing-as-the-user-no-database-acce.md) | AI assistant through a tool gateway executing as the user; no database access | AI / Security | Proposed |
| [ADR-034](034-two-separate-billing-domains-saas-platform-billing-vs-tenant-client-billin.md) | Two separate billing domains: SaaS platform billing vs tenant client billing | Billing | Proposed |
| [ADR-035](035-unified-work-inbox-as-a-non-authoritative-projection.md) | Unified work inbox as a non-authoritative projection | Workflow / Authorization | Proposed |
| [ADR-036](036-retention-legal-hold-and-erasure-by-anonymisation-as-core-compliance-servi.md) | Retention, legal hold and erasure-by-anonymisation as core compliance services | Compliance | Proposed |
| [ADR-037](037-lowercase-physical-database-names.md) | Lowercase physical database names | Database | Proposed |
| [ADR-038](038-tenant-level-backup-restore-via-per-tenant-logical-dumps-plus-cluster-pitr.md) | Tenant-level backup/restore via per-tenant logical dumps plus cluster PITR; DR tiers | Backup / DR | Proposed |
| [ADR-039](039-container-platform-ecs-fargate-proposed-pending-decision.md) | Container platform: ECS Fargate (proposed) — pending decision | Deployment | Proposed |

Amended in final baseline review: ADR-004, 011, 012, 013, 027, 028 (see *Amendment* section in each).

## Planned ADRs (later phases)

- Payroll calculation engine design and reproducibility
- Biometric device integration model (push connector vs vendor cloud APIs)
- AI assistant data access, tenant data-use policy and model hosting
- Analytics/reporting warehouse (CDC pipeline)
- Offline support for mobile attendance
- Search (PostgreSQL FTS vs OpenSearch)
- Billing provider integration
