# TL-0304 deterministic plan resolution

**Date** 2026-11-25  
**Base** `6fce4510063cc94097850135327d08626dd37cdc`, PR #28  
**Environment** REF-CODEX-001 rev 2026-08-29.1, Windows 11 x64, pinned .NET SDK 10.0.400 and Python 3.14.7

## Contract

| Criterion | Evidence |
| --- | --- |
| Deterministic proposal | Essential capabilities precede selected optional capabilities, both in ordinal capability order. The v1 digest binds exact configuration bytes, job and sanitization decision, normalized evidence and policy disposition, compiled action requests and declarations, and deferred choices. |
| Blocking prerequisites | The resolver checks exact profile references and catalog digest, policy applicability, latest persisted sanitization decision and matching projected evidence, selected capabilities, and current policy disposition. A failure produces a typed block with no proposed actions. |
| Action rationale and scope | Every action carries the capability, reason, intent, catalog row, and registry-defined scope and verification. Reviewed workshop package plans require a current review and a supported machine-scope Windows 11 x64 entry. |
| Deferred recipient choices | Recipient choices remain a separate sorted collection of the original deferred choice types. |
| B1 boundary | A ready synthetic profile resolves only to parameterless no-ops. A reviewed proposal still requires independent backend and provenance checks. No planner operation downloads, elevates, persists data, or changes the device. |

The resolver expects normalized evidence projected from the current job
assessment. The existing evidence projection owns provider provenance and
lifecycle freshness. Plan review and execution will compose the resolver with
the authoritative assessment, approval, journal, and broker boundaries.

## Verification

- The pre-change Actions project passed 36 tests in 2.436 s.
- Solution force-evaluate and locked restores passed in 2.266 s and 1.824 s.
  Only the Actions and Actions.Tests lock files changed. Their first-party
  closure now includes Catalog, Core and Policy. All third-party pins and
  the other 24 lock files matched the earlier values.
- The repository lock verifier accepted all 26 projects, and supply-chain
  validation retained 28 components and the approved licence matrix.
- The new first-party reference graph triggers Full. This task changes no
  runtime dependency outside the repository, storage migration, UI control,
  privilege boundary, package execution, or release interface.
