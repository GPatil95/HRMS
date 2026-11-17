# ADR-022: Monorepo with pnpm workspace front-ends and generated API client

- **Status:** Proposed
- **Area:** Repository / Frontend

## Context
Backend, web, admin and mobile share contracts and change together.

## Decision
Single repo; `frontend/` pnpm workspace with apps (web, admin, mobile) and packages (api-client, ui, auth, shell-core, i18n); API client generated from OpenAPI.

## Why
Atomic changes across API and clients; no hand-written API types.

## Alternatives considered
- Polyrepo
- Nx/Turborepo-heavy setup from day one

## Advantages
- Single source of truth for contracts
- Shared tooling and CI

## Disadvantages
- Larger repo; CI needs path filters

## Scalability impact
Turborepo can be added if builds slow down.

## Security impact
One place for dependency scanning.

## Operational impact
One CI configuration.
