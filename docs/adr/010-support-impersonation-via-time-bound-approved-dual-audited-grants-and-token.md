# ADR-010: Support/impersonation via time-bound, approved, dual-audited grants and token exchange

- **Status:** Proposed
- **Area:** Security / Platform admin

## Context
Support sometimes needs to see a tenant's configuration or reproduce issues.

## Decision
Support access requires a grant (reason, ticket, level, ≤8 h), approved by tenant admin or two-person break-glass; access through an exchanged token carrying `act` and `support_grant_id`; all actions audited in tenant and platform audit; visible banner; revocable.

## Why
Meets explicit, time-bound, permission-controlled and fully audited requirement.

## Alternatives considered
- Shared support account per tenant
- Direct DB access by support staff
- Impersonating real users

## Advantages
- Accountability per support engineer
- Tenant controls and sees access
- Least privilege (read-only default)

## Disadvantages
- Slower emergency support
- Token exchange adds implementation work

## Scalability impact
Neutral.

## Security impact
Removes standing access; every action attributable.

## Operational impact
Break-glass procedure and alerting must be documented and tested.
