# ThirdLife Setup Core — Failure-injection scenario register

**Status:** Active specification; provider-contract subset first executed at `TL-0105`  
**Procedure revision:** TL-0008 same-machine revision 2  
**Task:** `TL-0008`  
**Execution state:** The deterministic common provider-unavailable subset of `FI-001` passed at `TL-0105`, and the first concrete Windows system-inventory unavailable/access-denied/malformed subset passed at `TL-0106`; network/transfer variants and `FI-002`–`FI-012` remain `Not run` except for separately recorded owning-task subsets.

## 1. Purpose and claim boundary

This register consolidates the detailed provider, network, resource, process, broker, package, persistence, interruption, update, manual-workflow, and cold-boot failure cases into twelve canonical scenarios. Each scenario retains fail-closed state, integrity, accessibility, recovery, cleanup, and evidence requirements. Revised `TL-0008` defined the cases, tier triggers, and initial command placeholders. Owning tasks replace only the applicable placeholder with an independently invokable bounded case and record its narrow result; that does not imply the rest of the scenario or broad matrix ran.

A scenario passes only when the deliberately introduced fault is observed and every expected safe, truthful, recoverable invariant holds. A rejected or failed product operation can therefore be a **Pass** for the injection scenario; it never means the underlying operation succeeded.

All development execution is hosted by the active Codex machine. Fixtures, adapters, hosted environments, and constraints prove only the represented fault and recorded environment. They do not certify physical hardware, another machine, a minimum specification, or long-term reliability.

## 2. Result and evidence semantics

`test_result` and `evidence_class` are independent.

| Field | Exact value | Meaning |
|---|---|---|
| `test_result` | `Pass` | The complete scenario ran, the intended fault occurred, all invariants held, and required cleanup/recovery succeeded. |
| `test_result` | `Fail` | An invariant, integrity, security/privacy, accessibility, recovery, or cleanup condition failed. False success or unexplained state always fails. |
| `test_result` | `Not available` | A required safe injection mechanism, implemented capability, environment, or evidence source was unavailable. It is not a pass. |
| `test_result` | `Not run` | The scenario was not started/completed; an attributable reason is required. It is not a pass. |
| `evidence_class` | `Observed` | A bounded provider, harness, journal, store, operating-system, or artifact observation was captured with provenance. |
| `evidence_class` | `Inferred` | A conclusion was derived from named observations; inference alone cannot establish a required pass. |
| `evidence_class` | `Not available` | Required evidence was unavailable; missing evidence remains unknown. |
| `evidence_class` | `Human confirmed` | A human actually performed/observed a later interaction/physical procedure and recorded role, timestamp, and provenance. |

Synthetic fixtures/harnesses are evidence sources, not real-device evidence classes. A passing deterministic test never becomes a real job result or `Human confirmed` evidence.

Allowed `Not available` reasons include `capability_absent`, `environment_unavailable`, `provider_unavailable`, `permission_denied`, and `unsafe_to_run`. `Not run` reasons include `future_control_not_implemented`, `blocked_by_prerequisite`, `cancelled_before_injection`, and `later_trigger_not_reached`.

## 3. Active-machine environments and prohibitions

All direct and hosted environments use the active Codex machine:

| Environment | Safe use | Limitation |
|---|---|---|
| Deterministic fixture/adapter | Typed provider, storage, clock, journal, package, update, report, and manual-workflow outcomes. | Proves only represented input/state. |
| Hosted disposable environment | VM, Windows Sandbox, container, isolated workspace, copied synthetic store, or virtual disk for termination/interruption/recovery. | Does not prove physical power, firmware, battery, ports, or another machine. |
| Safe same-machine constraint | Injected capacity, throttled adapter, bounded low priority/concurrency, no-GPU, slow destination, or deterministic network interruption. | Does not simulate a particular untested device. |
| Physical active-machine observation | Later explicit UAC, operator interruption, or full off/on procedure when named by an owning task/gate. | Requires checkpoint, human attribution where applicable, abort rule, and restoration; proves that run only. |

Use only synthetic jobs and non-sensitive fixtures. Evidence excludes names, contacts, serials/fragments, asset tags, hardware UUIDs, device/host names, usernames/SIDs, MAC/IP/SSID values, credentials, account/tenant data, product/recovery keys, personal paths, screenshots, raw logs, dumps, archives, and donor/recipient content.

Never fill the host system volume; destructively stress storage/battery; cut physical power during unsafe mutation; alter firmware, Secure Boot, TPM, activation, ownership/management, or Windows eligibility; disable provenance, hash/signature, approval, verification, security/privacy, accessibility, or recovery; add an arbitrary execution surface; or use a sibling/private interface.

## 4. Prerequisites for any later run

Before injection, record:

1. scenario ID, owning task, selected tier, exact independently invokable command/procedure, build/source revision, and reference-profile revision;
2. fixture/workload ID, version, bounded size, SHA-256, expected result, provenance, and privacy review;
3. direct/hosted environment, `SMC-*` profile/settings, Windows/tool versions, and opaque project-created run ID;
4. initial job/action state, durable checkpoint, exact injection point, expected terminal/review state, and actual-state re-observation;
5. storage/time/retry/output/process bounds, accessibility expectations, abort rule, recovery method, cleanup, and residue inspection.

If a prerequisite is absent, leave the scenario `Not run` or `Not available`; do not improvise a broader/destructive mechanism.

## 5. Canonical scenario and invocation register

At revised `TL-0008`, every command cell was deliberately non-executable. An owning task replaces only its applicable placeholder with a checked-in single-scenario command or bounded human procedure; unreached cells remain placeholders. A runner that can invoke only the whole matrix does not satisfy D-062.

| ID | Consolidated fault/checkpoint | Expected invariant and recovery | Must not happen | Default tier and explicit earliest trigger | Individually invokable command/procedure | Current state |
|---|---|---|---|---|---|---|
| `FI-001` | Network/provider unavailable before operation, including timeout, exception, malformed/access-denied evidence. | Offline-capable work remains usable; no mutation; required evidence stays unavailable; unrelated observations remain; bounded retry/next action is shown. | Missing/malformed data becomes pass, raw provider output persists, or core local data becomes unavailable. | Targeted at provider work beginning `TL-0105`, network action `TL-0405`, or update scan `TL-0504`. | Common provider-contract subset: `dotnet test .\tests\ThirdLife.Inventory.Tests\ThirdLife.Inventory.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName=ThirdLife.Inventory.Tests.ProviderFailureInjectionTests.FI001SmcProviderUnavailableRemainsFailClosed"`; TL-0106 system-inventory subset uses the same project with filter `FullyQualifiedName=ThirdLife.Inventory.Tests.SystemInventoryProviderTests.FI001SmcSystemInventoryFailuresRemainFailClosed`; network/transfer owners add their own bounded case. | Common subset passed 1/1 at `e5e9c7ef26ddfb2674ce08d7ea35b0dd193b9a2c`; TL-0106 concrete deterministic subset passed 1/1 with exact commit/timing pinned in task evidence; real permission denial and network/transfer variants `Not run` |
| `FI-002` | Network interruption, slowness, filtering, or timeout during metadata/package transfer. | Partial untrusted bytes are bounded/removed or quarantined; progress/cancel/uncertainty remain accessible; checkpoint is retry-safe; retry revalidates identity/trust. | Partial execution, infinite retry/wait, stale approval, hidden source fallback, UI deadlock, or unbounded cache. | Extended at transfer risk `TL-0405`/`TL-0408` or failure gate `TL-0510`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-scenario command or bounded procedure before invocation` | Defined; `Not run` |
| `FI-003` | Insufficient capacity/resource before mutation or bounded destination/resource exhaustion after preflight. | Preflight preserves rollback headroom; mid-operation failure leaves truthful review/failed state; output/store remains atomic or recoverable; cleanup is bounded. | Host system volume is filled, source data corrupts, work appears complete/verified, or security/accessibility is disabled for resources. | Targeted at storage preflight `TL-0503`; Extended for mid-write/resource risk at `TL-0510`/`TL-0606`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-scenario command or bounded procedure before invocation` | Defined; `Not run` |
| `FI-004` | UI/manual workflow terminates or pauses before dispatch, after durable intent, or between manual-test steps. | Pre-dispatch state is unchanged; post-intent ambiguity is reconciled; completed manual results persist; incomplete work remains `Not run`/`Not available`; reopening offers safe recovery. | Unattributable dispatch, false verified/pass, blind repeat, wrong-job resume, or loss/rewrite of independent results. | Targeted at journal `TL-0308`, manual workflow `TL-0114`, or UI/broker integration `TL-0313`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-scenario command or bounded procedure before invocation` | Defined; `Not run` |
| `FI-005` | Broker/backend terminates before mutation, during mutation, or immediately after an ambiguous attempt. | Correlated attempt remains attributable; no permanent elevated process; actual state is re-observed before retry; ambiguity blocks readiness. | Dispatch/backend exit becomes applied/verified, duplicate mutation, automatic unsafe retry, history rewrite, or privileged residue. | Targeted before mutation at `TL-0311`/`TL-0312`; Extended for ambiguous mutation at `TL-0408`/`TL-0409`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-scenario command or bounded procedure before invocation` | Defined; `Not run` |
| `FI-006` | Operator declines UAC before broker authorization. | No privileged mutation or approval; job remains valid/truthful; retry/stop is clear and keyboard reachable; broker exits. | Applied/approved state is minted, UI loses context, inaccessible recovery, or elevated process remains. | Targeted at broker/UI approval `TL-0313`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-scenario command or bounded procedure before invocation` | Defined; `Not run` |
| `FI-007` | Catalogue/package identity, source, publisher, version, content digest, or trust metadata changes after preview/approval. | Approval is invalidated; a new resolution/preview is required; no stale request executes. | Source substitution, downgrade, hash bypass, or unchanged approval after material change. | Targeted at approval/digest `TL-0307` or catalogue resolution `TL-0403`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-scenario command or bounded procedure before invocation` | Defined; `Not run` |
| `FI-008` | Package/update/backend reports success while expected product/version is absent or update service does not converge. | Record at most applied/pending/review; independent verification fails; finite pass/restart state and recovery guidance remain; readiness stays blocked. | Backend success becomes verified/ready, infinite update loop, settings scraping, firmware injection, or raw output leakage. | Targeted at verification `TL-0406`; Full at update sequence `TL-0504`/`TL-0505` when task contract triggers. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-scenario command or bounded procedure before invocation` | Defined; `Not run` |
| `FI-009` | Installed application launch immediately fails, hangs, or resolves the wrong executable/version. | Launch verification fails with stable bounded reason; state is not verified; safe cleanup/retry/manual guidance is shown. | Presence alone becomes functional pass, arbitrary executable/path is launched, infinite wait, or failure is hidden. | Targeted at application verification `TL-0406`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-scenario command or bounded procedure before invocation` | Defined; `Not run` |
| `FI-010` | Synthetic job/database/report or migration is corrupt, interrupted, older, or newer than supported; export final replace can fail. | Transaction rolls back or open/export blocks safely; original/source job remains intact; backup/recovery is explicit; no corrupt final artifact/history rewrite. | Silent data loss, partial migration, raw fallback, unintended overwrite, invented evidence, or personal destination path in logs. | Targeted at persistence `TL-0102`; Full at migration/lifecycle `TL-0704`; export subcase targeted at `TL-0604`/`TL-0606`. | TL-0102 persistence interruption subset: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\run-tl0102-sandbox.ps1 -Phase Interruption`; the four independently filtered child-process cases cover termination after first insert, before commit, after commit, and before migration commit. Report/export replacement remains owned by `TL-0604`/`TL-0606`. | Persistence subset implemented; Passed 4/4 in Windows Sandbox on the active machine; report/export subset `Not run` |
| `FI-011` | Clock moves, request/session expires, or resume token is stale, copied, replayed, or bound to another job/device/action/session. | Invalid authority is rejected; no action executes; timestamps/history remain immutable; operator starts a newly approved attributable path. | Replay, cross-job/device resume, silent expiry extension, or evidence timestamp rewrite. | Targeted at IPC/session `TL-0310`/`TL-0312` and resume `TL-0309`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-scenario command or bounded procedure before invocation` | Defined; `Not run` |
| `FI-012` | Expected/unexpected restart, safe interruption, or full powered-off cold boot resumes with failed/unavailable/changed post-boot verification. | Durable checkpoint binds correct job; fresh actual-state observation precedes retry/continue; failed/unknown checks and update convergence remain visible; readiness is blocked. | Warm/app restart or synthetic result counts as cold boot, old evidence is reused, unrelated job resumes, unsafe physical power cut, or false terminal success. | Targeted/full for resume at `TL-0309`/`TL-0505`; Extended physical cold boot at `TL-0509`; broader gate `TL-0510` only as triggered. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-scenario command or bounded procedure before invocation` | Defined; `Not run` |

## 6. Tier rules and reruns

- **Quick:** document/schema/static checks, ID uniqueness/order, links, and prohibited-claim wording. No injection at revised `TL-0008`.
- **Targeted:** changed subsystem plus the smallest deterministic fault case; required when a provider, persistence, privilege/IPC, package/update, network/filesystem, report, migration, accessibility, or recovery boundary changes.
- **Full:** applicable complete layers only at milestone/pilot/stable gates, major refactor/migration/dependency changes, or explicit task trigger.
- **Extended:** only the independently invokable interruption, resource, hostile-input, cold-boot, or endurance scenario whose named risk/task/gate triggers it.

On failure, rerun the single `FI-*` command first, then the related targeted set. Rerun full/extended scope only when the trigger remains applicable or a shared cause is suspected.

## 7. Interruption and cold-boot contract

Every interruption record includes last durable checkpoint, injection point, expected reopen state, first permitted recovery action, actual-state re-observation, and cleanup. Independent committed results remain visible; essential failure, unknown blocking evidence, or ambiguous mutation prevents ready disposition.

A cold boot is a reviewed operating-system shutdown to observable powered-off state followed by physical power-on of the active Codex machine. Process/application restart, ordinary warm restart, hosted restart, or ambiguous hybrid shutdown does not satisfy physical cold-boot evidence. The run records boot/session correlation and restart-sensitive rechecks. If the transition/correlation is unavailable, record `Not available` or `Fail`, never `Pass`. It is not run at `TL-0008`; earliest trigger is `TL-0509`.

## 8. Result record and current state

Every later result records identity/build/tier/command, active reference profile and hosted/constraint settings, fixture hash/provenance, actor/time, checkpoint/injection point, bounds, exact result/evidence class, integrity inspection, recovery/cleanup/residue, artifacts, defects, focused rerun, and limitation. Artifacts are bounded, sanitized, repository-relative or durable references—never raw logs or unrestricted local paths.

| Scenario range | Result | Evidence class | Human reviewer | Artifact | Broad matrix |
|---|---|---|---|---|---|
| `FI-001` provider-contract subset | `Pass` | `Observed` | None required | `ProviderFailureInjectionTests.FI001SmcProviderUnavailableRemainsFailClosed`; commit `e5e9c7ef26ddfb2674ce08d7ea35b0dd193b9a2c` | Not triggered; exact deterministic fake-provider case only |
| `FI-001` TL-0106 system-inventory subset | `Pass` | `Observed` | None required | `SystemInventoryProviderTests.FI001SmcSystemInventoryFailuresRemainFailClosed`; exact source commit and fixture digest pinned in TL-0106 task evidence | Not triggered; injected concrete-provider source states only |
| `FI-001` network/transfer variants | `Not run` | `Not available` | None | None | Owning network tasks have not reached their trigger |
| `FI-002`–`FI-012` | See any separately recorded owning-task subset; otherwise `Not run` | See owning-task evidence; otherwise `Not available` | None unless named by that task | See owning-task record | No broad matrix triggered by `TL-0105` |

### TL-0105 durable result — `FI-001` / `SMC-PROVIDER-UNAVAILABLE` provider-contract subset

- **Task/source:** `TL-0105`; branch `codex/tl-0105-provider-contracts`; evidence-hardening commit `e5e9c7ef26ddfb2674ce08d7ea35b0dd193b9a2c`.
- **Tier/trigger:** Targeted; first common provider-contract implementation.
- **Reference environment:** Direct active Codex machine; `REF-CODEX-001` revision `2026-08-21.1`; Windows 11 x64; .NET SDK 10.0.400; no hosted constraint, elevation, permission change, real provider disablement, or network injection.
- **Fixture/provenance:** Project-created in-code fake-provider matrix `TL0105-FI001-CONTRACT-1` in `tests/ThirdLife.Inventory.Tests/ProviderFailureInjectionTests.cs`; SHA-256 `fe32b7739ed67188fab2af379d939ba2f5ecfe9ab27d24ada883b34ad66a8a5e`; synthetic, non-personal data except a deliberate non-real sensitive-seed string used only to prove exception-text exclusion.
- **Invocation/time:** Exact filtered command above; final recorded run start `2026-08-28T21:14:43.9460286+02:00`; end `2026-08-28T21:14:45.7369423+02:00`; 177 ms reported test duration / 1.787 s wall.
- **Injection points/bounds:** Provider unavailable with one unrelated Boolean retained; access denied; wrong scalar types causing contract invalid; thrown exception containing the seed; and a 50 ms cooperative timeout. Two declared scalar facts, one provider invocation per case, descriptor/runner text/count/cardinality bounds, and one-second cancellation-observation bound apply.
- **Result/integrity:** `Pass` 1/1. Every outcome, exact recovery action, exact limitation, provider-status code, unavailable affected fact, partial-evidence rule, single invocation, and cancellation signal matched. Serialized output excluded the seeded exception text and its identifying fragments. The deterministic fake has no mutation API and the test setup changed no provider, host permission, network, or security control.
- **Cleanup/residue/accessibility/resources:** Cancellation was observed; no file, process, database, network transfer, hosted workspace, or retained artifact was created, so cleanup/residue and UI/accessibility/resource measurements are not applicable to this contract-level case.
- **Defect/focused rerun:** Final review found recovery validation was only enum-defined. Commit `e5e9c7e` replaced it with exact recovery/limitation/error-code assertions; the exact case and all 22 Inventory tests passed afterward.
- **Claim limitation:** This proves only common fail-closed normalization and raw-exception sanitization for the five represented deterministic failures. Typed text/enum evidence is not semantically sanitized by TL-0105. Concrete Windows APIs, provider-payload minimization, real access denial, network/transfer behavior, hardware facts, hard termination, resource/UI behavior, and the broad FI/SMC matrix remain unrun.

### TL-0106 durable result — `FI-001` / `SMC-PROVIDER-UNAVAILABLE` system-inventory subset

- **Task/source:** `TL-0106`; branch `codex/tl-0106-system-inventory`; exact final commit is pinned in `TASKS.yaml` after completion synchronization.
- **Tier/trigger:** Targeted; first concrete Windows inventory acquisition/parser and required per-provider failure case.
- **Reference environment:** Direct active Codex machine; `REF-CODEX-001` revision `2026-08-29.1`; Windows 11 x64; .NET SDK 10.0.400; no hosted constraint, elevation, permission change, provider disablement, network injection, or system mutation.
- **Fixture/provenance:** Project-created in-code source-state matrix `TL0106-FI001-SYSTEM-INVENTORY-1` in `SystemInventoryProviderTests.FI001SmcSystemInventoryFailuresRemainFailClosed`; wholly synthetic identity strings and buffers with `synthetic_fixture` provenance. The separate JSON system-inventory fixture and exact test/source hashes are pinned in TL-0106 evidence.
- **Invocation/time:** `dotnet test .\tests\ThirdLife.Inventory.Tests\ThirdLife.Inventory.Tests.csproj -c Release --no-restore --no-build --filter "FullyQualifiedName=ThirdLife.Inventory.Tests.SystemInventoryProviderTests.FI001SmcSystemInventoryFailuresRemainFailClosed" --logger "console;verbosity=minimal"`; start `2026-08-29T08:50:58.2541357+02:00`; end `2026-08-29T08:50:59.9432087+02:00`; 58 ms reported test duration / 1.689 s wall.
- **Injection points/bounds:** Firmware source unavailable; firmware source access denied; and declared-length-corrupt SMBIOS. Independent x64, installed-memory, and logical-count source values remain available. The provider uses one source invocation, a 1 MiB firmware ceiling, strict table/record/text/count/range bounds, stable error codes, and zero retention for the raw buffer.
- **Result/integrity:** `Pass` 1/1. Outcomes and error codes were exactly unavailable/access-denied/invalid-data; serial remained unknown; independent x64 evidence remained attributable; no synthetic serial escaped through other observations; and the malformed buffer was cleared.
- **Cleanup/residue/accessibility/resources:** Owned raw bytes were zeroed. The deterministic case created no file, process, database, network transfer, hosted workspace, UI, or retained artifact, so no cleanup residue or accessibility walkthrough applies.
- **Defect/focused rerun:** Initial generic-value absence handling misclassified unavailable numeric sources as invalid data; an explicit source-value presence bit and focused regression corrected it. Independent reviews also found and closed serial stringification, field-global string bounds, placeholder separators, Type-4 processor-type/population filtering, emulated architecture, and cancellation-cleanup proof gaps. The exact case and complete Targeted suite passed after the fixes.
- **Claim limitation:** This proves injected code paths, not a real host permission denial, disabled provider, different firmware/manufacturer, another machine, network/transfer fault, or forcibly pre-empted native call. Firmware/OS evidence remains self-reported and cannot establish authenticity, ownership, eligibility, reliability, or readiness.

Approval of this register's definitions alone is not execution evidence. Only the narrow result records above establish their stated subset. No result here supplies a human completion requirement, release authorization, or hardware/reliability claim.
