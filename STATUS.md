# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-08-21  
**Bundle baseline:** 0.3.0  
**Portfolio baseline:** ThirdLife Software Portfolio v2.1  
**Current milestone:** M0 — Foundation and product contract  
**Current task: `TL-0008` — Define the same-machine validation system and manual-test specification**

## Current state

The v0.3.0 governance and task-contract transition is complete on the active Codex machine, and `TL-0008` is `done`. The former physical-device procedure is archived as historical material and the active testing contract uses tiered, risk-based, same-machine evidence. No physical hardware walkthrough was performed or is required for revised `TL-0008`.

The commit containing this file is the TL-0008 checkpoint. Because a commit cannot embed its own hash, resolve that identity with `git rev-parse HEAD`; the session completion report records the resolved hash and the post-push equality check.

## Verified Git state

| Field | Verified value |
|---|---|
| Snapshot time before checkpoint commit | `2026-08-21T14:12:31+02:00` |
| Remote | `origin` → GitHub repository `PikkuJanne/ThirdLife` over SSH |
| Branch | `codex/tl-0008-device-test-procedure` |
| HEAD at session start | `4fa3ea050fd5e9985fde9cc8218281698d371cc8` |
| HEAD before checkpoint commit | `4fa3ea050fd5e9985fde9cc8218281698d371cc8` |
| TL-0008 checkpoint HEAD | The commit containing this file; resolve with `git rev-parse HEAD` |
| Upstream | `origin/codex/tl-0008-device-test-procedure` |
| Upstream commit at session start | `4fa3ea050fd5e9985fde9cc8218281698d371cc8` |
| Initial divergence | `0` ahead, `0` behind after authenticated fetch |
| Historical source relation | Current at session start; no reset or history rewrite performed |
| Pre-checkpoint upstream comparison | `4fa3ea050fd5e9985fde9cc8218281698d371cc8`; `0` ahead, `0` behind |
| Checkpoint push invariant | Before final handoff, current HEAD must equal the fetched upstream; the resolved hash and comparison belong in the completion report |

The configured SSH remote rejected unattended public-key authentication on this machine. Synchronization used GitHub CLI's authenticated HTTPS credential bridge without changing the configured remote or exposing a credential. The checkpoint is pushed through the same bounded bridge and compared with the fetched upstream before handoff.

## Historical reference superseded

| Item | Historical value |
|---|---|
| Procedure | `TL-0008 draft 1` |
| Source commit | `4fa3ea050fd5e9985fde9cc8218281698d371cc8` |
| Normalized procedure digest | `ef150dbf14b5db208582b7b526c7e0c6d0a5b912736e9e6519b8918abcf0928b` |
| Former active paths | `docs/testing/manual-hardware-tests.md`; `docs/testing/device-matrix.md` |
| Former action | Build a physical device pool and execute `MHT-001`–`MHT-021` |
| Current disposition | Superseded and preserved under `docs/history/` with a **DO NOT EXECUTE** notice |

The digest was reproduced from the former governed normalization algorithm before the active files were replaced. `MHT-001`–`MHT-021` remain product-workflow specifications only; none was run in this transition.

## Task-contract migration

The supplied 0.3.0 contracts contained the same 91 task IDs as the live repository. The governed merge replaced immutable contract fields while preserving each task's prior `status`, `evidence`, and `blocked_reason` presence and value. The only later mutable edit is the authorized `TL-0008` lifecycle/evidence update.

| Check | Result |
|---|---|
| Pre-merge live `TASKS.yaml` SHA-256 | `610bc8a54f48bf57eb40c3b83a98dd01587cdde3c44381939416452ac8b5d4d5` |
| Immediate post-merge SHA-256 | `213451fdcc9c7c7ea6c863dabad434134d5be181aa23da9ae968e34ee23027ff` |
| Task-ID comparison | 91 exact IDs; none added, removed, or duplicated |
| Mutable-field comparison | 0 unauthorized presence or value differences |
| `TL-0008` dependency state | `TL-0003` and `TL-0004` are `done` |
| `TL-0008` transition | Historical `review` → authorized `in_progress` → `review` → `done` after verification |

## Verification state

| Tier/check | Command | Duration | Result |
|---|---|---:|---|
| Baseline environment probe | `python tools/validate_bundle.py` | 3.3 s | Failed before repository validation: the WindowsApps Python environment lacks PyYAML; no dependency was installed |
| Baseline governed validator | `.venv\Scripts\python.exe tools/validate_bundle.py` | 1.78 s | Passed against the pre-transition v0.2 repository; `jsonschema` was unavailable and the validator used its custom structural checks |
| Focused validator regression | `.venv\Scripts\python.exe tools\tests\test_validate_bundle.py` | 14.686 s | Passed, 54/54 |
| Live governed validator | Activate `.venv`, then `python tools/validate_bundle.py` | 1.178 s | Passed: 91 tasks, 8 milestones, 66 decisions, valid DAG; optional `jsonschema` unavailable, custom structural checks used |
| Integration quick | `powershell.exe -File eng/verify.ps1 -Tier Quick` | 16.572 s | Passed: validator regression, governance, manifest, repository boundary, locked-package, and optional workflow checks |
| Final pre-commit quick | Same command after final task/evidence/status/manifest state | Recorded in the completion report | Required before commit; no application/runtime, full, or extended tier is included |
| Targeted | Not triggered | 0 s | Documentation, schema, governance, and validator migration did not change application/runtime behavior |
| Full | Not triggered | 0 s | Revised `TL-0008` contract declares no full-tier trigger |
| Extended/stress | Explicitly not triggered | 0 s | Scenarios are defined for later invoking tasks; none is run at revised `TL-0008` |
| Manual `MHT-001`–`MHT-021` | Prohibited for this transition | 0 s | Not run; specification only |

## Machine and evidence scope

The sanitized reference profile is `REF-CODEX-001`, revision `2026-08-21.1`. It records only coarse Windows, CPU/memory, storage-class/free-space-bucket, GPU-presence, and toolchain facts needed to interpret reproducible development evidence. It contains no machine name, serial or asset ID, account name, network identifier, credential, personal path, donor/recipient data, or personal-media detail.

Single-machine constrained scenarios improve regression detection and architectural discipline, but they do not prove behavior on every modest computer, manufacturer, storage type, battery state, firmware configuration, or peripheral set. They are not hardware certification.

## Continuation state

- Working tree before checkpoint: expected TL-0008 tracked/new files plus one pre-existing unrelated untracked roadmap DOCX. The unrelated document is preserved untouched and excluded from staging. After push, this is the only expected working-tree item.
- Removed active draft: `docs/testing/device-matrix.md`, only after both former files matched the named source commit exactly in the historical archive. The deletion is recoverable from Git history and the archive.
- Boundary impact: no sibling repository, private interface, or cross-project dependency was inspected or introduced.
- Data/migration impact: governance/task-contract files and sanitized evidence templates only; no product data or persistent schema migration.
- Release-interface impact: the draft placeholder now records the same-machine evidence scope and explicitly disclaims cross-hardware certification.
- Next dependency-ready task after verified TL-0008 completion: `TL-0005` — Define privacy/logging model. It has not been started.

## Changed paths

- Governance and handoff: `ACCESSIBILITY.md`, `AGENTS.md`, `BUNDLE_MANIFEST.sha256`, `CHANGELOG.md`, `CODEX_START_PROMPT.md`, `CODEX_TL0008_TRANSITION_PROMPT.md`, `DECISIONS.md`, `DEVELOPMENT_WORKFLOW.md`, `FUTURE_ASSEMBLY_NOTES.md`, `LOW_SPEC.md`, `PROJECT_BOUNDARY.md`, `README.md`, `RELEASE_INTERFACE.md`, `ROADMAP.md`, `SECURITY.md`, `STATUS.md`, `TASKS.schema.json`, `TASKS.yaml`, `TESTING.md`, and `TL-0008_TRANSITION.md`.
- Governed details: `docs/change-control.md` and `docs/security/abuse-cases.md`.
- Testing contracts: `docs/testing/accessibility-matrix.md`, `docs/testing/capability-risk-matrix.md`, `docs/testing/failure-injection.md`, `docs/testing/manual-hardware-tests.md`, `docs/testing/reference-machine-profile.md`, and `docs/testing/same-machine-constraints.md`.
- Historical transition: added `docs/history/TL-0008-draft-1-superseded.md`; removed the superseded active `docs/testing/device-matrix.md` after exact archive verification.
- Verification tooling: `eng/verify.ps1`, `eng/verify.sh`, `tools/merge_task_contracts.py`, `tools/validate_bundle.py`, `tools/tests/test_validate_bundle.py`, and a success-message-only update in `tools/validate_repository.py`.
