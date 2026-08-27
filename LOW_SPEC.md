# ThirdLife Setup Core — Modest-Hardware Engineering and Same-Machine Evidence

**Bundle version:** 0.3.1  
**Portfolio baseline:** ThirdLife Software Portfolio v2.1  
**Physical validation scope:** active Codex machine only

## 1. Purpose

ThirdLife Setup Core is intended to remain usable on modest supported Windows 11 hardware. This is an engineering requirement, not a claim that the project has tested every processor, memory size, storage technology, manufacturer, or peripheral combination.

The project has no device lab, lower-performance test computer, volunteer hardware pool, cloud runner matrix, or second physical validation machine. All implementation, tests, benchmarks, clean environments, and release evidence run on the active Codex machine. Modest-hardware readiness therefore comes from resource-conscious architecture, deterministic fixtures, conservative defaults, measurable budgets, and repeatable same-machine constraint scenarios.

No document may turn these methods into a broad hardware certification or an invented minimum specification.

## 2. Binding engineering rules

ThirdLife Setup Core must:

- stream or page data instead of loading unbounded inventories, reports, archives, or logs into memory;
- keep concurrency explicit, configurable, and conservative by default;
- provide a single-worker or low-resource mode when concurrency materially affects memory, disk, network, or UI responsiveness;
- require no GPU; hardware acceleration may be used only as an optional detected optimization with a working CPU path;
- preflight required disk space, preserve rollback and temporary-space headroom, and fail before mutation when capacity is insufficient;
- keep long operations cancellable and, where appropriate, pausable, resumable, and checkpointed;
- bound caches, temporary files, diagnostic retention, report history, and database growth;
- avoid permanent background indexing, unnecessary startup work, repeated provider polling, and repeatedly loading large resources;
- remain responsive while package, update, inventory, report, and verification work executes;
- degrade by reducing concurrency, preview detail, refresh frequency, or optional work—not by weakening verification, security, privacy, data ownership, or evidence semantics; and
- expose resource or capability limitations honestly rather than silently hanging, crashing, corrupting state, or reporting success.

## 3. Active reference-machine profile

The project records one sanitized reference-machine profile for the active Codex machine. It is development evidence, not recipient or asset inventory.

Record only:

- profile ID and capture date/time with offset;
- supported Windows edition, build, and architecture;
- CPU model/class, logical processor count, and relevant virtualization status;
- installed memory;
- storage technology/class, free-space class, and volume used for tests;
- GPU presence and whether tests used acceleration or the CPU path;
- .NET SDK/runtime, PowerShell, package tooling, database tooling, and relevant test-tool versions;
- whether clean environments use a VM, Windows Sandbox, container, separate worktree, or clean clone on the same machine; and
- known machine limitations that affect evidence interpretation.

Do not record serial numbers, donor or corporate asset tags, usernames, email addresses, device names, SSIDs, IP addresses, credentials, recovery keys, personal paths, screenshots, photos, audio, video, or raw logs.

The canonical repository record belongs at `docs/testing/reference-machine-profile.md`.

## 4. Same-machine constraint profiles

Constraints are selected only to answer a concrete risk. Each scenario must be safe, reversible, independently invokable, and clearly labelled as simulation or constrained execution.

| Constraint profile | Risk answered | Acceptable methods on the active machine | Required evidence |
|---|---|---|---|
| `BASELINE` | Normal regression and comparison | Normal host or clean hosted Windows environment | Reference profile, command, duration, workload hash, result |
| `NO_GPU` | Accidental accelerator dependency | Explicit software/CPU path; disable optional acceleration through supported product/test configuration | Selected path and proof core journey completes |
| `CONSERVATIVE_CONCURRENCY` | Memory/IO growth and race sensitivity | One worker or documented low concurrency | Worker count, peak memory, elapsed time, correctness |
| `LOW_PRIORITY` | Responsiveness under scheduling pressure | Supported process-priority controls in a disposable test context | Priority, UI responsiveness observations, completion state |
| `LOW_FREE_SPACE` | Preflight and rollback safety | Disposable virtual disk/volume, quota, bounded test workspace, or injected capacity provider | Free-space threshold, no unsafe mutation, cleanup |
| `OFFLINE` | Core continuity and truthful network state | Network stubs, blocked test provider, or disposable hosted-environment network isolation | Requests attempted, offline result, recovery path |
| `INTERRUPTED_NETWORK` | Resume/retry and journal integrity | Deterministic fault injection or disposable hosted-environment interruption | Checkpoint, preserved state, retry result |
| `SLOW_DESTINATION` | Long writes, timeouts, and cancellation | Throttled test adapter or controlled slow test destination | Throughput/latency setting, cancellation/recovery |
| `PROVIDER_UNAVAILABLE` | Missing Windows/hardware evidence | Synthetic/captured provider result, access-denied fixture, or unsupported-state fixture | Evidence remains unavailable and does not pass policy |
| `LARGE_WORKLOAD` | Growth, chunking, and bounded retention | Versioned representative fixture sized to answer the risk | Fixture hash, peak memory, temp/cache/database growth |

A constraint that cannot be created safely on the active machine is marked **Not run** with a reason. It is not replaced by an unsupported claim.

## 5. Resource evidence

For each benchmark or resource-sensitive test, record:

- application/bundle version and source commit;
- test tier and exact command;
- active reference-machine profile revision;
- clean-environment type, when used;
- constraint profile and exact settings;
- workload/fixture identifier and hash;
- elapsed time and CPU time where available;
- peak working set or another consistently defined memory metric;
- temporary storage peak, cache growth, database growth, and output size;
- cancellation/checkpoint/resume outcome;
- pass, fail, not run, or blocked result;
- cleanup result; and
- limitation or claim boundary.

Initial numerical budgets remain `TBD` until measured. Codex must not invent a threshold to make a gate appear complete. Once a budget is approved, regression beyond it requires investigation, an explicit accepted trade-off, or a release-blocking limitation.

`TL-0102` adds defensive persistence ceilings rather than a performance or supported-capacity claim: at most 10,000 jobs, 10,000 evidence records and 10,000 checkpoints per job, 256 records per evidence batch, 64 KiB per normalized JSON payload, and 256 MiB each for the SQLite database and rollback journal. Initialization residue is limited to 64 matching path entries before reconciliation fails closed. These values prevent unbounded growth in the initial store; they do not establish an approved retention period, expected workshop workload, modest-hardware throughput budget, or cross-hardware certification. Later lifecycle and resource tasks must measure representative workloads and define retention, export, backup, cleanup, and operator recovery.

A slower result may be acceptable. The following are not acceptable for a supported core path:

- out-of-memory termination;
- unbounded cache, temporary, log, or database growth;
- corruption or partial output presented as final;
- lost journal/checkpoint state;
- inability to cancel a long operation safely;
- UI starvation that prevents understanding or safe recovery;
- silently dropping verification, security, privacy, or evidence checks; or
- requiring a GPU or high concurrency to complete the core journey.

## 6. Hardware and provider coverage

Hardware variability is covered through a capability/risk matrix, not a physical-device inventory. For each variant—such as battery absent, storage counters unavailable, Secure Boot disabled, TPM access denied, multiple disks, unsupported CPU, camera absent, or network unavailable—the matrix identifies one or more of:

1. deterministic unit/component fixture;
2. captured and sanitized provider sample;
3. adapter/integration test in a clean environment on the active machine;
4. bounded observation of the active machine’s actual capability;
5. manual-test workflow state simulation; or
6. explicit unverified limitation.

A synthetic fixture proves software behavior for that input. It does not prove that every real provider or device reports data identically. An active-machine observation proves only that exact recorded environment. Both limitations must remain visible.

## 7. Manual hardware-test workflow

ThirdLife still needs a guided manual-test feature because operating-system inventory cannot prove keyboard, pointer, display, audio, microphone, camera, charging, sleep/wake, or port function.

Development verification of that feature uses deterministic state transitions, evidence validation, pause/resume, accessibility tests, and bounded observation of capabilities available on the active Codex machine. Missing peripherals or capabilities are exercised through `Not available`, `Not run`, failure, and resume fixtures. They are not project blockers.

The earlier TL-0008 draft 1 request to assemble a device pool and execute `MHT-001`–`MHT-021` is superseded. Those test identifiers may remain as product-workflow specifications or future field-validation cases, but they are not current TL-0008 completion evidence and do not imply a hardware lab.

## 8. Test-tier relationship

`TESTING.md` governs execution:

- **Quick** checks run after small fixes and before checkpoint commits.
- **Targeted** checks run before pushing a feature/fix and after a risk-boundary change.
- **Full** checks run at milestone/release gates and after major refactors, migrations, dependency changes, or other named triggers.
- **Extended** scenarios run only when their specific risk changed or an explicit gate requires them.

Low-resource scenarios are not automatically rerun after every edit. The task graph identifies the expected tier and broader triggers. Every reproducible defect becomes the smallest practical deterministic regression case and is run first on later iterations.

## 9. Release wording

Permitted wording:

> ThirdLife Setup Core is designed for modest supported Windows 11 hardware through bounded resource use, conservative defaults, CPU fallbacks, cancellation, checkpointing, and same-machine constrained validation. Published measurements describe the recorded reference machine and workloads.

Not permitted without separate evidence:

- “certified for low-end PCs”;
- “tested across 4 GB and 8 GB systems” when no such systems were used;
- “works on all Windows 11 devices”;
- “hardware independent”;
- a minimum CPU/RAM/storage specification inferred only from simulations; or
- a statement that VMs, process limits, or synthetic fixtures reproduce a specific real device.

`RELEASE_INTERFACE.md`, release notes, support documentation, and known limitations must identify the reference machine, constraint methods, observed results, skipped scenarios, and the absence of cross-hardware certification.

## 10. Release blocker rule

A stable release is blocked when:

- the core journey exceeds an approved resource budget without an accepted explanation;
- correctness, cancellation, recovery, or data safety fails under a required same-machine scenario;
- a GPU or high-concurrency path becomes mandatory;
- growth is unbounded;
- the active-machine evidence cannot be reproduced from a clean clone; or
- release text overstates hardware evidence.

Lack of a second computer, a specific storage technology, a degraded battery, a touchscreen, a dock, or other lab equipment is **not** by itself a blocker. The correct outcome is deterministic coverage where possible and an explicit limitation where not.
