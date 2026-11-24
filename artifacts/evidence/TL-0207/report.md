# TL-0207 — Candidate policy reports and workspace

The audit workspace evaluates the bundled candidate against the selected
saved assessment and its current sanitization gate. It records the exact
policy bytes and captured decision in the restricted job store. Reopening
loads the saved decision. Explicit reevaluation appends a new record only
when the original evidence set still matches. A changed evidence set
requires a new assessment.

The plain-language disposition is visible in the workspace. Keyboard
expansion opens read-only policy, reason and evidence details. A restricted
workshop report links the policy ID, version and hash, the captured
disposition, rule outcomes, exception entries and saved evidence IDs.
Existing audit-only jobs retain their v1 report representation. The v2
representation keeps unknown facts visible and gives exceptions their own
section. Its display filters withhold secret-shaped identifiers and omit
operator exception prose.

## Evidence and lifecycle scope

The evaluation projection uses evidence IDs from the exact saved assessment,
its verified intake and scoped guided checks. The Windows release and
support aliases preserve provider ownership, dates, availability and
limitations. Conflicting architecture and stale lifecycle evidence cannot
satisfy the target. The bundled lifecycle revision was fresh through
2026-10-30. Current live evaluations on 2026-11-24 can require review for
unavailable support. Historical saved wording and evidence bindings remain
renderable. [Lifecycle source record](../../../docs/inventory/windows-lifecycle-data.md)

Unavailable CPU eligibility, ownership clearance, system storage mapping,
aggregate battery ratio, network confirmation and swelling inspection stay
unresolved. Candidate results use the available evidence. They do not
activate policy or authorize preparation, hardware work or release.

## Verification

Active machine `REF-CODEX-001` revision `2026-08-29.1`, Windows 11 x64,
.NET SDK 10.0.400, on 2026-11-24. All test stores and report outputs used
isolated synthetic data.

| Check | Result |
| --- | --- |
| Pre-edit Reports and AuditReport tests | 17 Reports and six UI tests passed in 13.068 s. |
| Reports with `PolicyReportTests` and `WorkshopReportRendererTests` | 15 passed in 3.586 s. Verified five v2 golden dispositions, visible unknowns and exceptions, secret-shaped identifier filtering, evidence binding, and the unchanged M1 golden report. |
| UI with `PolicyWorkflowTests` | Two passed in 5.866 s. Verified SQLite-backed capture, report, reopen and explicit reevaluation. The mounted WPF panel passed keyboard expansion and focus return. |
| UI with `AuditPolicyEvidenceTests` | 11 passed in 7.827 s. Checked scoped aliases, exact intake, saved manual confirmations, expiry, invalid revisions, architecture conflict and non-overlapping provenance. |
| `eng/verify.ps1 -Tier Targeted` with the Reports and Policy filters | Passed 1,175 .NET tests including Quick in 56.640 s. Build succeeded with zero warnings or errors. |
| `eng/verify.ps1 -Tier Full -Restore` in a clean detached worktree | Passed 286 governance and 1,572 .NET tests in 302.340 s on `7979f53c2893683a5b32306eb81d8e433ab284ea`. The direct Reports policy reference triggered Full. |

A direct render of the same saved synthetic Ready fixture produced the
expected captured disposition and verified the report-generation path.

The isolated compatibility test now removes the new optional field without
changing the legacy JSON encoding. It verifies that older policy decisions
reopen and retain the same serialized bytes.

Extended had no trigger. GitHub Actions is supplementary. The source and
completion evidence use [PR #24](https://github.com/PikkuJanne/ThirdLife/pull/24).
The branch publication was verified after a repository-server push error
recovered with a non-thin HTTPS retry.

The current workshop store and output paths stay in use. Additional report
fields are workbench-only policy identity, rule states, opaque evidence IDs
and governed exception attribution. The change adds no new export audience
or network path.
