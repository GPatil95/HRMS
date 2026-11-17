# ADR-019: UUIDv7 primary keys, business codes as separate unique columns

- **Status:** Proposed
- **Area:** Database

## Context
Records may move between databases (tenant moves, exports, extraction); IDs exposed in APIs.

## Decision
Application-generated UUIDv7 (`Guid.CreateVersion7()`), business identifiers (employee code) as citext unique columns.

## Why
Time-ordered UUIDs avoid random-UUID index fragmentation while remaining globally unique and non-guessable enough to not leak counts.

## Alternatives considered
- bigint identity
- random UUIDv4

## Advantages
- Globally unique across tenant DBs
- Client/offline generation possible (mobile)
- Good B-tree locality

## Disadvantages
- 16 bytes vs 8
- Less human-friendly (codes cover that)

## Scalability impact
Good insert performance at scale.

## Security impact
No sequential ID enumeration (authorization still required).

## Operational impact
None significant.
