# TL-0114 audit workspace verification

**Date:** 24 November 2026, Europe/Berlin  
**Base:** `e88718118e7d6e73e3291879e3053af93fea5afb`, PR #14 integrated on main  
**Branch:** `codex/tl-0114-audit-workflow`  
**Environment:** Active Windows 11 Pro x64 build 26300, `REF-CODEX-001` revision `2026-08-29.1`, SDK 10.0.400

## Implemented scope

The standard-user [audit workspace](../../../docs/ui/audit-workspace.md) connects job
creation and reopening, attributable external sanitization, a cancellable read-only
assessment and guided manual records. It uses the registered SQLite store and existing
orchestrator. State is recovered from committed records. Opening and navigating the
shell grants no collection access. Collection or a manual pass makes no readiness
decision.

Fixture and live evidence remain separate in the workspace and at live dispatch.
An isolated test store and synthetic roster use a distinct origin. Opening an
opposite-origin job permits review and blocks capture and assessment. The fixture
workflow records only Not run or Not available with explicit synthetic provenance.
The live orchestration canary refused synthetic evidence without dispatching a provider.

Guided manual records add bounded notes, a procedure reference and a link to the
previous visible attempt. The stored vocabulary keeps four values and preserves
the legacy `not_tested` token. SQLite schema version 3 remains in use. The current
reader accepts historical records. Older strict readers cannot read the enriched payload.

## Verification

- Focused pre-edit baseline passed 25 Core sanitization and job tests, 42 Inventory
  orchestrator tests, and 34 review-shell state and accessibility tests.
- Final focused `ManualTestTests` passed 27 in 4.466s with locked dependencies and
  isolated build output. Tests cover canonical outcomes, unavailable reasons, notes,
  essential failures, fixture origin and append-only retest history.
- Final focused `AuditWorkflowTests|AuditAccessibilityTests` passed 12 in 7.113s with
  locked dependencies and isolated build output. Tests exercise the real temporary SQLite
  store, missing and failed gate refusal, exact standard-user application roster
  composition, synthetic isolation, provider failure and timeout, cancellation with
  committed partial results, interrupted restart, persisted manual results and a
  loaded WPF keyboard journey with navigation during assessment.
- The first focused UI iterations exposed a fake-clock provider ordering issue and an
  incorrect tab expectation for the added history expander. Separating the timeout
  fixture and asserting the reachable expander resolved both. Scoped format verification
  exposed LF in newly added C# code, which was normalized to repository CRLF.
- The active-machine isolated worktree passed `eng/verify.ps1 -Tier Full -Restore` in
  284.726s. Connecting the production UI to SQLite changed its runtime dependency
  closure and triggered Full. The 278 governance tests passed in 198.497s, and all
  1,315 supported .NET tests passed with zero failures or skips, including Core 110,
  Inventory 901, Persistence 113 and UI 55. Formatting, locked restore and Release
  build completed. The run covered the Quick selection and affected subsystems.
  No independent Extended risk trigger applied.
- The verified candidate overlay SHA-256 was
  `a7fd19f44551c5496f42040d350c59bade0dae77b537df511321b23a5331a25e`
  on the base above. The prior checked-in apphost observer passed in 1.827s on the
  same Release build and standard-user token. It opened and closed its own shell
  normally, with no job or live collection. Apphost SHA-256
  `3dc6ed4a1382c1892ef3fd0cdaa5e939a79ac3a5b3ccbdbd5abd8463e3e5e54d`,
  UI assembly SHA-256
  `87f65badac1e70ceb3dd701368f9ce741ccc6739c13227e28898087029bd2891`.

No new package version, privilege, network path, background worker, product data
migration or release-interface claim was introduced. The UI uses locked dependencies
already in the solution. The smoke recorded a 262,885,376-byte peak working set for
startup and the observer, without establishing an acceptance budget.

## M1 scenario preparation

The active machine has an interactive standard-user session. A disposable guest with
both an evidenced standard-user token and no donor storage has not been established.
The existing WPF fixture composition is prepared as the safe starting point for the
integrated test. It uses the real window and services with one isolated SQLite store,
synthetic intake and a fixed synthetic provider roster. The loaded-window rehearsal
already covers create, sanitization, assessment, an unavailable manual result and
reopen. The fixture inputs contain no recipient data.

Use a successful bounded fixture observation and clearly labelled unavailable and
failure examples for the integrated flow. Use Not run or Not available if no safe
physical sample can be exercised. Carry the test scope and lack of physical-function
or live-device assessment evidence into the M1 review. TL-0115 adds report output.
TL-0116 owns the full integrated rehearsal, human observations and owner decision.
