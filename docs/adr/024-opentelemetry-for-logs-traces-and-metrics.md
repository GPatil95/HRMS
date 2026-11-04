# ADR-024: OpenTelemetry for logs, traces and metrics

- **Status:** Proposed
- **Area:** Observability

## Context
Need vendor-neutral, correlated telemetry with tenant dimensions.

## Decision
OpenTelemetry SDK in all hosts exporting OTLP to a collector; backend tooling selectable (Grafana stack or managed APM); Sentry for errors.

## Why
Standard, portable, first-class .NET support.

## Alternatives considered
- Vendor agents only
- Logs only

## Advantages
- Correlated traces/logs/metrics
- Swap vendors without code changes

## Disadvantages
- Collector to operate
- Cardinality control needed for tenant_id

## Scalability impact
Sampling and per-tenant top-N metrics.

## Security impact
Redaction before export.

## Operational impact
Dashboards/alerts defined as code.
