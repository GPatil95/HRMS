# 12. Audit Architecture

Two kinds of audit, both built before any business module:

| | Data-change audit | Security audit |
|---|---|---|
| What | create/update/delete of audited entities + explicit business actions (approve, finalize, export, share) | authentication, session, credential, role/permission, support access, API auth failures |
| Where | tenant DB `audit.audit_entries` (platform: `audit.platform_audit_events`) | tenant DB `audit.security_events` (platform: `audit.platform_security_events`) |
| Written by | EF Core `SaveChanges` interceptor (same transaction) + `IAuditWriter.RecordAction(...)` | `ISecurityEventWriter` from auth/identity code |

Tenant audit lives in the tenant DB so it is isolated, backed up and exported with the tenant's data, and deleted only with the tenant.

## 12.1 Audit record

| Column | Notes |
|---|---|
| `id uuid` (v7), `occurred_at timestamptz` | |
| `tenant_id` | redundant inside tenant DB but kept so exported records are self-describing |
| `module_code`, `entity_type`, `entity_id` | e.g. `employee`, `Employee`, `018f…` |
| `action` | `create` / `update` / `delete` / `restore` / business verbs (`approve`, `reject`, `finalize`, `export`, `download`, `share`, `import`) |
| `actor_type`, `actor_id`, `actor_display` | `user` / `service` / `job` / `integration` / `support` (+ `on_behalf_of` for support/delegation) |
| `changes jsonb` | `[{field, old, new}]` for changed fields only; masked/omitted per field classification |
| `before jsonb`, `after jsonb` | optional full snapshots for entities flagged `SnapshotOnChange` (financial records) |
| `source` | `web` / `mobile` / `api` / `import` / `job` / `integration` / `ai` / `system` |
| `ip inet`, `user_agent`, `device_id` | from request context where applicable |
| `correlation_id`, `request_id` | links to logs/traces |
| `reason`, `workflow_instance_id` | from command when provided (required for sensitive actions, e.g. salary change) |
| `prev_hash bytea`, `hash bytea` | tamper-evidence chain (see below) |

Partitioned monthly (`PARTITION BY RANGE (occurred_at)`), indexes on `(entity_type, entity_id, occurred_at)`, `(actor_id, occurred_at)`, `(correlation_id)`.

## 12.2 Capture mechanics

- Entities opt in with `[Audited]` (or `IAuditable`); fields opt out/mask with `[AuditIgnore]`, `[AuditMasked]` (e.g. bank account → `XXXX1234`), `[Sensitive]` (value never stored, only "changed").
- `AuditInterceptor` inspects the EF `ChangeTracker` before save, builds entries, and adds them to the **same transaction** → no business change without its audit record, no audit record for a rolled-back change.
- Business actions not expressible as field changes call `IAuditWriter.RecordAction(entity, action, reason, details)` inside the use case.
- Bulk operations (imports, payroll runs) write one summary entry + per-row entries in batches; source `import`/`job`, linked by `correlation_id` and job ID.
- Raw SQL / `ExecuteUpdate` bypasses the change tracker → **banned for audited entities** unless the handler writes audit explicitly (arch test + review checklist).

## 12.3 Protection against modification

1. **Privileges:** the tenant app role has `INSERT, SELECT` only on `audit.*` — no `UPDATE/DELETE/TRUNCATE`. Partition maintenance runs with the migrator role.
2. **Trigger guard:** `BEFORE UPDATE OR DELETE` trigger raises an exception (defence in depth if grants are misconfigured).
3. **Hash chain:** each entry's `hash = SHA-256(prev_hash || canonical_json(entry))`, chained per tenant stream (serialised through `audit_chain_heads` row lock, or per partition/day to avoid contention); a scheduled verifier job recomputes and alerts on breaks.
4. **WORM export:** daily job exports the previous day's audit partition (and chain head) to S3 with **Object Lock (compliance mode)** for the retention period. Even a DBA cannot silently rewrite history.
5. Retention policy per tenant (default 8 years for payroll-related audit, configurable minimums per regulation); old partitions detached and archived, not deleted ad hoc.

## 12.4 What is never logged

Passwords, password hashes, OTP/TOTP codes, recovery codes, access/refresh tokens, cookies, API secrets, full card/bank numbers, full Aadhaar numbers, document contents. A redaction layer (Microsoft.Extensions.Compliance.Redaction with data classification attributes) applies to both audit and application logs.

## 12.5 Access to audit

- Permissions `audit.entry.view` (scoped like other data — a branch HR sees audit for their branch's employees), `audit.security.view` (tenant admin/security), `audit.export` (sensitive, step-up MFA, itself audited).
- Entity history UI component: any page can show "History" for a record via `GET /api/v1/audit/entities/{type}/{id}`.
- Platform admins **cannot** read tenant audit except under an approved support grant; platform audit records all support activity on both sides.

## 12.6 Platform audit

Same model in `hrms_platform.audit`: tenant created/suspended, plan/subscription/entitlement changes, DB provisioning/migration/move, platform user/role changes, support grant lifecycle, platform config and feature-flag changes.
