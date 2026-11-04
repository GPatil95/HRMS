# ADR-013: Module Contracts and optional integrations

- **Status:** Proposed
- **Area:** Module dependency

## Context
Payroll may use Attendance/Leave but must work without them; optional modules must not require each other.

## Decision
Modules reference only other modules' `.Contracts`; hard dependencies allowed only on `core.*` or parent module; optional integrations resolved via `IOptionalIntegration<T>` with fallback behaviour; event handlers run only if consuming module is entitled.

## Why
Makes independent purchasing technically true, not just commercially.

## Alternatives considered
- Direct project references between modules
- Shared database joins across modules

## Advantages
- Low coupling; modules extractable
- Explicit, reviewable dependency graph

## Disadvantages
- Contracts need versioning discipline
- Some duplication of read models

## Scalability impact
Allows moving a module out of process later by implementing Contracts remotely.

## Security impact
Disabled module data not reachable through other modules' integrations.

## Operational impact
Dependency graph validated in CI and at startup.

## Amendment — final baseline review
Dependency classes formalised as Hard / Optional / Integration / None with a full matrix (architecture §14). Sub-modules: `attendance.biometric → attendance`, `payroll.tax → payroll`, `employee.documents|letters|exit → core.employee`.
