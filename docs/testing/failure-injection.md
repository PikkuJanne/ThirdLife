# ThirdLife Setup Core — Failure-Injection Procedure

**Status:** Draft procedure; human evidence pending  
**Procedure revision:** TL-0008 draft 1  
**Task:** `TL-0008`  
**Authority:** `DECISIONS.md`, `ROADMAP.md`, `PROJECT_BOUNDARY.md`, `SECURITY.md`, `ACCESSIBILITY.md`, `LOW_SPEC.md`, and `AGENTS.md`  
**Execution state:** No failure-injection run or human result is recorded by this document.

## 1. Purpose and claim boundary

This document defines repeatable, reviewable failure scenarios for ThirdLife Setup Core. It is a plan for later implementation and lab tasks, not evidence that the application, broker, persistence, package, update, report, accessibility, or recovery behavior already exists or has passed.

The procedure supports D-015 evidence semantics, D-033 cold-boot verification, and the failure-injection layer in the binding roadmap. A scenario passes only when the deliberately introduced fault produces the scenario's expected safe, truthful, recoverable outcome. A rejected or failed operation can therefore be a **Pass** for a failure-injection scenario; it does not mean the underlying operation succeeded.

Automation and VM results cannot prove physical-device behavior or long-term hardware reliability. A future physical result must be recorded separately and human confirmed. Nothing in this procedure authorizes unsupported Windows installation, processor/TPM/Secure Boot bypass, activation or ownership bypass, firmware flashing, TPM clearing, destructive storage testing, malware cleanup, or work on donor data.

## 2. Result and evidence semantics

`test_result` and `evidence_class` are separate fields.

| Field | Exact value | Meaning |
|---|---|---|
| `test_result` | `Pass` | The complete scenario was run, the injected fault was observed, every required invariant held, and required recovery and cleanup succeeded. |
| `test_result` | `Fail` | An expected invariant, recovery, cleanup, accessibility, security, or integrity condition did not hold. False success, corruption, unsafe retry, or unexplained state is always a failure. |
| `test_result` | `Not available` | The required capability, safe injection mechanism, environment, or evidence source was unavailable. It is not a pass. |
| `test_result` | `Not run` | The scenario was not started or not completed. A reason is required. It is not a pass. |
| `evidence_class` | `Observed` | A bounded provider, harness, journal, store, operating-system, or artifact observation was captured with source and provenance. |
| `evidence_class` | `Inferred` | A conclusion was derived from identified observations and its limits are stated. Inference alone cannot establish a required pass. |
| `evidence_class` | `Not available` | Required evidence could not be collected. Missing evidence remains unknown. |
| `evidence_class` | `Human confirmed` | A named human actually observed the physical or interaction result and recorded provider, timestamp, and provenance. Codex cannot create this evidence. |

Allowed reason codes for `Not available` include `capability_absent`, `equipment_missing`, `environment_unavailable`, `provider_unavailable`, `permission_denied`, and `unsafe_to_run`. `Not run` uses a reason such as `future_control_not_implemented`, `awaiting_approved_build`, `blocked_by_prerequisite`, `cancelled_before_injection`, or `human_run_pending`.

A `Pass` must have at least one available evidence item. `Pass` paired only with `Not available` evidence is invalid. Human confirmation is evidence provenance, not a substitute for an application, journal, or integrity assertion that can be independently observed.

## 3. Permitted environments and safety limits

Every run records hardware environment (`Physical` or `Virtual`), execution context (`Interactive lab` or `CI`), constraint profile (`None` or a named imposed constraint), and evidence source (direct observation, named provider/harness, or `Synthetic`) as orthogonal fields. The combinations below describe permitted setups; they do not replace those fields. A constrained VM remains virtual evidence, and a synthetic result cannot satisfy a physical-device or D-033 cold-boot requirement.

| Environment class | Permitted use | Limitation |
|---|---|---|
| `Synthetic` | Deterministic provider, journal, parser, storage, clock, and backend outcomes using non-personal fixtures. | Does not prove Windows integration or physical behavior. |
| `VM` | A disposable VM used for process termination, restart, service/backend loss, bounded virtual-disk exhaustion, corrupted copied stores, and recovery from a known snapshot. | Does not prove physical power, battery, firmware, ports, or long-term reliability. |
| `Constrained` | Reproducible CPU, memory, storage, priority, and network constraints applied to a disposable process, VM, or bounded test destination. | A constraint is a test class, not a supported minimum-spec claim. |
| `Physical` | An approved physical lab device used for safe network removal, ordinary UAC decline, operator interruption, full power-off/power-on, and observable hardware behavior after prerequisites are satisfied. | Requires a human operator, an approved reference device, and explicit cleanup. Destructive fault injection is prohibited. |

Every run must use a synthetic job and non-sensitive fixtures. Repository evidence uses opaque device references such as `LAB-DEVICE-001`; it must not contain names, contacts, serials or serial fragments, service or asset tags, hardware UUIDs, hostnames, usernames or SIDs, MAC/IP/SSID values, tenant/account data, product or recovery keys, personal paths, screenshots, raw logs, dumps, archives, or exact workshop locations.

The following are prohibited:

- filling a physical system volume, writing unknown removable media, destructive storage or battery stress, or deliberately draining or damaging a battery;
- abrupt physical power removal during firmware, Windows Update, storage mutation, migration, or another action where the approved test cannot prove a recoverable boundary;
- changing firmware, Secure Boot, TPM ownership/readiness, activation, MDM, Autopilot-style, anti-theft, ownership, or Windows eligibility state to manufacture coverage;
- disabling provenance, hash, signature, approval, verification, security, privacy, accessibility, or recovery controls;
- introducing arbitrary shell commands, executable paths, registry paths, URLs, or unrestricted file operations into product inputs;
- using a sibling product, sibling repository, private interface, or shared portfolio service.

## 4. Run prerequisites

Before any injection, the operator records:

1. procedure revision, scenario ID, exact ThirdLife build/version and source revision;
2. synthetic fixture ID and SHA-256 digest;
3. hardware environment, execution context, constraint profile, evidence source, opaque device/VM reference, supported Windows 11 x64 build, and architecture;
4. the scenario's owning implementation task and whether the required control is implemented;
5. the initial job/action state and the exact durable checkpoint at which injection is allowed;
6. recovery image, snapshot, copied synthetic store, or other approved restoration method;
7. bounded storage, time, retry, output, and process limits;
8. a stop condition that prevents the injection from crossing an unreviewed mutation boundary;
9. required accessibility observations and a keyboard-reachable recovery path; and
10. the planned integrity, residue, privacy, and cleanup inspection.

If a prerequisite is absent, record `Not available` or `Not run`; do not improvise a more destructive mechanism. A pending firmware or update mutation, donor data, unclear ownership state, or absent recovery method prevents the run.

## 5. Scenario catalogue

All catalogue rows are plans. Their initial result is `Not run` and initial evidence class is `Not available`.

| ID | Planned fault and checkpoint | Permitted environment | Expected invariant and recovery | Must not happen | Earliest owning task | Initial result | Initial evidence class |
|---|---|---|---|---|---|---|---|
| `FINJ-001` | Provider timeout, exception, malformed value, or unavailable result before normalized evidence commit. | Synthetic; Disposable VM | Record bounded sanitized failure or not-available evidence; unrelated observations remain independent; safe rerun is offered. | Missing or malformed data becomes pass; raw provider output is persisted. | `TL-0105`, `TL-0112` | `Not run` | `Not available` |
| `FINJ-002` | Operator declines UAC before broker authorization. | Disposable VM; Approved physical lab device | No privileged mutation; durable state remains truthful; UI explains retry or stop without a mouse. | Approval or applied state is minted; a privileged process remains. | `TL-0313` | `Not run` | `Not available` |
| `FINJ-003` | Network is absent before a network-dependent action starts. | Synthetic; Disposable VM; Approved physical lab device | Offline-capable work remains usable; network action fails or defers before unsafe mutation with clear recovery. | Core data becomes unavailable; absence is reported as success. | `TL-0405`, `TL-0504` | `Not run` | `Not available` |
| `FINJ-004` | Network is removed during bounded metadata or package transfer. | Disposable VM; Approved physical lab device | Partial untrusted output is quarantined or removed; checkpoint remains retry-safe; later retry revalidates identity and trust. | Partial bytes execute; stale approval remains valid after material change. | `TL-0405`, `TL-0408` | `Not run` | `Not available` |
| `FINJ-005` | Slow, intermittent, high-latency, filtered, or captive network. | Constrained environment; Approved physical lab device | Progress, timeout, cancellation, uncertainty, and recovery remain visible and bounded. | Infinite retry, UI deadlock, hidden fallback source, or unbounded cache growth. | `TL-0405`, `TL-0510` | `Not run` | `Not available` |
| `FINJ-006` | Required disk headroom is insufficient at preflight. | Synthetic; Disposable VM; Constrained environment | Execution is blocked before download or mutation; estimates and uncertainty remain visible. | Modification begins or unknown estimates are treated as zero. | `TL-0503` | `Not run` | `Not available` |
| `FINJ-007` | A bounded disposable destination becomes full after preflight. | Disposable VM; Constrained environment | Atomic/partial output rules hold; journal records failure or review-required state; recovery and cleanup are explicit. | Physical system drive is filled; completed/verified state is recorded; existing data is corrupted. | `TL-0503`, `TL-0606` | `Not run` | `Not available` |
| `FINJ-008` | UI exits before action dispatch. | Synthetic; Disposable VM | No started checkpoint or privileged mutation exists; reopening shows the unchanged approved plan. | A queued action executes without attributable dispatch. | `TL-0308`, `TL-0313` | `Not run` | `Not available` |
| `FINJ-009` | UI exits after the durable started/dispatch-intent checkpoint. | Synthetic; Disposable VM | Correlated broker result or later reconciliation controls state; reopening exposes ambiguity and recovery. | UI disappearance marks verified or causes blind repeat. | `TL-0308`, `TL-0313` | `Not run` | `Not available` |
| `FINJ-010` | Broker exits before backend mutation. | Synthetic; Disposable VM | Attempt is failed or requires review after reconciliation; broker leaves no permanent elevated process. | Applied/verified is inferred from dispatch; automatic unsafe retry. | `TL-0311`, `TL-0312` | `Not run` | `Not available` |
| `FINJ-011` | Broker exits during or immediately after an ambiguous mutation attempt. | Synthetic; Disposable VM | Started attempt remains attributable; actual state is re-observed before any retry; ambiguity blocks ready state. | Duplicate mutation, history rewrite, or backend exit code treated as verification. | `TL-0312`, `TL-0408`, `TL-0409` | `Not run` | `Not available` |
| `FINJ-012` | Package/update/backend is unavailable, hangs, or exceeds its timeout. | Synthetic; Disposable VM | Timeout and cancellation are bounded; failure classification and safe retry policy are explicit. | Permanent elevated process, infinite wait, raw backend output, or success state. | `TL-0405`, `TL-0407` | `Not run` | `Not available` |
| `FINJ-013` | Backend reports success while the expected package, version, or launch result is absent. | Synthetic; Disposable VM | Record at most applied; independent verification fails; readiness remains blocked. | Backend success becomes verified or ready. | `TL-0406` | `Not run` | `Not available` |
| `FINJ-014` | Catalogue/package metadata or approved content digest changes between preview and execution. | Synthetic; Disposable VM | Approval is invalidated and a new preview is required; no stale request executes. | Source substitution, downgrade, or unchanged approval survives a material change. | `TL-0307`, `TL-0403` | `Not run` | `Not available` |
| `FINJ-015` | Cancellation occurs before dispatch, during a cancellable phase, or during a declared non-cancellable phase. | Synthetic; Disposable VM | Phase-specific behavior is explained; terminal or review-required state is exactly once; recovery remains keyboard reachable. | Cancellation is reported before it takes effect; state or partial output is lost. | `TL-0405`, `TL-0505` | `Not run` | `Not available` |
| `FINJ-016` | An approved action reaches an expected restart checkpoint and the application resumes later. | Synthetic; Disposable VM | Device/job-bound checkpoint is durable; resume revalidates actual state and continues within finite bounds. | Pre-restart evidence is reused as fresh verification; unrelated job resumes. | `TL-0309`, `TL-0505` | `Not run` | `Not available` |
| `FINJ-017` | Unexpected reboot or process/power interruption at a reviewed safe checkpoint. | Disposable VM only until separately approved | Reopen shows a started or review-required attempt; reconciliation precedes retry; store and journal remain intact. | Physical power cut during mutation; blind retry; false terminal success. | `TL-0309`, `TL-0510` | `Not run` | `Not available` |
| `FINJ-018` | Resume token is stale, copied, expired, or bound to another device, job, action, or session. | Synthetic; Disposable VM | Token is rejected; no action executes; operator starts a newly approved path. | Cross-job/device resume or replay. | `TL-0309`, `TL-0509` | `Not run` | `Not available` |
| `FINJ-019` | Fresh post-boot observation finds a new blocker or changed system state. | Synthetic; Disposable VM; Approved physical lab device | Verification fails or requires review; ready state is blocked and prior history remains visible. | Checkpoint existence alone satisfies acceptance. | `TL-0507`, `TL-0509` | `Not run` | `Not available` |
| `FINJ-020` | Report/support export is cancelled, destination becomes unavailable/full, or final replace fails. | Synthetic; Disposable VM; Constrained environment | No corrupt final artifact; bounded partial output is removed or explicitly recoverable; source job remains intact. | Raw fallback, unintended overwrite, personal destination path in ordinary logs, or false export success. | `TL-0604`, `TL-0606` | `Not run` | `Not available` |
| `FINJ-021` | Transaction, copied synthetic database, or migration is interrupted or corrupt. | Synthetic; Disposable VM | Transaction rolls back or safe recovery blocks open; original is preserved; no history is rewritten. | Unsafe partial migration, silent data loss, or invented passed record. | `TL-0102`, `TL-0704` | `Not run` | `Not available` |
| `FINJ-022` | Clock moves or request/session expiry is reached. | Synthetic; Disposable VM | Expired or invalid temporal authority is rejected; a new attributable session is required. | Replay succeeds or evidence timestamps are silently rewritten. | `TL-0310`, `TL-0312` | `Not run` | `Not available` |
| `FINJ-023` | Memory or another bounded resource is exhausted. | Synthetic; Disposable VM; Constrained environment | Work stops recoverably, safe cancellation remains, and no job/journal/report corruption occurs. | Unhandled resource failure, UI deadlock, or accessibility/security check is disabled. | `TL-0503`, `TL-0510` | `Not run` | `Not available` |
| `FINJ-024` | Manual-test workflow closes, pauses, or loses a known test peripheral between steps. | Synthetic; Approved physical lab device | Completed results remain attributable; incomplete test is `Not run` or `Not available`; resume does not invent a pass. | Device presence becomes functional pass; unrelated results are erased. | `TL-0114` | `Not run` | `Not available` |
| `FINJ-025` | Windows Update service is unavailable or the finite scan/install/restart sequence does not converge. | Synthetic; Disposable VM | Explicit service/offline result, pending-restart state, bounded pass count, and recovery guidance are retained. | Settings screen scraping, firmware update injection, infinite loop, or current claim. | `TL-0504`, `TL-0505` | `Not run` | `Not available` |
| `FINJ-026` | Full powered-off cold boot resumes, but post-boot independent verification fails or is unavailable. | Approved physical lab device; synthetic negative-state supplement that cannot satisfy D-033 | Cold-boot checkpoint remains unsatisfied; exact failed/unavailable checks are visible; readiness is blocked. | Warm process restart, synthetic-only result, or ambiguous hybrid shutdown counts as cold boot; old evidence passes. | `TL-0509`, `TL-0510` | `Not run` | `Not available` |

## 6. Interruption and cold-boot contract

Every interruption scenario records the last durable checkpoint, the interruption point, the expected state after reopening, the first permitted recovery action, the required actual-state re-observation, and cleanup. Independent results already committed remain visible; an independent failure must not erase them. Essential failure, unknown blocking evidence, or an ambiguous mutation prevents a ready disposition.

A cold boot means a reviewed operating-system shutdown to an observable powered-off state followed by physical power-on. A process restart, application reopen, ordinary warm restart, or shutdown whose Fast Startup/hybrid status cannot be distinguished does not satisfy `FINJ-026`. The run records the boot/session correlation and reruns restart-sensitive verification. If the full power transition or correlation cannot be established, record `Not available` or `Fail`, never `Pass`.

## 7. Result record

Each executed scenario creates a durable result with these fields:

| Field | Requirement |
|---|---|
| Identity | Result schema version, procedure revision, run ID, scenario ID, and owning task. |
| Build | ThirdLife version, 40-hex source revision, configuration, Windows edition/build, and x64 architecture. |
| Environment | Hardware environment, execution context, constraint profile, evidence source, opaque device/VM reference, and relevant matrix requirement IDs. |
| Fixture | Synthetic fixture ID, version, SHA-256 digest, and privacy review state. |
| Actor/time | Provider or human operator role, start/end timestamps with offset, and reviewer. |
| Checkpoint | Initial state, last durable checkpoint, injection point, correlation identifiers, and expected terminal/review state. |
| Bounds | Timeout, retry, collection, output, temporary-storage, and process-lifetime bounds. |
| Outcome | Exact `test_result`, exact `evidence_class`, stable result codes, and bounded observations. |
| Integrity | Job, journal, database, output, cache/temp, privileged-process, and unintended-mutation inspection. |
| Recovery | Recovery attempted, re-observation, retry decision, cleanup result, remaining residue, and next safe action. |
| Evidence | Repository-relative or approved durable artifact references and SHA-256 digests; no unrestricted local path. |
| Limitations | Unavailable evidence, environment limitations, defect/blocker IDs, and rerun requirement. |

The empty result register is intentional:

| Scenario range | Test result | Evidence class | Human reviewer | Evidence reference |
|---|---|---|---|---|
| `FINJ-001`–`FINJ-026` | `Not run` | `Not available` | Pending | Pending |

## 8. Review, defects, and future gates

- Any false verified/ready state, corruption, unsafe privileged residue, security/privacy boundary loss, inaccessible recovery, or unbounded resource growth is a blocking failure.
- A fixed failure receives a new run ID linked to the prior result; prior evidence is not overwritten.
- VM, constrained, automated, physical, and human evidence remain distinguishable.
- `TL-0510` owns the full-profile failure-injection execution. This draft does not pre-approve its mechanisms or results.
- Procedure approval confirms only that this plan and an actual reference-device pool were reviewed. It is not a release authorization or a claim that any scenario passed.

Human execution of these future failure scenarios remains pending. Completion of the TL-0008 reference-device walkthrough does not approve or pass them. Codex must not mark a physical or human-assisted scenario `Pass`, supply a human reviewer, or claim long-term reliability.
