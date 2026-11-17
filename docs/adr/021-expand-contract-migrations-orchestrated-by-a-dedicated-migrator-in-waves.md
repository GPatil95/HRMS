# ADR-021: Expand/contract migrations orchestrated by a dedicated Migrator in waves

- **Status:** Proposed
- **Area:** Database / CI/CD

## Context
Hundreds of tenant DBs must be migrated safely with rollback options.

## Decision
Hrms.Migrator applies platform then tenant migrations in waves with per-tenant version tracking; releases are backward compatible with previous schema; app rollback = redeploy previous image; destructive changes delayed one release; pre-wave PITR point.

## Why
Safe, observable, resumable rollout across many databases.

## Alternatives considered
- Migrate on API startup
- Manual DBA scripts per tenant

## Advantages
- No thundering herd at startup
- Partial failures contained and visible

## Disadvantages
- Requires discipline (expand/contract) from developers
- Longer to remove columns

## Scalability impact
Parallelism tuned per cluster.

## Security impact
Migrator role separate from app role (least privilege).

## Operational impact
Dashboards for schema versions; runbook for failed waves.
