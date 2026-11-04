# ADR-001: Modular monolith as the initial architecture

- **Status:** Proposed
- **Area:** Architecture

## Context
Greenfield product, one team, ~30 future modules, strong consistency needs (payroll), requirement to extract modules later.

## Decision
One deployable codebase (API, Worker, Migrator hosts) composed of modules with explicit Contracts, own schemas and own DbContexts. No microservices at start.

## Why
Gives module boundaries and independent evolution without distributed-systems cost (network failures, eventual consistency everywhere, distributed tracing/deploy complexity) before we know where scale pressure is.

## Alternatives considered
- Microservices from day one
- Classic layered monolith without module boundaries

## Advantages
- Simple deploy/debug, in-process calls and local transactions
- Refactoring across modules is cheap while domain is still being learned
- Boundaries enforced by compiler + arch tests keep extraction possible

## Disadvantages
- One deploy unit: a bad release affects all modules
- Requires discipline to keep boundaries
- Single runtime/language for all modules

## Scalability impact
API and Worker scale horizontally; heavy workloads (payroll, imports) isolated by Worker queues. Extraction of hot modules later via Contracts.

## Security impact
Smaller attack surface (fewer service-to-service credentials); one authorization pipeline to get right.

## Operational impact
One pipeline, one set of dashboards; fewer moving parts for a new team.
