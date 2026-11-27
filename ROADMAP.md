# ThirdLife Setup Core — Delivery Roadmap

**Bundle version:** 0.8.0  
**Portfolio roadmap:** ThirdLife Software Portfolio v2.1 (15 August 2026)  
**Project / queue:** ThirdLife Setup Core — Team B / B1  
**Pilot target:** v0.1 controlled partner pilot at `TL-0611`  
**Standalone release target:** ThirdLife Setup Core 1.0 at `TL-0710`  
**Future suite project:** ThirdLife Deployment and Suite Assembly — Team B / B4  
**Supported application host and workflow target:** Windows 11 client on native x64  
**Implementation baseline:** C# / .NET 10 / WPF / SQLite

## 1. Purpose

This roadmap describes delivery increments for ThirdLife Setup Core.
The [product contract](docs/product-contract.md) defines current scope and
invariants. [TASKS.yaml](TASKS.yaml) records the actual implementation graph,
execution and release prerequisites, and evidence. Milestone labels describe
outcomes. Independent implementation can proceed when its real prerequisites
are available.

## 2. Portfolio position and delivery posture

| Item | Delivery position |
|---|---|
| Product family / code identity | `ThirdLife`; production namespaces remain `ThirdLife.*`. |
| Current project | **ThirdLife Setup Core**, Team B queue position B1. |
| Development posture | Standalone **project vacuum**: one repository, one task graph, no live sibling dependency. |
| Immediate sibling-project behavior | None. Generic free essentials and synthetic packages are used during Core development. |
| Stable-release transition | After `TL-0710`, Team B moves to **Scam Explainer**, not to suite integration. |
| Future integration owner | **ThirdLife Deployment and Suite Assembly**, Team B/B4. |
| Future integration inputs | Frozen installers, exact hashes, `RELEASE_INTERFACE.md`, sample artifacts, known limitations, and public documentation. |
| Default future integration | Install, launch, open a standard user-selected file/workspace, and provide guidance; use a narrow optional adapter only when justified. |

No B1 task may depend on a PaperWorkShell, CaptionKit, Scam Explainer, Job Application Studio, Charity Cyber Check, or Backup Circle branch, schema, database, service, test fixture, or unpublished behavior. A cross-project idea is recorded in `FUTURE_ASSEMBLY_NOTES.md` and does not alter the active graph.

### 2.1 GitHub continuity and single-machine validation

The project follows the portfolio v2.1 universal operating rule:

- GitHub is the canonical continuity record. Integrate verified checkpoints under the current development workflow and record the continuation in STATUS.md. Gate evidence uses a clean clone or worktree on the active Codex machine as applicable.
- The active Codex machine is the only physical machine used for implementation, tests, benchmarks, clean environments, manual walkthroughs, cold-boot evidence, and release evidence.
- The sole Windows guest is the existing `ThirdLife-W11` VM on that machine. This is a permanent constraint through M0–M7, release, maintenance and future tasks. Do not create or request another VM, guest clone, Windows Sandbox instance or associated licence. Do not reinstall Windows or invent another baseline merely to manufacture a test target. Ordinary servicing, repair and authorized checkpoint refresh within this guest remain engineering work. Existing historical environment evidence keeps its original provenance.
- Run clean-install, upgrade, restart, recovery and resource scenarios sequentially in that guest using retained authorized states. Separate source worktrees, safe process constraints, deterministic fixtures and test data remain available without creating another Windows instance.
- No lab, second PC, lower-performance test device, volunteer hardware pool, cloud runner matrix, or missing equipment class is a project dependency or blocker.
- Modest-hardware readiness is engineered through streaming/chunking, conservative configurable concurrency, CPU fallback, bounded cache/temp/database growth, preflighted disk space, cancellation/checkpointing, and measured same-machine constraints.
- Quick and targeted checks drive iteration. Full and extended runs occur only at named gates or when a task’s risk trigger requires them.
- Each reproducible defect becomes the smallest practical deterministic regression, and slow scenarios remain independently invokable and checkpointed.
- Release wording distinguishes design intent, synthetic/captured fixtures, same-machine constraints, and actual active-machine observations. It never implies cross-hardware certification without separate evidence.

The former `TL-0008 draft 1` physical device-pool walkthrough is superseded by `D-064` and `TL-0008_TRANSITION.md`. Its source commit and digest remain historical references only.

## 3. Product outcomes

> ThirdLife Setup Core is a local-first, auditable refurbishment workflow that helps a volunteer determine whether a sanitized used computer is suitable for deployment, applies an approved setup plan, verifies the outcome, and produces a clear handover for the recipient.

A completed job must support a traceable statement that the following conditions hold.

- the device entered through an accepted external sanitization state;
- observed, inferred, unavailable, and human-confirmed evidence remain distinguishable;
- a versioned organization policy produced an explainable disposition;
- every modifying action was previewed, attributable, journaled, and run with the privileges its action required;
- every claimed success passed an independent verification condition;
- restart, interruption, low-space, network-loss, and declined-elevation states are recoverable or truthfully blocked;
- finalization found no unresolved known workshop-residue blocker;
- workshop, recipient, and support outputs respect separate privacy boundaries; and
- limitations and unverified conditions remain visible rather than becoming a score or safety guarantee.

### 3.1 Primary users and starting condition

The primary operator is a volunteer or employee at a refurbishment charity, repair café, library, school, municipality, or community organization. The supported preparation flow starts with a sanitized/replaced/no-donor-storage device and a fresh or known Windows 11 client installation on native x64. Existing personal computers with unknown user data, malware, licences, keys, and configuration are outside this roadmap.

### 3.2 Visible workflow

**Intake → Inspect → Decide → Prepare → Verify → Handover**

The UI may allow navigation, but it may not convert missing or failed blocking evidence into a pass merely because the operator advances. Each stage exposes evidence, policy, actions, uncertainty, and the next safe recovery step.

### 3.3 Allowed dispositions

1. **Ready to prepare**
2. **Repair and retest**
3. **Human review required**
4. **Do not deploy**

Historical decisions retain their original policy and disposition, including the retired alternative-operating-system result. There is no composite health or security percentage. Every disposition cites the governing policy rule and evidence, including missing or contradictory evidence.

## 4. Project boundary

See [project ownership](PROJECT_BOUNDARY.md) for the detailed boundary.

### 4.1 ThirdLife Setup Core owns

- refurbishment jobs, recorded external sanitization evidence, read-only inventory, human tests, policy evaluation, and explainable disposition;
- a small reviewed catalogue of generic free essentials and local outcome-based profiles;
- complete change-plan preview, supported package/update operations, per-action elevation, journaling, restart recovery, and independent verification;
- recipient-controlled accessibility setup and basic operating-system backup onboarding for the stable Core 1.0 boundary;
- finalization, technical workshop record, plain-language recipient guide, sanitized diagnostics, and its own application lifecycle;
- its own data locations, installer, update, repair, uninstall, migration, support, security, accessibility, and low-spec evidence.

### 4.2 ThirdLife Setup Core does not own

- donor-media erasure, Windows imaging/installation, unsupported compatibility or ownership bypasses, firmware flashing, generic optimization, registry cleaning, aggressive debloating, malware cleanup, or existing-PC repair;
- portfolio sibling workflows or data, including documents, recordings/transcripts, suspicious messages, job-search records, charity-assessment evidence, backup repositories, credentials, or recovery keys;
- a shared user-content database, central identity, portfolio content library, permanent privileged service, shared SDK, universal schema, plugin framework, or cross-repository source dependency;
- B4 catalogue entries, sibling-specific profiles, app adapters, suite compatibility, or suite deployment media.

A change to **Owns** or **Does not own** requires an explicit portfolio-owner decision and coordinated updates to the affected specifications. Codex must not infer permission from technical convenience.

## 5. Release model

| Stage | Gate | Meaning | Does not mean |
|---|---|---|---|
| Architecture-proving write slice | `TL-0411` | One approved machine-wide package is installed and independently verified through ThirdLife, with journal and report continuity. | Pilot readiness or product completion. |
| Full machine profile | `TL-0511` | The reference profile converges through packages, updates, restarts, security checks and approved same-machine VM cold-start verification. | Handover authorization. |
| Controlled v0.1 pilot | `TL-0611` | Named humans approve a bounded partner pilot with finalization, reports, audits, package, and runbook. | Team B/B1 exit, stable 1.0, or permission for sibling integration. |
| Standalone Core 1.0 | `TL-0710` | Product lifecycle, recipient-controlled accessibility/basic backup, release evidence, artifacts, and black-box interface are frozen. | B4 suite assembly or synchronized sibling releases. |

The pilot and stable-release gates are deliberately separate. Findings from the controlled pilot may inform M7 hardening, but they may not silently weaken safety, privacy, accessibility, low-spec, or project-boundary criteria.

## 6. Integration-readiness contract

During B1, “integration ready” means that ThirdLife Setup Core behaves like a responsible standalone desktop application and later supplies:

- stable identity, supported OS/architecture, publisher, version, licence, package, hash, and verification method;
- documented install, update, repair, restart, rollback/non-rollback, uninstall, and remaining-data behavior;
- documented configuration, job/workspace, cache, temporary, model/engine, log, support, and backup locations;
- ordinary input/output formats, normal interactive launch, and only independently useful command-line options;
- offline behavior, network categories, practical resource evidence, privilege/security boundaries, and known limitations;
- a sanitized previewable support bundle and non-sensitive samples with expected results.

These facts are populated as a verified preview draft in `RELEASE_INTERFACE.md` at `TL-0610` and completed for the frozen Core 1.0 candidate at `TL-0706`. The file is human-readable release documentation, not a shared application API, runtime schema, or obligation to expose private state.

### 6.1 Prohibited before B4

- no sibling-specific package entry, profile, file association, command, URI, adapter, or acceptance test;
- no testing against another team’s active branch or development build;
- no app-to-app call, sibling private database access, content indexing, or hidden shared workspace;
- no shared SDK, universal job/findings/handoff schema, monorepo mandate, or portfolio background service;
- no cross-project `depends_on` edge; and
- no release delay while waiting for another product.

## 7. Target architecture and dependency direction

| Component | Responsibility | Prohibited behavior |
|---|---|---|
| `ThirdLife.UI` | Unelevated WPF workflow; accessible evidence, plan, progress, and handover views. | Privileged execution, arbitrary command input, hidden policy logic. |
| `ThirdLife.Core` | Job, device, evidence, decision, action, verification, finalization, and handover domain contracts. | UI, Windows-provider, package-backend, or SQLite dependency. |
| `ThirdLife.Persistence` | SQLite migrations/repositories and restrictive per-job attachment storage. | Unbounded raw output, silent historical result rewrite, sibling data ownership. |
| `ThirdLife.Inventory` | Read-only structured Windows/CIM/API providers and normalized observations. | System modification, diagnosis beyond evidence, pass-on-missing behavior. |
| `ThirdLife.Policy` | Versioned deterministic rules, explanations, dispositions, confirmations, exceptions, and replay. | Evidence mutation, hidden override, retroactive historical rewrite. |
| `ThirdLife.Catalog` | Validated declarative policy/profile/catalog data, provenance, and reviewed generic essentials. | Scripts, arbitrary URLs/commands, sibling-specific B1 entries. |
| `ThirdLife.Packages` | Replaceable structured package backend, resolution, progress, errors, and installed-state queries. | Localized table parsing, unrestricted CLI arguments, hash bypass. |
| `ThirdLife.Actions` | Compiled allowlisted action types, planning, preconditions, execution contracts, and journal integration. | Profile-provided code or general shell execution. |
| `ThirdLife.Broker.Protocol` | Typed versioned requests/results, digest binding, nonce, expiry, correlation, and limits. | Shell strings, arbitrary executables/registry/URLs/files. |
| `ThirdLife.Broker` | Ephemeral elevated validation and execution of ThirdLife-owned privileged code over restricted authenticated IPC. | Permanent service, caller trust, unknown actions, broad token retention. |
| `ThirdLife.Verification` | Independent post-action, profile, restart, and cold-boot checks. | Treating backend return codes as proof. |
| `ThirdLife.Reports` | Workshop record, recipient guide, and support outputs with distinct schemas. | Secret leakage, donor/recipient mixing, unsupported safety claims. |
| `ThirdLife.Diagnostics` | Allow-listed local diagnostic capture, preview, redaction, and export. | Whole-log dumping, content scanning, sibling support-data collection. |

Default dependency direction is inward toward domain contracts. UI and infrastructure implement or call Core abstractions; Core does not depend on WPF, SQLite, WinGet, PowerShell, package CLI output, or sibling products.

## 8. Cross-cutting release requirements

### 8.1 Local-first, offline, and data ownership

- Job creation, inventory, policy evaluation, existing-record access, reports, and support preview work without a project-controlled server or account.
- Network use is separated into visible categories such as Windows Update, package metadata/download, catalogue update, and optional self-update.
- No telemetry by default. Any future measurement is explicit, minimized, separable, and does not block core use.
- ThirdLife stores only its own configuration, jobs, evidence, plans, logs, reports, and support metadata. It does not ingest sibling content or recovery material.
- Every copy, conversion, reference, export, retention, and deletion action is explicit. Uninstall cannot delete sibling workspaces.

### 8.2 Security and privilege

- The main UI runs unelevated. Suitable package actions use qualified Windows, WinGet or vendor routes with required operating-system consent. ThirdLife-owned privileged code uses the ephemeral allowlisted broker.
- Profiles and policies are data. No profile-supplied PowerShell, command, executable path, registry path, URL, or arbitrary file operation is accepted.
- Package identity, source, publisher, version, signature/hash/provenance, licence, and verification are bound to approval.
- Material package/catalog changes invalidate approval and require a new preview.
- Secrets and sensitive content never enter process arguments, ordinary logs, crash reports, reports, support bundles, or future suite metadata.
- No device/message/organization/backup “safe” guarantee is produced when evidence cannot establish it.

### 8.3 Accessibility

- The entire primary operator journey works with keyboard only, visible focus, logical order, named controls, status announcements, scalable text, high contrast, and no color-only meaning.
- Long operations expose phase, progress, uncertainty, cancellation, and recovery.
- Recipient accessibility choices are made by the present recipient or authorized organization, with scope, preview/reversal limits, verification, and sealed-handover deferral visible.
- Technical checks cover keyboard, UI Automation, assistive-technology integration and layout as supported. Record the exercised settings and tools, known defects and any unverified perceptual behavior.

### 8.4 Modest-hardware engineering and graceful degradation

- No GPU is required; hardware acceleration may only be optional with a verified CPU fallback.
- Startup work, inventory duration, peak memory, CPU/elapsed time, temporary storage, cache/database growth, report generation, output size, cancellation, and resume are measured against versioned hashed workloads on the active reference machine.
- Concurrency is conservative and configurable; work is streamed/chunked where relevant; no unbounded cache or permanent background indexing is introduced.
- Same-machine no-GPU, reduced-concurrency, low-priority, low-free-space, offline, interrupted-network, slow-destination, provider-unavailable, and representative-large-workload scenarios answer named risks without weakening correctness or safety.
- Missing hardware classes are represented through deterministic fixtures, sanitized captured provider samples, truthful unavailable states, bounded active-machine observation, or explicit unverified limitations—not through a lab dependency.
- Hardware support wording is limited to actual observations and documented constraints; VMs and simulations are never presented as certification of a specific untested device.

### 8.5 Verification and truthful state

The core action states are **planned, approved, started, applied, verified, failed, skipped, rolled back,** and **requires review**. `applied` is never synonymous with `verified`. Missing evidence remains unknown. Human confirmation is attributable and cannot be fabricated by Codex.

## 9. Engineering and prerequisites

Use TASKS.yaml for task details and evidence. An implementation dependency
supplies code, data or an interface to another task. An execution prerequisite
gates a particular action. A release prerequisite gates a particular claim or
exposure. Verified isolated checkpoints may integrate with affected unqualified
execution unavailable. Codex owns engineering verification and prepares
candidates for genuine decisions. Technical completion creates zero routine
owner QA or sign-off.

A missing test condition, including an eligible Windows Update offer, is a scoped
evidence limitation. Codex completes available code, deterministic cases and native
observations, records the exact reopening condition, and continues other engineering.
An unsupported offered update is not evidence of no available updates or a fully
patched system. Never turn a fixture pass into a native servicing claim.

The M5 graph uses already integrated update contracts for `TL-0505` maintenance
coordination and lets `TL-0506` security providers proceed independently.
`TL-0510` matrix and `TL-0605` guide engineering may use identifiable candidate
inputs; `TL-0508` adoption remains a prerequisite for the affected exposure.
`TL-0504` has an integrated protected servicing path. Native transaction qualification
requires an eligible offer in `ThirdLife-W11`, actual phase authorization and a verified postcondition.
`TL-0511`, `TL-0611` and `TL-0710` retain their actual capability and exposure
requirements without becoming barriers to unrelated candidate work.

## 10. Delivery milestones

### M0 — Foundation and product contract

**Objective:** Establish the repository, standalone and portfolio boundaries, product safeguards, pilot inputs, GitHub continuity and tiered same-machine verification.

**Exit criteria:**

- The repository builds from a clean checkout on the supported Windows environment on the active Codex machine.
- Product, non-goals, threat, privacy, accessibility, modest-hardware design, dependency/licence, reference-machine profile, test-tier, and ADR inputs have named owners.
- No lab, physical-device pool, lower-performance test machine, external runtime matrix, or missing equipment is a dependency or release blocker.
- The Team B/B1 project-vacuum boundary and future B4 late-binding posture are explicit and validated.
- The roadmap bundle, GitHub workflow, task graph, and repository rules are internally consistent and validated.

**Gate:** `TL-0010` — Validate the M0 foundation gate. Preserve the original foundation evidence and approvals.

### M1 — Audit-only vertical slice

**Objective:** Create a durable local job, enforce sanitization evidence, run a read-only assessment, capture guided human-test states, and produce a privacy-safe workshop report using deterministic fixtures and the active Codex machine.

**Exit criteria:**

- The normal flow makes no system changes and runs unelevated.
- Missing evidence remains unknown and normalized observations are deterministic.
- The isolated, actually launched WPF route starts with a fresh store and proves job creation, confirmed and saved intake, enabled Start assessment, completed read-only assessment, an attributable persisted manual fixture result, close/reopen and report agreement.
- Technical accessibility, privacy-safe persistence and logging, safe cancellation/interruption and the triggered Full gate have objective evidence. Reported defects in required behavior are resolved. Record separate limits for physical and perceptual coverage. M1 establishes an internal audit-only baseline.

TL-0113's completed shell observations retain their original provenance. TL-0114 connects durable job create/open/reopen, external sanitization capture, read-only assessment and guided manual capture to the operator UI, with automated implementation verification. TL-0115 renders the deterministic workshop report from durable job evidence and adds the operator UI path to generate and open that report.

**Gate:** `TL-0116` — Validate the M1 audit-only gate. Codex owns the launched-route regression and technical acceptance under [D-074](DECISIONS.md#d-074). Retain earlier failures and applicable unchanged observations with their provenance. The prior permanent-testing-reset implementation assignment ended at verified integration of this gate.

### M2 — Explainable disposition engine

**Objective:** Evaluate versioned organization policy against immutable evidence and produce explainable dispositions, confirmations, repairs, and governed exceptions.

**Exit criteria:**

- Every disposition cites evidence and policy.
- Blocking failures cannot silently pass; exceptions remain attributable and visible.
- Historical jobs preserve the policy/result originally used.
- New or materially changed disposition wording and exception governance receive an attributable policy-owner decision on prepared technical evidence.

**Gate:** `TL-0208` — Validate the M2 explainable-disposition gate. Codex verifies the behavior and presents only applicable policy choices for decision.

### M3 — Safe planning and privilege boundary

**Objective:** Resolve declarative profiles into a complete approved plan and verify broker-dependent privileged actions against their scoped trust boundary.

**Exit criteria:**

- Profiles/catalog entries cannot execute arbitrary commands.
- Approval binds resolved content and material changes require reapproval.
- The broker protocol, IPC, journal, and reboot checkpoints fail closed under adversarial tests.
- Accountable authority records one privilege-boundary disposition on the prepared evidence and residual risks before the authorized scope is used.

The [platform amendment](docs/amendments/2026-11-25-windows-11-only.md) supersedes the former Windows 10 live-audit criterion from TL-0107 and applies to the first executable broker. TL-0311 retains responsibility for trusted elevated startup and actual privilege and recovery evidence.

**Gate:** `TL-0314` — Prepare the technical evidence and complete the gate after `TL-0318` verifies the live application route and the security owner records the scope-bound privilege decision. Independent package engineering follows its own prerequisites.

### M4 — One package installation vertical slice

**Objective:** Install and independently verify one approved machine-wide package through an appropriate supported backend and the actual application plan, approval, journal, recovery and reporting path.

**Exit criteria:**

- Exact package identity/source/trust data is enforced without localized table parsing or hash bypass.
- Independently verify the installed package ID, version and machine scope. Save the transaction, reopen the job and confirm that its report agrees with the persisted facts.
- UAC decline, network loss, process termination, reboot, and retry are recoverable and idempotent.
- The actual first-write-capable product/security decision uses prepared isolated lifecycle, verification and recovery evidence. Any authorization required to execute a particular action still precedes that action.

TL-0401 records the backend route and its evidence in ADR 0009. The installer, scope and trust binding remain explicit. Broker qualification is required only for a route that uses it.

**Gate:** `TL-0411` — Validate the M4 package-installation gate through the launched application. Record applicable action consent and any separate first-write exposure decision against the prepared evidence.

### M5 — Full machine profile

**Objective:** Apply the reviewed pilot profile across Windows Update, multiple packages, security checks, restart phases, cold-start verification in the approved same-machine environment, and risk-relevant constrained scenarios.

**Exit criteria:**

- The job-seeker machine profile converges in the verified Windows 11 client/native-x64 state of the existing `ThirdLife-W11` guest. Clean and upgraded application states are exercised sequentially in that guest.
- Essential failures and resource or ownership blockers prevent ready status.
- Same-machine offline, low-space, no-GPU, reduced-concurrency, interruption, and failure scenarios preserve state and recovery.
- Checkpoint and approved VM cold-start verification record exact versions and environment. State any unverified physical power cycle explicitly and retain genuinely required physical product/release conditions.
- New or materially changed catalogue content retains applicable policy, application-selection, licensing and privacy decisions. Security baseline and pilot-scope dispositions belong to TL-0611.
- Evidence describes the reference machine and constraints and does not imply cross-hardware certification.

**Gate:** `TL-0511` — Validate the M5 full-machine-profile gate. Codex owns integration, fault and resource evidence and records blockers and coverage limits. A missing eligible update defers only the corresponding native servicing observation and claim; prepare this gate and continue M6/M7 engineering. Do not mark unfinished servicing implementation or unobserved installation complete.

### M6 — Handover and controlled pilot

**Objective:** Block handover until finalization, reports, diagnostic privacy, accessibility, security/privacy review, packaging, and human go/no-go approvals support a controlled v0.1 pilot; this is not the standalone stable-release or Team B exit gate.

**Exit criteria:**

- Workshop, recipient, and diagnostic outputs respect their distinct data boundaries.
- Finalization and sign-off cannot be bypassed while blockers remain.
- Codex completes recipient-guide checks, technical audits and the exact Windows 11 client/native-x64 pilot package lifecycle, rollback and runbook in sequential states of the sole existing guest before seeking consent for exposure.
- The prepared evidence and required pilot records support one coordinated go/no-go packet at TL-0611, with actual security, privacy, accessibility, licensing, release and partner decisions attributed by scope.
- The gate record states that Core 1.0 completion and portfolio handoff remain in M7.

**Gate:** `TL-0611` — Validate the M6 and v0.1 controlled-pilot gate. Actual pilot exposure requires the scoped go/no-go and partner consent. Reuse valid unchanged decisions and preserve required pilot evidence.

### M7 — Core 1.0 standalone release and portfolio handoff

**Objective:** Complete recipient-controlled accessibility and basic operating-system backup onboarding, harden the standalone product lifecycle, publish the minimal release interface, freeze exact release artifacts, and prove ThirdLife Setup Core 1.0 is independently releasable.

**Exit criteria:**

- ThirdLife Setup Core is useful, installable, updateable, repairable, removable, and recoverable without sibling applications, a ThirdLife account, or a permanent privileged service.
- Recipient accessibility and basic operating-system backup choices remain recipient-controlled, reversible where applicable, and do not leak recovery material.
- Offline core behavior, same-machine modest-hardware evidence, security/privacy, accessibility, update, repair, uninstall, migration, and data-preservation evidence pass the Windows 11 client/native-x64 stable-release matrix. Clean-install and upgraded application states run sequentially in `ThirdLife-W11`; the matrix cannot introduce a second guest or another Windows baseline.
- Exact artifacts, hashes, source revision, SBOM, licence evidence, known limitations, sanitized samples, and RELEASE_INTERFACE.md are frozen.
- Codex verifies the frozen standalone candidate before exposure. TL-0710 records actual stable-release and security/privacy/accessibility/licensing/support/portfolio decisions on the candidate and required pilot evidence.
- Team B/B1 is complete and the next project is Scam Explainer; no B4 sibling adapter work is authorized by this gate.

**Gate:** `TL-0710` — Validate the M7 and ThirdLife Setup Core 1.0 standalone release gate. Use prepared technical evidence, pilot evidence, known limits and the exact frozen candidate for the attributable stable-release and portfolio-transition decisions.

## 11. Verification and evidence matrix

| Evidence layer | Minimum proof | Typical owner |
|---|---|---|
| Quick | Formatting/static checks, bundle/schema validation, changed unit/component tests, smallest regression, shortest smoke path. | Codex |
| Targeted | Changed subsystem, nearby integrations, and only the affected Windows/data/privilege/security/accessibility/resource cases. | Codex |
| Full | Complete supported layers and core journeys in clean/upgraded environments hosted on the active machine; migration/recovery/export/repair/uninstall where applicable. | Codex |
| Extended | Individually invokable, risk-triggered large/adversarial, interruption, cold-start, restore, endurance, and resource scenarios. | Codex |
| Provider/hardware variants | Deterministic synthetic/captured provider fixtures, bounded active-machine observation, truthful unavailable states, and explicit unverified limitations. | Codex |
| Security/adversarial | Unknown actions, replay/expiry, other-user IPC, oversized input, path traversal/junctions, package-source substitution, hash mismatch, unsafe metadata, secret-bearing fixtures. | Codex, with accountable decisions at designated gates |
| Accessibility | Keyboard, focus, semantics, supported assistive-technology integration and layout, recovery, technical output checks and evidence limits. | Codex |
| Modest-hardware | Active reference-machine measurements and same-machine no-GPU/concurrency/priority/storage/network/workload constraints with claim boundaries. | Codex |
| Release/portfolio | Prepared technical evidence for clean clone, installer/hash/source/SBOM/licence, test manifest, offline behavior, lifecycle and data preservation, samples, limits, release interface and boundary audit. | Codex prepares evidence. Release and portfolio authorities decide exposure and transition. |

Evidence names task, branch/commit, tier, exact command or procedure, duration, reference-machine profile, environment/constraint, fixture/workload hash, result, cleanup, defect/limitation, and durable artifact. Completion reports state every broader tier not run and why. No evidence layer implies a physical device lab or cross-hardware certification.

## 12. Stable-release evidence package

Before `TL-0710`, freeze at minimum:

- exact Windows 11 native-x64 installer/package, package size, cryptographic hash, signature/verification method, source revision, and dependency lock;
- SBOM, licence/redistribution review, third-party notices, and vulnerability-review record;
- migration, update, repair, rollback/non-rollback, uninstall, and data-preservation evidence;
- security, privacy, offline, same-machine modest-hardware, accessibility, hostile-input, failure-injection, cold-start coverage and targeted rerun results, with separately evidenced physical cold-boot conditions where required;
- sanitized support-bundle sample and proof that prohibited categories are excluded;
- non-sensitive sample job/input/output artifacts and hashes;
- active reference-machine profile, test-tier manifest, constraint settings, workload hashes, skipped scenarios, and limits on hardware claims;
- verified Windows 11 client editions, builds and servicing coverage, known limitations, reporting/support path, and maintenance status;
- completed `RELEASE_INTERFACE.md`; and
- a signed/approved gate record confirming standalone usefulness and project-boundary compliance.

The release interface records black-box behavior. It does not grant later ThirdLife code access to the application database, private classes, job content, logs beyond the support contract, or any sibling data.

## 13. Team transition and future assembly

After `TL-0710`:

1. ThirdLife Setup Core enters bounded maintenance.
2. Team B starts **Scam Explainer** according to the portfolio queue.
3. Core defects are handled through ordinary maintenance; milestone-sized work formally reoccupies Team B’s lane.
4. Cross-project ideas remain deferred until **ThirdLife Deployment and Suite Assembly** is the active B4 project.
5. B4 selects exact frozen releases and owns catalogue entries, profiles, adapter briefs, compatibility tests, manual fallbacks, and retirement rules.
6. A future adapter that appears to require a product redesign stops at a decision point; the default response is a narrower action or manual fallback, not live cross-team coupling.

## 14. Change control

Apply the [current product contract](docs/product-contract.md) and [change control](docs/change-control.md) when adjusting implementation or proposing a material product or authority change.
