# Approved amendment — Permanent testing policy reset and M1 completion

| Field | Approved value |
| --- | --- |
| Amendment | `AMD-2026-11-24-PERMANENT-TESTING-POLICY-RESET` |
| Approving owner | Janne Vuorela, ThirdLife project owner |
| Approval date | 24 November 2026, Europe/Berlin |
| Authorization source | The project owner's application instruction in the active Codex conversation, titled **THIRDLIFE — PERMANENT TESTING POLICY RESET AND M1 COMPLETION**, directing the changes and M1 completion below |
| Governing decisions | [D-045](../../DECISIONS.md#d-045--authority-and-change-control), [D-074](../../DECISIONS.md#d-074) |
| Bundle revision | `0.5.0` → `0.6.0` |
| Verified continuation when applied | PR #17 head `674638a972915cf533a62f37a9f81b4b0985f6ca`; `main` `7c382bf784c5128ae96a8fcf07724fea35e9bc40` |

The owner directed, “After the amended requirements and genuine repository merge
requirements are satisfied, commit, push and integrate the M1 work under the existing
assignment.” The conversation instruction authorizes this prospective amendment and the
assigned M1 repair, verification, evidence, commit, push, PR update and routine integration
after the amended technical gate and actual repository merge requirements are met. It
supplies neither an unperformed result nor an M1 approval. The supplied revisions identify
the inspected sources. The fetched repository determines the starting state.

## Scope and rationale

This supersedes the testing and handover direction in the 24 November continuation and
recovery files and the prospective policy of [D-073](../../DECISIONS.md#d-073). Preserve
the [prior amendment](2026-11-24-minimal-human-testing.md), completed task contracts and
actual evidence with their original provenance. Routine human QA and repeated confirmation
consume owner time without resolving specific consequential decisions. Codex owns the
engineering route from reproduction through integration. Known broken required behavior
blocks completion. Missing physical or perceptual evidence limits the claim.

This is the approved synchronized scope for [PR #17](https://github.com/PikkuJanne/ThirdLife/pull/17).
The assignment ends at M1. M2–M7 contracts change prospectively. Later implementation,
external exposure, branch retirement, destructive operations, security-policy changes and
sibling work require their own authority.

## Contract and dependency changes

| Scope | Responsibility |
| --- | --- |
| TL-0116 | Codex proves the internal audit-only gate, including the repaired real launched journey, technical accessibility, privacy, continuity and applicable Full checks. Withdraw the former operator assignment. |
| TL-0208 | Codex proves disposition behavior. Retain the policy-owner decision for new or materially changed wording and exception governance. |
| TL-0311, TL-0312, TL-0314 | Codex implements and proves broker/IPC behavior. One privilege-boundary authorization is decided on prepared evidence at TL-0314. |
| TL-0401, TL-0410, TL-0411 | Codex records the compliant structured WinGet ADR and verifies the isolated first-write journey. Retain the actual first-write-capable product/security decision at TL-0411 and honor action-specific authority. |
| TL-0504, TL-0506, TL-0508 | Codex verifies supported update/security behavior. The actual security baseline disposition belongs to TL-0611. Retain applicable policy/application-selection/licensing/privacy choices for changed catalog content at TL-0508. |
| TL-0509, TL-0510, TL-0511 | Codex proves checkpoints, approved VM cold-start behavior, risk/resource scenarios and the technical M5 gate. Preserve physical evidence limits and blockers. TL-0611 owns the actual pilot-scope decision. |
| TL-0605, TL-0608, TL-0609, TL-0610, TL-0611 | Codex verifies guide structure/privacy, audits, package lifecycle/rollback and runbook. TL-0611 owns the controlled-pilot authorization and necessary accountable dispositions and partner decision. Preserve any required specialist and pilot evidence. |
| TL-0701, TL-0702, TL-0704–TL-0709 | Codex verifies recipient-control, recovery, lifecycle, documentation, release-interface checks, release matrix, frozen candidate and standalone boundary. Candidate preparation precedes the stable exposure decision. |
| TL-0710 | Decide stable release and portfolio transition on the frozen artifact and required evidence. Attribute actual security/privacy/accessibility/licensing/support/portfolio decisions by scope. |

TL-0701 and TL-0702 depend on technical pilot candidate TL-0610 in place of TL-0611.
TL-0710 explicitly depends on TL-0611. TL-0611 still controls actual pilot exposure and
the stable gate retains required pilot evidence. TL-0703 retains its existing TL-0611
prerequisite. These changes authorize development
sequencing only. Task IDs, milestone ownership, original historical observations and
completed task records are preserved.

## Evidence and impact

The prior [operator record](../../artifacts/evidence/TL-0116/human-check.md) is withdrawn
as an instruction and remains evidence of reported failures and unperformed checks. The
[technical gate record](../../artifacts/gates/M1-audit-only.md) maps results to the amended
criteria. Reported failures require evidence of resolution or an explicit justified
disposition. Technical accessibility evidence cannot establish audible speech, physical
touchpad usability, or representative comprehension. No separate M1 technical sign-off
is required. Preserve real product control, authorization and evidence semantics.

No data schema, migration, package, security policy, privilege, release interface or
product boundary change is authorized by this amendment. M1 repair uses isolated synthetic
providers and storage, keeps the sanitization gate and host Application Control in force,
and exercises the unelevated supported UI. Technical accessibility and modest-hardware
requirements still apply. D-066 limits coverage claims. Team B/B1 completes only at TL-0710
and the B4 boundary is unaffected.

Synchronize governing documents, task contracts and dependency validators in one bundle.
Verify the amended graph, state rules and M1 entry-point coverage with focused regressions,
clean-worktree verification and applicable Full gates. Record further corrections in
forward commits. Preserve evidence provenance if a later amendment changes this contract.
