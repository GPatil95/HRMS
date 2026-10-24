# 19. CI/CD, Environments and Observability

## 19.1 Environments

| Env | Purpose | Data | Deploy |
|---|---|---|---|
| Local | developer machines: `docker compose -f infra/compose/docker-compose.dev.yml up` (PostgreSQL, Redis, MinIO, Mailpit, ClamAV, OTel collector + Grafana) | seeded demo tenants | `dotnet run` / `pnpm dev` |
| Development | shared integration, ephemeral PR previews optional | synthetic | auto on merge to `main` |
| Testing / UAT | QA + customer acceptance, release candidates | synthetic / anonymised | manual approval, release tags |
| Production | customers | real | manual approval + change window, progressive |

Configuration: `appsettings.json` (defaults) → `appsettings.{Environment}.json` (non-secret) → environment variables → secret manager (via configuration provider). All options classes bound with `ValidateDataAnnotations().ValidateOnStart()` so misconfiguration fails at boot. `.env.example` (placeholders) for local compose only; real `.env` git-ignored. Separate cloud accounts/subscriptions per environment; prod secrets inaccessible from non-prod roles.

## 19.2 Pipelines (GitHub Actions)

```mermaid
flowchart LR
  PR[Pull request] --> B[Build backend + frontend]
  B --> L[Lint / format / analyzers]
  L --> U[Unit + architecture tests]
  U --> I[Integration + API + authz matrix + isolation tests<br/>Testcontainers]
  I --> S[CodeQL, gitleaks, dependency review,<br/>OpenAPI diff, EF pending-model-changes,<br/>migration SQL artefact for review]
  S --> M{merge to main}
  M --> IMG[Build images tagged with git SHA<br/>SBOM, Trivy, cosign sign, push]
  IMG --> DEV[Deploy Dev: Migrator platform → tenants → API/Worker rolling]
  DEV --> SMOKE[Smoke + Playwright E2E + ZAP baseline]
  SMOKE --> UAT[Promote same image to UAT (approval)]
  UAT --> PROD[Promote to Prod (approval): migration waves → canary → full]
```

- **Build once, promote the same signed image** through environments.
- Path filters: backend changes run backend jobs; frontend changes run frontend jobs; contract changes run both.
- Required checks + CODEOWNERS (security-sensitive paths: `BuildingBlocks/*Auth*`, `Tenancy`, `Audit`, migrations → require security/architect review).
- Mobile: EAS Build on release branches, EAS Update (OTA) for JS-only fixes to compatible native versions, store submission manual.

## 19.3 Database migration strategy in CI/CD

1. PR: generated migration SQL attached as artefact; reviewer checks for locks (no table rewrites, `CREATE INDEX CONCURRENTLY`, batched backfills).
2. Deploy: `Hrms.Migrator migrate platform` → `migrate tenants --wave canary` → verify → `--wave all` (parallelism per cluster) → then rolling deploy of API/Worker. Because releases follow expand/contract, old pods keep working during the wave.
3. Per-tenant status in registry; dashboard shows tenants on each schema version.
4. **Rollback**: application rollback = redeploy previous image (schema is backward compatible). Schema rollback is *forward-fix* by default; EF `Down` migrations are maintained and tested in CI for non-destructive changes, and a pre-wave snapshot/PITR point exists for disaster cases. Contract (destructive) migrations only after the previous release is retired.

## 19.4 Deployment & rollback

Rolling or blue/green deployment behind the load balancer with readiness probes; automatic rollback on failed health checks or error-rate SLO breach during canary; feature flags to dark-launch risky features; maintenance mode per tenant (503) for tenant-level incidents.

## 22. Observability

| Signal | Approach |
|---|---|
| Logs | structured JSON via `ILogger` (+ Serilog sinks optional) → OTLP collector → log store (Grafana Loki / Elastic / CloudWatch). Enriched with `tenant_id`, `user_id` (opaque), `correlation_id`, `trace_id`, `module`. PII redaction enforced. |
| Traces | OpenTelemetry for ASP.NET Core, HttpClient, Npgsql, Redis, Hangfire → Tempo/X-Ray/Jaeger |
| Metrics | OpenTelemetry metrics → Prometheus/Grafana: request rate/latency/errors per endpoint & module (RED), per-tenant top-N, DB pool usage, queue depth, job duration/failures, cache hit rates, login success/failure, entitlement cache staleness |
| Errors | Sentry (backend, web, mobile) with PII scrubbing, release tracking |
| Health | `/health/live` (process), `/health/ready` (platform DB, Redis, S3, jobs DB); tenant DB health sampled by a background monitor (not in readiness — N tenants) and surfaced in admin portal |
| DB monitoring | managed-service metrics + `pg_stat_statements`, slow query log, per-database size/connections, autovacuum, replication lag, backup status |
| Jobs | Hangfire dashboard (platform realm) + metrics & alerts on failures/backlog |
| Security monitoring | security events stream → SIEM; alerts for anomalies (§17.4) |
| SLOs (proposed) | 99.9% monthly availability; p95 < 300 ms for interactive reads, < 800 ms writes; payroll run of 10k employees < 15 min |

### Additional monitoring (final baseline)

| Area | Signals |
|---|---|
| Tenant provisioning | provisioning operations by status/step, duration, failures (alert), tenants in `provisioning`/`maintenance` > threshold |
| Migrations | tenants per schema version, wave progress, failed tenants |
| Integrations | biometric connector heartbeats, last sync per device, ingest error rate, unmapped punches; email/SMS/push provider success & bounce rates; calendar sync failures; payment webhooks; LLM latency/error/token usage |
| Backups | per-tenant last successful logical backup age (alert > 26 h), restore test results |
| Security | failed logins, lockouts, `TENANT_MISMATCH`, refresh reuse, support sessions, break-glass, bulk exports, AI denied tool calls |
