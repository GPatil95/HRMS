# ADR-011: Permission codes + role scope levels + assignment scope targets + user overrides (deny wins)

- **Status:** Proposed
- **Area:** Authorization

## Context
Need granular action permissions and data scopes (own, reports, department, branch, selected, tenant), role permissions plus user overrides.

## Decision
Permissions are code-defined strings `module.resource.action`; roles grant permissions with a scope level; user-role assignments carry scope targets; time-bound user overrides grant or deny; deny wins; union of grants. Effective set cached by version.

## Why
Expressive enough for HR org structures while staying understandable for tenant admins; codes in C# are single source of truth.

## Alternatives considered
- Pure RBAC without scopes
- ABAC/policy engine (OPA, Cedar)
- ACL per record

## Advantages
- Covers all brief requirements
- Fast evaluation (cached sets, SQL scope filters)
- Easy to explain in UI

## Disadvantages
- Scope filters must be applied in every relevant query (mitigated by shared helper + tests)
- Complex combinations (overrides + multiple roles) need clear UI

## Scalability impact
Scope queries use closure table and indexed branch/department columns; permission evaluation O(1) from cache.

## Security impact
Deny-wins and no-self-grant rules prevent escalation; 404 outside scope prevents enumeration.

## Operational impact
Tenant admins need good role templates; permission changes audited.

## Amendment — final baseline review
Added action `configure`; added scope level `company` (multi-company tenants) distinct from `tenant`; formalised the **grant ceiling rule** (architecture §10.5): a user cannot grant a permission, scope or role exceeding what they hold, including assignment targets. Menu → sub-menu → page hierarchy declared in manifests.
