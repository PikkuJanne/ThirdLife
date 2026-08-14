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

## 8. TL-0008 benchmark contract

**Procedure status:** Draft procedure; human evidence pending  
**Procedure revision:** TL-0008 draft 1  
**Record schema:** `LSR-1` (`template_not_evidence`)

The procedure below turns the measurement fields and workloads above into repeatable evidence. It does not set a minimum specification or a numerical performance budget. A 4 GB run, an 8 GB run, a VM run, and a process-constrained run are separate test classes. None of them establishes physical-device support, broad compatibility, accessibility conformance, or long-term reliability.

Every run records two independent classifications:

- **test result:** `Pass`, `Fail`, `Not available`, or `Not run`; and
- **evidence class:** `Observed`, `Inferred`, `Not available`, or `Human confirmed`, as defined by D-015.

`Human confirmed` identifies provenance; it is not a passing result. `Pass` means the exact procedure completed, every applicable safety/integrity invariant held, and attributable evidence exists for the workload; it is independent of the numerical budget decision. Where no approved numerical budget exists, a successful run is provisional evidence with `test_result: Pass` and `budget_result: Not available`.

## 9. Controlled benchmark procedure

1. Use a sanitized or fresh-known lab environment and synthetic, versioned fixtures. Never use donor or recipient content.
2. Bind the run to the exact ThirdLife version, 40-character source revision, procedure revision, fixture ID/version/SHA-256, Windows build, device ID, constraint profile, and measurement-tool version. The device ID is an opaque lab reference, never a serial, hostname, username, address, or encoded hardware identifier.
3. Record three orthogonal dimensions: hardware environment (`Physical` or `Virtual`), execution context (`Interactive lab` or `CI`), and whether a constraint profile was imposed. Record imposed CPU, priority, memory, storage, network, acceleration, and power conditions separately from observed hardware. A constrained CI/VM run remains virtual evidence and cannot imply physical proof.
4. Preflight free space, rollback headroom, power, and the workload's safe stop conditions. Do not begin a modifying workload when the preflight fails.
5. Record baseline background activity, pending restart/update state, available memory and storage, network conditions, power source, instrumentation overhead, and any condition that could affect comparison.
6. Measure the complete ThirdLife process tree. Use the catalogue's recorded start trigger, stop trigger, reset rule, and stabilization rule; record any necessary variation before the run. Distinguish a process-cold application start from a powered-off physical cold boot; neither substitutes for the other.
7. Run at least three measured repetitions when the workload is safe and repeatable. Retain each value and report minimum, median, and maximum. Explain deviations; never discard an outlier silently. A separately authorized modifying or non-repeatable workload runs only in an approved disposable/recoverable environment and records why fewer repetitions were safe.
8. Exercise cancellation and resume at a documented checkpoint where supported. Record latency, durable state, recovery, and whether actual state was re-observed before retry. Never blindly repeat a mutation.
9. Inspect UI responsiveness, data integrity, database/log/cache growth, temporary and final storage, cleanup, security checks, and accessibility behavior as well as timing. Resource-saving behavior must not disable names, focus, announcements, non-color status, safe cancellation, or trust checks.
10. Stop on unsafe temperature, battery swelling, unexpected storage errors, threatened data loss, insufficient capacity, or an unexplained privileged action. Preserve truthful state and open a blocker or defect.
11. After the run, verify output integrity, remove owned temporary material, record residual files and restart state, and hash only sanitized evidence artifacts. Do not commit screenshots, raw logs, dumps, absolute paths, or device identifiers.
12. Have a named reviewer compare the record with the procedure and the applicable budget revision. Automation can check structure and point-in-time measurements; it cannot prove physical behavior or long-term hardware reliability.

## 10. Stable workload catalogue

| ID | Workload | Measurement boundary and reset | Required observation | Availability rule |
|---|---|---|---|---|
| `LSB-001` | Process-cold application start | Start at recorded process-launch request after all ThirdLife processes exit; stop when the primary window is keyboard operable and initial status is announced; reset by closing the complete process tree; use a declared fixed idle-observation interval | Startup time, responsiveness, idle memory and stabilization samples | Not a machine cold boot |
| `LSB-002` | Warm application start | Start at recorded launch with the same build/profile after one unmeasured warm-up; stop at the same operable/announced state as `LSB-001`; reset by closing the complete process tree | Startup time and retained-state correctness | Compare only within the same build/profile |
| `LSB-003` | Create, reopen, and archive a synthetic job | Start at each recorded user action; stop at durable completion and responsive UI; reset from a fresh fixture copy | Per-phase time, storage growth, integrity, and cleanup | Synthetic job only |
| `LSB-004` | Normal inventory | Start when the bound inventory request is accepted; stop when all bounded providers reach a terminal state and UI is responsive; reset provider fixture/state | Duration, peak memory, bounded records, responsiveness | Provider/build recorded |
| `LSB-005` | Provider-unavailable inventory | Start at accepted inventory request; stop at truthful terminal limitation/recovery display; reset the unavailable-provider fixture | Recoverable failure, limitation, and bounded retry | Missing evidence cannot pass |
| `LSB-006` | Large bounded observation set | Start when the versioned fixture is accepted; stop after durable normalized output and responsive UI; reset from fixture source | Peak memory, elapsed time, collection bound, output integrity | Versioned synthetic fixture |
| `LSB-007` | Policy evaluation and historical replay | Start at evaluation request; stop at durable deterministic result; reset identical policy/history fixtures | CPU/elapsed time, deterministic result, memory | Versioned policy and history fixture |
| `LSB-008` | Workshop report and sanitized support generation | Start at approved generation request; stop after atomic artifact completion and preview readiness; remove owned outputs before reset | Elapsed time, temporary/output size, privacy-safe output | Separate output schemas |
| `LSB-009` | Multi-action plan resolution | Start at resolution request; stop at stable preview/digest; reset identical inputs | Elapsed time, memory, deterministic digest | Resolution only until actions exist |
| `LSB-010` | Throttled/interrupted package lifecycle | Start at approved lifecycle phase; stop at verified or truthful recoverable terminal state; restore disposable snapshot/cache policy | Network profile, checkpoints, cancellation/recovery, verification | Disposable/synthetic package environment |
| `LSB-011` | Windows Update scan, restart, and resume | Start at structured scan request; stop after bounded convergence and fresh post-restart verification; restore disposable snapshot where repeatable | Bounded convergence, restart state, fresh verification | Supported Windows API/environment only |
| `LSB-012` | Database migration and recovery | Start before backup/preflight; stop after verified reopen or safe recovery block; reset copied synthetic database | Backup/headroom, time, storage, integrity after interruption | Synthetic database copies only |
| `LSB-013` | Accessibility-setting preview, apply, and reverse | Start at recipient-approved preview; stop after independent reversal verification and responsive accessible UI; restore the initial setting | Responsiveness, reversibility, attributable accessibility checks | Present recipient and supported setting when implemented |
| `LSB-014` | Backup onboarding and harmless restore test | Start at capacity preflight; stop after representative restore verification and cleanup; reset approved disposable destination | Capacity preflight, time, verification, cleanup | Supported disposable destination only |

A workload that has not yet been implemented is `Not run`; its catalogue row is not an implementation claim. `Not available` requires an attributable reason such as missing capability, missing equipment, unsafe conditions, provider unavailable, or permission denied.

## 11. Provisional budgets and regression handling

Numerical budgets remain `Pending` until measured results and an authorized review establish a versioned budget. A provisional record may compare runs only across the same build, fixture, hardware environment, execution context, and constraint profile, but it cannot define a minimum specification.

`test_result` records procedure and invariant success; `budget_result` separately records `Pass`, `Fail`, or `Not available`. When a budget exists, `budget_result: Pass` means every applicable numerical limit was met. A failed procedure invariant makes `test_result: Fail` even if its measurements fit a budget. A numerical overrun makes `budget_result: Fail` and records the limit plus a defect/blocker reference. `Not available` and `Not run` never become passes. A release comparison records both the current and baseline raw values; it does not hide a regression behind an aggregate score.

Each record reports hardware environment, execution context, imposed constraint profile, and automated/human/partner provenance as separate dimensions. A single successful run cannot be extrapolated to untested storage, memory, network, battery, firmware, or form-factor classes. Automation and workshop observation provide point-in-time evidence only and cannot certify a device or prove long-term reliability.

## 12. Provisional resource-record template

The following is a field template, not evidence. An executed record changes `record_kind` to `benchmark_result`, replaces every `Pending` value, and retains `Not run` or `Not available` only with an attributable reason. Do not commit personal or machine-identifying values.

```yaml
schema_version: LSR-1
record_kind: template_not_evidence
record_id: Pending
benchmark_id: Pending
procedure_revision: TL-0008 draft 1
thirdlife_version: Pending
source_revision: Pending
recorded_at_with_offset: Pending
operator_role: Pending
reviewer_role: Pending
hardware_environment: Pending
execution_context: Pending
constraint_applied: Pending
device_id: Pending
windows_edition: Pending
windows_build: Pending
windows_architecture: Pending
windows_support_state: Pending
cpu_model_class: Pending
physical_core_count: Pending
logical_processor_count: Pending
imposed_cpu_or_priority_constraint: Pending
installed_memory_mib: Pending
available_memory_mib_at_start: Pending
imposed_memory_constraint: Pending
storage_type: Pending
free_space_mib_at_start: Pending
free_space_mib_at_end: Pending
imposed_storage_condition: Pending
network_profile: Pending
network_bandwidth_latency_loss_filtering: Pending
power_source_and_battery_state: Pending
gpu_acceleration_state: Pending
cpu_fallback_result: Pending
fixture_id: Pending
fixture_version: Pending
fixture_sha256: Pending
workload_variant: Pending
workload_protocol_version: Pending
measurement_start_trigger: Pending
measurement_stop_trigger: Pending
reset_and_stabilization_rule: Pending
measurement_tool_and_version: Pending
instrumentation_overhead: Pending
preflight_result_and_rollback_headroom: Pending
baseline_background_activity: Pending
pending_update_and_restart_state: Pending
measured_process_scope: Pending
repetition_count: Pending
fewer_than_three_repetitions_reason: Pending
raw_measurement_artifact_refs: []
elapsed_time_ms_each: []
elapsed_time_ms_min_median_max: Pending
cpu_time_ms_each: []
startup_time_ms_each: []
idle_working_set_mib_each: []
peak_working_set_mib_each: []
peak_commit_mib_each: []
temporary_storage_peak_mib_each: []
final_output_size_mib_each: []
database_log_cache_size_before_after_mib: Pending
ui_responsiveness_method_and_observation: Pending
cancellation_latency_ms_and_result: Not run
resume_duration_ms_and_result: Not run
completion_state: Pending
budget_revision: Pending
budget_result: Not available
data_integrity_result: Pending
cleanup_result_and_residue: Pending
accessibility_check_ids_results_environment_evidence: Pending
security_check_ids_results_evidence: Pending
test_result: Not run
evidence_class: Not available
provider_or_human_reviewer: Pending
provenance: Pending
limitations: Pending
defect_or_blocker_ids: []
sanitized_artifact_sha256_refs: []
```
