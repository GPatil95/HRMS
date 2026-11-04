# ADR-008: Web uses BFF cookie sessions; mobile uses PKCE with short access tokens and rotating refresh tokens

- **Status:** Proposed
- **Area:** Security / Authentication

## Context
Tokens stored in browser JS are exposed to XSS; mobile apps can store secrets in secure enclaves.

## Decision
Web SPA authenticates via server-side code flow; browser holds only an HttpOnly, Secure, `__Host-` cookie + CSRF token. Mobile: system-browser PKCE, 10-min JWT, rotating opaque refresh token in SecureStore with reuse detection.

## Why
Follows IETF OAuth 2.0 for Browser-Based Apps guidance; minimises token theft impact.

## Alternatives considered
- SPA with tokens in memory + refresh in localStorage
- Long-lived JWTs everywhere

## Advantages
- No tokens reachable by XSS on web
- Immediate server-side revocation
- Mobile best practice

## Disadvantages
- Two auth schemes in the API (policy scheme selects)
- CSRF protection needed for cookies

## Scalability impact
Session lookup cached in Redis.

## Security impact
Significantly reduces token exfiltration risk.

## Operational impact
Session store and revocation list must be highly available (Redis HA).
