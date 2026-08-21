# ThirdLife Setup Core — Capability and risk coverage matrix

**Status:** Active coverage specification; defined but not executed at revised `TL-0008`  
**Revision:** TL-0008 same-machine revision 2  
**Hardware scope:** Active Codex machine only  
**Inventory status:** Not a physical-device inventory; no device count or equipment requirement.

## 1. Purpose and claim boundary

This matrix replaces the former physical-device inventory with eleven canonical capability/risk groups. It preserves coverage of operating-system/CPU/architecture, memory, storage topology, battery/power, security/ownership evidence, manual peripherals, disk headroom, network behavior, accelerator/concurrency/resources, partial failure/retest, and sleep/restart/cold-boot continuity.

Each group maps variants to the smallest safe method:

- deterministic synthetic fixtures;
- bounded, provenance-reviewed, sanitized captured provider samples;
- safe reversible same-machine constraints;
- bounded read-only or human observation of a capability already available on the active Codex machine; or
- an explicit unverified limitation.

A method proves only application behavior for represented input and the recorded environment. It does not prove every provider, manufacturer, firmware, peripheral, storage medium, network, untested low-performance device, or future reliability. Missing hardware is not a blocker, cannot become a pass, and does not trigger another-machine work.

Revised `TL-0008` defines this matrix only. All groups are `Not run`; no fixture/sample/result is invented.

## 2. Coverage methods and evidence

| Method | Required contract | What it proves | What it does not prove |
|---|---|---|---|
| `FIXTURE` | Small deterministic project-created input; version, bounded size, expected result, SHA-256, privacy review, and single-case command supplied by owning task. | Behavior for represented values/state. | Windows integration or physical hardware. |
| `CAPTURE` | Sanitized/minimized active-machine provider sample; provider/version/provenance/hash; raw source discarded or permission-restricted per contract. | Parser/model behavior for that sample. | Every provider/device implementation. |
| `CONSTRAINT` | Safe reversible `SMC-*` settings, reference profile, workload hash, abort rule, cleanup, and result. | Same-machine behavior under recorded condition. | A particular CPU/RAM/disk/network/device class. |
| `OBSERVE` | Bounded read-only provider or human observation on the active machine at a later named trigger. | Exact recorded build/environment/date/capability. | Other hardware or long-term reliability. |
| `LIMIT` | Explicit unknown/not-available real-hardware/provider variant with fallback/report wording. | Honest scope boundary. | Any claim for the unavailable variant. |

Real evidence uses D-015 classes `Observed`, `Inferred`, `Not available`, or `Human confirmed`. “Observed by provider” is `Observed` with named-provider provenance, not a fifth class. Deterministic simulation is harness provenance and never stored/reported as real-device evidence.

Planned fixture/sample labels below are contract placeholders, not artifacts. Path, version, hash, expected result, and command remain **not implemented until the owning task**.

## 3. Privacy, safety, and active-machine scope

All later execution occurs directly on the active Codex machine or in a clean clone/worktree, disposable VM, Windows Sandbox session, container, isolated workspace, or virtual disk it hosts. No second computer, lower-performance device, hardware lab, volunteer pool, external hardware matrix, or authoritative remote runtime is required.

Fixtures/samples/evidence exclude serials/fragments, asset/service tags, hardware UUIDs, device/host names, usernames/SIDs, names/contacts, donor/recipient data, SSIDs, MAC/IP addresses, credentials, account/tenant data, product/recovery keys, personal paths, screenshots, photographs, audio/video, raw logs, dumps, and unreviewed archives.

Never change firmware, Secure Boot, TPM ownership/readiness, activation, ownership/management state, or Windows eligibility; fill the system volume; stress battery/storage; use unknown media; disrupt host network/storage without explicit approval/recovery; disable security/privacy/provenance/verification/accessibility/recovery; or add arbitrary execution inputs.

## 4. Canonical capability/risk matrix

| ID | Consolidated variants/risk | Required product behavior | Coverage mapping | Default tier and explicit earliest trigger | Evidence proves | Explicit limitation/current status |
|---|---|---|---|---|---|---|
| `CRM-001` | Supported/unsupported Windows/build/architecture; supported/unsupported/unknown CPU; malformed/missing facts; activation/management/ownership blocker states. | Preserve sourced facts/unknowns; policy blocks unsupported/controlled target without bypass; no aggregate score or false ownership conclusion. | `FIXTURE PLATFORM-SUPPORT-STATES` covering OS/architecture/CPU/activation/management values; optional sanitized `CAPTURE PLATFORM-ACTIVE`; bounded read-only `OBSERVE`. | Targeted at provider/policy work beginning `TL-0105`; Full at security/release gate. | Normalization/policy/report behavior for represented states and optional exact active-machine facts. | Does not cover every CPU/build/control or establish legal ownership; fixture/sample/hash not implemented at TL-0008; the named owning task must supply the item before invocation; `Not run`. |
| `CRM-002` | Memory values/units/bounds, malformed or missing data; large input and bounded resource pressure. | Explicit units/bounds; missing remains unavailable; stream/chunk with conservative concurrency; cancellation/checkpoint/recovery stay usable. | `FIXTURE MEMORY-VALUES`; optional `CAPTURE MEMORY-ACTIVE`; `SMC-LARGE-WORKLOAD` and resource-failure case when named. | Provider task targeted; `TL-0510` Extended only for resource risk; later release gate when triggered. | Model/parser behavior and later same-machine resource behavior for recorded workload/settings. | No performance/minimum-RAM claim; budgets/workload/hash/results not implemented at TL-0008; the named owning task must supply the item before invocation; `Not run`. |
| `CRM-003` | HDD/SATA SSD/NVMe/multiple disks; system-volume ambiguity; missing/invalid counters; slow destination/media. | Separate topology/media/system volume; validate bounds; unavailable stays unavailable; streaming/atomic output and accessible cancel/recovery. | `FIXTURE STORAGE-TOPOLOGIES`; sanitized `CAPTURE STORAGE-PROVIDER-SAMPLES`; read-only `OBSERVE`; `SMC-SLOW-DESTINATION` for named risk. | Provider/storage task Targeted; `TL-0510` or export/lifecycle task Extended when slow-write risk triggers. | Behavior for represented topologies/counters and later same-machine slow-destination settings. | No physical reliability, vendor, broad media-performance, or minimum-spec claim; artifacts not implemented at TL-0008; the named owning task must supply the item before invocation; `Not run`. |
| `CRM-004` | No battery; present/charging/degraded/invalid/denied/malformed battery evidence; AC indication, brief transition, unsafe physical condition. | Distinguish absent/unavailable/invalid/measured; stop for hazard; inventory presence does not prove function; no health/endurance inference. | `FIXTURE BATTERY-POWER-STATES`; sanitized provider sample when implemented; deterministic manual states for `MHT-001`, `MHT-013`, `MHT-014`; later safe bounded `OBSERVE` only if capability exists. | Provider/manual workflow Targeted beginning `TL-0105`/`TL-0114`; later explicit operator trigger. | Parser/state/workflow/report behavior and optional point-in-time active-machine indication/transition. | No swelling/capacity/endurance/lifetime proof; no destructive drain/stress; artifacts not implemented at TL-0008; the named owning task must supply the item before invocation; `Not run`. |
| `CRM-005` | TPM present/ready/absent/disabled/not-ready/unavailable/denied; Secure Boot enabled/disabled/legacy/unknown; activation/management evidence boundaries. | Preserve capability/state/uncertainty and distinct known-unsupported vs unknown; fail closed; never mutate/bypass controls. | `FIXTURE SECURITY-PLATFORM-STATES`; sanitized provider samples; safe read-only `OBSERVE` when implemented. | Provider/security task Targeted; Full at security/release gate. | Evidence/policy/report behavior for represented values. | Does not cover every firmware/TPM/control or prove absence; no mutation; artifacts not implemented at TL-0008; the named owning task must supply the item before invocation; `Not run`. |
| `CRM-006` | Keyboard/pointer/touch/pen; built-in/external display; audio/headphones/mic/camera; Ethernet/Wi-Fi/Bluetooth; USB-A/USB-C/dock/card/ports—present, absent, denied, unavailable, or functionally failing. | Provider presence stays separate from functional result; guide safe bounded check; persist `Pass`/`Fail`/`Not available`/`Not run`, pause/resume, privacy cleanup, and exact limitation. | Deterministic state/UI/report fixtures for `MHT-002`–`MHT-012` and `MHT-016`–`MHT-018`; sanitized provider samples; later bounded `OBSERVE` only for available active-machine capabilities/equipment. | Manual workflow `TL-0114` Targeted; later operator/accessibility gate Full only when named. | Workflow/evidence/report behavior; exact active-machine capability only if later observed. | Missing hardware/equipment remains limitation, not blocker; no capture retention or device certification; `Not run`. |
| `CRM-007` | Insufficient free space before work; bounded destination full after preflight; rollback headroom; export/partial output atomicity. | Preflight before mutation; unknown estimate not zero; preserve source/original; false completed/verified impossible; bounded cleanup/recovery. | Injected capacity fixture; `SMC-LOW-FREE-SPACE` in disposable virtual disk/workspace; `FI-003` single-case invocation. | Storage boundary `TL-0503` Targeted; Extended only for mid-write/export risk at `TL-0510`/`TL-0606`. | Preflight, atomicity, journal, cleanup, and recovery for represented conditions. | Never fill host system volume; not every physical full/slow disk; command/threshold/result not implemented at TL-0008; the named owning task must supply the item before invocation; `Not run`. |
| `CRM-008` | Wired/Wi-Fi/Bluetooth/offline; unavailable before operation; slow/interrupted/high-latency/filtered/captive path; metadata/trust change after retry. | Local core remains usable; network category/uncertainty visible; partial untrusted data bounded; cancel/retry safe; revalidate identity/source/publisher/version/digest. | Network fixtures/stubs; `SMC-OFFLINE`; `SMC-INTERRUPTED-NETWORK`; `FI-001`/`FI-002`; later bounded manual observation only if already available. | Network action Targeted beginning `TL-0405`; Extended only for interruption risk/gate. | Offline/recovery/trust semantics and represented network states. | Not every network/provider; no SSID/IP/credential evidence; host network not disrupted by default; `Not run`. |
| `CRM-009` | No GPU/optional acceleration; conservative concurrency; scheduling pressure; slow destination; large workload/resource exhaustion. | Essential CPU path; bounded workers/queues/memory/temp/cache/database/output; correct result; accessible progress/cancel/checkpoint/recovery. | Fallback/worker fixtures; `SMC-NO-GPU`, `SMC-CONSERVATIVE-CONCURRENCY`, `SMC-LOW-PRIORITY`, `SMC-SLOW-DESTINATION`, `SMC-LARGE-WORKLOAD`; `A11Y-009`. | Changed boundary Targeted; `TL-0510` or later release gate Extended only when named. | Correct fallback/bounds and later same-machine measurements for recorded workload/settings. | Does not simulate/certify low-end CPU/RAM/GPU/storage; commands/budgets/workloads/results not implemented at TL-0008; the named owning task must supply the item before invocation; `Not run`. |
| `CRM-010` | Partial hardware/provider/manual failure; unrelated passes; repair/retest; original evidence history. | Preserve exact failure/limitation/blocker; unrelated evidence remains; readiness honors essential failure; repair unauthorized here; retest separately attributable. | Deterministic `MHT-021` and report/journal fixtures covering pass/fail/unavailable, immutable original, and linked retest. | Manual workflow `TL-0114` Targeted; later operator/retest task. | State/journal/report/retest-link behavior. | Does not authorize/prove a real repair or long-term reliability; fixture/hash/command not implemented at TL-0008; the named owning task must supply the item before invocation; `Not run`. |
| `CRM-011` | Sleep/wake, expected restart, UI/process interruption, resume token, changed post-boot state, full powered-off cold boot, failed/unavailable recheck. | Durable checkpoint; correct job/environment; fresh observation before retry/continue; ambiguous/failed/unknown state blocks readiness; restart is not cold boot. | Deterministic `MHT-015`, `MHT-019`, `MHT-020`, `FI-004`, `FI-011`, `FI-012` fixtures; hosted restart; active-machine cold-boot procedure only at explicit trigger. | Resume `TL-0309` Targeted/Full; physical cold boot `TL-0509` Extended; later gate repeat only if named. | Continuity/recovery for fixtures/hosted restart and, when later run, exact active-machine cold-boot result. | No TL-0008 cold boot, other firmware/boot configuration, or cross-hardware continuity claim; `Not run`. |

## 5. Tier and trigger rules

- **Quick:** validate this matrix, exact ID order, links, schema/static rules, sanitization/claim wording. No observation/constraint at `TL-0008`.
- **Targeted:** smallest deterministic fixture/provider/manual-workflow case when owning subsystem changes; bounded read-only active-machine smoke only when task requires Windows integration.
- **Full:** complete applicable layers only at milestone/pilot/stable gates, major refactor/migration/dependency change, or explicit task trigger.
- **Extended:** one independently invokable `SMC-*`, `FI-*`, interruption/cold-boot/adversarial/endurance scenario only when named risk/task/gate triggers it.

Owning tasks replace placeholders with checked-in path/version/hash/expected result/command before execution. A failure reruns first at its single fixture/scenario, then related targeted scope; broader rerun only when triggered/shared cause suspected.

## 6. Evidence record

Every later execution records task/source/tier/command/duration, `CRM-*` row, reference-profile and hosted/`SMC-*` settings, fixture/sample/workload path/version/hash/provenance/privacy review, normalized observation and D-015 class/source, resource values where relevant, abort/cleanup/restoration/residue, outcome/defect/limitation, focused rerun, and exact claim boundary.

Evidence is bounded/sanitized. Do not attach raw command/provider/hardware logs or unrestricted local paths.

## 7. Current state

| Coverage range | Result | Fixture/sample artifacts | Active-machine observation | Certification |
|---|---|---|---|---|
| `CRM-001`–`CRM-011` | `Not run` | None; paths/versions/hashes/commands remain not implemented until owning tasks | None for `TL-0008` | None |

This matrix establishes a truthful route to coverage without a physical-device matrix. It does not establish minimum specifications, broad hardware compatibility, modest-hardware certification, or long-term reliability.
