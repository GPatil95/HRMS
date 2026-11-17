# ADR-007: ASP.NET Core Identity + OpenIddict, tenant users stored in each tenant DB

- **Status:** Proposed
- **Area:** Security / Authentication

## Context
Need OIDC flows for web BFF, mobile PKCE and integrations, MFA, revocation, and optional per-tenant SSO. Duende IdentityServer is commercial; managed IdPs move user data and add per-MAU cost.

## Decision
Self-hosted OIDC server built on OpenIddict with ASP.NET Core Identity; tenant identity tables in tenant DB, platform identity in platform DB; separate issuers/keys per realm. Per-tenant SSO via external login federation later.

## Why
Keeps all tenant personal data (including credentials) inside the tenant boundary; no per-user licence costs; full control over tenant-aware login UX.

## Alternatives considered
- Duende IdentityServer (commercial licence)
- Managed IdP: Auth0/Okta, Entra External ID, AWS Cognito
- Keycloak (self-hosted Java, realm per tenant)
- Custom JWT issuance without OIDC

## Advantages
- Data stays in tenant DB
- No per-MAU fees
- Standard protocols; IdP could be swapped later because APIs only depend on claims (`sub`, `tid`, `sid`)

## Disadvantages
- We own the security of the auth server (patching, key rotation, pen testing)
- More build effort than a managed IdP (MFA UX, recovery flows)

## Scalability impact
Stateless validation of JWTs; session/revocation checks hit Redis.

## Security impact
High-value component built in-house — mitigated by using a mature library, strict flows, and external pen testing. **Flagged for explicit review (OQ9).**

## Operational impact
Key rotation and signing key storage in KMS; monitoring of auth anomalies.
