# 2–4. Repository, Solution and Front-end Structure

## 2.1 Monorepo layout

```
hrms/
├── backend/
│   ├── Hrms.slnx
│   ├── Directory.Build.props          # nullable, warnings-as-errors, analyzers, LangVersion
│   ├── Directory.Packages.props       # Central Package Management (single version per package)
│   ├── .editorconfig
│   ├── src/
│   │   ├── Hosts/
│   │   │   ├── Hrms.Api/              # composition root: middleware, auth, module registration, OpenAPI
│   │   │   ├── Hrms.Worker/           # Hangfire server, outbox dispatcher, recurring dispatchers
│   │   │   └── Hrms.Migrator/         # tenant provisioning + platform/tenant migrations (CLI + job)
│   │   ├── BuildingBlocks/
│   │   │   ├── Hrms.SharedKernel/                 # Entity, AggregateRoot, ValueObject, DomainEvent, Result, Error, Guard, Clock
│   │   │   ├── Hrms.BuildingBlocks.Application/   # abstractions: ITenantContext, ICurrentUser, IPermissionChecker,
│   │   │   │                                      # IDataScopeProvider, IEntitlementService, IAuditWriter, IFileService,
│   │   │   │                                      # INotificationService, IJobScheduler, IOutbox, paging/sorting/filter models
│   │   │   ├── Hrms.BuildingBlocks.Infrastructure/# EF base DbContext, interceptors (audit columns, audit trail, outbox,
│   │   │   │                                      # soft delete), tenant DB connection factory, Redis, S3, email, Hangfire glue
│   │   │   ├── Hrms.BuildingBlocks.Web/           # endpoint conventions, [RequireModule]/[RequirePermission], ProblemDetails,
│   │   │   │                                      # correlation, tenant resolution middleware, API versioning setup
│   │   │   └── Hrms.BuildingBlocks.Testing/       # Testcontainers fixtures, tenant factory, auth helpers
│   │   ├── Platform/                              # modules backed by the PLATFORM DB
│   │   │   ├── Hrms.Platform.Tenancy/ (+ .Contracts)        # tenants, domains, DB registry, clusters, provisioning
│   │   │   ├── Hrms.Platform.Catalog/ (+ .Contracts)        # modules, features, plans, subscriptions, entitlements
│   │   │   ├── Hrms.Platform.Identity/ (+ .Contracts)       # platform users/roles/permissions, support access grants
│   │   │   ├── Hrms.Platform.Billing/ (+ .Contracts)        # later
│   │   │   └── Hrms.Platform.Operations/ (+ .Contracts)     # usage, quotas, platform config, feature flags, health
│   │   └── Modules/                               # modules backed by each TENANT DB
│   │       ├── Identity/   Hrms.Modules.Identity, Hrms.Modules.Identity.Contracts
│   │       ├── Organization/ ...
│   │       ├── Employee/ ...   (minimal core only in foundation phase)
│   │       ├── Audit/ ...
│   │       ├── Files/ ...
│   │       ├── Notifications/ ...
│   │       ├── Workflow/ ...
│   │       ├── Configuration/ ...
│   │       ├── ImportExport/ ...
│   │       ├── Dashboard/ ...
│   │       └── (later) Attendance/, Leave/, Payroll/, Tax/, Recruitment/, ...
│   └── tests/
│       ├── Hrms.ArchitectureTests/         # layering + module boundary + "every endpoint declares permission" rules
│       ├── Hrms.Modules.<X>.UnitTests/     # one per module (domain + application)
│       ├── Hrms.IntegrationTests/          # real PostgreSQL/Redis/MinIO via Testcontainers
│       ├── Hrms.Api.Tests/                 # HTTP-level tests via WebApplicationFactory
│       └── Hrms.SecurityTests/             # tenant isolation, authorization matrix, entitlement, scope tests
├── frontend/                               # pnpm workspace
│   ├── package.json, pnpm-workspace.yaml, tsconfig.base.json
│   ├── apps/
│   │   ├── web/                            # tenant application (React + Vite)
│   │   ├── admin/                          # platform admin portal (separate app, separate origin)
│   │   └── mobile/                         # React Native + Expo
│   └── packages/
│       ├── api-client/                     # generated from OpenAPI (types + fetch client + TanStack Query hooks)
│       ├── auth/                           # session handling (web BFF cookie / mobile token store)
│       ├── ui/                             # design system wrappers for web
│       ├── shell-core/                     # module registry types, permission helpers, nav model (shared web/mobile)
│       ├── i18n/                           # translations
│       └── config/                         # eslint, tsconfig, prettier presets
├── database/
│   ├── bootstrap/                          # cluster bootstrap SQL: roles, extensions (citext, btree_gist, pg_stat_statements)
│   ├── pgbouncer/                          # pooler config templates
│   └── reference-data/                     # versioned seed data (countries, currencies, statutory packs)
├── infra/
│   ├── docker/                             # Dockerfiles (api, worker, migrator, web, admin)
│   ├── compose/                            # docker-compose.dev.yml: postgres, redis, minio, mailpit, clamav, otel/grafana
│   ├── terraform/                          # cloud resources per environment (modules + envs/dev|uat|prod)
│   └── deploy/                             # Helm charts or ECS task definitions (decision pending)
├── docs/
│   ├── architecture/  adr/  runbooks/  security/  api/
├── .github/workflows/                      # ci.yml, codeql.yml, release.yml, deploy-*.yml, mobile.yml
├── .env.example                            # local-dev only, placeholder values
└── README.md
```

### Why this structure

| Choice | Reason | Alternative considered |
|---|---|---|
| **Monorepo** | Backend + web + mobile + infra change together (API contract → generated client → UI). One PR, one CI, atomic versioning of contracts. | Polyrepo per app: contract drift, cross-repo PR coordination, duplicated CI. |
| **`backend/` and `frontend/` top-level split** | Each has its own toolchain (dotnet vs pnpm); CI path filters keep pipelines fast. | Mixed project folders per module (backend + frontend together): appealing for vertical ownership but fights both toolchains. |
| **Two projects per module (`Module` + `Module.Contracts`)** | Contracts is the only thing other modules may reference — compiler-enforced boundary. Layering *inside* the module is by folder and enforced by architecture tests. Keeps project count ~2×modules (≈60) instead of 5×modules (≈150). | Four/five projects per module (Domain/Application/Infrastructure/Api): stronger compile-time layering but very slow builds and navigation at 30 modules. A module can be split later if it grows (Payroll probably will). |
| **`Platform/` vs `Modules/`** | Makes the database boundary visible: Platform code can never take a tenant `DbContext`, tenant modules can never take the platform `DbContext` (arch test). | Single list of modules with a flag: easier to violate. |
| **Separate `admin` front-end app** | Different origin → different cookies, CSP, and deployment; a tenant XSS cannot reach platform admin sessions; platform portal can be IP-restricted. | One SPA with role-based areas: smaller bundle count but mixes the two security realms. |
| **`database/` folder** even though EF migrations live with modules | Cluster-level SQL (roles, extensions, pooler config) is not owned by any module. | Putting it in infra/: fine too, but DBAs look for it here. |

## 3. ASP.NET Core solution — inside a module

```
Modules/Organization/Hrms.Modules.Organization/
├── OrganizationModule.cs           # IModuleDefinition: code, manifest, RegisterServices, MapEndpoints
├── Manifest/
│   ├── Permissions.cs              # const strings: "org.branch.view", "org.branch.create", ...
│   ├── Features.cs
│   └── Menu.cs                     # menu items with required permission + feature
├── Domain/
│   ├── Branch.cs, Department.cs, ...
│   └── Events/
├── Application/
│   ├── Branches/
│   │   ├── CreateBranch.cs         # command record + validator + handler (one file per use case)
│   │   ├── GetBranches.cs          # query + handler (applies data scope)
│   │   └── BranchDto.cs
│   └── EventHandlers/
├── Infrastructure/
│   ├── OrganizationDbContext.cs    # schema "org", inherits TenantDbContextBase
│   ├── Configurations/             # IEntityTypeConfiguration<T>
│   └── Migrations/
└── Api/
    └── BranchesController.cs       # [ApiVersion("1.0")] [Route("api/v{version:apiVersion}/org/branches")]
                                    # [RequireModule(OrganizationModule.Code)]
Modules/Organization/Hrms.Modules.Organization.Contracts/
├── IOrganizationQueries.cs         # e.g. GetBranchIdsForEmployee, GetDepartmentTree
├── Dtos/
└── IntegrationEvents/              # BranchCreated, EmployeeTransferred ...
```

`IModuleDefinition` (shape):

```csharp
public interface IModuleDefinition
{
    string Code { get; }                                  // "payroll"
    string DisplayName { get; }
    ModuleKind Kind { get; }                              // Core | Optional | SubModule
    IReadOnlyList<string> RequiredModules { get; }        // only core.* or parent
    IReadOnlyList<string> OptionalIntegrations { get; }
    IReadOnlyList<FeatureDefinition> Features { get; }
    IReadOnlyList<PermissionDefinition> Permissions { get; }
    IReadOnlyList<MenuItemDefinition> Menu { get; }
    void RegisterServices(IServiceCollection services, IConfiguration config);
    void MapEndpoints(IEndpointRouteBuilder app);         // controllers are discovered; minimal APIs optional
}
```

The host discovers module definitions at startup, validates the dependency graph, registers services, and exposes the combined manifest. The Migrator syncs permissions/features/menus into the Platform catalog and into every tenant DB (idempotent upsert by code), so codes defined in C# are the single source of truth.

Controllers vs minimal APIs: **controllers** — the brief asks for thin controllers, they integrate cleanly with API versioning, filters and OpenAPI grouping, and most .NET developers know them. Minimal APIs remain allowed for tiny endpoints (health, app-config).

## 4. Front-end structure

### 4.1 Web app (`frontend/apps/web`)

Stack: **React 19 + TypeScript (strict) + Vite**, TanStack Router (type-safe routes, code-split), TanStack Query (server state), TanStack Table (headless grids), react-hook-form + zod (form *shape* only; business validation errors come from the API as RFC 9457 ProblemDetails and are mapped to fields), i18next, a component library (decision pending: **MUI (core, MIT)** or **shadcn/ui (Radix + Tailwind)**), Vitest + Testing Library, Playwright.

```
apps/web/src/
├── app/
│   ├── main.tsx, router.tsx, providers.tsx        # QueryClient, auth, theme, i18n, error boundary
│   └── shell/
│       ├── AppShell.tsx                           # header, tenant branding, dynamic sidebar, breadcrumbs
│       ├── Sidebar.tsx                            # renders GET /api/v1/me/navigation (already filtered server-side)
│       ├── NotificationCenter.tsx                 # SignalR-driven
│       └── ApprovalInbox.tsx
├── modules/                                       # one folder per module, each exports a ModuleRegistration
│   ├── registry.ts                                # lazy imports: { code: 'org', routes, widgets, menuIcons }
│   ├── org/  (routes/, pages/, components/, api.ts)
│   └── ...
├── components/                                    # DataTable (server paging/sort/filter), FormFields, Dialog,
│                                                  # ConfirmDialog, FileUpload, ImportWizard, ExportButton,
│                                                  # EmptyState, ErrorState, Skeletons, PermissionGate
├── dashboard/                                     # widget host + layout engine; widgets registered by modules
├── lib/                                           # http (CSRF header, correlation id, error mapping), formatters
└── styles/
```

Plug-in model: a module ships a `ModuleRegistration` `{ code, routes, widgets, icons }`. The shell mounts routes only for modules present in `/api/v1/me/session` → `entitledModules`, and the sidebar is driven by the server-filtered navigation tree — so adding a module never requires editing navigation code. `PermissionGate` / `usePermission('payroll.run.process')` hide buttons **for UX only**; the API enforces the same rule independently.

### 4.2 Admin portal (`frontend/apps/admin`)

Same stack and shared packages, separate origin `admin.hrms.com`, separate auth realm, IP allow-list at WAF, mandatory MFA. Screens: tenants, domains, provisioning status, DB registry, plans/modules/features, subscriptions & entitlements, platform users/roles, support-access grants, platform audit, health.

### 4.3 Mobile (`frontend/apps/mobile`)

Expo (managed workflow + EAS Build/Update), Expo Router, TanStack Query, `expo-secure-store` for refresh token, `expo-local-authentication` for biometric unlock, `expo-notifications` for push. Shares `api-client`, `shell-core`, `i18n`. First launch asks for **company code** (or email domain discovery) → resolves to the tenant's login host via `GET https://auth.hrms.com/api/v1/tenant-discovery` (returns only the public login URL and branding, nothing sensitive) → OIDC Authorization Code + PKCE. App calls `/api/v1/app-config` on start for minimum supported version (forced upgrade).

### 4.4 Generated API client

CI generates the OpenAPI document from the API build and runs `orval` (or `openapi-typescript` + `openapi-fetch`) into `packages/api-client`. A PR that changes the API without regenerating fails CI. `oasdiff` flags breaking changes against the last released spec.
