# ADR-014: Audit via same-transaction EF interceptor + explicit business events; append-only with hash chain and WORM export

- **Status:** Proposed
- **Area:** Audit / Compliance

## Context
Every critical change must be attributable and tamper-resistant; audit must not be skippable.

## Decision
AuditInterceptor writes field-level changes for `[Audited]` entities in the same transaction; IAuditWriter records business actions; tenant audit in tenant DB, platform audit in platform DB; INSERT-only grants, trigger guard, SHA-256 hash chain, daily export to S3 Object Lock.

## Why
Atomic with business changes; defence in depth against tampering; isolated per tenant.

## Alternatives considered
- Database triggers capturing changes
- Separate audit service/database (event streaming)
- Application logs as audit

## Advantages
- Rich context (user, source, reason, correlation) not available to DB triggers
- Cannot lose audit on commit
- Tamper-evident

## Disadvantages
- Bypassed by raw SQL (banned for audited entities)
- Write amplification on hot tables
- Hash chain adds serialization point (mitigated by per-day chains)

## Scalability impact
Monthly partitions; archive old partitions; volume grows with activity.

## Security impact
Strong integrity guarantees; masking of sensitive fields.

## Operational impact
Verifier job and WORM export monitoring; retention management.
