# ADR 0008 — Minimal release-interface envelope

## Status and authority

**Status:** Accepted for `TL-0009`.

**Task:** [`TL-0009`](../../TASKS.yaml)

This ADR records existing binding decisions as planned architecture constraints. It does not amend [`DECISIONS.md`](../../DECISIONS.md) or [`PROJECT_BOUNDARY.md`](../../PROJECT_BOUNDARY.md), and it is not evidence that the planned behavior is implemented or verified.

## Decision IDs

- [D-047](../../DECISIONS.md) — Standalone release independence
- [D-048](../../DECISIONS.md) — Project-vacuum development
- [D-049](../../DECISIONS.md) — Late-binding suite assembly
- [D-050](../../DECISIONS.md) — No early shared integration infrastructure
- [D-052](../../DECISIONS.md) — Minimal release interface envelope
- [D-053](../../DECISIONS.md) — Portfolio data ownership
- [D-058](../../DECISIONS.md) — Portfolio v2.1 development baseline
- [D-059](../../DECISIONS.md) — Active Codex machine is the only physical validation hardware
- [D-061](../../DECISIONS.md) — Quick, targeted, full, and extended test tiers
- [D-063](../../DECISIONS.md) — GitHub continuity and no remote-runtime dependency
- [D-066](../../DECISIONS.md) — Hardware and resource claims are limited to observed evidence

## Context

A later deployment or support owner needs ordinary black-box facts about a frozen standalone application: what artifact it is, how it installs and launches, what data it owns, how it behaves offline, how it is removed, what evidence exists, and what remains unsupported. That need does not justify a shared runtime API, private database access, or speculative compatibility contract during B1.

Many release facts are not implemented yet. Filling them early would convert design intent into an unsupported promise and could bind future work to guessed paths, commands, resource limits, or artifacts.

## Decision

ThirdLife Setup Core maintains the human-readable [`RELEASE_INTERFACE.md`](../../RELEASE_INTERFACE.md) as the minimal black-box release envelope. It covers only implemented and verified standalone facts for:

- product identity, supported platform, publisher, version, licence, maintenance state, artifact, hash, signature/development label, source revision, dependency lock, SBOM, and notices;
- install, update, repair, restart, rollback/non-rollback, removal, data preservation, and data left behind;
- normal interactive launch, independently useful command-line options, and supported ordinary file behavior;
- configuration, policy/profile/catalogue, database, attachment, cache, temporary, log, report, support, migration, and backup locations with retention/deletion ownership;
- validated inputs, audience-specific outputs, whether data is referenced/copied/transformed/omitted, and non-sensitive samples with hashes;
- offline core behavior and each explicit network category;
- privilege boundaries, ephemeral broker behavior, accepted untrusted inputs, provenance, secret handling, support preview, and known security limitations;
- active reference-machine observations, workload hashes, test tiers, same-machine constraints, measured resource behavior, skipped scenarios, accessibility evidence, and exact limits on hardware claims;
- source continuity, clean-clone instructions, external-asset restoration, support/security contacts, and known limitations.

At `TL-0610`, verified pilot behavior may be entered while the sheet remains explicitly preview/incomplete. At `TL-0706`, the sheet is completed for the exact frozen Core 1.0 candidate and is frozen with the release evidence before `TL-0710`. Until evidence exists, a field remains `TBD`, `not yet verified`, or `Not supported` with a reason. TL-0009 does not populate speculative values.

The release interface is documentation, not an API, SDK, plugin contract, machine-readable shared schema, private-state permission, adapter specification, compatibility promise, or authorization for B4 work. It never requires access to the job database, private classes, logs outside the support contract, sibling data, or an active development branch.

GitHub records source continuity, but no remote runtime runner is authoritative. Runtime evidence remains tied to the active Codex machine and named hosted environments, constraints, commands, durations, fixtures/workloads, and results. VM, constraint, fixture, and one-machine observations are not cross-hardware certification.

## Alternatives considered

- **Define a shared runtime API or universal handoff schema:** rejected because later black-box deployment needs release facts, not a speculative coupled protocol.
- **Permit private database access:** rejected because internal schemas are not stable public behavior and contain restricted Core-owned state.
- **Complete the sheet from roadmap intent:** rejected because guessed paths, commands, artifacts, limits, and behaviors would be false release evidence.
- **Publish no lifecycle or data documentation:** rejected because a standalone product must be installable, repairable, removable, recoverable, and supportable without developer knowledge.
- **Use “latest” or an active branch as the integration identity:** rejected because later compatibility must bind to an exact frozen release and hashes.

## Consequences

- Release and support owners receive a bounded, reviewable black-box handoff without expanding the runtime attack or data surface.
- The sheet evolves only when owning tasks implement and verify facts; documentation maintenance and evidence traceability are part of release work.
- Unsupported and unknown behavior stays visible, which may limit early automation but prevents invented compatibility claims.
- A future B4 project may use the frozen sheet, artifact, hashes, public documentation, limitations, and samples, but owns all adapter and compatibility decisions separately.
- Hardware, accessibility, security, offline, lifecycle, and resource wording is limited to the exact recorded evidence and cannot be generalized from a simulation or one physical machine.

## References

- [Minimal release interface sheet](../../RELEASE_INTERFACE.md)
- [Canonical project boundary](../../PROJECT_BOUNDARY.md)
- [Development and GitHub continuity workflow](../../DEVELOPMENT_WORKFLOW.md)
- [Testing strategy and evidence contract](../../TESTING.md)
