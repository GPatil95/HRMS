# ADR-017: Shared S3 bucket per environment with tenant prefixes, presigned URLs after authorization, scan-before-use

- **Status:** Proposed
- **Area:** File storage / Security

## Context
Many document types across modules; must be isolated, validated, scanned, versioned and audited.

## Decision
Common IFileService; metadata in tenant DB; objects under `tenants/{tenantId}/...`; upload via presigned PUT to quarantine; malware scan; download via 60-second presigned GET after module access policy check; per-tenant KMS key optional.

## Why
One hardened path for all files; storage costs low; clients never get storage credentials.

## Alternatives considered
- Bucket per tenant
- Files in PostgreSQL (bytea)
- Proxy all bytes through API

## Advantages
- Scales without API bandwidth bottleneck
- Uniform security and retention

## Disadvantages
- Prefix isolation relies on server-side key generation (tested)
- Presigned URLs are bearer links for their short lifetime

## Scalability impact
S3 scales effectively unlimited; bucket-per-tenant would complicate quotas and IaC.

## Security impact
Scan-before-use, type sniffing, short URLs, download audit.

## Operational impact
Lifecycle rules for retention; scanner capacity monitoring.
