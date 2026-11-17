# ADR-026: Application-level field encryption for highly sensitive personal data

- **Status:** Proposed
- **Area:** Security / Compliance

## Context
Bank details, PAN, identity documents and health data need protection beyond disk encryption (DBA access, backups, support).

## Decision
Envelope encryption with per-tenant data keys wrapped by KMS for designated fields, blind-index HMAC for equality search, masking by default in APIs; full Aadhaar numbers not stored.

## Why
Limits exposure from DB dumps, backups and over-privileged queries.

## Alternatives considered
- Disk/TDE encryption only
- pgcrypto in database

## Advantages
- Defence in depth
- Crypto-shredding possible per tenant

## Disadvantages
- No range/LIKE search on encrypted fields
- Key management complexity

## Scalability impact
Slight CPU overhead; DEKs cached in memory.

## Security impact
Major reduction of breach impact.

## Operational impact
Key rotation runbook.
