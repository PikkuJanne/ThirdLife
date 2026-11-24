# M1 gate — Audit-only vertical slice

| Field | Value |
| --- | --- |
| Gate task | `TL-0116` |
| Record state | Corrections in progress |
| Verified default-branch base | `7c382bf784c5128ae96a8fcf07724fea35e9bc40`, PR #16 |
| Gate branch | `codex/tl-0116-m1-audit-gate` |
| First candidate source | `da746d306623f860ce64cd1143738087486886ca` |
| Candidate build and evidence digests | [Build and fixture identity](../evidence/TL-0116/human-check.md#codex-only-preparation-and-record) |
| Runtime authority | Active Codex Windows machine, `REF-CODEX-001` revision `2026-08-29.1` |
| Owner decision | Pending |
| Integration | Draft PR #17. Gate has not merged. |

The owner-authorized [minimal human testing amendment](../../docs/amendments/2026-11-24-minimal-human-testing.md)
assigns the integrated operator review and the M1 decision to this gate. The amendment
authorizes routine integration once the gate is evidenced. The [roadmap](../../ROADMAP.md#m1--audit-only-vertical-slice)
and [task graph](../../TASKS.yaml) govern the acceptance criteria.

## Scope and safety

The scenario exercises the real WPF audit controls, normal job and assessment services,
SQLite persistence, and workshop report generation and opening. It uses a separate
synthetic workspace with an empty store, an explicitly synthetic intake record and
available, unavailable and failing synthetic providers. The applicable keyboard
hardware check is recorded as **Not available** because the fixture has no donor keyboard
to test. The operator's observations concern usability of the workflow. The review
uses the active machine because a clean standard-user guest without donor storage
could not be established. Live-device collection and physical hardware function are
outside the fixture evidence.

The normal product's standard-user startup is independently checked against the exact
candidate. The fixture never opens the registered live workshop store or authorizes
assessment of the real host. Output stays local and restricted. The tested scope is
audit only, with no inference of readiness or later product, release or hardware
approval. GitHub Actions evidence remains supplementary.

## Integrated predecessors

| Task | Accepted evidence and relevance |
| --- | --- |
| TL-0112 | [Assessment orchestration](../evidence/TL-0112/report.md) covers deterministic normalization, unavailable evidence, bounded cancellation and owned-child interruption. |
| TL-0113 | [Observed shell cases](../evidence/TL-0113/human-walkthrough.md) preserve the original build, host, setting and outcome. |
| TL-0114 | [Durable audit and manual capture](../evidence/TL-0114/report.md) covers gating, cancellation/recovery, manual outcome variants, SQLite persistence and reopen. |
| TL-0115 | [Workshop report](../evidence/TL-0115/report.md) covers deterministic content, privacy, guarded output, keyboard preview and standard-user launch. |
| TL-0104 | [Structured logging task evidence](../../TASKS.yaml) records the exact sanitized logging contract, diagnostics and redaction checks. |

## First candidate automated evidence and acceptance mapping

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
| Keyboard route and accessibility groundwork | The named run verified focus movement, live-region semantics and the read-only report preview. The applicable earlier shell observations are carried forward below. Human review of changed controls is pending. |
| Clean-worktree continuity and triggered gate checks | Active-machine Targeted including Quick and exact-candidate Full passed. The separate standard-user smoke and named fixture rehearsal passed. No Extended trigger applied. |

The integrated route runs through the named
`ThirdLife.UI.Tests.M1AuditJourneyTests.OperatorRouteReopensSavedFixtureAndPreviewsItsWorkshopReport`
test. The explicit `ActiveMachine` invocation is separate from the default Full
selection. `dotnet test` used Release with `--no-build --no-restore`, the exact test
name filter and `RunConfiguration.TreatNoTestsAsError=true`. One test passed in
3.734s total with zero failures or skips. The automated assertions cover UI focus,
control state and automation metadata. Actual speech, readability and operator
judgement have their own record.

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
Physical touchpad feedback remains pending.

The initial Targeted run stopped in formatting when the two new C# files used LF
instead of the repository's CRLF. Normalizing those files and checking the changed
formatting made the next run informative. Targeted including Quick passed in
63.560s before the final recovery-focus change. Core 110, Inventory 901, UI 65
and seven explicitly filtered tests passed with zero failures or skips.

Narrator's process was still open on the correct desktop. The operator reported no
usable speech. The next handoff verifies the Windows Narrator toggle and foreground
fixture and explicitly samples speech. Recheck the changed or uncompleted portions
of the audit journey. The corrected UI and automated evidence require a new clean
candidate run before that handoff.

## Operator observations and owner decision

The [prepared operator check](../evidence/TL-0116/human-check.md) owns the current
keyboard and Narrator journey, display samples on the changed controls, review of
labelled unavailable/failure examples, and confirmation of close/reopen and report
usability. Reuse TL-0113 `A11Y-001`–`A11Y-007` for the unchanged portions of the shell
only. Review the added job, sanitization, assessment, manual capture and report controls
with the exact candidate. TL-0113 does not supply integrated speech, progress, manual or
report observations. The automated cancellation and owned-child checks remain distinct
from the operator's actual review.

| Human evidence | Outcome |
| --- | --- |
| Representative operator's observations mapped to the current journey | First attempt blocked. The operator reported Narrator unavailable, disabled Start assessment and uncontrollable touchpad scrolling. The [attempt record](../evidence/TL-0116/human-check.md#first-operator-attempt) preserves the observed limits. |
| Operator's explicit acknowledgement of the machine and fixture scope | Pending |
| Project owner's explicit M1 decision | Pending |

Record the actual response, observed cases, restoration, and any focused correction
before final disposition. A failed or unperformed check stays visible. Keep the gate
in review when the current observations or decision are missing. Integration requires
the triggered automated evidence and all human evidence above. The current assignment
ends with evidenced M1 completion.
