# TL-0108 — Storage inventory and reliability evidence

**Date:** 2026-09-30  
**Task state:** Done; default-branch integration pending normal PR  
**Branch:** `codex/tl-0108-storage-reliability`  
**Base:** `c960e03bc17bc90287f2ab8d8b4f865bce4e0298`  
**Runtime authority:** active Codex machine only; remote CI is supplementary

## Continuity

PR #8 merged TL-0107 into main on 2026-09-30 at 14:28:41 UTC. Its reported head
`344e2a11b9e570cfd754de8a388efb22c6938f33` is an ancestor of the new main, with no unique
commits and one merge commit ahead. Local main fast-forwarded before this task branch was
created. All TL-0108 transitive dependencies are done. The session read the binding documents,
provider contracts, relevant evidence/privacy ADRs, existing tests, fixtures and prior evidence.

The documented process-scoped HTTPS bridge synchronized the unchanged SSH origin. No branch
was deleted, superseded PR closed, history rewritten, or completed-task stack created. The
pre-existing untracked Word roadmap reference remains untouched and excluded. No sibling
repository was accessed. TL-0107's supplementary final CI run was cancelled before its merge;
its active-machine evidence remains separate and is not reused as this task's baseline.

## Baseline

The narrow Inventory `ProviderContractTests` baseline passed 20/20 in 10.456 s, with no
pre-existing failure in that run. Exact command and duration are in `runs.json`. The prior
task's Application Control limitation remains relevant, but no security setting or test
configuration was changed in anticipation of a failure.

## Implemented contract and acceptance

| Criterion | Implementation and proof |
| --- | --- |
| Explicit capacity/free-space units | Physical size and three independent quota-aware volume values use bytes; quota inequalities and unsigned overflow covered by deterministic tests. |
| Missing health/wear remains unavailable | Missing, unknown, reserved, denied and malformed source values preserve unknown/Not available; no healthy fallback. |
| Critical errors retained without diagnosis | Reported unhealthy state, raw bounded operational codes, independent error counts, wear above 100 and per-scope numeric errors survive unrelated missing fields. No score, remaining-life estimate or readiness conclusion. |
| Multiple disks and system volume separate | Four physical-disk slots, fixed volume keys, explicit WMI association ownership and deterministic route ordering; duplicate/orphan/shared associations fail closed. No volume-to-disk inference. |
| Read only | Exact reviewed DLL imports and COM GUID/vtable/signature allowlist; fixed projections/local namespace; no mutation methods, dynamic dispatch, shell, arbitrary paths, disk content, repair, optimization, SMART reset or write test. |
| Required variants | Wholly synthetic HDD, SATA SSD, NVMe, multiple disks, missing counters, denied/malformed/provider failures, quotas, native types, boundaries, cancellation and retry in the two storage test files. |
| Active-machine smoke | Standard-user Windows smoke passed with physical scope AccessDenied and three volume capacity/free fields available; minimized availability record in `active-storage.json`. |

The provider fits the existing 64-observation maximum: four disks times fourteen fields,
seven singleton facts, and one failure observation. Routing strings are limited to 2,048
characters, operational arrays to 16 values and related WMI rows to eight. A fifth disk
invalidates the whole physical enumeration instead of claiming the first four are complete.

The source reads `MSFT_PhysicalDisk`, `MSFT_StorageReliabilityCounter`, their documented
association, and system-volume metadata. Local routing identifiers are transient and never
leave the source snapshot. Reliability queries stop after the first failing collection step;
no second error is collected and then dropped. Each normalized key has one semantic owner.

## Verification

Exact commands, durations and failed-attempt records are in `runs.json`.

| Check | Result |
| --- | --- |
| Narrow baseline | 20 provider-contract tests passed, 10.456 s. |
| Final focused regression set | 109 storage/native-boundary tests passed, 11.047 s. |
| Targeted, including Quick | Passed in 43.281 s: bundle and repository controls, format, 26-project warnings-as-errors build, 79 Core tests and 329 Inventory tests; Inventory repeats as the explicitly selected project. No failures or skips. |
| Standalone final Quick | Passed in 35.117 s, including 79 Core and 329 Inventory tests and all required controls. |
| Final active storage smoke | One test passed, command 1.881 s; physical scope denied and system-volume facts available. |
| Independent source/privacy/acceptance review | No remaining blocker; reviewed COM slots, bounded parsing, exact association ownership, cleanup, scoped errors and all task criteria. |
| Full / Extended | Not triggered. No dependency, engine, migration, major refactor, protected merge, milestone or named scenario trigger. This is a normal unmerged feature branch. |

The final standalone Quick check is recorded in `runs.json`. Remote CI is
supplementary and cannot replace these active-machine results. No clean-environment gate or
physical storage stress test was triggered.

### Defects and corrections

The first focused build failed before test execution on four analyzer rule classes: token
parameter ordering, throwing from finally, StringBuilder P/Invoke buffers, and a concrete
array parameter type. Corrections preserved cleanup and used fixed character buffers; no
rule was disabled. The focused subset then passed 108 tests. Final review added deadline
checks between property reads and a malformed SAFEARRAY element-size regression; the final
focused subset passed 109.

The first Targeted attempt reached formatting and stopped on whitespace/line-ending issues
in new files. Scoped `dotnet format` corrected only task code. The related focused subset
ran before the passing Targeted run. No failed test was skipped or quarantined.

Windows PowerShell refused to launch the verification script under its existing execution
policy. Installed PowerShell 7 (`pwsh`) ran it under existing configuration. No security or
execution-policy setting was changed. The prior TL-0107 Application Control failure did not
recur in this task's verified runs; that prior limitation is not claimed resolved generally.

The first successful active smoke used a logger that omitted successful-test output. One
detailed capture recorded the required metrics; the smoke repeated after final source
hardening. Both earlier successful runs remain in `runs.json`.

## Active-machine and resource evidence

Environment: `REF-CODEX-001` revision `2026-08-29.1`, Windows 11 x64, unelevated process,
.NET SDK 10.0.400 / runtime 10.0.11. No physical machine other than the active Codex host,
resource constraint, network disruption, security weakening or elevation was used.

Final measured provider call: **108.826 ms elapsed**, **62.500 ms CPU**, **77,824 bytes
private-memory growth**, **47 handles growth**, **22 normalized observations**. Assertions
bounded elapsed/CPU to eight seconds, private growth to 128 MiB and handle growth to 256.
The measurement ends at runner return; it is not a worst-case cleanup or low-spec benchmark.

Physical count, disk capacity, media/bus, health, operational codes, wear, temperature, error
counts and operating time were unavailable because physical WMI access was denied. The
numeric source error remained present. System-volume caller capacity, caller free space and
total free space were available. Exact capacity/free values, identifiers, paths and raw
provider output are not in the public evidence artifact. This task proves the denied path
on this host and successful represented data through fixtures; it does not claim a real
counter capture, physical health or broad Windows-provider compatibility.

The runner has a five-second caller timeout. Native work has a six-second between-call
budget and finite enumeration polling, but an individual COM call cannot be interrupted.
The ConnectServer maximum-wait flag has its Windows-defined limit. One in-flight guard stays
occupied until native cleanup returns; late evidence is discarded. Native WMI may allocate
before validation; retained managed rows, strings and arrays are bounded. Future orchestration
must reuse provider instances and bound batch concurrency.

## Privacy, architecture, accessibility and boundaries

- No new dependency, schema, migration, persistence, cache, temp file, report/export field,
  telemetry, network request, broker capability, privilege elevation or permanent service.
- Domain dependency direction remains unchanged. The Windows source is internal; the public
  provider accepts only a cancellation token and exposes a fixed descriptor.
- No UI/control/focus/keyboard/scaling/high-contrast change or human accessibility claim.
  Standard typed failures, cancellation, bounded work and recovery remain usable by later UI.
- No GPU requirement, background polling, automatic retry, indexing or disk write. No
  cross-hardware, minimum-spec, media-performance or reliability certification.
- `CRM-003`, `SECURITY.md`, `LOW_SPEC.md` and the storage contract now describe this scope.
  `RELEASE_INTERFACE.md` is unchanged because no release behavior is introduced.
- No sibling access or B4 work. No task contract, dependency or frozen decision changed.

## Durable inputs and handoff

`inputs.sha256` pins the exact checked-out bytes of source, synthetic test inputs, toolchain,
runner and reference-profile files. Its SHA-256 is
`5af7180a635d10f186cbe23788538c4cb8c02902ea580c714d823520fdc90125`.
These fixtures are versioned by this task's implementation commit; their values are synthetic.
Single-case invocation uses the exact test name through `dotnet test --filter
FullyQualifiedName~<method>`; the complete storage subset command is in `runs.json`.

Only compact evidence is added to `TASKS.yaml`; this report, run records, input digests and
availability projection hold the details. No raw hardware/test log is committed.

Publish this task through one normal PR into main. Integration must finish before starting
`TL-0109` — Implement battery report generation and parsing. No later task has started.
