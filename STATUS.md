# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-11-25  
**Bundle baseline:** 0.6.0  
**Runtime authority:** active Codex machine only. Remote CI is supplementary.  
**Current milestone:** M2 — Explainable disposition engine  
**Current task:** TL-0208 — Done

## Continuity

PRs #18 through #24 integrated all seven M2 engineering tasks into
`main` at `bf7076ccee9e5c4c438f4706a68a1021e98316b7`. PR #25
contains the completed TL-0208 gate and scope-bound approval.

The original workspace still contains the documented untracked Word reference.

## Outcome and evidence

Clean-worktree Full passed 286 governance and 1,572 .NET tests. The
unelevated apphost passed startup and UI Automation. Six rendered reports
cover all five dispositions and one governed exception. Live Windows
support remains unavailable where the bundled lifecycle data has expired.
[M2 gate](artifacts/gates/M2-disposition.md)

## Next action

Janne Vuorela approved the proposed wording and exception governance on
2026-11-25 for the internal M2 candidate. [Decision record](artifacts/gates/M2-disposition.md#policy-owner-decision)

The next dependency-ready task is TL-0301, Implement the application catalog
schema, from updated `main` in M3.
