# TL-0504 saved Windows Update findings

On 2026-11-27 the active Windows 11 x64 host ran build 26300.9974 with .NET SDK 10.0.400 and the configured Python 3.14.7 environment. The engineering base was `967ae8dad190441f7877f72defa10950e69870a1`, which includes the native servicing broker from PR 75. The six existing update workspace tests passed before editing. [Earlier implementation and guest evidence](report.md)

## Saved findings

The production WUA scan retains one bounded eligibility reason alongside each classified offer. The journal saves the original offer identity, revision, class and transfer bound plus the observed reason. An exact approval still requires independently rechecked selection, policy and resources for that revision. SQLite schema 011 preserves historical payloads and their hashes. Opening an older scan leaves its unrecorded reason unknown.

Prepare lists the saved offers and displays the observed reason for restricted ones. Verify explains that Windows offered revisions and none met the servicing requirements when that was the scan outcome. A single recorded reason appears in the shared status. Pending restart, missing installed-state verification and an unresolved handoff retain their previous recovery priority.

The limited-token `WindowsUpdateAssessment` uses one native scan and at most one exact read-only lookup. It passes the captured scan to the ordinary Prepare workflow for an isolated synthetic intake job, opens the saved job again and compares the inventory and journal entries. The test has a read-only adapter for this captured inventory. It verifies that the job contains only a scan start and completion and has no approval or dispatch. The guest response exposes bounded counts, exact-lookup status, reason and reopening result. Temporary job data stays within the run's cleanup scope. [Guest procedure](report.md#repeating-the-guest-assessment)

## Host verification

Technical source `c3bba44def7aec9d6c36740d7dbff814e99cb212` passed Targeted in 369.787 seconds. All 1,442 .NET test executions passed with zero skips. Quick selected Core, Inventory and UI. The additional Windows Update selections passed 16 Core, 40 Persistence, 54 Broker, 31 Packages and 19 UI cases. The locked restore, formatting, warnings-as-errors Release build and scoped governance checks passed. The in-process tests use controlled offers and action responses. The host did not initiate native servicing.

```powershell
$projects = @('Core','Persistence','Broker','Packages','UI') | ForEach-Object {
    "tests/ThirdLife.$_.Tests/ThirdLife.$_.Tests.csproj"
}
./eng/verify.ps1 -Tier Targeted -TestProject $projects -Filter 'FullyQualifiedName~WindowsUpdate&Category!=ActiveMachine&Category!=Extended&Category!=SandboxOnly'
```

The SQLite cases exercise exact approval and phase intent, changed revision or class, consent refusal, cancellation, timeout, partial results, missing receipt, the three-pass limit, independently observed installed identity and restart/rescan. The migration regression opens a schema 010 record, saves a new unsupported-handler result and rejects both the restricted candidate and a relabelled candidate at approval. The journal retains both scans and the historical reason remains unknown. The broker tests check separate protected grant and peer authorization, current machine facts and phase binding. Native installation and the update-specific recovery reserve still require a qualifying real offer on ThirdLife-W11.

Technical source `85ffc412c5e56824580383720457b31dcd94a4c9` passed all six focused UI status and inventory tests in one second and all 11 Core update-policy tests in 202 ms. They cover distinct explanations for empty and ineligible scans, unrecorded historical reasons and recovery precedence. These durations are the test assemblies' reported execution times.

The verifier identified two test initializer lines that needed standard formatting. The corrected technical source `5a817a0f96e7eaafb013c6e76f1f54e35bd3df8b` passed Targeted in 184.701 seconds with 1,319 test executions and zero skips. This run used the command above with Core and UI selected, including the full Quick selection. Formatting and the warnings-as-errors Release build passed.

## ThirdLife-W11 observation

The integrated assessment source was `68537173b38d84b58eb8b2b7f2c59622432b341b`. On 2026-11-27 the existing guest ran Windows build 26300.9974 and wuapi.dll 1511.2611.5012.0. The limited-token `WindowsUpdateAssessment` passed in 226.146 seconds including verified cleanup. WUA offered one revision and zero selectable revisions. The exact lookup reported `UnsupportedHandler` and uninstall unavailable. Prepare saved the captured native inventory, reopened the same isolated job and retained the exclusion. The journal had zero action approvals and zero dispatches. The fixture intake, current guest facts and later controlled Verify text check have distinct provenance.

| Artifact | SHA-256 |
| --- | --- |
| Staged assessment payload | `2b8124490c87757ee0a5779af3e8abda164e18210c06f805ea14227ef1b78b86` |
| WUA adapter | `b850e19acd563f3491c71993a12bf566558b17769d105ef28b6f77ff787f43ba` |
| Local runner receipt | `215432a8a76a458f4d0ea2b5da517611e73e7f5185a00829f79e1da212b38b4c` |

The observation concerns applicability at that scan. Native download, installation, independently observed installed identity/revision, rescan and any required restart remain open at TL-0511. Reopen on this guest when a selectable security or critical CBS offer is available and an exact, effective review binds its recovery method, measured space and route authority. Apply the normal plan approval, per-phase authorization and Windows consent to that actual transaction.

## Integrated Windows verification

On 2026-11-27 a clean detached checkout of
`91c0153bd03228a45fd15f3e585a4eb27c01d72c` passed
`eng/verify.ps1 -Tier Full -Restore` on the active Windows 11 Pro x64 host in
446.842 s. The 310 governance tests took 305.871 s. Locked restore, formatting,
warnings-as-errors build and all 2,458 deterministic .NET tests in 13 assemblies
passed with zero skips. The checkout remained clean. An earlier candidate was
retired from its Full run during discovery for the corrected post-install
recovery guard and test formatting. The completed Full run covers both.

The combined Quick selection passed 1,308 cases. The independent owned-process
UI interruption selection exercised `KilledPackageApplicationReopens`,
`KilledNoOpApplicationReopensTheSameJobAndReconcilesWithoutDispatch` and
`TerminatedOwned` on the preceding integrated recovery build. All five passed
in 32 s with isolated jobs and fixture backends. On the same final source tree
as the completed Full run, `eng/run-ui-smoke.ps1` reached Inspect through UI
Automation and closed the unelevated application normally in 1.549 s.
The independent guest receipt above remains the native update observation.

The implementation meets TL-0504's engineering criteria. The empty effective
review catalog leaves native servicing closed. TL-0511 retains actual native
download, installation and independently confirmed installed revision and
rescan with any required restart. Ordinary dispatch still requires TL-0314.

The final task disposition passed the five continuation/checkpoint tests in 0.459 s. The warm Quick run passed all 1,308 selected tests in 129.510 s, 9.510 s above the 120 s target, with 67 s in WPF. The earlier concurrent cold run took 167.885 s. [Integration PR 77](https://github.com/PikkuJanne/ThirdLife/pull/77)
