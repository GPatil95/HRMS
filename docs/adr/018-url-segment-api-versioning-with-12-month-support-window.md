# ADR-018: URL-segment API versioning with 12-month support window

- **Status:** Proposed
- **Area:** API

## Context
Web is deployed with API, but mobile apps and integrations lag.

## Decision
`/api/v{major}/...` via Asp.Versioning; breaking changes only in new major; Deprecation/Sunset headers; oasdiff in CI.

## Why
Explicit, cache/proxy friendly, easy to route and document.

## Alternatives considered
- Header/media-type versioning
- No versioning (always backward compatible)

## Advantages
- Visible and simple
- Per-version OpenAPI

## Disadvantages
- URL churn on major versions

## Scalability impact
Neutral.

## Security impact
Old versions retired on schedule reduce attack surface.

## Operational impact
Track usage per version before sunset.
