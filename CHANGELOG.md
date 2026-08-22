# Roadmap Bundle Changelog

## 0.3.1 — 22 August 2026

Governance patch that assigns a unique future ADR identity without changing product scope or runtime behavior.

### Approved amendment

- Janne Vuorela, Principal Software Architect & Sole Project Owner, approved the [named ADR-number uniqueness amendment](docs/amendments/2026-08-22-adr-0009-reservation.md) on 22 August 2026. The approval covers the named resolution, not an exact-byte review of the later implementation commit.
- Preserved `docs/adr/0004-ephemeral-broker.md` and its completed TL-0009 evidence unchanged.
- Reserved ADR 0009 for TL-0401 at `docs/adr/0009-winget-backend.md`; the former future `docs/adr/ADR-004-winget-backend.md` contract path is superseded and never contained a file or evidence.
- Updated the TL-0401 deliverable and human-evidence wording together. Dependencies, status, evidence, backend requirements, test triggers, and the future maintainer approval remain unchanged.
- Added `docs/amendments/2026-08-22-adr-0009-reservation.md` and validator coverage for the exact ADR 0004/0009 ownership and path reservation; no portfolio-wide ADR registry is created.
- Synchronized active bundle metadata from 0.3.0 to 0.3.1 and regenerated the governed manifest.

### Impact

No frozen product decision, project boundary, task dependency, milestone gate, runtime, data, migration, security, privacy, accessibility, low-spec, licence, redistribution, release, or sibling-integration behavior changes.

## 0.3.0 — 15 August 2026

Single-machine development and risk-based testing revision aligned with the ThirdLife Software Portfolio v2.1 baseline.

### Changed

- Replaced every active requirement for a lab, physical device pool, lower-performance test computer, volunteer hardware matrix, or authoritative remote runtime runner with the **active Codex machine only** rule.
- Recast modest-hardware support as an architectural and measurable engineering obligation: bounded resources, conservative concurrency, CPU fallback, disk preflight, cancellation/checkpointing, and graceful degradation.
- Added quick, targeted, full, and extended test tiers with task-level expected tiers and explicit broad-suite triggers.
- Added the rule that reproducible defects receive the smallest practical deterministic regression fixture and that slow scenarios remain independently invokable and checkpointed.
- Added GitHub-continuous development, start-of-session divergence checks, meaningful checkpoint pushes, `STATUS.md`, and same-machine clean-clone gates.
- Superseded `TL-0008 draft 1` and its immediate physical device-pool `MHT-001`–`MHT-021` walkthrough without deleting its historical identity.
- Rewrote `TL-0008` as a Codex-executable governance and test-system transition task. It now creates the reference-machine profile, capability/risk matrix, same-machine constraints, manual-test specification, and tiered test contract; it does not run the former broad matrices.
- Retained the product’s guided manual functional-test workflow while removing hardware-matrix certification and missing-equipment blockers.
- Updated milestone gates, task acceptance criteria, release evidence, security, accessibility, project boundary, and release-interface wording to distinguish design intent, fixture coverage, same-machine constraints, and observed active-machine evidence.
- Updated portfolio metadata from version 2.0 to version 2.1.
- Extended the frozen decision register from 57 to 66 decisions.

### Added

- `STATUS.md`
- `DEVELOPMENT_WORKFLOW.md`
- `TESTING.md`
- `TL-0008_TRANSITION.md`
- `CODEX_TL0008_TRANSITION_PROMPT.md`
- `docs/testing/reference-machine-profile.md`
- `docs/testing/capability-risk-matrix.md`
- `docs/testing/same-machine-constraints.md`
- `docs/testing/manual-hardware-tests.md`
- `docs/testing/failure-injection.md`
- `docs/testing/accessibility-matrix.md`
- `docs/history/TL-0008-draft-1-superseded.md`
- `tools/merge_task_contracts.py`

### Migration note for the active repository

Development has already reached the former TL-0008 hardware-test boundary. Do not replace the active repository’s task execution history with the clean bundle defaults. Use `tools/merge_task_contracts.py` to merge the v0.3.0 task contracts while preserving the active task `status`, `evidence`, `blocked_reason`, and `notes`; then verify the result manually, update `STATUS.md`, commit, push, and continue with revised TL-0008.

The historical references below are preserved only to identify the superseded procedure:

- procedure: `TL-0008 draft 1`;
- source commit: `4fa3ea050fd5e9985fde9cc8218281698d371cc8`;
- procedure digest: `ef150dbf14b5db208582b7b526c7e0c6d0a5b912736e9e6519b8918abcf0928b`.

## 0.2.0 — 14 August 2026

Portfolio-alignment revision made before implementation began.

### Changed

- Reframed the active project as **ThirdLife Setup Core** while retaining `ThirdLife.*` code identity.
- Recorded Team B / B1 ownership and the future Team B / B4 suite-assembly boundary.
- Changed the delivery endpoint from the M6 controlled pilot to an M7 standalone **Core 1.0 stable-release gate**.
- Added recipient-controlled accessibility setup and basic operating-system backup onboarding to the Core 1.0 completion scope.
- Prohibited live sibling dependencies, shared SDKs/schemas, sibling-specific catalogue work, and development against sibling branches.
- Defined late binding against exact frozen releases, interface sheets, samples, and public documentation.
- Added the minimal release-interface obligation, a verified pilot-draft step, and a deferred future-assembly backlog.
- Added project-level security, accessibility, low-spec, and boundary documents required by the portfolio governance model.
- Extended the machine-readable graph from 81 to 91 tasks and from 7 to 8 milestones.
- Extended the decision register from 45 to 57 decisions.
- Strengthened the validator to check the portfolio metadata, required documents, M7 stable gate, and project-vacuum contract.

### Migration note

No implementation task had started in the bundle baseline. `TL-0001` remained the only initially ready task, so no execution evidence or completed task history required migration at that time.
