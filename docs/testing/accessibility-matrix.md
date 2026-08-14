# ThirdLife Setup Core — Accessibility Test Matrix

**Status:** Draft procedure; human evidence pending  
**Procedure revision:** TL-0008 draft 1  
**Task:** `TL-0008`  
**Authority:** D-003, D-039, D-057, `ACCESSIBILITY.md`, `LOW_SPEC.md`, `ROADMAP.md`, and `AGENTS.md`  
**Execution state:** No keyboard, Narrator, NVDA, scaling, contrast, recipient, output, or human accessibility result is recorded by this document.

## 1. Purpose and claim boundary

This matrix defines the repeatable accessibility coverage required for the workshop-operator journey and the later recipient-controlled accessibility setup. It is a test plan, not an accessibility-conformance claim and not evidence that unimplemented workflows have passed.

The primary operator journey is **Intake → Inspect → Decide → Prepare → Verify → Handover**. Tests cover keyboard access, focus, UI Automation semantics, assistive-technology output, scaling, contrast, reduced resolution, long text, progress, cancellation, UAC return, interruption/recovery, reports, support preview, and the later recipient-present and sealed-handover paths.

Automated UI checks may find defects but cannot replace human Narrator, NVDA, visual, cognitive, keyboard, or recipient/proxy review. A VM cannot prove physical input, display, audio, or low-spec behavior. No result in this draft may be read as a release approval.

## 2. Result, evidence, and availability semantics

`test_result` and `evidence_class` are independent.

| Field | Exact value | Meaning |
|---|---|---|
| `test_result` | `Pass` | A complete applicable procedure was performed and every stated accessibility expectation was met with required evidence. |
| `test_result` | `Fail` | A required interaction, meaning, announcement, focus behavior, recovery path, output, or low-resource accessibility condition did not meet the expectation. |
| `test_result` | `Not available` | Required assistive technology, hardware, capability, safe environment, or evidence source was unavailable. It is not a pass. |
| `test_result` | `Not run` | The test was not started or completed, including because its owning workflow is not implemented. It is not a pass. |
| `evidence_class` | `Observed` | Bounded UI Automation, application, Windows, generated-output, or artifact evidence was captured with provenance. |
| `evidence_class` | `Inferred` | A conclusion was derived from named observations with limitations. Inference alone cannot satisfy required human assistive-technology review. |
| `evidence_class` | `Not available` | Required evidence could not be collected. Missing evidence remains unknown. |
| `evidence_class` | `Human confirmed` | A named human actually performed or observed the interaction and recorded assistive technology/provider, timestamp, and provenance. Codex cannot create this evidence. |

Allowed `Not available` reasons include `assistive_technology_missing`, `capability_absent`, `equipment_missing`, `environment_unavailable`, `provider_unavailable`, and `unsafe_to_run`. `Not run` requires a reason such as `future_control_not_implemented`, `awaiting_approved_build`, `blocked_by_prerequisite`, `run_interrupted`, or `human_review_pending`.

A `Pass` cannot be supported only by `Not available` evidence. An automated scan cannot be relabelled `Human confirmed`. Absence of a mouse, screen reader, display configuration, or test language is a coverage gap, not proof that the corresponding path is accessible.

Every result separately records hardware environment (`Physical` or `Virtual`), execution context (`Interactive lab` or `CI`), constraint profile (`None` or a named constraint), and evidence source (human interaction, named UI Automation/provider observation, or `Synthetic`). A constrained VM remains virtual evidence; automation and synthetic input cannot become human assistive-technology evidence.

## 3. Test data, privacy, and safety

Use only synthetic jobs, policies, profiles, device labels, errors, long strings, reports, and support fields. Accessibility evidence must not contain names, contacts, serials or serial fragments, service or asset tags, hardware UUIDs, hostnames, usernames or SIDs, MAC/IP/SSID values, tenant/account data, product or recovery keys, personal paths, screenshots, raw logs, dumps, archives, or exact workshop locations.

Repository evidence identifies a device only through an opaque local reference such as `LAB-DEVICE-001`; the identifier must not encode or hash a hardware identifier. Artifact references are repository-relative or approved durable evidence identifiers, not unrestricted local paths.

The test operator must not weaken a safety, privacy, support, ownership, or verification gate to make an accessibility path complete. Recipient-specific choices are exercised only by a present recipient or authorized organization in the later owning task. Sealed handover applies no recipient-specific preference.

## 4. Environment profiles

Availability and results for every profile remain pending until an actual run record exists.

| ID | Planned environment | Required record | Current availability | Current result |
|---|---|---|---|---|
| `AXE-001` | Supported Windows 11 x64 physical reference device; keyboard only; pointer set aside. | Windows/build, device ID, input devices, application build, operator, date. | Pending human inventory | `Not run` |
| `AXE-002` | Windows Narrator on supported Windows 11 x64. | Narrator and Windows versions, voice/output route, device ID, human reviewer. | Pending human inventory | `Not run` |
| `AXE-003` | NVDA on supported Windows 11 x64. | Exact NVDA version/source, Windows/build, device ID, human reviewer. | Pending human inventory | `Not run` |
| `AXE-004` | 200% display scaling and the approved large-text setting. | Display resolution, scaling, text size, monitor/form factor, device ID. | Pending human inventory | `Not run` |
| `AXE-005` | Windows high-contrast themes and non-color status review. | Theme name/state, contrast mode, device ID, human reviewer. | Pending human inventory | `Not run` |
| `AXE-006` | Reduced-resolution display. | Exact resolution, scaling, form factor, device ID, clipping/scroll observations. | Pending human inventory | `Not run` |
| `AXE-007` | Long and synthetic pseudolocalized strings. | Fixture ID/version/SHA-256, language/locale setting, application build. | Fixture and run pending | `Not run` |
| `AXE-008` | 4 GB or 8 GB test class, no required GPU/hardware acceleration, and an approved assistive technology. | Device/constraint IDs, memory, storage, acceleration state, AT/version, resource record. | Pending human inventory | `Not run` |

Four- and eight-gigabyte configurations, reduced resolution, VMs, and constrained processes are test classes. They are not minimum-spec, compatibility, or accessibility-support claims. Missing NVDA or a physical device is recorded as `Not available` and a coverage blocker at the applicable gate; it is never silently omitted.

## 5. Result record

Every executed accessibility case records:

| Field | Requirement |
|---|---|
| Identity | Result schema version, procedure revision, run ID, case ID, and owning task. |
| Build | ThirdLife version, 40-hex source revision, configuration, Windows edition/build, and x64 architecture. |
| Environment | `AXE-*` profile, opaque device/VM ID, hardware environment, execution context, constraint profile, evidence source, form factor, and relevant `DMX-*` requirements. |
| Assistive technology | Product, exact version/source, settings relevant to the result, and audio/display route without personal device names. |
| Display/input | Resolution, scaling, text size, contrast state, input mode, and hardware-acceleration state. |
| Fixture | Synthetic fixture ID, version, SHA-256 digest, locale, and privacy review state. |
| Actor/time | Provider or human reviewer role, start/end timestamps with offset, and provenance. |
| Procedure | Preconditions, ordered steps, expected names/roles/states/focus/announcement/output, and recovery path. |
| Outcome | Exact `test_result`, exact `evidence_class`, bounded observations, and result codes. |
| Evidence | Repository-relative or approved durable artifact references and SHA-256 digests; no unrestricted local path. |
| Defect/limitation | Severity, defect ID, user impact, workaround if accessible and safe, limitation, owner, and required rerun. |

## 6. Accessibility case catalogue

All cases are planned future checks. Their initial result is `Not run` and initial evidence class is `Not available`.

| ID | Planned procedure and pass expectation | Required profiles | Earliest owning task | Initial result | Initial evidence class |
|---|---|---|---|---|---|
| `A11Y-001` | Complete the primary operator journey without a mouse; every action, decision, blocker, and handover step is keyboard reachable. | `AXE-001` | `TL-0608` | `Not run` | `Not available` |
| `A11Y-002` | Verify visible focus, logical order, no keyboard trap, no drag-only interaction, and a reachable skip/close/cancel path. | `AXE-001`, `AXE-004`, `AXE-006` | `TL-0113`, `TL-0608` | `Not run` | `Not available` |
| `A11Y-003` | Inspect programmatic names, roles, values, states, descriptions, instructions, and error relationships for actionable controls. | `AXE-002`, `AXE-003` | `TL-0113`, `TL-0608` | `Not run` | `Not available` |
| `A11Y-004` | A human completes representative primary-flow tasks with Narrator and confirms meaningful speech, focus, state, and recovery. | `AXE-002` | `TL-0113`, `TL-0608` | `Not run` | `Not available` |
| `A11Y-005` | A human completes representative primary-flow tasks with NVDA and confirms meaningful speech, focus, state, and recovery. | `AXE-003` | `TL-0113`, `TL-0608` | `Not run` | `Not available` |
| `A11Y-006` | At 200% scaling, essential controls and text remain visible or scrollable, usable, and free from overlap or clipping that blocks completion. | `AXE-004` | `TL-0113`, `TL-0608` | `Not run` | `Not available` |
| `A11Y-007` | With the approved large-text setting, content reflows or remains reachable without losing instructions, status, approval, cancellation, or recovery. | `AXE-004` | `TL-0113`, `TL-0608` | `Not run` | `Not available` |
| `A11Y-008` | In Windows high contrast, focus, status, selection, warning, blocker, and disabled state remain perceivable without forced colors that remove meaning. | `AXE-005` | `TL-0113`, `TL-0608` | `Not run` | `Not available` |
| `A11Y-009` | At reduced resolution, the workflow remains operable with logical scrolling and no off-screen unreachable essential action. | `AXE-006` | `TL-0113`, `TL-0608` | `Not run` | `Not available` |
| `A11Y-010` | Long and pseudolocalized strings wrap/reflow; labels do not obscure controls; essential meaning is not truncated without an accessible alternative. | `AXE-007`, `AXE-004`, `AXE-006` | `TL-0608` | `Not run` | `Not available` |
| `A11Y-011` | Icons have text alternatives and pass/fail/warning/blocker/selection meaning is not conveyed by color, position, or icon alone. | `AXE-001`, `AXE-002`, `AXE-003`, `AXE-005` | `TL-0113`, `TL-0608` | `Not run` | `Not available` |
| `A11Y-012` | Requirements, evidence, plans, journals, reports, and expert details use accessible lists/tables with headers, item position, selection, status, and expansion state. | `AXE-001`, `AXE-002`, `AXE-003` | `TL-0113`, `TL-0608` | `Not run` | `Not available` |
| `A11Y-013` | Long operations announce phase, progress or indeterminate state, uncertainty, completion, failure, and next safe action without excessive repetition. | `AXE-002`, `AXE-003` | `TL-0113`, `TL-0405`, `TL-0608` | `Not run` | `Not available` |
| `A11Y-014` | Safe cancellation is keyboard reachable, announces acceptance and actual completion separately, and leaves truthful state and recovery guidance. | `AXE-001`, `AXE-002`, `AXE-003` | `TL-0405`, `TL-0608` | `Not run` | `Not available` |
| `A11Y-015` | Errors and blockers identify the affected control/work, explain impact in plain language, move or associate focus appropriately, and expose a safe recovery action. | `AXE-001`, `AXE-002`, `AXE-003` | `TL-0113`, `TL-0608` | `Not run` | `Not available` |
| `A11Y-016` | Closing a dialog or returning from a nested view restores focus and context to the invoking control or an explained safe location. | `AXE-001`, `AXE-002`, `AXE-003` | `TL-0113`, `TL-0608` | `Not run` | `Not available` |
| `A11Y-017` | After UAC approval or decline, the unelevated UI restores attributable context, announces the outcome, and offers keyboard-reachable recovery. | `AXE-001`, `AXE-002`, `AXE-003` | `TL-0313`, `TL-0608` | `Not run` | `Not available` |
| `A11Y-018` | After safe process interruption or workflow resume, the current stage, completed work, uncertainty, focus, and next action are understandable without a mouse. | `AXE-001`, `AXE-002`, `AXE-003` | `TL-0309`, `TL-0608` | `Not run` | `Not available` |
| `A11Y-019` | After a full powered-off cold boot, resumed context and post-boot verification are announced and navigable; an ambiguous or failed checkpoint cannot appear complete. | `AXE-001`, `AXE-002`, `AXE-003` | `TL-0509`, `TL-0608` | `Not run` | `Not available` |
| `A11Y-020` | Workshop and recipient outputs have logical structure, headings, meaningful links, table headers/reading order, non-color status, and usable 200% zoom. | `AXE-002`, `AXE-003`, `AXE-004`, `AXE-005` | `TL-0604`, `TL-0605`, `TL-0608` | `Not run` | `Not available` |
| `A11Y-021` | Sanitized support preview exposes every included field/file with accessible names, structure, selection, warning, export, and cancellation behavior. | `AXE-001`, `AXE-002`, `AXE-003`, `AXE-004` | `TL-0606`, `TL-0608` | `Not run` | `Not available` |
| `A11Y-022` | A present recipient or authorized organization can choose, preview, apply, verify, and reverse each supported accessibility setting with scope and limitations explained. | `AXE-001`, `AXE-002`, `AXE-003`, `AXE-004`, `AXE-005` | `TL-0701` | `Not run` | `Not available` |
| `A11Y-023` | Sealed handover applies no recipient-specific preference and exposes the deferred accessibility onboarding item and later guidance accessibly. | `AXE-001`, `AXE-002`, `AXE-003` | `TL-0603`, `TL-0701` | `Not run` | `Not available` |
| `A11Y-024` | Under an approved 4 GB/8 GB, slow-storage, no-GPU, or other constrained class, focus, announcements, text alternatives, status detail, and safe cancellation remain enabled and usable. | `AXE-008` plus `AXE-002` or `AXE-003` | `TL-0510`, `TL-0707` | `Not run` | `Not available` |

## 7. Required execution pairings

The later audit records at least these pairings; a single automated run cannot replace them:

| Pairing | Cases | Human requirement | Current state |
|---|---|---|---|
| Keyboard-only reference-device journey | `A11Y-001`, `A11Y-002`, `A11Y-011`, `A11Y-012`, `A11Y-014`–`A11Y-019` | Human keyboard walkthrough | Pending; `Not run` |
| Narrator primary and recovery paths | `A11Y-003`, `A11Y-004`, `A11Y-011`–`A11Y-021` | Human Narrator review | Pending; `Not run` |
| NVDA primary and recovery paths | `A11Y-003`, `A11Y-005`, `A11Y-011`–`A11Y-021` | Human NVDA review | Pending; `Not run` |
| Scaling, large text, contrast, resolution, and long strings | `A11Y-006`–`A11Y-010`, `A11Y-020`, `A11Y-021` | Human visual/interaction review, supplemented by automation | Pending; `Not run` |
| UAC, interruption, and cold-boot recovery | `A11Y-017`–`A11Y-019` | Human review on supported Windows hardware | Pending; `Not run` |
| Recipient-present and sealed handover | `A11Y-022`, `A11Y-023` | Accessibility reviewer and representative recipient/proxy in the owning tasks | Future workflow; `Not run` |
| Low-resource accessibility | `A11Y-024` with representative navigation, progress, error, and cancellation cases | Human AT review plus a LOW_SPEC resource record | Pending; `Not run` |

## 8. Human assistive-technology procedure

For Narrator or NVDA runs, the human reviewer:

1. records the exact Windows, application, and assistive-technology versions and the synthetic fixture digest;
2. starts from a documented focus location and uses keyboard commands rather than a pointer to repair focus;
3. records the spoken name, role, state, value, instructions, error, progress, and recovery behavior relevant to the case;
4. verifies that visual text and spoken output describe the same safety state without claiming unsupported certainty;
5. exercises cancellation, an expected error, a blocking condition, dialog return, and resume where the owning behavior exists;
6. records repetition, verbosity, silence, focus loss, inaccessible custom control, clipping, and timing defects;
7. checks cleanup and confirms that no live personal data, screenshot, raw speech transcript, or machine-specific path entered repository evidence; and
8. signs the result as `Human confirmed` only when they actually performed the run.

If an owning feature is not implemented, the case remains `Not run`. If the required assistive technology or device is missing, it is `Not available`. Neither state can be waived by an automated accessibility scan.

## 9. Defect and gate policy

Every defect records case/run ID, affected flow/control, user impact, severity, environment and assistive technology, reproduction steps, expected accessible behavior, remediation, evidence reference, and required rerun.

| Severity | Gate treatment |
|---|---|
| Critical | Blocks the affected gate: a core safety action, blocker, approval, cancellation, recovery, or result cannot be perceived or operated accessibly. |
| High | Blocks the affected gate: the primary workflow or required output cannot be completed safely through an accessible path. |
| Medium | Tracked with owner and rerun; cannot be accepted if it removes a required matrix condition or safe fallback. |
| Low | Tracked with owner and rationale; may not be used to hide missing required evidence. |

Fixes produce a new run linked to the original defect and result. Prior failures remain in the evidence history. An accessible workaround is acceptable only when it is safe, documented, independently usable, and does not require a mouse, color-only interpretation, expert-only command, disabled safety control, or sibling application.

## 10. Low-spec and output obligations

Accessibility remains enabled during constrained runs. Reducing animation or preview fidelity is permitted only when focus, names, roles, states, text alternatives, status detail, progress, uncertainty, cancellation, and recovery remain intact. A resource improvement obtained by disabling accessibility or security checks is a failure.

Generated workshop records, recipient guides, and support previews are tested as outputs, not inferred accessible from the WPF UI. Export-format limitations remain explicit. A technical support archive does not need to become a recipient document, but its preview and export controls remain accessible.

The empty result register is intentional:

| Case range | Test result | Evidence class | Accessibility reviewer | Evidence reference |
|---|---|---|---|---|
| `A11Y-001`–`A11Y-024` | `Not run` | `Not available` | Pending | Pending |

Human keyboard, Narrator, NVDA, visual, low-resource, and recipient/proxy results are pending. Procedure approval confirms only that the matrix and available device pool were reviewed; it is not accessibility conformance, release authorization, a minimum-spec claim, or proof of long-term reliability.
