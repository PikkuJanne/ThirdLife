# TL-0309 authenticated restart checkpoints

**Date** 2026-11-25  
**Base** `ef2802d5ccb8dbfc998dd35dffc04c9ffb575370`, PR #33  
**Environment** REF-CODEX-001 rev 2026-08-29.1, unelevated Windows 11 x64, local winsqlite3 and Windows CurrentUser DPAPI, .NET SDK 10.0.400 and Python 3.14.7

| Requirement | Evidence |
| --- | --- |
| Minimal checkpoint | The immutable v1 metadata binds the job and both plan digests, the exact action and journal entry, approval and correlation, and a validity window of up to 24 hours. The file-bound DPAPI authenticator protects a digest. The checkpoint contains no recipient identity, account string, or plaintext credential. |
| Wrong, expired or reused proof | Strict job and checkpoint lookup, fixed-time comparison, current-user authentication, current approval and intake checks, and an immediate transaction permit one explicit decision. Invalid proof, a replaced store, a time before issuance, and expiry deny resume. |
| Verified action stays terminal | Only the exact current Applied entry with RestartRequired opens an offer. Resume consumes the offer for review and grants no new dispatch. A reopened restart-pending action still requires fresh verification. |
| Abandonment and visibility | The normal audit job view loads pending offers without making a decision. It exposes explicit Resume for review and Abandon resume controls, and shows the latest committed outcome after reopening. An expired authentic offer remains revocable. |
| Unelevated start | The shell stays `asInvoker`. The recovery path registers no login action and has no launch, dispatch or elevation interface. |

Schema v6 adds two bounded tables to the existing protected SQLite store.
The migration has SHA-256 `02bf81fc6c5c49941951ad4bedd52401a38cd15e00c332a1d74697ae3657dbed`.
It preserves v1–v5 migration bytes and existing job data. The v5 reader rejects
the newer store without replacing it. The implementation adds no separate file,
cache, network dependency, exporter, or release interface.

## Verification

- Before edits, the focused `JournalTests` baseline passed 35 cases in 3.519 s.
  The first search filter selected zero tests and was corrected to the actual class.
- The Actions `RebootCheckpointTests` selection passed 21 cases. It covers exact
  authentication data, ownership of bounded token bytes, rejection of future
  journal entries, and expiry.
- `InterruptedRebootCheckpointMigration` passed two v5-to-v6 cases before and
  after commit. Validation and the test took 7.483 s together. It also checks
  preserved job records and a v5 reader's rejection of v6.
- The targeted Persistence `RebootCheckpointStoreTests` selection passed four
  cases in 5.133 s. It covers repeat issuance, durable decision readback, stale
  intake, expiry and rollback before issuance, and a previously Verified action.
- Both loaded-window `RebootRecoveryTests` passed in 7.066 s. They cover keyboard
  selection, explicit UI Automation action, names and live status, focus outside
  the action group, and job reopen through a fixture recovery adapter. Native
  SQLite and DPAPI were exercised separately. The first run chose a control
  disabled by the existing busy state as its focus assertion. The rerun used a
  shell control that remains available during the operation.
- The [independently invokable Extended cases](../../../docs/testing/failure-injection.md#tl-0309-restart-recovery-scenarios)
  both passed. `FI-011`: one case in 4.637 s, with cross-job and alternate-file scope,
  concurrent claims, a modified token, and modified metadata with a recomputed
  ordinary checksum. `FI-012`: two cases in 4.591 s, with faults immediately before
  and after decision commit, reopen, and no second grant or journal transition.
- Actions, Persistence and UI Targeted passed 1,406 .NET tests in 118.603 s,
  including Quick and the existing duplicate-start journal regression. The tested
  source tree is `4c4a987ee59c64ad23c169c7dc55719413e1d97d`, pushed as checkpoint
  `b3eabcd01c5e8a41f92bdec44aa10dfe29a4af44`.
- `./eng/verify.ps1 -Tier Full -Restore` passed in 340.433 s from a clean detached
  worktree of the same source tree. It ran 287 governance and 1,845 .NET tests.
  Locked restore, formatting and the warnings-as-errors Release build passed.

## Limits

All persistence and WPF runs use bounded synthetic job data on the active
machine. No installed application, other Windows account, physical power
cycle, or cold VM start was used. This task verifies authenticated continuity
and explicit review. Actual boot correlation and verification remain assigned
to TL-0509. An out-of-band same-file rollback by a privileged local principal
or an unobserved wall-clock rollback into the original validity window is
outside this proof. Resume carries no authority to change the machine.
Technical WPF checks establish the named control behavior. Perceptual speech,
hardware variation, and constrained host behavior were unobserved.
