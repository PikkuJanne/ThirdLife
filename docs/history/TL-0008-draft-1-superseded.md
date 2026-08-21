# SUPERSEDED — DO NOT EXECUTE

> **HISTORICAL ARCHIVE ONLY. Do not use any instruction below as a current task, test plan, gate, evidence requirement, or release requirement.**

This record preserves the complete former `manual-hardware-tests.md` and `device-matrix.md` texts named by the TL-0008 transition for audit history. The archived text appears verbatim below the supersession record because it already existed in the repository; its imperative language is historical and inactive.

| Field | Historical value |
|---|---|
| Procedure | `TL-0008 draft 1` |
| Source commit | `4fa3ea050fd5e9985fde9cc8218281698d371cc8` |
| Procedure digest | `ef150dbf14b5db208582b7b526c7e0c6d0a5b912736e9e6519b8918abcf0928b` |
| Former paths | `docs/testing/manual-hardware-tests.md`; `docs/testing/device-matrix.md` |
| Former requirement | Build a physical device pool and immediately perform the `MHT-001`–`MHT-021` walkthrough |
| Superseded | 2026-08-15 |
| Replacement | `TL-0008_TRANSITION.md`, `TESTING.md`, `LOW_SPEC.md`, the revised `TASKS.yaml`, and the active documents under `docs/testing/` |
| Reason | Portfolio v2.1 makes the active Codex machine the only physical validation hardware and replaces lab/device-pool gating with tiered, risk-based, same-machine evidence and resource-conscious design. |

The historical procedure never becomes active by implication. Missing hardware is not a TL-0008 blocker, and this archive records no executed test, human confirmation, device certification, or release evidence.

---

## Archived former `docs/testing/manual-hardware-tests.md`

# ThirdLife Setup Core — Manual hardware-test procedure

**Status:** Draft procedure; human evidence pending  
**Procedure revision:** TL-0008 draft 1  
**Human walkthrough result:** Pending  
**Walkthrough owner and role:** Pending  
**Walkthrough date:** Pending  
**Reviewed source commit:** Pending  
**Reference device ID:** Pending  
**Walkthrough evidence reference:** Pending  
**Evidence digest:** Pending

## 1. Scope and limitation

This procedure gives a volunteer or staff refurbisher repeatable, human-assisted checks for a sanitized Windows device. It covers point-in-time function of input, display, audio/video, networking, power, ports, sleep/wake, interruption/resume, partial failure, and a full powered-off cold boot.

Required device classes, actual-pool inventory, reference-device selection, and equipment gaps are recorded separately in the [Physical-device matrix](device-matrix.md).

The procedure does not erase donor media, repair an existing personal computer, certify a device, diagnose every intermittent fault, or prove future service life. Automation and a single manual run cannot prove long-term hardware reliability. A result describes only the exact device, environment, procedure revision, time, and evidence recorded.

The normal target is supported Windows 11 x64. An unsupported operating system, processor, TPM state, Secure Boot state, activation state, ownership control, or management state must remain visible and may block normal readiness. This procedure never authorizes a bypass.

## 2. Preconditions and safety stops

Before starting, the operator must:

1. use an externally sanitized device or an approved non-personal lab device; ThirdLife Setup Core does not perform donor-media erasure;
2. assign a sanitized lab device ID and avoid serial numbers, donor/recipient information, usernames, personal paths, SSIDs, IP addresses, credentials, recovery material, or organization secrets;
3. record the procedure revision, tested source commit, Windows edition/build, architecture, power state, and approved test equipment;
4. confirm that every test peripheral and medium is known, organization-approved, and free of personal content;
5. create the first durable checklist checkpoint before changing power state or attaching equipment; and
6. stop if ownership, sanitization, electrical safety, or the intended test environment is uncertain.

Immediately stop and disconnect power when safe for battery swelling, smoke, unusual heat, liquid damage, exposed conductors, sparking, a damaged charger, a burning smell, or another physical hazard. Record `Fail` when the applicable inspection exposes the condition; record `Not available` with `unsafe_to_run` when the inspection cannot safely be completed. Do not open the enclosure, stress a suspect battery or storage device, flash firmware, change Secure Boot, clear a TPM, bypass Windows requirements, defeat an ownership/management control, or continue merely to complete the checklist.

## 3. Result and evidence semantics

Every result has two orthogonal fields:

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
- Missing, conflicting, or stale evidence remains unknown. Record the limitation and do not select `Pass`.

An interrupted run retains completed records and the last durable checkpoint. On resume, the operator re-identifies the device and environment, reviews prior results, repeats any test whose state may have changed, and never silently converts an incomplete result to a pass.

`Not run` is the planning/checklist label in these TL-0008 documents. When the future `TL-0114` implementation persists a manual-test result, it maps exactly to that task's `not tested` value; `Pass`, `Fail`, and `Not available` map without a semantic change. No persisted result may invent a fifth outcome or turn this mapping into a pass.

Every run separately records hardware environment (`Physical` or `Virtual`), execution context (`Interactive lab` or `CI`), constraint profile (`None` or a named constraint), and evidence source (direct physical observation, named provider, or `Synthetic`). A constrained VM remains virtual evidence and cannot satisfy a physical-device requirement.

## 4. Approved equipment and privacy

Use the minimum approved equipment needed for an applicable test, for example a known keyboard or pointer, approved display/cable, known headphones, an approved non-sensitive test network, and organization-controlled test peripherals. Do not use unknown removable media or write to a donor device merely to test a port.

- Camera and microphone tests use an ephemeral preview or loopback. Do not retain recordings or images.
- Audio tests use a non-sensitive local test sound at a safe volume.
- Network tests confirm bounded connectivity only. Do not access personal services, enter personal credentials, or record an SSID, IP address, proxy credential, or captive-portal content.
- Bluetooth pairings and temporary network state created for testing are removed when the approved procedure calls for cleanup and removal is safe.
- Do not place screenshots, photographs, audio, video, raw device captures, or unreviewed attachments in repository evidence. Use bounded, normalized textual observations or structured records that exclude serials, names, personal content, notification text, paths, and network identifiers.

## 5. Operator workflow

1. **Start:** identify the sanitized device and run, inspect safety, record environment and applicability, and create a checkpoint.
2. **Test:** run only applicable `MHT-*` procedures. Record each result immediately; do not hold an unbounded set of notes for later transcription.
3. **Pause:** save the current test ID, completion state, attached equipment, power/network state, and required safe cleanup.
4. **Resume:** re-identify device/run, inspect for changed safety or environment, reconcile completed records, and restart only the interrupted test.
5. **Cold boot:** after applicable functional checks, perform `MHT-019` and repeat its selected post-boot checks as a distinct verification phase.
6. **Complete:** link defects and pilot blockers, perform cleanup, preview the sanitized record, and leave every deferred or unavailable test explicit.

An essential failure or unknown evidence is not overridden by progress through the checklist. Repair or retest creates a linked later record and preserves the original result.

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
| `MHT-019` | Full powered-off cold boot and recheck | Required physical reference device; no VM substitute | Follow the dedicated cold-boot procedure in section 7 and repeat selected safety, display, input, network, and evidence checks | Observable full power-off, later physical power-on, fresh boot correlation, and all required rechecks recorded truthfully | Boot fails, state is ambiguous, essential recheck fails, or prior state is silently reused | `provider_unavailable`, `permission_denied`, or `unsafe_to_run`; ambiguous shutdown cannot pass | Durable checkpoint before shutdown; correlate pre/post runs; preserve attempted/applied/verified distinctions |
| `MHT-020` | Pause, close, reopen, and resume the manual workflow | Every representative walkthrough; current durable record surface | Checkpoint after a completed test, pause/close the record, reopen it, reconcile identity/state, and resume at the next safe test | Completed results persist, no test is duplicated/skipped, and interrupted state remains explicit | Lost/rewritten result, wrong-device resume, duplicate action, or incomplete result becomes pass | `provider_unavailable`, `permission_denied`, or `unsafe_to_run` | Use synthetic/non-personal record; preserve original checkpoint and cleanup before pause |
| `MHT-021` | Partial failure, repair/retest linkage, and blocker preservation | Any detected or pre-existing partial failure | Record original result, affected capability, impact and blocker/defect; after authorized repair, create a separate linked retest | Original failure remains immutable and later retest is separately attributable | Original evidence is overwritten, partial failure is hidden by other passes, or readiness ignores blocker | `capability_absent`, `provider_unavailable`, `permission_denied`, or `unsafe_to_run` | No repair is authorized by this document; retain links, limitation, recovery, and final cleanup state |

## 7. Cold-boot procedure

`MHT-019` is a separate verification phase, not another name for restart.

1. Confirm `MHT-001` still permits safe operation and the device is on approved power.
2. Save a durable pre-boot checkpoint containing device ID, run ID, procedure/source revisions, timestamp with offset, completed results, known failures, and selected post-boot rechecks.
3. Use the supported Windows power interface to request full shutdown. Do not alter firmware or weaken Fast Startup/security policy merely to force a result.
4. Observe full powered-off state: display off and other normal device-specific power indicators inactive. If full power-off is ambiguous, record `Not available`; a process restart or ambiguous hybrid shutdown cannot pass.
5. Wait only the bounded interval stated in the run record, then use the physical power control to start the device.
6. Record a fresh boot/session correlation and timestamp. Do not copy pre-boot provider values as post-boot evidence.
7. Recheck physical safety, built-in display, primary keyboard/pointer, applicable network state, and every earlier result identified as restart-sensitive.
8. Record each recheck separately and distinguish attempted, applied, and verified outcomes. A failed or unavailable essential recheck remains visible.
9. Complete cleanup and link any defect or pilot blocker. Do not claim cold-boot verification if any required correlation is missing.

The human walkthrough for `TL-0008` must state which cold-boot steps were physically executed and which were only reviewed or unavailable. A planned scenario is not cold-boot evidence.

## 8. Partial failures and blockers

A partial failure does not invalidate unrelated observations, but it cannot be hidden by them. Record:

- the exact affected capability and original `MHT-*` result;
- whether the condition is a safety stop, essential-function blocker, non-essential limitation, equipment gap, or unknown;
- user/workshop impact in plain language;
- defect and `GAP-DMX-*` identifiers where applicable;
- any safe manual alternative without calling it equivalent proof;
- authorized repair/recovery owner and state, if one exists; and
- a separately timestamped retest rather than overwriting history.

When required equipment is missing, use `Not available` with `equipment_missing` and create or reference an explicit pilot blocker. When no safe path exists, stop; do not improvise with unknown equipment.

## 9. Result-record template

Create one bounded record per test result. The following fields are required; `Pending` is allowed only while this draft has not been executed, never in completed evidence:

| Field | Required content |
|---|---|
| Record identity | Record ID, run ID, sanitized device ID, test ID, procedure revision, tested source commit |
| Attribution | Operator or source, role, timestamp with UTC offset, evidence class, provenance |
| Environment | Windows edition/build/architecture; hardware environment; execution context; constraint profile; evidence source; relevant power/network state |
| Applicability | Declared capability, approved equipment class, preconditions, and any `Not available` reason code |
| Result | Exact `test_result`, concise observation, pass/fail criterion evaluated, and limitation |
| Artifact | Sanitized reference and SHA-256 when an artifact exists; otherwise explicit `none` |
| Continuity | Pre/post checkpoint, interruption/resume state, cold-boot correlation when applicable |
| Recovery | Cleanup performed, final safe state, unresolved recovery action |
| Traceability | Defect ID, pilot-blocker ID, repair/retest link, and reviewer/date where applicable |

Completed evidence must be previewed for secrets and personal/device identifiers before it is committed. A raw command transcript, camera image, microphone recording, network identifier, or full serial is not acceptable merely because it was produced during a test.

## 10. Human walkthrough and sign-off

The actual representative-device walkthrough is **Pending**. The placeholder below is not evidence and must not be changed to `Pass` without a real human run on the exact referenced procedure and source commit.

| Field | Current value |
|---|---|
| Walkthrough result | Pending |
| Human recorder and role | Pending |
| Date and timestamp with offset | Pending |
| Procedure revision | `TL-0008 draft 1` |
| Reviewed source commit | Pending |
| Physical reference device ID | Pending |
| Windows edition/build/architecture | Pending |
| Tests physically executed | Pending |
| Tests reviewed only or not run | Pending |
| Cold-boot result and evidence class | Pending |
| Interruption/resume result and evidence class | Pending |
| Missing equipment and pilot blockers | Pending |
| Limitations and defects | Pending |
| Sanitized evidence reference/hash | Pending |
| Cleanup/recovery result | Pending |

Record one bounded outcome for every manual test. The sign-off table supplies the common operator, procedure revision, source commit, device ID, and Windows environment; each row below supplies its own record/run identity, offset timestamp, provenance, observation, artifact decision, continuity, cleanup, and traceability. A row may be `Not available` when the capability, safe equipment, provider, or permission is unavailable, but no row may remain `Not run` when the representative walkthrough is signed off. `MHT-019` and `MHT-020` must be `Pass` with attributable `Human confirmed` evidence. A `Fail` row must name a defect or pilot blocker in its limitation field; it is not hidden by the overall walkthrough result.

| Test ID | Record/run ID | Test result | Evidence class | Hardware/context/source | Timestamp with offset | Observation and criterion | Artifact reference/hash or none | Continuity/checkpoint | Cleanup/recovery | Defect, blocker, or limitation |
|---|---|---|---|---|---|---|---|---|---|---|
| `MHT-001` | Pending | `Not run` | `Not available` | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| `MHT-002` | Pending | `Not run` | `Not available` | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| `MHT-003` | Pending | `Not run` | `Not available` | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| `MHT-004` | Pending | `Not run` | `Not available` | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| `MHT-005` | Pending | `Not run` | `Not available` | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| `MHT-006` | Pending | `Not run` | `Not available` | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| `MHT-007` | Pending | `Not run` | `Not available` | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| `MHT-008` | Pending | `Not run` | `Not available` | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| `MHT-009` | Pending | `Not run` | `Not available` | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| `MHT-010` | Pending | `Not run` | `Not available` | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| `MHT-011` | Pending | `Not run` | `Not available` | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| `MHT-012` | Pending | `Not run` | `Not available` | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| `MHT-013` | Pending | `Not run` | `Not available` | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| `MHT-014` | Pending | `Not run` | `Not available` | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| `MHT-015` | Pending | `Not run` | `Not available` | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| `MHT-016` | Pending | `Not run` | `Not available` | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| `MHT-017` | Pending | `Not run` | `Not available` | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| `MHT-018` | Pending | `Not run` | `Not available` | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| `MHT-019` | Pending | `Not run` | `Not available` | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| `MHT-020` | Pending | `Not run` | `Not available` | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| `MHT-021` | Pending | `Not run` | `Not available` | Pending | Pending | Pending | Pending | Pending | Pending | Pending |

This draft records no actual device pool, physical run, cold boot, workshop observation, accessibility review, or reliability evidence. `TL-0008` must remain short of `done` until a human records the real pool, confirms an available reference device, completes the representative walkthrough, records equipment gaps as explicit pilot blockers, and repository verification passes.

---

## Archived former `docs/testing/device-matrix.md`

# ThirdLife Setup Core — Physical-device matrix

**Status:** Draft procedure; human evidence pending  
**Procedure revision:** TL-0008 draft 1  
**Actual device-pool record:** Pending  
**Physical reference device:** Pending  
**Human recorder and role:** Pending  
**Walkthrough result:** Pending  
**Walkthrough date:** Pending  
**Reviewed source commit:** Pending  
**Procedure digest:** Pending
**Evidence digest:** Pending

## 1. Purpose and authority

This document defines the physical and constrained-device classes needed to test ThirdLife Setup Core. It is a derived test plan for `TL-0008`; it does not amend `DECISIONS.md`, `ROADMAP.md`, `PROJECT_BOUNDARY.md`, `SECURITY.md`, `ACCESSIBILITY.md`, or `LOW_SPEC.md`.

Physical functional checks use the stable test IDs and result rules in [Manual hardware-test procedure](manual-hardware-tests.md). Planned coverage, actual pool inventory, and completed test evidence remain separate.

The primary target is supported Windows 11 x64. Windows 10, an unsupported processor, unavailable TPM evidence, or unavailable Secure Boot evidence may be useful negative or audit-only cases, but none may be represented as a supported normal-ready device. Tests must observe these states without changing firmware configuration, clearing a TPM, weakening Secure Boot, bypassing processor or operating-system requirements, or circumventing an ownership or management control.

The matrix is a coverage plan, not a minimum specification or support promise. A 4 GB or 8 GB device is a test class. Automation, a constrained environment, and a single physical run provide point-in-time evidence; they cannot prove long-term hardware reliability, certify a device, or establish a broad minimum specification.

## 2. Evidence environments and result semantics

Every record keeps four evidence dimensions separate:

- **hardware environment:** `Physical` or `Virtual`;
- **execution context:** `Interactive lab` or `CI`;
- **constraint profile:** `None` or a named imposed CPU, memory, storage, priority, network, acceleration, or power constraint; and
- **evidence source:** `Direct physical observation`, `Named provider`, or `Synthetic` input.

These dimensions are orthogonal. A constrained VM remains `Virtual`; a CI run does not imply virtual or physical hardware; and synthetic/provider evidence never proves that a physical function worked.

Every coverage result records two independent fields:

| Field | Allowed values | Meaning |
|---|---|---|
| `test_result` | `Pass`, `Fail`, `Not available`, `Not run` | Outcome of the exact procedure. |
| `evidence_class` | `Observed`, `Inferred`, `Not available`, `Human confirmed` | Provenance class required by D-015. |

`Human confirmed` is an evidence class, not a result, and may support either `Pass` or `Fail`. `Not available` never passes and requires one of `capability_absent`, `equipment_missing`, `provider_unavailable`, `unsafe_to_run`, or `permission_denied`, plus an explanation. `Not run` means the procedure was deferred, interrupted, or not attempted. Automated presence detection cannot pass a functional hardware test. Missing or conflicting information remains unknown and cannot be converted to a pass.

Each record also names the procedure revision, source revision, device/run ID, operator or source, timestamp with offset, Windows environment, provenance, sanitized artifact reference and hash where applicable, limitations, cleanup or recovery result, and linked defect or blocker IDs.

## 3. Privacy and physical-safety rules

- Use human-assigned lab IDs such as `LAB-DEVICE-001`; do not record full serial numbers, asset tags that identify a donor, SSIDs, IP addresses, usernames, email addresses, personal paths, donor or recipient data, ownership secrets, credentials, or recovery material.
- Use only externally sanitized devices or approved non-personal lab devices. ThirdLife Setup Core does not erase donor media.
- Store only bounded, normalized textual observations or structured records. Repository evidence must not include screenshots, photographs, audio, video, raw device captures, or unreviewed attachments.
- Stop for battery swelling, smoke, unusual heat, liquid damage, exposed conductors, sparking, a damaged charger, or another physical hazard. Record `Not available` with `unsafe_to_run`; do not continue to obtain coverage.
- Do not open an enclosure, perform destructive stress, flash firmware, change firmware security state, clear a TPM, bypass compatibility or ownership controls, or run an unknown executable or removable medium for this matrix.
- Recipient-specific accessibility choices remain under a present recipient or authorized organization's control. A sealed handover records them as pending rather than applying assumptions.

## 4. Required coverage matrix

Coverage may be distributed across several devices. A single device is not expected to represent every class. `Coverage availability` is `Covered`, `Missing`, or `Pending`; it is not a test result. `Covered` requires an actual device/run reference. `Missing` requires a unique `GAP-DMX-*` pilot blocker. Every row must leave `Pending` before TL-0008 is complete, while the separate run table records `Pass`, `Fail`, `Not available`, or `Not run` outcomes.

| Requirement ID | Dimension | Required class or state | Admissible environment | Claim or safety rule | Device/run refs | Coverage availability | Gap ID |
|---|---|---|---|---|---|---|---|
| `DMX-001` | Primary reference | Supported Windows 11 x64 physical device | Physical only | Required human-confirmed reference; no unsupported-state substitution | Pending | Pending | Pending |
| `DMX-002` | Processor | Unsupported-CPU negative or audit case | Physical or VM with truthful limitation | Audit/disposition only; never a modifying or normal-ready path | Pending | Pending | Pending |
| `DMX-003` | OS/architecture | Non-target OS or architecture | Physical or VM | Record audit-only or unsupported classification; never supported | Pending | Pending | Pending |
| `DMX-004` | Form factor | Laptop | Physical | Record actual power and battery applicability | Pending | Pending | Pending |
| `DMX-005` | Form factor | Desktop | Physical | Record battery as not applicable, not provider failure | Pending | Pending | Pending |
| `DMX-006` | Form factor | Other or unusual form factor when available | Physical | Preserve exact limitation; do not generalize | Pending | Pending | Pending |
| `DMX-007` | Memory | 4 GB RAM class | Physical, VM, or constrained with label | Test class only; not a minimum-spec claim | Pending | Pending | Pending |
| `DMX-008` | Memory | 8 GB RAM class | Physical, VM, or constrained with label | Test class only; not a minimum-spec claim | Pending | Pending | Pending |
| `DMX-009` | Storage | SATA HDD | Physical | Point-in-time functional evidence is not a reliability guarantee | Pending | Pending | Pending |
| `DMX-010` | Storage | SATA SSD | Physical | Point-in-time functional evidence is not a reliability guarantee | Pending | Pending | Pending |
| `DMX-011` | Storage | NVMe | Physical | Point-in-time functional evidence is not a reliability guarantee | Pending | Pending | Pending |
| `DMX-012` | Power | No battery or desktop | Physical | Distinguish not applicable from provider unavailable | Pending | Pending | Pending |
| `DMX-013` | Power | Battery present on AC and charging indication | Physical | Immediate indication only; no capacity or longevity claim | Pending | Pending | Pending |
| `DMX-014` | Power | Brief operation on battery | Physical | Stop safely before critical power; no endurance claim | Pending | Pending | Pending |
| `DMX-015` | Power | Low, weak, or not-charging battery | Physical | Record partial failure and recovery path; do not stress | Pending | Pending | Pending |
| `DMX-016` | Power evidence | Battery provider unavailable | Physical or synthetic supplement | Distinct from no battery and never passed implicitly | Pending | Pending | Pending |
| `DMX-017` | TPM | Present and ready | Physical or VM with environment label | Observe only; no TPM mutation | Pending | Pending | Pending |
| `DMX-018` | TPM | Absent, disabled, or not ready | Physical or VM | Record exact known state; no bypass or firmware change | Pending | Pending | Pending |
| `DMX-019` | TPM evidence | State unavailable or access denied | Physical or synthetic supplement | Unknown remains unknown; no pass | Pending | Pending | Pending |
| `DMX-020` | Secure Boot | Enabled | Physical or VM with environment label | Observe supported evidence; do not change firmware | Pending | Pending | Pending |
| `DMX-021` | Secure Boot | Capable but disabled | Physical or VM | Negative/audit state only; do not enable for this test | Pending | Pending | Pending |
| `DMX-022` | Secure Boot | Legacy, unsupported, or unavailable | Physical or VM | Preserve the distinction between known unsupported and unknown | Pending | Pending | Pending |
| `DMX-023` | Network | Wired network | Physical | Use approved non-sensitive test infrastructure; do not record IPs | Pending | Pending | Pending |
| `DMX-024` | Network | Wi-Fi | Physical | Use an approved non-sensitive test network; do not record its SSID | Pending | Pending | Pending |
| `DMX-025` | Network | Offline | Physical, VM, or constrained | Local workflow must explain unavailable network-dependent evidence | Pending | Pending | Pending |
| `DMX-026` | Network | Slow or high-latency | Constrained or approved physical network | Record imposed conditions and limitations | Pending | Pending | Pending |
| `DMX-027` | Network | Intermittent or network loss | Constrained, VM, or approved physical network | Require truthful interruption and recovery evidence | Pending | Pending | Pending |
| `DMX-028` | Network | Filtered, proxy, or captive network | Approved lab network or synthetic supplement | Do not use personal or partner credentials | Pending | Pending | Pending |
| `DMX-029` | Partial failure | Unusual or partially failed hardware | Physical | Record the failed capability explicitly; never convert partial success to full pass | Pending | Pending | Pending |
| `DMX-030` | Storage capacity | Low-space condition | Bounded disposable volume, VM, or constrained environment | Never fill a physical system drive; fail before unsafe mutation | Pending | Pending | Pending |
| `DMX-031` | Restart behavior | Physical cold boot and post-boot recheck | Physical only | Full power-off then physical power-on; restart is not a substitute | Pending | Pending | Pending |
| `DMX-032` | Graphics | No-GPU or hardware-acceleration-disabled CPU fallback | Physical, VM, or constrained with label | Accessibility and safety checks remain enabled | Pending | Pending | Pending |
| `DMX-033` | Supplement | Constrained VM or process run | VM or constrained | Supplement only; explicitly not physical-device proof | Pending | Pending | Pending |

## 5. Actual device pool

No actual device-pool evidence has been supplied. The row below is an explicit pending placeholder, not a device record and not acceptance evidence. A human recorder must replace it with sanitized facts gathered from the real pool.

| Device ID | Availability | Reference device | Form factor | Windows edition/build/support | Architecture | CPU support state | RAM class | System storage | Battery state | TPM state | Secure Boot state | Network capabilities | Known partial failure | Matrix roles | Owner/date | Limitations |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Pending — no pool entry recorded | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Human evidence required |

For each real row, `Availability` is `Available`, `Planned`, or `Missing`. `Reference device` is `Yes` or `No`; only the selection rules below permit `Yes`. The owner/date field identifies the human recorder and role without adding an email address or other unnecessary personal information.

## 6. Device-to-requirement coverage

No coverage result is recorded yet. Add one row for every claimed device/requirement pairing; do not infer coverage merely from the pool inventory.

| Device ID | Requirement ID | Actual state | Hardware environment | Execution context | Constraint profile | Evidence source | Evidence reference | Test result | Evidence class | Limitation |
|---|---|---|---|---|---|---|---|---|---|---|
| Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | `Not run` | `Not available` | Human pool record and test run pending |

## 7. Reference-device selection

At least one reference device is required before `TL-0008` can be complete. A human must confirm that it:

1. is a real, available physical device identified only by a sanitized lab ID;
2. runs supported Windows 11 x64 on a supported processor;
3. has known TPM and Secure Boot evidence recorded truthfully, without a bypass;
4. has no unresolved physical-safety stop condition;
5. is available for the manual procedure, a full powered-off cold boot, and post-boot rechecks;
6. has its form factor, memory, storage, power, network, and known partial failures recorded; and
7. is linked to an attributable human pool record and walkthrough record for the exact procedure revision and source commit.

A Windows 10 device, unsupported-CPU device, VM, synthetic fixture, constrained process, or machine with an unresolved safety stop may cover a negative or supplemental class but cannot be the required reference device.

The canonical human-evidence block at the top of this document remains `Pending` until the physical reference device and walkthrough are actually recorded.

## 8. Coverage gaps and pilot blockers

The actual equipment gap review is pending. Once the human pool is recorded, each unavailable required class must receive a stable `GAP-DMX-*` identifier. A gap may remain an explicit pilot blocker without being misrepresented as tested. If the human review finds no gap, it must record that conclusion explicitly rather than leaving this table pending.

| Gap ID | Missing requirement | Reason | Pilot impact | Acquisition or safe alternative plan | Owner | Review date | Status |
|---|---|---|---|---|---|---|---|
| Pending | Actual pool not yet recorded | Human inventory pending | Pilot coverage cannot yet be assessed | Record the sanitized pool and map every `DMX-*` requirement | Pending | Pending | Pending |

VM, constrained, or synthetic evidence may be listed as a limitation-aware supplement, but it does not close a physical-equipment gap. If no supported Windows 11 x64 reference device exists, record that concrete blocker; do not substitute a VM or fabricate availability.

## 9. Run and evidence linkage

Every completed run record must include:

- record ID, device ID, requirement ID, and associated manual-test IDs;
- this procedure revision and the tested source commit;
- test result and independent evidence class using the exact vocabulary above;
- operator or automated source, timestamp with offset, Windows edition/build, and environment kind;
- bounded observation, sanitized artifact reference and SHA-256 when an artifact exists;
- limitation, `Not available` reason code where applicable, and any defect or `GAP-DMX-*` blocker;
- pause/resume checkpoint, cleanup or recovery result, and post-cold-boot correlation where applicable.

The repository record must state exactly what was observed, inferred, unavailable, or human confirmed. It must never turn a planned row, hardware-presence signal, or missing observation into physical proof.

The validator computes the procedure SHA-256 over UTF-8/LF text from the normative sections in this exact order: this document sections 1–4, 7, and 9; `manual-hardware-tests.md` sections 1–9; `failure-injection.md` sections 1–8; `accessibility-matrix.md` sections 1–10; and `LOW_SPEC.md` sections 1–12 with its TL-0008 procedure-status line omitted. Before section 4 is hashed, the final three cells of every `DMX-*` row (`Device/run refs`, `Coverage availability`, and `Gap ID`) are each replaced by the literal `<evidence-excluded>`; their recorded values belong to the evidence digest instead. Each part is encoded as an 8-byte unsigned big-endian path length, repository-relative UTF-8 path bytes, an 8-byte unsigned big-endian content length, and UTF-8 content bytes. Actual-pool rows, coverage results, gap decisions, walkthrough results, and approval/status metadata are deliberately excluded. The top-level `Procedure digest` records the lowercase 64-hex result for the reviewed source commit.

The separate evidence SHA-256 uses the same length-delimited encoding over this document's sections 4, 5, 6, 8, and 10 followed by `manual-hardware-tests.md` section 10. It binds the recorded `DMX-*` coverage cells, sanitized pool, device coverage, gap decisions, per-test walkthrough results, and sign-off without creating a self-reference; the top-level `Evidence digest` field itself is outside those sections. The exact digest must be included in the durable `TL-0008` human-evidence reference.

## 10. Human review state

The actual device pool, reference-device availability, equipment gaps, and representative-device walkthrough are all **Pending**. This draft supplies no physical-device, cold-boot, workshop, accessibility, or long-term reliability evidence. `TL-0008` must remain short of `done` until the required human records exist and the repository verification and documented human walkthrough have both passed.
