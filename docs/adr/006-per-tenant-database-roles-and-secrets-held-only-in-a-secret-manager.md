# ADR-006: Per-tenant database roles and secrets held only in a secret manager

- **Status:** Proposed
- **Area:** Security / Database

## Context
Brief forbids plaintext passwords in source/config; registry must locate databases.

## Decision
Each tenant DB has an app role (DML) and migrator role (DDL) with generated passwords stored in AWS Secrets Manager (or Vault). Registry stores secret references only. Connection strings live only in process memory.

## Why
Credential-level isolation and least privilege; rotation without deploys.

## Alternatives considered
- One shared app role for all tenant DBs
- IAM database authentication only

## Advantages
- A wrong connection string cannot authenticate to another tenant
- Audit tables can be INSERT-only for the app role
- Rotation per tenant

## Disadvantages
- Secret manager cost and latency (mitigated by caching)
- More roles to manage (automated)

## Scalability impact
Thousands of secrets are fine for managed secret stores; cache avoids per-request calls.

## Security impact
Strong least-privilege and blast-radius reduction.

## Operational impact
Rotation runbook and monitoring of secret-fetch failures needed. IAM auth can be layered later.
