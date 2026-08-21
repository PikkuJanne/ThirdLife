# ThirdLife Setup Core — Failure-injection scenario register

**Status:** Active specification; defined but not executed at revised `TL-0008`  
**Procedure revision:** TL-0008 same-machine revision 2  
**Task:** `TL-0008`  
**Execution state:** `FI-001`–`FI-012` are defined and individually addressable; all are `Not run`.

## 1. Purpose and claim boundary

This register consolidates the detailed provider, network, resource, process, broker, package, persistence, interruption, update, manual-workflow, and cold-boot failure cases into twelve canonical scenarios. Each scenario retains fail-closed state, integrity, accessibility, recovery, cleanup, and evidence requirements. Revised `TL-0008` defines the cases, tier triggers, and non-executable command placeholders only; it does not inject a fault or run a broad matrix.

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

Every command cell is deliberately non-executable. The earliest owning task replaces it with a checked-in single-scenario command or bounded human procedure. A runner that can invoke only the whole matrix does not satisfy D-062.

| ID | Consolidated fault/checkpoint | Expected invariant and recovery | Must not happen | Default tier and explicit earliest trigger | Individually invokable command/procedure | TL-0008 state |
|---|---|---|---|---|---|---|
| `FI-001` | Network/provider unavailable before operation, including timeout, exception, malformed/access-denied evidence. | Offline-capable work remains usable; no mutation; required evidence stays unavailable; unrelated observations remain; bounded retry/next action is shown. | Missing/malformed data becomes pass, raw provider output persists, or core local data becomes unavailable. | Targeted at provider work beginning `TL-0105`, network action `TL-0405`, or update scan `TL-0504`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-scenario command or bounded procedure before invocation` | Defined; `Not run` |
| `FI-002` | Network interruption, slowness, filtering, or timeout during metadata/package transfer. | Partial untrusted bytes are bounded/removed or quarantined; progress/cancel/uncertainty remain accessible; checkpoint is retry-safe; retry revalidates identity/trust. | Partial execution, infinite retry/wait, stale approval, hidden source fallback, UI deadlock, or unbounded cache. | Extended at transfer risk `TL-0405`/`TL-0408` or failure gate `TL-0510`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-scenario command or bounded procedure before invocation` | Defined; `Not run` |
| `FI-003` | Insufficient capacity/resource before mutation or bounded destination/resource exhaustion after preflight. | Preflight preserves rollback headroom; mid-operation failure leaves truthful review/failed state; output/store remains atomic or recoverable; cleanup is bounded. | Host system volume is filled, source data corrupts, work appears complete/verified, or security/accessibility is disabled for resources. | Targeted at storage preflight `TL-0503`; Extended for mid-write/resource risk at `TL-0510`/`TL-0606`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-scenario command or bounded procedure before invocation` | Defined; `Not run` |
| `FI-004` | UI/manual workflow terminates or pauses before dispatch, after durable intent, or between manual-test steps. | Pre-dispatch state is unchanged; post-intent ambiguity is reconciled; completed manual results persist; incomplete work remains `Not run`/`Not available`; reopening offers safe recovery. | Unattributable dispatch, false verified/pass, blind repeat, wrong-job resume, or loss/rewrite of independent results. | Targeted at journal `TL-0308`, manual workflow `TL-0114`, or UI/broker integration `TL-0313`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-scenario command or bounded procedure before invocation` | Defined; `Not run` |
| `FI-005` | Broker/backend terminates before mutation, during mutation, or immediately after an ambiguous attempt. | Correlated attempt remains attributable; no permanent elevated process; actual state is re-observed before retry; ambiguity blocks readiness. | Dispatch/backend exit becomes applied/verified, duplicate mutation, automatic unsafe retry, history rewrite, or privileged residue. | Targeted before mutation at `TL-0311`/`TL-0312`; Extended for ambiguous mutation at `TL-0408`/`TL-0409`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-scenario command or bounded procedure before invocation` | Defined; `Not run` |
| `FI-006` | Operator declines UAC before broker authorization. | No privileged mutation or approval; job remains valid/truthful; retry/stop is clear and keyboard reachable; broker exits. | Applied/approved state is minted, UI loses context, inaccessible recovery, or elevated process remains. | Targeted at broker/UI approval `TL-0313`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-scenario command or bounded procedure before invocation` | Defined; `Not run` |
| `FI-007` | Catalogue/package identity, source, publisher, version, content digest, or trust metadata changes after preview/approval. | Approval is invalidated; a new resolution/preview is required; no stale request executes. | Source substitution, downgrade, hash bypass, or unchanged approval after material change. | Targeted at approval/digest `TL-0307` or catalogue resolution `TL-0403`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-scenario command or bounded procedure before invocation` | Defined; `Not run` |
| `FI-008` | Package/update/backend reports success while expected product/version is absent or update service does not converge. | Record at most applied/pending/review; independent verification fails; finite pass/restart state and recovery guidance remain; readiness stays blocked. | Backend success becomes verified/ready, infinite update loop, settings scraping, firmware injection, or raw output leakage. | Targeted at verification `TL-0406`; Full at update sequence `TL-0504`/`TL-0505` when task contract triggers. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-scenario command or bounded procedure before invocation` | Defined; `Not run` |
| `FI-009` | Installed application launch immediately fails, hangs, or resolves the wrong executable/version. | Launch verification fails with stable bounded reason; state is not verified; safe cleanup/retry/manual guidance is shown. | Presence alone becomes functional pass, arbitrary executable/path is launched, infinite wait, or failure is hidden. | Targeted at application verification `TL-0406`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-scenario command or bounded procedure before invocation` | Defined; `Not run` |
| `FI-010` | Synthetic job/database/report or migration is corrupt, interrupted, older, or newer than supported; export final replace can fail. | Transaction rolls back or open/export blocks safely; original/source job remains intact; backup/recovery is explicit; no corrupt final artifact/history rewrite. | Silent data loss, partial migration, raw fallback, unintended overwrite, invented evidence, or personal destination path in logs. | Targeted at persistence `TL-0102`; Full at migration/lifecycle `TL-0704`; export subcase targeted at `TL-0604`/`TL-0606`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-scenario command or bounded procedure before invocation` | Defined; `Not run` |
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
| `FI-001`–`FI-012` | `Not run` | `Not available` | None | None | Not triggered by `TL-0008` |

Approval of this register confirms only that scenarios, safe environments, invariants, tier triggers, invocation placeholders, evidence, and cleanup contracts are defined. It is not evidence that a scenario passed, a human completion requirement, release authorization, or a hardware/reliability claim.
