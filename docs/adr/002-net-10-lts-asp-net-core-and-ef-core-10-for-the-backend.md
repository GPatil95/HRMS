# ADR-002: .NET 10 LTS, ASP.NET Core and EF Core 10 for the backend

- **Status:** Proposed
- **Area:** Technology

## Context
Brief mandates ASP.NET Core/C#/EF Core; need to pick the version.

## Decision
.NET 10 (LTS, supported until Nov 2028), C# 14, EF Core 10 with Npgsql provider; upgrade to next LTS (.NET 12) within 6 months of release.

## Why
LTS gives 3 years of support; latest EF/Identity features (passkeys in Identity, built-in OpenAPI, HybridCache, improved JSON/complex types).

## Alternatives considered
- .NET 8 LTS (ends Nov 2026 — too short)
- .NET 9 STS

## Advantages
- Longest support window
- Modern performance and features

## Disadvantages
- Some third-party packages may lag a new major version briefly

## Scalability impact
High throughput Kestrel; container-friendly; chiseled images.

## Security impact
Supported runtime receives security patches through 2028.

## Operational impact
Monthly patch updates via Renovate; one runtime across hosts.
