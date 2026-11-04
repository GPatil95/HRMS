# 20–21. Implementation Roadmap, Risks and Open Questions (final baseline)

## 20.1 Foundation phases (no business modules), each ends with a review gate

| Phase | Deliverables | Exit criteria |
|---|---|---|
| **0. Baseline approval** | this document set; ADRs accepted/amended; pending decisions (§21.3) answered | ADRs 001–039 accepted or amended |
| **1. Scaffolding & delivery pipeline** | monorepo, solution + building blocks, analyzers, CPM, docker-compose dev stack, CI (build, test, CodeQL, gitleaks, arch tests), web/admin/mobile shells, OpenAPI → generated client | green CI; one-command local start |
| **2. Platform core & tenancy** | Platform DB (tenancy, catalog, subscription, ops), DB registry & clusters, Migrator (provision, waves), tenant resolution, TenantContext, connection factory + identity check, admin APIs/UI for tenants, domains, limits | 2 tenants provisioned end-to-end; **T1** passes |
| **3. Identity & authentication** | tenant + platform identity, OpenIddict, BFF web login, mobile PKCE, MFA (TOTP/passkeys), sessions & revocation, lockout & rate limits, security events | auth tests; **T5** |
| **4. Authorization & entitlements** | manifests & catalog sync, entitlements + cache invalidation, roles/scopes/overrides, **grant ceiling**, data scope provider, `/me/session`, `/me/navigation`, platform admin plans/entitlements UI, tenant roles UI | **T2, T3, T4, T8, T9**; authz matrix 100% |
| **5. Common services** | audit (interceptor, append-only, hash chain, WORM), outbox/inbox, Hangfire + tenant job filter, files + scanning, notifications + **email** (SMTP/provider, tenant config, templates, queue, status), workflow engine (all actions), work inbox projection, import/export engine, reporting framework, dashboard framework, config/feature flags, retention/legal hold, SignalR | **T6, T7, T12, T13, T17, T18, T19** |
| **6. Org structure + HR Core skeleton** | companies, branches, departments, designations, grades, locations/floors, cost centres, work calendars, business partners, employee core with effective-dated history & reporting closure | scope tests on real hierarchy |
| **7. Hardening** | observability & alerts, load tests (500 tenant DBs/cluster, 50k-employee tenant), ZAP, backup/restore drill, DR runbook, pen-test readiness, **platform billing MVP** (plans → invoices → GST → payments) before first paying tenant | SLOs in load test; restore drill passed |

## 20.2 Business module order (after the foundation gate; each module has its own design review)

| Order | Module(s) | Rationale |
|---|---|---|
| 1 | **HR Core full** + Employee Documents + Letters | everything depends on employee data; letters needed for joining/exit |
| 2 | Attendance (+ Biometric connector) | most-demanded; independent |
| 3 | Leave | independent; feeds payroll optionally |
| 4 | Payroll + Tax/TDS | highest value & risk; needs statutory rule-pack pipeline; golden-test suite with verified cases |
| 5 | Loans & Advance, Expense & Reimbursement | payroll inputs |
| 6 | **Exit & FNF** | uses all previous providers (works earlier with manual inputs if a customer needs it before payroll) |
| 7 | Recruitment, PMS | talent modules, independent |
| 8 | Assets | feeds exit clearance |
| 9 | Calendar, Meeting Rooms, Workplace, Visitors, Mailroom | workplace suite (shares core office/floor masters) |
| 10 | Projects, My Work | productivity |
| 11 | Client Billing | needs payroll/attendance/timesheets providers |
| 12 | AI Assistant | after module tool catalogue is stable; provider decision required |
| — | Mobile app features | delivered with each module (ESS first: attendance, leave, payslips, approvals) |

## 21.1 Risks

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R1 | Connection explosion with many tenant DBs | High | High | PgBouncer, small pools, cluster sharding, load tests |
| R2 | Migration fan-out failures / schema drift | Medium | High | expand/contract, waves, per-tenant versions, maintenance mode |
| R3 | Operational cost of DB-per-tenant | High | Medium | automation, managed services, capacity planning |
| R4 | Cross-tenant analytics harder | High | Low | usage events; later CDC warehouse |
| R5 | Data-scope filter forgotten in new queries | Medium | High | single `ApplyScope`, generated authz matrix, widget/report consistency tests |
| R6 | Module boundary erosion | Medium | Medium | Contracts-only, arch tests, module-independence tests (T16) |
| R7 | Self-hosted OIDC security burden | Medium | High | OpenIddict, pen test; IdP swappable |
| R8 | Entitlement/permission cache staleness | Low | Medium | versioned keys + pub/sub, ≤30 s; session revocation immediate |
| R9 | Payroll/tax/FNF correctness and frequent statutory change | High | High | rule packs, snapshots, golden tests, maker-checker publishing, domain expert sign-off |
| R10 | Sensitive PII compliance (Aadhaar, bank, health, visitor IDs) | Medium | High | field encryption, masking, retention, legal review |
| R11 | Hangfire storage hotspot | Low | Medium | dedicated jobs DB, queues, abstraction |
| R12 | OSS licence changes | Medium | Medium | permissive libs, seams, licence scan |
| R13 | Mobile version fragmentation | Medium | Medium | 12-month support, forced upgrade, OTA |
| R14 | Cloud choice open | Medium | Medium | decide OQ1 |
| R15 | **Scope breadth** (30+ modules) dilutes quality | High | High | strict foundation gate, one module design review at a time, module-independence tests |
| R16 | AI: prompt injection, data leakage, provider residency, cost | Medium | High | tool gateway (ADR-033), confirmation tokens, minimisation, quotas, red-team tests |
| R17 | Workflow engine complexity (conditions, parallel, send back, delegation) | Medium | Medium | bounded feature set, versioned definitions, extensive tests |
| R18 | Biometric vendor diversity & on-prem connector support | High | Medium | adapter model, prioritised vendor list, connector telemetry |
| R19 | Email deliverability (tenant SMTP/domains), abuse of compose/bulk email | Medium | Medium | domain verification, quotas, suppression lists, audit |
| R20 | GST/e-invoicing rules for platform and client billing | Medium | Medium | effective-dated tax config, IRP integration point, CA review |
| R21 | Dashboard/report performance on large tenants | Medium | Medium | pre-aggregation jobs, read replicas per cluster later |
| R22 | External calendar sync complexity | Medium | Low | one-way publish first |

## 21.3 Decisions still requiring approval

| # | Decision | Recommendation (default) |
|---|---|---|
| OQ1 | Cloud & regions | AWS ap-south-1, DR ap-south-2 (ADR-028) |
| OQ2 | Statutory scope | India first, rule-pack design for others |
| OQ3 | Scale targets | 1,000 tenants / 3 years; largest 50k employees |
| OQ4 | Customer SSO at launch | design now, build after foundation |
| OQ5 | One login across tenants | no; group companies = multiple companies in one tenant |
| OQ6 | ECS Fargate vs EKS | ECS Fargate (ADR-039) |
| OQ7 | UI component library | MUI core |
| OQ8 | Commercial libraries allowed | OSS first; evaluate QuestPDF/Hangfire Pro case by case |
| OQ9 | Self-hosted identity vs managed IdP | OpenIddict (ADR-007) |
| OQ10 | GitHub org / repo name | — |
| D1 | Physical DB naming (ADR-037) | lowercase `hrms_tenant_1001` |
| D2 | Packaging of Documents / Letters / Exit (ADR-030) | Documents + Exit in base plan; Letters base or add-on (commercial) |
| D3 | Is Tax sold separately from Payroll? | included with Payroll, toggleable sub-module |
| D4 | Comp-off ownership | Attendance earns, Leave holds balance |
| D5 | Expense "leave reimbursement" meaning | LTA/leave-travel claims; encashment stays in Leave/Payroll/FNF |
| D6 | AI LLM provider & hosting | in-region managed model (Azure OpenAI India / AWS Bedrock) with no-training, retention-off |
| D7 | Payment gateway for SaaS billing | Razorpay (India) — confirm |
| D8 | SaaS billing basis | max active employees in period |
| D9 | DR tiers & RPO/RTO per plan | as §24.3 |
| D10 | Default retention periods | legal review |
| D11 | Aadhaar storage | masked/reference only |
| D12 | PDF engine | headless Chromium in Worker (OSS) vs QuestPDF (licence threshold) |
| D13 | E-sign provider | defer |
| D14 | Custom report builder | out of v1 |
| D15 | Alumni (ex-employee) portal access | read-only own documents/payslips for configurable period |
| D16 | Visitor watchlist | feature off by default; enable per tenant after legal check |
