# ThirdLife Setup Core — Product Contract

**Status:** Current product contract  
**Active project:** Team B / B1 — ThirdLife Setup Core

This contract defines the product outcomes, scope, invariants and engineering authority. The linked specifications supply requirements for affected behavior. [Change control](change-control.md) explains when an engineering choice needs a separate decision.

## Product and users

**ThirdLife** is the product family, repository, solution and `ThirdLife.*` code identity. **ThirdLife Setup Core** is the active standalone project and user-facing release. It remains useful, installable, updateable, repairable, removable, recoverable and supportable without a sibling application, mandatory account, project-controlled server or permanent privileged service.

The primary operator is a volunteer or staff refurbisher. A present recipient participates only in explicitly recipient-controlled setup. Where a supported workflow permits an authorized organization to act, its authority and recovery ownership must be explicit. Personal choices stay with the recipient.

Windows 11 client on native x64 is the only supported application host and target for new live assessment, preparation, remediation and handover. Application entry points check structured Windows version, client product type, recognized build and native architecture before live work. Uncertain identity fails closed. Preparation additionally requires current reviewed servicing and policy evidence. Stale servicing evidence can leave prior records accessible without permitting preparation. Existing records remain readable with their original platform observations, policy provenance and decisions.

## Required workflow

1. **Intake.** Record the method, operator, date, bounded media identifier, result, verification and policy version for donor storage sanitized externally, replaced or absent. Start from a fresh or known Windows installation. Unknown or failed sanitization blocks preparation.
2. **Inspect.** Gather bounded read-only observations and guided human tests. Keep observed, inferred, unavailable and human-confirmed evidence distinguishable. Record source, time, availability and provenance. Each evidence fact has one semantic owner or an explicit deterministic reconciliation rule.
3. **Decide.** Evaluate immutable evidence against attributable, versioned organization policy. Cite the controlling rules and supporting or missing evidence for each disposition. Current dispositions are **Ready to prepare**, **Repair and retest**, **Human review required** and **Do not deploy**. Allowed exceptions carry operator identity, reason, policy authorization and timestamp. Show them in the workshop output. Prohibited rules cannot be overridden through an exception. Preserve older dispositions with their original policy. Unknown, stale, contradictory or unrecognized lifecycle data cannot satisfy a support rule.
4. **Prepare.** Show the complete resolved plan with actions, data touched, privilege, network, disk, restart, rollback and verification expectations. Execute supported setup operations through the normal application after approval binds the exact plan. Reapproval is required after material changes. Instructions, Settings links and checklists can support the workflow. A promised automated operation requires actual execution at the approved scope.
5. **Verify.** Record a correlated durable dispatch intent before executing and maintain recoverable state across cancellation, interruption and restart. Keep applied and verified distinct. Only an independent observation of the approved postcondition establishes verification. Final acceptance includes the actual required cold boot and fresh re-verification. Development evidence from a same-machine VM retains its own scope. Essential failure, missing evidence and ambiguous state block readiness as applicable.
6. **Handover.** Require applicable policy, essential verification and blocking finalization. Check for workshop accounts, credentials, Wi-Fi profiles, test media, diagnostic exports, package residue and remote-support access. Report what was checked or removed. Produce a technical workshop record and a plain-language recipient guide that agree with saved facts and disclose limitations. A previewable sanitized support bundle follows a separate allowlist. Core makes no device security, sanitization, reliability or universal hardware certification claim.

## Product invariants

- **Privilege and execution.** Keep the WPF UI unelevated. Use supported Windows, WinGet or vendor mechanisms for suitable approved operations. Use the short-lived, independently validating, typed, allowlisted broker for operations that require ThirdLife-owned privileged code. Unavailable capabilities block only affected actions. Preserve ordinary Windows consent and any other authorization that actually applies. A supported installer may obtain elevation through Windows. Neither elevation nor process success proves an install.
- **Package trust.** Bind exact package identity, reviewed source and publisher, selected version and installer, architecture, requested scope, licence and applicable provenance to the approval. Recheck material metadata before use and preserve signature/hash verification. Keep read-only resolution and independent installed-state queries available without the broker when the backend supports them. Profiles, catalogue entries and policy are bounded declarative data. Compiled actions accept typed inputs and cannot take arbitrary commands, paths, URLs, registry keys, installer arguments or unrestricted file operations. Core 1.0 excludes WinGet Configuration and profile-supplied PowerShell DSC. Use structured Windows Update APIs for update orchestration and preserve restart, cancellation and resume behavior.
- **Data and recovery.** Use local SQLite history and bounded access-restricted attachments. Version migrations and preserve source facts, policy, action history, approval and verification evidence. Preflight disk and rollback headroom where relevant. Recovery reconciles actual state before a retry. Job management, assessment, policy, existing record access and reports remain local-first. Show catalogue update, software download, Windows Update and optional application update network categories explicitly. Telemetry is off by default. Pilot measurements use explicit aggregated or pseudonymized exports.
- **Architecture.** Maintain the C#/.NET/WPF application. Keep domain contracts independent of WPF, SQLite, OS/package infrastructure and localized output. Providers return typed normalized observations. Use replaceable, narrow adapters and the existing plan, approval, journal and verification seams for execution work.
- **Privacy, accessibility and resources.** Keep Core-owned records and audience-specific outputs within their approved privacy scopes. Recipient identity is optional. Restrict necessary full serials or operator attribution to approved workshop fields. Keep credentials, recovery material and private content out of ordinary outputs. Provide keyboard operation, named controls, accessible progress, clear errors, cancellation and recovery. Require no GPU. Bound startup work, memory, storage, output, retry and concurrency.
- **Project vacuum.** B1 uses generic public free essentials and synthetic packages. Keep a small reviewed catalogue that records publisher, licence, privacy, architecture, storage, background and update behavior, verification and redistribution status. Do not inspect, import or depend on a sibling repository, active branch, service, database, schema, fixture or release schedule. Defer sibling catalogue entries, profiles, adapters, compatibility cuts and suite media to B4. Record useful ideas in `FUTURE_ASSEMBLY_NOTES.md`.

## Delivery and accountability

| Delivery cut | Meaning |
|---|---|
| M4 / `TL-0411` | One real machine-wide, approved package transaction through the application with independent verification and persisted job/report continuity. |
| M0 through M6 / `TL-0611` | Scope-bound controlled v0.1 partner pilot with actual exposure authorization. |
| M7 / `TL-0710` | Accountable gate for independently releasable ThirdLife Setup Core 1.0 and the only Team B/B1 exit. |
| After `TL-0710` | Team B proceeds to **Scam Explainer**. |
| Future Team B / B4 | **ThirdLife Deployment and Suite Assembly** may consume exact frozen releases, public documentation, hashes and non-sensitive samples. |

Core 1.0 includes recipient-controlled accessibility setup and basic operating-system backup onboarding with destination/capacity checks, an initial backup and harmless restore verification where supported. Claim backup configuration only after the representative restore succeeds. Sealed handover defers recipient-specific choices. Personal account creation, recovery-key custody, a custom backup engine and control of sibling backup data are excluded. Encrypt only with an approved recovery-ownership plan. See [project ownership](../PROJECT_BOUNDARY.md) and [non-goals](non-goals.md).

Codex can split and resequence implementation, revise real implementation dependencies and interfaces, choose compliant supported routes, refine acceptance and tests, repair tooling, and prepare or integrate verified isolated checkpoints while affected execution or exposure stays gated. Preserve completed work, evidence and history. Material loss of a promised outcome, a materially different trust model, new commercial/licensing obligations and external exposure require applicable scope-bound authority.

Codex owns implementation verification and evidence with zero routine owner QA. Use the active Codex machine for authoritative runtime, security, resource and accessibility evidence. Keep fixtures, actual observations, unobserved behavior and decisions distinct. Remote CI is supplementary.

## Specifications and progress

- [Security](../SECURITY.md), [privacy](privacy/privacy-model.md), [logging](privacy/logging-standard.md) and [supply chain](supply-chain/dependencies.md) define the applicable trust, provenance, data and output controls.
- [Accessibility](../ACCESSIBILITY.md) and [modest hardware](../LOW_SPEC.md) define the behavior and evidence required at the affected surfaces.
- [Testing](../TESTING.md) and [development workflow](../DEVELOPMENT_WORKFLOW.md) define verification and integration practice.
- [TASKS.yaml](../TASKS.yaml) records work, dependencies, prerequisites, evidence and limitations. [STATUS.md](../STATUS.md) summarizes the current continuation. The [roadmap](../ROADMAP.md) describes delivery cuts. [Decisions](../DECISIONS.md) and ADRs preserve rationale.
- [RELEASE_INTERFACE.md](../RELEASE_INTERFACE.md) contains verified preview or frozen release behavior. Leave unverified fields explicit.
