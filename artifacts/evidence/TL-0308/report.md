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
| Recovery | The repository retains Started, Applied and RequiresReview without automatic redispatch. A new approval cannot bypass an unreconciled action in the job. |

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
