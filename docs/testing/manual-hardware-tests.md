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
