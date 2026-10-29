# ADR-027: In-house approval workflow engine (not a general BPM engine)

- **Status:** Proposed
- **Area:** Workflow

## Context
Dynamic approval workflows across leave, expense, loans, recruitment, etc.

## Decision
Purpose-built engine: versioned JSON definitions, approver resolvers, conditions, parallel/sequential steps, SLA escalation, delegation; modules register document types.

## Why
Approvals are a well-bounded problem; general engines add heavy runtime and learning cost.

## Alternatives considered
- Elsa Workflows (MIT)
- Camunda/Temporal (external runtime)

## Advantages
- Fits HR approval semantics and tenant config UI
- Simple to operate

## Disadvantages
- We build and maintain it
- Not suitable for arbitrary long-running orchestrations

## Scalability impact
Tasks indexed per assignee; escalations via dispatcher jobs.

## Security impact
Approver resolution respects permissions and scopes; all actions audited.

## Operational impact
Definition versioning keeps in-flight instances stable.

## Amendment — final baseline review
Actions finalised: Approve, Reject, Send Back, Delegate, Escalate, Cancel (+ Comment, Withdraw). Conditions over amount, branch, department, designation, employee, manager, role, grade and custom fields using a sandboxed expression evaluator shared with payroll formulas. Every action audited (architecture §19.1).
