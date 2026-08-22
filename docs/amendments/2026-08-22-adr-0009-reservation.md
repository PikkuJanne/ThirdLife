# Approved amendment — Reserve ADR 0009 for the TL-0401 WinGet backend decision

## Approval and authority

| Field | Approved value |
|---|---|
| Amendment ID | `AMD-2026-08-22-ADR-0009` |
| Status | Approved |
| Approving owner | Janne Vuorela — Principal Software Architect & Sole Project Owner |
| Approval date | 2026-08-22 |
| Approval source | User instruction in the active Codex task: “Please proceed with recommended resolution. Approved by Janne Vuorela.” |
| Source baseline | `4872f7c34143a3278b92bcb37c8fcd9a435b03a7` — completed TL-0009 checkpoint; the approval names the resolution, not the later 0.3.1 implementation bytes |
| Authority | [D-045 — Authority and change control](../../DECISIONS.md#d-045--authority-and-change-control) |
| Bundle transition | `0.3.0` → `0.3.1` |

The approved amendment is narrowly named: preserve the completed broker decision as [ADR 0004](../adr/0004-ephemeral-broker.md) and reserve the next unoccupied ADR identifier, ADR 0009, for the future `TL-0401` WinGet backend decision at `docs/adr/0009-winget-backend.md`.

## Affected authority, tasks, and evidence

- **Decision IDs:** D-024, D-025, D-026, and D-043 continue to govern the future package-backend comparison; D-045 governs this synchronized contract amendment. No frozen decision text or meaning changes.
- **Authority files:** `DECISIONS.md` records the approval; `ROADMAP.md` identifies ADR 0009 with TL-0401; lower bundle-version headers are synchronized.
- **Task contract:** only the future `TL-0401` ADR deliverable path and matching human-evidence wording change in `TASKS.yaml`. Its ID, milestone, dependencies, priority, status, executor, environment, decisions, objective, other deliverables, acceptance criteria, verification, test triggers, and existing evidence remain unchanged.
- **Schemas and releases:** `TASKS.schema.json`, runtime schemas, package metadata, and release artifacts do not change. No release-interface behavior is populated or promised.
- **Completed evidence:** `TL-0009` and commit `4872f7c34143a3278b92bcb37c8fcd9a435b03a7` remain historically exact. ADR 0004 retains its title, path, content, digest history, and relationship to D-023 and D-029 through D-033.

## User problem and decision

The initial ADR set already occupies ADR 0001 through ADR 0008. The future TL-0401 contract separately called its WinGet record “ADR-004.” Although the two paths were technically different, human reviews, approvals, evidence indexes, commit messages, and support discussions could cite “ADR-004” and identify the wrong architectural decision.

The contract therefore reserves:

- **ADR 0004:** `docs/adr/0004-ephemeral-broker.md`, unchanged; and
- **ADR 0009:** `docs/adr/0009-winget-backend.md`, to be created only when dependency-ready TL-0401 executes.

The TL-0401 maintainer must review the future spike evidence and approve ADR 0009 at that exact path before production adapter work begins. This amendment is numbering approval only; it is not that future backend-selection approval.

## Alternatives considered

- **Retain both “ADR-004” names and require full paths manually:** rejected as the fallback because it leaves durable human evidence ambiguous and depends on every later reviewer remembering the exception.
- **Renumber the completed ephemeral-broker ADR:** rejected because it would rewrite completed TL-0009 identity and weaken historical references for no product benefit.
- **Use a descriptive path without a number:** rejected because it would create a parallel naming convention while the governed initial register already uses a four-digit sequence.
- **Reserve ADR 0009 for TL-0401:** approved because 0001–0008 are occupied, 0009 is unreserved, and no existing evidence must move.

Until bundle 0.3.1 is consumed, the safe manual fallback is to cite both old subjects by full path and avoid the shorthand “ADR-004.”

## Impact assessment

- **Security and privacy:** no runtime, privilege, IPC, package trust, data-classification, collection, retention, logging, report, support-export, or telemetry behavior changes.
- **Accessibility and low-spec:** no UI, interaction, assistive-technology, background-work, storage, memory, CPU, GPU, concurrency, or hardware claim changes.
- **Data, migration, recovery, and support:** no persistent data, schema, migration, backup, rollback, recovery, lifecycle, diagnostic, or support artifact changes.
- **Project vacuum:** no sibling repository, source, runtime, data, schema, adapter, service, dependency, fixture, profile, or acceptance edge is introduced.
- **Release interface:** no field in `RELEASE_INTERFACE.md` gains implementation or verification evidence; only its bundle metadata is synchronized.
- **Licensing and redistribution:** no dependency, catalogue identity, artefact, licence proposal, installation right, or redistribution right changes.

## Graph, compatibility, rollout, and history

The 91-task dependency graph and every milestone gate remain unchanged. Compatibility is documentary: no code, API, database, package, or release consumer observes the ADR filename before TL-0401 creates it.

Rollout is atomic in bundle 0.3.1: update the authority note, task path, human-evidence wording, bundle metadata, navigation, validator regression, changelog, status, and manifest together. Validation must reject the former future path, any reuse of ADR 0004 by TL-0401, and any second task-contract reservation of ADR 0009.

Before TL-0401 evidence exists, rollback is a complete revert of the 0.3.1 amendment. Partial rollback is unsafe because it can restore the ambiguity. After evidence cites ADR 0009, history must not be rewritten; any later renumbering requires another approved supersession record while preserving prior references.

The former `docs/adr/ADR-004-winget-backend.md` string identifies superseded unexecuted contract text only. No file, spike result, maintainer approval, release artifact, or runtime evidence ever existed at that path.

## Synchronized documents, validation, and owners

Bundle 0.3.1 synchronizes `DECISIONS.md`, `ROADMAP.md`, `TASKS.yaml`, `STATUS.md`, `README.md`, `CHANGELOG.md`, active bundle-version headers, `tools/validate_bundle.py`, its regression tests, and `BUNDLE_MANIFEST.sha256`. The bundle validator owns the exact ADR 0004/0009 reservation and path guard; the repository Quick tier owns the complete static verification. This narrow amendment does not establish a portfolio-wide ADR registry or allocate numbers for unrelated future tasks.

Janne Vuorela owns this numbering amendment. A TL-0401 maintainer still owns the later spike review and backend-selection approval, including all existing package, security, licence, environment, and verification limitations.
