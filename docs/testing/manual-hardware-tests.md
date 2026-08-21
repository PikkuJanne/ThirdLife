# ThirdLife Setup Core — Guided manual hardware-test workflow specification

**Status:** Active specification; defined but not executed at revised `TL-0008`  
**Specification revision:** TL-0008 same-machine revision 2  
**Execution scope:** Future named tasks and gates only; active Codex machine is the only physical development/test hardware  
**Current result:** `MHT-001`–`MHT-021` are all `Not run` with evidence `Not available`

## 1. Scope and limitation

This document specifies how ThirdLife Setup Core will guide a volunteer or staff refurbisher through repeatable, human-assisted checks for a sanitized Windows device. It covers point-in-time function of input, display, audio/video, networking, power, ports, sleep/wake, interruption/resume, partial failure, and a full powered-off cold boot. It is a product contract for instructions, state transitions, evidence, pause/resume, cleanup, reports, and limitations; it is not an instruction to perform the checks while implementing `TL-0008`.

Variant and risk coverage is recorded in the [Capability and risk coverage matrix](capability-risk-matrix.md). That matrix is not a device inventory. Development uses deterministic fixtures, sanitized captured provider samples, safe same-machine constraints, and later bounded observation of capabilities actually available on the active Codex machine. There is no device pool, equipment-acquisition gate, second PC, or required hardware class.

The workflow does not erase donor media, repair an existing personal computer, certify a device, diagnose every intermittent fault, or prove future service life. Automation, fixtures, constraints, and a single manual run cannot prove long-term hardware reliability or cross-hardware compatibility. A result describes only the exact job, machine, environment, specification revision, time, procedure, and evidence recorded.

The normal target is supported Windows 11 x64. An unsupported operating system, processor, TPM state, Secure Boot state, activation state, ownership control, or management state must remain visible and may block normal readiness. This procedure never authorizes a bypass.

## 2. Invocation, preconditions, and safety stops

`TL-0008` defines this specification only. It does not invoke any `MHT-*` scenario. A later task may invoke a bounded scenario only when its task/gate contract names the trigger, the product behavior needed by that scenario exists, and the run is safe on the active Codex machine. A future operator job outside development follows the same safety and evidence rules on the sanitized device being prepared.

Before a future run, the operator must:

1. use an externally sanitized device or a project-created non-personal fixture; ThirdLife Setup Core does not perform donor-media erasure;
2. assign an opaque run/job identifier and avoid serial numbers, asset tags, donor/recipient information, usernames, personal paths, device names, SSIDs, IP addresses, credentials, recovery material, or organization secrets;
3. record the specification revision, tested source commit/build, Windows edition/build, architecture, power state, hosted environment or active reference-machine profile, and approved test-equipment class;
4. confirm that every test peripheral and medium is known, approved, and free of personal content;
5. create the first durable checklist checkpoint before changing power state or attaching equipment;
6. define cleanup, recovery, and abort conditions before any safe active-machine observation; and
7. stop if ownership, sanitization, electrical safety, or the intended environment is uncertain.

Immediately stop and disconnect power when safe for battery swelling, smoke, unusual heat, liquid damage, exposed conductors, sparking, a damaged charger, a burning smell, or another physical hazard. Record `Fail` when the applicable inspection exposes the condition; record `Not available` with `unsafe_to_run` when the inspection cannot safely be completed. Do not open the enclosure, stress a suspect battery or storage device, flash firmware, change Secure Boot, clear a TPM, bypass Windows requirements, defeat an ownership/management control, or continue merely to complete the checklist.

## 3. Result and evidence semantics

Every real product result has two orthogonal fields:

| Field | Allowed values | Meaning |
|---|---|---|
| `test_result` | `Pass`, `Fail`, `Not available`, `Not run` | Outcome of the exact test procedure. |
| `evidence_class` | `Observed`, `Inferred`, `Not available`, `Human confirmed` | D-015 provenance class. |

Apply these rules:

- `Pass` means all stated pass criteria were met for this run. Hardware presence, provider output, or an inference alone cannot pass a functional test.
- `Fail` means an applicable test was performed and one or more stated criteria were not met, or the test exposed an unsafe condition.
- `Not available` never passes. Use `capability_absent`, `equipment_missing`, `provider_unavailable`, `unsafe_to_run`, or `permission_denied` and explain the specific condition.
- `Not run` means deferred, interrupted, phase-not-implemented, or not attempted. It is distinct from `Not available`.
- `Human confirmed` is evidence provenance, never a result by itself. It can support either `Pass` or `Fail` and must identify the human and role.
- `Inferred` evidence may explain or prioritize another observation but cannot independently pass a functional test.
- A named provider observation proves only the normalized provider facts it returned; it does not prove physical function.
- A deterministic synthetic fixture verifies application behavior and is labelled as test evidence. It is never stored or reported as a real-device result and cannot become `Human confirmed` evidence.
- The phrase “observed by provider” maps to the D-015 evidence class `Observed`; the named provider is recorded as provenance, not invented as a fifth evidence class. “Simulated deterministic test” is harness/source provenance only, not a persisted real-device evidence class.
- Missing, conflicting, or stale evidence remains unknown. Record the limitation and do not select `Pass`.

An interrupted run retains completed records and the last durable checkpoint. On resume, the operator re-identifies the device and environment, reviews prior results, repeats any test whose state may have changed, and never silently converts an incomplete result to a pass.

`Not run` is the planning/checklist label in these TL-0008 documents. When the future `TL-0114` implementation persists a manual-test result, it maps exactly to that task's `not tested` value; `Pass`, `Fail`, and `Not available` map without a semantic change. No persisted result may invent a fifth outcome or turn this mapping into a pass.

Every run separately records hardware environment (`Physical` or `Virtual`), execution context (`Interactive active-machine`, `Hosted active-machine`, or `Automated`), constraint profile (`None` or a named safe same-machine constraint), and evidence source (`Direct human observation`, named provider, or `Synthetic deterministic fixture`). A hosted or constrained environment remains hosted/constrained evidence. It never becomes physical observation or proof about untested hardware.

## 4. Approved equipment and privacy

In a later triggered run, use only the minimum approved equipment already available for an applicable test, for example a known keyboard or pointer, approved display/cable, known headphones, an approved non-sensitive test network, and controlled test peripherals. Missing equipment produces `Not available`; it is not a TL-0008 blocker and does not require finding another machine or peripheral. Do not use unknown removable media or write to a donor device merely to test a port.

- Camera and microphone tests use an ephemeral preview or loopback. Do not retain recordings or images.
- Audio tests use a non-sensitive local test sound at a safe volume.
- Network tests confirm bounded connectivity only. Do not access personal services, enter personal credentials, or record an SSID, IP address, proxy credential, or captive-portal content.
- Bluetooth pairings and temporary network state created for testing are removed when the approved procedure calls for cleanup and removal is safe.
- Do not place screenshots, photographs, audio, video, raw device captures, or unreviewed attachments in repository evidence. Use bounded, normalized textual observations or structured records that exclude serials, names, personal content, notification text, paths, and network identifiers.

## 5. Product workflow and durable state

1. **Start:** identify the sanitized device and run, inspect safety, record environment and applicability, and create a checkpoint.
2. **Test:** run only applicable `MHT-*` procedures. Record each result immediately; do not hold an unbounded set of notes for later transcription.
3. **Pause:** save the current test ID, completion state, attached equipment, power/network state, and required safe cleanup.
4. **Resume:** re-identify device/run, inspect for changed safety or environment, reconcile completed records, and restart only the interrupted test.
5. **Cold boot:** only when a later named task/gate explicitly triggers it, perform `MHT-019` on the active Codex machine and repeat its selected post-boot checks as a distinct verification phase.
6. **Complete:** link defects and explicit capability limitations, perform cleanup, preview the sanitized record, and leave every deferred or unavailable test explicit.

An essential failure or unknown evidence is not overridden by progress through the checklist. Repair or retest creates a linked later record and preserves the original result. The application must validate state transitions so that `Not run`, `Not available`, interruption, missing provenance, or synthetic fixture coverage can never be silently converted to `Pass`.

At revised `TL-0008`, this workflow, its catalogue, and its transition rules are specifications only. No row below has been physically executed, no human evidence exists, and no device or hardware capability has been certified.

## 6. Manual test catalogue

Unless a row states otherwise, evidence is `Observed` or attributable `Human confirmed`; inference alone cannot pass. Each row is independently recorded and checkpointed.

| Test ID | Capability | Applicability and equipment | Preconditions and steps | Pass criteria | Fail criteria | Not-available handling | Evidence, checkpoint, and cleanup |
|---|---|---|---|---|---|---|---|
| `MHT-001` | Visual physical-safety inspection | Every physical device; good lighting only | With power disconnected where safe, inspect exterior, battery area, charger, ports, hinges, display, cables, and ventilation without opening the enclosure | No visible or sensed stop condition; safe to continue this procedure | Swelling, smoke, heat, liquid, exposed conductor, sparking, damaged charger, or other hazard | `permission_denied` or `unsafe_to_run` if inspection itself cannot be performed safely; stop | Record concise observation; checkpoint safety decision; isolate equipment according to workshop policy |
| `MHT-002` | Keyboard | Built-in or declared external keyboard; approved ephemeral text surface | Test representative letters, numbers, modifiers, navigation, editing, function keys, and keyboard-only dismissal; clear test text | Tested keys produce the intended input once and navigation works without a mouse | Missing, repeated, stuck, or wrong input, or required keyboard journey cannot complete | `capability_absent`, `equipment_missing`, or `unsafe_to_run` | No typed personal content; checkpoint key groups tested; clear the test surface |
| `MHT-003` | Pointer or touchpad | Built-in touchpad or declared pointer | Test move, primary/secondary activation, scroll, and drag alternative where applicable on a harmless local surface | Pointer is controllable and each tested action has the expected result | Drift, unintended activation, missing button/scroll, or unusable control | `capability_absent`, `equipment_missing`, or `permission_denied` | Record device type, not hardware serial; return pointer settings unchanged |
| `MHT-004` | Touchscreen or pen | Only when declared present; approved pen if required | Test representative screen regions, activation, scrolling, and pen contact without changing calibration | Declared touch/pen input responds accurately enough to complete the bounded checks | Dead region, persistent ghost input, unintended contact, or declared pen failure | `capability_absent` if not fitted; `equipment_missing` if approved pen unavailable | Record region/behavior, not screenshots with identifiers; leave calibration unchanged |
| `MHT-005` | Built-in display | Device with built-in display | At normal safe brightness, inspect startup and Windows surfaces, text readability, stable image, and brightness control without prolonged stress patterns | Stable usable image and tested brightness control work with no essential content obscured | No image, severe instability, unreadable essential region, or non-working declared control | `capability_absent`, `provider_unavailable`, or `unsafe_to_run` | Point-in-time observation is not a panel-longevity claim; restore initial brightness where known |
| `MHT-006` | External display or video output | Declared port plus approved display/cable | Attach known equipment, request supported display detection, inspect stable image, then disconnect safely | Output is detected and presents a stable usable image through the tested port/cable | No detection, unstable/corrupt image, repeated disconnect, or unsafe connector behavior | `capability_absent`, `equipment_missing`, `permission_denied`, or `unsafe_to_run` | Identify port class only; checkpoint connected state; restore prior display arrangement |
| `MHT-007` | Speakers and headphones | Declared output plus approved headphones when applicable | Play a bounded non-sensitive local test sound at safe volume through each applicable output | Sound is intelligible through each tested declared output with working bounded volume control | Missing, severely distorted, one-sided, or uncontrollable output | `capability_absent`, `equipment_missing`, `permission_denied`, or `unsafe_to_run` | Do not record audio; restore safe volume and remove headphones |
| `MHT-008` | Microphone | Declared microphone and ephemeral local level/loopback surface | Speak a non-personal test phrase, observe bounded input/loopback, then close the surface | Input is visibly or audibly attributable to the tested microphone without retained recording | No input, unusable distortion, or wrong declared input | `capability_absent`, `provider_unavailable`, `permission_denied`, or `unsafe_to_run` | Retain no recording or phrase; close preview and confirm no artifact was saved |
| `MHT-009` | Camera | Declared camera and ephemeral preview | Open approved local preview, confirm image and indicator behavior, cover/uncover lens if useful, then close | Live image responds to the physical scene and preview closes normally | No image, frozen/unrelated image, unusable corruption, or preview cannot close safely | `capability_absent`, `provider_unavailable`, `permission_denied`, or `unsafe_to_run` | Retain no image/video; avoid faces and personal background; close preview and verify capture stopped |
| `MHT-010` | Ethernet | Declared port/adapter, approved cable, and non-sensitive test network | Attach approved network, observe link, perform bounded approved connectivity check, then restore state | Link and approved bounded connectivity succeed for the tested path | No link, repeated loss, or approved connectivity fails while reference equipment works | `capability_absent`, `equipment_missing`, `provider_unavailable`, `permission_denied`, or `unsafe_to_run` | Do not record IP/network names; remove cable or restore initial state |
| `MHT-011` | Wi-Fi | Declared Wi-Fi and approved non-sensitive test network | Connect only under workshop policy, perform bounded connectivity check, disconnect, and forget temporary profile when required | Adapter connects to the approved test network and bounded connectivity succeeds | Adapter cannot connect, repeatedly drops, or connectivity fails while reference equipment works | `capability_absent`, `equipment_missing`, `provider_unavailable`, `permission_denied`, or `unsafe_to_run` | Do not record SSID or credential; remove temporary profile according to workshop policy |
| `MHT-012` | Bluetooth | Declared Bluetooth plus known approved peripheral | Discover/pair approved test peripheral, confirm one bounded interaction, then disconnect and remove pairing when required | Peripheral is discovered, pairs, and performs the declared interaction | Discovery, pairing, or declared interaction fails while reference equipment works | `capability_absent`, `equipment_missing`, `provider_unavailable`, `permission_denied`, or `unsafe_to_run` | Record peripheral class only; remove temporary pairing and verify cleanup |
| `MHT-013` | AC adapter and immediate charging indication | Battery device with approved matching adapter | Inspect adapter, connect power, observe stable AC state and immediate charging indication when battery state permits | Stable AC power and truthful immediate charging/power indication are observed | Intermittent power, unsafe heat/spark, incompatible adapter, or expected immediate indication absent | `capability_absent`, `equipment_missing`, `provider_unavailable`, or `unsafe_to_run` | No capacity or long-term charger claim; leave device on safe approved power |
| `MHT-014` | Brief battery/power-source transition | Safe battery device with sufficient charge | Checkpoint work, disconnect AC briefly, observe continued operation and power-source state, then reconnect before low power | Device remains stable briefly and reports the transition consistently | Immediate unsafe shutdown, instability, swelling/heat, or materially inconsistent power state | `capability_absent`, `provider_unavailable`, `unsafe_to_run`, or `permission_denied` | No endurance/capacity claim; reconnect promptly and record initial/final power state |
| `MHT-015` | Sleep and wake | Device supporting an approved sleep path | Checkpoint, request supported sleep, observe transition, wake with declared control, and recheck display/input/network state | Device enters and exits sleep and selected functions return without lost checklist state | Cannot enter/wake, hangs, loses essential function, or checklist state becomes ambiguous | `capability_absent`, `provider_unavailable`, `permission_denied`, or `unsafe_to_run` | Record whether network reconnection was rechecked; restore safe awake state |
| `MHT-016` | USB-A | Declared USB-A plus known non-storage test hardware | Attach approved harmless peripheral, verify declared interaction, eject/disconnect if applicable | Peripheral is detected and completes the bounded declared interaction | No detection, repeated disconnect, unsafe connector behavior, or interaction failure | `capability_absent`, `equipment_missing`, `permission_denied`, or `unsafe_to_run` | Never use unknown media or require a donor-device write; remove peripheral safely |
| `MHT-017` | USB-C, dock, or power delivery | Declared capability plus known compatible test hardware | Test only declared data/display/power function with approved equipment, one function at a time | Each explicitly tested declared function works with stable safe connection | Declared function fails, repeatedly disconnects, or creates unsafe heat/power behavior | `capability_absent`, `equipment_missing`, `permission_denied`, or `unsafe_to_run` | Do not infer every USB-C feature; record exact cable/device class; disconnect safely |
| `MHT-018` | Card reader or representative port | Declared port and organization-controlled non-personal test item | Attach/insert approved item, perform a bounded detection or read-only interaction, then eject/remove | Port detects and completes the exact bounded interaction | No detection, unstable connection, read failure, or physical damage | `capability_absent`, `equipment_missing`, `permission_denied`, or `unsafe_to_run` | No unknown medium and no required write; eject/remove and confirm no test content retained |
| `MHT-019` | Full powered-off cold boot and recheck | Later explicit cold-boot task/gate on the active Codex machine; no fixture/VM substitute for an executed physical result | Follow the dedicated cold-boot procedure in section 7 and repeat selected safety, display, input, network, and evidence checks | Observable full power-off, later physical power-on, fresh boot correlation, and all required rechecks recorded truthfully | Boot fails, state is ambiguous, essential recheck fails, or prior state is silently reused | `provider_unavailable`, `permission_denied`, or `unsafe_to_run`; ambiguous shutdown cannot pass | Durable checkpoint before shutdown; correlate pre/post runs; preserve attempted/applied/verified distinctions |
| `MHT-020` | Pause, close, reopen, and resume the manual workflow | Every representative walkthrough; current durable record surface | Checkpoint after a completed test, pause/close the record, reopen it, reconcile identity/state, and resume at the next safe test | Completed results persist, no test is duplicated/skipped, and interrupted state remains explicit | Lost/rewritten result, wrong-device resume, duplicate action, or incomplete result becomes pass | `provider_unavailable`, `permission_denied`, or `unsafe_to_run` | Use synthetic/non-personal record; preserve original checkpoint and cleanup before pause |
| `MHT-021` | Partial failure, repair/retest linkage, and blocker preservation | Any detected or pre-existing partial failure | Record original result, affected capability, impact and blocker/defect; after authorized repair, create a separate linked retest | Original failure remains immutable and later retest is separately attributable | Original evidence is overwritten, partial failure is hidden by other passes, or readiness ignores blocker | `capability_absent`, `provider_unavailable`, `permission_denied`, or `unsafe_to_run` | No repair is authorized by this document; retain links, limitation, recovery, and final cleanup state |

## 7. Cold-boot procedure

`MHT-019` is a separate verification phase, not another name for restart. It is defined now but is **not run for `TL-0008`**. The earliest named execution trigger is `TL-0509`; later pilot/stable gates may require a bounded repeat. Any development/release observation uses the active Codex machine only and proves only that recorded build/environment.

1. Confirm `MHT-001` still permits safe operation and the device is on approved power.
2. Save a durable pre-boot checkpoint containing device ID, run ID, procedure/source revisions, timestamp with offset, completed results, known failures, and selected post-boot rechecks.
3. Use the supported Windows power interface to request full shutdown. Do not alter firmware or weaken Fast Startup/security policy merely to force a result.
4. Observe full powered-off state: display off and other normal device-specific power indicators inactive. If full power-off is ambiguous, record `Not available`; a process restart or ambiguous hybrid shutdown cannot pass.
5. Wait only the bounded interval stated in the run record, then use the physical power control to start the device.
6. Record a fresh boot/session correlation and timestamp. Do not copy pre-boot provider values as post-boot evidence.
7. Recheck physical safety, built-in display, primary keyboard/pointer, applicable network state, and every earlier result identified as restart-sensitive.
8. Record each recheck separately and distinguish attempted, applied, and verified outcomes. A failed or unavailable essential recheck remains visible.
9. Complete cleanup and link any defect or explicit limitation. Do not claim cold-boot verification if any required correlation is missing.

A future owning task must state which cold-boot steps were physically executed and which were only reviewed or unavailable. A planned scenario, deterministic checkpoint test, hosted restart, or ordinary Windows restart is not cold-boot evidence.

## 8. Partial failures and blockers

A partial failure does not invalidate unrelated observations, but it cannot be hidden by them. Record:

- the exact affected capability and original `MHT-*` result;
- whether the condition is a safety stop, essential-function blocker, non-essential limitation, equipment gap, or unknown;
- user/workshop impact in plain language;
- defect and capability/risk limitation identifiers where applicable;
- any safe manual alternative without calling it equivalent proof;
- authorized repair/recovery owner and state, if one exists; and
- a separately timestamped retest rather than overwriting history.

When equipment is missing, use `Not available` with `equipment_missing` and record the bounded limitation. Missing equipment is not a development blocker and does not trigger acquisition or another-machine work. When no safe path exists, stop; do not improvise with unknown equipment.

## 9. Result-record template

Create one bounded record per test result. The following fields are required; `Pending` is allowed only while this draft has not been executed, never in completed evidence:

| Field | Required content |
|---|---|
| Record identity | Record ID, run ID, sanitized device ID, test ID, procedure revision, tested source commit |
| Attribution | Operator or source, role, timestamp with UTC offset, evidence class, provenance |
| Environment | Windows edition/build/architecture; active reference-machine profile or hosted environment; hardware environment; execution context; constraint profile; evidence source; relevant power/network state |
| Applicability | Declared capability, approved equipment class, preconditions, and any `Not available` reason code |
| Result | Exact `test_result`, concise observation, pass/fail criterion evaluated, and limitation |
| Artifact | Sanitized reference and SHA-256 when an artifact exists; otherwise explicit `none` |
| Continuity | Pre/post checkpoint, interruption/resume state, cold-boot correlation when applicable |
| Recovery | Cleanup performed, final safe state, unresolved recovery action |
| Traceability | Defect or capability/risk limitation ID, repair/retest link, and reviewer/date where applicable |

Completed evidence must be previewed for secrets and personal/device identifiers before it is committed. A raw command transcript, camera image, microphone recording, network identifier, or full serial is not acceptable merely because it was produced during a test.

## 10. Verification layers, individual invocation, and later triggers

The workflow contract is verified in layers. Deterministic application tests may prove state validation, persistence, pause/resume, report wording, and unavailable/failure handling; they do not produce a real-device `MHT-*` result. Human execution is added only by a later owning task and only for a capability safely available on the active Codex machine.

| Tier | What may be verified | Explicit trigger | Invocation contract | TL-0008 state |
|---|---|---|---|---|
| Quick | Document/schema/static checks; stable ID and allowed-state coverage | Revised `TL-0008` and normal iteration | Repository quick command; no `MHT-*` physical action | Definition checked; all scenarios remain `Not run` |
| Targeted | One deterministic state path or the changed manual-workflow subsystem | `TL-0114` implementation and later changes to the workflow/evidence/report contract | The owning task must publish an exact filterable command for each affected ID, such as a test case tagged `MHT-002`; run the smallest case first | Not run at `TL-0008` |
| Full | Complete implemented manual-test journey, output, persistence, accessibility baseline, and recovery | Named milestone/pilot/stable gate, including `TL-0608` or `TL-0707`, when their dependency chain is complete | Governed full-tier command plus manifest of included `MHT-*` cases | Not run at `TL-0008`; no full-tier trigger |
| Extended | Only the named slow, interruption, physical cold-boot, or resource-risk scenario | `MHT-019` at `TL-0509`, or a later task/gate whose `extended_test_triggers` names the risk | Independently invokable scenario command/procedure with checkpoint, abort, cleanup, and scenario-level result | Not run at `TL-0008`; no extended trigger |

Before an implementing task can execute a command, it must replace any command placeholder with a checked-in, reviewable, independently invokable command or human procedure. A later failure is rerun first at its single scenario ID, then at related targeted scope. Broad reruns occur only when the governing tier trigger applies or a shared cause is suspected.

The current definition register is intentionally empty of execution evidence:

| Scenario range | Product test result | Evidence class | Human reviewer | Physical observation | Certification claim |
|---|---|---|---|---|---|
| `MHT-001`–`MHT-021` | `Not run` | `Not available` | None | None | None |

There is no TL-0008 human walkthrough or sign-off requirement. A future real run records one bounded outcome per invoked test. It may leave an uninvoked test `Not run`, or record `Not available` when the capability, safe equipment, provider, or permission is unavailable. Missing hardware is an explicit limitation, not a development blocker.

A later human-confirmed pass proves only that the specified workflow and exact capability check completed on the recorded machine, build, environment, and date. It is not device certification, a long-term reliability guarantee, a broad hardware-support claim, or evidence for any untested hardware combination.
