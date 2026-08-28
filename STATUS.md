# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-08-28  
**Snapshot preparation time:** 2026-08-28T14:37:30+02:00  
**Bundle baseline:** 0.3.1  
**Portfolio baseline:** ThirdLife Software Portfolio v2.1  
**Current milestone:** M1 — Audit-only vertical slice — active  
**Current task:** `TL-0104` — Implement structured logging and redaction — `in_progress`  
**Action state:** implementation and adversarial hardening are complete; final current-source Sandbox verification, evidence binding, commit, and publication remain

## Executive state

M0 is complete. `TL-0101`, `TL-0102`, and `TL-0103` are done. `TL-0104` now has a production diagnostics candidate for the approved local structured-logging/redaction subset: a closed typed event schema, explicit sensitive-value wrappers, sanitization before persistence/display, protected bounded local records, deterministic execution of the exact synthetic privacy fixture, and an internal exact 25-field support projection.

The design is intentionally narrower than a conventional logger. Callers cannot provide free-form messages, arbitrary objects/maps, raw provider or command output, ordinary exception text, or caller-selected crash correlations. Secrets, personal content, raw outputs, and sibling-private fields cannot be constructed through the public wrapper. There is no telemetry SDK, uploader, account requirement, new network path, SQLite integration, or privileged service.

Local sanitized logs use canonical whole-record JSON files under the registered current-user application-data root. The store enforces a maximum 14-day retention age and configured byte ceiling, restrictive ACLs, bounded cross-process locking and queues, exact schema/name validation, identity/link/reparse/path checks, and verified narrow cleanup. Append removes verified prior orphan temporaries before staging, then uses a durable `.txn-*` intent before evicting final events. Restart recovery validates and completes an in-window intent, or deletes a verified intent already outside retention, while final `evt-*` records stay within the configured ceiling.

The latest completed governed transient Targeted run passed 120/120 Diagnostics tests offline in same-machine Windows Sandbox. Later review hardening added fresh crash-correlation generation, pending-transaction wrong-clock checks, pre-mutation temporary validation, immediate committed-intent digest revalidation, and focused regressions. Current source builds with zero warnings/errors, but its final Sandbox rerun is still required before `done`.

No support preview/archive/export, upload, production digest provenance, database logging, release authorization, cross-platform production claim, or cross-hardware certification is created by TL-0104.

## Implemented behavior

- `StructuredDiagnosticEvent` admits only registered event/component/phase/severity envelopes, stable typed fields, and canonical records bounded to 16 KiB.
- `SensitiveDiagnosticValue` accepts only classified person/device/network/path families and never stringifies or serializes its raw value; excluded classes cannot be constructed.
- The engine executes all 56 exact synthetic `RDX-001`–`RDX-056` cases, including Unicode, separator, casing, nesting, markup/formula, URL, path, network, identifier, raw-output, secret, and sibling-private adversarial values.
- Exception handling never reads `Message`, `ToString()`, stack, source, `Data`, or inner exceptions. Each public crash call creates a fresh opaque correlation and returns a stable sanitized result even when writing is cancelled, unavailable because pre-write cleanup cannot complete, or durability-ambiguous after commit.
- The store rejects expired or more-than-five-minutes-future incoming records before root creation. Bounds are 4,096 non-lock root entries, 64 pending operations, 16 KiB per record, and at most 256 MiB configured final-record bytes.
- Cross-instance/process writes share one protected bounded lock. Tests cover concurrent child writers, killed lock holders, full disk, cancellation, process death, corrupt/noncanonical data, wrong clocks, invalid names, ACL widening, hard links, reparse points, path replacement, and root exhaustion.
- Append stages `.tmp-*`, commits one `.txn-*`, revalidates the exact intent immediately before retention mutation, evicts only verified finals, and publishes by no-overwrite rename. Post-commit uncertainty is recovery-pending, never safe to blind-retry.
- Recovery preflights temporary/reparse objects and validates timestamp, canonical bytes, expected final path, length, and digest before eviction. Detected changes preserve available prior state and the suspicious object for review; an exact expired intent is deleted and reported instead of published or allowed to brick the store.
- Windows deletion uses the validated open file handle. The portable fallback is path-bound after revalidation and supplies no non-Windows production or equivalent deletion-race assurance.
- `thirdlife.diagnostics.event.v1` is treated as persisted compatibility; a literal prior-build record is reopened and aged out.
- The internal support projection contains exactly the approved 25 fields and only exact synthetic registered values. Production preview-byte provenance and export remain assigned to `TL-0606`.
- The production assembly has no Persistence/SQLite, `System.Net.*`, OpenTelemetry, Application Insights, uploader, or background-worker dependency.

## Git state

| Field | Verified value |
|---|---|
| Remote | `origin` → GitHub repository `PikkuJanne/ThirdLife` |
| Branch | `codex/tl-0104-structured-logging` |
| Starting commit | `26c6b5004c7eab6e067897c8988c85deb3499db2` — published TL-0103 handoff |
| Candidate commit | Pending final review and current-source verification |
| History handling | Started from fetched local/upstream equality; no reset, rebase, force push, or history rewrite |
| Publication state | Not yet published; final upstream equality remains required |

The SSH remote rejects unattended authentication here. Publication uses the governed process-scoped HTTPS `insteadOf` bridge without changing the configured remote or exposing credentials.

The unrelated untracked `ThirdLife_Two-Team_Software_Portfolio_Roadmap_v2.1.docx` predates TL-0104 and remains untouched and unstaged.

## Verification evidence

| Scope | Result | Duration / limitation |
|---|---|---|
| Pre-change Diagnostics baseline | Passed 1/1 | 6.275 s on protected host |
| Current Release build | Passed; 0 warnings/errors | Direct host; changed unsigned testhost runtime remains Application-Control constrained |
| Current formatter | Passed strict verify-no-changes | Direct host |
| Bundle contract validator | Passed; 91 tasks, 8 milestones, 66 frozen decisions, valid DAG | Approved TL-0005 contract files remain byte-for-byte unchanged |
| Sandbox harness regression | Passed 3/3; PowerShell AST parsing passed | TL-0102/TL-0103 compatibility retained |
| Latest governed transient Targeted | Passed 120/120; 0 failed/skipped | 51.341 s complete / 21 s tests; source SHA-256 `6d96d7613b0481864dd30a7c2ff99636cd8e2c18c5a05f07ca5ec002d6702535`; result SHA-256 `60efca28d0ff6caa86ced4aa69cb1707b185c279e29020d3f473a7dc9516edae`; offline Sandbox |
| Final current-source Targeted | Pending | Required because final security hardening followed the transient run |
| Final governed Quick | Pending | Run after task/status/manifest synchronization |
| Full | Not triggered | No dependency, migration, privilege, package/update, installer/lifecycle, backup, release, or broad shared-boundary change |
| Extended | Not triggered | Crash, concurrency, full-disk, ACL/path, fuzz, and bounds cases are independently invokable within Targeted |

Targeted uses a disposable 4,096 MiB Windows Sandbox on the active physical machine, exact .NET SDK 10.0.400 and Git 2.55.0.windows.5, lock-derived offline NuGet inputs, networking disabled, bounded output, and verified descendant/staging cleanup. It is not direct-host policy compatibility, physical power-loss proof, filesystem-filter coverage, cross-platform assurance, modest-hardware certification, or a cross-hardware claim.

## Defect handling

1. A post-publication one-record byte overage was replaced with recoverable `.tmp` → `.txn` → retention → `evt` semantics and real child-process crash tests before cleanup and after eviction.
2. Cross-process proof now includes synchronized child writers and process death while holding the lock.
3. Canonical/identity coverage includes invalid UTF-8, order/whitespace drift, duplicate/unknown keys, message mismatch, filename/timestamp mismatch, ACL widening, links/reparse points, and path replacement.
4. Deterministic full-disk faults before temporary creation and after writing but before flush prove stable sanitized failure, no partial final, bounded residue, and safe next cleanup.
5. File/queue/wait bounds are exact and fail closed.
6. Public crash logging no longer accepts caller correlation; two calls prove fresh distinct IDs.
7. Invalid temporary and future-clock committed state is rejected before recovery eviction. An exact committed intent already outside retention is safely deleted and reported instead of being published or blocking all later operations.
8. Verified orphan temporaries are removed before the next append stages bytes; repeated real pre-commit child-process crashes prove residue remains at one latest temporary rather than growing per restart.
9. Live append and restart recovery revalidate committed intent immediately before retention mutation; same-length canonical live substitution preserves the seed and reports ambiguity. Local current-user/administrator files are not claimed tamper-proof.
10. The approved TL-0005 privacy files were briefly annotated; validation rejected the exact-commit change, so both were restored byte-for-byte and implementation status remains in non-contract records.
11. Direct testhost reruns were not blindly repeated after Application Control `0x800711C7`; governed runtime assertions use the approved same-machine Sandbox without weakening host security.

## Boundary and risk impact

- **Project vacuum / sibling:** no sibling source, data, runtime, adapter, dependency, or test.
- **Data / migration:** only protected local sanitized-log files; no SQLite/job migration, attachment, report, or export.
- **Release interface:** unchanged; no compatibility promise.
- **Dependency / licence:** no package, toolchain, or matrix change.
- **Security / privacy:** redact before first write, no raw/free payload API, bounded recovery. Current-user/admin tamper resistance and export controls are not claimed.
- **Accessibility / modest hardware:** no UI change; local serialized work with explicit value/record/byte/file/queue/wait bounds; no GPU, resident service, background indexing/upload, or hardware certification.

## Historical TL-0008 transition

The superseded `TL-0008 draft 1` procedure remains preserved only as a historical record at source commit `4fa3ea050fd5e9985fde9cc8218281698d371cc8`, with procedure SHA-256 `ef150dbf14b5db208582b7b526c7e0c6d0a5b912736e9e6519b8918abcf0928b`. No physical hardware walkthrough was performed for that transition; its former device-pool procedure is not current evidence.

## Outstanding and next steps

1. Finish independent contract review against the latest committed-intent and crash-correlation changes.
2. Run the complete Diagnostics Targeted suite against current source in offline same-machine Windows Sandbox.
3. Synchronize `TASKS.yaml`, this snapshot, and `BUNDLE_MANIFEST.sha256`; run final governed Quick.
4. Commit, push, verify upstream equality, and mark TL-0104 `done` only if all current-source gates remain green.

## Upcoming decisions

- **Closed event surface — decided:** diagnostics are an allowlisted contract, not a general logging channel. Future subsystems cannot add free-form fallback text; a missing field/code requires bounded design and privacy/threat review.
- **Durable retention — decided:** final-record byte limits take priority over publication-before-cleanup. Durable intent enables restart recovery and truthful ambiguity without blind retry, but does not prove physical-power-loss behavior or tamper-proof local evidence.
- **Crash correlation — decided:** the logger creates a fresh opaque ID per crash. This prevents accidental cross-crash linkage; future attributable linkage would require a separate privacy-minimized proposal.
- **Support provenance — deferred to TL-0606:** exact synthetic-only construction proves schema/redaction mechanics without treating arbitrary digests as approved export bytes. TL-0606 must bind preview bytes, manifest, approval, destination, and production digests before export exists.
- **Platform assurance — decided for current scope:** Windows receives handle-bound deletion proof. A non-Windows target would need its own identity, permission, link, atomicity, and deletion-race contract.
- **Verification tier — pending evidence, not scope:** Targeted contains the relevant scenarios. Full remains untriggered unless a later finding crosses dependency, migration, privilege, package/update, installer/lifecycle, backup, release, or broad shared boundaries.

## Next dependency-ready task

After TL-0104 is complete and published, the lowest dependency-ready task is `TL-0105` — Define inventory provider contracts and evidence normalization. Do not start it in this session.
