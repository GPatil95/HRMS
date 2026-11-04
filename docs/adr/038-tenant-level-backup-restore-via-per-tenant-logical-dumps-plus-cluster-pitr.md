# ADR-038: Tenant-level backup/restore via per-tenant logical dumps plus cluster PITR; DR tiers

- **Status:** Proposed (final baseline review)
- **Area:** Backup / DR

## Context
Restoring one tenant must not require restoring all; RPO/RTO commitments vary by plan.

## Decision
Cluster PITR + cross-region copies, nightly per-tenant logical dumps to an isolated Object-Lock backup account, S3 versioning + CRR for files, automated restore tests; DR tiers (snapshot-based standard, replica-based enterprise).

## Why
Per-tenant restore without collateral impact; evidence of recoverability.

## Alternatives considered
- Cluster-level backups only
- Hot-hot multi-region from day one

## Advantages
- Granular restore
- Cost-tiered DR

## Disadvantages
- Backup storage cost; dump duration for very large tenants (use dedicated cluster + PITR clone)

## Scalability impact
Dumps parallelised per cluster.

## Security impact
Encrypted, isolated, immutable backups.

## Operational impact
Restore runbooks and drills.
