# HRMS SaaS Platform — Architecture & Foundation Plan

Status: **FINAL BASELINE — pending approval of the decisions listed in [12 §21.3](architecture/12-roadmap-and-risks.md#213-decisions-still-requiring-approval).** Start with the review summary: [architecture/26-review-findings.md](architecture/26-review-findings.md).
Scope: the architecture covers the platform foundation and every module at the architecture level. No business module is implemented in this phase.

## Reading order

| # | Deliverable (brief §24) | Document |
|---|---|---|
| 1 | Complete architecture proposal | [architecture/01-architecture-overview.md](architecture/01-architecture-overview.md) |
| 2–4 | Repository, ASP.NET Core solution, React/mobile structure | [architecture/02-repository-and-solution-structure.md](architecture/02-repository-and-solution-structure.md) |
| 5–7 | PostgreSQL architecture, Platform DB schema, Tenant DB schema | [architecture/03-database-architecture.md](architecture/03-database-architecture.md) |
| 8 | Tenant resolution, registry, provisioning | [architecture/04-tenancy.md](architecture/04-tenancy.md) |
| 9 | Authentication | [architecture/05-authentication.md](architecture/05-authentication.md) |
| 10–11 | Authorization, module entitlement | [architecture/06-authorization-and-entitlements.md](architecture/06-authorization-and-entitlements.md) |
| 12 | Audit | [architecture/07-audit.md](architecture/07-audit.md) |
| 13–16 | Common services, API versioning, background jobs, file storage | [architecture/08-common-services.md](architecture/08-common-services.md) |
| 17 | Security | [architecture/09-security.md](architecture/09-security.md) |
| 18 | Testing strategy | [architecture/10-testing-strategy.md](architecture/10-testing-strategy.md) |
| 19 | CI/CD, environments, observability | [architecture/11-cicd-environments-observability.md](architecture/11-cicd-environments-observability.md) |
| 20–21 | Roadmap, risks | [architecture/12-roadmap-and-risks.md](architecture/12-roadmap-and-risks.md) |
| 22 | ADR list | [adr/README.md](adr/README.md) |

## Final-baseline deliverables (review brief §43)

| # | Deliverable | Document |
|---|---|---|
| 1 | Updated architecture document | [01](architecture/01-architecture-overview.md) + this index |
| 2 | Module catalogue | [14 §14.2](architecture/14-module-catalogue-and-dependencies.md) |
| 3 | Module dependency matrix | [14 §14.3](architecture/14-module-catalogue-and-dependencies.md) |
| 4 | Platform DB ERD | [03 §6, §6.1](architecture/03-database-architecture.md) |
| 5 | Tenant DB ERD | [03 §7, §7.2](architecture/03-database-architecture.md) + module docs 15–22 |
| 6 | Security architecture | [09](architecture/09-security.md), [23](architecture/23-security-threat-model.md) |
| 7 | Authorization model | [06](architecture/06-authorization-and-entitlements.md) |
| 8 | Tenant isolation model | [04](architecture/04-tenancy.md), [03 §5](architecture/03-database-architecture.md) |
| 9 | Audit architecture | [07](architecture/07-audit.md) |
| 10 | Workflow architecture | [19 §19.1–19.2](architecture/19-workflow-inbox-notifications-email.md) |
| 11 | Notification architecture | [19 §19.3–19.4](architecture/19-workflow-inbox-notifications-email.md) |
| 12 | File/document architecture | [08 §16](architecture/08-common-services.md), [15 §15.2](architecture/15-hr-core-documents-letters.md) |
| 13 | AI architecture | [21](architecture/21-ai-assistant.md) |
| 14 | Billing architecture | [22](architecture/22-billing.md) |
| 15 | Workplace architecture | [18 §18.4–18.7](architecture/18-talent-workplace-productivity-modules.md) |
| 16 | Exit/FNF architecture | [16](architecture/16-exit-and-fnf.md) |
| 17 | API architecture | [25](architecture/25-api-architecture.md), [08 §14](architecture/08-common-services.md) |
| 18 | Deployment architecture | [24 §24.4](architecture/24-compliance-backup-dr-deployment.md), [11](architecture/11-cicd-environments-observability.md) |
| 19 | Backup/DR architecture | [24 §24.2–24.3](architecture/24-compliance-backup-dr-deployment.md) |
| 20 | Testing architecture | [10](architecture/10-testing-strategy.md) |
| 21 | ADR updates | [adr/README.md](adr/README.md) (ADR-029–039 new; 004/011/012/013/027/028 amended) |
| 22 | Implementation roadmap | [12](architecture/12-roadmap-and-risks.md) |

Additional documents: [13 Platform & tenant administration](architecture/13-platform-and-tenant-administration.md) · [17 Time & pay modules](architecture/17-time-and-pay-modules.md) · [20 Dashboards, reporting, import](architecture/20-dashboards-reporting-import.md) · [26 Review findings](architecture/26-review-findings.md)

## Key decisions at a glance

1. **Modular monolith** on **.NET 10 (LTS) / ASP.NET Core / EF Core 10**, one deployable API + one worker + one migrator.
2. **One platform DB + one PostgreSQL database per tenant**, located through a `tenant_database_registry`. Each tenant DB has its **own PostgreSQL login role**; credentials live only in a secret manager.
3. **Tenant is never taken from the client.** It is resolved from the request host (verified domain mapping) and must match the signed `tid` claim of the session/token. Any `tenantId` in a URL/body is ignored for routing.
4. **Separate security realms**: Platform Admin (own portal, own identity store in Platform DB, own token audience) vs Tenant users (identity store inside each tenant DB). Platform tokens are rejected by tenant APIs. Support access = explicit, time-bound, approved, audited grant.
5. **Request pipeline (fail-closed):** Authentication → Tenant resolution → Subscription check → Module entitlement → Feature entitlement → Permission → Data scope → Operation. Every endpoint must declare its module + permission or the build's architecture tests fail.
6. **Authorization** = permission codes (`module.resource.action`) granted to roles with a **scope level**, assigned to users with **scope targets** (branches/departments/employees), plus time-bound user overrides (**deny wins**).
7. **Modules communicate only through `*.Contracts`** projects (interfaces, DTOs, integration events). Optional integrations (e.g. Payroll ← Attendance) resolve to a "not available" implementation when the tenant is not entitled.
8. **Audit** captured in the same transaction via EF Core interceptor + explicit business events, stored append-only (DB grants + triggers + hash chain) and exported to WORM object storage.
9. **Transactional outbox** for events, **Hangfire** (PostgreSQL storage, dedicated jobs DB) for background work, **Redis** for cache/revocation/rate-limit/SignalR backplane.
10. **Object storage** (S3-compatible), shared bucket per environment with tenant prefix, presigned URLs only after authorization, scan-before-use.
11. **Avoid libraries that recently moved to commercial licences** (MediatR, AutoMapper, MassTransit v9+, EPPlus, Duende IdentityServer, FluentAssertions v8+) unless we decide to buy them.

12. **Dependency classes** Hard / Optional / Integration with a full matrix. Shared masters (offices, work calendars, business partners) live in core.
13. **Finalized financial results are immutable.** Payroll, FNF, tax and invoices are corrected only through revisions.
14. **AI works only through a tool gateway, acting as the user.** It has no database access, and sensitive writes need a confirmation.
15. **Two separate billing domains:** SaaS billing in the platform DB, and tenant client billing as an optional tenant module.

## Decisions that need your input

See [architecture/12-roadmap-and-risks.md §21.3](architecture/12-roadmap-and-risks.md#213-decisions-still-requiring-approval).
