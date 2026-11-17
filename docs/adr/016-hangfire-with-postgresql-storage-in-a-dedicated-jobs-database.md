# ADR-016: Hangfire with PostgreSQL storage in a dedicated jobs database

- **Status:** Proposed
- **Area:** Background jobs

## Context
Need retries, failure tracking, scheduling, dashboard for payroll, imports, emails, reminders across many tenants.

## Decision
Hangfire (LGPL core) + Hangfire.PostgreSql in `hrms_jobs`; tenant-aware job filter; dispatcher recurring jobs; IDs-only arguments; business status in module tables.

## Why
Mature, simple, built-in retries and dashboard; PostgreSQL keeps infrastructure small.

## Alternatives considered
- Quartz.NET (Apache-2.0, strong scheduling, no dashboard)
- Cloud-native queues (SQS + workers)
- Custom BackgroundService queues

## Advantages
- Fast to build; visible failures
- Easy retry/requeue for operators

## Disadvantages
- Job args stored in plain text (IDs only rule)
- Pro features (batches) are commercial
- Storage can become a hotspot at very high volume

## Scalability impact
Horizontal Worker scale; queues isolate payroll/bulk; replaceable behind IJobScheduler.

## Security impact
Dashboard only in platform realm; no PII in args.

## Operational impact
Separate DB backup policy; alerts on failed jobs.
