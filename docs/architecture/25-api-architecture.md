# 25. API Architecture (consolidated)

Builds on §14 (versioning) of [08-common-services.md](08-common-services.md).

| API surface | Host / path | Auth | Consumers |
|---|---|---|---|
| Tenant REST API | `{tenant}.hrms.com/api/v{n}/{module}/...` | BFF cookie + CSRF | web SPA |
| Mobile/integration REST API | `api.hrms.com/api/v{n}/...` | Bearer JWT (`tid` claim) | mobile, tenant integrations |
| BFF endpoints | `{tenant}.hrms.com/bff/login|logout|csrf|user` | cookie | web |
| Platform API | `admin.hrms.com/api/v{n}/platform/...` | platform realm cookie/JWT | admin portal |
| Tenant discovery | `auth.hrms.com/api/v1/tenant-discovery` | anonymous, rate-limited, returns login URL + branding only | mobile |
| Biometric ingest | `api.hrms.com/api/v1/biometric/ingest` | client credentials (connector), idempotency key | connectors |
| Webhooks (inbound) | `api.hrms.com/webhooks/{provider}` | provider signature verification | email/payment/calendar providers |
| Webhooks (outbound, later) | tenant-configured URLs | HMAC-signed payloads, SSRF-safe delivery, retries | customer systems |
| Real-time | `/hubs/notifications`, `/hubs/workitems`, `/hubs/jobs` | same as REST | web, mobile |
| AI | `/api/v1/ai/*` | same as REST + `ai.assistant.use` | web, mobile |

Conventions (all surfaces): resource-oriented URLs per module; thin controllers → application services; request DTO per use case (mass-assignment protection, unknown fields rejected on writes); RFC 9457 errors with stable `code`; `ETag/If-Match` concurrency; `Idempotency-Key` on POSTs with side effects; pagination caps; filtering/sorting via allow-listed fields; ISO 8601 dates with explicit time zones; correlation id header echoed; rate-limit headers; OpenAPI per version and realm, generated TS client; breaking-change detection; deprecation/sunset headers; 12-month support for previous major version.

Every endpoint declares: `[RequireModule]` (by convention), `[RequireFeature]` (optional), `[RequirePermission]` / `[AuthenticatedOnly]` / reviewed `[AllowAnonymous]`; scoped handlers call `IDataScopeProvider`.
