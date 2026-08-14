# ThirdLife Setup Core — Accessibility Requirements

**Status:** Binding release baseline  
**Bundle version:** 0.2.0

## 1. Scope

Accessibility applies to both:

- the workshop operator interface; and
- the recipient-controlled accessibility setup included in ThirdLife Setup Core 1.0.

It is a release requirement, not a later visual-polish task. Status, safety gates, progress, evidence limits, and recovery must remain understandable without color, mouse use, or expert Windows terminology.

## 2. Operator workflow requirements

The complete **Intake → Inspect → Decide → Prepare → Verify → Handover** workflow must support:

- keyboard-only completion with visible focus and logical tab order;
- no drag-only interaction;
- programmatic names, roles, values, states, descriptions, and error relationships;
- screen-reader announcement of navigation, progress, cancellation, completion, warnings, blockers, and recovery actions;
- 200% scaling, large text, high contrast, reduced resolution, and long localized test strings without clipped essential controls;
- text equivalents for icons and no required meaning carried only by color;
- accessible tables/lists for requirements, evidence, plans, journals, and reports;
- safe cancellation and truthful cancellation state for long work;
- restoration of focus and context after dialogs, UAC return, errors, resume, and restart;
- plain-language task names before technical provider terms;
- an expandable expert view for bounded technical evidence.

Use standard WPF controls where practical. Custom controls require explicit UI Automation peers and focused tests.

## 3. Recipient-controlled accessibility setup

Core 1.0 may guide a present recipient through supported Windows settings such as text scaling, contrast, keyboard accessibility, pointer size/speed, screen reader, captions, speech input, mono audio, language, and keyboard layout.

Rules:

- Do not choose settings based on assumptions about the recipient.
- State whether a setting is machine-wide or user-specific.
- Preview the effect when Windows supports preview.
- Make changes reversible and journaled.
- Independently verify the resulting setting.
- Do not create an online identity or retain personal secrets.
- In sealed handover, do not apply recipient-specific preferences; record them as pending guidance.
- Accessibility setup must not be a prerequisite for the operator UI itself being accessible.

## 4. Reports and exported guidance

- Recipient guides use plain language, headings, meaningful link text, and accessible document structure where the chosen output format supports it.
- Tables must have understandable headers and reading order.
- Status and limitations must not rely on color.
- Export limitations must be stated rather than silently producing inaccessible output.
- The sanitized diagnostic bundle is technical and previewable, but its preview UI remains accessible.

## 5. Required test matrix

- complete keyboard-only workflow;
- Windows Narrator;
- NVDA;
- 200% scaling and large text;
- Windows high-contrast modes;
- reduced screen resolution;
- long and translated test strings;
- progress announcements and cancellation;
- UAC return, interruption/resume, and error recovery without a mouse;
- recipient accessibility-setting preview, apply, verify, reverse, and sealed-handover skip;
- generated workshop and recipient output review.

Automated accessibility checks supplement but do not replace human assistive-technology review.

## 6. Evidence and defect policy

Every accessibility defect records:

- affected flow and control;
- assistive technology and Windows/build version;
- reproduction steps;
- user impact and severity;
- expected accessible behavior;
- remediation and regression evidence;
- any verified limitation.

Critical or high-severity findings in the primary workflow block release unless a human-approved scope change removes the affected workflow. A limitation may be documented only when the user can still complete the core task safely through an accessible path.

## 7. Resource-aware accessibility

Accessibility features must remain functional in low-resource mode. Do not disable focus, announcements, text alternatives, status detail, or safe cancellation to reduce CPU or memory use. Animation and preview fidelity may be reduced when this does not remove essential meaning.
