# ADR-009: Separate platform-admin realm, portal and audience; no implicit tenant data access

- **Status:** Proposed
- **Area:** Security / Platform admin

## Context
Brief: platform admin must not have unlimited invisible access to employee data.

## Decision
Platform admins authenticate against the platform identity store on `admin.hrms.com` with mandatory MFA; their tokens are rejected by tenant APIs. Tenant data access only via support grants (ADR-010).

## Why
Hard separation prevents accidental or malicious access by staff and simplifies customer assurance.

## Alternatives considered
- Super-admin role inside tenant realm
- Single portal with role-based areas

## Advantages
- Clear separation of duties
- Separate origin protects against cross-realm XSS/session issues
- Admin portal can be IP-restricted

## Disadvantages
- Two front-end apps and two identity stores to maintain
- Support workflows need extra steps

## Scalability impact
Neutral.

## Security impact
Strong insider-threat control; auditable.

## Operational impact
Support staff need training on grant workflow.
