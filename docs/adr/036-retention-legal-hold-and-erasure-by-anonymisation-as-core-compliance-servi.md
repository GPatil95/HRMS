# ADR-036: Retention, legal hold and erasure-by-anonymisation as core compliance services

- **Status:** Proposed (final baseline review)
- **Area:** Compliance

## Context
HR data has statutory minimum retention and privacy-driven maximums; legal holds must override deletion.

## Decision
Retention policies per data category, legal hold register, erasure workflow that anonymises personal fields while preserving statutory/financial records, all audited; tenant-configurable within platform minimums.

## Why
Supports DPDP/GDPR-style rights and statutory retention simultaneously.

## Alternatives considered
- Manual deletion on request
- Hard-delete everything after exit

## Advantages
- Consistent, provable data lifecycle

## Disadvantages
- Complexity in modules to declare categories

## Scalability impact
Batch jobs per tenant.

## Security impact
Reduces exposure of stale personal data.

## Operational impact
Legal review of default periods required.
