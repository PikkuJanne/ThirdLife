# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-11-24  
**Bundle baseline:** 0.5.0  
**Runtime authority:** active Codex machine only; remote CI is supplementary  
**Current milestone:** M1 — Audit-only vertical slice  
**Current task:** TL-0116 — Validate the M1 audit-only gate; review

## Continuity

PR #14 integrated TL-0113 and the
[owner-authorized amendment](docs/amendments/2026-11-24-minimal-human-testing.md)
into `main` at `e88718118e7d6e73e3291879e3053af93fea5afb`. PR #15 integrated TL-0114
at `ac35205d0295452d806fc494fffc88d39b974738`. PR #16 integrated TL-0115 at
`7c382bf784c5128ae96a8fcf07724fea35e9bc40`. TL-0116 started from that verified base
on `codex/tl-0116-m1-audit-gate` in draft PR #17. Corrected candidate source is
`edb9b03ba813f5de67238d84dd424ad6da572257`. SSH origin, prior branches and the
unrelated untracked Word reference are preserved.

## Outcome and evidence

TL-0113's applicable [A11Y-001–007 observations](artifacts/evidence/TL-0113/human-walkthrough.md)
passed, and Windows settings were restored. The TL-0114 baseline passed 25 Core,
42 Inventory and 34 UI tests. The audit workspace connects durable job intake,
sanitization, assessment and guided manual records. The isolated active-machine Full run
passed 278 governance and 1,315 .NET tests in 284.726s. The standard-user apphost smoke
passed in 1.827s. [TL-0114 evidence](artifacts/evidence/TL-0114/report.md)

TL-0115 generates a deterministic, restricted workshop report from the saved job and
opens the verified copy in the audit workspace. The isolated active-machine Full run
passed 279 governance and 1,337 .NET tests in 281.624s. Standard-user launch passed.
[TL-0115 evidence](artifacts/evidence/TL-0115/report.md)

TL-0116 first-candidate clean-worktree Full passed in 283.547s, including 1,337
.NET tests. The ordinary apphost launched and responded under a standard-user token.
The isolated named WPF rehearsal passed create, synthetic intake, read-only
assessment, one manual unavailable sample, durable close/reopen and restricted
report generation/preview. The first operator attempt stopped before assessment.
The open job had no saved external result. The operator reported unavailable
Narrator and uncontrollable touchpad scrolling. The isolated fixture was retired
and the original Windows settings restored. The corrected UI guides the operator to
intake and uses proportional wheel movement. Corrected clean-worktree Full passed
279 governance and 1,342 .NET tests in 302.263s. Standard-user launch and the exact
prepared fixture rehearsal passed. [Gate record](artifacts/gates/M1-audit-only.md)

## Next action

Use the corrected fixture and the [prepared operator check](artifacts/evidence/TL-0116/human-check.md)
for the repaired intake, Narrator, touchpad and remaining audit steps. Record actual
observations, explicit scope acknowledgement and separate owner decision. Restore the
original settings and verify cleanup. Complete and merge PR #17 once the gate is
satisfied. The assignment ends at M1.
