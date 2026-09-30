# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-09-30  
**Bundle baseline:** 0.4.0  
**Current milestone:** M1 — Audit-only vertical slice  
**Current task:** `TL-0108` — storage inventory and reliability evidence; done, integration pending

## Continuity

- TL-0107 PR #8 merged into main at `c960e03bc17bc90287f2ab8d8b4f865bce4e0298` on 2026-09-30 at 14:28:41 UTC; previous head is an ancestor with no unique commits.
- `codex/tl-0108-storage-reliability` starts from that updated main. Integrate its normal PR before the next task.
- Prior branches remain retained. No branch deletion, PR closure, reset or history rewrite.
- The unrelated untracked Word roadmap reference remains untouched and excluded.
- Git uses the documented process-scoped HTTPS bridge; configured SSH origin is unchanged.
- Runtime authority remains the active Codex machine; remote CI is supplementary.

## Completed outcome

Storage inventory now preserves explicit disk capacity/media/health and reliability evidence
when exposed, with independent quota-aware system-volume capacity/free fields. Exact WMI
associations establish counter ownership. Ambiguous, malformed, missing and denied values
remain explicit; no disk-to-volume inference, diagnosis, repair, write test or health score.
Output stays within 64 observations, and cancellation/retry retains one native worker per instance.

## Verification and limits

- Standalone final Quick passed in 35.117 s. Targeted including Quick passed in 43.281 s: 79 Core and 329 Inventory tests, validators, format and warnings-as-errors build. Final focused storage/boundary set passed 109 tests.
- Final active smoke passed in 1.881 s; provider read 108.826 ms, 62.500 ms CPU, 77,824 bytes private growth and 47 handles growth.
- Physical-disk WMI was access denied under unelevated permissions; system-volume facts were available. No real disk reliability data or cross-hardware claim.
- Individual native calls may outlive caller timeout; the in-flight guard remains until cleanup. Full/Extended were not triggered.
- Windows PowerShell policy blocked script launch; installed PowerShell 7 succeeded without changing policy. Prior TL-0107 Application Control issues did not recur in the verified runs.
- Evidence: `artifacts/evidence/TL-0108/report.md`, `runs.json`, `active-storage.json`, `inputs.sha256`; publication state is verified at handoff.

## Next action

Merge TL-0108 into main, verify ancestry and update local main. Then the next dependency-ready
task is `TL-0109` — Implement battery report generation and parsing. It has not started.
