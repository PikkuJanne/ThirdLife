# TL-0308 durable action journal

**Date** 2026-11-25  
**Base** `b624994b2a37997c14ba6fbb3e12ee0085c33777`, PR #32  
**Environment** REF-CODEX-001 rev 2026-08-29.1, Windows 11 x64, local winsqlite3, pinned .NET SDK 10.0.400 and Python 3.14.7

## Contract

| Criterion | Evidence |
| --- | --- |
| Exact plan and approval | The v1 canonical plan bytes reproduce the planning digest. The retained impact has its own digest and references this exact plan. Approval records its opaque operator, time, correlation and both digests. |
| State validation | The portable rule table checks previous state, attributable role, time, correlation, approval, compiled verification and rollback before appending. |
| Independent verification | Backend results can record Applied. A separate Verifier role and matching typed postcondition are required for Verified. |
| Sanitized payload | Entries have bounded opaque IDs, typed results and typed checkpoints. The API takes no backend, error, command or installer output text. |
| Transaction and replay | A guarded immediate transaction validates current approval and intake before Started. A matching duplicate returns `AlreadyRecorded`. A different payload under the same ID is a conflict. |
| Recovery | The repository retains Started, Applied and RequiresReview without automatic redispatch. A new approval checks the hashed event history for unresolved actions. An independent no-change confirmation can close an interrupted synthetic no-op. |

V5 is additive. The new records live in the existing protected SQLite job
store with no new cache or attachment paths. Prior migration bytes and
unrelated evidence are intact. Old binaries reject the newer schema. The
journal adds no external backend, reboot token, or automatic resume path.

## Verification

- The existing v3-to-v4 interrupted migration baseline passed one test in
  5.846 s before edits.
- V5 migration SHA-256 `7db7f1ad2977fce7464989e3a3ff6a2686b306e8120bc93cfb5d6c6f9190878e`.
- Three focused migration cases passed in 7.180 s with the migration changes
  overlaid on an isolated copy. They cover v4-to-v5 interruption on both sides
  of commit, retained gate and job records, retry, and rejection by a v4 reader.
- Solution force-evaluate and locked restores passed in 1.675 s and 1.730 s.
  Only the four expected project-reference lock closures changed. External
  package records and the approved licence-matrix digest were preserved.
- The narrow Actions journal suite passed 29 tests in 3.671 s and the
  SQLite journal suite passed four in 5.720 s. They cover typed transition
  rules, separate verification, exact plan and impact validation, stale gate,
  and identical replay after faults before and after commit.
- Persistence and Actions Targeted passed 1,370 tests in 112.312 s,
  including 130 Persistence and 121 Actions tests at source checkpoint
  `bbc3a3b`.
- Review of that checkpoint found that verification could pass while restart
  was pending, an interrupted no-op had no independent no-change resolution,
  and the cross-plan check trusted indexed state before validating its
  payload. The initial broad run stopped. The correction adds a typed
  no-change outcome, holds restart-pending actions, and validates the
  bounded event history before taking a new approval.
- After the correction the focused Actions journal suite passed 35 tests and
  the focused SQLite journal and migration selection passed eight. The
  sequential command took 9.526 s. The corrupt-state regression uses only a
  disposable job store and verifies that a new approval fails closed.
- Final Persistence and Actions Targeted passed 1,377 tests in 112.211 s.
  Clean `.\eng\verify.ps1 -Tier Full -Restore` passed 287 governance and
  1,816 .NET tests in 329.908 s. The clean source tree
  `f0c5f9f48a3feac4635867d83831bd5984d49a87` matches pushed checkpoint
  `e31498ac45d37cda7208df524444b9206cb00a2d`. Extended was outside the
  task's triggers.
