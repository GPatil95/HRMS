# ADR-031: Finalized financial results are immutable; corrections via revisions

- **Status:** Proposed (final baseline review)
- **Area:** Payroll / Compliance

## Context
Payroll, FNF, tax computations and invoices must be reproducible and auditable.

## Decision
Payroll runs, FNF settlements, tax statements and invoices store input snapshots, rule/engine versions and are locked on finalize (domain rule + DB trigger). Corrections create linked revisions (reopen-revision, FNF revision, credit/debit notes, arrears) — never in-place edits.

## Why
Meets reproducibility and audit requirements; prevents silent changes.

## Alternatives considered
- Allow edits with audit trail only

## Advantages
- Every historical figure explainable
- Strong audit position

## Disadvantages
- More storage; revision UX needed

## Scalability impact
Snapshot storage grows linearly; partition by period.

## Security impact
Tamper resistance for financial data.

## Operational impact
Reopen requires approval + step-up MFA.
