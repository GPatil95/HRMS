# HRMS SaaS Platform — Architecture & Foundation Plan

Status: **PROPOSED — for review**. Nothing here is final until each ADR is marked *Accepted*.
Scope: platform foundation only. No HRMS business modules (Payroll, Attendance, Leave, Recruitment, PMS, Assets, Projects, Visitors, AI) are designed in detail or built in this phase.

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

## Decisions that need your input

See [architecture/12-roadmap-and-risks.md#open-questions-for-the-product-owner](architecture/12-roadmap-and-risks.md#open-questions-for-the-product-owner).
