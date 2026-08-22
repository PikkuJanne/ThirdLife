# ThirdLife Setup Core — Testing Strategy and Execution Contract

**Bundle version:** 0.3.1  
**Portfolio baseline:** ThirdLife Software Portfolio v2.1  
**Authoritative runtime hardware:** active Codex machine only

## 1. Purpose

This file defines how ThirdLife Setup Core is verified quickly, reproducibly, and honestly. It replaces the former assumption that the project would maintain a hardware lab, lower-performance reference devices, or an external runner matrix.

All implementation, tests, benchmarks, clean-clone checks, virtual machines, containers, Windows Sandbox sessions, and release evidence run on the same physical Codex machine. GitHub is the continuity source of truth, not a remote hardware test farm.

The test strategy must preserve two properties at the same time:

1. fast feedback during development; and
2. complete, risk-relevant evidence at milestone and release gates.

A long suite is not the default response to every code change.

## 2. Test tiers

### 2.1 Quick tier

**Default target:** two minutes or less on the active Codex machine.

Typical contents:

- formatting, linting, analyzers, and static/type checks;
- bundle/schema validation;
- changed unit/component tests and the smallest nearby regression set;
- shortest primary smoke path with tiny deterministic fixtures; and
- secret/PII and prohibited-coupling checks suitable for fast execution.

Required:

- after each small fix;
- before every checkpoint commit; and
- before moving a task from `in_progress` to `review` or `done`.

Quick tests fail fast and do not load large fixtures, start broad clean-environment matrices, or perform destructive/mutating host operations.

### 2.2 Targeted tier

**Default target:** ten minutes or less unless the task records a justified project-specific budget.

Typical contents:

- the failing regression case first;
- the changed subsystem and nearby integrations;
- only the relevant Windows provider, persistence, UI, package, update, broker, security, accessibility, migration, or report tests;
- a bounded clean-environment or active-machine smoke test when the changed boundary requires it; and
- deterministic fault cases for the changed behavior.

Required:

- before pushing a feature or fix;
- after changing a data/migration, privilege, IPC, package/update, security, network, filesystem, installer/lifecycle, or accessibility boundary; and
- when a quick test indicates a wider but still bounded regression risk.

### 2.3 Full tier

Typical contents:

- all supported test layers;
- complete core end-to-end journeys;
- clean and upgraded installation paths;
- migration, recovery, export, repair, and uninstall where applicable;
- complete security/privacy and accessibility baseline checks; and
- same-machine clean-clone/bootstrap verification.

Required:

- at milestone, preview, pilot, and stable-release gates;
- after major refactors;
- after data migrations or dependency/engine changes;
- before a protected release merge; or
- when `TASKS.yaml` names a full-tier trigger.

Do not rerun the full tier after every edge-case fix. First run the smallest regression and related targeted set. Rerun the full tier only after the relevant fixes are green or when a shared cause makes the broader rerun necessary.

### 2.4 Extended/stress tier

Typical contents:

- large or adversarial fixtures;
- repeated interruption, restart, resume, or endurance cases;
- low-space, offline, interrupted-network, slow-destination, no-GPU, conservative-concurrency, and low-priority scenarios;
- broad hostile-input, restore, integrity, or failure matrices; and
- long resource measurements.

Required only:

- when the changed risk specifically needs it;
- at a task or gate whose `extended_test_triggers` require it; or
- at an explicitly scheduled release gate.

Every extended scenario must be independently invokable, checkpointed when long-running, and capable of producing a scenario-level result. Rerun the failed scenario and its related targeted tests, not the whole matrix, unless a shared cause is suspected.

## 3. Required test layers

| Layer | Project responsibility |
|---|---|
| Unit/component | Domain invariants, parsers, validators, schemas, policy, redaction, path safety, persistence, migration, journal transitions, retention, report contracts, and failure handling. |
| Provider/engine integration | Real structured invocation where safe, version/provenance checks, cancellation, timeout, unavailable/access-denied behavior, offline behavior, hostile input, update/rollback behavior, and cleanup. |
| End-to-end | Complete primary journey in clean and upgraded environments on the active Codex machine, including interruption, recovery, export, restart, repair, and uninstall where applicable. |
| Accessibility | Keyboard, focus, names/roles/states, screen-reader semantics/announcements, scaling, contrast, reduced resolution, plain-language errors, cancellation, and recovery. |
| Modest-hardware | Same-machine no-GPU, conservative concurrency, low-priority, low-space, offline/interrupted-network, large-input, bounded-cache/temp/database, and graceful-degradation evidence. |
| Security/privacy | Threat-model cases, privilege/IPC boundaries, malicious input, path/junction attacks, secrets, support-bundle sanitization, package/update provenance, logging, and no-telemetry default. |
| User evidence | Representative operator/recipient/proxy walkthroughs on the active Codex machine, terminology, misunderstood actions, recovery, and usefulness. |

## 4. Allowed environments

All environments below run on the active Codex machine:

- the host Windows installation for safe read-only or explicitly approved bounded tests;
- a clean clone or separate worktree;
- a clean virtual machine;
- Windows Sandbox where suitable;
- a container for portable tooling/tests;
- a disposable virtual disk or isolated test workspace;
- deterministic fakes, stubs, captured/sanitized provider samples, and synthetic fixtures; and
- process/resource constraints that are safe and reversible.

These environments improve isolation and reproducibility. They do not constitute a multi-hardware test matrix.

Not required or authoritative:

- a second physical computer;
- a lower-performance machine;
- a volunteer/partner device pool;
- GitHub Actions or another cloud runtime matrix;
- an external CI hardware farm; or
- a physical set covering every storage, battery, manufacturer, TPM, display, port, or network class.

An optional remote check may be used for non-authoritative convenience only if it introduces no release dependency and no secret/data risk. Its result cannot substitute for required active-machine evidence.

## 5. Reference-machine record

Before resource-sensitive or release testing, update `docs/testing/reference-machine-profile.md` with the sanitized active-machine profile required by `LOW_SPEC.md`.

Every test record that depends on Windows behavior or performance references that profile revision. Never include serial numbers, asset tags, device names, usernames, SSIDs, IP addresses, credentials, recovery keys, personal paths, screenshots, or raw logs.

## 6. Deterministic fixture policy

Use the smallest fixture capable of disproving the behavior.

Fixtures must be:

- deterministic and versioned;
- non-sensitive;
- attributable or project-created;
- independently runnable;
- bounded in size;
- accompanied by expected result and hash when used for release evidence; and
- safe to delete and recreate.

Provider and hardware variants are represented through synthetic or captured sanitized data where the active machine cannot expose them. Examples include no battery, malformed battery XML, unavailable storage counters, TPM access denied, unsupported CPU, Secure Boot disabled, camera absent, multiple disks, provider timeout, or managed-device indicators.

A fixture proves the application’s response to the represented data. It does not prove identical behavior on every real device or provider implementation.

## 7. Defect workflow

When a test exposes a defect:

1. stop the broad run when enough evidence exists to isolate it;
2. reduce it to the smallest practical deterministic reproduction;
3. add or update a focused regression test;
4. run that regression first;
5. run the related targeted set;
6. run a broader tier only when its trigger applies;
7. preserve the failed and corrected scenario evidence; and
8. update `STATUS.md` and the task evidence with exact commands and durations.

Blindly rerunning a flaky test is not success. Flaky tests are defects. Temporary quarantine requires an owner, task, reason, risk assessment, and removal condition.

## 8. Test isolation and safety

Tests must be deterministic, isolated, idempotent, and independent of execution order wherever practical.

Use:

- temporary workspaces and unique internal IDs;
- controlled clocks and random seeds;
- network/provider stubs;
- disposable databases and virtual disks;
- explicit cleanup verification;
- bounded timeouts and cancellation; and
- restore/checkpoint records before any approved mutating host or VM test.

Do not:

- alter firmware, TPM, Secure Boot, ownership/management controls, activation, or unsupported compatibility settings;
- use unknown removable media;
- run destructive stress by default;
- disable the host’s security controls merely to create a test case;
- place secrets or personal data in fixtures/logs; or
- disrupt the active machine’s network, storage, or operating state without an explicit task, checkpoint, recovery plan, and human approval.

## 9. Manual hardware-test workflow

ThirdLife’s guided manual hardware tests remain a product feature. Their development verification is not a hardware-lab exercise.

At TL-0008:

- specify test IDs, instructions, allowed result states, evidence classes, pause/resume, cleanup, and safety boundaries;
- test state transitions and unavailable/failure paths deterministically;
- do not execute the old `MHT-001`–`MHT-021` physical device-pool walkthrough; and
- do not treat missing peripherals or equipment as blockers.

At later explicit tasks:

- a representative operator may walk through capabilities available on the active Codex machine;
- absent capabilities use deterministic `Not available`/failure fixtures;
- cold boot is performed only at its named gate on the active machine when required; and
- the evidence is limited to workflow usability and that exact machine.

A pass means the specified workflow was truthfully completed. It is not hardware certification.

## 10. Accessibility execution

Automated UI Automation and component tests run at quick/targeted scope where possible. Human keyboard/screen-reader/scaling walkthroughs run only when the task or gate requires them, always on the active Codex machine.

At minimum, record:

- keyboard-only completion;
- visible focus and logical order;
- control names, roles, states, relationships, and announcements;
- Narrator and, when installed/approved, NVDA behavior;
- 200% scaling and reduced resolution;
- high contrast and color independence;
- progress/cancellation/recovery; and
- explicit limitations.

## 11. Evidence record

Every test execution record contains:

- task ID;
- source commit and branch;
- test tier;
- exact command or human procedure revision;
- start/end time and duration;
- active reference-machine profile revision;
- hosted environment and constraint profile, if any;
- fixture/workload IDs and hashes;
- pass, fail, blocked, or not run result;
- durable artifact path;
- cleanup/recovery result;
- defect/limitation reference; and
- tests not run with rationale.

Completion reports state branch, commit, push status, clean-tree status, tests by tier/command/duration/result, skipped tiers and why, and why the selected scope was sufficient.

## 12. Current TL-0008 supersession

The following prior instruction is historical and must not be executed as the current gate:

- procedure: `TL-0008 draft 1`;
- source commit: `4fa3ea050fd5e9985fde9cc8218281698d371cc8`;
- procedure digest: `ef150dbf14b5db208582b7b526c7e0c6d0a5b912736e9e6519b8918abcf0928b`;
- former requirement: physical device pool plus `MHT-001`–`MHT-021` walkthrough.

Preserve the old text only as a superseded historical record with a prominent **DO NOT EXECUTE** notice. The active TL-0008 contract is the same-machine plan in `TASKS.yaml` and `TL-0008_TRANSITION.md`.

## 13. Release evidence and claim limits

A release evidence package includes:

- test manifest with quick, targeted, full, and extended commands, durations, results, and skipped tiers;
- active reference-machine profile;
- clean-clone result;
- same-machine constraint settings and workload hashes;
- resource measurements and regression comparison;
- accessibility/security/privacy/lifecycle results;
- defect disposition and targeted reruns; and
- explicit limitations.

Release notes may say that the product is **designed for modest hardware** and report observed same-machine results. They may not claim broad hardware support, manufacturer coverage, lower-performance-device validation, or minimum specifications that were not actually tested.
