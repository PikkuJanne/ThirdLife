# ThirdLife Setup Core — Same-machine constraint procedures

**Status:** Active specification; defined but not executed at revised `TL-0008`  
**Revision:** TL-0008 same-machine revision 2  
**Physical hardware scope:** Active Codex machine only  
**Current execution state:** All `SMC-*` profiles are `Not run`; commands, workloads, numerical budgets, and results remain owning-task placeholders.

## 1. Purpose and claim boundary

These procedures apply safe, bounded, reversible conditions on the active Codex machine or a disposable environment it hosts. Each profile answers one concrete product risk. None is a substitute for a second computer, a lower-performance machine, a hardware lab, a volunteer device pool, or cross-hardware certification.

A same-machine constraint proves only the recorded build, active reference-machine profile, hosted environment, exact settings, fixture/workload, and observation window. It does not simulate a particular processor, RAM size, storage device, network, GPU, manufacturer, firmware, or minimum specification.

Revised `TL-0008` defines the profiles and later triggers only. It does not apply a constraint, run a resource matrix, disrupt the active machine, or create benchmark evidence.

## 2. Common safety and invocation contract

Before any later constraint run:

1. confirm that the owning task or gate names the risk and selected tier;
2. update/reference the sanitized active-machine profile in `reference-machine-profile.md`;
3. use a synthetic, deterministic, bounded fixture/workload with version, SHA-256, expected result, and privacy review;
4. record exact supported product/test settings and an independently invokable command;
5. prefer an injected provider, fake adapter, isolated workspace, disposable virtual disk, VM, Windows Sandbox session, or container over host mutation;
6. define baseline/comparison method, bounds, timeout, cancellation/checkpoint behavior, abort rule, cleanup/restoration, and residue check;
7. preflight free space and preserve host/recovery headroom; and
8. stop if the procedure would affect donor/personal data, firmware, TPM, Secure Boot, activation, ownership/management controls, host safety, or an unreviewed mutation boundary.

Never fill the host system volume, globally disrupt the active machine's network, disable host security/privacy/accessibility controls, use unknown removable media, run destructive stress, or retain raw machine-identifying logs. A placeholder is not a command and must not be executed.

Every later command must support the single named scenario. A broad runner is insufficient unless it also exposes an exact scenario filter.

## 3. Profile selection and tiers

| Profile | Concrete risk answered | Default tier | Earliest explicit trigger | Related coverage |
|---|---|---|---|---|
| `SMC-BASELINE` | Establish a comparable normal run for a resource-sensitive workload. | Targeted when measurement is needed; Full at named gate. | First owning performance/resource task; gate requiring a baseline. | `CRM-002`, `CRM-003`, `CRM-009` |
| `SMC-NO-GPU` | Hidden accelerator dependency or inaccessible CPU fallback. | Targeted; Extended for gate/resource claim. | Relevant UI/processing implementation; `TL-0510` when named. | `CRM-009`, `A11Y-009` |
| `SMC-CONSERVATIVE-CONCURRENCY` | Unbounded workers, races, or memory/I/O amplification. | Targeted; Extended for large workload. | Changed concurrency boundary; `TL-0510`. | `CRM-009`, `FI-003` |
| `SMC-LOW-PRIORITY` | Loss of responsiveness/progress/cancellation under scheduling pressure. | Extended only when named. | `TL-0510` or later gate naming scheduling risk. | `CRM-009`, `A11Y-004`, `A11Y-009` |
| `SMC-LOW-FREE-SPACE` | Unsafe mutation, missing rollback headroom, corrupt partial output. | Targeted for preflight; Extended for mid-write risk. | Storage-boundary task; `TL-0503`/`TL-0606` when risk applies. | `CRM-007`, `FI-003` |
| `SMC-OFFLINE` | Core becomes unusable or network-dependent work reports false success. | Targeted. | Network-dependent action at `TL-0405` or update at `TL-0504`. | `CRM-008`, `FI-001` |
| `SMC-INTERRUPTED-NETWORK` | Partial transfer executes, checkpoint is unsafe, or stale approval survives. | Extended only when interruption risk is named. | `TL-0405`/`TL-0408` or later gate trigger. | `CRM-008`, `FI-002` |
| `SMC-PROVIDER-UNAVAILABLE` | Missing/access-denied/malformed provider evidence becomes pass. | Targeted. | Each provider implementation/change beginning at `TL-0105`. | `CRM-001`–`CRM-005`, `FI-001` |
| `SMC-SLOW-DESTINATION` | Unbounded buffering, timeout, inaccessible cancel, corrupt partial/final output. | Extended only when slow-write risk is named. | Resource/failure matrix `TL-0510` or export/lifecycle task trigger. | `CRM-003`, `CRM-009`, `FI-003`, `FI-010`, `A11Y-004`, `A11Y-009` |
| `SMC-LARGE-WORKLOAD` | Memory/temp/cache/database/output growth is unbounded or work cannot resume. | Extended only when large-workload risk/gate is named. | `TL-0510` or later pilot/stable gate. | `CRM-002`, `CRM-009`, `FI-003`, `A11Y-009` |

The quick tier validates these definitions and safe wording only. Revised `TL-0008` has no targeted, full, or extended constraint trigger.

## 4. `SMC-BASELINE`

- **Purpose:** Establish a normal comparison run for one versioned workload; not a universal benchmark.
- **Prerequisites:** Implemented independently invokable workload, idle/stabilization rule, reference-profile revision, measurement tool/version, bounded repetition rule, and sufficient disk/recovery headroom.
- **Safe setup:** Use the supported default product configuration in an isolated synthetic workspace. Record relevant background activity and pending restart state without capturing raw logs.
- **Invocation state:** Not implemented at TL-0008; the named owning task must supply exact workload command before invocation.
- **Expected invariant:** Correct deterministic output, truthful journal/result, accessible progress/cancellation, bounded resources, and complete cleanup.
- **Abort:** Wrong workspace/build/fixture, insufficient headroom, unexpected host mutation, unsafe temperature/power state, lost cancellation/recovery, or prohibited data appears.
- **Cleanup/restoration:** Remove only the verified disposable workspace; inspect temp/cache/database/output residue; restore any task-specific reversible setting.
- **Claim limitation/status:** Comparison applies only to recorded run; command/budget/result are not implemented at TL-0008; the named owning task must supply the item before invocation; `Not run`.

## 5. `SMC-NO-GPU`

- **Purpose:** Prove the essential workflow has a supported CPU path and does not require graphics acceleration.
- **Prerequisites:** Reviewed product/test configuration that disables only optional product acceleration; do not disable Windows security, accessibility, or unrelated host graphics controls.
- **Safe setup:** Use an isolated synthetic workload and record direct/hosted environment plus graphics-acceleration state.
- **Invocation state:** Not implemented at TL-0008; the named owning task must supply supported configuration and one-scenario command before invocation.
- **Expected invariant:** Same essential deterministic result and safety verification through CPU fallback; progress, focus, announcements, cancellation, and recovery remain available.
- **Abort:** The method requires an unsupported driver/registry change, host-wide control change, lost display/accessibility, corruption, or unbounded resources.
- **Cleanup/restoration:** Exit the isolated run and restore the reviewed product configuration.
- **Claim limitation/status:** Does not prove every graphics device/driver or low-end PC; command/result not implemented at TL-0008; the named owning task must supply the item before invocation; `Not run`.

## 6. `SMC-CONSERVATIVE-CONCURRENCY`

- **Purpose:** Detect unbounded workers, order/race faults, memory/I/O amplification, and unsafe cancellation.
- **Prerequisites:** Supported configurable worker limit and deterministic workload with order-independent expected output.
- **Safe setup:** Select worker count `1` or another reviewed low value through product/test configuration; do not globally change system scheduling.
- **Invocation state:** Not implemented at TL-0008; the named owning task must supply worker setting and command before invocation.
- **Expected invariant:** Correct output/journal; slower completion is acceptable; bounded queues/workers; progress, cancellation, checkpoint, and resume remain truthful.
- **Abort:** Worker setting is ignored/unbounded, output differs, UI/recovery is inaccessible, or host/resource safety threshold is crossed.
- **Cleanup/restoration:** Restore default reviewed worker configuration; remove verified disposable artifacts.
- **Claim limitation/status:** Does not simulate a CPU/RAM class; setting, command, budget, and result not implemented at TL-0008; the named owning task must supply the item before invocation; `Not run`.

## 7. `SMC-LOW-PRIORITY`

- **Purpose:** Observe responsiveness, progress, timeouts, and cancellation under reversible scheduling pressure.
- **Prerequisites:** Reviewed process-scoped priority mechanism in a disposable test context, workload bounds, UI responsiveness observation method, and recovery path.
- **Safe setup:** Apply the reversible constraint only to the test process after launch; never reduce priority of system/security/accessibility services.
- **Invocation state:** Not implemented at TL-0008; the named owning task must supply process-scoped setup, workload command, and restoration before invocation.
- **Expected invariant:** The application remains operable, reports progress/uncertainty, accepts safe cancellation, and finishes or fails recoverably.
- **Abort:** Host responsiveness/safety is affected, accessibility output disappears, cancellation/recovery becomes unavailable, or timeout bounds are exceeded.
- **Cleanup/restoration:** Restore/terminate only the isolated test process; verify no child/background process remains.
- **Claim limitation/status:** Scheduling pressure is not a processor simulation; method/command/result not implemented at TL-0008; the named owning task must supply the item before invocation; `Not run`.

## 8. `SMC-LOW-FREE-SPACE`

- **Purpose:** Prove capacity preflight, rollback headroom, atomicity, safe refusal, and cleanup.
- **Prerequisites:** Injected capacity provider or disposable bounded virtual disk/workspace; known fixture sizes; failure threshold; recovery image/snapshot when needed.
- **Safe setup:** Prefer injected values. If integration behavior requires a volume, create a disposable virtual disk or bounded workspace with explicit maximum size away from the host system volume.
- **Invocation state:** Not implemented at TL-0008; the named owning task must supply isolated capacity setup and one-scenario command before invocation.
- **Expected invariant:** Insufficient capacity blocks before unsafe mutation; mid-write exhaustion never produces false complete/verified state; partial output is bounded and removed or explicitly recoverable.
- **Abort:** Target resolves to system/personal/donor storage, free-space headroom is uncertain, bounds fail, or unrelated data could be changed.
- **Cleanup/restoration:** Confirm exact disposable target, detach/delete it through the reviewed task procedure, verify source job and host storage remain intact.
- **Claim limitation/status:** Does not test every full/slow physical disk; thresholds/command/result not implemented at TL-0008; the named owning task must supply the item before invocation; `Not run`.

## 9. `SMC-OFFLINE`

- **Purpose:** Prove local inventory, job, policy, manual-test, evidence-review, and report paths remain usable and network-dependent actions fail/defer truthfully.
- **Prerequisites:** Network/provider stub or hosted-environment isolation; deterministic local and network-dependent fixtures; no reliance on cached success.
- **Safe setup:** Prefer a fake adapter. Hosted network isolation may be used only inside a disposable environment with documented reconnection; do not disconnect or reconfigure the host network.
- **Invocation state:** Not implemented at TL-0008; the named owning task must supply stub/hosted-isolation command before invocation.
- **Expected invariant:** Core local work continues; network use category and unavailability are visible; no mutation/success is fabricated; retry is safe.
- **Abort:** The method affects host connectivity, personal services, credentials, or an unreviewed network boundary.
- **Cleanup/restoration:** Stop stub/hosted environment, verify ordinary host state was untouched, remove partial synthetic outputs.
- **Claim limitation/status:** Does not cover every network/provider failure; command/result not implemented at TL-0008; the named owning task must supply the item before invocation; `Not run`.

## 10. `SMC-INTERRUPTED-NETWORK`

- **Purpose:** Prove checkpoint/retry, bounded partial data, approval invalidation, and metadata/trust revalidation.
- **Prerequisites:** Deterministic faulting/throttled adapter or disposable hosted environment; exact injection checkpoint; bounded transfer fixture; trust metadata variants.
- **Safe setup:** Inject failure through the adapter/harness. Host network interruption requires a separate explicit approval and recovery plan and is not the default.
- **Invocation state:** Not implemented at TL-0008; the named owning task must supply injection point and one-scenario command before invocation.
- **Expected invariant:** Partial untrusted bytes never execute; state remains attributable/retry-safe; later retry re-resolves identity/source/publisher/version/digest and requires reapproval after material change.
- **Abort:** Injection crosses an unreviewed mutation boundary, affects host connectivity, or cancellation/cleanup cannot be guaranteed.
- **Cleanup/restoration:** Restore adapter/hosted environment; remove/quarantine partial synthetic data; verify journal and cache bounds.
- **Claim limitation/status:** Does not cover every real network path; injection/command/result not implemented at TL-0008; the named owning task must supply the item before invocation; `Not run`.

## 11. `SMC-PROVIDER-UNAVAILABLE`

- **Purpose:** Prove unavailable, access-denied, timeout, malformed, and bounded-exception evidence remains unknown/fail-closed.
- **Prerequisites:** Typed provider interface and deterministic fixture/stub for each represented failure code.
- **Safe setup:** Inject at the provider boundary; do not change host permissions or disable a real provider merely to manufacture failure.
- **Invocation state:** Not implemented at TL-0008; the named owning provider task must supply exact failure case/filter before invocation.
- **Expected invariant:** Required evidence cannot pass; unrelated observations remain; raw output is not retained; next safe action is clear.
- **Abort:** The harness would require elevated/broad permission changes, real provider damage, unbounded output, or sensitive capture.
- **Cleanup/restoration:** Dispose the stub/fixture workspace and verify no host permission/provider change occurred.
- **Claim limitation/status:** Proves represented error contract only; fixture/command/result not implemented at TL-0008; the named owning task must supply the item before invocation; `Not run`.

## 12. `SMC-SLOW-DESTINATION`

- **Purpose:** Prove bounded buffering, progress, timeout, cancellation, checkpoint/recovery, and atomic output under slow writes.
- **Prerequisites:** Throttled test adapter or controlled disposable destination; deterministic bounded output; timeout/cancellation bounds; atomic replace contract.
- **Safe setup:** Use the product's test adapter or a disposable hosted destination. Do not rely on unknown external/removable hardware.
- **Invocation state:** Not implemented at TL-0008; the named owning task must supply throttle settings and one-scenario command before invocation.
- **Expected invariant:** Memory/temp growth remains bounded; status stays accessible; cancellation leaves truthful state; source data remains intact; final artifact is valid or absent.
- **Abort:** Destination resolves outside disposable scope, host/system storage is pressured, time/resource bounds fail, or cleanup becomes uncertain.
- **Cleanup/restoration:** Stop throttling, remove verified disposable partial/final artifacts, inspect source job and residue.
- **Claim limitation/status:** Does not prove a particular HDD/USB/network destination; settings/command/budget/result not implemented at TL-0008; the named owning task must supply the item before invocation; `Not run`.

## 13. `SMC-LARGE-WORKLOAD`

- **Purpose:** Measure streaming/chunking and bounded memory, temp, cache, database, output, cancellation, checkpoint, and resume behavior.
- **Prerequisites:** Representative deterministic workload protocol, generator/source, expected output, size/count bounds, version/SHA-256, measurement tool/version/overhead, baseline, repetitions, and initial evidence-informed budgets.
- **Safe setup:** Generate/use only bounded synthetic data inside an isolated workspace after disk/headroom preflight. Increase size only through a reviewed task update; never consume space merely to stress the host.
- **Invocation state:** Not implemented at TL-0008; the named owning task must supply workload ID/hash, size, budgets, and one-scenario command before invocation.
- **Expected invariant:** Correct output; streaming/chunking; conservative concurrency; bounded peak working set/commit/temp/cache/database/output; responsive progress/cancel; durable resume where supported.
- **Abort:** Predicted/observed headroom becomes unsafe, budget is crossed without controlled stop, host stability/accessibility is affected, output is incorrect, or cancellation/recovery fails.
- **Cleanup/restoration:** Verify output/checkpoints, remove only the disposable workload/workspace, record residue and before/after storage, restore configuration.
- **Claim limitation/status:** Numerical budgets and representative workload require later measurement/approval; no 4 GB/8 GB or lower-performance-device claim; `Not run`.

## 14. Evidence record

Every executed profile records:

```text
Task:
Source commit and branch:
Tier and named trigger:
Reference-machine profile revision:
Hosted/direct environment:
Constraint profile:
Exact supported settings:
Fixture/workload ID, version, path, SHA-256, expected result:
Independently invokable command/procedure:
Start, end, duration, repetition/stabilization:
Measurement tool/version/overhead:
Peak memory, CPU, temp, cache, database, output:
UI responsiveness and accessibility observations:
Cancellation, checkpoint, resume:
Product/integrity result:
Abort or cleanup/restoration result:
Residue:
Defect and focused rerun:
Claim limitation:
```

Evidence must be bounded and sanitized. Do not attach raw hardware/diagnostic logs or unrestricted local paths.

## 15. Current state and later execution

| Profile range | Quick definition check | Targeted | Full | Extended | Evidence |
|---|---|---|---|---|---|
| `SMC-BASELINE` through `SMC-LARGE-WORKLOAD` | Defined for TL-0008 | `Not run`; no trigger | `Not run`; no trigger | `Not run`; no trigger | None; these items are not implemented at TL-0008, and each named owning task must supply its command, fixture/hash, budget, and expected result before invocation |

When a later scenario fails, rerun its single command first and then the related targeted set. Rerun full or extended scope only when its trigger applies or a shared cause is suspected. Release wording may report recorded same-machine results and design intent, never broad modest-hardware certification or unobserved minimum specifications.
