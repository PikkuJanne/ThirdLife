# Single guest and project-wide continuation correction

**Date:** 2026-11-27  
**Prepared patch base:** `b893288340fd274e827a86ce3fe8f20cf498393f`  
**Owner direction:** one existing guest throughout the complete project timeline;
unavailable test conditions must not halt independent development. Recorded in D-076.

## Findings and changes

The TL-0504 record distinguishes offered software with unsupported handlers from an
eligible supported update. PR 75 subsequently integrated the v2 broker, per-phase grant, policy and recovery/resource checks, durable receipts and postcondition verification. The current guest observation leaves native installation unverified.

The product contract, agent/start instructions, workflow, testing, boundaries, security,
resource guidance, release interface, roadmap and current status now use only the existing
`ThirdLife-W11` guest through development, pilot, release and maintenance. Ordinary supported
servicing, repair and authorized checkpoint refresh remain permitted on that same guest.
Historical Sandbox evidence keeps its original provenance; its launch instructions are
superseded prospectively. A clean source checkout does not require a fresh Windows install.

The existing task schema and selector are retained. All task IDs and previous evidence entries are retained. The correction initially reopened TL-0504 for saved rejection-reason coverage. The subsequent engineering evidence and native limitation are recorded in TASKS.yaml. The redundant `blocked_reason: null` on completed TL-0010 was
removed to conform to its existing optional string schema.

| Task | Engineering correction | Retained execution/exposure obligation |
|---|---|---|
| TL-0504 | Use the implementation merged in PR 75 and verify the persisted reason for each excluded offer. | Exact eligible update, qualified route, action approval, Windows consent and native postcondition evidence remain required for actual servicing. |
| TL-0505 | Uses integrated update contracts and the completed scheduler/preflight/recovery components instead of waiting for all TL-0504 evidence. | Actual update dispatch retains TL-0504 prerequisites. |
| TL-0506 | Read-only security providers no longer wait for maintenance completion. | Later post-maintenance composition remains TL-0507 work. |
| TL-0510 / TL-0605 | Candidate matrix and guide engineering no longer wait for TL-0508 adoption. | Adoption remains recorded for affected exposure. |
| TL-0511 | Gate preparation continues with explicit limits. | Completed update implementation and actual native update capability evidence are separate prerequisites; task completion cannot substitute for live evidence. |
| TL-0611 | Candidate preparation remains independent. | Direct TL-0508 prerequisite preserves adopted pilot inputs; release prerequisites are not assumed to propagate transitively. |
| TL-0509 / TL-0610 / TL-0704 / TL-0707 | Cold-start, package lifecycle and release scenarios use sequential states of the same guest. | Real outcomes, data preservation, recovery and scoped release claims remain required. |

The host runner now omits checkpoint lookup only for `Inspect` and the read-only
`WindowsUpdateAssessment`. All other scenarios retain the existing exact-one-checkpoint
requirement. VM identity checks, credentials, exclusive lease, limited-token update
assessment, dispatch rules and owned-resource cleanup are unchanged. The patch creates
or restores no guest or checkpoint and performs no Windows Update mutation.

## Verification and limits

The supplied patch was checked in a Linux preparation environment with Python 3.12.14, PyYAML 6.0.3
and jsonschema 4.26.0. These are supplementary engineering checks, not the authoritative
Windows runtime or release evidence. No Windows, PowerShell or Hyper-V execution occurred.

| Check | Result |
|---|---|
| `python3 tools/validate_bundle.py` | Passed: 97 tasks, 8 milestones, 76 decisions; valid implementation graph. |
| YAML / existing JSON schema / acyclicity / prerequisite and exposure validators | Passed. All original task IDs, statuses and historical evidence preserved. |
| Focused continuation and checkpoint tests | Four passed; one mocked PowerShell execution test skipped because PowerShell is unavailable here. |
| Complete Python governance regression | 309 tests ran; 20 errors and 49 skips. All 20 errors are the same pre-existing missing-`powershell.exe` errors observed in the untouched base (304 tests, 20 errors, 48 skips). No new error or failure appeared. This is not a full-suite pass. |
| `python3 tools/validate_repository.py` | Same 45 Windows-backslash ProjectReference errors as the untouched base on Linux; no other errors. No validator checks were disabled. |
| Manifest | Regenerated all 99 required entries using the existing coverage rules. |
| `git diff --check` | Passed. |

The new functional selector tests verify that an unavailable update does not block TL-0505
or TL-0506, pending policy adoption does not block candidate matrix/guide engineering,
and completed updater engineering cannot clear native capability evidence. The focused PowerShell test executes the production guard against mocked snapshot results.

## Reconciliation on the active Windows host

The supplied patch digest was verified against the bundle. Its base was
`b893288340fd274e827a86ce3fe8f20cf498393f`. The worktree started at remote main
`967ae8dad190441f7877f72defa10950e69870a1`, which includes PR 75.
The task record preserves each of that PR's evidence entries. The original
checkout and its untracked work were preserved. All 97 task IDs and the eight
milestones were compared. For unfinished M3 and M5–M7 work, dispatch, adoption,
pilot and stable exposure remain scoped to the applicable task. M0–M2 and M4
retain their completed evidence in the original environment.

On the Windows host, the configured Python 3.14.7 environment passed the five
requested continuation and Hyper-V selection tests in 0.499 s, including the
previously skipped mocked PowerShell check. `tools/validate_bundle.py` and
`tools/validate_repository.py` passed with the pinned PyYAML environment. The
bundle used its built-in structural validation. The actual `Inspect` runner admitted
the running Generation 2 guest on Windows build 26300 with the expected account.
It performed no scan, checkpoint operation or servicing.

Both historical Sandbox launchers now reject a run before staging. Their
preflight and historical result-validation paths retain their existing semantics.
The focused source check covers both launchers' guards and preflight returns.

The publication candidate `f2b4bd82f39b3d10e9a32c99505c9c904a184697` passed
all 310 Windows governance tests in 324.230 s using `python -m unittest discover
-s tools/tests -p "test_*.py"`. The five requested tests passed again in 0.658 s.
An earlier discovery run loaded the pre-edit manifest contract and failed one
selection test. The finalized manifest and validator passed all 16 related tests.
The first supplementary checkout found a stale digest for the final broker
runbook edit. Regenerating the manifest satisfied the repository validator and
the supplementary Windows check.

The correction's active-host Quick tier passed 1,279 deterministic tests in
278.869 s during the concurrent cold builds and governance checks. The WPF
suite occupied 177 s of that run. The later combined candidate passed 1,308
Quick tests in 167.885 s, including 84 s for WPF. Both exceeded the 120 s warm
target. Remote main integrated the correction at `bacf1a7ef0f3ce55e88ffe5afb9082cc6ee59a82`.
[Correction PR](https://github.com/PikkuJanne/ThirdLife/pull/76)
