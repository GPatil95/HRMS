# ADR-035: Unified work inbox as a non-authoritative projection

- **Status:** Proposed (final baseline review)
- **Area:** Workflow / Authorization

## Context
Users need one inbox across modules; modules remain independent.

## Decision
`core.workitems` projection written in-transaction by modules/workflow; inbox lists by assignee + entitlement; acting always goes through the owning module's API with full checks; titles contain no sensitive data.

## Why
Fast unified view without giving the inbox authority or cross-module coupling.

## Alternatives considered
- Query every module live for pending items
- Inbox actions executed by inbox service directly

## Advantages
- Fast, simple, decoupled

## Disadvantages
- Projection can be briefly stale

## Scalability impact
Indexed per assignee.

## Security impact
Stale items cannot bypass authorization.

## Operational impact
Reconciliation job for orphaned items.
