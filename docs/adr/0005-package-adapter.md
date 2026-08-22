# ADR 0005 — Replaceable structured package adapter

## Status and authority

**Status:** Accepted for `TL-0009`.

**Task:** [`TL-0009`](../../TASKS.yaml)

This ADR records existing binding decisions as planned architecture constraints. It does not amend [`DECISIONS.md`](../../DECISIONS.md) or [`PROJECT_BOUNDARY.md`](../../PROJECT_BOUNDARY.md), and it is not evidence that the planned behavior is implemented or verified.

## Decision IDs

- [D-022](../../DECISIONS.md) — Curated application catalog
- [D-023](../../DECISIONS.md) — Profiles are non-executable data
- [D-024](../../DECISIONS.md) — Structured WinGet integration
- [D-025](../../DECISIONS.md) — Package trust controls
- [D-026](../../DECISIONS.md) — No WinGet Configuration in Core 1.0
- [D-032](../../DECISIONS.md) — Action journal and completion
- [D-043](../../DECISIONS.md) — Dependency and licensing control
- [D-055](../../DECISIONS.md) — Generic Core catalogue and profiles

## Context

ThirdLife needs structured package resolution, progress, cancellation, error, and installed-state behavior without binding domain contracts to one WinGet interface or localized command output. The production backend decision is intentionally deferred until [`TL-0401`](../../TASKS.yaml), which compares Microsoft.WinGet.Client and the WinGet COM API under the real privilege, scope, cancellation, and verification requirements.

The seam must not become a generic execution surface, and an adapter must not hide supply-chain changes between operator approval and execution.

## Decision

`ThirdLife.Packages` provides a replaceable structured package-backend seam. The outer execution composition binds a compiled package-action handler to that seam through backend-neutral typed action DTO and registry contracts; portable `ThirdLife.Actions` does not reference the Windows package implementation. The seam covers only reviewed package semantics:

- resolve an exact approved source and package ID into typed publisher, version, architecture, scope, provenance, and available signature/hash evidence;
- expose bounded structured progress, cancellation, timeout, restart signaling, stable result categories, and installed-state queries;
- return explicit unavailable, mismatch, failed, ambiguous, and requires-review results without carrying raw backend output as a production contract;
- allow deterministic fakes and conformance tests to exercise the same typed behavior; and
- separate backend application from independent package identity/state/launch verification.

Backend selection remains open until `TL-0401`. Structured supported APIs are preferred. A controlled CLI fallback may be considered only when it produces structured or invariant bounded output and preserves the same validation contract; localized human-readable WinGet tables are never parsed as the production interface.

Profiles select reviewed generic capabilities. Catalogue entries and deterministic resolution bind those capabilities to exact reviewed identities. Neither profiles nor catalogue data can provide package commands, executable paths, URLs, source switches, installer arguments, scripts, WinGet Configuration, or PowerShell DSC resources. Only a compiled allowlisted action may call the adapter.

Before approval, resolution records the exact material metadata. At execution, the backend re-resolves or retrieves the artifact and compares source, redirects/cache identity, ID, publisher, version, architecture, scope, provenance, and available trust evidence to the approved snapshot. A material change blocks execution and requires a new resolution, preview, and approval. There is no source substitution, security-hash override, or continue-anyway path.

Licence-to-use, installation rights, and redistribution rights remain separate reviewed facts. The approved [supply-chain contract](../supply-chain/dependencies.md) grants no blanket redistribution right and preserves all written limitations and withheld rights. Current synthetic catalogue placeholders are non-installable, have no artifact, are not shipped, and are not production package admission.

## Alternatives considered

- **Choose Microsoft.WinGet.Client or COM during M0:** rejected because `TL-0401` owns evidence-based selection against the later execution requirements and installed platform versions.
- **Parse ordinary WinGet CLI tables:** rejected because localized presentation text is not a stable typed integration contract.
- **Put install commands in profiles or catalogue rows:** rejected because declarative data must not become arbitrary execution.
- **Expose a generic shell or installer adapter:** rejected because unbounded commands, paths, URLs, and arguments cannot be bound safely to the reviewed catalogue/action model.
- **Treat backend exit success as completion:** rejected because the requested identity and postcondition require independent verification.

## Consequences

- Package planning and actions can be tested without network access or host mutation, and the backend can change after a governed comparison without changing domain policy.
- The common contract may expose fewer backend-specific features; a new feature needs an independently useful Core outcome, typed semantics, trust review, and tests.
- Resolution and execution require duplicate trust checks by design. Metadata drift creates visible reapproval work rather than silent substitution.
- Network use, cancellation, timeout, restart, non-rollback behavior, and recovery remain explicit to the operator and journal.
- Any dependency, backend, real catalogue application, or distribution-plan change triggers the governed supply-chain workflow and its applicable Full-tier test trigger; this ADR alone admits none.

## References

- [Security package and update supply-chain rules](../../SECURITY.md#8-package-and-update-supply-chain)
- [Dependency, licence, provenance, and SBOM controls](../supply-chain/dependencies.md)
- [Candidate synthetic catalogue contract](../../fixtures/README.md)
- [Security data-flow package boundaries](../security/data-flow.md)
