# ADR-015: Transactional outbox for domain/integration events, in-process dispatch first

- **Status:** Proposed
- **Area:** Architecture / Reliability

## Context
Modules react to each other's changes; notifications/emails must not be lost or sent for rolled-back transactions.

## Decision
Events saved to `integration.outbox_messages` in the same transaction; Worker dispatches to in-process handlers with inbox-based idempotency. Broker (e.g. RabbitMQ/SQS) only when a module is extracted.

## Why
Reliable at-least-once delivery without introducing a broker dependency now.

## Alternatives considered
- Direct in-process event publishing after commit
- Message broker from day one (MassTransit v9+ is commercial)

## Advantages
- No dual-write problem
- Simple operations

## Disadvantages
- Polling latency (seconds)
- Outbox tables need cleanup

## Scalability impact
Per-tenant outbox polling via dispatcher; partition work across Worker instances.

## Security impact
Events contain IDs, not sensitive payloads where possible.

## Operational impact
Monitoring of outbox lag per tenant.
