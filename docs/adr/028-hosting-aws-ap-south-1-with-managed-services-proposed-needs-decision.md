# ADR-028: Hosting: AWS (ap-south-1) with managed services — PROPOSED, needs decision

- **Status:** Proposed
- **Area:** Infrastructure

## Context
S3-compatible storage required; India data residency likely.

## Decision
AWS: RDS/Aurora PostgreSQL, ElastiCache (Redis/Valkey), S3, KMS, Secrets Manager, ECS Fargate (or EKS), CloudFront + WAF. Terraform IaC.

## Why
Native S3; mature managed PostgreSQL; Mumbai + Hyderabad regions for residency and DR.

## Alternatives considered
- Azure (Blob is not S3-compatible natively)
- GCP
- Self-managed/colocation

## Advantages
- Managed HA/backup/PITR
- Rich security services

## Disadvantages
- Vendor coupling (kept at infra layer)

## Scalability impact
Elastic.

## Security impact
KMS, IAM roles for tasks, GuardDuty.

## Operational impact
Requires cloud ops skills; cost monitoring.

## Amendment — final baseline review
Still pending approval (OQ1). Deployment topology documented in architecture §24.4.
