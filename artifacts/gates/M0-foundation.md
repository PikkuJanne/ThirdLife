# ThirdLife Setup Core — M0 foundation gate record

**Record status:** Approved — M0 gate complete  
**Task:** `TL-0010` — Validate the M0 foundation gate  
**Milestone:** `M0` — Foundation and product contract  
**Record revision:** 3  
**Prepared:** 2026-08-22; automated verification and final human approval completed 2026-08-27  
**Branch:** `codex/tl-0010-m0-foundation-gate`  
**Source baseline:** `1c2aeff4b6517d676a3fc127fe1f912fb6b6c516`  
**Verification candidate:** `17975419badd4154b82895d9d92a4a904790c7c0`  
**Reference-machine profile:** `REF-CODEX-001`, revision `2026-08-21.1`  
**Required test tier:** Full  
**Extended test tier:** Not triggered

## 1. Decision and claim boundary

This record is the human-readable checklist for the [`TL-0010`](../../TASKS.yaml) hybrid gate. It demonstrates whether the repository and governed M0 inputs are ready for implementation work. It does not authorize a preview, pilot, production release, package redistribution, sibling integration, broad hardware-support claim, accessibility-conformance claim, security guarantee, privacy guarantee, sanitization guarantee, or reliability certification.

The gate has four truthful states:

- **Candidate:** the checklist exists, but required verification or human evidence is incomplete.
- **Blocked:** a concrete technical or environmental condition prevents a required check from passing.
- **Review:** all required automated checks pass, but one or more declared human acknowledgements remain.
- **Approved:** every acceptance criterion, verification step, named-owner acknowledgement, and project-owner signature is attached; only this state permits `TL-0010` to become `done`.

An earlier predecessor approval proves only the exact artifact and revision it names. It does not silently become the fresh M0 acknowledgement required by this gate.

## 2. Predecessor closure

| Task | State | Durable M0 input | Gate treatment |
|---|---|---|---|
| `TL-0001` | `done`; 4 evidence entries | Solution/repository scaffold, central compiler policy, WPF boundary, and project graph | Accepted as the transitive foundation predecessor; current clean-checkout verification below revalidates the repository rather than treating the old build as current gate evidence. |
| `TL-0002` | `done`; 3 evidence entries | Deterministic verification, locked restore, warning-as-error build, tests, and GitHub continuity | Accepted as predecessor history. Its 2026-08-14 GitHub Actions run is not current M0 runtime evidence; the active-machine clean-checkout run below is required. |
| `TL-0003` | `done`; 3 evidence entries | Product contract, non-goals, glossary, change control, and project-vacuum governance | Accepted as current governed input, subject to the M0 responsibility acknowledgement below. |
| `TL-0004` | `done`; 5 evidence entries | Threat model, data flows, abuse cases, and residual-risk treatments | Exact historical security-owner approval retained; fresh M0 acknowledgement remains separate. |
| `TL-0005` | `done`; 7 evidence entries | Privacy classifications, retention guidance, logging/export contract, and redaction fixtures | Exact historical privacy-owner approval retained; fresh M0 acknowledgement remains separate. |
| `TL-0006` | `done`; 10 evidence entries | Dependency inventory, licence/rights matrix, deterministic SBOM controls, and limitations | Exact historical rights approvals retained; fresh M0 acknowledgement remains separate. |
| `TL-0007` | `done`; 8 evidence entries | Synthetic pilot fixtures, candidate policy values, profiles, and four generic capability records | Exact pilot-owner and renewed matrix approvals retained; no fixture is production evidence. |
| `TL-0008` | `done`; 4 evidence entries | Same-machine test system, reference profile, capability/risk coverage, manual-test, failure, accessibility, and constraint specifications | The revised contract is authoritative. The older draft-1 physical walkthrough language is superseded and is not M0 evidence. No device pool or missing-equipment approval is required. |
| `TL-0009` | `done`; 3 evidence entries | Eight accepted initial ADRs and the approved future ADR 0009 reservation | Accepted as architecture input, subject to the M0 responsibility acknowledgement below. |

The authoritative status and complete append-only evidence for every row remain in [`TASKS.yaml`](../../TASKS.yaml). This table is an index, not a replacement for that evidence.

The structured predecessor audit enumerated all 54 deliverables, 56 acceptance criteria, 22 verification items, and 4 predecessor human-evidence requirements across `TL-0001` through `TL-0009`. No item is waived by this summary. Current M0 verification and gate-specific acknowledgements are repeated below because historical predecessor evidence cannot satisfy a fresh gate requirement by implication.

## 3. Acceptance checklist

### M0 milestone exit criteria

| # | Milestone exit criterion | Candidate result |
|---:|---|---|
| 1 | The repository builds from a clean checkout on supported Windows on the active Codex machine. | Pass: the exact candidate completed locked restore, formatting, warnings-as-errors Release build, and Release tests in the approved same-machine Windows Sandbox environment. The historical enforced-host unsigned-assembly block remains a stated compatibility limitation, not a passing-host claim. |
| 2 | Product, non-goals, threat, privacy, accessibility, modest-hardware, dependency/licence, reference-profile, test-tier, and ADR inputs have named owners. | Pass: Janne Vuorela accepted every proposed M0 role and acknowledgement in section 4 while preserving PikkuJanne's historical security-model attribution. |
| 3 | No lab, physical-device pool, lower-performance test machine, external runtime matrix, or missing equipment is a dependency or release blocker. | Pass: the same-machine contract and explicit limitations require none of these inputs. |
| 4 | The Team B/B1 project-vacuum boundary and future B4 late-binding posture are explicit and validated. | Pass: authority review and live validators confirm the boundary. |
| 5 | The roadmap bundle, GitHub workflow, task graph, and repository rules are internally consistent and validated. | Pass: the exact published candidate completed governed Quick and Full in Windows Sandbox, the repository controls pass, and the final owner review is signed. |

### TL-0010 acceptance criteria

| # | `TL-0010` acceptance criterion | Candidate result | Evidence or unblock condition |
|---:|---|---|---|
| 1 | All predecessor tasks are done with evidence. | Pass | Section 2 and live task-graph validation; all nine transitive M0 predecessors are `done` with non-empty evidence. |
| 2 | The documented clean-checkout Quick command passes on the active Codex machine. | Pass | Exact published candidate `17975419badd4154b82895d9d92a4a904790c7c0` passed `.\eng\verify.ps1 -Tier Quick` from a disposable clean clone on the active machine. |
| 3 | Threat, privacy, accessibility, modest-hardware, dependency, and licence reviews have named owners. | Pass | Janne Vuorela signed the exact section 9 approval and acknowledged every section 4 assignment on 2026-08-27. Historical attribution and all section 5 limitations remain intact. |
| 4 | The reference profile, test tiers, same-machine constraints, and manual-test specification are approved without requiring a device pool. | Pass | The project owner approved the exact M0 input set in section 9. No physical device pool, second PC, lower-performance machine, or missing-equipment approval was required or inferred. |
| 5 | `PROJECT_BOUNDARY.md` states Team B/B1 and prohibits live sibling dependencies and B4 work. | Pass | [`PROJECT_BOUNDARY.md`](../../PROJECT_BOUNDARY.md) states the Team B/B1 project vacuum, active-machine-only validation, and future B4 late binding against frozen releases. |
| 6 | `RELEASE_INTERFACE.md` remains an honest draft rather than a speculative API. | Pass | [`RELEASE_INTERFACE.md`](../../RELEASE_INTERFACE.md) is a draft placeholder, leaves unknown fields `TBD`, and remains **not a shared application API** or early B4 adapter contract. |
| 7 | `STATUS.md` identifies branch, commit, tests, and next action without chat history. | Pass | [`STATUS.md`](../../STATUS.md) records the exact branch/candidate, published harness checkpoint, bounded hosted result, historical host limitation, cleanup, completed acknowledgements, and next dependency-ready task. |
| 8 | No unresolved contradiction exists among binding bundle files. | Pass | Bundle/repository validators and the structured authority review found no contradiction, and the project owner approved the exact candidate and retained limitations. |

## 4. Named responsibility and acknowledgement matrix

Historical attribution is preserved. The exact section 9 approval appoints the named M0 owners below for this gate and does not create independent assurance or weaken any limitation.

| M0 input | Governed artifacts | Existing approved owner/evidence | Proposed accountable M0 owner | M0 acknowledgement |
|---|---|---|---|---|
| Product contract and non-goals | [`docs/product-contract.md`](../../docs/product-contract.md), [`docs/non-goals.md`](../../docs/non-goals.md), [`PROJECT_BOUNDARY.md`](../../PROJECT_BOUNDARY.md) | Project governance completed by `TL-0003`; no separate named artifact owner recorded | Janne Vuorela — Product and Project Boundary Owner; Principal Software Architect & Sole Project Owner | Acknowledged — Janne Vuorela; 2026-08-27; candidate 17975419badd4154b82895d9d92a4a904790c7c0; gate SHA-256 b4dfbc2fd66bd869ee10a4332ab8089c9f5c3586b378d3a99a095763e18df153 |
| Threat and security model | [`docs/security/threat-model.md`](../../docs/security/threat-model.md), data-flow and abuse-case records | PikkuJanne — Security owner; approved reviewed commit `917b5ebd5f5e4cf273a087a05dd381da54324235` | Janne Vuorela — M0 Security Owner for the fresh gate acknowledgement; historical attribution remains unchanged | Acknowledged — Janne Vuorela; 2026-08-27; candidate 17975419badd4154b82895d9d92a4a904790c7c0; gate SHA-256 b4dfbc2fd66bd869ee10a4332ab8089c9f5c3586b378d3a99a095763e18df153 |
| Privacy and logging model | [`docs/privacy/privacy-model.md`](../../docs/privacy/privacy-model.md), logging standard and redaction fixtures | Janne Vuorela — Privacy Owner; approved reviewed commit `118240955b01ea4a0b941b00d357ea165b035981` | Janne Vuorela — Privacy Owner | Acknowledged — Janne Vuorela; 2026-08-27; candidate 17975419badd4154b82895d9d92a4a904790c7c0; gate SHA-256 b4dfbc2fd66bd869ee10a4332ab8089c9f5c3586b378d3a99a095763e18df153 |
| Accessibility baseline | [`ACCESSIBILITY.md`](../../ACCESSIBILITY.md), [`docs/testing/accessibility-matrix.md`](../../docs/testing/accessibility-matrix.md) | Governed specification; no named human owner recorded | Janne Vuorela — Accessibility Owner | Acknowledged — Janne Vuorela; 2026-08-27; candidate 17975419badd4154b82895d9d92a4a904790c7c0; gate SHA-256 b4dfbc2fd66bd869ee10a4332ab8089c9f5c3586b378d3a99a095763e18df153 |
| Modest-hardware design and limitations | [`LOW_SPEC.md`](../../LOW_SPEC.md), capability/risk and same-machine constraint records | Governed specification; no named human owner recorded | Janne Vuorela — Modest-Hardware Engineering Owner | Acknowledged — Janne Vuorela; 2026-08-27; candidate 17975419badd4154b82895d9d92a4a904790c7c0; gate SHA-256 b4dfbc2fd66bd869ee10a4332ab8089c9f5c3586b378d3a99a095763e18df153 |
| Dependencies, licence, and redistribution rights | [`docs/supply-chain/dependencies.md`](../../docs/supply-chain/dependencies.md), licence matrix | Janne Vuorela — Principal Software Architect & Sole Project Owner; renewed approval at commit `7afc6c7599523fb56a66774a29e9107e6a9a0aac`, matrix SHA-256 `32ff63e4e6deb703f978efad368ba54cdc898004106fa443e211d046126ee193` | Janne Vuorela — Dependency and Licence Owner | Acknowledged — Janne Vuorela; 2026-08-27; candidate 17975419badd4154b82895d9d92a4a904790c7c0; gate SHA-256 b4dfbc2fd66bd869ee10a4332ab8089c9f5c3586b378d3a99a095763e18df153 |
| Reference-machine profile, test tiers, constraints, and manual tests | [`docs/testing/reference-machine-profile.md`](../../docs/testing/reference-machine-profile.md), [`TESTING.md`](../../TESTING.md), same-machine and manual-test specifications | Revised same-machine contract completed by `TL-0008`; no named human owner recorded | Janne Vuorela — Validation-System and Reference-Profile Owner | Acknowledged — Janne Vuorela; 2026-08-27; candidate 17975419badd4154b82895d9d92a4a904790c7c0; gate SHA-256 b4dfbc2fd66bd869ee10a4332ab8089c9f5c3586b378d3a99a095763e18df153 |
| Initial ADR inputs | [`docs/adr/0001-windows-wpf-stack.md`](../../docs/adr/0001-windows-wpf-stack.md) through ADR 0008; approved ADR 0009 reservation | Architecture records completed by `TL-0009`; amendment approved by Janne Vuorela | Janne Vuorela — Architecture Decision Owner; Principal Software Architect | Acknowledged — Janne Vuorela; 2026-08-27; candidate 17975419badd4154b82895d9d92a4a904790c7c0; gate SHA-256 b4dfbc2fd66bd869ee10a4332ab8089c9f5c3586b378d3a99a095763e18df153 |
| M0 gate decision | This record and its cited evidence | Hybrid gate; no prior M0 signature | Janne Vuorela — Project Owner; Principal Software Architect & Sole Project Owner | Signed — Janne Vuorela; 2026-08-27; candidate 17975419badd4154b82895d9d92a4a904790c7c0; gate SHA-256 b4dfbc2fd66bd869ee10a4332ab8089c9f5c3586b378d3a99a095763e18df153 |

The approved concentration of roles reflects the current sole-owner project structure. It is explicit, reviewable, and does not claim independent assurance or separation of duties.

## 5. Binding limitations preserved at M0

1. The security-owner approval covers the initial threat model and residual-risk treatments only. Planned controls are not implemented merely because M0 approves their design, and the approval is not release authorization.
2. The privacy-owner approval covers the exact classification, retention-guidance, logging/export, and redaction design contract. Runtime privacy controls remain owned and verified by later tasks.
3. The recorded `xunit.abstractions` licence evidence remains mutable upstream evidence with the documented confidence limitation.
4. Redistribution of the .NET SDK and CPython toolchains remains withheld until exact installer provenance, hashes/signatures, applicable licences, and notices are reviewed.
5. The four synthetic catalogue placeholders remain `NOASSERTION`, non-installable, no-artifact, `not-shipped`, and separately withheld for installation and redistribution. They are fixtures, not approved packages.
6. No predecessor or M0 acknowledgement grants blanket installation, redistribution, legal, production, release, or final-product-licence rights.
7. `REF-CODEX-001` is one sanitized active-machine snapshot. Same-machine fixtures, constraints, clones, worktrees, and hosted environments prove only the recorded run; they do not certify another machine, hardware class, manufacturer, minimum specification, or long-term reliability.
8. `MHT-001`–`MHT-021`, `A11Y-001`–`A11Y-010`, `FI-001`–`FI-012`, and all `SMC-*` profiles remain `Not run` unless section 7 explicitly says otherwise. Their definitions are not physical, accessibility, failure-injection, resource, or human-completion evidence.
9. TL-0010 has no Extended-tier trigger. A physical cold boot, device-pool walkthrough, lower-performance machine, external runtime matrix, or equipment acquisition is not required by this gate.
10. No sibling repository, active branch, private database, service, schema, fixture, adapter, or B4 implementation is an M0 input.

## 6. Clean-checkout method and safety

The current gate uses a disposable clean clone of the exact published candidate on the active Codex machine. The run must:

1. resolve a GUID-named temporary directory under the operating-system temporary directory and verify that resolved path before use or cleanup;
2. clone only this ThirdLife repository and check out the exact published candidate commit;
3. verify `git status --short` is empty before tool bootstrap;
4. create the ignored `.venv` and install only the hash-pinned `tools/requirements.txt` dependency;
5. run the governed Quick tier;
6. run the governed Full tier, which includes locked restore, formatting, a warnings-as-errors Release build, and Release tests;
7. record the exact commit, reference-profile revision, start/end/duration, result, and any focused diagnosis without a machine-specific path; and
8. remove only the verified disposable clone after evidence is captured.

On the active host, Smart App Control, Code Integrity, warnings, analyzers, signatures, hashes, provenance, privacy, accessibility, and recovery controls must remain enabled. A hosted guest may truthfully report that it does not inherit a host control, but its baseline controls must be observed and must not be changed. `Unblock-File`, alternate data-stream removal, Smart App Control disablement, Code Integrity policy edits, test omission, configuration weakening, or a false substitute for the Full tier is prohibited.

### Approved one-command Windows Sandbox hosted rerun

On 2026-08-27, Janne Vuorela, acting as Principal Software Architect & Sole Project Owner, selected the installed Windows Sandbox capability on the active Codex machine as the same-machine hosted environment for this exact rerun. This approves the environment and bounded procedure only. It is not an M0 gate approval, human evidence acknowledgement, release authorization, blanket redistribution right, or claim about another machine.

**Hosted constraint profile:** `TL0010-WSB-2026-08-27.1` — 8192 MiB guest memory, vGPU/clipboard/audio/video/printer redirection disabled, Protected Client and required restore networking enabled; source branch `codex/tl-0010-m0-foundation-gate`.

The complete human procedure is one command from a clean tracked checkout of this branch:

`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\run-tl0010-sandbox.ps1`

No human cloning, guest command entry, tool installation, hash calculation, raw-log curation, or temporary-directory cleanup is required. The governed launcher:

1. verifies Windows Sandbox and the exact host Git, .NET SDK 10.0.400, and CPython 3.14.7 inputs;
2. creates a standalone no-hardlink clone of candidate `17975419badd4154b82895d9d92a4a904790c7c0`, verifies that it is clean, and verifies gate-record SHA-256 `b4dfbc2fd66bd869ee10a4332ab8089c9f5c3586b378d3a99a095763e18df153`;
3. maps that standalone project-local clone checked out at the exact candidate (including its reachable ThirdLife Git history), the minimal runner/request, and installed tool roots read-only; the dedicated ignored result directory is the sole writable host mapping;
4. disables vGPU, clipboard, audio, video, and printer redirection, enables Protected Client, and enables networking only for the hash-pinned Python bootstrap and locked NuGet restore/audit;
5. copies the candidate into guest-local writable storage, records the guest Windows/toolchain and Smart App Control/Code Integrity observations, runs Quick, and runs the exact governed Full command only after Quick passes;
6. keeps raw command and Code Integrity output inside the disposable guest, retaining only a schema-checked result of at most 16 KiB with exact hashes, versions, times, exits, success markers, last completed Full stage, clean/candidate/gate checks, the named policy-observation method, a privacy-safe normalized policy fingerprint, stable failure classification, and the one-machine limitation; and
7. closes the disposable guest, verifies temporary staging cleanup, and prints `passed`, `failed`, or `blocked` plus the bounded local evidence path.

Quick-before-Full deliberately preserves the existing gate contract even though Full repeats the Quick phases. Efficiency comes from eliminating human setup, repeated manual diagnosis, and unbounded evidence handling; it does not skip governed work. If Full reproduces `0x800711C7`, the runner records the error and any recognized affected basenames once and stops—four additional one-project reruns are unnecessary unless the Full output cannot classify the failure.

The host's enforced security remains enabled and is not changed. Windows Sandbox is a disposable hosted Windows environment and may not inherit the host's Smart App Control policy. The runner observes the guest state before and after without changing it. It prefers read-only `CiTool` enumeration; when that unelevated query is unavailable or invalid, it hashes the documented SAC registry state plus readable system-volume Code Integrity policy files. Registry/provider access errors make the observation unavailable rather than `not_detected`. This fallback does not claim to enumerate EFI-resident policies, and the exact observation method is retained. The reviewed harness declares `security_mutation_attempted=false`, while repository controls reject its known security/trust-mutation operations; that field is not independent operating-system instrumentation. A passing exact Full run in this approved hosted environment is valid automated unblock evidence only when the observed guest state is unchanged; the evidence must name the observed guest state and must not claim that unsigned assemblies are compatible with the enforced host policy. A changed or unobservable transition is not silently converted into a pass.

Read-only use of the installed host .NET SDK and CPython roots in this same-machine Sandbox is test execution, not shipment. The existing toolchain redistribution limitations remain binding.

## 7. Verification evidence

| Check | Source revision | Environment | Result | Duration / durable reference |
|---|---|---|---|---|
| Pre-change bundle validator | `1c2aeff4b6517d676a3fc127fe1f912fb6b6c516` | Active Codex machine; repository-local Python 3.14.7 / PyYAML 6.0.3 | Pass: 91 tasks, 8 milestones, 66 decisions, valid DAG | 2026-08-22; combined pre-change bundle/repository validation 3.751 s |
| Pre-change repository validator | `1c2aeff4b6517d676a3fc127fe1f912fb6b6c516` | Active Codex machine; 26 projects, 26 locks, 24 governed components | Pass | 2026-08-22; included in the 3.751 s baseline command |
| Focused M0 gate-record regressions | `17975419badd4154b82895d9d92a4a904790c7c0` | Active Codex machine; repository-local Python 3.14.7 / PyYAML 6.0.3 | Pass: 13/13 lifecycle, predecessor, commit/blob binding, owner attribution, digest, limitation, contradiction, and hidden-content tests; exact-clone Quick reran the complete suite | 2026-08-22; 1.243 s focused pre-checkpoint run; exact-clone complete suite included below; `M0FoundationGateContractTests` |
| Working-tree governed Quick | Candidate content later committed as `17975419badd4154b82895d9d92a4a904790c7c0`; source baseline `1c2aeff4b6517d676a3fc127fe1f912fb6b6c516` | Active Codex machine; `REF-CODEX-001` revision `2026-08-21.1` | Pass: 162 Python tests, bundle/schema/manifest, repository boundaries, package locks, and licence controls | 2026-08-22; `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\verify.ps1 -Tier Quick`; 108.033 s tests; 00:01:53.042 wall |
| Exact-commit clean-clone Quick | `17975419badd4154b82895d9d92a4a904790c7c0` | Disposable clean clone on the active Codex machine; `REF-CODEX-001` revision `2026-08-21.1`; pinned environment bootstrap 7.331 s | Pass: 162 Python tests in 110.348 s plus bundle, manifest, repository, lock, supply-chain, licence, and CI controls | 2026-08-22; `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\verify.ps1 -Tier Quick`; 00:01:55.378 wall; tracked-clean before/after bootstrap |
| Historical exact-commit host Full attempt | `17975419badd4154b82895d9d92a4a904790c7c0` | Disposable clean clone directly on the active host; no host-security setting changed | Blocked: exit 1 after 162 Python tests, bundle/repository controls, locked restore, formatting, and a 0-warning/0-error Release build passed; four Release assembly-contract tests were blocked by Application Control error `0x800711C7` | 2026-08-22; `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\verify.ps1 -Tier Full`; 00:03:01.538 wall; bounded diagnosis below |
| Blocked-state governed Quick | Blocked-state evidence working tree derived from `17975419badd4154b82895d9d92a4a904790c7c0` | Active Codex machine; repository-local Python 3.14.7 / PyYAML 6.0.3; `REF-CODEX-001` revision `2026-08-21.1` | Pass: 162 Python tests plus live bundle, manifest, repository, lock, supply-chain, licence, and CI controls | 2026-08-22; `.\eng\verify.ps1 -Tier Quick`; 118.166 s tests |
| Sandbox harness contract and published preflight | Harness `9f86bfa09571a2e027d868b1f9d1cee48fee4fe2` validating candidate `17975419badd4154b82895d9d92a4a904790c7c0` | Active Codex machine; Windows PowerShell 5.1; Windows Sandbox 0.8.107.0; schema 2 | Pass: 15/15 fail-closed harness tests, 177-test governed Quick, stable fallback policy fingerprint, exact local/remote equality, and published-checkpoint `-PreflightOnly` cleanup | 2026-08-27; focused 13.713 s; Quick tests 103.602 s; preflight 4.9 s; independent review found no P0/P1 issue |
| Historical hosted attempt 1 | Harness `be7c64eecea9656e7b32593b77c0715cc5bacd9d` validating candidate `17975419badd4154b82895d9d92a4a904790c7c0` | Windows Sandbox on the active Codex machine | Failed before Quick: the launcher did not yet identify the Store-hosted `WindowsSandboxRemoteSession` and `WindowsSandboxServer` processes, so no result was accepted or retained | 2026-08-27; 23.489 s; focused process-identity defect; corrected at `b03181e8a4756451c955117327b83a1a9f61d6c3` |
| Historical hosted attempt 2 | Harness `b03181e8a4756451c955117327b83a1a9f61d6c3` validating candidate `17975419badd4154b82895d9d92a4a904790c7c0` | Windows Sandbox on the active Codex machine; schema 1 | Failed closed in preflight: the unelevated guest `CiTool` query was unavailable; Quick and Full were not run; Sandbox closure and staging cleanup passed | 2026-08-27; 64.019 s host command; run `24152eb7ea38ba9e3a07226592644a7a`; bounded result SHA-256 `d131b1d2b662dff0a3bae9a23a53c04b6303f31416864319f5a818912d07d3d4` |
| Exact-commit clean-clone Full | Candidate `17975419badd4154b82895d9d92a4a904790c7c0`; harness `9f86bfa09571a2e027d868b1f9d1cee48fee4fe2` | Protected Windows Sandbox on the active physical Codex machine; `REF-CODEX-001` revision `2026-08-21.1`; hosted constraint `TL0010-WSB-2026-08-27.1`; 8192 MiB | Pass: Quick passed, then the exact governed Full command passed through `tests`; candidate/gate/tracked-clean checks passed; guest SAC was `evaluation` before/after with unchanged fallback policy fingerprint; no security mutation was attempted | 2026-08-27; Quick 106.316 s; Full 1070.198 s; run `64dda9491c7fddf8a9e9f18429a6957b`; bounded result SHA-256 `170ff71314ea8c46d161e42bc1f6564b719f8b04e0d6bee371e01c67c8ce4dea`; Sandbox close and staging cleanup passed |
| Post-evidence bundle and repository validation | Approved-state handoff based on harness `9f86bfa09571a2e027d868b1f9d1cee48fee4fe2` and candidate `17975419badd4154b82895d9d92a4a904790c7c0` | Active Codex machine; repository-local Python 3.14.7 / PyYAML 6.0.3 | Pass: 29/29 focused M0 lifecycle/harness tests; 178-test governed Quick; 91 tasks, 8 milestones, 66 decisions, valid DAG; 26 projects, 26 locks, 24 governed components and licence controls valid | 2026-08-27; focused 20.267 s; Quick tests 144.185 s; live bundle and repository validators passed; `TL-0101` reported dependency-ready |

### Historical host Full blocker and focused diagnosis

The exact TL-0010 candidate reproduced the inherited Smart App Control risk. Full passed 162 Python regressions in 110.313 s, both live validators, locked restore, formatting, and a Release build in 17.50 s with 0 warnings and 0 errors. The Release test phase then passed 10 tests across 9 projects and failed the assembly-contract test in each of these 4 projects:

- `ThirdLife.Packages.Tests` — load of `ThirdLife.Packages.dll` blocked;
- `ThirdLife.Persistence.Tests` — load of `ThirdLife.Persistence.dll` blocked;
- `ThirdLife.Reports.Tests` — load of `ThirdLife.Reports.dll` blocked; and
- `ThirdLife.Verification.Tests` — load of `ThirdLife.Verification.dll` blocked.

Each focused one-project rerun exited 1 with `System.IO.FileLoadException`, “An Application Control policy has blocked this file,” and error `0x800711C7`. The four application DLLs report Authenticode status `NotSigned` and only the ordinary `:$DATA` stream; no `Zone.Identifier` stream was present. Bounded Code Integrity observations recorded event IDs 3033 and 3077 under the enforced Smart App Control policy for the four basenames. No raw event export or machine-specific path is retained.

Smart App Control, Code Integrity, warnings, analyzers, hashes, and provenance were not disabled or bypassed. A clone or worktree change is not a remedy because this exact clean clone has no alternate-stream block to remove. The approved Windows Sandbox path subsequently ran the exact candidate through Quick and Full without changing host or guest security, satisfying the automated unblock condition. This does not erase the direct-host incompatibility: the unsigned assemblies remain unverified under the enforced host policy, and no signing, redistribution, or broad host-compatibility right is inferred.

The first working-tree wrapper attempt omitted the process-scoped PowerShell execution-policy argument and was rejected by Windows before `eng/verify.ps1` loaded (exit 1; 0.421 s). The successful rerun used the repository's previously evidenced `-ExecutionPolicy Bypass` process argument. It did not change the persistent execution policy, Smart App Control, or Code Integrity, and no repository verification result was inferred from the rejected invocation.

The disposable exact-candidate clone remained tracked-clean after verification. Cleanup verified the resolved target was under the operating-system temporary directory and still identified the expected commit. The execution layer rejected two recursive `Remove-Item` invocations before they ran; bounded cleanup then removed the verified clone, normalized the three remaining read-only Git pack-file attributes after the first directory-delete attempt, completed deletion, and confirmed no clone directory remained.

## 8. Boundary, risk, and data review

- **Project-vacuum / sibling integration:** No sibling source, data, runtime, adapter, dependency, or B4 work is introduced by this gate record.
- **Data / migration:** No runtime configuration, job data, database, attachment, cache, log, report, retention, deletion, migration, or uninstall behavior changes. The persistent evidence is this repository record plus two ignored bounded local result/summary pairs: one preflight failure and the final pass. Raw command and Code Integrity logs were discarded with each guest.
- **Release interface:** The interface sheet remains a draft placeholder; no implementation fact, compatibility promise, API, product licence, release owner, or stable approval is invented.
- **Security / privacy:** No product executable, privilege, IPC, network, package, signing, logging, retention, or support-export surface changes. The test-only guest used networking for hash-pinned Python bootstrap and locked NuGet restore/audit, retained only schema-checked bounded evidence, and made no security-policy mutation. Evidence excludes secrets, machine-specific paths, raw Code Integrity logs, and personal/device identifiers.
- **Accessibility / low-spec:** No UI or runtime work is added. The disposable guest used 8192 MiB under one recorded hosted constraint. No human accessibility, lower-memory, constrained-resource, physical-hardware, cold-boot, or cross-hardware scenario is claimed.

## 9. Human approval and acknowledgement

### Approval target

| Field | Value |
|---|---|
| Verification candidate commit | 17975419badd4154b82895d9d92a4a904790c7c0 |
| Gate-record candidate SHA-256 | b4dfbc2fd66bd869ee10a4332ab8089c9f5c3586b378d3a99a095763e18df153 |
| Full-tier result | Pass — exact candidate in approved Windows Sandbox; Quick 106.316 s, Full 1070.198 s through `tests`; run `64dda9491c7fddf8a9e9f18429a6957b` |
| Clean-checkout Quick result | Pass — exact candidate; 162 tests plus governed bundle/repository controls; 00:01:55.378 wall |
| Project-owner decision | Signed — Janne Vuorela; 2026-08-27; candidate 17975419badd4154b82895d9d92a4a904790c7c0; gate SHA-256 b4dfbc2fd66bd869ee10a4332ab8089c9f5c3586b378d3a99a095763e18df153 |
| Security-owner M0 acknowledgement | Acknowledged — Janne Vuorela; 2026-08-27; candidate 17975419badd4154b82895d9d92a4a904790c7c0; gate SHA-256 b4dfbc2fd66bd869ee10a4332ab8089c9f5c3586b378d3a99a095763e18df153 |
| Privacy-owner M0 acknowledgement | Acknowledged — Janne Vuorela; 2026-08-27; candidate 17975419badd4154b82895d9d92a4a904790c7c0; gate SHA-256 b4dfbc2fd66bd869ee10a4332ab8089c9f5c3586b378d3a99a095763e18df153 |
| Dependency/licence-owner M0 acknowledgement | Acknowledged — Janne Vuorela; 2026-08-27; candidate 17975419badd4154b82895d9d92a4a904790c7c0; gate SHA-256 b4dfbc2fd66bd869ee10a4332ab8089c9f5c3586b378d3a99a095763e18df153 |

### Exact approval statement for the final candidate

The approving human must explicitly identify the candidate commit and gate-record SHA-256. Approval of this statement means:

> I, Janne Vuorela, acting as Principal Software Architect & Sole Project Owner, approve the exact TL-0010 M0 foundation gate candidate identified above. I accept the proposed named M0 ownership assignments in section 4, including acting as the M0 Security Owner for this fresh gate acknowledgement while preserving PikkuJanne's historical security-model attribution, Privacy Owner, Accessibility Owner, Modest-Hardware Engineering Owner, Dependency and Licence Owner, Validation-System and Reference-Profile Owner, Product and Project Boundary Owner, and Architecture Decision Owner. I acknowledge the security, privacy, dependency, licence, reference-profile, test-tier, same-machine constraint, manual-test, accessibility, and modest-hardware artifacts exactly as recorded. Every limitation in section 5 remains binding, including the `xunit.abstractions` evidence limitation, withheld .NET SDK and CPython redistribution, withheld synthetic-placeholder rights, no blanket redistribution right, no release authorization, no cross-hardware certification, and no claim that unrun manual, accessibility, failure, resource, or extended scenarios passed.

Janne Vuorela provided the statement above verbatim on 2026-08-27. Section 7 records passing required verification, every section 4 acknowledgement is attributable, and every section 5 limitation remains binding.

## 10. Final gate decision

**Decision:** Approved  
**Decision date:** 2026-08-27  
**Approver:** Janne Vuorela — Principal Software Architect & Sole Project Owner; candidate `17975419badd4154b82895d9d92a4a904790c7c0`; gate SHA-256 `b4dfbc2fd66bd869ee10a4332ab8089c9f5c3586b378d3a99a095763e18df153`  
**Unresolved blockers:** None  
**Next action:** M0 is complete. `TL-0101` is the next dependency-ready task; report it without starting it in this gate-completion session.
