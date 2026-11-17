# ADR-037: Lowercase physical database names

- **Status:** Proposed (final baseline review)
- **Area:** Database

## Context
Brief examples use `HRMS_Platform`, `HRMS_Tenant_1001`. PostgreSQL folds unquoted identifiers to lowercase; mixed-case names require quoting in every tool/script.

## Decision
Physical names `hrms_platform`, `hrms_tenant_1001`; mixed-case form shown as display name. **Pending approval.**

## Why
Avoids quoting bugs in tooling, backups, monitoring and scripts.

## Alternatives considered
- Use quoted mixed-case names exactly as in the brief

## Advantages
- Tool-friendly, conventional

## Disadvantages
- Differs cosmetically from the brief

## Scalability impact
None.

## Security impact
None.

## Operational impact
Simpler runbooks.
