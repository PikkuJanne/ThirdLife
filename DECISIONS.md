# ThirdLife Setup Core — Decision Register

**Bundle version:** 0.8.0  
**Generated:** 2026-11-27  
**Status:** Rationale and decision history for Team B/B1

## Purpose and status

The [product contract](docs/product-contract.md) defines the current outcomes and
invariants. This register records their rationale and the choices made at each
stage. Entries are current rationale unless marked historical or qualified by
a later decision. The applicable product contract and scoped specifications
govern implementation. Historical choices retain their original evidence and
do not impose sequencing or independent approval requirements.

D-075 records the current development model and identifies the implementation
constraints that have been superseded. [Change control](docs/change-control.md)
covers recording a material product, trust, licensing or exposure decision.
D-076 fixes guest capacity for the complete lifecycle and applies that model to
unavailable live test conditions without changing product safeguards.

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
| [D-024](#d-024) | Structured package integration |
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
| [D-045](#d-045) | Original authority and change control (historical) |
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
| [D-058](#d-058) | Portfolio v2.1 development baseline |
| [D-059](#d-059) | Active Codex machine is the only physical validation hardware |
| [D-060](#d-060) | Modest-hardware readiness is engineered, not lab-certified |
| [D-061](#d-061) | Quick, targeted, full, and extended test tiers |
| [D-062](#d-062) | Focused deterministic regressions and independently invokable slow scenarios |
| [D-063](#d-063) | GitHub continuity and no remote-runtime dependency |
| [D-064](#d-064) | TL-0008 draft 1 and the immediate MHT walkthrough are superseded |
| [D-065](#d-065) | Manual hardware-test workflow remains, hardware-matrix certification does not |
| [D-066](#d-066) | Hardware and resource claims are limited to observed evidence |
| [D-067](#d-067) | Prior task integration policy (historical) |
| [D-068](#d-068) | Remote CI remains supplementary |
| [D-069](#d-069) | Compact live status and durable evidence |
| [D-070](#d-070) | Reusable test runners and declarative profiles |
| [D-071](#d-071) | Evidence ownership and deterministic reconciliation |
| [D-072](#d-072) | Versioned and stale-safe lifecycle knowledge |
| [D-073](#d-073) | Focused human testing and bounded continuation |
| [D-074](#d-074) | Codex owns technical verification and accountable decisions remain explicit |
| [D-075](#d-075) | Current product contract and capability-specific engineering |
| [D-076](#d-076) | One existing guest and continued engineering across the complete timeline |

## Decisions

<a id="d-001"></a>

### D-001 — Project and component identity

**Decision.** The product family, repository, solution, and production namespaces use the identity ThirdLife. The active Team B/B1 project and user-facing release name are ThirdLife Setup Core.

**Rationale.** The current standalone component needs a clear project boundary while retaining the stable `ThirdLife.*` code identity that the later B4 host may extend.

**Implementation constraint.** Use `ThirdLife.*` namespaces and `ThirdLife.sln`; identify the active product as ThirdLife Setup Core in roadmap, UI, release, and support text. Do not scaffold a speculative integration product.

**Revisit trigger.** Only after an explicit human-approved naming, legal, or portfolio-architecture decision before public release.

<a id="d-002"></a>

### D-002 — Product outcome

**Decision.** ThirdLife Setup Core is a local-first, auditable refurbishment workflow that assesses a sanitized device, applies an approved plan, verifies the outcome, and produces a clear handover.

**Rationale.** The deliverable is a policy-backed, verified device state rather than a collection of cleanup actions or a shell around sibling products.

**Implementation constraint.** Every feature must support Intake, Inspect, Decide, Prepare, Verify, Handover, recipient-controlled Core accessibility/backup onboarding, or the standalone release lifecycle.

**Revisit trigger.** Only through a new product charter approved by the project and portfolio owners.

<a id="d-003"></a>

### D-003 — Primary Core operator

**Decision.** The primary operator is a volunteer or staff refurbisher preparing donated Windows computers for recipients. A present recipient participates only in explicitly recipient-controlled setup.

**Rationale.** This gives the first project one operational context and one accountable workshop operator while preventing volunteers from choosing personal settings or secrets on a recipient's behalf.

**Implementation constraint.** Coordinator and recipient experiences are secondary; fleet administration and sibling-app workflows are not Core 1.0 goals.

**Revisit trigger.** After the standalone Core 1.0 release and a separately approved product milestone.

<a id="d-004"></a>

### D-004 — Starting-state boundary

**Decision.** v0.1 assumes sanitized, replaced, or absent donor storage and a fresh or known Windows installation. Existing personal-PC repair or cleanup is out of scope.

**Rationale.** Unknown user data, malware, licenses, encryption, and configuration make an existing-PC workflow materially riskier.

**Implementation constraint.** Do not add preservation, malware-removal, migration, or unknown-application cleanup behavior.

**Revisit trigger.** Only as a separately threat-modeled, initially read-only product mode.

<a id="d-005"></a>

### D-005 — Operating-system target

**Decision.** Windows 11 client on native x64 is the only supported application host and operating-system target for new live assessment, preparation, remediation, and handover. The WPF UI runs as a standard user.

**Rationale.** Workshops need a single, explicit platform boundary for supported deployment.

**Implementation constraint.** Verify the structured Windows version, workstation product type, reviewed Windows 11 build, and native OS architecture before live work. The initial identifying base build is 22000. Unknown or contradictory identity fails closed. Separately evaluate servicing with versioned edition, release, and date evidence. An identified Windows 11 host can open existing work when servicing data is stale. Stale evidence cannot satisfy readiness requirements. Historical evidence retains its original platform facts and carries no current execution authority.

**Approved amendment.** The [Windows 11–only amendment](docs/amendments/2026-11-25-windows-11-only.md) supersedes the former Windows 10 audit and disposition route.

**Revisit trigger.** A change to the platform boundary requires a governed amendment. Reviewed release and servicing data are maintained under D-072.

<a id="d-006"></a>

### D-006 — Pilot and standalone release cuts

**Decision.** Milestones M0 through M6 produce the controlled v0.1 partner pilot. Milestone M7 completes and freezes ThirdLife Setup Core 1.0 as the independently releasable Team B/B1 product.

**Rationale.** The pilot proves the core refurbishment architecture, but the portfolio requires a stable standalone release, basic accessibility and operating-system backup onboarding, lifecycle hardening, and a release-interface sheet before Team B leaves B1.

**Implementation constraint.** Passing `TL-0611` does not complete B1 or authorize suite integration. B1 completes only at human gate `TL-0710`.

**Revisit trigger.** Only by an approved roadmap revision that preserves completed milestone evidence and records the effect on the Team B queue.

<a id="d-007"></a>

### D-007 — Sanitization gate

**Decision.** Media sanitization is an external prerequisite recorded as evidence. Unknown or failed sanitization blocks preparation.

**Rationale.** Sanitization is a separate high-risk program and should not be reduced to a generic live-Windows wipe command.

**Implementation constraint.** v0.1 records method, operator, date, media identifier, result, verification, and policy version; it does not erase donor data.

**Revisit trigger.** Only in a separately released component with its own threat model and validation program.

<a id="d-008"></a>

### D-008 — OS installation and imaging

**Decision.** Windows installation, imaging, Sysprep automation, and product-key installation are external prerequisites for v0.1.

**Rationale.** The first release focuses on assessment, controlled preparation, verification, and handover.

**Implementation constraint.** ThirdLife may record and verify the resulting state but must not silently absorb imaging responsibilities.

**Revisit trigger.** After v0.1 through a separate architecture and operational review.

<a id="d-009"></a>

### D-009 — No compatibility, ownership, or activation bypasses

**Decision.** ThirdLife must not bypass Windows requirements, activation, firmware passwords, MDM, Autopilot-style enrollment, anti-theft controls, or ownership restrictions.

**Rationale.** These are support, licensing, security, or ownership boundaries rather than cleanup tasks.

**Implementation constraint.** Detection produces a blocker or human review; no override implementation may be added.

**Revisit trigger.** Never for bypass behavior; only evidence providers may evolve.

<a id="d-010"></a>

### D-010 — No optimizer scope

**Decision.** ThirdLife is not a PC cleaner, optimizer, debloater, registry cleaner, driver-download utility, or general IT toolbox.

**Rationale.** These labels invite unsafe, unbounded, and difficult-to-verify behavior.

**Implementation constraint.** Reject features without a direct refurbishment acceptance or handover outcome.

**Revisit trigger.** Only by replacing the product charter, not by incremental scope creep.

<a id="d-011"></a>

### D-011 — Local-first data model

**Decision.** Jobs, observations, plans, journals, and reports are stored locally. Core inventory and policy evaluation work offline.

**Rationale.** Workshops need privacy, resilience, and operation under poor connectivity.

**Implementation constraint.** No recipient or device data is sent to a ThirdLife service.

**Revisit trigger.** A future optional service requires separate consent, privacy, and threat-model decisions.

<a id="d-012"></a>

### D-012 — Network categories

**Decision.** The application exposes network use as package-catalog update, software download, Windows Update, or optional self-update.

**Rationale.** Operators must understand why the device is using the network.

**Implementation constraint.** No hidden or catch-all network category; tests use fakes unless integration behavior is under test.

**Revisit trigger.** When a new approved network capability is introduced.

<a id="d-013"></a>

### D-013 — Telemetry default

**Decision.** Product telemetry is off by default. Pilot metrics use explicit partner exports containing aggregated or pseudonymized data.

**Rationale.** The project should not create a surveillance dependency for recipients or workshops.

**Implementation constraint.** No mandatory analytics SDK, account, device identifier, or background uploader.

**Revisit trigger.** Only with explicit opt-in design and privacy approval.

<a id="d-014"></a>

### D-014 — Identity and minimization

**Decision.** Recipient identity is not required for workshop jobs. Full serial numbers remain in the workshop record; support exports omit or truncate them.

**Rationale.** Device preparation should not unnecessarily spread personal or identifying data.

**Implementation constraint.** No names in log filenames, temporary paths, package commands, or ordinary diagnostic logs.

**Revisit trigger.** Only if an approved operational requirement cannot be met with an internal job identifier.

<a id="d-015"></a>

### D-015 — Evidence semantics

**Decision.** Evidence is explicitly classified as observed, inferred, not available, or human confirmed, with provider, timestamp, and provenance.

**Rationale.** Automated evidence has limits and must not be represented as certainty.

**Implementation constraint.** Missing data is unknown, never implicitly passed.

**Revisit trigger.** Only by extending the vocabulary without weakening provenance.

<a id="d-016"></a>

### D-016 — No aggregate health score

**Decision.** ThirdLife does not calculate or display a universal numeric device-health score.

**Rationale.** A score can hide a single critical blocker such as failing storage or unsupported software.

**Implementation constraint.** Show requirements, evidence, and dispositions instead.

**Revisit trigger.** Never for deployment decisions; analytics may use separate non-decisional metrics later.

<a id="d-017"></a>

### D-017 — Disposition vocabulary

**Decision.** The current dispositions are Ready to prepare, Repair and retest, Human review required, and Do not deploy.

**Rationale.** These dispositions give workshops an action or a clear explanation for withholding Windows 11 deployment.

**Implementation constraint.** Each disposition cites evidence and policy. A verified target outside the platform scope or unable to satisfy preparation policy is Do not deploy. Repair and retest requires an evidenced repair path in the supported workflow. Missing, unavailable, stale, or contradictory evidence requires review. Preserve the serialized alternative-operating-system disposition and its interpretation as a historical result with original policy provenance. Current policies emit no such result.

**Approved amendment.** See the [Windows 11–only amendment](docs/amendments/2026-11-25-windows-11-only.md) for versioning and continuity.

**Revisit trigger.** Only through a versioned policy and report change that preserves historical interpretation.

<a id="d-018"></a>

### D-018 — Facts, policy, and decisions are separate

**Decision.** Observed facts are immutable job evidence, organization policy is versioned input, and disposition is a reproducible evaluation result.

**Rationale.** Organizations may adopt different standards without rewriting machine facts.

**Implementation constraint.** Policy changes do not retroactively rewrite historical jobs.

**Revisit trigger.** Only if an equally auditable model replaces the separation.

<a id="d-019"></a>

### D-019 — Explicit exceptions

**Decision.** Permitted exceptions require operator identity, reason, policy authorization, timestamp, and visible report output.

**Rationale.** A blocker must not silently turn green.

**Implementation constraint.** Prohibited rules cannot be overridden by ordinary operators.

**Revisit trigger.** Only through policy-governance changes.

<a id="d-020"></a>

### D-020 — Outcome-based profiles

**Decision.** Profiles express user outcomes and split workshop capabilities from recipient choices. During B1 they use generic capabilities and do not name or require sibling portfolio applications.

**Rationale.** Outcome-based profiles allow package choices to change without coupling the profile to one application and keep recipient-specific decisions under recipient control.

**Implementation constraint.** Profiles reference reviewed catalogue capabilities and compiled actions, not raw installers, scripts, sibling identifiers, or private cross-project schemas.

**Revisit trigger.** Sibling-product profile entries are created only by the formal B4 suite-assembly project against frozen releases.

<a id="d-021"></a>

### D-021 — Recipient-controlled settings and secrets

**Decision.** Browser preference, cloud accounts, recovery material, backup keys, accessibility choices, and similar personal settings are configured with the recipient or left pending.

**Rationale.** The workshop must not own the recipient’s secrets or make personal choices by assumption.

**Implementation constraint.** Sealed handover performs machine-level setup only.

**Revisit trigger.** Only for an explicitly authorized organizational deployment model.

<a id="d-022"></a>

### D-022 — Curated application catalog

**Decision.** ThirdLife Setup Core exposes only a small reviewed catalogue. B1 development uses generic free essentials and synthetic test packages; sibling portfolio applications are not catalogue entries until B4.

**Rationale.** A narrow catalogue is supportable and prevents arbitrary package execution or premature live integration.

**Implementation constraint.** Every Core entry records publisher, licence, privacy, architecture, storage, background behavior, update behavior, verification, and redistribution status. Do not add PaperWorkShell-specific or other sibling-specific catalogue code in B1.

**Revisit trigger.** B4 may add exact frozen sibling releases through its own compatibility process.

<a id="d-023"></a>

### D-023 — Profiles are non-executable data

**Decision.** Profiles and policies cannot contain scripts, shell commands, arbitrary executables, registry paths, or URLs that become code.

**Rationale.** Executable behavior must remain in reviewed, compiled action implementations.

**Implementation constraint.** Unknown action types fail closed.

**Revisit trigger.** Never for v0.1; any future extension requires a new trust model.

<a id="d-024"></a>

### D-024 — Structured WinGet integration

**Current in part.** The replaceable structured adapter and localized-table prohibition continue. D-075 supersedes the original WinGet-only comparison and permits supported package routes within the same trust boundary. The original decision follows.

**Decision.** Package operations use a replaceable structured adapter. v0.1 begins with a spike comparing Microsoft.WinGet.Client and the WinGet COM API; localized CLI tables are not parsed.

**Rationale.** Structured APIs provide safer progress, errors, and machine-readable metadata.

**Implementation constraint.** CLI invocation may exist only as a controlled fallback with structured or invariant output and tests.

**Revisit trigger.** After the spike or when Microsoft support boundaries materially change.

<a id="d-025"></a>

### D-025 — Package trust controls

**Decision.** Package actions pin approved source and exact package ID, record resolved publisher and version, preserve hash validation, and require reapproval after material resolution changes.

**Rationale.** The package source and resolved artifact are part of the approved plan.

**Implementation constraint.** Security-hash overrides and unexpected source substitution are impossible through the UI or profile.

**Revisit trigger.** Only to strengthen verification or support an equally reviewed backend.

<a id="d-026"></a>

### D-026 — No WinGet Configuration in Core 1.0

**Decision.** Core 1.0 does not execute WinGet Configuration or profile-provided PowerShell DSC resources.

**Rationale.** That path creates a wider module-download and execution boundary than the standalone Core release needs.

**Implementation constraint.** Use ThirdLife schemas and compiled allowlisted actions.

**Revisit trigger.** After a dedicated supply-chain and execution-boundary review in a later roadmap.

<a id="d-027"></a>

### D-027 — Desktop stack

**Decision.** The Windows 11 client/native-x64 implementation uses C#, .NET 10, WPF, structured Windows APIs, and testable adapters.

**Rationale.** The application is Windows-specific, API integration is central, and WPF exposes UI Automation.

**Implementation constraint.** The UI project is the only WPF-dependent production assembly. Core and protocol assemblies remain independent of WPF. Production entry points enforce D-005 before live work.

**Approved amendment.** See the [Windows 11–only amendment](docs/amendments/2026-11-25-windows-11-only.md).

**Revisit trigger.** Only through an architecture decision that demonstrates equivalent Windows integration and accessibility.

<a id="d-028"></a>

### D-028 — Persistence

**Decision.** Job state uses SQLite plus a restrictive per-job directory for attachments and raw reports.

**Rationale.** The workflow needs durable checkpoints, migrations, and local audit records.

**Implementation constraint.** Database migrations are explicit; raw outputs are minimized and never copied into logs blindly.

**Revisit trigger.** Only with a migration plan preserving historical job evidence.

<a id="d-029"></a>

### D-029 — Privilege separation

**Current in part.** The unelevated UI, bounded privilege, durable recovery and no-permanent-service protections continue. D-075 supersedes the broker-exclusive route for suitable supported package operations. The original decision follows.

**Decision.** The main UI runs unelevated. Approved privileged batches run through an ephemeral elevated broker that exits after the batch.

**Rationale.** The normal interface should not become a long-lived administrator process.

**Implementation constraint.** Declined UAC leaves the job consistent and no permanent LocalSystem service is required for v0.1.

**Revisit trigger.** Only after focused security review of an alternative model.

<a id="d-030"></a>

### D-030 — Broker protocol

**Decision.** Broker requests are typed, schema-validated, size-limited, expiring, correlated, nonce-protected, and allowlisted over an authenticated named pipe restricted to the initiating user.

**Rationale.** The broker is a privileged attack surface and must fail closed.

**Implementation constraint.** No arbitrary command strings, executable paths, URLs, registry paths, or unbounded file operations.

**Revisit trigger.** Only to strengthen authentication or transport guarantees.

<a id="d-031"></a>

### D-031 — No always-on privileged service

**Decision.** Core 1.0 does not install a permanently running LocalSystem service.

**Rationale.** The standalone product does not need that persistent attack surface or lifecycle burden.

**Implementation constraint.** Privileged work is explicit, approved, bounded, and ephemeral.

**Revisit trigger.** Only if a demonstrated future requirement cannot be met safely by the broker and a focused security review approves an alternative.

<a id="d-032"></a>

### D-032 — Action journal and completion

**Decision.** Actions are journaled as planned, approved, started, applied, verified, failed, skipped, rolled back, or requiring review. Applied is not equivalent to complete; verified is complete.

**Rationale.** Installers and updates can partially succeed, reboot, hang, or misreport success.

**Implementation constraint.** Every action declares verification and rollback characteristics before approval.

**Revisit trigger.** Only by extending states with a migration and unchanged audit semantics.

<a id="d-033"></a>

### D-033 — Separate verification and cold boot

**Decision.** Verification is a distinct phase, and final acceptance includes a cold boot followed by re-verification.

**Rationale.** A configuration that only works before restart is not complete.

**Implementation constraint.** Final reports distinguish attempted, applied, and verified outcomes. Under D-074, TL-0509 verifies checkpoint and re-verification behavior with approved same-machine VM cold-start evidence for development. Record any physical host power cycle as unverified unless directly evidenced. Final product acceptance still requires its actual cold boot. Any explicitly required physical development or release evidence must use the active Codex machine and a bounded authorized procedure. TL-0008 only specifies the procedure.

**Revisit trigger.** Only if a stronger reproducible verification model replaces it.

<a id="d-034"></a>

### D-034 — Windows Update integration

**Decision.** Structured Windows Update Agent APIs are the primary update integration; screen scraping Settings is prohibited.

**Rationale.** The workflow needs applicable-update queries, progress, result codes, and repeatable verification.

**Implementation constraint.** Update work is cancellable where supported, restart-aware, and resumable.

**Revisit trigger.** When Microsoft replaces the supported API boundary.

<a id="d-035"></a>

### D-035 — Finalization is blocking

**Decision.** Known workshop accounts, credentials, Wi-Fi profiles, test media, diagnostic exports, package residue, remote-support access, and similar artifacts are checked before handover.

**Rationale.** A technically configured device can still expose workshop or donor data.

**Implementation constraint.** ThirdLife reports exactly what it checked or removed and does not claim forensic removal.

**Revisit trigger.** The detector catalog may expand, but the blocking phase remains.

<a id="d-036"></a>

### D-036 — Three output classes

**Decision.** ThirdLife generates a full workshop record, a plain-language recipient guide, and a reviewable sanitized diagnostic bundle.

**Rationale.** Each audience needs different detail and privacy boundaries.

**Implementation constraint.** The recipient guide contains no workshop secrets; the diagnostic bundle is previewed before export.

**Revisit trigger.** Additional outputs may be added without merging these privacy classes.

<a id="d-037"></a>

### D-037 — Diagnostic redaction

**Decision.** Ordinary diagnostic exports omit names, usernames, Wi-Fi names, serial numbers, IP addresses, package download URLs, and personal paths unless explicitly reviewed and included.

**Rationale.** Supportability must not depend on broad personal-data collection.

**Implementation constraint.** Redaction has automated adversarial tests and structured fields are preferred over raw command output.

**Revisit trigger.** Only to tighten minimization or add explicit reviewed fields.

<a id="d-038"></a>

### D-038 — Encryption recovery ownership

**Decision.** Disk encryption is enabled only when the recipient or authorized organization controls and understands recovery material.

**Rationale.** A volunteer-owned recovery key can lock the recipient out later.

**Implementation constraint.** v0.1 does not silently enable encryption or retain an unauthorized key copy.

**Revisit trigger.** Only with a defined recovery-ownership workflow.

<a id="d-039"></a>

### D-039 — Accessibility baseline and recipient control

**Decision.** The operator UI supports keyboard-only use, screen-reader names, logical focus, high contrast, 200% scaling, non-color status, accessible progress, cancellation, and clear error recovery. Core 1.0 also supports a recipient-present, previewable and reversible accessibility setup for approved Windows settings.

**Rationale.** Accessibility is a release quality attribute, and recipient preferences must remain under recipient control rather than workshop assumption.

**Implementation constraint.** Custom WPF controls require UI Automation peers and verification. Sealed handover applies no recipient-specific preference and records onboarding as pending.

**Revisit trigger.** Only to raise the baseline or add a separately reviewed supported setting.

<a id="d-040"></a>

### D-040 — Basic operating-system backup scope

**Decision.** The v0.1 pilot may report backup as pending. Core 1.0 includes basic operating-system backup onboarding for a present recipient, destination/capacity checks, an initial backup and harmless restore test where supported, and clear recovery ownership. It does not implement a backup engine or configure Backup Circle.

**Rationale.** The portfolio expects basic backup setup in the standalone Core product while keeping high-risk encrypted repository behavior and keys inside the later Backup Circle product.

**Implementation constraint.** Do not report backup configured until a representative restore succeeds. Sealed handover creates no account, credential, recovery key, or backup job. Recovery material never enters ThirdLife metadata, logs, reports, support bundles, or suite metadata.

**Revisit trigger.** Advanced encrypted backup, repository management, or Backup Circle coordination requires its own product or future B4 work against a frozen release.

<a id="d-041"></a>

### D-041 — No AI in Core 1.0

**Decision.** Core 1.0 contains no AI model, AI cloud service, or AI-based disposition logic.

**Rationale.** The product needs deterministic, explainable rules and a minimal data boundary.

**Implementation constraint.** Do not add AI dependencies or generated safety recommendations to the runtime product.

**Revisit trigger.** Only after a separate product, privacy, safety, and explainability decision.

<a id="d-042"></a>

### D-042 — Offline degradation and deferred suite cache

**Decision.** Inventory, job management, policy evaluation, manual tests, existing evidence review, and report generation remain usable without network access. A portfolio offline package cache and deployment medium are deferred to B4.

**Rationale.** Workshops may have intermittent connectivity, but suite distribution must not become hidden B1 integration work.

**Implementation constraint.** Network-dependent actions fail into recoverable journal states. Core 1.0 documents its own offline installation and operation limits without claiming an offline sibling-app catalogue.

**Revisit trigger.** B4 may add verified offline suite deployment against frozen releases and redistribution evidence.

<a id="d-043"></a>

### D-043 — Dependency and licensing control

**Decision.** Every runtime dependency and catalog application has known provenance and license review; releases produce an SBOM.

**Rationale.** Supply-chain and redistribution risks are first-class product risks.

**Implementation constraint.** Installation rights and redistribution rights are tracked separately.

**Revisit trigger.** Only to strengthen governance or replace tooling.

<a id="d-044"></a>

### D-044 — Additional Core 1.0 non-goals

**Decision.** Core 1.0 excludes firmware flashing, third-party driver-download services, registry cleaning, generic debloating, automatic deletion of unknown software, data recovery, malware cleanup, password migration, project cloud storage, always-on remote management, shared portfolio content, sibling adapters, compatibility cuts, and offline suite deployment media.

**Rationale.** These functions expand risk, test surface, or cross-project coupling without proving the standalone refurbishment outcome.

**Implementation constraint.** Windows Update and existing supported mechanisms may be inspected and used within the approved workflow. Future integration ideas go to `FUTURE_ASSEMBLY_NOTES.md` only.

**Revisit trigger.** Each capability requires a named later project/milestone and threat/data review.

<a id="d-045"></a>

### D-045 — Authority and change control

**Historical.** Superseded prospectively by [D-075](#d-075). Historical task contracts and evidence retain provenance. The old file precedence, restricted task fields and approval gate for engineering changes have retired.

**Decision.** Authority order is DECISIONS.md, ROADMAP.md, PROJECT_BOUNDARY.md, SECURITY.md, ACCESSIBILITY.md, LOW_SPEC.md, DEVELOPMENT_WORKFLOW.md, TESTING.md, AGENTS.md, TASKS.yaml, STATUS.md, then prompts and README. Codex may update task execution fields but may not alter frozen decisions, project boundaries, portfolio posture, dependencies, or acceptance criteria without human approval.

**Rationale.** The bundle must remain synchronized and resistant to safety, scope, and cross-project drift.

**Implementation constraint.** A change requires a human-approved decision/ADR, graph validation, synchronized document updates, and an explicit bundle-version change.

**Approved amendment `AMD-2026-08-22-ADR-0009` — unique TL-0401 ADR number (22 August 2026).** Janne Vuorela, Principal Software Architect & Sole Project Owner, approved preserving the completed ADR 0004 at `docs/adr/0004-ephemeral-broker.md` and reserving the next unoccupied identifier, ADR 0009 for TL-0401 at `docs/adr/0009-winget-backend.md`. The future task's deliverable and human-evidence wording change together; its dependencies, status, evidence, package/security requirements, and separate maintainer approval remain unchanged. The complete impact, alternatives, rollout, rollback, and historical-evidence treatment are recorded in [`docs/amendments/2026-08-22-adr-0009-reservation.md`](docs/amendments/2026-08-22-adr-0009-reservation.md). This amendment changes no product, boundary, runtime, data, licence, or release decision.

D-074 supersedes that separate maintainer approval for a compliant TL-0401 selection. The ADR reservation and applicable trust, privilege, licence and architecture authority still apply.

**Approved amendment `AMD-2026-11-25-WINDOWS-11-ONLY` (25 November 2026).** Project owner Janne Vuorela authorized changing D-005, D-017, and D-027 together and implementing TL-0315. The [amendment](docs/amendments/2026-11-25-windows-11-only.md) records the scope, provenance, verification, integration authority, and forward rollback.

**Revisit trigger.** Only through a human-approved governance revision.

<a id="d-046"></a>

### D-046 — Portfolio component and queue position

**Decision.** ThirdLife Setup Core is the active Team B/B1 project. After its standalone stable gate, Team B proceeds to Scam Explainer. ThirdLife Deployment and Suite Assembly is a separate future Team B/B4 project.

**Rationale.** Both teams must own meaningful end-user projects and must not turn integration into a permanent parallel lane.

**Implementation constraint.** Current tasks may build only Core B1. Passing M6 or M7 does not authorize B4 work; M7 records the transition to Scam Explainer.

**Revisit trigger.** Only through a portfolio queue decision affecting unstarted work.

<a id="d-047"></a>

### D-047 — Standalone release independence

**Decision.** ThirdLife Setup Core must be independently useful, releasable, installable, updateable, repairable, removable, recoverable, and supportable without a sibling application, shared portfolio service, or project-controlled account.

**Rationale.** Standalone integrity protects users from suite lock-in and prevents synchronized cross-team releases.

**Implementation constraint.** No Core acceptance criterion may require a sibling application. ThirdLife uninstall must preserve unrelated application data.

**Revisit trigger.** Any exception requires a formal portfolio architecture, failure-mode, migration, and sustainability review.

<a id="d-048"></a>

### D-048 — Project-vacuum development

**Decision.** B1 is developed in its own repository and vacuum. Codex does not browse, import, modify, or depend on sibling repositories, private interfaces, active branches, databases, or services.

**Rationale.** The two teams cannot sustain day-to-day coordination and must advance asynchronously.

**Implementation constraint.** `TASKS.yaml` contains only project-local dependency edges. A sibling idea is deferred, not implemented around.

**Revisit trigger.** Only when B4 is the formal active Team B project or the user explicitly assigns a portfolio-governance task.

<a id="d-049"></a>

### D-049 — Late-binding suite assembly

**Decision.** Portfolio integration occurs only in B4 against exact frozen stable releases, hashes, release-interface sheets, samples, and public documentation.

**Rationale.** Frozen-artifact black-box assembly is reproducible and does not make product teams design against unstable work.

**Implementation constraint.** Core B1 performs no live integration testing and makes no compatibility claim for sibling development or “latest” versions.

**Revisit trigger.** Only through a portfolio architecture change.

<a id="d-050"></a>

### D-050 — No early shared integration infrastructure

**Decision.** B1 does not create a shared SDK, universal handoff schema, plugin framework, monorepo mandate, universal job/findings engine, portfolio background service, or shared user-content database.

**Rationale.** Premature shared infrastructure would be a hidden third project and a permanent coupling point.

**Implementation constraint.** Add an interface or command only when it independently benefits Core users, testing, automation, or support. Do not scaffold empty future extension layers.

**Revisit trigger.** Requires evidence from at least two stable products and a named owning project.

<a id="d-051"></a>

### D-051 — Future adapter ownership and integration order

**Decision.** B4 owns future sibling-specific catalogue entries, profiles, file associations, adapters, compatibility records, and workarounds. The default order is install, launch, open a user-selected standard file/workspace, and show human guidance before adding a custom adapter.

**Rationale.** Shallow, optional integration is easier to audit, maintain, disable, and replace with a manual path.

**Implementation constraint.** An adapter must be version-bounded, optional, independently disableable, and preserve standalone operation. Product private data is not an adapter interface.

**Revisit trigger.** A custom adapter requires documented repeated user value, safety, independence, maintenance, data ownership, and fallback evidence.

<a id="d-052"></a>

### D-052 — Minimal release interface envelope

**Decision.** Core 1.0 publishes a human-readable `RELEASE_INTERFACE.md` covering identity, artifacts, install/update/repair/remove, data locations, inputs/outputs, launch, offline/resource behavior, privilege/security, support bundle, samples, contacts, and known limitations.

**Rationale.** This is sufficient for later black-box deployment without a speculative shared runtime schema.

**Implementation constraint.** Unknown fields remain explicit. Unsupported behavior is not invented. The sheet is frozen with the exact stable release at `TL-0710`.

**Revisit trigger.** Add a field only when a released product or a B4 black-box test proves it necessary.

<a id="d-053"></a>

### D-053 — Portfolio data ownership

**Decision.** ThirdLife Setup Core owns only device-support job, catalogue/profile, action, compatibility-free release, handover, and support metadata. It does not own or index sibling content, workspaces, evidence, recordings, messages, application records, repositories, credentials, or recovery keys.

**Rationale.** User-owned data and product boundaries must remain clear even when several tools are installed later.

**Implementation constraint.** No shared content library or private database access. Opening a future user-selected file must state whether it is referenced, copied, or converted.

**Revisit trigger.** Only through a formal portfolio data-boundary and privacy decision.

<a id="d-054"></a>

### D-054 — Cross-project ideas are backlog only

**Decision.** Cross-project opportunities discovered during B1 are recorded in `FUTURE_ASSEMBLY_NOTES.md` and do not become active tasks, dependencies, or code.

**Rationale.** This preserves useful observations without turning speculative integration into scope or cross-team blocking work.

**Implementation constraint.** A note must identify a manual fallback and why it belongs to B4. Codex continues the selected project-local task.

**Revisit trigger.** When B4 begins and the portfolio owner promotes a note into its own governed task graph.

<a id="d-055"></a>

### D-055 — Generic Core catalogue and profiles

**Decision.** B1 proves package, profile, plan, journal, verification, and recovery behavior with generic public free essentials and synthetic packages. It does not use PaperWorkShell or another sibling as a development dependency or reference integration.

**Rationale.** Core has substantial standalone value and must not wait for or shape another team's release.

**Implementation constraint.** Sibling names may appear only in deferred portfolio documentation, not in active Core catalogue/profile behavior or acceptance tests.

**Revisit trigger.** B4 may add a sibling only after its exact stable artifacts and release-interface sheet are frozen.

<a id="d-056"></a>

### D-056 — Core 1.0 stable gate and portfolio handoff

**Decision.** `TL-0710` is the only gate that completes Team B/B1. It freezes Core 1.0 artifacts and release evidence and supplies the minimal portfolio handoff.

**Rationale.** A controlled pilot is not an independently supported stable release.

**Implementation constraint.** The gate requires lifecycle, offline-core, same-machine modest-hardware, accessibility, security/privacy, licence, data-preservation, sample, and release-interface evidence. It sends Team B to Scam Explainer and does not authorize B4. Release wording must not imply cross-hardware certification.

**Revisit trigger.** Only by a portfolio-owner decision that changes the queue or stable-release standard.

<a id="d-057"></a>

### D-057 — Core 1.0 accessibility and backup completion boundary

**Decision.** Core 1.0 includes recipient-controlled accessibility setup and basic operating-system backup onboarding/restore verification, but excludes personal account creation, automatic recovery-key custody, a custom backup engine, Backup Circle control, and broader recipient application integration.

**Rationale.** These are meaningful standalone device-handover capabilities while advanced domain behavior belongs to later products.

**Implementation constraint.** Recipient-present actions are explicit, reversible where supported, verified, and secret-minimizing. Sealed handover records personal setup as pending.

**Revisit trigger.** Broader onboarding or advanced backup requires a later project or milestone with its own threat/data decisions.

<a id="d-058"></a>

### D-058 — Portfolio v2.1 development baseline

**Decision.** ThirdLife Setup Core adopts the ThirdLife Software Portfolio v2.1 baseline dated 15 August 2026: GitHub-continuous development, efficient single-machine validation, staged risk-based testing, and late-binding suite assembly.

**Rationale.** The portfolio-level document supersedes the earlier assumption that projects require a device lab or repeated broad test execution while preserving the standalone Team B/B1 boundary.

**Implementation constraint.** Bundle metadata, task contracts, Codex prompts, test evidence, and release wording must follow v2.1. A lower-authority repository document cannot reintroduce a lab or external runtime dependency.

**Revisit trigger.** Only through a later human-approved portfolio baseline and synchronized project-bundle amendment.

<a id="d-059"></a>

### D-059 — Active Codex machine is the only physical validation hardware

**Decision.** The active Codex machine is the only physical machine used for implementation, tests, benchmarks, clean environments, manual walkthroughs, cold-boot evidence, and release evidence.

**Rationale.** The project will not maintain a device lab, lower-performance test computer, volunteer hardware pool, second-PC matrix, cloud hardware farm, or parallel external runtime environment.

**Implementation constraint.** A task may use a clean clone, worktree, VM, Windows Sandbox, container, virtual disk, or deterministic fixture only when it runs on the same physical Codex machine. Missing device/equipment classes are not blockers. No task may require another physical machine.

**Revisit trigger.** Broader hardware validation may be added only as a separate explicitly staffed activity with new evidence and claim rules; it is not an implicit release prerequisite.

<a id="d-060"></a>

### D-060 — Modest-hardware readiness is engineered, not lab-certified

**Decision.** ThirdLife is designed for modest supported hardware through streaming/chunking, bounded resource use, conservative configurable concurrency, CPU fallback, preflighted disk space, cancellation/checkpointing, bounded caches, and graceful degradation.

**Rationale.** Resource-conscious architecture prevents obvious regressions even when lower-performance hardware is unavailable.

**Implementation constraint.** Same-machine no-GPU, reduced-concurrency, low-priority, low-space, offline, interrupted-network, slow-destination, and representative-workload scenarios are used only to answer concrete risks. They do not simulate or certify a particular untested device.

**Revisit trigger.** Numerical budgets may be frozen after measurement; the engineering principles and honest-evidence rule remain.

<a id="d-061"></a>

### D-061 — Quick, targeted, full, and extended test tiers

**Decision.** Every task declares an expected test tier and explicit full/extended triggers. Quick checks guide iteration, targeted checks protect the changed subsystem, full checks run at defined gates or major-risk changes, and extended scenarios run only for the named risk or scheduled gate.

**Rationale.** Fast feedback and risk-based escalation find defects more efficiently than repeatedly running an opaque long suite.

**Implementation constraint.** `TESTING.md` governs tier contents, budgets, triggers, fail-fast behavior, evidence, and skipped-tier rationale. Completion reports state every tier run or not run and why.

**Revisit trigger.** Per-tier time budgets may change through `DECISIONS.md`; the staged principle and trigger documentation remain mandatory.

<a id="d-062"></a>

### D-062 — Focused deterministic regressions and independently invokable slow scenarios

**Decision.** Every reproducible defect is reduced to the smallest practical deterministic regression case, and each slow/failure/resource scenario is independently invokable and checkpointed when long-running.

**Rationale.** This prevents broad suites from becoming serial edge-case discovery loops and permits focused reruns after a fix.

**Implementation constraint.** Run the failed case first, then the related targeted set. Rerun the full or extended matrix only when its trigger applies or a shared cause is suspected. Flaky tests are defects, not evidence obtained by blind rerun.

**Revisit trigger.** An exception requires a documented technical reason, owner, bounded follow-up task, risk, and removal condition.

<a id="d-063"></a>

### D-063 — GitHub continuity and no remote-runtime dependency

**Decision.** GitHub is the canonical continuity record. Every meaningful checkpoint is committed and pushed, `STATUS.md` describes the handoff, and milestone/release gates include a clean-clone check on the active Codex machine. GitHub Actions or another remote runtime runner is not required or authoritative.

**Rationale.** Another machine must be able to continue from the pushed repository without turning remote CI or unpushed local state into a hidden dependency.

**Implementation constraint.** Start each session with fetch/status/divergence checks from `DEVELOPMENT_WORKFLOW.md`; end with verified push and clean-tree state. Optional remote static checks may not become release evidence or a hardware matrix.

**Revisit trigger.** Only through a documented continuity architecture change that preserves local reproducibility and the single-machine hardware scope.

<a id="d-064"></a>

### D-064 — TL-0008 draft 1 and the immediate MHT walkthrough are superseded

**Decision.** `TL-0008 draft 1`, source commit `4fa3ea050fd5e9985fde9cc8218281698d371cc8`, digest `ef150dbf14b5db208582b7b526c7e0c6d0a5b912736e9e6519b8918abcf0928b`, and its immediate physical device-pool `MHT-001`–`MHT-021` walkthrough are superseded.

**Rationale.** The old gate conflicts with decisions D-058 through D-063 and would block development on unavailable hardware rather than improve the product architecture.

**Implementation constraint.** Preserve the former procedure as a clearly marked **SUPERSEDED — DO NOT EXECUTE** historical record. Revised TL-0008 creates the test tiers, active reference-machine profile, capability/risk matrix, constraint procedures, and manual-test specification. It does not run the broad physical, accessibility, failure, low-resource, or cold-boot matrices.

**Revisit trigger.** The historical procedure never becomes active again by implication. Any future field validation is separately scoped and cannot silently change release claims.

<a id="d-065"></a>

### D-065 — Manual hardware-test workflow remains, hardware-matrix certification does not

**Decision.** ThirdLife retains guided manual functional tests because software inventory cannot prove keyboard, pointer, display, audio, camera, microphone, charging, sleep/wake, or port behavior. Development verification does not require possession of every capability or hardware class.

**Rationale.** The product workflow is useful even though the project lacks a lab. Deterministic state, evidence, pause/resume, accessibility, unavailable/failure, and report tests can prove the workflow contract.

**Implementation constraint.** Later named tasks may perform bounded operator observation of capabilities available on the active Codex machine. Absent capabilities use deterministic fixtures and explicit limitations. A workflow pass is not device certification or long-term reliability proof.

**Revisit trigger.** Separate real-world validation may add observations but cannot weaken evidence semantics or make missing hardware a hidden development dependency.

<a id="d-066"></a>

### D-066 — Hardware and resource claims are limited to observed evidence

**Decision.** Documentation and release claims distinguish design intent, synthetic/captured fixture coverage, same-machine constraint results, and actual active-machine observations. They do not claim broad modest-hardware certification or untested minimum specifications.

**Rationale.** VMs, process limits, synthetic provider data, and one physical machine are useful engineering evidence but cannot establish universal hardware support.

**Implementation constraint.** `LOW_SPEC.md`, `TESTING.md`, `RELEASE_INTERFACE.md`, release notes, support guidance, and known limitations record the reference-machine profile, constraints, workload hashes, skipped cases, and claim boundaries. Unsupported wording blocks release until narrowed.

**Revisit trigger.** Broader claims require separately collected, reviewable evidence from the named hardware scope and an approved release-interface update.

<!-- BEGIN V0.4.0 REPOSITORY STABILIZATION DECISIONS -->

Approved for application by the project owner on 30 September 2026 under
[AMD-2026-09-12-REPOSITORY-STABILIZATION](docs/amendments/2026-09-12-repository-stabilization.md).
The cumulative merge and branch/PR retirement remain pending separate owner review.

<a id="d-067"></a>

### D-067 — Completed task work is integrated before the next roadmap task

**Historical.** Superseded prospectively by [D-075](#d-075). Source continuity and history protections continue under the current development workflow. The earlier sequencing rule follows.

**Decision.** A completed task branch is integrated into the repository’s default branch
before the next roadmap task begins. A temporary stacked branch is permitted only for an
explicitly named recovery, cumulative stabilization, or approved dependency sequence, and it
must include a documented collapse plan.

**Rationale.** GitHub cannot be the continuity source of truth when the default branch remains
many completed tasks behind and multiple stale branches appear equally authoritative.

**Implementation constraint.** Every task handoff records the integration pull request or the
approved temporary-stack exception. Codex does not begin the next product task merely because
the previous task is pushed to a non-default branch. No reset, force push, or history rewrite
is used to collapse the stack.

**Revisit trigger.** Only through a documented branch-strategy decision that preserves one
unambiguous continuation line.

<a id="d-068"></a>

### D-068 — Remote CI is supplementary and never authoritative runtime evidence

**Decision.** GitHub Actions and other remote runners may perform supplementary clean-checkout,
format, build, static, and deterministic test checks, but they are not authoritative runtime,
hardware, accessibility, benchmark, milestone, pilot, or release evidence.

**Rationale.** The portfolio explicitly assigns implementation and validation to the active
Codex machine. Calling a hosted runner authoritative contradicts the single-machine evidence
boundary and can conceal environmental differences.

**Implementation constraint.** Workflow names, job names, documentation, and new task
evidence use “supplementary” or equivalent wording. Historical CI evidence remains intact but
is accompanied by a clarification. Required task and release evidence is recorded from the
active Codex machine or an allowed environment hosted on it.

**Revisit trigger.** Only through a later portfolio-level validation-hardware decision.

<a id="d-069"></a>

### D-069 — Live status and task evidence remain compact and reference durable artifacts

**Decision.** `STATUS.md` is a concise operational handoff snapshot. New `TASKS.yaml` evidence
entries are compact structured references to committed durable evidence rather than long-form
design essays or raw logs.

**Rationale.** Oversized live-control files are difficult for humans and automation to review,
increase merge conflicts, and obscure the current action.

**Implementation constraint.** Detailed immutable evidence belongs under
`artifacts/evidence/<task>/`, gate records, ADRs, or dated reviews. A compact task entry retains
task, command/review, tier, result, duration, environment, commit/reference, digest, and
limitation. Existing completed history is not rewritten without a separate migration review.

**Revisit trigger.** The evidence schema may evolve, but live control files remain bounded and
machine-readable.

<a id="d-070"></a>

### D-070 — Test execution uses reusable runners and declarative profiles

**Decision.** Repeated clean-environment, Sandbox, and scenario execution is implemented
through reusable host/guest runners and declarative profiles. New large task-specific runner
scripts require a documented technical reason and replacement/retirement plan.

**Rationale.** Copying large one-off harnesses increases maintenance cost, hides behavioral
differences, and makes test-tier execution difficult to compare.

**Implementation constraint.** Existing task-named scripts remain valid historical evidence
and may act as compatibility wrappers. New scenarios are independently invokable and report
machine-readable results. The test manifest identifies tier, budget, project/filter, hosted
environment, and risk trigger.

**Revisit trigger.** A scenario may receive a dedicated runner only when the reusable harness
cannot safely express its lifecycle or isolation needs.

<a id="d-071"></a>

### D-071 — Each evidence fact has one semantic owner or an explicit reconciliation rule

**Decision.** A normalized evidence key has one semantic owner. When two providers observe
related facts, they use distinct keys and provenance or an orchestrator applies an explicit,
versioned, deterministic reconciliation rule.

**Rationale.** Silent first-wins, last-wins, or overwrite behavior can turn conflicting
architecture, lifecycle, security, or hardware observations into false certainty.

**Implementation constraint.** Duplicate keys with incompatible meanings are rejected.
Conflict, freshness, and source preference are tested. Unknown or contradictory observations
remain unknown/review-required rather than being selected by enumeration order.

**Revisit trigger.** A source-preference rule changes only through a reviewed evidence-contract
amendment.

<a id="d-072"></a>

### D-072 — Lifecycle knowledge is versioned, dated, attributable, and stale-safe

**Decision.** Operating-system and other externally changing support-lifecycle knowledge is
stored as versioned data with source identity, revision, and an “as of” date. Providers collect
facts; lifecycle mapping remains attributable and reproducible.

**Rationale.** A hard-coded unversioned “supported” boolean becomes silently wrong as vendor
support policy changes.

**Implementation constraint.** Missing, stale, contradictory, or unrecognized lifecycle data
cannot satisfy a required support rule. Historical jobs retain the lifecycle-data revision
used. Refreshing lifecycle data does not retroactively rewrite earlier evidence or decisions.

**Revisit trigger.** Data source or refresh policy may change through a reviewed provenance and
offline-behavior decision.

<!-- END V0.4.0 REPOSITORY STABILIZATION DECISIONS -->

<a id="d-073"></a>

### D-073 — Human testing answers a concrete question and bounded assignments continue through integration

**Historical.** D-074 supersedes this testing and handoff policy prospectively. Completed work and recorded observations retain the contract and provenance under which they were collected.

**Decision.** Automated checks are the default for implementation verification. Ask for human testing only when a concrete unresolved perception, usability or physical-observation question affects a decision. Accountable owner and risk approvals require an actual decision from the designated person. Record automated results, human observations and approvals distinctly.

**Rationale.** Objective checks and technical administration can run before a review. They can establish startup, state transitions and accessibility semantics. Perceived speech, actual display usability and operator judgment need attributable observations where required.

**Implementation constraint.** Codex owns the verified build and starting state, route rehearsal, privacy-safe build and environment identity, timing, prepared settings and restoration, case mapping, and durable evidence. Every handoff identifies the question, brief action, time estimate, affected decision and retest trigger. Combine overlapping observations in one session and record each case independently. Carry forward valid unchanged evidence with original provenance, and retest failed, changed or genuinely new interactions. Required observations and approvals remain pending until received.

Keep one active task and branch at a time and merge before starting the next task under D-067. Within the expressly assigned sequence TL-0113 through TL-0116, Codex continues after integration without a separate conversational stop at each task. The prepared TL-0113 observation, the integrated TL-0116 observations and owner gate decision, a concrete external blocker, and evidenced M1 completion are the handoff points for this assignment. Later milestones retain their governing requirements and need their own execution scope.

Each diagnostic repeat identifies the changed input or expected new information. After two materially identical uninformative attempts, use a different approach or record the unresolved limitation and continue independent work. Keep triggered quality gates, active-machine validation, host security and supplementary remote CI in force.

**Approved amendment.** On 24 November 2026, project owner Janne Vuorela authorized `AMD-2026-11-24-MINIMAL-HUMAN-TESTING` and routine work through evidenced M1 completion. The [amendment record](docs/amendments/2026-11-24-minimal-human-testing.md) defines the scope, transferred TL-0114 human acceptance and evidence treatment.

**Revisit trigger.** A later bounded assignment may consolidate human procedures while preserving their explicit owners and decisions. A change to a governed acceptance criterion requires an approved synchronized amendment under D-045.

<a id="d-074"></a>

### D-074 — Codex owns technical verification and accountable decisions remain explicit

**Current.** Engineering ownership, evidence, and scope-bound decisions continue. D-075 supersedes the historical M1 assignment endpoint and the old integration and task-dependency rules.

**Decision.** Codex owns preparation, implementation, functional and regression testing, technical accessibility checks, defect resolution, evidence and integration. Completing engineering creates zero routine human QA assignments or owner sign-offs. Codex finishes objectively verifiable engineering when its acceptance evidence exists and known defects that affect required behavior are resolved. Actual observations, objective results, evidence limits, reported defects and accountable decisions retain distinct provenance.

**Rationale.** Required workflows need demonstrated outcomes through their real entry points. Perceptual and physical limits constrain claims. A candidate can be prepared and verified before a decision about using, accepting or releasing it.

**Implementation constraint.** D-074 supersedes D-073 prospectively, including its M1 observation and approval. TL-0116 is a Codex-owned technical gate for the internal audit-only baseline. Retain earlier failed attempts and unresolved defects, and record the exact coverage and limitations of the repaired route. A known failure of required behavior blocks the affected outcome. A missing perceptual observation creates an evidence limit and never by itself creates QA work for the owner.

Request exceptional human input only when available verification and prior evidence cannot resolve one specific fact, preference or authorization, and the answer changes a consequential decision now. Continue safe authorized work when the uncertainty can be stated. For a usability question, prepare a working result and limit the request to one tiny coherent action and one question under ordinary conditions. A genuine approval request identifies the prepared result, material limits and exact proposed action. Count related requests, retries and setup together. A reported defect creates engineering work and no retest obligation for its reporter.

Preserve product ownership and sanitization confirmation, UAC, recipient settings and credentials, policy and privilege-boundary decisions, and pilot/public-release consent. Codex may administer a task state based on an actual attributable scope-bound decision. A decision required before an action remains a prerequisite for that action. Technical engineering and evidence preparation can proceed where safe before a later decision about the candidate. Retain independent specialist evidence where genuinely required, limit claims within authorized scope, or hold the affected exposure.

The previous assignment ended with the integrated M1 technical gate. TL-0611 remains the actual controlled-pilot authorization and the required pilot evidence contributes to TL-0710. Keep active-machine, Full and named risk checks in force. Apply the current execution and release prerequisites in TASKS.yaml.

**Approved amendment.** On 24 November 2026, project owner Janne Vuorela directed application of the permanent testing policy reset and M1 completion in the active Codex conversation. The [amendment record](docs/amendments/2026-11-24-permanent-testing-policy-reset.md) records the scope and authority.

**Revisit trigger.** Changes affecting accountable authority or product safeguards require the applicable decision under [change control](docs/change-control.md).

<a id="d-075"></a>

### D-075 — Current product contract and capability-specific engineering

**Current.** The [product contract](docs/product-contract.md) defines product outcomes, scope, invariants and delegated engineering. Technical specifications govern their affected behavior. TASKS.yaml records live work, implementation dependencies, execution and release prerequisites, evidence and limits. ROADMAP.md describes delivery goals. Decisions, ADRs and prior evidence preserve rationale and provenance.

**Decision.** Codex can split and resequence work, refine implementation and its tests, choose supported routes and integrate verified isolated checkpoints. An unavailable execution capability blocks the action that needs it. A release prerequisite gates the corresponding claim or exposure. Keep actual authorization, evidence and access controls in force.

**Decision.** Keep the WPF UI unelevated. Use a qualified supported Windows, WinGet or vendor route for suitable approved package operations. Retain the independently validating ephemeral broker for ThirdLife-owned privileged code. Both routes require the exact approved plan, trust and scope binding, durable dispatch intent and independent postcondition verification. Preserve ordinary Windows consent and any applicable operation-specific restrictions.

**Rationale.** Separating engineering, runtime capability and release authority keeps the product moving when one route needs qualification and prevents a merged checkpoint from enabling unqualified execution.

**Approved amendment.** Project owner Janne Vuorela approved the [development contract reset](docs/amendments/2026-11-25-development-contract-reset.md) on 25 November 2026. It supersedes D-045 and D-067 prospectively, narrows D-029 and replaces the historical D-074 assignment endpoint. The current product contract identifies when a material change still requires a decision.

<a id="d-076"></a>

### D-076 — One existing guest and continued engineering across the complete timeline

**Owner direction.** On 27 November 2026, Janne Vuorela directed that only one guest will ever be made available and that unavailable technical test conditions must not halt development project-wide or across the complete timeline. This settles guest capacity; no further guest-count, alternate-baseline or second-Windows-licence question is required.

**Decision.** Use the existing `ThirdLife-W11` for every guest scenario throughout development, pilot, release and maintenance. Run scenarios sequentially. Reuse existing scoped authority for guest operations, preserve evidence before restoration, and verify the same guest identity and state. Ordinary supported servicing, repair and authorized checkpoint refresh remain engineering work on that guest. Do not create another Windows instance, clone or Windows Sandbox session, or reinstall/downgrade merely to manufacture a live test target.

**Continuation.** A missing eligible Windows Update offer limits the corresponding native download/install observation. Complete the protected handler, authorization, policy, resources, journal and recovery implementation with focused controlled cases, retain the native evidence gap, and continue independent tasks. Separate implementation dependencies from action qualification and release exposure using existing task fields. Correct a false dependency immediately; do not create a new approval stage, polling loop or owner QA assignment. Never report unavailable evidence as passed or mark unimplemented behavior complete.

**Scope and history.** This narrows earlier D-059/D-070 and historical Sandbox procedures prospectively. Completed observations retain their original source and environment; their former commands do not authorize new guests. Runtime consent, exact trust/action binding, independent verification, recovery, recipient choices and the actual TL-0611/TL-0710 exposure decisions remain scoped to the operations and claims they protect. Missing evidence does not silently remove promised product functionality.
