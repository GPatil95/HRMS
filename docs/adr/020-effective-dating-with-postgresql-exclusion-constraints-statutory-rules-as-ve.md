# ADR-020: Effective dating with PostgreSQL exclusion constraints; statutory rules as versioned reference data

- **Status:** Proposed
- **Area:** Database / Payroll

## Context
Rules and org assignments change over time; payroll must be reproducible.

## Decision
Effective-dated tables use `effective_from/effective_to` + `EXCLUDE USING gist` to prevent overlaps. Statutory rule sets are platform-published, versioned, copied into tenant DBs, and referenced by payroll runs.

## Why
Database-enforced correctness for time ranges; reproducible calculations.

## Alternatives considered
- Application-only overlap checks
- Hard-coded statutory values
- Tenant-maintained statutory values only

## Advantages
- Integrity guaranteed by DB
- Traceable rule versions

## Disadvantages
- More complex queries (as-of joins)
- Publishing pipeline for statutory data

## Scalability impact
Index-supported range queries.

## Security impact
Prevents silent rule tampering; changes audited.

## Operational impact
Statutory content team/process needed for updates.
