# ADR-030: Employee/HR Core always on; Documents, Letters, Exit/FNF as toggleable sub-modules

- **Status:** Proposed (final baseline review)
- **Area:** Module packaging

## Context
Every example tenant includes Employee; authorization scopes depend on employee/org data.

## Decision
`core.employee` (full HR Core) is part of every tenant. `employee.documents`, `employee.letters`, `employee.exit` are sub-modules (hard → core.employee) — included in base plan by default, but toggleable for commercial flexibility.

## Why
Scopes, workflows and every module need the employee master; packaging flexibility kept for add-ons.

## Alternatives considered
- Employee as an optional module
- Documents/Letters/Exit merged into HR Core

## Advantages
- Stable foundation for all modules
- Commercial flexibility

## Disadvantages
- Base plan always includes HR Core cost

## Scalability impact
Neutral.

## Security impact
Single authoritative employee master for scope evaluation.

## Operational impact
None.
