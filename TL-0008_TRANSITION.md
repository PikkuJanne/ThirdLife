# TL-0008 Transition — From Hardware-Lab Walkthrough to Same-Machine Validation

**Effective date:** 2026-08-15  
**Applies to:** ThirdLife Setup Core, Team B/B1  
**Current task:** `TL-0008`  
**Authority:** user-approved portfolio v2.1 alignment and decisions `D-058`–`D-066`

## 1. Transition decision

The previous TL-0008 plan assumed a physical device pool, a designated lab/reference device, missing-equipment blockers, and immediate execution of `MHT-001`–`MHT-021`. That approach is no longer part of the project.

The active Codex machine is the only physical machine used for implementation, tests, benchmarks, clean environments, and release evidence. The project will not maintain a lab, second PC, lower-performance test machine, volunteer hardware pool, or external runtime matrix.

This change does not remove the manual hardware-test feature from ThirdLife. It changes how the feature and modest-hardware behavior are engineered and verified during development.

## 2. Superseded instruction

Preserve the following facts for audit history:

- procedure: `TL-0008 draft 1`;
- source commit: `4fa3ea050fd5e9985fde9cc8218281698d371cc8`;
- procedure digest: `ef150dbf14b5db208582b7b526c7e0c6d0a5b912736e9e6519b8918abcf0928b`;
- paths named by the old instruction:
  - `docs/testing/manual-hardware-tests.md`;
  - `docs/testing/device-matrix.md`;
- former action: assemble a physical device/equipment pool and execute `MHT-001`–`MHT-021`.

Do not delete the historical procedure without preserving a reviewable supersession record. Do not run it as current TL-0008 evidence.

Recommended history location:

```text
docs/history/TL-0008-draft-1-superseded.md
```

Place this banner at the top:

```text
SUPERSEDED — DO NOT EXECUTE

This procedure was replaced on 2026-08-15 by the ThirdLife Portfolio v2.1
single-machine validation baseline. It is retained only for audit history.
It is not a current task, test plan, gate, or release requirement.
```

Preserve the source commit and digest in the banner. The named commit is not a reset target.

## 3. Required file changes

### 3.1 Replace `device-matrix.md`

The former physical inventory becomes `docs/testing/capability-risk-matrix.md`.

The matrix maps each relevant variant to a software-verification method:

| Variant | Required coverage method |
|---|---|
| Different CPU/memory values | synthetic/captured provider fixtures plus active-machine smoke |
| HDD/SATA SSD/NVMe | deterministic provider samples and multiple-disk/system-volume cases |
| Battery/no battery/degraded values | battery XML fixtures, no-battery state, missing-field/error cases |
| TPM/Secure Boot states | enabled/disabled/unavailable/access-denied/locked-state fixtures plus read-only host observation |
| Unsupported CPU/OS | lifecycle/eligibility fixtures and policy tests |
| Cameras, microphones, ports, touch, docks | manual-workflow `Pass`/`Fail`/`Not available` state tests; bounded host observation when present |
| Network unavailable/interrupted | stubs, injected faults, or disposable hosted-environment controls |
| Low free space/slow destination | disposable virtual disk/volume or deterministic adapter constraint |
| Partial failure | fixture with preserved original result, defect/gap reference, and retest semantics |
| Cold boot | specification at TL-0008; execution only at `TL-0509` or a later named release trigger |

The matrix must say what each method proves and what it does not prove.

### 3.2 Rewrite `manual-hardware-tests.md`

Keep the manual test IDs and safe operator instructions where useful, but convert the document into a **product-workflow specification**, not an immediate device-pool runbook.

It must define:

- test ID and purpose;
- prerequisites and safety stop conditions;
- capability detection versus functional confirmation;
- allowed result states;
- allowed `Not available` reasons;
- evidence class;
- pause/resume and continuity behavior;
- cleanup/recovery;
- defect/gap linking;
- accessibility of the test UI/instructions; and
- limitation that a workflow pass is not hardware certification.

Add a prominent note that `MHT-001`–`MHT-021` are not executed during revised TL-0008.

### 3.3 Create `reference-machine-profile.md`

Record the sanitized active Codex machine profile from `LOW_SPEC.md`. The profile is for reproducibility, not device inventory. Use no serials, asset tags, usernames, device names, SSIDs, IPs, credentials, recovery keys, personal paths, screenshots, or raw logs.

### 3.4 Create `same-machine-constraints.md`

Define safe, reversible, independently invokable profiles for:

- baseline;
- no GPU;
- conservative concurrency;
- low process priority;
- low free space;
- offline;
- interrupted network;
- provider unavailable/access denied;
- slow destination; and
- representative large workload.

Each profile needs purpose, setup, scope, cleanup, evidence, and claim limitation.

### 3.5 Update `failure-injection.md`

Specify individual scenarios and later triggers. At TL-0008, do not run the broad matrix.

Each scenario must be separately invokable and include:

- injection method;
- expected checkpoint/journal state;
- expected user-visible recovery;
- cleanup;
- data-safety invariant;
- test tier; and
- task/gate trigger.

### 3.6 Update `accessibility-matrix.md`

Define keyboard, focus, UI Automation, Narrator/NVDA, scaling, contrast, reduced-resolution, progress, cancellation, and error-recovery cases. At TL-0008, create the matrix; do not claim the later human audit ran.

### 3.7 Update top-level governance

Apply the 0.3.0 versions of:

- `ROADMAP.md`;
- `DECISIONS.md`;
- `TASKS.yaml` without overwriting existing mutable task state;
- `AGENTS.md`;
- `CODEX_START_PROMPT.md`;
- `README.md`;
- `LOW_SPEC.md`;
- `TESTING.md`;
- `DEVELOPMENT_WORKFLOW.md`;
- `STATUS.md`;
- `RELEASE_INTERFACE.md`;
- `PROJECT_BOUNDARY.md`;
- `ACCESSIBILITY.md` where test-environment wording changes; and
- validator/schema/manifest files.

## 4. Task-state migration

Do not replace the live repository’s execution history with the template status values in a bundle.

When merging the new `TASKS.yaml` contract:

- update task objectives, deliverables, acceptance, verification, decisions, test-tier metadata, and portfolio metadata from 0.3.0;
- preserve the live value of each existing task’s:
  - `status`;
  - `evidence`;
  - `blocked_reason`;
- preserve any valid live task history/notes unless they conflict with the superseded procedure;
- remove a physical-device blocker created solely by the old TL-0008 requirement and record that it was superseded by `D-064` rather than silently deleting history; and
- do not mark later tasks done merely because their hardware prerequisites were removed.

Use `tools/merge_task_contracts.py` or perform an equivalent reviewed merge.

## 5. What to run now

TL-0008 is a documentation/governance task. Run:

1. bundle/task schema validation;
2. markdown/link checks available in the repository;
3. quick static checks affected by the governance changes;
4. searches for active prohibited dependencies; and
5. the clean-clone quick tier only when it is already available and safe.

Do not run now:

- the former physical `MHT-001`–`MHT-021` walkthrough;
- broad failure injection;
- accessibility audit matrices;
- low-resource extended matrices;
- real cold boot solely for TL-0008;
- package/update mutation; or
- full/extended release suites.

## 6. Required searches

Search binding documents and task contracts for active requirements such as:

```text
physical-device matrix
hardware lab
lab device
reference laptop
4 GB test device
8 GB test device
approved device pool
missing equipment blocker
authoritative Windows CI
remote runner matrix
```

Historical/supersession text may contain these terms only when clearly marked as retired. Active task contracts must not require them.

## 7. TL-0008 evidence record

The final task evidence should identify:

- branch and commit;
- source commit/digest of the superseded procedure;
- files replaced/created;
- task-state merge method and confirmation that mutable fields were preserved;
- validator and quick-tier commands, duration, and result;
- search result showing no active hardware-lab dependency;
- tests not run and why they were not triggered;
- active reference-machine profile path; and
- explicit statement that no physical walkthrough or cross-hardware certification was performed.

## 8. Completion and next task

After the revised TL-0008 contract is fully evidenced, set it to `done`, update `STATUS.md`, commit and push, and stop. Let `TASKS.yaml` dependency logic identify the next task. Do not opportunistically start `TL-0009`, `TL-0010`, or a later implementation task in the same session unless the user explicitly authorizes it.
