# ADR 0001 — Windows and WPF stack

## Status and authority

**Status:** Accepted architecture rationale from `TL-0009`.

**Task:** [`TL-0009`](../../TASKS.yaml)

The [current product contract](../product-contract.md) and applicable specifications govern implementation. See the linked task evidence for the verified scope.

## Decision IDs

- [D-027](../../DECISIONS.md) — Desktop stack
- [D-039](../../DECISIONS.md) — Accessibility baseline and recipient control
- [D-058](../../DECISIONS.md) — Portfolio v2.1 development baseline
- [D-059](../../DECISIONS.md) — Active Codex machine is the only physical validation hardware
- [D-060](../../DECISIONS.md) — Modest-hardware readiness is engineered, not lab-certified
- [D-061](../../DECISIONS.md) — Quick, targeted, full, and extended test tiers
- [D-063](../../DECISIONS.md) — GitHub continuity and no remote-runtime dependency
- [D-066](../../DECISIONS.md) — Hardware and resource claims are limited to observed evidence

## Context

ThirdLife Setup Core is a Windows-specific refurbishment workflow for supported Windows 11 x64. It needs structured Windows integration and an operator interface that can expose UI Automation while keeping domain rules testable without WPF, package backends, persistence, or sibling products. The repository already contains the production assembly scaffold in [`ThirdLife.sln`](../../ThirdLife.sln); at `TL-0009`, it has no production-to-production `ProjectReference` edges, and only `ThirdLife.UI` enables WPF.

The initial project graph must guide later implementation without creating an integration host, shared portfolio layer, or speculative reference solely for future use.

## Decision

ThirdLife Setup Core uses C#, .NET 10, WPF, and structured Windows APIs behind typed adapters.

The dependency direction is inward:

- `ThirdLife.Core` is the innermost domain boundary for job, evidence, decision, action, verification, finalization, and handover contracts. It does not depend on WPF, SQLite, WinGet, PowerShell, localized command output, Windows-provider implementations, or sibling products.
- Portable contract and domain assemblies such as `ThirdLife.Policy`, `ThirdLife.Catalog`, `ThirdLife.Actions`, and `ThirdLife.Broker.Protocol` remain free of WPF and infrastructure implementations. Add production references only for actual component dependencies.
- Infrastructure assemblies such as `ThirdLife.Persistence`, `ThirdLife.Inventory`, `ThirdLife.Packages`, `ThirdLife.Verification`, `ThirdLife.Diagnostics`, and `ThirdLife.Reports` implement or call inward-facing contracts. Their concrete types do not leak back into Core.
- `ThirdLife.UI` is the only WPF production assembly. It is an unelevated composition root that presents bounded domain and service state. Keep policy explicit and elevated code outside the UI.
- `ThirdLife.Broker` is a separate ephemeral elevated host. It may share the portable `ThirdLife.Broker.Protocol` contract with an unelevated client, but `ThirdLife.UI` and `ThirdLife.Broker` have no production assembly reference or in-process/runtime-loading dependency on each other. Packaging may include both artifacts; their runtime interaction remains only the authenticated protocol.
- Production code never references test projects, project references remain acyclic, and no assembly is added merely as a future extension point or portfolio integration layer.

Independent supported package execution follows its own qualified route. ThirdLife-owned privileged code retains the separate broker boundary.

WPF selection carries the complete keyboard, focus, names/roles/states, screen-reader announcement, scaling, high-contrast, cancellation, and recovery obligations in [`ACCESSIBILITY.md`](../../ACCESSIBILITY.md). Runtime design requires no GPU and follows the bounded-resource and claim limits in [`LOW_SPEC.md`](../../LOW_SPEC.md). All verification runs on the active Codex machine under [`TESTING.md`](../../TESTING.md), while GitHub remains the source-continuity record under [`DEVELOPMENT_WORKFLOW.md`](../../DEVELOPMENT_WORKFLOW.md).

## Alternatives considered

- **WinUI, MAUI, Electron, or a browser-hosted UI:** not selected because the desktop-stack decision chooses WPF and its established Windows/UI Automation boundary.
- **An elevated monolithic desktop process:** rejected because it would keep the entire operator interface privileged and erase the explicit broker boundary.
- **Infrastructure types directly in Core:** rejected because SQLite, WinGet, WPF, provider, or shell dependencies would make domain behavior harder to test and would reverse the required dependency direction.
- **A shared suite host or plugin layer:** rejected because B1 is a standalone project vacuum and has no present portfolio integration requirement.

## Consequences

- Windows-specific adapter and UI work is explicit, while domain and protocol behavior can be tested with deterministic fixtures.
- More assemblies and interfaces require deliberate composition and reference review, but they keep platform, privilege, and storage concerns from becoming domain dependencies.
- WPF does not itself prove accessibility; later UI tasks must produce the applicable technical evidence and record any unobserved behavior.
- Same-machine tests and resource constraints can prove only the named behavior and environment. They do not establish cross-hardware certification or an unmeasured minimum specification.
- A project-reference change must serve an actual component dependency, point inward, keep the graph acyclic, and pass repository validation.

## References

- [Delivery roadmap](../../ROADMAP.md#7-target-architecture-and-dependency-direction)
- [Canonical project boundary](../../PROJECT_BOUNDARY.md)
- [Accessibility requirements](../../ACCESSIBILITY.md)
- [Modest-hardware engineering contract](../../LOW_SPEC.md)
- [Testing strategy](../../TESTING.md)
