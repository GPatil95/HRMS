# ADR-039: Container platform: ECS Fargate (proposed) — pending decision

- **Status:** Proposed (final baseline review)
- **Area:** Deployment

## Context
Need managed container orchestration for API, Worker, Migrator, PgBouncer, scanner.

## Decision
ECS Fargate with ALB, autoscaling; EKS remains an option if the team has Kubernetes expertise or needs its ecosystem.

## Why
Lower operational burden for a new team.

## Alternatives considered
- EKS (Kubernetes)
- VMs

## Advantages
- No cluster management
- Simple IAM per task

## Disadvantages
- Less portable than Kubernetes; fewer ecosystem tools

## Scalability impact
Autoscaling per service.

## Security impact
Task IAM roles, no node access.

## Operational impact
Terraform modules; migration to EKS possible since workloads are containers.
