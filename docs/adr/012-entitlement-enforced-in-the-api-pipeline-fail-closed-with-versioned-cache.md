# ADR-012: Entitlement enforced in the API pipeline, fail-closed, with versioned cache

- **Status:** Proposed
- **Area:** Subscription / Module dependency

## Context
Hiding menus is not security; disabled modules must be unreachable via direct API calls, jobs and files.

## Decision
Module requirement attached by convention to every endpoint of a module; feature requirements per endpoint; subscription check globally; jobs and event handlers check entitlement; entitlement cache invalidated via version + pub/sub (≤30 s staleness).

## Why
Central, automatic enforcement avoids relying on developers remembering attributes.

## Alternatives considered
- Check entitlements only in UI
- Check in each application service manually
- Separate deployments per plan

## Advantages
- Uniform enforcement
- Testable automatically across all endpoints

## Disadvantages
- Small staleness window after changes
- Cache invalidation infrastructure required

## Scalability impact
Cheap per-request check from memory.

## Security impact
Prevents unpaid/disabled functionality access and data exposure through forgotten endpoints.

## Operational impact
Admin changes propagate within seconds; monitoring of pub/sub health.

## Amendment — final baseline review
Entitlement also gates: dashboard widgets, reports, imports/exports, work-inbox items, notifications, AI tools and scheduled jobs of the module.
