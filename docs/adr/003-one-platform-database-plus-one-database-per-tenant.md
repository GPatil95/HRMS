# ADR-003: One platform database plus one database per tenant

- **Status:** Proposed
- **Area:** Tenant isolation / Database

## Context
Mandated by the brief. HR and payroll data of different companies must be strongly isolated; enterprise customers ask for isolation, per-customer restore and deletion.

## Decision
`hrms_platform` for platform data; `hrms_tenant_<n>` per tenant on shared or dedicated PostgreSQL clusters, located via `tenant_database_registry`. Tenant business data never in the platform DB.

## Why
Strongest practical isolation short of separate infrastructure; per-tenant backup/restore/export/delete; noisy tenants can be moved to dedicated clusters; simple queries (no tenant_id predicate everywhere).

## Alternatives considered
- Shared database, shared schema with tenant_id + Row-Level Security
- Shared database, schema per tenant
- Separate cluster/stack per tenant

## Advantages
- A missed WHERE clause cannot leak another tenant's rows
- Per-tenant lifecycle operations (restore, move, delete) are natural
- Per-tenant encryption keys and DB credentials possible
- Clear story for enterprise security reviews

## Disadvantages
- Many databases to migrate, monitor and back up
- Connection pooling harder (pool per DB)
- Cross-tenant analytics needs a separate pipeline
- Higher baseline cost per tenant than shared schema

## Scalability impact
Scales by adding clusters; hundreds of DBs per cluster with PgBouncer; very large tenants get dedicated clusters. Requires automation (Migrator) to stay operable at thousands of tenants.

## Security impact
Isolation at database + credential level; combined with per-tenant roles a code bug cannot silently cross tenants.

## Operational impact
Needs provisioning automation, migration waves, per-tenant backup monitoring and capacity management from day one.
