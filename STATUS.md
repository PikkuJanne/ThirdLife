# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-08-28  
**Snapshot preparation time:** 2026-08-28T20:30:07+02:00  
**Bundle baseline:** 0.3.1  
**Portfolio baseline:** ThirdLife Software Portfolio v2.1  
**Current milestone:** M1 — Audit-only vertical slice — active  
**Current task:** `TL-0105` — Define inventory provider contracts and evidence normalization — `in_progress`  
**Action state:** repository synchronization, dependency verification, binding-contract review, and the pre-change Inventory baseline are complete; contract implementation and Targeted verification remain

## Executive state

M0 is complete. `TL-0101`, `TL-0102`, `TL-0103`, and `TL-0104` are done. `TL-0105` is selected and in progress from the exact published TL-0104 completion. Its scope is a read-only, cancellable, timeout-bounded, platform-independent provider contract and evidence-normalization layer that preserves source-specific uncertainty and converts provider failure into explicit not-available evidence plus a sanitized error rather than a pass.

TL-0105 has no Windows provider implementation scope: device, OS, storage, battery, firmware/security, and network adapters remain assigned to `TL-0106`–`TL-0111`. This task defines the common boundary those providers must obey and supplies deterministic fakes and the independently invokable provider-unavailable scenario without querying or modifying the active machine.

The design is intentionally narrower than a conventional logger. Callers cannot provide free-form messages, arbitrary objects/maps, raw provider or command output, ordinary exception text, or caller-selected crash correlations. Secrets, personal content, raw outputs, and sibling-private fields cannot be constructed through the public wrapper. There is no telemetry SDK, uploader, account requirement, new network path, SQLite integration, or privileged service.

Local sanitized logs use canonical whole-record JSON files under the registered current-user application-data root. The store enforces a maximum 14-day retention age and configured byte ceiling, restrictive ACLs, bounded cross-process locking and queues, exact schema/name validation, identity/link/reparse/path checks, and verified narrow cleanup. Append removes verified prior orphan temporaries before staging, then uses a durable `.txn-*` intent before evicting final events. Restart recovery validates and completes an in-window intent, or deletes a verified intent already outside retention, while final `evt-*` records stay within the configured ceiling.

The exact clean and pushed implementation checkpoint `37e912386a3a54f1896c8bd4a9a2919c51e677d0` passed 127/127 Diagnostics tests offline in same-machine Windows Sandbox. The synchronized review checkpoint `e415262e8377b78329ef2fdb38e0401127838cac` then passed 187/187 governed Quick regressions plus the bundle and repository validators. Both gates kept networking disabled, retained no raw output, proved source unchanged after execution, and left no Sandbox process or staging directory.

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
| Branch | `codex/tl-0105-provider-contracts` |
| Starting commit | `b98b03698f9615340ae755796c6abbe2b0456f91` — published TL-0104 completion |
| Candidate commit | Pending implementation and current-source verification |
| History handling | Started from fetched TL-0104 local/upstream equality; no reset, rebase, force push, or history rewrite |
| Publication state | New task branch created locally; first governed checkpoint and upstream remain pending |

The SSH remote rejects unattended authentication here. Publication uses the governed process-scoped HTTPS `insteadOf` bridge without changing the configured remote or exposing credentials.

The unrelated untracked `ThirdLife_Two-Team_Software_Portfolio_Roadmap_v2.1.docx` predates TL-0104 and remains untouched and unstaged.

## Verification evidence

| Scope | Result | Duration / limitation |
|---|---|---|
| Pre-change Inventory baseline | Passed 1/1 | 362 ms reported test duration; 7.724 s command; existing assembly scaffold only |
| Pre-change Diagnostics baseline | Passed 1/1 | 6.275 s on protected host |
| Current Diagnostics host suite | Passed 127/127; 0 failed/skipped | 35 s on the protected host after the focused accounting correction |
| Current Release build | Passed; 0 warnings/errors | 5.15 s direct host; protected host security controls unchanged |
| Current formatter | Passed strict verify-no-changes | Direct host |
| Bundle contract validator | Passed; 91 tasks, 8 milestones, 66 frozen decisions, valid DAG | Approved TL-0005 contract files remain byte-for-byte unchanged |
| Sandbox harness regression | Passed 3/3; PowerShell AST parsing passed | TL-0102/TL-0103 compatibility retained |
| Final current-source Targeted | Passed 127/127; 0 failed/skipped | 59.567 s complete / 30 s tests; result SHA-256 `693e8b9e549f2afe0ad33c5660641aad18d31628e4f6a423cab89bf7d13da568`; offline Sandbox; exact clean/pushed `37e9123` |
| Final governed Quick | Passed 187/187; bundle and repository validators passed | 214.322 s complete / 161.833 s regressions; result SHA-256 `c8dc8fd248682a48b84402d9a5c49e82d66d4d1d2439e937a881e3f5d3cf4b0a`; exact clean/pushed `e415262` |
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
11. An earlier changed testhost launch hit Application Control `0x800711C7`; no host policy was weakened or bypassed. A later final protected-host suite ran 127/127, while authoritative source-bound runtime evidence remains the governed same-machine Sandbox result.
12. The first persisted final Targeted attempt passed 126 cases but exposed one cleanup-result composition assertion: successful expired-transaction deletion was performed, but its removed-file count was overwritten when later cleanup results were combined. Commit `37e9123` preserves both counts; the focused case, all 127 host cases, and the exact persisted Sandbox rerun passed. The failed result and manifest remain append-only with SHA-256 `29385cb27281ce29e5709f5096d3df437d0cdafaee3a9bf20d039e6a23d01b9a` and `7ac3b6908159a8b0fac8e2a0245e74bdbf8f01662aa94034b932f1412b6ed5b9`.

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

1. Define bounded provider identity, privilege, duration/timeout, network-use, supported-OS, evidence-key, cancellation, and sanitized-error contracts.
2. Implement deterministic normalization that maps success, unavailable, failure, timeout, and cancellation without inventing a pass or retaining raw exception/provider output.
3. Add fake-provider contract tests, the independently invokable `FI-001` / `SMC-PROVIDER-UNAVAILABLE` filter, read-only architecture checks, and current-source Targeted evidence.
4. Synchronize task/status/threat records, publish checkpoints, and mark TL-0105 `done` only after every acceptance criterion and the final governed Quick pass.

## Upcoming decisions

- **Closed event surface — decided and binding:** diagnostics are an allowlisted data contract, not a general logging channel. This choice reduces accidental disclosure at the API boundary because callers cannot fall back to free-form text, arbitrary objects, ordinary exception messages, or raw provider output. Its operational implication is that each future diagnostic field or event code must be intentionally designed, bounded, classified, tested, and reviewed; convenience alone is not sufficient reason to broaden the surface.
- **Durable retention ordering — decided and binding:** the store commits a recoverable intent before it evicts final records, and final-record age/byte ceilings take priority over immediate publication. This permits deterministic restart recovery and truthful `recovery pending` outcomes without blind retry. It does not mean physical-power-loss behavior is fully proven, local administrator changes are tamper-proof, or every filesystem filter behaves identically; those remain explicit assurance limits.
- **Crash correlation ownership — decided and binding:** the logger creates a fresh opaque correlation for every public crash call rather than accepting a caller-selected identifier. This prevents accidental linkage of separate crashes and blocks callers from smuggling identity through a correlation field. If later support workflows need durable cross-event linkage, they require a separate privacy-minimized design with purpose, retention, access, and disclosure controls.
- **Support provenance and export — intentionally deferred to `TL-0606`:** TL-0104 proves the exact 25-field schema and redaction mechanics only with registered synthetic values. It does not make arbitrary digests trustworthy, create a preview, package an archive, select a destination, or authorize transfer. TL-0606 must bind the exact preview bytes, manifest, approval, destination, and production digests before any support export can exist.
- **Platform assurance — Windows-only for the proved deletion race:** Windows uses handle-bound deletion after identity and link checks. The portable fallback is path-bound and is not an equivalent non-Windows production guarantee. Supporting another platform would therefore be a new security decision requiring native permission, identity, link, atomicity, replacement, and deletion-race evidence rather than a simple target-framework change.
- **Verification scope — decided and evidenced:** task-expected Targeted covers the changed diagnostics privacy, filesystem, recovery, concurrency, and resource risks; synchronized Quick proves the governance bundle still agrees. Full and Extended were not triggered because TL-0104 changed no dependency, migration, privilege, package/update, installer/lifecycle, backup, release, or broad shared boundary. A later change crossing one of those boundaries must select the broader tier at that time.
- **Next provider-contract decision — upcoming in `TL-0105`:** provider APIs must expose privilege, duration, network use, supported OS, cancellation, timeout, and typed unavailable/error outcomes while retaining source-specific uncertainty. TL-0105 must decide the smallest normalized evidence vocabulary that lets policy consume observations without converting missing, stale, conflicting, or weak evidence into a pass.

## Next dependency-ready task

After TL-0105 is complete and published, the lowest dependency-ready task will be `TL-0106` — Implement device identity, CPU, memory, and architecture inventory. Do not start it in this session.
