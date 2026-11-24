# M1 gate — Audit-only vertical slice

| Field | Value |
| --- | --- |
| Gate task | `TL-0116` |
| Record state | Done — internal audit-only technical baseline under D-074 |
| Verified default-branch base | `7c382bf784c5128ae96a8fcf07724fea35e9bc40`, PR #16 |
| Gate branch | `codex/tl-0116-m1-audit-gate` |
| First candidate source | `da746d306623f860ce64cd1143738087486886ca` |
| Prior wheel-correction source | `edb9b03ba813f5de67238d84dd424ad6da572257` |
| Verified candidate source | `34c7786f182ad13dcc22574209ea6c2a45bee7d3` |
| Prior candidate digests and actual observations | [Historical attempt record](../evidence/TL-0116/human-check.md) |
| Runtime authority | Active Codex Windows machine, `REF-CODEX-001` revision `2026-08-29.1` |
| Integration | [PR #17](https://github.com/PikkuJanne/ThirdLife/pull/17) |

The owner-authorized [permanent testing policy reset](../../docs/amendments/2026-11-24-permanent-testing-policy-reset.md)
defines TL-0116 as a Codex-owned technical gate and authorizes routine integration once
the gate and repository requirements are satisfied. The [roadmap](../../ROADMAP.md#m1--audit-only-vertical-slice)
and [task graph](../../TASKS.yaml) govern acceptance. The former operator request is
withdrawn. Its failed and unperformed criteria retain their historical results.

## Scope and safety

The scenario exercises the real WPF audit controls, normal job and assessment services,
SQLite persistence, and workshop report generation and opening. It uses a separate
synthetic workspace with an empty store, an explicitly synthetic intake record and
available, unavailable and failing synthetic providers. The applicable keyboard
hardware check is recorded as **Not available** because the fixture has no donor keyboard
to test. The fixture uses the active machine because a clean standard-user guest
without donor storage could not be established. It provides no evidence of live-device
collection or physical hardware function.

The normal product's standard-user startup is independently checked against the exact
candidate. The fixture never opens the registered live workshop store or authorizes
assessment of the real host. Output stays local and restricted. The tested scope is
audit only. Representative usability, perceived accessibility, later product readiness
and release authorization are outside this gate. GitHub Actions remains supplementary.

## Integrated predecessors

| Task | Accepted evidence and relevance |
| --- | --- |
| TL-0112 | [Assessment orchestration](../evidence/TL-0112/report.md) covers deterministic normalization, unavailable evidence, bounded cancellation and owned-child interruption. |
| TL-0113 | [Observed shell cases](../evidence/TL-0113/human-walkthrough.md) preserve the original build, host, setting and outcome. |
| TL-0114 | [Durable audit and manual capture](../evidence/TL-0114/report.md) covers gating, cancellation/recovery, manual outcome variants, SQLite persistence and reopen. |
| TL-0115 | [Workshop report](../evidence/TL-0115/report.md) covers deterministic content, privacy, guarded output, keyboard preview and standard-user launch. |
| TL-0104 | [Structured logging task evidence](../../TASKS.yaml) records the exact sanitized logging contract, diagnostics and redaction checks. |

## Historical first-candidate evidence and acceptance mapping

All 25 transitive prerequisites are complete. The pre-edit audit UI baseline passed
18 tests. Active-machine `eng/verify.ps1 -Tier Targeted -Restore -TestProject
tests/ThirdLife.UI.Tests/ThirdLife.UI.Tests.csproj` included Quick and passed Core 110,
Inventory 901 and UI 61 with zero failures or skips in 54.258s. The identical default
UI selection ran once. The first named integrated rehearsal passed under a
standard-user token. Final verification used a clean, detached worktree of the exact
candidate on Windows 11 Pro x64 build 26300 with SDK 10.0.400.

`eng/verify.ps1 -Tier Full -Restore` passed in 283.547s. It checked bundle and
repository integrity, governance regressions, locked restore, format, Release build
with warnings as errors and 1,337 .NET tests with zero failures or skips. Extended
had no named trigger. `eng/run-ui-smoke.ps1` confirmed the exact apphost opened and
responded under a standard-user token in 1.314s and closed normally in 1.569s total.
The clean worktree stayed clean. No source, fixture or binary changed for handoff.
Supplementary Windows clean-checkout run `45435029912` passed on the candidate.

| Acceptance area | Current candidate evidence |
| --- | --- |
| Unelevated launch and audit-only behaviour | The normal apphost and the fixture rehearsal ran under a standard-user token. The fixture constructs only the three typed synthetic providers and writes job history and the restricted report within its isolated workspace. |
| Deterministic observations, unknowns, fixture isolation | Full passed deterministic observation normalization, gate and orchestrator tests, including rejection of synthetic intake for live providers. The named run preserved explicit synthetic collected, unavailable and failed outcomes, with readiness unknown after reopen. |
| Durable job, cancellation and interruption | The named run created, saved, closed and reopened a SQLite job. Full passed the partial-evidence cancellation test and Persistence's 113 tests, including `TransactionInterruptionTests`. Those tests terminate owned children around job creation, commit, gate recording and guarded provider batch writes, then reopen the store. TL-0112 documents the prior assessment interruption work. |
| Privacy in logs, SQLite, temporary output and report | Full passed structured diagnostics, privacy and deterministic report tests. The named run checked the SQLite fixture, absence of normal log and staging artifacts in that workspace, restricted output with an opaque name, byte equality with the opened report and exclusion of the private failure marker and free-text note from the report. No raw data was exported. |
| Keyboard route and accessibility groundwork | The named run verified focus movement, live-region semantics and the read-only report preview in process. Applicable earlier shell observations carry their original provenance. |
| Clean-worktree continuity and triggered gate checks | Active-machine Targeted including Quick and exact-candidate Full passed. The separate standard-user smoke and named fixture rehearsal passed. No Extended trigger applied. |

The separate in-process route ran through the named
`ThirdLife.UI.Tests.M1AuditJourneyTests.OperatorRouteReopensSavedFixtureAndPreviewsItsWorkshopReport`
test. The explicit `ActiveMachine` invocation is separate from the default Full
selection. `dotnet test` used Release with `--no-build --no-restore`, the exact test
name filter and `RunConfiguration.TreatNoTestsAsError=true`. One test passed in
3.734s total with zero failures or skips. The automated assertions cover UI focus,
control state and automation metadata. The prior external launcher primarily proved
that its window opened and closed before timeout. Neither run established the
ordinary-input journey through the actually offered launcher.

## Corrections after the first attempt

The first fixture had one job and no saved sanitization or gate decision. A focused
rehearsal used the launcher's actual prefilled controls and toggled the confirmation
before saving. One test passed and enabled the existing gate. The original route
had assigned the fields itself. The correction brings focus to the required intake
action on create, offers a route from the blocked assessment, and focuses Start
assessment after the existing gate allows it. The focused integrated/accessibility
check passed three tests. The final focus change also covers completion of an
interrupted saved gate. Two focused tests passed for that recovery and the
complete prepared route, both using real SQLite fixtures.

The wheel baseline changed delivery timing after a batched-event test coalesced the
movement. Six spaced wheel pulses of −20 moved the audit window 288 DIPs, while
one −120 detent moved it 48 DIPs. The update bounds movement, preserves fractional
offsets, uses pixel-scrolled evidence lists and scrolls the outer view when the
inner pane is already at a boundary. The app caps a pulse at one detent and three
lines. The loaded-WPF wheel and shell accessibility set passed 14 tests in 8.663s.
No later physical touchpad observation was established.

The initial Targeted run stopped in formatting when the two new C# files used LF
instead of the repository's CRLF. Normalizing those files and checking the changed
formatting made the next run informative. Targeted including Quick passed in
63.560s before the final recovery-focus change. Core 110, Inventory 901, UI 65
and seven explicitly filtered tests passed with zero failures or skips.

Narrator's process was still open on the correct desktop. The operator reported no
usable speech. The later transcription establishes some screen-reader consumption of
WPF semantics. It does not establish audio playback.

The corrected worktree initially resolved a Python installation without PyYAML and
stopped at toolchain validation. The complete run explicitly uses the existing
pinned Python 3.14.7 and PyYAML 6.0.3 environment through its process-local tool path.

`eng/verify.ps1 -Tier Full -Restore` passed against the corrected source in a clean
same-machine worktree in 302.263s. It passed 279 governance and 1,342 .NET tests
with zero failures or skips. Windows Release formatting, build and locked restore
passed. The apphost smoke observed normal unelevated launch and navigation in 1.444s
and closed normally in 1.767s total. The exact named fixture rehearsal passed in
4.329s including test startup. It exercised the prefilled inputs including the
checkbox, disabled and enabled assessment states, keyboard transitions, the
unavailable manual sample, reopen and restricted report content in process. The later
owner report still described a disabled Start assessment through the supplied route.

## Reported defects and technical disposition

The [withdrawn operator record](../evidence/TL-0116/human-check.md) preserves both
reports. The exact corrected build reproduced the repeated functional blocker with
the documented Enter route. WPF left the confirmation unchecked, so the save rejected
the intake and `CanAssess` correctly stayed false. The scoped repair enables Enter on
the checkbox. The evidence establishes a defect in the offered route. The exact input
used in the historical operator attempt was unrecorded. [Exact-build reproduction and verification](../evidence/TL-0116/fixture-route.md)

The [technical accessibility record](../evidence/TL-0116/accessibility-technical.md)
records the wheel overtravel cause and correction, including a remaining jump over a
closed, unfocused intake dropdown. The historical speech report remains an audible
failure with an undetermined cause. External UI Automation received the tested
application events. The result establishes the programmatic announcement delivery
required by the technical gate. Audible output remains unresolved and carries no
pass. No Windows settings were changed for the investigation. Physical touchpad
behavior, real display-mode perception and representative comprehension have no
new direct observations on this candidate. TL-0113 A11Y-001–007 observations apply
to unchanged shell portions under their original settings and build.

## Current technical verification

The changed-surface Targeted verifier included Quick, locked restore, format and a
warnings-as-errors Release build. It passed Core 110, Inventory 901, UI 67 and the
18 explicitly filtered accessibility and wheel checks in 65.452s, with zero failures
and skips. The formatter recorded a workspace-load notice and returned success. The
Release build reported zero warnings and errors. The 34 focused governance and ADR
regressions passed in 9.294s. The bundle validator passed with its customary pinned
PyYAML structural check.

The active Windows 11 Pro x64 machine was at build 26300, `REF-CODEX-001`
revision `2026-08-29.1`. A clean detached worktree used SDK 10.0.400 and the existing
pinned Python 3.14.7/PyYAML 6.0.3 environment via a process-local tool path.
`eng/verify.ps1 -Tier Full -Restore` passed in 305.251s. It passed 285 governance
and 1,343 supported .NET tests with zero failures or skips and verified the bundle,
repository controls, locked restore, format and Release build. The formatter notice
did not abort the run. The Release build reported zero warnings and errors. The
separate active-machine route and startup smoke used those exact clean binaries.
The same worktree was clean after all checks.

| Acceptance area | Evidence on the verified candidate |
| --- | --- |
| Actual offered workflow | `pwsh -NoProfile -File eng/run-m1-audit-journey.ps1 -VerifyAccessibility` passed in 9.635s. The isolated window started with no job. Normal Tab, Shift+Tab and Enter created the job, toggled explicit confirmation, saved intake and started assessment only after the button enabled. The completed read-only assessment correctly required review. The same job reopened with the persisted unavailable manual result and a matching report preview/file. The launcher checked actual outcomes before normal cleanup. |
| Safety and unknowns | The external route began blocked, saved an explicitly synthetic record, and used only the three isolated providers. Deterministic Full tests cover missing, failed and unsaved intake, synthetic evidence rejection for live providers, and normalized collected/unavailable/failure results. The completed fixture and report keep readiness unknown. |
| Persistence, interruption and privacy | The launched-route assertions reopened SQLite and verified intake, assessment, the manual result, the same job in the preview and exact report bytes. They checked the restricted output label, opaque filename, bounded database, and absence of raw failure text or unexpected log and staging files. Supported Full tests cover cancellation, owned-child interruption and durable recovery. |
| Accessibility and wheel behavior | The scripted keyboard journey used the real window and external UI Automation received four distinct workflow events. Six native fractional wheel messages and one detent each moved 2.22990% in the measured viewport. The focused dropdown regression and Full component checks cover proportional movement, focus, keyboard recovery, reduced viewport and double-scale layout. |
| Unelevated normal startup and resource observation | `eng/run-ui-smoke.ps1` passed against the ordinary apphost, opened Inspect and proved a responsive standard-user UI. The observation took 1.445s and normal exit completed in 1.749s. Peak process working set was 222,076,928 bytes. This is a bounded observation of one startup. |
| Governance and integration | The 28 affected task contracts preserve earlier evidence and completed records. The roadmap and graph agree, including the two TL-0610 onboarding edges and the explicit stable-gate requirement for TL-0611. Supplementary clean-checkout workflow run `45452635193` passed on the tested candidate. No Extended trigger applied. |

[Source-bound test receipt](../evidence/TL-0116/clean-candidate.json). The candidate
and the evidence-only completion commit are integrated through PR #17. This gate
establishes the internal audit-only baseline. Controlled-pilot and stable-release
authorization remain at their later gates. The assignment ends at M1.
