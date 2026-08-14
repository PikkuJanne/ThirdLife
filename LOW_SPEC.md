# ThirdLife Setup Core — Low-Spec and Graceful-Degradation Plan

**Status:** Binding engineering baseline; numerical support claims require measured evidence  
**Bundle version:** 0.2.0

## 1. Principle

ThirdLife Setup Core must remain usable on the kind of modest machine it is preparing. Low-spec compatibility is developed continuously. It is not deferred to a final weak-device test and it must not depend on a GPU, constant broadband, large memory, fast storage, or an always-running background service.

Exact minimum hardware claims are not frozen here. They are published only after measured validation. A 4 GB or 8 GB device in the test matrix is a **test class**, not an automatic support promise.

## 2. Mandatory design rules

- Stream or page large provider results, reports, logs, and exports rather than loading unbounded content.
- Bound collection size, raw evidence size, concurrency, retries, timeouts, cache size, and temporary storage.
- Default conservatively on unknown hardware; make safe concurrency configurable.
- Keep the UI responsive while inventory, updates, downloads, installs, verification, reports, and backup tests run.
- Support cancellation and durable resume/checkpointing for long operations.
- Estimate download, installed, temporary, rollback, and report space before modification.
- Fail before changing the device when required capacity is unavailable.
- Avoid permanent indexing, repeated full inventory, automatic startup work, and repeated loading of heavy dependencies.
- Require a tested CPU path; hardware acceleration is optional only.
- Separate large package caches or future model assets from core application updates.
- Preserve accessibility semantics and safety checks in low-resource modes.

## 3. Initial measurement fields

Each benchmark record includes:

- ThirdLife version and source revision;
- Windows edition/build and architecture;
- CPU model/core count and imposed CPU constraint;
- installed and available memory;
- storage type, free space, and imposed storage condition;
- network state/bandwidth/latency when relevant;
- workload/fixture ID and hash;
- elapsed time and CPU time;
- startup time and idle memory;
- peak working set/commit;
- temporary storage peak and final output size;
- UI responsiveness observations;
- cancellation/resume result;
- success, recoverable failure, or corruption result.

## 4. Representative test classes

The release matrix should include, where available:

- 4 GB RAM and 8 GB RAM classes;
- SATA HDD, SATA SSD, and NVMe;
- low free space;
- standard user and administrator/UAC paths;
- no GPU dependency and no hardware acceleration;
- intermittent, filtered, slow, and absent network;
- battery and mains-powered devices;
- laptop and desktop;
- at least one unusual or partially broken device.

Where dedicated hardware is unavailable, use constrained process priority, CPU, memory, storage, and network conditions in CI/VMs. Preserve the limitation that VMs cannot prove physical hardware behavior.

## 5. Benchmark workloads

At minimum:

1. cold and warm application start;
2. create/reopen/archive a synthetic job;
3. normal and provider-unavailable inventory;
4. large but bounded observation set;
5. policy evaluation and historical replay;
6. report and sanitized support-bundle generation;
7. plan resolution with multiple actions;
8. package download/install/verification with throttled or interrupted network;
9. Windows Update scan and restart/resume where supported;
10. database migration and recovery;
11. accessibility-setting preview/apply/reverse;
12. basic backup onboarding and harmless restore test where supported.

Fixtures include small, normal, large, malformed, and adversarial cases. No benchmark may contain real donor or recipient data.

## 6. Regression policy

Project-specific numerical budgets are established by `TL-0008`/subsequent benchmark work and updated only with evidence. The following always block release regardless of elapsed time:

- out-of-memory or unhandled resource exception on a supported workload;
- unbounded cache, log, database, or temporary-file growth;
- UI deadlock or loss of safe cancellation;
- corrupted job, journal, report, or partial output;
- modification beginning after preflight reported insufficient capacity;
- failure to resume or truthfully classify an interrupted action;
- accessibility or security checks disabled to improve performance.

A slower result may be accepted when the UI remains responsive, progress/limitations are clear, and the operation completes or fails recoverably.

## 7. Release claims

Until physical partner evidence exists, hardware claims are labelled provisional. Release notes distinguish:

- automated constrained-environment evidence;
- VM evidence;
- physical-device evidence;
- partner/volunteer evidence;
- untested configurations.

Do not convert a single successful low-end run into a broad minimum-spec claim.
