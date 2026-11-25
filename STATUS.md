# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-11-25  
**Bundle baseline:** 0.7.0  
**Runtime authority:** active Codex machine only. Remote CI is supplementary.  
**Current milestone:** M3 — Safe planning and privilege boundary  
**Current task:** TL-0315 — Done

## Continuity

The owner-authorized [Windows 11–only amendment](docs/amendments/2026-11-25-windows-11-only.md)
is based on `main` after broker protocol PR #35 at `8f1441b`. The verified candidate
`b8314ef` targets Windows 11 client on native x64 and retains historical jobs,
reports, policy and journal readback.

Targeted passed 1,812 tests. Clean Full passed 290 governance and 1,941 .NET tests.
Three direct active-machine checks passed, including the apphost and the isolated
assessment and report journey. [TL-0315 evidence](artifacts/evidence/TL-0315/report.md)

## Next action

Continue on retained broker draft PR #36 after its platform reconciliation. TL-0311
is blocked on trusted elevated startup, with real privilege, recovery and cross-account
evidence pending. TL-0312 follows completed TL-0311. The original workspace retains
the documented untracked Word reference.
