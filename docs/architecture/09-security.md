# 17. Security Architecture

> **Final baseline note:** the full threat model and the per-topic security review are in [23-security-threat-model.md](23-security-threat-model.md); compliance controls in [24](24-compliance-backup-dr-deployment.md).

## 17.1 Threat model highlights (STRIDE, top items)

| Threat | Example | Primary controls |
|---|---|---|
| Cross-tenant data access | tampered tenant ID, wrong connection string, cache key collision | host+token tenant resolution, per-tenant DB roles, `meta.tenant_identity` check, prefixed cache/storage keys, isolation test suite |
| Privilege escalation | HR exec grants themselves payroll access | no self-role edit, can't grant what you don't hold, audited security events, step-up MFA |
| Broken object-level authorization (IDOR) | `GET /employees/{otherBranchId}` | data scope checks, 404 outside scope, authorization matrix tests |
| Account takeover | credential stuffing, phishing | MFA/passkeys, rate limits, lockout, breached-password check, session revocation, anomaly alerts |
| Platform insider access | support engineer browses salaries | separate realm, no implicit tenant access, time-bound approved support grants, dual audit |
| Data exfiltration | bulk export, file links shared | export permission + step-up MFA + security event, 60 s presigned URLs, watermarking for letters later |
| Injection | SQL, template, CSV formula, XSS | EF parameterisation (raw SQL only `FromSql` interpolated), Scriban sandbox, CSV escaping, React escaping + strict CSP |
| Malicious files | malware in uploads | quarantine + scan, type sniffing, attachment disposition |
| Secrets leakage | connection strings in git/logs | secret manager, gitleaks, redaction, no secrets in Redis/job args |
| Tampering with audit | cover tracks | INSERT-only grants, triggers, hash chain, WORM export |

## 17.2 Controls by layer

**Edge**: CDN + WAF (OWASP managed rules, bot control, geo/IP rules for admin), TLS 1.2+ only (TLS 1.3 preferred), HSTS (preload after stabilisation), DDoS protection.

**Application**
- Security headers middleware: `Content-Security-Policy` (strict, nonce-based, no `unsafe-inline`), `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy`, `frame-ancestors 'none'`, `Cross-Origin-Opener-Policy: same-origin`.
- CORS: none needed for same-origin BFF web; explicit allow-list for any other origin; never `*` with credentials.
- CSRF: antiforgery header for cookie-authenticated unsafe requests; bearer-token requests are not CSRF-prone.
- Rate limiting: ASP.NET Core rate limiter partitioned by IP (anonymous), user and tenant; Redis-backed distributed counters for login/OTP/reset endpoints; per-tenant API quotas.
- Input validation: FluentValidation on every command; model binding limits (request body size, JSON depth); allow-list enums.
- Output encoding: React default escaping; `dangerouslySetInnerHTML` banned by lint rule except sanitized (DOMPurify) rich text component.
- Error handling: ProblemDetails with correlation ID only; `UseDeveloperExceptionPage` only in Development.
- Deserialization: System.Text.Json only, no polymorphic type-name handling.
- SSRF: outbound HTTP only through named `HttpClient`s with allow-listed hosts (webhooks later validated against private IP ranges).

**Data**
- Encryption in transit everywhere (client↔edge, edge↔app, app↔PostgreSQL `sslmode=verify-full`, app↔Redis TLS, S3 HTTPS).
- Encryption at rest: managed PostgreSQL storage encryption (KMS), Redis at-rest encryption, S3 SSE-KMS, encrypted backups.
- **Field-level encryption** for highly sensitive attributes (bank account, PAN, Aadhaar reference/last-4 only, passport, medical notes): envelope encryption with a per-tenant data key wrapped by KMS, `bytea` ciphertext + HMAC blind index for equality lookups; decryption only in services holding the relevant permission. Full Aadhaar numbers should not be stored (Aadhaar Act/UIDAI constraints) — store masked/reference token; confirm with counsel.
- Least privilege DB roles: app role DML only; migrator role DDL; read-only reporting role; no superuser use by applications.
- Data classification attributes on DTOs/entities drive redaction in logs, audit masking, and export warnings.

**Identity**: see authentication doc (MFA, passkeys, rotation, revocation, lockout).

**Secrets**: AWS Secrets Manager (or Vault) for DB credentials, signing keys material references, provider API keys; workload identity (IAM roles for tasks) — no long-lived cloud keys in config; `.env.example` contains placeholders only; gitleaks pre-commit + CI.

**Platform admin & support access**
- Platform admins manage tenants/subscriptions without any tenant data access by default.
- Support access flow: platform user requests grant (ticket, reason, level, duration ≤ 8 h) → tenant admin approves in their portal (or, for emergencies, two platform super-admins approve "break-glass" and the tenant admin is notified immediately) → support user obtains a tenant-scoped token via token exchange whose claims include `act` (actor = platform user) and `support_grant_id` → every request is checked against the grant (expiry, level, permission list) and audited in **both** tenant and platform audit with `source=support` → UI banner "Support session active" → tenant can revoke at any time.

**Supply chain**: Central Package Management, lock files (`packages.lock.json`, `pnpm-lock.yaml`), Dependabot/Renovate, `dotnet list package --vulnerable`, `pnpm audit`, Trivy image scan, SBOM (CycloneDX) per image, image signing (cosign), minimal base images (chiseled/distroless .NET images), non-root containers, read-only root FS.

## 17.3 Compliance considerations (to validate with legal)

- India **DPDP Act 2023** (+ Rules): purpose limitation, consent/legitimate use for employment, data principal rights (access, correction, erasure where lawful), breach notification, data retention limits. Design hooks: data inventory by classification, per-tenant retention policies, export/erasure jobs, breach-response runbook.
- Tenant = data fiduciary for its employees; we are the data processor → DPA templates, sub-processor list, audit rights.
- Readiness for ISO/IEC 27001 and SOC 2 Type II (controls above map to most Annex A / TSC items).
- Payroll/tax record retention per Income-tax Act and labour laws (typically ≥ 8 years) — drives audit/file retention defaults.

## 17.4 Security testing & operations

SAST (CodeQL), secrets scanning (gitleaks), dependency & container scanning, DAST (OWASP ZAP baseline on UAT each release), authorization matrix & tenant isolation suites in CI, annual external penetration test before GA and after major changes, security event alerting (failed-login spikes, `TENANT_MISMATCH`, refresh-token reuse, break-glass usage, bulk exports), incident response runbook.

## 17.5 Local secret-store master key (dev runbook)

`ops.secrets` ciphertexts are AES-256-GCM under `Secrets:MasterKey`. If the key changes between the run that
*wrote* a secret and the run that *reads* it, decryption fails with
`AuthenticationTagMismatchException` (the error seen when tenant provisioning fails at `create_roles`).

**Key resolution order** (`DbSecretStore`):

1. `Secrets__MasterKey` env var, when set (base64, 32 bytes).
2. `Secrets__MasterKeyFile` — path to a file holding the key; auto-generated (mode 600) on first use. Docker Compose maps this to `/keys/master.key` on the `keydata` volume so the key survives container rebuilds/restarts.
3. Development fallback: `~/.hrms-dev-masterkey` (per-user) so `dotnet run` and the CLI share one key. Production with no key configured fails fast.

**Rotation — old key known**: `Hrms.Cli rekey --old-key <base64>` re-encrypts every `ops.secrets` row under the currently configured key; rows not decryptable with the old key are reported and skipped.

**Recovery — old key lost**: `Hrms.Cli repair-secrets` (requires `HRMS_CLUSTER_ADMIN_PASSWORD`) re-stores the cluster admin secret under the current key, then regenerates every tenant's app/migrator role passwords via `ALTER ROLE` and re-encrypts them. Tenant data is untouched; no tenant is deleted or recreated.
