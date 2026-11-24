# M1 gate — Audit-only vertical slice

| Field | Value |
| --- | --- |
| Gate task | `TL-0116` |
| Record state | Candidate |
| Verified default-branch base | `7c382bf784c5128ae96a8fcf07724fea35e9bc40`, PR #16 |
| Gate branch | `codex/tl-0116-m1-audit-gate` |
| Candidate build, source, evidence digests | Pending exact-candidate verification |
| Runtime authority | Active Codex Windows machine, `REF-CODEX-001` revision `2026-08-29.1` |
| Owner decision | Pending |
| Integration | Pending |

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

## Automated evidence and acceptance mapping

All 25 transitive prerequisites are complete. The pre-edit audit UI baseline passed
18 tests. Active-machine Targeted including Quick passed Core 110, Inventory 901 and
UI 61 with zero failures or skips in 54.258s. The identical default UI selection ran
once. The first named integrated rehearsal passed under a standard-user token. Final
verification uses a clean worktree of the exact gate candidate below.

| Acceptance area | Current candidate evidence |
| --- | --- |
| Unelevated launch and audit-only behaviour | Pending exact-build standard-user startup and fixture route rehearsal. Check the scenario uses only typed fixture providers, writes only to its isolated store/report, and offers no system-changing audit action. |
| Deterministic observations, unknowns, fixture isolation | Pending integrated rehearsal, applicable existing deterministic regressions and final Full. Confirm synthetic intake cannot permit live collection, unavailable and failed evidence are explicit and readiness stays unknown. |
| Durable job, cancellation and interruption | Pending fresh integrated create, save, close and reopen. Carry forward TL-0112's actual owned-child interruption evidence and revalidate the existing `TransactionInterruptionTests` with the candidate. Those tests kill the owned process around job creation, gate recording and guarded provider batch commits, then reopen the temporary store. They cover process termination separately from the in-window close/reopen check. |
| Privacy in logs, SQLite, temporary output and report | Pending candidate inspection and end-to-end report check. Verify the restricted report matches the committed fixture, protected output stays inside the owned workspace, sensitive values are filtered, and no normal log or unexpected temporary artifact contains prohibited data. Record sanitized results only. |
| Keyboard route and accessibility groundwork | Pending new or changed controls rehearsal. Use applicable earlier observations below only on unchanged shell interactions and record current human observations separately. |
| Clean-worktree continuity and triggered gate checks | Pending clean checkout of the candidate on the active machine, locked restore, Release build/start, bundle and repository validation, applicable Quick and Targeted selection and Full. Extended has no named trigger. Record commands, durations, counts, artifact digests and actual outcomes. |

The integrated route is exercised by
`ThirdLife.UI.Tests.M1AuditJourneyTests.OperatorRouteReopensSavedFixtureAndPreviewsItsWorkshopReport`.
Its automation result is pending. It is separate from both an earlier accepted shell
observation and a current human observation.

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
| Representative operator's observations mapped to the current journey | Pending |
| Operator's explicit acknowledgement of the machine and fixture scope | Pending |
| Project owner's explicit M1 decision | Pending |

Record the actual response, observed cases, restoration, and any focused correction
before final disposition. A failed or unperformed check stays visible. Keep the gate
in review when the current observations or decision are missing. Integration requires
the triggered automated evidence and all human evidence above. The current assignment
ends with evidenced M1 completion.
