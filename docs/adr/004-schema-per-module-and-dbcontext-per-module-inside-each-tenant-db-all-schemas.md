# ADR-004: Schema per module and DbContext per module inside each tenant DB; all schemas in every tenant DB

- **Status:** Proposed
- **Area:** Database / Modularity

## Context
Modules must be independent and extractable; tenants buy different module sets.

## Decision
Each tenant module owns a PostgreSQL schema and a DbContext with its own migration history. Every tenant DB gets all module schemas regardless of entitlement. FKs only to `core.*` tables.

## Why
Keeps persistence boundaries aligned with module boundaries; uniform schema across tenants keeps migrations simple; enabling a module is an entitlement change only.

## Alternatives considered
- Single DbContext for all modules
- Create module schemas only when a module is purchased

## Advantages
- Clear ownership, extractable persistence
- Migrations uniform and predictable
- Instant module enablement

## Disadvantages
- Empty tables for unpurchased modules (negligible cost)
- Cross-module queries need Contracts, not joins
- Multiple DbContexts to coordinate in rare cross-module transactions (shared connection/transaction or outbox)

## Scalability impact
Neutral; enables moving a module schema to its own DB later.

## Security impact
Module DB permissions could later be split per schema if a module is extracted.

## Operational impact
Migrator applies N module contexts per tenant; version tracked per schema in registry.

## Amendment — final baseline review
Schema list extended to all modules (see architecture §7.2). Shared masters used by several optional modules (office/floor, work calendars, business partners) are placed in `core.org` so FKs never cross optional modules.
