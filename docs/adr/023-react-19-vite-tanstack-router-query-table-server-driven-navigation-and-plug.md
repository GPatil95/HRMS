# ADR-023: React 19 + Vite + TanStack (Router/Query/Table), server-driven navigation and plug-in module registry

- **Status:** Proposed
- **Area:** Frontend

## Context
Many modules plugged into one shell; permission-based menus; data-heavy screens.

## Decision
Vite SPA; TanStack Router/Query/Table; react-hook-form + zod for form shape; navigation from `/me/navigation`; modules register routes/widgets lazily.

## Why
Type-safe, fast, widely adopted; navigation not hard-coded so modules plug in.

## Alternatives considered
- Next.js (SSR not needed for an authenticated app; adds a Node server)
- Angular

## Advantages
- No Node backend needed (static hosting)
- Strong typing end-to-end

## Disadvantages
- Several libraries to compose vs an all-in-one framework

## Scalability impact
Static assets on CDN.

## Security impact
No server-side rendering surface; strict CSP feasible.

## Operational impact
Simple static deploy.
