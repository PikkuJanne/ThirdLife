# ADR 0007 — Standalone product and late-binding boundary

## Status and authority

**Status:** Accepted for `TL-0009`.

**Task:** [`TL-0009`](../../TASKS.yaml)

This ADR records existing binding decisions as planned architecture constraints. It does not amend [`DECISIONS.md`](../../DECISIONS.md) or [`PROJECT_BOUNDARY.md`](../../PROJECT_BOUNDARY.md), and it is not evidence that the planned behavior is implemented or verified.

## Decision IDs

- [D-046](../../DECISIONS.md) — Portfolio component and queue position
- [D-047](../../DECISIONS.md) — Standalone release independence
- [D-048](../../DECISIONS.md) — Project-vacuum development
- [D-049](../../DECISIONS.md) — Late-binding suite assembly
- [D-050](../../DECISIONS.md) — No early shared integration infrastructure
- [D-051](../../DECISIONS.md) — Future adapter ownership and integration order
- [D-053](../../DECISIONS.md) — Portfolio data ownership
- [D-054](../../DECISIONS.md) — Cross-project ideas are backlog only
- [D-055](../../DECISIONS.md) — Generic Core catalogue and profiles
- [D-056](../../DECISIONS.md) — Core 1.0 stable gate and portfolio handoff

## Context

ThirdLife Setup Core is the active Team B/B1 project. It must deliver a useful standalone refurbishment workflow while other portfolio products may develop independently. Designing against sibling branches, private data, speculative schemas, or synchronized release schedules would make Core incomplete on its own and turn integration into an unowned parallel project.

The shared `ThirdLife.*` code identity permits a future product family to extend the same naming convention; it does not establish a shared host, runtime, database, SDK, or B1 integration layer.

## Decision

B1 is developed and released in a project vacuum:

- Core has no source, binary, runtime, service, process, data, private API, schema, fixture, test, branch, release-schedule, or acceptance dependency on a sibling portfolio product.
- Core catalogue entries, profiles, commands, file associations, fixtures, and acceptance tests use generic public free essentials or project-created synthetic packages. They contain no sibling-specific behavior.
- B1 creates no shared SDK, universal job/findings/handoff schema, plugin framework, monorepo requirement, portfolio background service, shared content store, or speculative extension point whose only purpose is possible future integration.
- Core owns only its device-support jobs, configuration snapshots, actions, reports, lifecycle, support, and release metadata. It does not discover, index, reference, copy, convert, export, retain, delete, or expose sibling-private content or credentials.
- An interface or command is considered only when it has a complete independently useful Core outcome for users, testing, automation, or support.
- A cross-project idea is recorded only in [`FUTURE_ASSEMBLY_NOTES.md`](../../FUTURE_ASSEMBLY_NOTES.md) with a manual fallback and reason it belongs to B4. The note creates no B1 code, dependency, acceptance criterion, task edge, compatibility promise, or release blocker.

After the standalone stable gate `TL-0710`, Team B proceeds to Scam Explainer. A future formally active Team B/B4 ThirdLife Deployment and Suite Assembly project may consume only exact frozen releases, cryptographic hashes, public documentation, a completed human-readable release-interface sheet, known limitations, and non-sensitive samples.

B4 owns sibling-specific catalogue entries, profiles, file associations, compatibility cuts, deployment media, and adapters. Its default sequence is install, launch, open a user-selected standard file or documented workspace through supported behavior, and show human guidance before considering custom code. Any adapter remains optional, version-bounded, independently disableable, privacy-reviewed, and paired with a manual fallback. It never requires private database access or makes standalone Core operation depend on integration.

## Alternatives considered

- **Integrate against sibling active branches during B1:** rejected because their behavior is mutable and would create continuous cross-team coordination and release coupling.
- **Create a universal shared schema or SDK now:** rejected because requirements from at least two frozen stable products do not yet exist and the shared component would be an unowned third project.
- **Add empty plugin or adapter extension points:** rejected because speculative surfaces increase security and maintenance burden without an independent Core outcome.
- **Delay Core until sibling products are ready:** rejected because Core must be independently useful and its release may not be blocked by another team.
- **Read sibling private stores directly:** rejected because private implementation state is not a public integration contract or Core-owned data.

## Consequences

- Core can build, test, release, update, repair, remove, recover, and support independently.
- B1 may duplicate small project-local contracts that a later B4 project could otherwise have shared; B4 evaluates consolidation only from evidence of stable products.
- Future B4 compatibility work is explicit black-box work against exact versions and may need narrow adapters or manual guidance after Core is frozen.
- Cross-project opportunities remain visible without expanding the current task graph or inspecting sibling repositories.
- Boundary violations block the relevant portfolio review or stable release; technical convenience is not approval to cross the boundary.

## References

- [Canonical portfolio boundary](../../PROJECT_BOUNDARY.md)
- [Binding development roadmap](../../ROADMAP.md)
- [Non-goals](../non-goals.md)
- [Non-binding future assembly notes](../../FUTURE_ASSEMBLY_NOTES.md)
