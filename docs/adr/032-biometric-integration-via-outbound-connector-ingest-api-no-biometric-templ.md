# ADR-032: Biometric integration via outbound connector/ingest API; no biometric templates stored

- **Status:** Proposed (final baseline review)
- **Area:** Integration / Security

## Context
Many device vendors; devices sit on customer networks; vendors must not access the tenant DB.

## Decision
On-prem connector agent (outbound HTTPS, client credentials per tenant) pushes raw punches to an ingest API; vendor-cloud pull adapters in Worker; raw → processed punch pipeline with dedupe, mapping, retries, reprocessing; device credentials as secret refs; only device user IDs stored, never fingerprint/face templates.

## Why
Keeps the DB unreachable from outside, supports offline sync and many vendors.

## Alternatives considered
- Devices/vendor software writing directly to DB
- Inbound connections from cloud to customer devices

## Advantages
- Secure, vendor-agnostic
- Resilient offline sync

## Disadvantages
- Connector to build, distribute and update

## Scalability impact
Batch ingest scales horizontally.

## Security impact
No inbound firewall holes; minimal biometric data.

## Operational impact
Connector heartbeat monitoring and version management.
