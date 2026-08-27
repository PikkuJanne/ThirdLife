# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-08-28  
**Snapshot preparation time:** 2026-08-28T00:01:44+02:00  
**Bundle baseline:** 0.3.1  
**Portfolio baseline:** ThirdLife Software Portfolio v2.1  
**Current milestone:** M1 — Audit-only vertical slice — active  
**Current task:** `TL-0102` — Implement the SQLite job store and migrations — `review`  
**Action state:** implementation and all automated Targeted, Quick, Full, security, supply-chain, and source-bound SBOM evidence complete; preparing the exact pushed human-approval candidate

## Executive state

M0 is complete and `TL-0101` is done. `TL-0102` now implements the first durable local job-store slice and has passed its complete 72-test Targeted suite both on the host and in a hardened same-machine Windows Sandbox. Exact checkpoint `8a4330afd288f4aead03e01bf95c935c4d342785` passed persisted-evidence Quick and trigger-required Full: 184 governed regressions, locked restore and NuGet audit, format verification, a zero-warning/zero-error Release build, and all 145 solution tests. Two source-bound CycloneDX 1.6 SBOMs are byte-identical. Automated work is complete; the task remains `review` solely for fresh named human approval of the exact 28-component licence/installation/redistribution matrix at the pushed review candidate.

No release, production packaging, retention/deletion, backup/restore, attachment, sibling-integration, cross-hardware, or legal-rights claim is made by this checkpoint.

## Implemented behavior

- `ThirdLife.Core.Jobs.IJobStore` owns the pure repository port and typed batch, stored-job, and checkpoint contracts. Core remains independent of SQLite, Windows APIs, WPF, WinGet, shell output, and sibling products.
- `SqliteJobStore` uses the registered job-store location beneath the current user's local application-data directory through its public API; arbitrary roots are internal test-only inputs.
- Schema versions 1 and 2 persist jobs, normalized observations, external sanitization evidence, human-test evidence, reversible archive state, and append-only store checkpoints.
- Embedded migrations are transactional and record migration name, script SHA-256, resulting-schema SHA-256, and application/user versions. Reopen verifies identity, version, ledger, schema, quick integrity, foreign keys, payload hashes, counts, and legal archive history.
- First creation builds, migrates, integrity-checks, and closes a restrictive random sibling database; it removes only the verified owned journal and publishes with a no-overwrite same-directory handle rename. Crashes leave either no final database or a complete identified one; concurrent creators adopt the complete winner.
- The store root, `jobs` root, database, rollback journal, and per-job directories have protected ACLs for the current user, LocalSystem, and local Administrators. Held handles, final-path/object identity, link count, ACL, reparse, junction, symlink, ancestor, hardlink, replacement, and unexpected WAL/SHM checks fail closed.
- Job directory names are `j-` plus SHA-256 of the validated internal job ID. Recipient/device names never become paths. Directories admit no attachment payload at this task.
- Hard ceilings are 10,000 jobs, 10,000 evidence records and 10,000 checkpoints per job, 256 evidence records per batch, 64 KiB per normalized JSON payload, 256 MiB each for the database and journal, and 64 matching initialization entries before reconciliation fails closed.
- Archive/restore preserves evidence and appends alternating lifecycle checkpoints. No deletion or retention enforcement is implemented.

## Git state

| Field | Verified value |
|---|---|
| Remote | `origin` → GitHub repository `PikkuJanne/ThirdLife` |
| Branch | `codex/tl-0102-sqlite-job-store` |
| Starting commit | `929f0e34dda02704c32658af9d7b8efc59c44028` — published TL-0101 completion handoff |
| Current commit | `8a4330afd288f4aead03e01bf95c935c4d342785` — published implementation and Sandbox-fix checkpoint; review-evidence commit is being prepared |
| History handling | Started from fetched local/upstream equality; no reset, rebase, force push, or history rewrite |
| Publication state | Implementation checkpoint is pushed and equals its upstream; final review candidate is not yet established |

The configured SSH remote rejects unattended public-key authentication on this machine. Publication uses the governed process-scoped HTTPS `insteadOf` bridge without changing the configured remote or exposing credentials.

The unrelated untracked `ThirdLife_Two-Team_Software_Portfolio_Roadmap_v2.1.docx` predates TL-0102 and remains untouched and unstaged.

## Verification evidence

| Scope | Result | Duration / limitation |
|---|---|---|
| Release build | Passed; 0 warnings/errors | 9.94 s, direct host |
| Formatter | Passed; no changes | 18.956 s, direct host |
| Complete persistence suite | Passed 72/72 twice by implementation audit and once independently | Latest direct-host test duration 23 s |
| Migration/corruption focus | Passed 28/28 | Includes identity, ledger, schema, bounds, concurrent upgrade, and first-publication cases |
| First-run process-kill focus | Passed 4/4 | Before migration, during migration 1, before publish, and after publish |
| Hardened Sandbox RoundTrip | Passed 1/1 | 48.950 s complete / 573 ms tests; networking disabled |
| Hardened Sandbox Targeted | Passed 72/72; 0 failed/skipped | 81.510 s complete / 30 s tests; result SHA-256 `bc04775fae8d28f8841a0fb66d5167fac77fd385ad0c01e43c3b41e55cbfb190` |
| Supply-chain and bundle regressions | Passed 159/159 | 120.969 s |
| Point-in-time NuGet advisory query | Exit 0; 0 vulnerable direct/transitive records | 5.923 s; response SHA-256 `52947476747cce6e5f8919ef06d50ec212c537709525fc3c1c9254460cb38316` |
| Pending-review SBOM | Two byte-identical CycloneDX 1.6 files; 28 components | SHA-256 `228962193bd56ddd0c21abcc290197ff13d650cee51356c6b3160037d0c65c10`; 136,475 bytes |
| Governed Quick | Passed exact checkpoint `8a4330a`; 184/184 | 294.487 s complete / 219.503 s regressions; source SHA-256 `a2bb4d47b1723ef74daf76f68eb9d26fdb88a0ec700ca386baf6e7a83f2b49b6`; result SHA-256 `a5865981761e6fb8a64ce2e1c5d871d7ea0db55a7f83936d5003c3140e3be957`; networking disabled |
| Trigger-required Full | Passed exact checkpoint `8a4330a`; 145/145 solution tests | 471.959 s complete; locked restore/audit, unchanged format, 42.55 s zero-warning/error build, 31 s persistence tests; result SHA-256 `7f9424e23a59ae757e182eed6fcba4fd9effd268de4d06778bf0b4be34e087c5`; networking enabled only for NuGet audit |
| Source-bound SBOM | Two byte-identical CycloneDX 1.6 files; 28 components / 29 dependency records | 15.581 s; 136,597 bytes; SHA-256 `47e46fc7e0c61032a8e3bac74206a01ae22da36c70c5b535586a85d8d68d0ab3`; source `8a4330a`; review pending |
| Extended | Not triggered | TL-0102 declares no Extended trigger; deterministic process-kill/path cases are part of Targeted |

Sandbox Targeted, Quick, and Full used a 4096 MiB same-machine guest, exact lock-derived NuGet packages, bounded source/dependency/history inputs, no host Git configuration/hooks/credentials or broad package cache, a 2 MiB hard in-memory command-output ceiling, an 8 KiB sanitized evidence tail, and a kill-on-close Windows job that verified no descendant remained. Networking was disabled for Targeted and Quick and enabled in Full only for the declared NuGet audit. This evidence is not direct-host policy compatibility, physical power-loss, filesystem-filter coverage, accessibility or modest-hardware certification, or a cross-hardware claim.

## Defect handling

The required reduce–fix–focus–broaden sequence was followed:

1. Job-count, migration-ledger, full archive-history, concurrent migration, Core dependency direction, file-size, journal identity, and path replacement findings received focused tests before the 72-case suite reran.
2. First-run zero/partial publication was replaced with complete temporary initialization plus exact-handle atomic publication; four kill points and repeated orphan-journal rejection passed.
3. An intermittent native rename defect was reduced to missing trailing UTF-16 buffer capacity; the focused 28-case migration set passed, followed by two consecutive 72-case suites.
4. Sandbox review removed the broad NuGet cache, raw output persistence, host `.git` exposure, overwrite evidence, reparse-unsafe cleanup, unbounded staging, polling output cap, and wrapper-only termination.
5. Live Sandbox attempts exposed an empty-output PowerShell 5.1 sum bug, first-use CLIXML version parsing, persistent build-server descendants, and an omitted untracked Core port. Each failed closed, was reduced and corrected, then RoundTrip 1/1 and Targeted 72/72 passed.
6. The first exact-pushed Sandbox Quick stopped during Git-history preflight after tool-version checks. A bounded host reproduction passed archive, bundle, init, fetch, reference binding, reset, and commit verification 6/6. Stable command-stage classifiers reduced the Sandbox failure to a `ParameterBindingValidationException`: successful `git init --quiet` returned an empty bounded output tail that the diagnostic helper's mandatory string rejected. Explicit empty-string admission fixed that diagnostics-only defect; a focused transient Quick passed, followed by exact persisted Quick 184/184 and Full 145/145 at `8a4330a`.

No blind rerun is accepted as evidence. Raw guest output was never retained; only bounded sanitized tails and structured result hashes were used.

Two manually aborted development Sandbox attempts left empty mapped staging-directory shells under the operating-system temporary directory because Windows retained mapping handles after forced shutdown. They contain no result, raw log, package, source, or user payload after verified nonrecursive cleanup of deletable contents. They remain outside the repository for operating-system release/reboot cleanup and are not accepted evidence.

The bounded host history reproduction also left one 4.6 MiB operating-system temporary directory containing only the exact repository source snapshot and Git bundle after this execution environment rejected the verified recursive cleanup command. It contains no package cache, test result, raw command output, credential, personal data, or user payload; it is not task evidence and remains for operating-system temporary-file cleanup.

## Historical TL-0008 transition

The superseded `TL-0008 draft 1` procedure remains preserved only as a historical record at source commit `4fa3ea050fd5e9985fde9cc8218281698d371cc8`, with recorded procedure SHA-256 `ef150dbf14b5db208582b7b526c7e0c6d0a5b912736e9e6519b8918abcf0928b`. No physical hardware walkthrough was performed for that transition, and its former device-pool procedure is not current TL-0102 evidence.

## Supply-chain review state

The inventory now has 28 components: 18 NuGet, four synthetic catalogue rows, three GitHub actions, one PyPI wheel, and two toolchains. Runtime scope adds:

- `Microsoft.Data.Sqlite.Core` 10.0.11 — direct, MIT;
- `SQLitePCLRaw.bundle_winsqlite3` 2.1.11 — direct, Apache-2.0;
- `SQLitePCLRaw.core` 2.1.12 — transitive, Apache-2.0; and
- `SQLitePCLRaw.provider.winsqlite3` 2.1.11 — transitive, Apache-2.0.

The provider uses supported Windows `winsqlite3.dll`; no native SQLite binary is included or admitted for redistribution. The exact current matrix SHA-256 is `e85f3002175dfadc860f0d2c92de0787f52364f491e50a030c936a5421395418`. All four runtime rows remain `not-shipped`, and redistribution/release admission is withheld pending the named review.

The prior TL-0006 approval remains immutable evidence only for its exact 24-component commit/digest. It does not approve the four TL-0102 runtime components.

## Boundary and risk impact

- **Project vacuum / sibling integration:** No sibling repository, source, runtime, data, service, profile, adapter, schema, SDK, framework, dependency, or acceptance test was introduced.
- **Data / migration:** Adds the registered local SQLite database, persistent rollback journal, restrictive empty per-job directories, schema versions 1–2, append-only typed evidence/checkpoints, and reversible archive projection. Deletion, retention enforcement, backup/export, incompatible migration recovery, attachments, and uninstall cleanup remain later work.
- **Release interface:** No release compatibility promise or guessed release behavior was added to `RELEASE_INTERFACE.md`.
- **Security / privacy:** Normalized typed Core payloads only; no raw command/provider output, recipient name, username, personal path, credential, recovery material, telemetry, or sibling data is admitted. The verified-journal startup interval remains an explicit same-user/local-administrator residual; no custom SQLite VFS is introduced.
- **Accessibility / modest hardware:** No UI or accessibility journey changes. Operations are serialized through one connection gate; counts, payloads, files, staging, output, and initialization residue are bounded. No GPU, resident service, background index, performance budget, modest-hardware certification, or cross-hardware claim is added.

## Outstanding and next steps

1. Validate this final automated-evidence update, commit/push it, and verify the exact review-candidate commit against upstream.
2. Obtain Janne Vuorela's named approval of that candidate commit and matrix SHA-256 `e85f3002175dfadc860f0d2c92de0787f52364f491e50a030c936a5421395418`, preserving every documented limitation and withheld right, plus explicit authorization to append renewed TL-0006 evidence without altering prior evidence.
3. Record the approval, rerun the approval-bound governance checks and Quick tier, set `TL-0102` to `done`, commit/push, and verify upstream equality.

## Upcoming decisions

- **Required now:** after the review-candidate push, approve or reject that exact commit and the 28-component licence/installation/redistribution proposal at matrix SHA-256 `e85f3002175dfadc860f0d2c92de0787f52364f491e50a030c936a5421395418`. Approval is governance of the recorded proposals only; it grants no blanket redistribution, native-SQLite redistribution, release authorization, legal advice, or removal of any limitation.
- **Later tasks, not TL-0102:** retention/deletion policy enforcement, backup/export and incompatible-migration recovery, typed attachment admission, uninstall cleanup, production packaging/notices, and release signing/lifecycle.

## Next dependency-ready task

After `TL-0102` is fully approved and `done`: `TL-0103` — Implement job lifecycle and sanitization gate services. Do not start it before TL-0102 completion.
