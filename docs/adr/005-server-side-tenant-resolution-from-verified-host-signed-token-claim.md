# ADR-005: Server-side tenant resolution from verified host + signed token claim

- **Status:** Proposed
- **Area:** Tenant isolation / Security

## Context
Clients must not be able to switch tenants by editing tenantId; web uses subdomains, mobile uses a shared API host.

## Decision
Tenant resolved from verified `tenant_domains` mapping of the Host header and/or the `tid` claim; when both exist they must match. Client-supplied tenant identifiers are never used for routing. Immutable TenantContext per request/job.

## Why
Two independent server-trusted signals; tokens are signed; hosts are verified mappings; mismatches are detectable attacks.

## Alternatives considered
- Tenant from header/query parameter
- Tenant from token claim only
- Path-based tenant (`/t/abc/...`)

## Advantages
- Impossible to switch tenant without a valid token for that tenant
- Subdomain gives tenant branding and cookie isolation (`__Host-` cookies)
- Works for mobile through `tid` claim

## Disadvantages
- Wildcard DNS/TLS and custom-domain verification to operate
- Mobile needs a tenant discovery step

## Scalability impact
Lookups are cached in memory/Redis; negligible overhead.

## Security impact
Eliminates the classic tenant-ID tampering class; cookie scoping prevents cross-tenant session reuse.

## Operational impact
Domain onboarding workflow (DNS TXT verification, TLS issuance) needed for custom domains.
