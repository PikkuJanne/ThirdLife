# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-11-24  
**Bundle baseline:** 0.6.0  
**Runtime authority:** active Codex machine only. Remote CI is supplementary.  
**Current milestone:** M2 — Explainable disposition engine  
**Current task:** TL-0204 — Done

## Continuity

PRs #18 through #20 integrated the schema, evaluation and dispositions.
PR #21 is the integration record for human confirmations and exceptions.
Use updated `main` for the next task. [M1 gate](artifacts/gates/M1-audit-only.md)

The original workspace still contains the documented untracked Word reference.

## Outcome and evidence

Saved guided evidence projects into the mapped policy facts with attribution.
An exception preserves the original failure and is scoped to the exact
policy, job, evidence and timestamp. Targeted passed 1,233 tests on the active
machine. The original candidate has no exception authorizations.
[TL-0204 evidence](artifacts/evidence/TL-0204/report.md)

## Next action

TL-0205 is the next dependency-ready task. Persist policy snapshots and
decision history from updated `main`.
