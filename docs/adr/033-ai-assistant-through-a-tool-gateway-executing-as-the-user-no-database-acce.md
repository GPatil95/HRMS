# ADR-033: AI assistant through a tool gateway executing as the user; no database access

- **Status:** Proposed (final baseline review)
- **Area:** AI / Security

## Context
AI must not have unrestricted data access; must respect entitlement, permission, scope; actions auditable; sensitive writes confirmed.

## Decision
AI orchestrator calls only registered tools mapped to existing application services, executed on behalf of the user with full entitlement/permission/scope checks; tool exposure filtered per request; categories read/suggest/execute; sensitive writes require server-issued confirmation tokens and native UI confirmation; some operations never exposed; full audit; regional no-training LLM provider behind `ILlmClient`.

## Why
Re-uses the single authorization model; prompt injection cannot escalate privileges.

## Alternatives considered
- Text-to-SQL over tenant DB
- Separate AI read replica with broad access
- Vector index of all tenant data

## Advantages
- Provably no broader access than the user
- Consistent business rules

## Disadvantages
- Tools must be built per capability
- Answers limited to available tools

## Scalability impact
LLM calls async/streamed; quotas.

## Security impact
Strong containment; data minimisation to provider.

## Operational impact
Provider selection and cost monitoring (pending decision).
