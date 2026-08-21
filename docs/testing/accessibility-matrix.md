# ThirdLife Setup Core — Accessibility verification matrix

**Status:** Active specification; defined but not executed at revised `TL-0008`  
**Procedure revision:** TL-0008 same-machine revision 2  
**Task:** `TL-0008`  
**Execution state:** `A11Y-001`–`A11Y-010` are defined and individually addressable; all are `Not run`.

## 1. Purpose and claim boundary

This matrix consolidates detailed keyboard, focus, UI Automation, screen-reader, scaling, large-text, long-string, contrast, output, plain-language, progress, cancellation, UAC, interruption, cold-boot, manual-test, recipient-present, sealed-handover, and constrained-resource coverage into ten canonical accessibility cases.

The primary operator journey is **Intake → Inspect → Decide → Prepare → Verify → Handover**. Later recipient-controlled accessibility setup is explicit, previewable/reversible where supported, verified, and secret-minimizing; sealed handover applies no personal preference and records onboarding pending.

Revised `TL-0008` defines cases, safe environments, tier triggers, and non-executable invocation placeholders only. It does not run a keyboard, Narrator, NVDA, visual, scaling, contrast, recipient/proxy, output, or constrained-resource audit. No definition here is an accessibility-conformance or release claim.

All development execution is hosted by the active Codex machine. Automation/hosted constraints cannot replace human assistive-technology or visual/cognitive review when a later task explicitly requires it, and they do not prove another machine or hardware combination.

## 2. Result and evidence semantics

`test_result` and `evidence_class` are independent.

| Field | Exact value | Meaning |
|---|---|---|
| `test_result` | `Pass` | The complete applicable procedure ran and every stated accessibility expectation was met with required evidence. |
| `test_result` | `Fail` | A required interaction, meaning, announcement, focus, recovery, output, or constrained-condition expectation failed. |
| `test_result` | `Not available` | Required assistive technology, implemented behavior, safe setting/capability, or evidence source was unavailable. It is not a pass. |
| `test_result` | `Not run` | The case was not started/completed or its later trigger was not reached. It is not a pass. |
| `evidence_class` | `Observed` | Bounded UI Automation, application, Windows, generated-output, or artifact evidence was captured with provenance. |
| `evidence_class` | `Inferred` | A conclusion was derived from named observations; inference alone cannot satisfy required human review. |
| `evidence_class` | `Not available` | Required evidence was unavailable; missing evidence remains unknown. |
| `evidence_class` | `Human confirmed` | A human actually performed/observed the interaction and recorded role, tool/version, timestamp, and provenance. |

Automation/synthetic input is a test source, not human evidence. An automated scan cannot be relabelled `Human confirmed`; absence of a mouse, assistive technology, display mode, or language is a limitation, not proof of accessibility.

## 3. Privacy and safety

Use only synthetic jobs, policies, profiles, labels, errors, long strings, reports, and support fields. Evidence excludes names/contacts, serials/fragments, asset/service tags, hardware UUIDs, device/host names, usernames/SIDs, MAC/IP/SSID values, account/tenant data, credentials, product/recovery keys, personal paths, screenshots, raw logs, dumps, archives, audio/speech transcripts, and exact locations.

An accessibility test must not weaken a safety/privacy/support/ownership/verification gate. Recipient choices occur only in the later recipient-present owning task; sealed handover applies none. Evidence uses opaque project-created run IDs and repository-relative or approved durable artifact references.

## 4. Active-machine environment profiles

All profiles run directly on the active Codex machine or in a reversible environment it hosts. They are definitions, not inventory/evidence. A later task records the sanitized reference-profile revision, build, exact settings/tool versions, fixture hash, reviewer, timestamp, restoration, and limitation.

| ID | Planned environment | Safe setup/limit | Current result |
|---|---|---|---|
| `AXE-001` | Supported Windows 11 x64; keyboard only; pointer set aside. | Do not change input-device configuration; record input class only. | `Not run` |
| `AXE-002` | Built-in Windows Narrator. | Retain no spoken output or personal notification content. | `Not run` |
| `AXE-003` | NVDA when already installed or explicitly approved for the owning task. | Record exact version/source; absence is an explicit limitation, not a TL-0008 install/blocker. | `Not run` |
| `AXE-004` | Reversible 200% scaling, approved large text, and supported reduced resolution on direct/hosted display. | Record/restore prior supported settings; never force an unsupported display mode. | `Not run` |
| `AXE-005` | Reversible Windows contrast theme and non-color review. | Record/restore prior theme/state. | `Not run` |
| `AXE-006` | Long, synthetic, and pseudolocalized strings. | Versioned deterministic fixture with SHA-256; no live personal content. | `Not run` |
| `AXE-007` | Approved same-machine no-GPU, conservative-concurrency, low-priority, slow-destination, or large-workload constraint. | Record `SMC-*` settings; does not simulate/certify a RAM/CPU/device class. | `Not run` |
| `AXE-008` | Recipient-present or sealed-handover synthetic path after implementation. | No personal account/secret/recovery material; representative recipient/proxy only at explicit later task. | `Not run` |

Missing assistive technology, display mode, or peripheral is recorded `Not available` at the later owning task. It does not require another machine and is not a TL-0008 blocker.

## 5. Result record

Every later result records:

| Field | Requirement |
|---|---|
| Identity/build | Task, case/run ID, source commit/branch, build/configuration, Windows version, procedure revision, tier, exact command/procedure, start/end/duration. |
| Environment | Reference-profile revision, `AXE-*` and `SMC-*` settings, direct/hosted context, input/display/form-factor classes. |
| Assistive technology | Product/version/source and relevant settings/output-route class without personal device names or retained speech. |
| Fixture | Synthetic fixture ID/version/path/SHA-256, expected result, locale, and privacy review. |
| Expected behavior | Names/roles/states/relationships, focus/keyboard path, announcements, visual/output behavior, cancellation/recovery/restoration. |
| Outcome/evidence | Exact result/class, bounded observations, human role where required, artifact/hash, defect/severity, limitation, focused rerun. |

## 6. Canonical case and invocation register

Every command cell is deliberately non-executable. The earliest owning task replaces it with a checked-in single-case automation command or bounded human procedure. A runner that can invoke only the whole matrix does not satisfy D-062.

| ID | Consolidated area and required outcome | Automated/targeted proof | Later human proof | Default tier and explicit earliest trigger | Individually invokable command/procedure | TL-0008 state |
|---|---|---|---|---|---|---|
| `A11Y-001` | Keyboard-only primary journey and outputs: every action, approval, blocker, table/list, report/support preview, manual-test result, cancel, and handover step is reachable without mouse/drag-only interaction. | Focus/navigation commands and deterministic primary-flow component/UI tests. | Keyboard walkthrough on active machine. | Targeted at primary UI `TL-0113`; Full operator audit `TL-0608`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-case command or bounded human procedure before invocation` | Defined; `Not run` |
| `A11Y-002` | Visible focus/logical order: no trap; dialog/nested-view/UAC return restores attributable context; skip/close/cancel is reachable at scaling/reduced resolution. | Focus-order/return assertions across representative dialogs/views. | Keyboard/visual review. | Targeted at `TL-0113` and UAC return `TL-0313`; Full at `TL-0608`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-case command or bounded human procedure before invocation` | Defined; `Not run` |
| `A11Y-003` | Names, roles, values, states, descriptions, relationships, table/list headers/position, expansion, selection, error association, and text alternatives are meaningful for UI/manual/output/support surfaces. | UI Automation peer/tree/component inspection; custom controls require peers/tests. | Narrator and, when installed/approved, NVDA semantic review. | Targeted at `TL-0113`; output/support tasks `TL-0604`–`TL-0606`; Full audit `TL-0608`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-case command or bounded human procedure before invocation` | Defined; `Not run` |
| `A11Y-004` | Status/progress/error/blocker announcements expose phase, uncertainty, completion/failure, plain next action, resume context, and non-duplicative timing. | Deterministic announcement/event tests for long operations, errors, resume, and manual results. | Narrator/NVDA review of primary and recovery paths. | Targeted at `TL-0113`/`TL-0405`; Full audit `TL-0608`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-case command or bounded human procedure before invocation` | Defined; `Not run` |
| `A11Y-005` | At 200% scaling/large text with long/pseudolocalized strings, essential text/actions/status/approval/cancel/recovery remain visible or logically scrollable without blocking overlap/clipping; outputs remain usable at 200% zoom. | Layout bounds, wrapping/reflow, long-string fixtures, and output structure checks where deterministic. | Active-machine visual/interaction review with reversible setting. | Targeted at `TL-0113`; outputs `TL-0604`–`TL-0606`; Full audit `TL-0608`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-case command or bounded human procedure before invocation` | Defined; `Not run` |
| `A11Y-006` | At a supported reduced resolution, primary/manual/support/output flow remains operable with logical scrolling and no unreachable off-screen essential action. | Hosted/direct layout tests for supported resolution profiles. | Active-machine visual/keyboard review with restoration. | Targeted at `TL-0113`; Full audit `TL-0608`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-case command or bounded human procedure before invocation` | Defined; `Not run` |
| `A11Y-007` | High contrast/color independence: focus, selection, disabled, pass/fail/warning/blocker, icons, reports, and support preview remain perceivable with text/non-color meaning. | Semantic/style/token and icon-text-alternative checks. | Active-machine contrast/visual/screen-reader review. | Targeted at `TL-0113`; outputs/support `TL-0604`–`TL-0606`; Full `TL-0608`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-case command or bounded human procedure before invocation` | Defined; `Not run` |
| `A11Y-008` | Plain language and recipient control: instructions/errors name affected work, impact, safe recovery, uncertainty, scope, reversibility, and limitation; recipient-present choices are explicit; sealed handover leaves preferences pending. | Terminology/error/content rules and synthetic recipient/sealed-path tests. | Operator/recipient/proxy and accessibility review at owning task. | Targeted content review throughout; Full operator review `TL-0608`; recipient setup `TL-0701`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-case command or bounded human procedure before invocation` | Defined; `Not run` |
| `A11Y-009` | Cancellation/recovery: cancel is keyboard reachable, acceptance vs completion is announced, state is truthful, and focus/context survives UAC decline, process interruption, resume, and later cold boot; constrained runs retain accessibility. | State/event/focus tests with deterministic interruption and `SMC-*` fixtures. | Human UAC/interruption review; checkpointed cold-boot only at named trigger; AT constrained-resource review at gate. | Targeted at `TL-0309`/`TL-0313`/`TL-0405`; Extended cold boot `TL-0509`; resource `TL-0510`; Full `TL-0608`/`TL-0707`. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-case command or bounded human procedure before invocation` | Defined; `Not run` |
| `A11Y-010` | Manual hardware-test workflow and handover: every result/evidence/unavailable/failure/pause/resume/retest path is perceivable/operable; absent capability cannot become pass; reports/recipient guidance preserve meaning. | Deterministic state/UI/report tests for all manual result and recovery paths. | Bounded active-machine operator walkthrough at `TL-0114` or later named gate, only for available capabilities. | Targeted at manual workflow `TL-0114`; Full operator/output audit `TL-0608`; recipient-controlled path `TL-0701` when applicable. | `Not implemented at TL-0008; the named owning task must supply a checked-in single-case command or bounded human procedure before invocation` | Defined; `Not run` |

## 7. Tier rules and later human pairings

- **Quick:** static/UI Automation/component checks that remain small/deterministic, plus ID/order/link/prohibited-claim validation. No human audit at revised `TL-0008`.
- **Targeted:** changed control/workflow plus the smallest case; required when an accessibility boundary changes.
- **Full:** applicable automated baseline and explicitly required human keyboard/screen-reader/visual/output review at named milestone/pilot/stable gates.
- **Extended:** only a named interruption, cold-boot, or same-machine resource scenario; never run merely because this matrix exists.

Later reviews pair keyboard-only coverage with `A11Y-001`/`A11Y-002`/`A11Y-009`; Narrator/NVDA with `A11Y-003`/`A11Y-004`/`A11Y-009`; scaling/resolution/contrast with `A11Y-005`–`A11Y-007`; recipient/content with `A11Y-008`; and manual workflow with `A11Y-010`. Only the applicable pairings named by the owning task/gate run.

On failure, rerun the single case first and then related targeted scope. Full/extended reruns occur only when the trigger applies or a shared cause is suspected.

## 8. Human assistive-technology procedure

At a later explicit trigger, the reviewer records exact Windows/application/AT versions and fixture hash; begins from documented focus; uses keyboard rather than pointer to repair focus; checks spoken name/role/state/value/instruction/error/progress/recovery; compares visual and spoken safety state; exercises cancellation/error/dialog return/resume where implemented; records silence/repetition/verbosity/focus/clipping/timing defects; restores settings; and confirms no personal data, screenshot, raw transcript, or machine-specific path entered evidence.

If behavior is unimplemented, the case remains `Not run`. If required AT or active-machine capability is unavailable, it is `Not available` with a limitation. Automation cannot waive either state, and no second machine is required.

## 9. Defects, low-spec behavior, and outputs

Critical/high defects that prevent a core safety action, blocker, approval, cancellation, recovery, primary workflow, or required output from being perceived/operated accessibly block the affected gate. Medium/low defects retain owner, impact, workaround/rationale, and exact rerun. Prior failures remain; fixes create linked results.

Accessibility remains enabled during constraints. Reducing animation/preview fidelity is allowed only when focus, semantics, text alternatives, status, uncertainty, progress, cancellation, and recovery remain intact. A resource improvement obtained by disabling accessibility/security is a failure.

Workshop records, recipient guides, and support previews are tested as outputs rather than inferred from WPF behavior. Export-format limitations remain explicit.

## 10. Current state

| Case range | Result | Evidence class | Human reviewer | Artifact | Audit |
|---|---|---|---|---|---|
| `A11Y-001`–`A11Y-010` | `Not run` | `Not available` | None | None | Not triggered by `TL-0008` |

Approval of this matrix confirms only that cases, environments, tiers, triggers, invocation placeholders, and claim limits are defined. It is not human completion, accessibility conformance, release authorization, minimum-spec evidence, or cross-hardware certification.
