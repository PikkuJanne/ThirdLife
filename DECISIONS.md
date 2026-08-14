# ThirdLife Setup Core — Frozen Decision Register

**Bundle version:** 0.2.0  
**Generated:** 2026-08-14  
**Status:** Binding for the Team B/B1 ThirdLife Setup Core roadmap

## Purpose and authority

This register freezes the product, portfolio boundary, architecture, safety, privacy, accessibility, low-spec, and delivery choices that Codex must treat as constraints rather than suggestions. This 0.2.0 revision was approved before implementation began to align the project with the portfolio's standalone-first, late-binding architecture.

When implementation pressure conflicts with a decision here, stop, record the conflict, and request an explicit human decision. Do not silently reinterpret a frozen decision.

Authority precedence for this bundle is:

1. `DECISIONS.md`
2. `ROADMAP.md`
3. `PROJECT_BOUNDARY.md`
4. `SECURITY.md`
5. `ACCESSIBILITY.md`
6. `LOW_SPEC.md`
7. `AGENTS.md`
8. `TASKS.yaml`
9. `CODEX_START_PROMPT.md`
10. `README.md`

A decision may be changed only by a human-approved amendment that:

- keeps the superseded decision/history visible;
- records the new decision, rationale, approver, and date;
- identifies affected tasks, tests, schemas, reports, release-interface fields, and threat/privacy assumptions;
- identifies any effect on the Team B queue or future B4 assembly; and
- updates every conflicting bundle artifact in the same change.

Codex may propose an amendment, but it may not declare one approved.

## Index

| ID | Decision |
|---|---|
| [D-001](#d-001) | Project and component identity |
| [D-002](#d-002) | Product outcome |
| [D-003](#d-003) | Primary Core operator |
| [D-004](#d-004) | Starting-state boundary |
| [D-005](#d-005) | Operating-system target |
| [D-006](#d-006) | Pilot and standalone release cuts |
| [D-007](#d-007) | Sanitization gate |
| [D-008](#d-008) | OS installation and imaging |
| [D-009](#d-009) | No compatibility, ownership, or activation bypasses |
| [D-010](#d-010) | No optimizer scope |
| [D-011](#d-011) | Local-first data model |
| [D-012](#d-012) | Network categories |
| [D-013](#d-013) | Telemetry default |
| [D-014](#d-014) | Identity and minimization |
| [D-015](#d-015) | Evidence semantics |
| [D-016](#d-016) | No aggregate health score |
| [D-017](#d-017) | Disposition vocabulary |
| [D-018](#d-018) | Facts, policy, and decisions are separate |
| [D-019](#d-019) | Explicit exceptions |
| [D-020](#d-020) | Outcome-based profiles |
| [D-021](#d-021) | Recipient-controlled settings and secrets |
| [D-022](#d-022) | Curated application catalog |
| [D-023](#d-023) | Profiles are non-executable data |
| [D-024](#d-024) | Structured WinGet integration |
| [D-025](#d-025) | Package trust controls |
| [D-026](#d-026) | No WinGet Configuration in Core 1.0 |
| [D-027](#d-027) | Desktop stack |
| [D-028](#d-028) | Persistence |
| [D-029](#d-029) | Privilege separation |
| [D-030](#d-030) | Broker protocol |
| [D-031](#d-031) | No always-on privileged service |
| [D-032](#d-032) | Action journal and completion |
| [D-033](#d-033) | Separate verification and cold boot |
| [D-034](#d-034) | Windows Update integration |
| [D-035](#d-035) | Finalization is blocking |
| [D-036](#d-036) | Three output classes |
| [D-037](#d-037) | Diagnostic redaction |
| [D-038](#d-038) | Encryption recovery ownership |
| [D-039](#d-039) | Accessibility baseline and recipient control |
| [D-040](#d-040) | Basic operating-system backup scope |
| [D-041](#d-041) | No AI in Core 1.0 |
| [D-042](#d-042) | Offline degradation and deferred suite cache |
| [D-043](#d-043) | Dependency and licensing control |
| [D-044](#d-044) | Additional Core 1.0 non-goals |
| [D-045](#d-045) | Authority and change control |
| [D-046](#d-046) | Portfolio component and queue position |
| [D-047](#d-047) | Standalone release independence |
| [D-048](#d-048) | Project-vacuum development |
| [D-049](#d-049) | Late-binding suite assembly |
| [D-050](#d-050) | No early shared integration infrastructure |
| [D-051](#d-051) | Future adapter ownership and integration order |
| [D-052](#d-052) | Minimal release interface envelope |
| [D-053](#d-053) | Portfolio data ownership |
| [D-054](#d-054) | Cross-project ideas are backlog only |
| [D-055](#d-055) | Generic Core catalogue and profiles |
| [D-056](#d-056) | Core 1.0 stable gate and portfolio handoff |
| [D-057](#d-057) | Core 1.0 accessibility and backup completion boundary |

## Decisions

### D-001 — Project and component identity

**Decision.** The product family, repository, solution, and production namespaces use the identity ThirdLife. The active Team B/B1 project and user-facing release name are ThirdLife Setup Core.

**Rationale.** The current standalone component needs a clear project boundary while retaining the stable `ThirdLife.*` code identity that the later B4 host may extend.

**Implementation constraint.** Use `ThirdLife.*` namespaces and `ThirdLife.sln`; identify the active product as ThirdLife Setup Core in roadmap, UI, release, and support text. Do not scaffold a speculative integration product.

**Revisit trigger.** Only after an explicit human-approved naming, legal, or portfolio-architecture decision before public release.

### D-002 — Product outcome

**Decision.** ThirdLife Setup Core is a local-first, auditable refurbishment workflow that assesses a sanitized device, applies an approved plan, verifies the outcome, and produces a clear handover.

**Rationale.** The deliverable is a policy-backed, verified device state rather than a collection of cleanup actions or a shell around sibling products.

**Implementation constraint.** Every feature must support Intake, Inspect, Decide, Prepare, Verify, Handover, recipient-controlled Core accessibility/backup onboarding, or the standalone release lifecycle.

**Revisit trigger.** Only through a new product charter approved by the project and portfolio owners.

### D-003 — Primary Core operator

**Decision.** The primary operator is a volunteer or staff refurbisher preparing donated Windows computers for recipients. A present recipient participates only in explicitly recipient-controlled setup.

**Rationale.** This gives the first project one operational context and one accountable workshop operator while preventing volunteers from choosing personal settings or secrets on a recipient's behalf.

**Implementation constraint.** Coordinator and recipient experiences are secondary; fleet administration and sibling-app workflows are not Core 1.0 goals.

**Revisit trigger.** After the standalone Core 1.0 release and a separately approved product milestone.

### D-004 — Starting-state boundary

**Decision.** v0.1 assumes sanitized, replaced, or absent donor storage and a fresh or known Windows installation. Existing personal-PC repair or cleanup is out of scope.

**Rationale.** Unknown user data, malware, licenses, encryption, and configuration make an existing-PC workflow materially riskier.

**Implementation constraint.** Do not add preservation, malware-removal, migration, or unknown-application cleanup behavior.

**Revisit trigger.** Only as a separately threat-modeled, initially read-only product mode.

### D-005 — Operating-system target

**Decision.** The v0.1 preparation target is supported Windows 11 x64. Windows 10 may be audited and dispositioned but cannot reach the normal ready state.

**Rationale.** Deployment must not normalize an unsupported operating-system state.

**Implementation constraint.** Eligibility evidence must remain explicit; unsupported Windows installations are never represented as supported.

**Revisit trigger.** When Microsoft lifecycle policy or a separately approved platform track changes the target.

### D-006 — Pilot and standalone release cuts

**Decision.** Milestones M0 through M6 produce the controlled v0.1 partner pilot. Milestone M7 completes and freezes ThirdLife Setup Core 1.0 as the independently releasable Team B/B1 product.

**Rationale.** The pilot proves the core refurbishment architecture, but the portfolio requires a stable standalone release, basic accessibility and operating-system backup onboarding, lifecycle hardening, and a release-interface sheet before Team B leaves B1.

**Implementation constraint.** Passing `TL-0611` does not complete B1 or authorize suite integration. B1 completes only at human gate `TL-0710`.

**Revisit trigger.** Only by an approved roadmap revision that preserves completed milestone evidence and records the effect on the Team B queue.

### D-007 — Sanitization gate

**Decision.** Media sanitization is an external prerequisite recorded as evidence. Unknown or failed sanitization blocks preparation.

**Rationale.** Sanitization is a separate high-risk program and should not be reduced to a generic live-Windows wipe command.

**Implementation constraint.** v0.1 records method, operator, date, media identifier, result, verification, and policy version; it does not erase donor data.

**Revisit trigger.** Only in a separately released component with its own threat model and validation program.

### D-008 — OS installation and imaging

**Decision.** Windows installation, imaging, Sysprep automation, and product-key installation are external prerequisites for v0.1.

**Rationale.** The first release focuses on assessment, controlled preparation, verification, and handover.

**Implementation constraint.** ThirdLife may record and verify the resulting state but must not silently absorb imaging responsibilities.

**Revisit trigger.** After v0.1 through a separate architecture and operational review.

### D-009 — No compatibility, ownership, or activation bypasses

**Decision.** ThirdLife must not bypass Windows requirements, activation, firmware passwords, MDM, Autopilot-style enrollment, anti-theft controls, or ownership restrictions.

**Rationale.** These are support, licensing, security, or ownership boundaries rather than cleanup tasks.

**Implementation constraint.** Detection produces a blocker or human review; no override implementation may be added.

**Revisit trigger.** Never for bypass behavior; only evidence providers may evolve.

### D-010 — No optimizer scope

**Decision.** ThirdLife is not a PC cleaner, optimizer, debloater, registry cleaner, driver-download utility, or general IT toolbox.

**Rationale.** These labels invite unsafe, unbounded, and difficult-to-verify behavior.

**Implementation constraint.** Reject features without a direct refurbishment acceptance or handover outcome.

**Revisit trigger.** Only by replacing the product charter, not by incremental scope creep.

### D-011 — Local-first data model

**Decision.** Jobs, observations, plans, journals, and reports are stored locally. Core inventory and policy evaluation work offline.

**Rationale.** Workshops need privacy, resilience, and operation under poor connectivity.

**Implementation constraint.** No recipient or device data is sent to a ThirdLife service.

**Revisit trigger.** A future optional service requires separate consent, privacy, and threat-model decisions.

### D-012 — Network categories

**Decision.** The application exposes network use as package-catalog update, software download, Windows Update, or optional self-update.

**Rationale.** Operators must understand why the device is using the network.

**Implementation constraint.** No hidden or catch-all network category; tests use fakes unless integration behavior is under test.

**Revisit trigger.** When a new approved network capability is introduced.

### D-013 — Telemetry default

**Decision.** Product telemetry is off by default. Pilot metrics use explicit partner exports containing aggregated or pseudonymized data.

**Rationale.** The project should not create a surveillance dependency for recipients or workshops.

**Implementation constraint.** No mandatory analytics SDK, account, device identifier, or background uploader.

**Revisit trigger.** Only with explicit opt-in design and privacy approval.

### D-014 — Identity and minimization

**Decision.** Recipient identity is not required for workshop jobs. Full serial numbers remain in the workshop record; support exports omit or truncate them.

**Rationale.** Device preparation should not unnecessarily spread personal or identifying data.

**Implementation constraint.** No names in log filenames, temporary paths, package commands, or ordinary diagnostic logs.

**Revisit trigger.** Only if an approved operational requirement cannot be met with an internal job identifier.

### D-015 — Evidence semantics

**Decision.** Evidence is explicitly classified as observed, inferred, not available, or human confirmed, with provider, timestamp, and provenance.

**Rationale.** Automated evidence has limits and must not be represented as certainty.

**Implementation constraint.** Missing data is unknown, never implicitly passed.

**Revisit trigger.** Only by extending the vocabulary without weakening provenance.

### D-016 — No aggregate health score

**Decision.** ThirdLife does not calculate or display a universal numeric device-health score.

**Rationale.** A score can hide a single critical blocker such as failing storage or unsupported software.

**Implementation constraint.** Show requirements, evidence, and dispositions instead.

**Revisit trigger.** Never for deployment decisions; analytics may use separate non-decisional metrics later.

### D-017 — Disposition vocabulary

**Decision.** The five dispositions are Ready to prepare, Repair and retest, Human review required, Alternative operating system candidate, and Do not deploy.

**Rationale.** These states are actionable without obscuring blockers.

**Implementation constraint.** Each disposition cites the evidence and policy rules that produced it.

**Revisit trigger.** Only through a versioned policy and report migration.

### D-018 — Facts, policy, and decisions are separate

**Decision.** Observed facts are immutable job evidence, organization policy is versioned input, and disposition is a reproducible evaluation result.

**Rationale.** Organizations may adopt different standards without rewriting machine facts.

**Implementation constraint.** Policy changes do not retroactively rewrite historical jobs.

**Revisit trigger.** Only if an equally auditable model replaces the separation.

### D-019 — Explicit exceptions

**Decision.** Permitted exceptions require operator identity, reason, policy authorization, timestamp, and visible report output.

**Rationale.** A blocker must not silently turn green.

**Implementation constraint.** Prohibited rules cannot be overridden by ordinary operators.

**Revisit trigger.** Only through policy-governance changes.

### D-020 — Outcome-based profiles

**Decision.** Profiles express user outcomes and split workshop capabilities from recipient choices. During B1 they use generic capabilities and do not name or require sibling portfolio applications.

**Rationale.** Outcome-based profiles allow package choices to change without coupling the profile to one application and keep recipient-specific decisions under recipient control.

**Implementation constraint.** Profiles reference reviewed catalogue capabilities and compiled actions, not raw installers, scripts, sibling identifiers, or private cross-project schemas.

**Revisit trigger.** Sibling-product profile entries are created only by the formal B4 suite-assembly project against frozen releases.

### D-021 — Recipient-controlled settings and secrets

**Decision.** Browser preference, cloud accounts, recovery material, backup keys, accessibility choices, and similar personal settings are configured with the recipient or left pending.

**Rationale.** The workshop must not own the recipient’s secrets or make personal choices by assumption.

**Implementation constraint.** Sealed handover performs machine-level setup only.

**Revisit trigger.** Only for an explicitly authorized organizational deployment model.

### D-022 — Curated application catalog

**Decision.** ThirdLife Setup Core exposes only a small reviewed catalogue. B1 development uses generic free essentials and synthetic test packages; sibling portfolio applications are not catalogue entries until B4.

**Rationale.** A narrow catalogue is supportable and prevents arbitrary package execution or premature live integration.

**Implementation constraint.** Every Core entry records publisher, licence, privacy, architecture, storage, background behavior, update behavior, verification, and redistribution status. Do not add PaperWorkShell-specific or other sibling-specific catalogue code in B1.

**Revisit trigger.** B4 may add exact frozen sibling releases through its own compatibility process.

### D-023 — Profiles are non-executable data

**Decision.** Profiles and policies cannot contain scripts, shell commands, arbitrary executables, registry paths, or URLs that become code.

**Rationale.** Executable behavior must remain in reviewed, compiled action implementations.

**Implementation constraint.** Unknown action types fail closed.

**Revisit trigger.** Never for v0.1; any future extension requires a new trust model.

### D-024 — Structured WinGet integration

**Decision.** Package operations use a replaceable structured adapter. v0.1 begins with a spike comparing Microsoft.WinGet.Client and the WinGet COM API; localized CLI tables are not parsed.

**Rationale.** Structured APIs provide safer progress, errors, and machine-readable metadata.

**Implementation constraint.** CLI invocation may exist only as a controlled fallback with structured or invariant output and tests.

**Revisit trigger.** After the spike or when Microsoft support boundaries materially change.

### D-025 — Package trust controls

**Decision.** Package actions pin approved source and exact package ID, record resolved publisher and version, preserve hash validation, and require reapproval after material resolution changes.

**Rationale.** The package source and resolved artifact are part of the approved plan.

**Implementation constraint.** Security-hash overrides and unexpected source substitution are impossible through the UI or profile.

**Revisit trigger.** Only to strengthen verification or support an equally reviewed backend.

### D-026 — No WinGet Configuration in Core 1.0

**Decision.** Core 1.0 does not execute WinGet Configuration or profile-provided PowerShell DSC resources.

**Rationale.** That path creates a wider module-download and execution boundary than the standalone Core release needs.

**Implementation constraint.** Use ThirdLife schemas and compiled allowlisted actions.

**Revisit trigger.** After a dedicated supply-chain and execution-boundary review in a later roadmap.

### D-027 — Desktop stack

**Decision.** The Windows-first implementation uses C#, .NET 10, WPF, structured Windows APIs, and testable adapters.

**Rationale.** The application is Windows-specific, API integration is central, and WPF exposes UI Automation.

**Implementation constraint.** The UI project is the only WPF-dependent production assembly.

**Revisit trigger.** Only through an architecture decision that demonstrates equivalent Windows integration and accessibility.

### D-028 — Persistence

**Decision.** Job state uses SQLite plus a restrictive per-job directory for attachments and raw reports.

**Rationale.** The workflow needs durable checkpoints, migrations, and local audit records.

**Implementation constraint.** Database migrations are explicit; raw outputs are minimized and never copied into logs blindly.

**Revisit trigger.** Only with a migration plan preserving historical job evidence.

### D-029 — Privilege separation

**Decision.** The main UI runs unelevated. Approved privileged batches run through an ephemeral elevated broker that exits after the batch.

**Rationale.** The normal interface should not become a long-lived administrator process.

**Implementation constraint.** Declined UAC leaves the job consistent and no permanent LocalSystem service is required for v0.1.

**Revisit trigger.** Only after focused security review of an alternative model.

### D-030 — Broker protocol

**Decision.** Broker requests are typed, schema-validated, size-limited, expiring, correlated, nonce-protected, and allowlisted over an authenticated named pipe restricted to the initiating user.

**Rationale.** The broker is a privileged attack surface and must fail closed.

**Implementation constraint.** No arbitrary command strings, executable paths, URLs, registry paths, or unbounded file operations.

**Revisit trigger.** Only to strengthen authentication or transport guarantees.

### D-031 — No always-on privileged service

**Decision.** Core 1.0 does not install a permanently running LocalSystem service.

**Rationale.** The standalone product does not need that persistent attack surface or lifecycle burden.

**Implementation constraint.** Privileged work is explicit, approved, bounded, and ephemeral.

**Revisit trigger.** Only if a demonstrated future requirement cannot be met safely by the broker and a focused security review approves an alternative.

### D-032 — Action journal and completion

**Decision.** Actions are journaled as planned, approved, started, applied, verified, failed, skipped, rolled back, or requiring review. Applied is not equivalent to complete; verified is complete.

**Rationale.** Installers and updates can partially succeed, reboot, hang, or misreport success.

**Implementation constraint.** Every action declares verification and rollback characteristics before approval.

**Revisit trigger.** Only by extending states with a migration and unchanged audit semantics.

### D-033 — Separate verification and cold boot

**Decision.** Verification is a distinct phase, and final acceptance includes a cold boot followed by re-verification.

**Rationale.** A configuration that only works before restart is not complete.

**Implementation constraint.** Final reports distinguish attempted, applied, and verified outcomes.

**Revisit trigger.** Only if a stronger reproducible verification model replaces it.

### D-034 — Windows Update integration

**Decision.** Structured Windows Update Agent APIs are the primary update integration; screen scraping Settings is prohibited.

**Rationale.** The workflow needs applicable-update queries, progress, result codes, and repeatable verification.

**Implementation constraint.** Update work is cancellable where supported, restart-aware, and resumable.

**Revisit trigger.** When Microsoft replaces the supported API boundary.

### D-035 — Finalization is blocking

**Decision.** Known workshop accounts, credentials, Wi-Fi profiles, test media, diagnostic exports, package residue, remote-support access, and similar artifacts are checked before handover.

**Rationale.** A technically configured device can still expose workshop or donor data.

**Implementation constraint.** ThirdLife reports exactly what it checked or removed and does not claim forensic removal.

**Revisit trigger.** The detector catalog may expand, but the blocking phase remains.

### D-036 — Three output classes

**Decision.** ThirdLife generates a full workshop record, a plain-language recipient guide, and a reviewable sanitized diagnostic bundle.

**Rationale.** Each audience needs different detail and privacy boundaries.

**Implementation constraint.** The recipient guide contains no workshop secrets; the diagnostic bundle is previewed before export.

**Revisit trigger.** Additional outputs may be added without merging these privacy classes.

### D-037 — Diagnostic redaction

**Decision.** Ordinary diagnostic exports omit names, usernames, Wi-Fi names, serial numbers, IP addresses, package download URLs, and personal paths unless explicitly reviewed and included.

**Rationale.** Supportability must not depend on broad personal-data collection.

**Implementation constraint.** Redaction has automated adversarial tests and structured fields are preferred over raw command output.

**Revisit trigger.** Only to tighten minimization or add explicit reviewed fields.

### D-038 — Encryption recovery ownership

**Decision.** Disk encryption is enabled only when the recipient or authorized organization controls and understands recovery material.

**Rationale.** A volunteer-owned recovery key can lock the recipient out later.

**Implementation constraint.** v0.1 does not silently enable encryption or retain an unauthorized key copy.

**Revisit trigger.** Only with a defined recovery-ownership workflow.

### D-039 — Accessibility baseline and recipient control

**Decision.** The operator UI supports keyboard-only use, screen-reader names, logical focus, high contrast, 200% scaling, non-color status, accessible progress, cancellation, and clear error recovery. Core 1.0 also supports a recipient-present, previewable and reversible accessibility setup for approved Windows settings.

**Rationale.** Accessibility is a release quality attribute, and recipient preferences must remain under recipient control rather than workshop assumption.

**Implementation constraint.** Custom WPF controls require UI Automation peers and verification. Sealed handover applies no recipient-specific preference and records onboarding as pending.

**Revisit trigger.** Only to raise the baseline or add a separately reviewed supported setting.

### D-040 — Basic operating-system backup scope

**Decision.** The v0.1 pilot may report backup as pending. Core 1.0 includes basic operating-system backup onboarding for a present recipient, destination/capacity checks, an initial backup and harmless restore test where supported, and clear recovery ownership. It does not implement a backup engine or configure Backup Circle.

**Rationale.** The portfolio expects basic backup setup in the standalone Core product while keeping high-risk encrypted repository behavior and keys inside the later Backup Circle product.

**Implementation constraint.** Do not report backup configured until a representative restore succeeds. Sealed handover creates no account, credential, recovery key, or backup job. Recovery material never enters ThirdLife metadata, logs, reports, support bundles, or suite metadata.

**Revisit trigger.** Advanced encrypted backup, repository management, or Backup Circle coordination requires its own product or future B4 work against a frozen release.

### D-041 — No AI in Core 1.0

**Decision.** Core 1.0 contains no AI model, AI cloud service, or AI-based disposition logic.

**Rationale.** The product needs deterministic, explainable rules and a minimal data boundary.

**Implementation constraint.** Do not add AI dependencies or generated safety recommendations to the runtime product.

**Revisit trigger.** Only after a separate product, privacy, safety, and explainability decision.

### D-042 — Offline degradation and deferred suite cache

**Decision.** Inventory, job management, policy evaluation, manual tests, existing evidence review, and report generation remain usable without network access. A portfolio offline package cache and deployment medium are deferred to B4.

**Rationale.** Workshops may have intermittent connectivity, but suite distribution must not become hidden B1 integration work.

**Implementation constraint.** Network-dependent actions fail into recoverable journal states. Core 1.0 documents its own offline installation and operation limits without claiming an offline sibling-app catalogue.

**Revisit trigger.** B4 may add verified offline suite deployment against frozen releases and redistribution evidence.

### D-043 — Dependency and licensing control

**Decision.** Every runtime dependency and catalog application has known provenance and license review; releases produce an SBOM.

**Rationale.** Supply-chain and redistribution risks are first-class product risks.

**Implementation constraint.** Installation rights and redistribution rights are tracked separately.

**Revisit trigger.** Only to strengthen governance or replace tooling.

### D-044 — Additional Core 1.0 non-goals

**Decision.** Core 1.0 excludes firmware flashing, third-party driver-download services, registry cleaning, generic debloating, automatic deletion of unknown software, data recovery, malware cleanup, password migration, project cloud storage, always-on remote management, shared portfolio content, sibling adapters, compatibility cuts, and offline suite deployment media.

**Rationale.** These functions expand risk, test surface, or cross-project coupling without proving the standalone refurbishment outcome.

**Implementation constraint.** Windows Update and existing supported mechanisms may be inspected and used within the approved workflow. Future integration ideas go to `FUTURE_ASSEMBLY_NOTES.md` only.

**Revisit trigger.** Each capability requires a named later project/milestone and threat/data review.

### D-045 — Authority and change control

**Decision.** Authority order is DECISIONS.md, ROADMAP.md, PROJECT_BOUNDARY.md, SECURITY.md, ACCESSIBILITY.md, LOW_SPEC.md, AGENTS.md, TASKS.yaml, then prompts and README. Codex may update task execution fields but may not alter frozen decisions, project boundaries, portfolio posture, dependencies, or acceptance criteria without human approval.

**Rationale.** The bundle must remain synchronized and resistant to safety, scope, and cross-project drift.

**Implementation constraint.** A change requires a human-approved decision/ADR, graph validation, synchronized document updates, and an explicit bundle-version change.

**Revisit trigger.** Only through a human-approved governance revision.

### D-046 — Portfolio component and queue position

**Decision.** ThirdLife Setup Core is the active Team B/B1 project. After its standalone stable gate, Team B proceeds to Scam Explainer. ThirdLife Deployment and Suite Assembly is a separate future Team B/B4 project.

**Rationale.** Both teams must own meaningful end-user projects and must not turn integration into a permanent parallel lane.

**Implementation constraint.** Current tasks may build only Core B1. Passing M6 or M7 does not authorize B4 work; M7 records the transition to Scam Explainer.

**Revisit trigger.** Only through a portfolio queue decision affecting unstarted work.

### D-047 — Standalone release independence

**Decision.** ThirdLife Setup Core must be independently useful, releasable, installable, updateable, repairable, removable, recoverable, and supportable without a sibling application, shared portfolio service, or project-controlled account.

**Rationale.** Standalone integrity protects users from suite lock-in and prevents synchronized cross-team releases.

**Implementation constraint.** No Core acceptance criterion may require a sibling application. ThirdLife uninstall must preserve unrelated application data.

**Revisit trigger.** Any exception requires a formal portfolio architecture, failure-mode, migration, and sustainability review.

### D-048 — Project-vacuum development

**Decision.** B1 is developed in its own repository and vacuum. Codex does not browse, import, modify, or depend on sibling repositories, private interfaces, active branches, databases, or services.

**Rationale.** The two teams cannot sustain day-to-day coordination and must advance asynchronously.

**Implementation constraint.** `TASKS.yaml` contains only project-local dependency edges. A sibling idea is deferred, not implemented around.

**Revisit trigger.** Only when B4 is the formal active Team B project or the user explicitly assigns a portfolio-governance task.

### D-049 — Late-binding suite assembly

**Decision.** Portfolio integration occurs only in B4 against exact frozen stable releases, hashes, release-interface sheets, samples, and public documentation.

**Rationale.** Frozen-artifact black-box assembly is reproducible and does not make product teams design against unstable work.

**Implementation constraint.** Core B1 performs no live integration testing and makes no compatibility claim for sibling development or “latest” versions.

**Revisit trigger.** Only through a portfolio architecture change.

### D-050 — No early shared integration infrastructure

**Decision.** B1 does not create a shared SDK, universal handoff schema, plugin framework, monorepo mandate, universal job/findings engine, portfolio background service, or shared user-content database.

**Rationale.** Premature shared infrastructure would be a hidden third project and a permanent coupling point.

**Implementation constraint.** Add an interface or command only when it independently benefits Core users, testing, automation, or support. Do not scaffold empty future extension layers.

**Revisit trigger.** Requires evidence from at least two stable products and a named owning project.

### D-051 — Future adapter ownership and integration order

**Decision.** B4 owns future sibling-specific catalogue entries, profiles, file associations, adapters, compatibility records, and workarounds. The default order is install, launch, open a user-selected standard file/workspace, and show human guidance before adding a custom adapter.

**Rationale.** Shallow, optional integration is easier to audit, maintain, disable, and replace with a manual path.

**Implementation constraint.** An adapter must be version-bounded, optional, independently disableable, and preserve standalone operation. Product private data is not an adapter interface.

**Revisit trigger.** A custom adapter requires documented repeated user value, safety, independence, maintenance, data ownership, and fallback evidence.

### D-052 — Minimal release interface envelope

**Decision.** Core 1.0 publishes a human-readable `RELEASE_INTERFACE.md` covering identity, artifacts, install/update/repair/remove, data locations, inputs/outputs, launch, offline/resource behavior, privilege/security, support bundle, samples, contacts, and known limitations.

**Rationale.** This is sufficient for later black-box deployment without a speculative shared runtime schema.

**Implementation constraint.** Unknown fields remain explicit. Unsupported behavior is not invented. The sheet is frozen with the exact stable release at `TL-0710`.

**Revisit trigger.** Add a field only when a released product or a B4 black-box test proves it necessary.

### D-053 — Portfolio data ownership

**Decision.** ThirdLife Setup Core owns only device-support job, catalogue/profile, action, compatibility-free release, handover, and support metadata. It does not own or index sibling content, workspaces, evidence, recordings, messages, application records, repositories, credentials, or recovery keys.

**Rationale.** User-owned data and product boundaries must remain clear even when several tools are installed later.

**Implementation constraint.** No shared content library or private database access. Opening a future user-selected file must state whether it is referenced, copied, or converted.

**Revisit trigger.** Only through a formal portfolio data-boundary and privacy decision.

### D-054 — Cross-project ideas are backlog only

**Decision.** Cross-project opportunities discovered during B1 are recorded in `FUTURE_ASSEMBLY_NOTES.md` and do not become active tasks, dependencies, or code.

**Rationale.** This preserves useful observations without turning speculative integration into scope or cross-team blocking work.

**Implementation constraint.** A note must identify a manual fallback and why it belongs to B4. Codex continues the selected project-local task.

**Revisit trigger.** When B4 begins and the portfolio owner promotes a note into its own governed task graph.

### D-055 — Generic Core catalogue and profiles

**Decision.** B1 proves package, profile, plan, journal, verification, and recovery behavior with generic public free essentials and synthetic packages. It does not use PaperWorkShell or another sibling as a development dependency or reference integration.

**Rationale.** Core has substantial standalone value and must not wait for or shape another team's release.

**Implementation constraint.** Sibling names may appear only in deferred portfolio documentation, not in active Core catalogue/profile behavior or acceptance tests.

**Revisit trigger.** B4 may add a sibling only after its exact stable artifacts and release-interface sheet are frozen.

### D-056 — Core 1.0 stable gate and portfolio handoff

**Decision.** `TL-0710` is the only gate that completes Team B/B1. It freezes Core 1.0 artifacts and release evidence and supplies the minimal portfolio handoff.

**Rationale.** A controlled pilot is not an independently supported stable release.

**Implementation constraint.** The gate requires lifecycle, offline-core, low-spec, accessibility, security/privacy, licence, data-preservation, sample, and release-interface evidence. It sends Team B to Scam Explainer and does not authorize B4.

**Revisit trigger.** Only by a portfolio-owner decision that changes the queue or stable-release standard.

### D-057 — Core 1.0 accessibility and backup completion boundary

**Decision.** Core 1.0 includes recipient-controlled accessibility setup and basic operating-system backup onboarding/restore verification, but excludes personal account creation, automatic recovery-key custody, a custom backup engine, Backup Circle control, and broader recipient application integration.

**Rationale.** These are meaningful standalone device-handover capabilities while advanced domain behavior belongs to later products.

**Implementation constraint.** Recipient-present actions are explicit, reversible where supported, verified, and secret-minimizing. Sealed handover records personal setup as pending.

**Revisit trigger.** Broader onboarding or advanced backup requires a later project or milestone with its own threat/data decisions.
