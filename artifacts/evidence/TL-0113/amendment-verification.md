# TL-0113 amendment verification and shell preparation

**Date:** 24 November 2026, Europe/Berlin  
**Scope:** [Minimal human testing amendment](../../../docs/amendments/2026-11-24-minimal-human-testing.md) on [draft PR #14](https://github.com/PikkuJanne/ThirdLife/pull/14)  
**State:** Amendment verified. Human shell observations are pending.

Fetched `main` at `d4ec836719746c659f12ca851f9890d62c76bfa7` and PR #14 at
`964863ebfc458a63ac559479a40450b652d2d787`. The branch was synchronized with its
upstream and four commits ahead of `main`. All nine companion bundle hashes matched.
When default SSH stalled, the process-scoped HTTPS bridge completed the fetch with
origin intact. The existing untracked Word reference remains in place.

## Verification

Active machine `REF-CODEX-001` revision `2026-08-29.1`, Windows 11 Pro x64 build 26300,
SDK 10.0.400, Python 3.14.7 and pinned PyYAML. Final runtime and governance used an
isolated same-machine worktree with the amendment overlaid on the fetched PR head.
Tested bundle manifest SHA-256
`b4a1edc8866a04652a7610c6a9163c443aa44196e5b30f22a51039af111968cc`.
Final evidence and wording edits receive direct manifest and repository validation.

| Check | Result |
| --- | --- |
| Baseline bundle validator and existing verifier regressions | Valid DAG and 8 verifier tests passed in 11.832s. The pinned environment uses the validator's custom structural check. |
| Focused metadata and testing-contract classes | 32 passed in 6.861s. Direct validation exposed a stale interface draft version marker, which was synchronized. |
| Focused verifier regressions | 11 passed in 12.713s. Four changed-risk cases passed under Windows PowerShell 5.1 in 2.491s. Command spies cover Windows path matching, exact filter text, distinct and empty selections, and failed discovery propagation. |
| Actual .NET discovery canary | The Core assembly with a deliberately nonexistent exact filter returned exit 1 under `RunConfiguration.TreatNoTestsAsError=true` in 3.435s. |
| Targeted including Quick | `eng/verify.ps1 -Tier Targeted -Restore -TestProject tests/ThirdLife.UI.Tests/ThirdLife.UI.Tests.csproj` passed in 101.017s on the active host. Locked restore, formatting and warnings-as-errors build passed. Core 83, Inventory 901 and UI 43 passed with zero failures/skips. UI ran once. |
| Complete Python governance | `python -m unittest discover -s tools/tests -p "test_*.py"` passed all 278 tests in 321.827s. |
| .NET Full and Extended | Neither triggered for this scope. TL-0116 retains its Full gate. |

The first complete governance run encountered six setup errors in two supply-chain
tests when their fixture copied an ignored local PR draft. A focused reproduction
confirmed Git's ignored-path rejection. An isolated worktree excluded the local files.
A shorter path and process-scoped Git long-path support addressed the first isolated
checkout's path-length error. Both affected tests passed in 30.635s before the final
governance run.

Sandbox preflight passed but the disposable guest did not produce a bounded result.
A direct-host UI model canary checked whether current Windows could load the original
Release test assembly and passed in 4.129s. The final active-host Targeted run used the
isolated worktree and the documented process-scoped Windows PowerShell invocation.
The owned Sandbox session was ended after the host pass and staging cleanup verified.
Current host security configuration was preserved.

Verified runner SHA-256
`ff18de43e97506690749591e4c7fc8f9acd4c0e6495d361806f7bf8fc5e65e5e`.
The [original task report](report.md) and [runs](runs.json) retain their tested commits
and environment limits. Application source, .NET tests, build configuration, and the
prepared apphost and UI assembly hashes match the original verified shell. The
[short shell card](human-walkthrough.md) binds that build, records rehearsal and leaves
each human case pending.
