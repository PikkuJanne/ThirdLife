# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-08-27  
**Snapshot preparation time:** 2026-08-27T17:46:13+02:00  
**Bundle baseline:** 0.3.1  
**Portfolio baseline:** ThirdLife Software Portfolio v2.1  
**Current milestone:** M0 — Foundation and product contract — complete  
**Current task:** `TL-0010` — Validate the M0 foundation gate — `done`  
**Action state:** done; automated verification and the exact project-owner/security/privacy/dependency-licence acknowledgements are complete

## Current state

`TL-0010` is `done`. All nine transitive M0 predecessors, `TL-0001` through `TL-0009`, are also `done` with non-empty evidence. The approved gate record is [`artifacts/gates/M0-foundation.md`](artifacts/gates/M0-foundation.md); it indexes predecessor closure, the five milestone exit criteria, eight task acceptance criteria, named M0 ownership, preserved licence/redistribution limitations, exact-candidate verification, risk/boundary review, historical host limitation, and attributable human approval.

Published candidate `17975419badd4154b82895d9d92a4a904790c7c0` passed its original clean-checkout Quick. Its direct-host Full attempt remains historically blocked because the enforced host policy rejected four unsigned ThirdLife application DLL loads with `0x800711C7`. Janne Vuorela approved installed Windows Sandbox on 2026-08-27 as the same-machine hosted environment for the exact rerun. From published harness checkpoint `9f86bfa09571a2e027d868b1f9d1cee48fee4fe2`, the one-command runner then passed Quick in 106.316 s and the exact Full command in 1070.198 s. The guest Smart App Control state was `evaluation` before and after, the `registry_and_system_policy_files` fingerprint was unchanged, no security mutation was attempted, and host validation confirmed exact candidate/gate binding, tracked-clean state, Sandbox shutdown, and staging cleanup.

Janne Vuorela provided the exact section 9 statement on 2026-08-27, signing as Principal Software Architect & Sole Project Owner and completing the M0 security, privacy, dependency/licence, accessibility, modest-hardware, validation, product-boundary, and architecture acknowledgements. The signature is bound to candidate `17975419badd4154b82895d9d92a4a904790c7c0` and historical gate-record candidate SHA-256 `b4dfbc2fd66bd869ee10a4332ab8089c9f5c3586b378d3a99a095763e18df153`. Every section 5 limitation remains binding. M1 is unlocked but has not been started in this gate-completion session.

The previously approved `AMD-2026-08-22-ADR-0009` amendment remains complete at baseline `1c2aeff4b6517d676a3fc127fe1f912fb6b6c516`. TL-0010 does not change ADR numbering, the task graph, a frozen decision, the product boundary, or that amendment's historical evidence.

## Gate contract state

| Area | Current state | Next proof |
|---|---|---|
| M0 predecessors | Pass: all nine are `done` with evidence | Revalidated by live bundle/task-graph checks |
| Gate artifact | Approved record structurally governed and bound to the historical candidate blob | Preserve exact candidate/digest and append-only evidence |
| Named owners | Complete: every M0 role acknowledged by Janne Vuorela; historical security attribution preserved | None |
| Clean-checkout Quick | Pass at `17975419badd4154b82895d9d92a4a904790c7c0` | 162 tests plus all governed static/manifest/repository controls |
| Hosted harness | Pass: 15/15 focused contracts, 177-test working-tree Quick, published preflight, and bounded final result | Retain the exact schema-2 evidence and claim limits |
| Full tier | Pass in approved Windows Sandbox: Quick then Full through `tests`; direct enforced-host block remains a limitation | Retain the no-direct-host-compatibility and no-signing claim limits |
| Extended tier | Not triggered | None |
| Gate decision | Approved | M0 complete; section 5 limitations remain binding |

## Git state

| Field | Verified value |
|---|---|
| Remote | `origin` → GitHub repository `PikkuJanne/ThirdLife` |
| Branch | `codex/tl-0010-m0-foundation-gate` |
| Source baseline | `1c2aeff4b6517d676a3fc127fe1f912fb6b6c516` — published bundle 0.3.1 ADR-reservation checkpoint |
| History handling | Continued from fetched local/upstream equality; no reset, rebase, force push, or history rewrite |
| Verification candidate | `17975419badd4154b82895d9d92a4a904790c7c0`; gate-record candidate SHA-256 `b4dfbc2fd66bd869ee10a4332ab8089c9f5c3586b378d3a99a095763e18df153` |
| Publication state | Harness checkpoint `9f86bfa09571a2e027d868b1f9d1cee48fee4fe2` and Review-state handoff `f913c8ae242d9457bbe65a83f3852b5b4f8524fb` were published, fetched, and verified at local/upstream equality 0 ahead and 0 behind; the Approved-state completion checkpoint is pending publication |
| Hosted rerun method | Windows Sandbox installed on the active physical machine; exact-candidate source and host tool roots mapped read-only; one writable bounded result directory; reviewed mutation declaration plus fail-closed static/behavioral guards |

The configured SSH remote rejects unattended public-key authentication on this machine. Publication uses GitHub CLI's authenticated HTTPS credential bridge without changing the configured remote or exposing a credential.

## Verification evidence

| Scope | Result | Duration / revision |
|---|---|---|
| Pre-change bundle and repository validators | Passed: 91 tasks, 8 milestones, 66 decisions, valid DAG; 26 projects, 26 locks, 24 governed components | 3.751 s combined at `1c2aeff4b6517d676a3fc127fe1f912fb6b6c516` |
| Focused M0 gate-record regressions | 13/13 passed, including lifecycle mismatch, predecessor closure/count, real commit/blob digest binding, declared-owner attribution, matrix digest, retained limitations, affirmative blanket-right rejection, and hidden-content rejection | 1.243 s focused run |
| Hosted harness contract regressions | 15/15 passed, including exact candidate/digest, Quick-before-Full, strict bounded-result states, policy-method coherence/change rejection, invalid blocked/postflight evidence, physical-host identity refusal, live remote binding, mapping permissions, fail-closed Code Integrity fallback, prohibited trust mutation, and PowerShell parsing | 13.713 s; Windows PowerShell 5.1-backed behavioral validation |
| Complete Python regression suite | 177/177 passed after final policy-observation hardening | 103.602 s tests within the governed Quick run |
| Working-tree governed Quick | Passed 177 Python tests, bundle/manifest controls, repository boundaries, package locks, and licence controls | Tests 103.602 s; complete command not separately timed |
| Bounded live remote query | Passed; exact governed remote branch resolved to the current published pre-harness checkpoint without changing repository state | 30 s bound; completed in 2.7 s wall including invocation overhead |
| Published-checkpoint Sandbox preflight | Passed exact host/tool/candidate/gate checks and verified staging cleanup without launching the guest | 4.9 s at `9f86bfa09571a2e027d868b1f9d1cee48fee4fe2` |
| Exact-commit clean-clone Quick | Passed at `17975419badd4154b82895d9d92a4a904790c7c0`; clone tracked-clean | Bootstrap 7.331 s; 162 tests 110.348 s; complete command 00:01:55.378 |
| Historical direct-host Full | Blocked: exit 1 in Release tests after 162 Python tests, validators, locked restore, formatting, and 0-warning/0-error Release build passed | 162 tests 110.313 s; build 17.50 s; attempt 00:03:01.538 |
| Blocked-state governed Quick | Passed all 162 Python tests and live bundle/manifest/repository/lock/supply-chain/licence/CI controls after recording the Full blocker | 162 tests 118.166 s; same active machine |
| Focused failure diagnosis | Four one-project reruns exited 1; four application DLLs `NotSigned`, only `:$DATA`; Code Integrity event IDs 3033/3077 under the enforced Smart App Control policy | Packages 2.552 s; Reports 2.226 s; Persistence/Verification bounded reruns; no raw log retained |
| Hosted attempt 1 defect handling | Failed before Quick because the launcher omitted the Store Sandbox process identities; no result was accepted or retained; focused regression added | 23.489 s at `be7c64eecea9656e7b32593b77c0715cc5bacd9d`; corrected at `b03181e8a4756451c955117327b83a1a9f61d6c3` |
| Hosted attempt 2 defect handling | Failed closed in guest preflight because unelevated `CiTool` was unavailable; Quick and Full were not run; bounded schema-1 result retained | 64.019 s; run `24152eb7ea38ba9e3a07226592644a7a`; result SHA-256 `d131b1d2b662dff0a3bae9a23a53c04b6303f31416864319f5a818912d07d3d4` |
| Exact-candidate Windows Sandbox Quick and Full | Passed: Quick exit 0/marker true, then Full exit 0/marker true through `tests`; policy state/fingerprint unchanged; exact candidate/gate/clean checks, Sandbox close, and cleanup passed | Quick 106.316 s; Full 1070.198 s; guest interval 1192.232 s; run `64dda9491c7fddf8a9e9f18429a6957b`; result SHA-256 `170ff71314ea8c46d161e42bc1f6564b719f8b04e0d6bee371e01c67c8ce4dea` |
| Approved-state focused and governed Quick | Passed: 29/29 M0 lifecycle/harness tests, 178-test Quick, bundle/manifest/task-graph controls, and repository/lock/supply-chain/licence controls | Focused 20.267 s; Quick tests 144.185 s; live validators passed; `TL-0101` dependency-ready |
| Human M0 gate approval | Passed: Janne Vuorela signed the project-owner decision and acknowledged security, privacy, dependency/licence, accessibility, modest-hardware, validation, product-boundary, and architecture roles against the exact candidate/digest | 2026-08-27; human statement not timed; every section 5 limitation retained |
| Disposable-clone cleanup | Passed; exact target/commit verified, read-only Git pack attributes normalized after an initial bounded delete failure, no clone directory remains | Same session; no repository or user data removed |

### Historical direct-host limitation and hosted resolution

The exact candidate failed one assembly-contract test in each of `ThirdLife.Packages.Tests`, `ThirdLife.Persistence.Tests`, `ThirdLife.Reports.Tests`, and `ThirdLife.Verification.Tests`. Application Control rejected loads of `ThirdLife.Packages.dll`, `ThirdLife.Persistence.dll`, `ThirdLife.Reports.dll`, and `ThirdLife.Verification.dll` with `0x800711C7`. Nine other .NET test projects passed 10 tests before the aggregate command exited 1.

Focused reruns reproduced all four blocks. Each application DLL is unsigned and has no `Zone.Identifier` stream, so changing clone/worktree location or removing alternate streams is not a remedy. Smart App Control and Code Integrity were not disabled or bypassed. The approved same-machine Windows Sandbox execution satisfied the automated unblock condition without weakening host security. It does not prove compatibility with the enforced host policy, authorize signing, or erase the historical limitation.

The first hosted attempt exposed incomplete process tracking for the Store-delivered Sandbox process names and retained no result. The second failed closed before Quick because the unelevated guest could not enumerate Code Integrity policy through `CiTool`. The final schema-2 runner added validated Store process identities and a read-only, fail-closed fallback over the documented SAC registry state plus readable system-volume Code Integrity policy files. The fallback produced a stable nonzero fingerprint on the host, passed independent review with no P0/P1 finding, and then succeeded in the guest. EFI-resident policy enumeration is explicitly not claimed.

The first wrapper preflight omitted the process-scoped PowerShell execution-policy argument and Windows rejected the script before the verifier loaded (exit 1; 0.421 s). The successful command used the repository's previously evidenced `-ExecutionPolicy Bypass` process argument; it changed no persistent policy and did not weaken Smart App Control or Code Integrity.

## Historical TL-0008 transition

The superseded `TL-0008` draft-1 procedure remains preserved at source commit `4fa3ea050fd5e9985fde9cc8218281698d371cc8`, with procedure SHA-256 `ef150dbf14b5db208582b7b526c7e0c6d0a5b912736e9e6519b8918abcf0928b`. No physical hardware walkthrough was performed for that transition. Its old device-pool language is historical and does not satisfy or block the current M0 gate.

## Boundary and risk impact

- **Project vacuum / sibling integration:** No sibling repository was browsed and no sibling source, runtime, data, service, profile, adapter, schema, SDK, plugin framework, dependency, or acceptance test was introduced.
- **Data / migration:** No runtime data location, database, schema, migration, retention, deletion, export, telemetry, personal-data, or uninstall behavior changed. Two ignored bounded local result/summary pairs are retained—one preflight failure and the final pass; raw guest logs were discarded.
- **Release interface:** `RELEASE_INTERFACE.md` remains an honest draft placeholder. No API, compatibility, release, product-licence, or redistribution promise was populated.
- **Security / privacy:** No product executable, privilege, IPC, signing, logging, retention, telemetry, or support-export behavior changed. The test-only harness adds a disposable Sandbox execution path with required restore/audit networking, six read-only source/tool mappings, one writable bounded evidence mapping, normalized policy-state observations, exact schemas, discarded raw logs, and fail-closed mutation/provenance checks. The `security_mutation_attempted` field is a reviewed harness declaration, not independent operating-system instrumentation.
- **Accessibility / low-spec:** No product UI or product runtime path was added. The hosted constraint profile assigns 8192 MiB to one disposable guest and records only active-machine evidence; it makes no lower-memory, manual accessibility, physical-hardware, failure-injection, resource, cold-boot, constrained-device, or cross-hardware claim.

## Changed paths on the TL-0010 branch

- `artifacts/gates/M0-foundation.md`: new governed gate checklist and human approval target.
- `eng/run-tl0010-sandbox.ps1`: one-command host preflight, Sandbox configuration, bounded result validation, and verified cleanup.
- `eng/run-tl0010-sandbox-guest.ps1`: internal exact-candidate bootstrap, guest policy observation, Quick-before-Full execution, bounded evidence, and disposable shutdown.
- `tools/validate_bundle.py`: M0 structure, lifecycle, ownership, verification, approval, and rights-limitation validation.
- `tools/tests/test_validate_bundle.py`: adversarial M0 gate-record and hosted-harness regressions.
- `TASKS.yaml`: only TL-0010 execution fields—`status`, append-only `evidence`, and `blocked_reason`—with the final state `done`.
- `STATUS.md`: current branch, checkpoint plan, test state, risk, and next action.
- `BUNDLE_MANIFEST.sha256`: synchronized to the governed approved-state evidence after final validation.

The unrelated untracked `ThirdLife_Two-Team_Software_Portfolio_Roadmap_v2.1.docx` was present before TL-0010 and remains untouched and unstaged.

## Outstanding

None for `TL-0010`. The historical direct-host unsigned-assembly limitation and every gate section 5 claim limitation remain recorded; neither is an unresolved M0 blocker.

## Next dependency-ready task

`TL-0101` — Implement core job and evidence domain models. It is dependency-ready but was not started in this gate-completion session.
