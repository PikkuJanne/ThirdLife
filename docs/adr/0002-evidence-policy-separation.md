# ADR 0002 — Evidence, policy, and decision separation

## Status and authority

**Status:** Accepted for `TL-0009`.

**Task:** [`TL-0009`](../../TASKS.yaml)

This ADR records existing binding decisions as planned architecture constraints. It does not amend [`DECISIONS.md`](../../DECISIONS.md) or [`PROJECT_BOUNDARY.md`](../../PROJECT_BOUNDARY.md), and it is not evidence that the planned behavior is implemented or verified.

## Decision IDs

- [D-015](../../DECISIONS.md) — Evidence semantics
- [D-016](../../DECISIONS.md) — No aggregate health score
- [D-017](../../DECISIONS.md) — Disposition vocabulary
- [D-018](../../DECISIONS.md) — Facts, policy, and decisions are separate
- [D-019](../../DECISIONS.md) — Explicit exceptions
- [D-023](../../DECISIONS.md) — Profiles are non-executable data
- [D-032](../../DECISIONS.md) — Action journal and completion
- [D-033](../../DECISIONS.md) — Separate verification and cold boot

## Context

Windows providers, human tests, and imported evidence can be incomplete, unavailable, stale, or contradictory. Organizations may apply different versioned policies to the same facts, and a policy may change after a job is completed. The product must explain a disposition without rewriting the observations that produced an earlier result or turning an unavailable value into a pass.

Action history introduces a related distinction: an attempted mutation, a backend-reported application, and an independently verified outcome are different facts.

## Decision

Evidence, policy, evaluation results, and action verification use separate domain contracts and histories:

- An evidence record is immutable and attributable. It records its classification (`observed`, `inferred`, `not available`, or `human confirmed`), provider or operator, timestamp, provenance, availability, and bounded normalized value. Missing or rejected evidence remains unknown.
- Organization policy is immutable versioned input for a job evaluation. The exact policy, profile, and relevant catalogue snapshots used by a historical job remain identifiable.
- Policy and profiles are declarative, validated, and non-executable. They cannot contain scripts, shell commands, arbitrary executable paths, registry paths, URLs, or unknown action types.
- A policy evaluation creates a new reproducible decision record that references the exact evidence set and policy version, records every controlling rule and explanation, and produces one of the five governed dispositions. It does not mutate evidence or collapse the result into a numeric health score.
- Re-evaluation after new evidence or a policy change appends a distinct result. It never rewrites the prior evidence, policy snapshot, disposition, confirmation, or exception.
- A governed exception remains attributable to its operator, reason, authorization, policy rule, and time, and remains visible in reports. It cannot override a prohibited rule.
- The action journal records planned, approved, started, applied, verified, failed, skipped, rolled back, and requires-review states. `Applied` is not completion. Only a separate fresh verification record can establish `verified`.

Provider adapters normalize observations; policy services evaluate them; the UI presents their relationship. Provider and UI code do not embed or silently override organization policy.

## Alternatives considered

- **One mutable current-state record:** rejected because it would erase provenance and make historical decisions impossible to reproduce.
- **Policy inside each provider:** rejected because a provider observation would become organization-specific and could silently report a pass.
- **UI-owned decision logic:** rejected because presentation state is not a durable or independently testable policy contract.
- **A universal numeric device score:** rejected because one critical blocker could be hidden by unrelated positive values.
- **Treating backend success as completion:** rejected because installers and updates may report success before restart-sensitive postconditions exist.

## Consequences

- Historical results can be replayed and explained against the exact evidence and policy that produced them.
- Storage must retain versioned snapshots and append-oriented histories rather than only the latest value.
- Domain types and tests must preserve unavailable, contradictory, exception, applied, and verification states explicitly.
- Re-evaluation may produce several valid historical decision records for one job; consumers must select by recorded evaluation identity and time rather than overwrite.
- Reports and UI projections must distinguish facts, policy rules, decisions, actions, verification, uncertainty, and limitations in accessible language.

## References

- [Product contract](../product-contract.md)
- [Governed glossary](../glossary.md)
- [Security data flow](../security/data-flow.md)
- [Candidate pilot fixture contract](../../fixtures/README.md)
