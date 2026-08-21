# ThirdLife Setup Core — Product Contract

**Status:** Governed summary of the frozen product contract  
**Active project:** Team B / B1 — ThirdLife Setup Core  
**Primary authority:** `DECISIONS.md`, `ROADMAP.md`, and `PROJECT_BOUNDARY.md`

This document makes the active product contract easy to find. It does not amend or outrank the authority files above. If a summary here conflicts with a higher-authority file, stop and follow `docs/change-control.md`.

## Identity

**ThirdLife** is the product family, repository, solution, and `ThirdLife.*` code identity. **ThirdLife Setup Core** is the active standalone project and user-facing release being built in this repository.

ThirdLife Setup Core is Team B / B1. It must remain useful, installable, updateable, repairable, removable, recoverable, and supportable without a sibling application, a shared portfolio service, or a project-controlled account.

## Users and outcome

The primary operator is a volunteer or staff refurbisher preparing donated Windows computers. A present recipient participates only in explicitly recipient-controlled setup. Where a later governed workflow permits an authorized organization to act, its authority and recovery ownership must be explicit; this does not let the workshop infer personal choices.

The product provides a local-first, auditable workflow:

1. **Intake** a device whose donor storage is sanitized, replaced, or absent.
2. **Inspect** the device with bounded read-only evidence and human-assisted tests.
3. **Decide** by evaluating immutable evidence against versioned organization policy.
4. **Prepare** only the complete plan that an operator reviewed and approved.
5. **Verify** outcomes independently after actions and required restarts.
6. **Handover** only when policy, verification, finalization, and reporting gates allow it.

The normal preparation target is supported Windows 11 x64. Windows 10 may be assessed and dispositioned, but it cannot be represented as normally ready. The product does not certify a device as secure, reliable, sanitized, or universally suitable.

## Delivery cuts and Team B queue

| Delivery cut | Meaning |
|---|---|
| M0 through M6 / `TL-0611` | A controlled v0.1 partner pilot. This does not complete Team B/B1 or authorize suite integration. |
| M7 / `TL-0710` | The human gate for an independently releasable ThirdLife Setup Core 1.0 and the only Team B/B1 exit gate. |
| After `TL-0710` | Team B proceeds to **Scam Explainer**. |
| Future Team B / B4 | **ThirdLife Deployment and Suite Assembly** may later work against exact frozen releases and public release documentation. B4 is not active or authorized by this repository. |

Core 1.0 includes recipient-controlled accessibility setup and basic operating-system backup onboarding with a harmless restore verification where supported. It excludes personal account creation, recovery-key custody, a custom backup engine, Backup Circle control, and applying recipient-specific choices during sealed handover.

## Product rules

- Develop B1 in a **project vacuum**. Do not inspect, import, or depend on a sibling repository, branch, service, database, schema, fixture, or release schedule.
- Keep observed facts, organization policy, and dispositions separate. Missing or unavailable evidence stays unknown and never becomes an implicit pass.
- Show the complete resolved plan, impact, privilege, network, restart, rollback, and verification expectations before any change.
- Keep the WPF UI unelevated. Privileged actions use a short-lived, independently validating, typed, allowlisted broker only after approval.
- Keep policies, profiles, and catalogue entries declarative and non-executable. They cannot supply scripts, shell commands, arbitrary executable paths, registry paths, URLs, or unrestricted file operations.
- Treat **applied** and **verified** as different states. Backend success is not proof that the requested outcome exists.
- Store only Core-owned local workflow data, minimize sensitive fields, and keep workshop, recipient, and diagnostic outputs in separate privacy classes.
- Make safety state, progress, cancellation, failure, and recovery accessible without a mouse or color-only meaning.
- Require no GPU, permanent background service, constant broadband, or unbounded cache, memory, storage, retry, or concurrency behavior.

## Specialized quality baselines

- `SECURITY.md` defines the security objective, trust boundaries, privileged-broker invariants, supply-chain controls, and release evidence.
- `docs/privacy/privacy-model.md` and `docs/privacy/logging-standard.md` define the three audience classes, excluded data, proposed retention defaults, prohibited diagnostics, and support allowlist; their named privacy-owner approval remains pending until evidenced in `TL-0005`.
- `ACCESSIBILITY.md` defines the operator and recipient accessibility baseline and the required human evidence.
- `LOW_SPEC.md` defines bounded-resource and graceful-degradation rules; numerical support claims require measurements.
- `RELEASE_INTERFACE.md` is populated only from verified preview or frozen release behavior. It is not a shared API or an early adapter contract.
- `docs/non-goals.md` records the product and portfolio exclusions that ordinary tasks may not erode.
- `docs/glossary.md` defines the vocabulary used for evidence, decisions, action state, and later black-box assembly.

## Change rule

Ordinary tasks may implement this contract but may not silently redefine it. Frozen decisions, the canonical **Owns / Does not own** boundary, the task graph, milestone scope, and portfolio posture require an explicitly approved governed amendment. Cross-project ideas go only to `FUTURE_ASSEMBLY_NOTES.md`; a note creates no B1 task, dependency, implementation, or release promise.

See `docs/change-control.md` for the authority order, stop conditions, amendment record, synchronization steps, and evidence rules.

## Current maturity and claims

This contract states the governed target, not a claim that every planned capability already exists. `TASKS.yaml` and its evidence show implementation progress. `RELEASE_INTERFACE.md` alone records verified preview or frozen-release facts; fields remain **TBD**, **not yet verified**, or **not supported** until evidence exists.
