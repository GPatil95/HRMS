# ADR-025: Prefer permissive OSS; avoid recently relicensed libraries

- **Status:** Proposed
- **Area:** Technology / Legal

## Context
Several popular .NET libs moved to commercial licences (MediatR, AutoMapper, MassTransit v9, FluentAssertions v8, Duende IdentityServer, EPPlus).

## Decision
Use Apache/MIT alternatives (plain handlers, Mapperly, ClosedXML, Shouldly, OpenIddict); evaluate commercial options case by case (OQ8).

## Why
Avoid surprise licence costs and lock-in.

## Alternatives considered
- Use the commercial libraries and pay

## Advantages
- Predictable costs

## Disadvantages
- Occasionally slightly more code

## Scalability impact
Neutral.

## Security impact
Fewer dependencies overall.

## Operational impact
Quarterly licence scan in CI.
