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
