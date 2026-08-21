# ThirdLife Setup Core — Accessibility Requirements

**Status:** Binding release baseline  
**Bundle version:** 0.3.0  
**Validation scope:** Active Codex machine only; no external hardware or assistive-technology device matrix is implied

## 1. Scope

Accessibility applies to both:

- the workshop operator interface; and
- the recipient-controlled accessibility setup included in ThirdLife Setup Core 1.0.

It is a release requirement, not a later visual-polish task. Status, safety gates, progress, evidence limits, and recovery must remain understandable without color, mouse use, or expert Windows terminology. Resource constraints must not be used to remove essential accessibility behavior.

## 2. Operator workflow requirements

The complete **Intake → Inspect → Decide → Prepare → Verify → Handover** workflow must support:

- keyboard-only completion with visible focus and logical tab order;
- no drag-only interaction and a keyboard-accessible alternative for every pointer action;
- programmatic names, roles, values, states, descriptions, and error relationships;
- screen-reader announcement of navigation, progress, cancellation, completion, warnings, blockers, and recovery actions;
- 200% scaling, large text, Windows high contrast, reduced resolution, and long localized test strings without clipped essential controls;
- text equivalents for icons and no required meaning carried only by color;
- accessible tables and lists for requirements, evidence, plans, journals, and reports;
- safe cancellation and truthful cancellation state for long work;
- restoration of focus and context after dialogs, UAC return, errors, resume, and restart;
- plain-language task names before technical provider terms;
- an expandable expert view for bounded technical evidence.

Use standard WPF controls where practical. Custom controls require explicit UI Automation peers, keyboard behavior, and focused regression tests.

## 3. Recipient-controlled accessibility setup

Core 1.0 may guide a present recipient through supported Windows settings such as text scaling, contrast, keyboard accessibility, pointer size/speed, screen reader, captions, speech input, mono audio, language, and keyboard layout.

Rules:

- Do not choose settings based on assumptions about the recipient.
- State whether a setting is machine-wide or user-specific.
- Preview the effect when Windows supports preview.
- Make changes reversible where supported and always journal the attempted and verified result.
- Independently verify the resulting setting rather than treating a successful API call as completion.
- Do not create an online identity or retain personal secrets.
- In sealed handover, do not apply recipient-specific preferences; record them as pending guidance.
- Accessibility setup must not be a prerequisite for the operator UI itself being accessible.

## 4. Reports and exported guidance

- Recipient guides use plain language, headings, meaningful link text, and accessible document structure where the chosen output format supports it.
- Tables have understandable headers and reading order.
- Status, blockers, uncertainty, and limitations do not rely on color.
- Export limitations are stated rather than silently producing inaccessible output.
- The sanitized diagnostic bundle is technical and previewable, but its preview UI remains keyboard and screen-reader usable.

## 5. Same-machine verification model

All accessibility implementation and verification run on the active Codex machine. The project does not require a second computer, separate low-performance computer, external assistive-technology laboratory, cloud UI runner, or multi-device matrix.

Evidence may use:

- automated UI Automation inspection and focused component tests;
- deterministic view-model/state fixtures for progress, warning, blocker, cancellation, resume, and error states;
- keyboard-only walkthroughs on the active Codex machine;
- Windows Narrator on the active Codex machine;
- NVDA on the active Codex machine when the named test task installs or provides it through an approved, reproducible procedure;
- same-machine scaling, high-contrast, reduced-resolution, long-string, and low-resource scenarios;
- generated-report structure review using non-sensitive fixtures.

A missing assistive technology or device capability is recorded as a limitation or deferred named test, not silently treated as passed. It does not authorize a broad claim that the product has been tested across assistive technologies or hardware configurations.

## 6. Required test coverage

The following coverage must exist by its named task or release gate, not necessarily during revised `TL-0008`:

- complete keyboard-only primary workflow;
- Windows Narrator primary-path review;
- NVDA primary-path review where the approved test environment is available on the active Codex machine;
- 200% scaling and large text;
- Windows high-contrast modes;
- reduced screen resolution;
- long and translated test strings;
- progress announcements and cancellation;
- UAC return, interruption/resume, and error recovery without a mouse;
- recipient accessibility-setting preview, apply, verify, reverse, and sealed-handover skip;
- generated workshop and recipient output review;
- same-machine low-resource run proving that focus, announcements, text alternatives, and cancellation remain available.

Automated accessibility checks supplement but do not replace the later human assistive-technology review explicitly required by the task graph.

## 7. Test-tier placement

- **Quick:** changed accessibility unit/component tests, static checks, shortest keyboard smoke path, and deterministic state fixtures.
- **Targeted:** affected screen, dialog, report, automation peer, scaling, keyboard, or announcement tests.
- **Full:** complete primary keyboard journey and supported accessibility matrix at milestone, preview, and stable gates or after a major UI architecture change.
- **Extended:** endurance, repeated interruption, very long strings, broad state combinations, or other expensive cases only when the changed risk or a scheduled release gate requires them.

Run the failed deterministic case first, then the related targeted set. Do not rerun the full accessibility matrix after every small correction.

## 8. Evidence and defect policy

Every accessibility defect records:

- affected flow and control;
- application version/source commit;
- active-machine Windows/build, scaling, contrast, assistive technology, and relevant constraint settings;
- reproduction steps and smallest deterministic fixture where practical;
- user impact and severity;
- expected accessible behavior;
- remediation and regression evidence;
- any verified limitation;
- test tier, command or walkthrough, result, and duration.

Critical or high-severity findings in the primary workflow block release unless a human-approved scope change removes the affected workflow. A limitation may be documented only when the user can still complete the core task safely through an accessible path.

## 9. Resource-aware accessibility

Accessibility features must remain functional in low-resource mode. Do not disable focus, announcements, text alternatives, status detail, error relationships, or safe cancellation to reduce CPU or memory use. Animation, decorative effects, and preview fidelity may be reduced when this does not remove essential meaning or feedback.

Release wording may report the accessibility tools and settings actually exercised on the active Codex machine. It must not imply an unperformed cross-device or cross-assistive-technology certification.
