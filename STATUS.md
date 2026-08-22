# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-08-22  
**Snapshot preparation time:** 2026-08-22T10:04:50+02:00  
**Bundle baseline:** 0.3.1  
**Portfolio baseline:** ThirdLife Software Portfolio v2.1  
**Current milestone:** M0 — Foundation and product contract  
**Current task:** `TL-0010` — Validate the M0 foundation gate  
**Action state:** in progress; manifest-bound candidate and exact-commit gate verification pending

## Current state

`TL-0010` is the only selected task. All nine transitive M0 predecessors, `TL-0001` through `TL-0009`, are `done` with non-empty evidence. The candidate gate record is [`artifacts/gates/M0-foundation.md`](artifacts/gates/M0-foundation.md); it indexes predecessor closure, the eight gate acceptance criteria, proposed named M0 ownership, preserved licence/redistribution limitations, the clean-checkout method, verification evidence, risk/boundary review, and the exact human approval statement.

The gate record and validator are still candidate work. Clean-checkout Quick and Full have not yet run against a manifest-bound TL-0010 checkpoint, and no fresh M0 project-owner/security/privacy/licence acknowledgement is recorded. `TL-0010` therefore remains `in_progress` and cannot unlock M1.

The previously approved `AMD-2026-08-22-ADR-0009` amendment remains complete at baseline `1c2aeff4b6517d676a3fc127fe1f912fb6b6c516`. TL-0010 does not change ADR numbering, the task graph, a frozen decision, the product boundary, or that amendment's historical evidence.

## Gate contract state

| Area | Current state | Next proof |
|---|---|---|
| M0 predecessors | Pass: all nine are `done` with evidence | Revalidated by live bundle/task-graph checks |
| Gate artifact | Candidate created and structurally governed | Manifest and exact candidate checkpoint |
| Named owners | Historical security/privacy/licence owners preserved; explicit M0 roles proposed | Exact human acknowledgement after technical verification |
| Clean-checkout Quick | Pending | Run `.\eng\verify.ps1 -Tier Quick` from the exact published candidate in a disposable same-machine clone |
| Full tier | Pending; inherited Smart App Control risk remains | Run `.\eng\verify.ps1 -Tier Full` without disabling or bypassing host security |
| Extended tier | Not triggered | None |
| Gate decision | Pending | Passing required verification, then exact human sign-off |

## Git state

| Field | Verified value |
|---|---|
| Remote | `origin` → GitHub repository `PikkuJanne/ThirdLife` |
| Branch | `codex/tl-0010-m0-foundation-gate` |
| Source baseline | `1c2aeff4b6517d676a3fc127fe1f912fb6b6c516` — published bundle 0.3.1 ADR-reservation checkpoint |
| History handling | Continued from fetched local/upstream equality; no reset, rebase, force push, or history rewrite |
| Candidate checkpoint | The first manifest-bound commit containing this status, the M0 gate record, validator regressions, and `TL-0010: in_progress`; resolve from the published branch tip |
| Publication state | Pending candidate commit and push |

The configured SSH remote rejects unattended public-key authentication on this machine. Publication uses GitHub CLI's authenticated HTTPS credential bridge without changing the configured remote or exposing a credential.

## Verification evidence

| Scope | Result | Duration / revision |
|---|---|---|
| Pre-change bundle and repository validators | Passed: 91 tasks, 8 milestones, 66 decisions, valid DAG; 26 projects, 26 locks, 24 governed components | 3.751 s combined at `1c2aeff4b6517d676a3fc127fe1f912fb6b6c516` |
| Focused M0 gate-record regressions | 13/13 passed, including lifecycle mismatch, predecessor closure/count, real commit/blob digest binding, declared-owner attribution, matrix digest, retained limitations, affirmative blanket-right rejection, and hidden-content rejection | 1.243 s focused run |
| Complete Python regression suite | 162/162 passed after independent-review hardening | 115.723 s tests; 00:01:56.664 wall |
| Working-tree governed Quick | Passed 162 Python tests, bundle/manifest controls, repository boundaries, package locks, and licence controls after independent-review hardening | 108.033 s tests; 00:01:53.042 wall; candidate working tree |
| Exact-commit clean-clone Quick | Pending | Run from published candidate on active Codex machine |
| Exact-commit clean-clone Full | Pending | Required milestone-gate check; no Extended tier |

### Known Full-tier risk

An earlier non-TL-0010 Full attempt passed validators, locked restore, formatting, and a zero-warning Release build before Windows Smart App Control blocked two unsigned Release test-assembly loads. That observation is not current gate evidence. Smart App Control has not been and will not be disabled or bypassed. If the exact TL-0010 candidate reproduces the block, the truthful task state is `blocked`, even when clean-checkout Quick and the Release build pass.

The first wrapper preflight omitted the process-scoped PowerShell execution-policy argument and Windows rejected the script before the verifier loaded (exit 1; 0.421 s). The successful command used the repository's previously evidenced `-ExecutionPolicy Bypass` process argument; it changed no persistent policy and did not weaken Smart App Control or Code Integrity.

## Historical TL-0008 transition

The superseded `TL-0008` draft-1 procedure remains preserved at source commit `4fa3ea050fd5e9985fde9cc8218281698d371cc8`, with procedure SHA-256 `ef150dbf14b5db208582b7b526c7e0c6d0a5b912736e9e6519b8918abcf0928b`. No physical hardware walkthrough was performed for that transition. Its old device-pool language is historical and does not satisfy or block the current M0 gate.

## Boundary and risk impact

- **Project vacuum / sibling integration:** No sibling repository was browsed and no sibling source, runtime, data, service, profile, adapter, schema, SDK, plugin framework, dependency, or acceptance test was introduced.
- **Data / migration:** No runtime data location, database, schema, migration, retention, deletion, export, telemetry, personal-data, or uninstall behavior changed. The gate record is bounded repository evidence only.
- **Release interface:** `RELEASE_INTERFACE.md` remains an honest draft placeholder. No API, compatibility, release, product-licence, or redistribution promise was populated.
- **Security / privacy:** No executable, privilege, IPC, network, package, signing, logging, retention, or support-export behavior changed. The gate validator fails closed on lifecycle mismatch, stale evidence counts, owner placeholders, missing verification rows, matrix/review drift, removed rights limitations, and affirmative blanket-right claims.
- **Accessibility / low-spec:** No UI or runtime path was added. No manual accessibility, physical hardware, failure-injection, resource, cold-boot, or constrained scenario is claimed. Same-machine evidence remains non-certifying.

## Changed paths in the candidate working tree

- `artifacts/gates/M0-foundation.md`: new governed gate checklist and human approval target.
- `tools/validate_bundle.py`: M0 structure, lifecycle, ownership, verification, approval, and rights-limitation validation.
- `tools/tests/test_validate_bundle.py`: adversarial M0 gate-record regressions.
- `TASKS.yaml`: only `TL-0010.status`, from `backlog` to `in_progress`.
- `STATUS.md`: current branch, checkpoint plan, test state, risk, and next action.
- `BUNDLE_MANIFEST.sha256`: pending regeneration after candidate content stabilizes.

The unrelated untracked `ThirdLife_Two-Team_Software_Portfolio_Roadmap_v2.1.docx` was present before TL-0010 and remains untouched and unstaged.

## Outstanding

1. Synchronize and validate the final candidate manifest.
2. Commit and publish the exact candidate; verify local/upstream equality.
3. Clone that exact commit into a verified disposable same-machine path, bootstrap the hash-pinned Python environment, and run clean-checkout Quick and Full.
4. If Full passes, update the record to `review` and request the exact human acknowledgements. If Full is blocked, record the bounded defect and unblock condition and set `TL-0010` to `blocked`.
5. `TL-0010` may become `done` only after every automated and human requirement is evidenced.

## Next dependency-ready task

None. `TL-0101` remains dependency-blocked until `TL-0010` is `done`.
