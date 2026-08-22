# ThirdLife Setup Core — M0 foundation gate record

**Record status:** Blocked — required Full tier did not pass  
**Task:** `TL-0010` — Validate the M0 foundation gate  
**Milestone:** `M0` — Foundation and product contract  
**Record revision:** 1  
**Prepared:** 2026-08-22  
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
| `TL-0006` | `done`; 9 evidence entries | Dependency inventory, licence/rights matrix, deterministic SBOM controls, and limitations | Exact historical rights approvals retained; fresh M0 acknowledgement remains separate. |
| `TL-0007` | `done`; 8 evidence entries | Synthetic pilot fixtures, candidate policy values, profiles, and four generic capability records | Exact pilot-owner and renewed matrix approvals retained; no fixture is production evidence. |
| `TL-0008` | `done`; 4 evidence entries | Same-machine test system, reference profile, capability/risk coverage, manual-test, failure, accessibility, and constraint specifications | The revised contract is authoritative. The older draft-1 physical walkthrough language is superseded and is not M0 evidence. No device pool or missing-equipment approval is required. |
| `TL-0009` | `done`; 3 evidence entries | Eight accepted initial ADRs and the approved future ADR 0009 reservation | Accepted as architecture input, subject to the M0 responsibility acknowledgement below. |

The authoritative status and complete append-only evidence for every row remain in [`TASKS.yaml`](../../TASKS.yaml). This table is an index, not a replacement for that evidence.

The structured predecessor audit enumerated all 54 deliverables, 56 acceptance criteria, 22 verification items, and 4 predecessor human-evidence requirements across `TL-0001` through `TL-0009`. No item is waived by this summary. Current M0 verification and gate-specific acknowledgements are repeated below because historical predecessor evidence cannot satisfy a fresh gate requirement by implication.

## 3. Acceptance checklist

### M0 milestone exit criteria

| # | Milestone exit criterion | Candidate result |
|---:|---|---|
| 1 | The repository builds from a clean checkout on supported Windows on the active Codex machine. | Pass: exact-candidate locked restore, formatting, and the warnings-as-errors Release build completed with 0 warnings and 0 errors. The later Release-test phase is separately blocked and Full is not green. |
| 2 | Product, non-goals, threat, privacy, accessibility, modest-hardware, dependency/licence, reference-profile, test-tier, and ADR inputs have named owners. | Pending human acceptance of the complete responsibility matrix in section 4. |
| 3 | No lab, physical-device pool, lower-performance test machine, external runtime matrix, or missing equipment is a dependency or release blocker. | Pass: the same-machine contract and explicit limitations require none of these inputs. |
| 4 | The Team B/B1 project-vacuum boundary and future B4 late-binding posture are explicit and validated. | Pass: authority review and live validators confirm the boundary. |
| 5 | The roadmap bundle, GitHub workflow, task graph, and repository rules are internally consistent and validated. | Pass at the exact published candidate; clean-clone Quick and pre-test Full phases passed. Final owner review remains pending because Full is blocked. |

### TL-0010 acceptance criteria

| # | `TL-0010` acceptance criterion | Candidate result | Evidence or unblock condition |
|---:|---|---|---|
| 1 | All predecessor tasks are done with evidence. | Pass | Section 2 and live task-graph validation; all nine transitive M0 predecessors are `done` with non-empty evidence. |
| 2 | The documented clean-checkout Quick command passes on the active Codex machine. | Pass | Exact published candidate `17975419badd4154b82895d9d92a4a904790c7c0` passed `.\eng\verify.ps1 -Tier Quick` from a disposable clean clone on the active machine. |
| 3 | Threat, privacy, accessibility, modest-hardware, dependency, and licence reviews have named owners. | Pending human acknowledgement | Historical owners and proposed M0 responsibility assignments are explicit in section 4; proposed assignments take effect only through the exact sign-off in section 9. |
| 4 | The reference profile, test tiers, same-machine constraints, and manual-test specification are approved without requiring a device pool. | Pending human acknowledgement | The governed specifications are complete and validated; the project owner must approve the exact M0 input set in section 9. No physical device pool, second PC, lower-performance machine, or missing-equipment approval is required. |
| 5 | `PROJECT_BOUNDARY.md` states Team B/B1 and prohibits live sibling dependencies and B4 work. | Pass | [`PROJECT_BOUNDARY.md`](../../PROJECT_BOUNDARY.md) states the Team B/B1 project vacuum, active-machine-only validation, and future B4 late binding against frozen releases. |
| 6 | `RELEASE_INTERFACE.md` remains an honest draft rather than a speculative API. | Pass | [`RELEASE_INTERFACE.md`](../../RELEASE_INTERFACE.md) is a draft placeholder, leaves unknown fields `TBD`, and remains **not a shared application API** or early B4 adapter contract. |
| 7 | `STATUS.md` identifies branch, commit, tests, and next action without chat history. | Pass | [`STATUS.md`](../../STATUS.md) records the exact branch/candidate, clean-clone results, Full blocker, cleanup, and safe unblock condition. |
| 8 | No unresolved contradiction exists among binding bundle files. | Pass for the candidate review; owner confirmation pending | Bundle/repository validators and the structured authority review found no contradiction. The project-owner checklist review remains required before approval. |

## 4. Named responsibility and acknowledgement matrix

Historical attribution is preserved. A proposed M0 owner is not treated as appointed until the project owner approves the exact assignment in section 9.

| M0 input | Governed artifacts | Existing approved owner/evidence | Proposed accountable M0 owner | M0 acknowledgement |
|---|---|---|---|---|
| Product contract and non-goals | [`docs/product-contract.md`](../../docs/product-contract.md), [`docs/non-goals.md`](../../docs/non-goals.md), [`PROJECT_BOUNDARY.md`](../../PROJECT_BOUNDARY.md) | Project governance completed by `TL-0003`; no separate named artifact owner recorded | Janne Vuorela — Product and Project Boundary Owner; Principal Software Architect & Sole Project Owner | Pending |
| Threat and security model | [`docs/security/threat-model.md`](../../docs/security/threat-model.md), data-flow and abuse-case records | PikkuJanne — Security owner; approved reviewed commit `917b5ebd5f5e4cf273a087a05dd381da54324235` | Janne Vuorela — M0 Security Owner for the fresh gate acknowledgement; historical attribution remains unchanged | Pending |
| Privacy and logging model | [`docs/privacy/privacy-model.md`](../../docs/privacy/privacy-model.md), logging standard and redaction fixtures | Janne Vuorela — Privacy Owner; approved reviewed commit `118240955b01ea4a0b941b00d357ea165b035981` | Janne Vuorela — Privacy Owner | Pending |
| Accessibility baseline | [`ACCESSIBILITY.md`](../../ACCESSIBILITY.md), [`docs/testing/accessibility-matrix.md`](../../docs/testing/accessibility-matrix.md) | Governed specification; no named human owner recorded | Janne Vuorela — Accessibility Owner | Pending |
| Modest-hardware design and limitations | [`LOW_SPEC.md`](../../LOW_SPEC.md), capability/risk and same-machine constraint records | Governed specification; no named human owner recorded | Janne Vuorela — Modest-Hardware Engineering Owner | Pending |
| Dependencies, licence, and redistribution rights | [`docs/supply-chain/dependencies.md`](../../docs/supply-chain/dependencies.md), licence matrix | Janne Vuorela — Principal Software Architect & Sole Project Owner; renewed approval at commit `7afc6c7599523fb56a66774a29e9107e6a9a0aac`, matrix SHA-256 `32ff63e4e6deb703f978efad368ba54cdc898004106fa443e211d046126ee193` | Janne Vuorela — Dependency and Licence Owner | Pending |
| Reference-machine profile, test tiers, constraints, and manual tests | [`docs/testing/reference-machine-profile.md`](../../docs/testing/reference-machine-profile.md), [`TESTING.md`](../../TESTING.md), same-machine and manual-test specifications | Revised same-machine contract completed by `TL-0008`; no named human owner recorded | Janne Vuorela — Validation-System and Reference-Profile Owner | Pending |
| Initial ADR inputs | [`docs/adr/0001-windows-wpf-stack.md`](../../docs/adr/0001-windows-wpf-stack.md) through ADR 0008; approved ADR 0009 reservation | Architecture records completed by `TL-0009`; amendment approved by Janne Vuorela | Janne Vuorela — Architecture Decision Owner; Principal Software Architect | Pending |
| M0 gate decision | This record and its cited evidence | Hybrid gate; no prior M0 signature | Janne Vuorela — Project Owner; Principal Software Architect & Sole Project Owner | Pending |

The proposed concentration of roles reflects the current sole-owner project structure. It is explicit, reviewable, and does not claim independent assurance or separation of duties.

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

Smart App Control, Code Integrity, warnings, analyzers, signatures, hashes, provenance, privacy, accessibility, and recovery controls must remain enabled. `Unblock-File`, alternate data-stream removal, Smart App Control disablement, Code Integrity policy edits, test omission, configuration weakening, or a false substitute for the Full tier is prohibited.

## 7. Verification evidence

| Check | Source revision | Environment | Result | Duration / durable reference |
|---|---|---|---|---|
| Pre-change bundle validator | `1c2aeff4b6517d676a3fc127fe1f912fb6b6c516` | Active Codex machine; repository-local Python 3.14.7 / PyYAML 6.0.3 | Pass: 91 tasks, 8 milestones, 66 decisions, valid DAG | 2026-08-22; combined pre-change bundle/repository validation 3.751 s |
| Pre-change repository validator | `1c2aeff4b6517d676a3fc127fe1f912fb6b6c516` | Active Codex machine; 26 projects, 26 locks, 24 governed components | Pass | 2026-08-22; included in the 3.751 s baseline command |
| Focused M0 gate-record regressions | `17975419badd4154b82895d9d92a4a904790c7c0` | Active Codex machine; repository-local Python 3.14.7 / PyYAML 6.0.3 | Pass: 13/13 lifecycle, predecessor, commit/blob binding, owner attribution, digest, limitation, contradiction, and hidden-content tests; exact-clone Quick reran the complete suite | 2026-08-22; 1.243 s focused pre-checkpoint run; exact-clone complete suite included below; `M0FoundationGateContractTests` |
| Working-tree governed Quick | Manifest-bound candidate working tree based on `1c2aeff4b6517d676a3fc127fe1f912fb6b6c516` | Active Codex machine; `REF-CODEX-001` revision `2026-08-21.1` | Pass: 162 Python tests, bundle/schema/manifest, repository boundaries, package locks, and licence controls | 2026-08-22; `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\verify.ps1 -Tier Quick`; 108.033 s tests; 00:01:53.042 wall |
| Exact-commit clean-clone Quick | `17975419badd4154b82895d9d92a4a904790c7c0` | Disposable clean clone on the active Codex machine; `REF-CODEX-001` revision `2026-08-21.1`; pinned environment bootstrap 7.331 s | Pass: 162 Python tests in 110.348 s plus bundle, manifest, repository, lock, supply-chain, licence, and CI controls | 2026-08-22; `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\verify.ps1 -Tier Quick`; 00:01:55.378 wall; tracked-clean before/after bootstrap |
| Exact-commit clean-clone Full | `17975419badd4154b82895d9d92a4a904790c7c0` | Same disposable clean clone on the active Codex machine; no host-security setting changed | Blocked: exit 1 after 162 Python tests, bundle/repository controls, locked restore, formatting, and a 0-warning/0-error Release build passed; four Release assembly-contract tests were blocked by Application Control error `0x800711C7` | 2026-08-22; `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\verify.ps1 -Tier Full`; 00:03:01.538 wall; bounded diagnosis below |
| Blocked-state governed Quick | Blocked-state evidence working tree derived from `17975419badd4154b82895d9d92a4a904790c7c0` | Active Codex machine; repository-local Python 3.14.7 / PyYAML 6.0.3; `REF-CODEX-001` revision `2026-08-21.1` | Pass: 162 Python tests plus live bundle, manifest, repository, lock, supply-chain, licence, and CI controls | 2026-08-22; `.\eng\verify.ps1 -Tier Quick`; 118.166 s tests |
| Post-evidence bundle and repository validation | Blocked-state evidence working tree derived from `17975419badd4154b82895d9d92a4a904790c7c0` | Active Codex machine; repository-local Python 3.14.7 / PyYAML 6.0.3 | Pass: 13/13 focused M0 tests; 91 tasks, 8 milestones, 66 decisions, valid DAG; 26 projects, 26 locks, 24 governed components and licence controls valid | 2026-08-22; focused 2.173 s wall / 1.419 s tests; bundle 2.644 s; repository 1.636 s |

### Full-tier blocker and focused diagnosis

The exact TL-0010 candidate reproduced the inherited Smart App Control risk. Full passed 162 Python regressions in 110.313 s, both live validators, locked restore, formatting, and a Release build in 17.50 s with 0 warnings and 0 errors. The Release test phase then passed 10 tests across 9 projects and failed the assembly-contract test in each of these 4 projects:

- `ThirdLife.Packages.Tests` — load of `ThirdLife.Packages.dll` blocked;
- `ThirdLife.Persistence.Tests` — load of `ThirdLife.Persistence.dll` blocked;
- `ThirdLife.Reports.Tests` — load of `ThirdLife.Reports.dll` blocked; and
- `ThirdLife.Verification.Tests` — load of `ThirdLife.Verification.dll` blocked.

Each focused one-project rerun exited 1 with `System.IO.FileLoadException`, “An Application Control policy has blocked this file,” and error `0x800711C7`. The four application DLLs report Authenticode status `NotSigned` and only the ordinary `:$DATA` stream; no `Zone.Identifier` stream was present. Bounded Code Integrity observations recorded event IDs 3033 and 3077 under the enforced Smart App Control policy for the four basenames. No raw event export or machine-specific path is retained.

Smart App Control, Code Integrity, warnings, analyzers, hashes, and provenance were not disabled or bypassed. A clone or worktree change is not a remedy because this exact clean clone has no alternate-stream block to remove. The gate remains `blocked` until an approved same-machine hosted environment can run the unsigned assemblies under the governed Full command, or later governed signing/lifecycle work provides an approved path. Full must then pass at the exact candidate/replacement checkpoint before human gate approval can be requested.

The first working-tree wrapper attempt omitted the process-scoped PowerShell execution-policy argument and was rejected by Windows before `eng/verify.ps1` loaded (exit 1; 0.421 s). The successful rerun used the repository's previously evidenced `-ExecutionPolicy Bypass` process argument. It did not change the persistent execution policy, Smart App Control, or Code Integrity, and no repository verification result was inferred from the rejected invocation.

The disposable exact-candidate clone remained tracked-clean after verification. Cleanup verified the resolved target was under the operating-system temporary directory and still identified the expected commit. The execution layer rejected two recursive `Remove-Item` invocations before they ran; bounded cleanup then removed the verified clone, normalized the three remaining read-only Git pack-file attributes after the first directory-delete attempt, completed deletion, and confirmed no clone directory remained.

## 8. Boundary, risk, and data review

- **Project-vacuum / sibling integration:** No sibling source, data, runtime, adapter, dependency, or B4 work is introduced by this gate record.
- **Data / migration:** No runtime configuration, job data, database, attachment, cache, log, report, retention, deletion, migration, or uninstall behavior changes. The only new persistent artifact is this bounded repository evidence record.
- **Release interface:** The interface sheet remains a draft placeholder; no implementation fact, compatibility promise, API, product licence, release owner, or stable approval is invented.
- **Security / privacy:** No executable, privilege, IPC, network, package, signing, logging, retention, or support-export surface changes. Evidence excludes secrets, machine-specific paths, raw Code Integrity logs, and personal/device identifiers.
- **Accessibility / low-spec:** No UI or runtime work is added. No human accessibility or constrained-resource scenario is claimed. Verification adds temporary local build/test load only and requires bounded cleanup.

## 9. Human approval and acknowledgement

### Approval target

| Field | Value |
|---|---|
| Verification candidate commit | 17975419badd4154b82895d9d92a4a904790c7c0 |
| Gate-record candidate SHA-256 | b4dfbc2fd66bd869ee10a4332ab8089c9f5c3586b378d3a99a095763e18df153 |
| Full-tier result | Blocked — Application Control rejected four unsigned Release application DLL loads; exit 1; `0x800711C7` |
| Clean-checkout Quick result | Pass — exact candidate; 162 tests plus governed bundle/repository controls; 00:01:55.378 wall |
| Project-owner decision | Pending |
| Security-owner M0 acknowledgement | Pending |
| Privacy-owner M0 acknowledgement | Pending |
| Dependency/licence-owner M0 acknowledgement | Pending |

### Exact approval statement for the final candidate

The approving human must explicitly identify the candidate commit and gate-record SHA-256. Approval of this statement means:

> I, Janne Vuorela, acting as Principal Software Architect & Sole Project Owner, approve the exact TL-0010 M0 foundation gate candidate identified above. I accept the proposed named M0 ownership assignments in section 4, including acting as the M0 Security Owner for this fresh gate acknowledgement while preserving PikkuJanne's historical security-model attribution, Privacy Owner, Accessibility Owner, Modest-Hardware Engineering Owner, Dependency and Licence Owner, Validation-System and Reference-Profile Owner, Product and Project Boundary Owner, and Architecture Decision Owner. I acknowledge the security, privacy, dependency, licence, reference-profile, test-tier, same-machine constraint, manual-test, accessibility, and modest-hardware artifacts exactly as recorded. Every limitation in section 5 remains binding, including the `xunit.abstractions` evidence limitation, withheld .NET SDK and CPython redistribution, withheld synthetic-placeholder rights, no blanket redistribution right, no release authorization, no cross-hardware certification, and no claim that unrun manual, accessibility, failure, resource, or extended scenarios passed.

The approval is valid only after section 7 records passing required verification. If Full is blocked or failed, a human acknowledgement may be recorded as an artifact review, but it cannot approve the M0 gate or make `TL-0010` `done`.

## 10. Final gate decision

**Decision:** Blocked  
**Decision date:** 2026-08-22  
**Approver:** Not applicable — required Full verification did not pass, so human gate approval was not requested  
**Unresolved blockers:** The enforced Windows Smart App Control / Code Integrity policy blocks unsigned `ThirdLife.Packages.dll`, `ThirdLife.Persistence.dll`, `ThirdLife.Reports.dll`, and `ThirdLife.Verification.dll` during the required Release test phase; gate-specific human acknowledgements also remain pending until Full passes.  
**Next action:** Provide an approved same-machine hosted environment that can execute the governed Full command without weakening host security, or complete later governed signing/lifecycle work; rerun exact-candidate Quick and Full, then request the exact human acknowledgements only after Full passes.
