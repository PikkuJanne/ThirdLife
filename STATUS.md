# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-08-27  
**Snapshot preparation time:** 2026-08-27T16:42:00+02:00  
**Bundle baseline:** 0.3.1  
**Portfolio baseline:** ThirdLife Software Portfolio v2.1  
**Current milestone:** M0 — Foundation and product contract  
**Current task:** `TL-0010` — Validate the M0 foundation gate  
**Action state:** blocked; the approved Windows Sandbox rerun is prepared but has not yet produced a passing Full result

## Current state

`TL-0010` is the only selected task and is `blocked`. All nine transitive M0 predecessors, `TL-0001` through `TL-0009`, are `done` with non-empty evidence. The gate record is [`artifacts/gates/M0-foundation.md`](artifacts/gates/M0-foundation.md); it indexes predecessor closure, the five milestone exit criteria, eight task acceptance criteria, proposed named M0 ownership, preserved licence/redistribution limitations, exact-candidate verification, risk/boundary review, blocker, and future exact human approval statement.

Published candidate `17975419badd4154b82895d9d92a4a904790c7c0` passed clean-checkout Quick. Its Full run passed all Python/governance checks, locked restore, formatting, and a Release build with 0 warnings and 0 errors, then failed during Release tests because Windows Application Control blocked four unsigned ThirdLife application DLL loads with `0x800711C7`. Windows Sandbox is now installed, and Janne Vuorela approved it on 2026-08-27 as the same-machine hosted environment for the exact rerun. The one-command fail-closed launcher at `eng/run-tl0010-sandbox.ps1` and its bounded guest runner are implemented and pass their focused contract suite; the hosted Quick/Full run itself is not yet evidence. Full is still not green, fresh M0 acknowledgements have not been requested, and M1 remains locked.

The previously approved `AMD-2026-08-22-ADR-0009` amendment remains complete at baseline `1c2aeff4b6517d676a3fc127fe1f912fb6b6c516`. TL-0010 does not change ADR numbering, the task graph, a frozen decision, the product boundary, or that amendment's historical evidence.

## Gate contract state

| Area | Current state | Next proof |
|---|---|---|
| M0 predecessors | Pass: all nine are `done` with evidence | Revalidated by live bundle/task-graph checks |
| Gate artifact | Blocked record created and structurally governed | Keep exact candidate/digest and append-only evidence intact |
| Named owners | Historical security/privacy/licence owners preserved; explicit M0 roles proposed | Request exact human acknowledgement only after Full passes |
| Clean-checkout Quick | Pass at `17975419badd4154b82895d9d92a4a904790c7c0` | 162 tests plus all governed static/manifest/repository controls |
| Hosted harness | Implemented; 14/14 focused harness contracts and the 176-test working-tree Quick suite pass | Publish the clean checkpoint, run launcher preflight, then execute the one-command Sandbox procedure |
| Full tier | Host attempt remains blocked; Windows Sandbox is installed and approved for the rerun | Run the one-command hosted procedure, require Quick and Full to pass, and retain only the bounded result |
| Extended tier | Not triggered | None |
| Gate decision | Blocked | Do not approve or mark `done` until Full passes and every human acknowledgement is attached |

## Git state

| Field | Verified value |
|---|---|
| Remote | `origin` → GitHub repository `PikkuJanne/ThirdLife` |
| Branch | `codex/tl-0010-m0-foundation-gate` |
| Source baseline | `1c2aeff4b6517d676a3fc127fe1f912fb6b6c516` — published bundle 0.3.1 ADR-reservation checkpoint |
| History handling | Continued from fetched local/upstream equality; no reset, rebase, force push, or history rewrite |
| Verification candidate | `17975419badd4154b82895d9d92a4a904790c7c0`; gate-record candidate SHA-256 `b4dfbc2fd66bd869ee10a4332ab8089c9f5c3586b378d3a99a095763e18df153` |
| Publication state | Published and fetched; local/upstream equality 0 ahead and 0 behind before clean-clone verification |
| Hosted rerun method | Windows Sandbox installed on the active physical machine; exact-candidate source and host tool roots mapped read-only; one writable bounded result directory; reviewed mutation declaration plus fail-closed static/behavioral guards |

The configured SSH remote rejects unattended public-key authentication on this machine. Publication uses GitHub CLI's authenticated HTTPS credential bridge without changing the configured remote or exposing a credential.

## Verification evidence

| Scope | Result | Duration / revision |
|---|---|---|
| Pre-change bundle and repository validators | Passed: 91 tasks, 8 milestones, 66 decisions, valid DAG; 26 projects, 26 locks, 24 governed components | 3.751 s combined at `1c2aeff4b6517d676a3fc127fe1f912fb6b6c516` |
| Focused M0 gate-record regressions | 13/13 passed, including lifecycle mismatch, predecessor closure/count, real commit/blob digest binding, declared-owner attribution, matrix digest, retained limitations, affirmative blanket-right rejection, and hidden-content rejection | 1.243 s focused run |
| Hosted harness contract regressions | 14/14 passed, including exact candidate/digest, Quick-before-Full, strict bounded-result states, missing success markers, invalid blocked/postflight evidence, physical-host identity refusal, live remote binding, mapping permissions, Code Integrity shape/types, prohibited trust mutation, and PowerShell parsing | 12.425 s tests; 13.196 s wall; Windows PowerShell 5.1-backed behavioral validation |
| Complete Python regression suite | 176/176 passed after hosted-harness hardening | 121.337 s tests within the governed Quick run |
| Working-tree governed Quick | Passed 176 Python tests, bundle/manifest controls, repository boundaries, package locks, and licence controls | 126.732 s wall; pre-checkpoint working tree |
| Bounded live remote query | Passed; exact governed remote branch resolved to the current published pre-harness checkpoint without changing repository state | 30 s bound; completed in 2.7 s wall including invocation overhead |
| Exact-commit clean-clone Quick | Passed at `17975419badd4154b82895d9d92a4a904790c7c0`; clone tracked-clean | Bootstrap 7.331 s; 162 tests 110.348 s; complete command 00:01:55.378 |
| Exact-commit clean-clone Full | Blocked: exit 1 in Release tests after 162 Python tests, validators, locked restore, formatting, and 0-warning/0-error Release build passed | 162 tests 110.313 s; build 17.50 s; attempt 00:03:01.538 |
| Blocked-state governed Quick | Passed all 162 Python tests and live bundle/manifest/repository/lock/supply-chain/licence/CI controls after recording the Full blocker | 162 tests 118.166 s; same active machine |
| Focused failure diagnosis | Four one-project reruns exited 1; four application DLLs `NotSigned`, only `:$DATA`; Code Integrity event IDs 3033/3077 under the enforced Smart App Control policy | Packages 2.552 s; Reports 2.226 s; Persistence/Verification bounded reruns; no raw log retained |
| Disposable-clone cleanup | Passed; exact target/commit verified, read-only Git pack attributes normalized after an initial bounded delete failure, no clone directory remains | Same session; no repository or user data removed |

### Current Full-tier blocker

The exact candidate failed one assembly-contract test in each of `ThirdLife.Packages.Tests`, `ThirdLife.Persistence.Tests`, `ThirdLife.Reports.Tests`, and `ThirdLife.Verification.Tests`. Application Control rejected loads of `ThirdLife.Packages.dll`, `ThirdLife.Persistence.dll`, `ThirdLife.Reports.dll`, and `ThirdLife.Verification.dll` with `0x800711C7`. Nine other .NET test projects passed 10 tests before the aggregate command exited 1.

Focused reruns reproduced all four blocks. Each application DLL is unsigned and has no `Zone.Identifier` stream, so changing clone/worktree location or removing alternate streams is not a remedy. Smart App Control and Code Integrity were not disabled or bypassed. The safe unblock condition is an approved same-machine hosted environment that can execute the governed Full command without weakening host security, or later governed signing/lifecycle work followed by an exact Full rerun.

The first wrapper preflight omitted the process-scoped PowerShell execution-policy argument and Windows rejected the script before the verifier loaded (exit 1; 0.421 s). The successful command used the repository's previously evidenced `-ExecutionPolicy Bypass` process argument; it changed no persistent policy and did not weaken Smart App Control or Code Integrity.

## Historical TL-0008 transition

The superseded `TL-0008` draft-1 procedure remains preserved at source commit `4fa3ea050fd5e9985fde9cc8218281698d371cc8`, with procedure SHA-256 `ef150dbf14b5db208582b7b526c7e0c6d0a5b912736e9e6519b8918abcf0928b`. No physical hardware walkthrough was performed for that transition. Its old device-pool language is historical and does not satisfy or block the current M0 gate.

## Boundary and risk impact

- **Project vacuum / sibling integration:** No sibling repository was browsed and no sibling source, runtime, data, service, profile, adapter, schema, SDK, plugin framework, dependency, or acceptance test was introduced.
- **Data / migration:** No runtime data location, database, schema, migration, retention, deletion, export, telemetry, personal-data, or uninstall behavior changed. The gate record is bounded repository evidence only.
- **Release interface:** `RELEASE_INTERFACE.md` remains an honest draft placeholder. No API, compatibility, release, product-licence, or redistribution promise was populated.
- **Security / privacy:** No product executable, privilege, IPC, signing, logging, retention, telemetry, or support-export behavior changed. The test-only harness adds a disposable Sandbox execution path with required restore/audit networking, six read-only source/tool mappings, one writable bounded evidence mapping, normalized policy-state observations, exact schemas, discarded raw logs, and fail-closed mutation/provenance checks. The `security_mutation_attempted` field is a reviewed harness declaration, not independent operating-system instrumentation.
- **Accessibility / low-spec:** No product UI or product runtime path was added. The hosted constraint profile assigns 8192 MiB to one disposable guest and records only active-machine evidence; it makes no lower-memory, manual accessibility, physical-hardware, failure-injection, resource, cold-boot, constrained-device, or cross-hardware claim.

## Changed paths on the TL-0010 branch

- `artifacts/gates/M0-foundation.md`: new governed gate checklist and human approval target.
- `eng/run-tl0010-sandbox.ps1`: one-command host preflight, Sandbox configuration, bounded result validation, and verified cleanup.
- `eng/run-tl0010-sandbox-guest.ps1`: internal exact-candidate bootstrap, guest policy observation, Quick-before-Full execution, bounded evidence, and disposable shutdown.
- `tools/validate_bundle.py`: M0 structure, lifecycle, ownership, verification, approval, and rights-limitation validation.
- `tools/tests/test_validate_bundle.py`: adversarial M0 gate-record and hosted-harness regressions.
- `TASKS.yaml`: only TL-0010 execution fields—`status`, append-only `evidence`, and `blocked_reason`—with the final state `blocked`.
- `STATUS.md`: current branch, checkpoint plan, test state, risk, and next action.
- `BUNDLE_MANIFEST.sha256`: synchronized to the governed blocked-state evidence after final validation.

The unrelated untracked `ThirdLife_Two-Team_Software_Portfolio_Roadmap_v2.1.docx` was present before TL-0010 and remains untouched and unstaged.

## Outstanding

1. Finish validation and publication of the governed Sandbox harness without touching the unrelated untracked DOCX.
2. From the clean published harness checkpoint, run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\run-tl0010-sandbox.ps1`; no guest interaction or manual log handling is required.
3. Require the bounded result to prove exact candidate/digest, Quick pass, Full pass, unchanged observed guest policy state, tracked-clean source, closed Sandbox, and verified staging cleanup. Do not weaken host or guest security settings.
4. Record the exact hosted result. Only a passing result may move `TL-0010` to `review` and trigger the exact project-owner/security/privacy/licence acknowledgements.
5. Mark `done` only after the complete automated and human evidence contract passes.

## Next dependency-ready task

None. `TL-0101` remains dependency-blocked until `TL-0010` is `done`.
