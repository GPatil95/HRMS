# ADR-034: Two separate billing domains: SaaS platform billing vs tenant client billing

- **Status:** Proposed (final baseline review)
- **Area:** Billing

## Context
Platform bills tenants; some tenants bill their own clients — different realms, data owners and rules.

## Decision
`Hrms.Platform.Billing` in Platform DB for SaaS subscriptions; optional tenant module `clientbilling` in tenant DB for tenants' customers. Shared only generic building blocks (money, GST calculator, PDF, numbering pattern).

## Why
Prevents mixing platform financial data with tenant business data and keeps tenant isolation.

## Alternatives considered
- One billing module serving both

## Advantages
- Clear ownership, isolation, permissions

## Disadvantages
- Some similar code paths (mitigated by shared building blocks)

## Scalability impact
Independent scaling.

## Security impact
No cross-realm data path.

## Operational impact
Separate numbering and reporting.
