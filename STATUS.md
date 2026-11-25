# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-11-25  
**Bundle baseline:** 0.6.0  
**Runtime authority:** active Codex machine only. Remote CI is supplementary.  
**Current milestone:** M3 — Safe planning and privilege boundary  
**Current task:** TL-0309 — Done

## Continuity

PR #33 integrated the [durable action journal](artifacts/evidence/TL-0308/report.md)
into `main` at `ef2802d`. The `codex/tl-0309-reboot-checkpoints`
branch contains the completed TL-0309 work and evidence.

The original workspace still contains the documented untracked Word reference.

## Outcome and evidence

The additive v6 store authenticates restart checkpoints and records one
explicit resume or abandon decision. Reopening the job shows the saved
outcome without dispatching the action. Clean Full passed 287 governance
and 1,845 .NET tests. Both named restart recovery scenarios passed.
[TL-0309 evidence](artifacts/evidence/TL-0309/report.md)

## Next action

Start TL-0310 after verifying TL-0309 integration on updated `main`.
