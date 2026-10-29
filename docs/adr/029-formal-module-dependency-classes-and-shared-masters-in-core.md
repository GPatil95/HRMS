# ADR-029: Formal module dependency classes and shared masters in core

- **Status:** Proposed (final baseline review)
- **Area:** Module dependency

## Context
Modules are sold independently; brief requires distinguishing hard, optional and integration dependencies and that disabling one module never breaks unrelated ones.

## Decision
Classes H/O/I/None declared in manifests and validated in CI; H only to `core.*` or parent; O via `IOptionalIntegration<T>` with mandatory fallback; I via outbox events/shared core surfaces. Office/floor, holiday calendars/weekly offs and business partners (clients/vendors) moved to `core.org`.

## Why
Removes hidden optional↔optional coupling (e.g. rooms↔workplace, projects↔client billing, attendance↔leave calendars).

## Alternatives considered
- Each module owns its own copies of shared masters
- Allow optional→optional hard dependencies

## Advantages
- Any combination of optional modules is valid
- Single source for shared masters
- Automated module-independence tests (T16)

## Disadvantages
- Core grows slightly
- Every optional integration needs a fallback path

## Scalability impact
Neutral.

## Security impact
Disabled module data not exposed through integrations.

## Operational impact
Dependency graph visible to platform admins when enabling/disabling.
