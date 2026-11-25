# TL-0307 material plan changes and reapproval

**Date** 2026-11-25  
**Base** `a6fc98ff91f92cfefccc9bb9d72da6741135cf6f`, PR #31  
**Environment** REF-CODEX-001 rev 2026-08-29.1, Windows 11 x64, pinned .NET SDK 10.0.400

## Material inputs

| Input | Effect on approval |
| --- | --- |
| Package identity | Source, exact package ID and version, publisher, architecture, minimum OS and scope are compared using the typed request and pinned declaration. |
| Planned actions | Added or removed capabilities, action order, kind, intent, reason, retry and availability changes require review. |
| Impact and safety | Changes to scope, elevation, restart, network, rollback, verification, download or installed size, service or auto-update declarations, or impact snapshot version require review. |
| Bound context | Job, current intake decision, selected profile, evaluated policy, current evidence or synthetic mode changes invalidate the approval. The exact digest also catches other pinned material, including catalog provenance and the applied exception set. |
| Progress | Progress counters, correlation and display-only timing belong to execution status. They are outside the plan comparison and do not refresh approval. |

The comparator requires both impact snapshots to match their plan digests.
Blocked or empty plans cannot pass the comparison. Changed package source
and version fail closed under the old approval. Dispatch will use the same
comparison after loading a current active approval. This task starts no
processes and adds no executable package path.

The review displays a plain-language diff, keeps former decisions with their
exact plan and impact, and records a new explicit decision for changed
material. The history is bounded and specific to the open review session.
Durable storage belongs to the journal task. No migration, dependency,
release interface or privilege activation is introduced here.

## Verification

The pre-change loaded PlanReview UI baseline passed five tests in 3 s of
test time. The focused PlanDiff suite passed 24 cases in 3.783 s. These
exercise exact source and version changes, independent equivalent rebuilds,
scope and impact mismatches, fixed contract fields, blocked plans, and
golden operator text.

Seven loaded WPF cases passed in 10.491 s. The new cases retain active
approval after an equivalent rebuild without duplicating history, require
an explicit second approval after removing an optional action, and keep
both decisions. Re-selecting an older digest does not restore its prior
approval after selection invalidation. The UI also detects a policy digest
change at the approval click and presents the changed plan before taking a
new decision.

The governed UI and Actions Targeted run passed 1,211 tests in 61.069 s,
including 89 UI and 92 Actions tests. Full and Extended were outside the
triggers for this comparison and review-session change.
