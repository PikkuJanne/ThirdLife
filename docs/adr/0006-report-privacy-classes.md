# ADR 0006 — Separate report privacy classes

## Status and authority

**Status:** Accepted for `TL-0009`.

**Task:** [`TL-0009`](../../TASKS.yaml)

This ADR records existing binding decisions as planned architecture constraints. It does not amend [`DECISIONS.md`](../../DECISIONS.md) or [`PROJECT_BOUNDARY.md`](../../PROJECT_BOUNDARY.md), and it is not evidence that the planned behavior is implemented or verified.

## Decision IDs

- [D-011](../../DECISIONS.md) — Local-first data model
- [D-013](../../DECISIONS.md) — Telemetry default
- [D-014](../../DECISIONS.md) — Identity and minimization
- [D-036](../../DECISIONS.md) — Three output classes
- [D-037](../../DECISIONS.md) — Diagnostic redaction
- [D-053](../../DECISIONS.md) — Portfolio data ownership

## Context

Workshop operators, recipients, and support personnel need different information. A full technical job record may legitimately contain restricted device and operator evidence that would be unnecessary or harmful in recipient guidance or a support archive. Redacting one rendered master report at export time is insufficient because collection, persistence, rendering, optional fields, attachments, and destination handling each create privacy risk.

The approved [`TL-0005`](../../TASKS.yaml) [privacy model](../privacy/privacy-model.md) and [logging standard](../privacy/logging-standard.md) define the complete classification, allowlist, retention-guidance, and redaction contracts. This ADR records the architectural separation; it does not restate or broaden those approved fields.

## Decision

`ThirdLife.Reports` and `ThirdLife.Diagnostics` produce three independent allowlisted projections from normalized Core-owned records:

1. **Technical workshop record (`WORKSHOP_RESTRICTED`).** A restricted technical projection for the workshop. It is the only output class permitted to include the full internal job/device identity and full serial, and only when those fields are needed by its reviewed schema.
2. **Plain-language recipient guide (`RECIPIENT_GUIDE`).** An independent accessible projection of capabilities, next steps, deferred choices, support information, and limitations. It contains no workshop secrets, donor/previous-owner detail, full device identifier, credential, recovery material, or unnecessary internal action history.
3. **Sanitized diagnostic bundle (`SUPPORT_SANITIZED`).** An independent support allowlist, not a copy of the job database, workshop report, attachment tree, or log directory. The current approved support schema contains neither full nor truncated serial and has no arbitrary optional attachment field.

A less-restricted output is projected from typed normalized source records through its own schema. It is never created by copying and redacting the already-rendered workshop record. Each schema preserves evidence state, blockers, uncertainty, and limitations rather than omitting them to simplify the output.

Raw provider, backend, installer, command, exception, standard-output, and standard-error content is `RAW_UNTRUSTED_SENSITIVE`. It is bounded and normalized before any persistent write and never becomes a report, log, or support fallback. Redaction is defense in depth; it is not permission to collect arbitrary fields.

Support export freezes a read-only normalized snapshot, projects only the approved schema, validates and escapes every bounded value, displays an accessible keyboard/screen-reader preview of every field and fixed file, binds approval to the exact preview/content digest, revalidates the final destination object and capacity, and writes atomically where supported. Changed output bytes, options, schema, destination type, overwrite semantics, or another previewed property require a new preview. After a failure, the same retained preview may be used for a safe retry to another path of the same reviewed destination type only after destination revalidation. Export is explicit and manual; there is no telemetry, automatic upload, or background sender.

After export, ThirdLife stores only the approved minimal workshop-side audit metadata. It does not log a personal destination path and cannot claim control over later copying or deletion of exported bytes.

## Alternatives considered

- **One report with audience modes:** rejected because a rendering or option mistake could expose workshop-only fields to a less-restricted audience.
- **Export and redact the database or log directory:** rejected because those stores are not support schemas and may contain restricted structure or unreviewed fields.
- **Redact only at final export:** rejected because prohibited values must be excluded or redacted before the first persistent diagnostic write.
- **Allow arbitrary operator-selected attachments:** rejected because preview alone cannot classify or safely minimize unknown content.
- **Automatically upload diagnostics:** rejected because telemetry is off by default and support transfer must be explicit and preview-bound.

## Consequences

- Separate schemas and renderers reduce accidental cross-audience disclosure and make exact privacy tests possible.
- Every new report/support field, file, optional item, raw attachment, audience, or retention change requires privacy and threat-model review before implementation.
- Report generation and preview must be bounded, cancellable, accessible, and safe under hostile text, large inputs, low space, interruption, and unsafe destinations.
- The workshop record remains sensitive even when local or encrypted; local storage does not make it anonymous or tamper-proof.
- Output accessibility and privacy are separate release requirements. Plain language and document structure cannot be obtained by weakening field separation.

## References

- [Approved privacy model](../privacy/privacy-model.md)
- [Approved logging and diagnostic export standard](../privacy/logging-standard.md)
- [Synthetic redaction contract](../privacy/redaction-test-cases.yaml)
- [Security data-flow report and export boundaries](../security/data-flow.md)
