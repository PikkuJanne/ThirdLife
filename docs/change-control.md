# ThirdLife Setup Core — Change Control

The [product contract](product-contract.md) defines current outcomes, invariants and delegated authority. [Project ownership](../PROJECT_BOUNDARY.md), [security](../SECURITY.md), [privacy](privacy/privacy-model.md), [accessibility](../ACCESSIBILITY.md) and [resource](../LOW_SPEC.md) specifications govern their affected behavior. [AGENTS.md](../AGENTS.md), the [development workflow](../DEVELOPMENT_WORKFLOW.md) and [testing strategy](../TESTING.md) govern execution and evidence. [TASKS.yaml](../TASKS.yaml) is the current work and state source. [ROADMAP.md](../ROADMAP.md) describes delivery cuts, [STATUS.md](../STATUS.md) is a handoff, and the [decision register](../DECISIONS.md) and ADRs preserve rationale and supersession.

## Engineering within the contract

Codex may revise implementation interfaces, priorities, sequencing, task decomposition, and dependencies as warranted by the product outcome. Refine how acceptance is tested while preserving promised behavior and protections. Preserve existing IDs and completed history. If splitting an unfinished outcome, keep the original obligation traceable and record what evidence is still owed. Keep schemas, fixtures, selection, validation and runner rules aligned with the current graph. Verify unique references and acyclicity.

An implementation dependency is a component, interface or data required to build another component. An execution prerequisite applies at the affected action, and a release prerequisite applies to a defined product claim or exposure. Evidence from an isolated integration does not make a blocked capability available. Retain any relevant release limitation and request actual consent only before the action for which it is required.

An ADR may document alternatives, a selected engineering route and its limitations. It gains authority through the product contract and relevant specification, implementation evidence and review.

## Material product or authority change

A material reduction in promised functionality, changed product boundary or supported platform, materially different trust model, new commercial/licensing obligation, actual exposure or portfolio transition requires its actual scope-bound decision. Stop before the conflicting implementation or exposure. Continue authorized work with unrelated risks contained.

Record the approving owner, date, affected outcome/specifications, rationale, evidence and compatibility or recovery implications. Synchronize the affected documents, task records, schemas and validators. Give historical decisions explicit supersession and preserve completed evidence with its original provenance. Assign a bundle version when migrating the governing contract. This is not required for ordinary contract-preserving engineering.

## Checkpoint review

- Check scope, source integrity, compatibility, real prerequisites and evidence. Regenerate `BUNDLE_MANIFEST.sha256` through the existing mechanism.
- Run `eng/verify.ps1` with the applicable tier. Keep package trust, privilege, licensing, privacy, security, accessibility, resource and data safeguards in force. Record the result and any remaining real gate.
- In a scoped checkpoint, confirm every unfinished capability remains accurately shown and any unqualified dispatch stays unreachable. Preserve prior history, source references, actual observations and known limits.
- Use `RELEASE_INTERFACE.md` for verified preview/frozen facts. An untested field stays explicit, and an implementation checkpoint alone conveys no release authorization.
- Put cross-project ideas in `FUTURE_ASSEMBLY_NOTES.md`. An entry creates no B1 requirement and includes a manual fallback for future B4 work.
