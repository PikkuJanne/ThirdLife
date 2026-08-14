# ThirdLife Setup Core — Binding Development Roadmap

**Bundle version:** 0.2.0  
**Portfolio roadmap:** ThirdLife Software Portfolio v2.0 (14 August 2026)  
**Project / queue:** ThirdLife Setup Core — Team B / B1  
**Pilot target:** v0.1 controlled partner pilot at `TL-0611`  
**Standalone release target:** ThirdLife Setup Core 1.0 at `TL-0710`  
**Future suite project:** ThirdLife Deployment and Suite Assembly — Team B / B4  
**Primary platform:** supported Windows 11 x64  
**Implementation baseline:** C# / .NET 10 / WPF / SQLite

## 1. Purpose and authority

This roadmap is the binding implementation plan for **ThirdLife Setup Core**, not for the complete future ThirdLife suite. It converts the product concept and the portfolio boundary architecture into a project-local sequence that Codex can execute without reading or depending on sibling repositories.

The project must deliver a useful standalone PC-assessment and preparation product first. Portfolio-specific catalogue entries, sibling-application profiles, file-opening adapters, compatibility cuts, and offline suite media are intentionally deferred to the later Team B/B4 project and may consume only frozen releases and public release documentation.

Apply repository documents in this order:

1. `DECISIONS.md` — frozen product, architecture, safety, portfolio-boundary, and delivery decisions.
2. `ROADMAP.md` — binding scope, milestone sequence, release gates, and evidence model.
3. `PROJECT_BOUNDARY.md` — canonical Team B/B1 ownership, non-goals, data boundary, and late-integration contract.
4. `SECURITY.md` — security objective, threat boundaries, reporting requirements, and release controls.
5. `ACCESSIBILITY.md` — operator and recipient accessibility requirements and evidence rules.
6. `LOW_SPEC.md` — resource budgets, constrained-test method, and graceful-degradation requirements.
7. `AGENTS.md` — Codex operating contract and task-execution rules.
8. `TASKS.yaml` — machine-readable executable task DAG and evidence state.
9. `CODEX_START_PROMPT.md` — copy-ready first-session and reusable Codex prompts.
10. `README.md` — bundle navigation and setup guidance.

`RELEASE_INTERFACE.md` is a release deliverable rather than a speculative API. `FUTURE_ASSEMBLY_NOTES.md` is a non-binding deferral log and cannot create B1 work. When documents conflict, stop before implementation that would cross a higher-authority boundary and request a named decision amendment.

## 2. Portfolio position and delivery posture

| Item | Binding position |
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

## 3. Binding product contract

> ThirdLife Setup Core is a local-first, auditable refurbishment workflow that helps a volunteer determine whether a sanitized used computer is suitable for deployment, applies an approved setup plan, verifies the outcome, and produces a clear handover for the recipient.

The product outcome is not merely that commands ran or packages were installed. A completed job must support a traceable statement that:

- the device entered through an accepted external sanitization state;
- observed, inferred, unavailable, and human-confirmed evidence remain distinguishable;
- a versioned organization policy produced an explainable disposition;
- every modifying action was previewed, attributable, journaled, and narrowly elevated;
- every claimed success passed an independent verification condition;
- restart, interruption, low-space, network-loss, and declined-elevation states are recoverable or truthfully blocked;
- finalization found no unresolved known workshop-residue blocker;
- workshop, recipient, and support outputs respect separate privacy boundaries; and
- limitations and unverified conditions remain visible rather than becoming a score or safety guarantee.

### 3.1 Primary users and starting condition

The primary operator is a volunteer or employee at a refurbishment charity, repair café, library, school, municipality, or community organization. The supported preparation flow starts with a sanitized/replaced/no-donor-storage device and a fresh or known Windows installation. Existing personal computers with unknown user data, malware, licences, keys, and configuration are outside this roadmap.

### 3.2 Visible workflow

**Intake → Inspect → Decide → Prepare → Verify → Handover**

The UI may allow navigation, but it may not convert missing or failed blocking evidence into a pass merely because the operator advances. Each stage exposes evidence, policy, actions, uncertainty, and the next safe recovery step.

### 3.3 Allowed dispositions

1. **Ready to prepare**
2. **Repair and retest**
3. **Human review required**
4. **Alternative operating system candidate**
5. **Do not deploy**

There is no composite health or security percentage. Every disposition cites the governing policy rule and evidence, including missing or contradictory evidence.

## 4. Project boundary

`PROJECT_BOUNDARY.md` is the canonical ownership statement. The summary below is binding but does not replace that file.

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

A change to **Owns** or **Does not own** requires an explicit portfolio-owner decision and coordinated updates to all affected binding files. Codex must not infer permission from technical convenience.

## 5. Release model

| Stage | Gate | Meaning | Does not mean |
|---|---|---|---|
| Architecture-proving write slice | `TL-0411` | One trusted package can be planned, elevated, installed, recovered, and independently verified end to end. | Pilot readiness or product completion. |
| Full machine profile | `TL-0511` | The reference profile converges through packages, updates, restarts, security checks, and cold-boot verification. | Handover authorization. |
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
| `ThirdLife.Broker` | Ephemeral elevated validation and execution over restricted authenticated IPC. | Permanent service, caller trust, unknown actions, broad token retention. |
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

- The main UI runs unelevated. Elevation is per approved action/batch through an ephemeral allowlisted broker.
- Profiles and policies are data. No profile-supplied PowerShell, command, executable path, registry path, URL, or arbitrary file operation is accepted.
- Package identity, source, publisher, version, signature/hash/provenance, licence, and verification are bound to approval.
- Material package/catalog changes invalidate approval and require a new preview.
- Secrets and sensitive content never enter process arguments, ordinary logs, crash reports, reports, support bundles, or future suite metadata.
- No device/message/organization/backup “safe” guarantee is produced when evidence cannot establish it.

### 8.3 Accessibility

- The entire primary operator journey works with keyboard only, visible focus, logical order, named controls, status announcements, scalable text, high contrast, and no color-only meaning.
- Long operations expose phase, progress, uncertainty, cancellation, and recovery.
- Recipient accessibility choices are made by the present recipient or authorized organization, with scope, preview/reversal limits, verification, and sealed-handover deferral visible.
- Limitations remain documented; no generated narrative substitutes for NVDA, Narrator, scaling, and human evidence.

### 8.4 Low-spec and graceful degradation

- No GPU is required; hardware acceleration may only be optional with a tested CPU fallback.
- Startup, idle memory, inventory duration, peak memory, temporary storage, database growth, report generation, and resume time are measured against versioned fixtures.
- Concurrency is conservative and configurable; work is streamed/chunked where relevant; no unbounded cache or permanent background indexing is introduced.
- Low-space, low-memory, no-network, slow-network, no-GPU, slow-destination, interruption, and malformed/adversarial cases fail before corruption and explain recovery.
- Hardware support claims remain provisional until physical-device evidence exists.

### 8.5 Verification and truthful state

The core action states are **planned, approved, started, applied, verified, failed, skipped, rolled back,** and **requires review**. `applied` is never synonymous with `verified`. Missing evidence remains unknown. Human confirmation is attributable and cannot be fabricated by Codex.

## 9. Machine-readable execution model

`TASKS.yaml` contains the executable DAG. Unless a user names a valid task, Codex selects one dependency-ready task using `AGENTS.md`. Normal implementation sessions may edit only `status`, `evidence`, and `blocked_reason` in existing task entries.

The strict milestone chain is:

`M0 → M1 → M2 → M3 → M4 → M5 → M6 → M7`

Every milestone gate transitively depends on all work in that milestone. Every task in a later milestone transitively depends on the previous milestone gate. Human and hybrid tasks remain at `review` or `blocked` until declared human evidence exists.

## 10. Milestones and binding task sequence

### M0 — Foundation and product contract

**Objective:** Create the governed repository, frozen standalone and portfolio boundaries, security/privacy/accessibility/low-spec baselines, pilot inputs, and verification infrastructure required before feature code expands.

**Exit criteria:**

- The repository builds from a clean checkout on the supported Windows environment.
- Product, non-goals, threat, privacy, accessibility, low-spec, dependency/license, test-device, and ADR inputs have named owners.
- The Team B/B1 project-vacuum boundary and future B4 late-binding posture are explicit and validated.
- The roadmap bundle and repository rules are internally consistent and validated.

| Task | Deliverable | Executor | Direct dependencies |
|---|---|---|---|
| `TL-0001` | Create the solution and repository scaffold | `codex` | None |
| `TL-0002` | Add deterministic verification and Windows CI | `codex` | `TL-0001` |
| `TL-0003` | Establish product, portfolio-boundary, and documentation governance | `codex` | `TL-0001` |
| `TL-0004` | Write the v0.1 threat model | `hybrid` | `TL-0003` |
| `TL-0005` | Define the privacy and logging model | `hybrid` | `TL-0003`, `TL-0004` |
| `TL-0006` | Create dependency, license, and SBOM controls | `hybrid` | `TL-0002`, `TL-0004` |
| `TL-0007` | Create synthetic pilot fixtures and reference inputs | `hybrid` | `TL-0003`, `TL-0005` |
| `TL-0008` | Define the physical-device matrix and workshop test procedure | `hybrid` | `TL-0003`, `TL-0004` |
| `TL-0009` | Record initial architecture decisions and project boundaries | `codex` | `TL-0001`, `TL-0003`, `TL-0004`, `TL-0005` |
| `TL-0010` | Validate the M0 foundation gate | `hybrid` | `TL-0002`, `TL-0003`, `TL-0004`, `TL-0005`, `TL-0006`, `TL-0007`, `TL-0008`, `TL-0009` |

**Gate:** `TL-0010` — Validate the M0 foundation gate. The gate is not complete until its automated verification and every declared human evidence item are attached.

### M1 — Audit-only vertical slice

**Objective:** Create a durable local job, enforce sanitization evidence, run a read-only assessment, collect human tests, and produce a privacy-safe workshop report.

**Exit criteria:**

- The normal flow makes no system changes and runs unelevated.
- Missing evidence remains unknown and normalized observations are deterministic.
- A representative volunteer completes the flow with accessible UI and privacy-safe persistence/logging.

| Task | Deliverable | Executor | Direct dependencies |
|---|---|---|---|
| `TL-0101` | Implement core job and evidence domain models | `codex` | `TL-0010` |
| `TL-0102` | Implement the SQLite job store and migrations | `codex` | `TL-0101` |
| `TL-0103` | Implement job lifecycle and sanitization gate services | `codex` | `TL-0102` |
| `TL-0104` | Implement structured logging and redaction | `codex` | `TL-0101`, `TL-0005` |
| `TL-0105` | Define inventory provider contracts and evidence normalization | `codex` | `TL-0101` |
| `TL-0106` | Implement device identity, CPU, memory, and architecture inventory | `hybrid` | `TL-0105` |
| `TL-0107` | Implement OS lifecycle and activation inventory | `codex` | `TL-0105` |
| `TL-0108` | Implement storage inventory and reliability evidence | `hybrid` | `TL-0105` |
| `TL-0109` | Implement battery report generation and parsing | `hybrid` | `TL-0105`, `TL-0104` |
| `TL-0110` | Implement UEFI, Secure Boot, and TPM inventory | `hybrid` | `TL-0105` |
| `TL-0111` | Implement network and key-device enumeration | `codex` | `TL-0105`, `TL-0104` |
| `TL-0112` | Implement the read-only assessment orchestrator | `codex` | `TL-0103`, `TL-0104`, `TL-0106`, `TL-0107`, `TL-0108`, `TL-0109`, `TL-0110`, `TL-0111` |
| `TL-0113` | Build the accessible six-stage WPF shell | `hybrid` | `TL-0002`, `TL-0103`, `TL-0105` |
| `TL-0114` | Implement manual hardware-test capture | `hybrid` | `TL-0101`, `TL-0111`, `TL-0113` |
| `TL-0115` | Generate the audit-only workshop report | `codex` | `TL-0112`, `TL-0114`, `TL-0104` |
| `TL-0116` | Validate the M1 audit-only gate | `hybrid` | `TL-0112`, `TL-0113`, `TL-0114`, `TL-0115` |

**Gate:** `TL-0116` — Validate the M1 audit-only gate. The gate is not complete until its automated verification and every declared human evidence item are attached.

### M2 — Explainable disposition engine

**Objective:** Evaluate versioned organization policy against immutable evidence and produce explainable dispositions, confirmations, repairs, and governed exceptions.

**Exit criteria:**

- Every disposition cites evidence and policy.
- Blocking failures cannot silently pass; exceptions remain attributable and visible.
- Historical jobs preserve the policy/result originally used.

| Task | Deliverable | Executor | Direct dependencies |
|---|---|---|---|
| `TL-0201` | Implement the versioned policy schema and validator | `codex` | `TL-0116`, `TL-0007` |
| `TL-0202` | Implement deterministic policy evaluation | `codex` | `TL-0201` |
| `TL-0203` | Implement disposition and explanation generation | `codex` | `TL-0202` |
| `TL-0204` | Implement human confirmations and governed exceptions | `codex` | `TL-0203`, `TL-0102` |
| `TL-0205` | Implement policy versioning and historical replay | `codex` | `TL-0201`, `TL-0102` |
| `TL-0206` | Implement device class, repair, and retest workflow | `codex` | `TL-0203`, `TL-0103` |
| `TL-0207` | Upgrade reports and UI to separate facts from decisions | `codex` | `TL-0203`, `TL-0204`, `TL-0205`, `TL-0115` |
| `TL-0208` | Validate the M2 explainable-disposition gate | `hybrid` | `TL-0204`, `TL-0205`, `TL-0206`, `TL-0207` |

**Gate:** `TL-0208` — Validate the M2 explainable-disposition gate. The gate is not complete until its automated verification and every declared human evidence item are attached.

### M3 — Safe planning and privilege boundary

**Objective:** Resolve declarative profiles into a complete approved plan and prove that a narrow ephemeral broker independently rejects anything outside compiled actions.

**Exit criteria:**

- Profiles/catalog entries cannot execute arbitrary commands.
- Approval binds resolved content and material changes require reapproval.
- The broker protocol, IPC, journal, and reboot checkpoints fail closed under adversarial tests.

| Task | Deliverable | Executor | Direct dependencies |
|---|---|---|---|
| `TL-0301` | Implement the application catalog schema | `codex` | `TL-0208`, `TL-0007` |
| `TL-0302` | Implement workshop and recipient profile schemas | `codex` | `TL-0208`, `TL-0007` |
| `TL-0303` | Implement the compiled action registry and contracts | `codex` | `TL-0301`, `TL-0302`, `TL-0101` |
| `TL-0304` | Implement deterministic plan resolution | `codex` | `TL-0301`, `TL-0302`, `TL-0303`, `TL-0202` |
| `TL-0305` | Implement impact and preapproval estimates | `codex` | `TL-0304` |
| `TL-0306` | Build the plan-review and approval UI | `codex` | `TL-0304`, `TL-0305`, `TL-0113` |
| `TL-0307` | Detect material plan changes and require reapproval | `codex` | `TL-0304`, `TL-0306` |
| `TL-0308` | Implement the durable action journal | `codex` | `TL-0303`, `TL-0102` |
| `TL-0309` | Implement reboot checkpoints and resume tokens | `codex` | `TL-0308`, `TL-0102` |
| `TL-0310` | Define the versioned broker protocol | `codex` | `TL-0303`, `TL-0009` |
| `TL-0311` | Implement the ephemeral broker and authenticated IPC | `hybrid` | `TL-0310`, `TL-0002` |
| `TL-0312` | Add focused broker security tests | `hybrid` | `TL-0311`, `TL-0004` |
| `TL-0313` | Connect approval, journal, UAC, broker, and result flow | `codex` | `TL-0306`, `TL-0308`, `TL-0311` |
| `TL-0314` | Validate the M3 safe-planning gate | `hybrid` | `TL-0307`, `TL-0309`, `TL-0312`, `TL-0313` |

**Gate:** `TL-0314` — Validate the M3 safe-planning gate. The gate is not complete until its automated verification and every declared human evidence item are attached.

### M4 — One package installation vertical slice

**Objective:** Install and independently verify one approved machine-wide package through the structured backend, broker, journal, recovery, and reporting path.

**Exit criteria:**

- Exact package identity/source/trust data is enforced without localized table parsing or hash bypass.
- Installer success is not accepted until independent verification passes.
- UAC decline, network loss, process termination, reboot, and retry are recoverable and idempotent.

| Task | Deliverable | Executor | Direct dependencies |
|---|---|---|---|
| `TL-0401` | Spike structured WinGet integration paths | `hybrid` | `TL-0314`, `TL-0006` |
| `TL-0402` | Implement the production package-manager adapter | `codex` | `TL-0401` |
| `TL-0403` | Enforce package resolution and trust policy | `codex` | `TL-0402`, `TL-0301`, `TL-0307` |
| `TL-0404` | Implement one approved machine-wide package action | `codex` | `TL-0403`, `TL-0313` |
| `TL-0405` | Handle progress, cancellation, timeout, and network loss | `codex` | `TL-0404` |
| `TL-0406` | Verify package installation and application launch | `codex` | `TL-0404` |
| `TL-0407` | Classify package failures and emit sanitized diagnostics | `codex` | `TL-0405`, `TL-0406`, `TL-0005` |
| `TL-0408` | Resume a package action safely after reboot or process loss | `codex` | `TL-0309`, `TL-0406`, `TL-0407` |
| `TL-0409` | Make package execution idempotent and safely retryable | `codex` | `TL-0408` |
| `TL-0410` | Complete the first write-capable end-to-end slice | `hybrid` | `TL-0409` |
| `TL-0411` | Validate the M4 package-installation gate | `hybrid` | `TL-0410` |

**Gate:** `TL-0411` — Validate the M4 package-installation gate. The gate is not complete until its automated verification and every declared human evidence item are attached.

### M5 — Full machine profile

**Objective:** Apply the reviewed pilot profile across Windows Update, multiple packages, security checks, restart phases, cold-boot verification, and the supported test matrix.

**Exit criteria:**

- The job-seeker machine profile converges on the approved Windows/device matrix.
- Essential failures and resource or ownership blockers prevent ready status.
- Final cold-boot verification records exact versions and unresolved limitations.

| Task | Deliverable | Executor | Direct dependencies |
|---|---|---|---|
| `TL-0501` | Execute dependency-aware multi-action plans | `codex` | `TL-0411` |
| `TL-0502` | Implement essential and optional action semantics | `codex` | `TL-0501`, `TL-0203` |
| `TL-0503` | Preflight disk, power, and resource requirements | `codex` | `TL-0501`, `TL-0305` |
| `TL-0504` | Implement a structured Windows Update workflow | `hybrid` | `TL-0503`, `TL-0004` |
| `TL-0505` | Coordinate maintenance phases and restart checkpoints | `codex` | `TL-0502`, `TL-0504`, `TL-0309` |
| `TL-0506` | Implement the v0.1 security baseline providers | `hybrid` | `TL-0505`, `TL-0112`, `TL-0004` |
| `TL-0507` | Verify the complete machine profile | `codex` | `TL-0505`, `TL-0506` |
| `TL-0508` | Finalize the pilot catalog, policy, and profiles | `hybrid` | `TL-0502`, `TL-0503`, `TL-0403`, `TL-0006` |
| `TL-0509` | Require final cold-boot verification | `hybrid` | `TL-0507` |
| `TL-0510` | Run the full-profile and failure-injection matrix | `hybrid` | `TL-0503`, `TL-0505`, `TL-0507`, `TL-0508`, `TL-0509` |
| `TL-0511` | Validate the M5 full-machine-profile gate | `hybrid` | `TL-0510` |

**Gate:** `TL-0511` — Validate the M5 full-machine-profile gate. The gate is not complete until its automated verification and every declared human evidence item are attached.

### M6 — Handover and controlled pilot

**Objective:** Block handover until finalization, reports, diagnostic privacy, accessibility, security/privacy review, packaging, and human go/no-go approvals support a controlled v0.1 pilot; this is not the standalone stable-release or Team B exit gate.

**Exit criteria:**

- Workshop, recipient, and diagnostic outputs respect their distinct data boundaries.
- Finalization and sign-off cannot be bypassed while blockers remain.
- The reproducible pilot package, runbook, audits, and named human approvals support a controlled partner pilot.
- The gate record states that Core 1.0 completion and portfolio handoff remain in M7.

| Task | Deliverable | Executor | Direct dependencies |
|---|---|---|---|
| `TL-0601` | Implement finalization rules and checklist state | `codex` | `TL-0511` |
| `TL-0602` | Detect bounded workshop artifacts | `codex` | `TL-0601`, `TL-0005`, `TL-0506` |
| `TL-0603` | Support assisted and sealed handover models | `codex` | `TL-0601` |
| `TL-0604` | Generate the technical workshop record | `codex` | `TL-0601`, `TL-0507` |
| `TL-0605` | Generate the plain-language recipient guide | `hybrid` | `TL-0603`, `TL-0508` |
| `TL-0606` | Build a previewable sanitized diagnostic bundle | `codex` | `TL-0602`, `TL-0407`, `TL-0604` |
| `TL-0607` | Enforce final sign-off and the blocking handover gate | `codex` | `TL-0602`, `TL-0603`, `TL-0604`, `TL-0605`, `TL-0606` |
| `TL-0608` | Complete the v0.1 accessibility audit | `hybrid` | `TL-0605`, `TL-0607`, `TL-0510` |
| `TL-0609` | Complete the v0.1 security and privacy release review | `hybrid` | `TL-0606`, `TL-0607`, `TL-0510` |
| `TL-0610` | Package the controlled pilot and operating runbook | `hybrid` | `TL-0607`, `TL-0608`, `TL-0609` |
| `TL-0611` | Validate the M6 and v0.1 controlled-pilot gate | `human` | `TL-0610` |

**Gate:** `TL-0611` — Validate the M6 and v0.1 controlled-pilot gate. The gate is not complete until its automated verification and every declared human evidence item are attached.

### M7 — Core 1.0 standalone release and portfolio handoff

**Objective:** Complete recipient-controlled accessibility and basic operating-system backup onboarding, harden the standalone product lifecycle, publish the minimal release interface, freeze exact release artifacts, and prove ThirdLife Setup Core 1.0 is independently releasable.

**Exit criteria:**

- ThirdLife Setup Core is useful, installable, updateable, repairable, removable, and recoverable without sibling applications, a ThirdLife account, or a permanent privileged service.
- Recipient accessibility and basic operating-system backup choices remain recipient-controlled, reversible where applicable, and do not leak recovery material.
- Offline core behavior, low-spec evidence, security/privacy, accessibility, update, repair, uninstall, migration, and data-preservation evidence pass the stable-release matrix.
- Exact artifacts, hashes, source revision, SBOM, licence evidence, known limitations, sanitized samples, and RELEASE_INTERFACE.md are frozen.
- Team B/B1 is complete and the next project is Scam Explainer; no B4 sibling adapter work is authorized by this gate.

| Task | Deliverable | Executor | Direct dependencies |
|---|---|---|---|
| `TL-0701` | Implement recipient-controlled accessibility setup | `hybrid` | `TL-0611` |
| `TL-0702` | Implement basic operating-system backup onboarding and restore verification | `hybrid` | `TL-0611` |
| `TL-0703` | Finalize standalone data ownership, profile export, and uninstall semantics | `codex` | `TL-0611` |
| `TL-0704` | Harden install, update, repair, uninstall, and data preservation | `hybrid` | `TL-0701`, `TL-0702`, `TL-0703` |
| `TL-0705` | Finalize stable security, accessibility, low-spec, and support documentation | `hybrid` | `TL-0701`, `TL-0702`, `TL-0704` |
| `TL-0706` | Complete the minimal release interface and black-box samples | `hybrid` | `TL-0703`, `TL-0704`, `TL-0705` |
| `TL-0707` | Run the Core 1.0 standalone stable-release matrix | `hybrid` | `TL-0701`, `TL-0702`, `TL-0704`, `TL-0705`, `TL-0706` |
| `TL-0708` | Freeze ThirdLife Setup Core 1.0 release artifacts | `hybrid` | `TL-0706`, `TL-0707` |
| `TL-0709` | Review standalone integrity and portfolio-boundary compliance | `hybrid` | `TL-0708` |
| `TL-0710` | Validate the M7 and ThirdLife Setup Core 1.0 standalone release gate | `human` | `TL-0709` |

**Gate:** `TL-0710` — Validate the M7 and ThirdLife Setup Core 1.0 standalone release gate. The gate is not complete until its automated verification and every declared human evidence item are attached.

## 11. Verification and evidence matrix

| Evidence layer | Minimum proof | Typical owner |
|---|---|---|
| Portable unit/component | Domain invariants, schema validation, deterministic policy, redaction, path safety, persistence/migration, journal transitions, report contracts. | Codex |
| Windows integration | Structured inventory/API behavior, WPF/UI Automation semantics, IPC ACL/protocol, package/update adapters, restart/resume, installer lifecycle. | Codex + Windows environment |
| Security/adversarial | Unknown actions, replay/expiry, other-user IPC, oversized input, path traversal/junctions, package-source substitution, hash mismatch, unsafe metadata, secret-bearing fixtures. | Codex + security reviewer |
| Failure injection | Network loss, full disk, process/broker/UI termination, reboot, UAC decline, stale metadata, corrupted job, backend unavailable, false-success installer. | Codex + Windows lab |
| Accessibility | Keyboard-only, focus, names/roles/states, Narrator, NVDA, 200% scaling, high contrast, reduced resolution, progress/error recovery. | Codex + accessibility reviewer/user |
| Low-spec | Constrained CPU/RAM/priority/storage/network, no GPU, representative fixtures, peak-resource regression records, graceful degradation. | Codex + physical/partner validation |
| Physical/workshop | Battery/ports/audio/video/display/input/network/sleep/charging/cold boot, operator comprehension, finalization residue. | Human |
| Release/portfolio | Installer/hash/source/SBOM/licence, offline behavior, update/repair/uninstall/data preservation, samples, known limits, `RELEASE_INTERFACE.md`, boundary review. | Release owner + portfolio owner |

Evidence must name the command or review, result, environment, date, and durable artifact. Codex must state when a Windows, physical-device, partner, licence, accessibility, security/privacy, or release approval was not performed.

## 12. Stable-release evidence package

Before `TL-0710`, freeze at minimum:

- exact installer/package, package size, cryptographic hash, signature/verification method, source revision, and dependency lock;
- SBOM, licence/redistribution review, third-party notices, and vulnerability-review record;
- migration, update, repair, rollback/non-rollback, uninstall, and data-preservation evidence;
- security, privacy, offline, low-spec, accessibility, hostile-input, failure-injection, and physical-device results;
- sanitized support-bundle sample and proof that prohibited categories are excluded;
- non-sensitive sample job/input/output artifacts and hashes;
- known limitations, supported versions/platforms, reporting/support path, and maintenance status;
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

A task-local implementation correction may proceed when it remains inside the task and frozen decisions. The following require a human-approved amendment before implementation:

- changing the product boundary, owner/team queue, supported platform, sanitization assumption, privilege model, data ownership, release gates, or frozen decision;
- adding sibling-specific behavior, a shared portfolio component, a cross-project dependency, a permanent privileged service, arbitrary execution, telemetry/default cloud dependency, or unsupported bypass;
- weakening acceptance, verification, accessibility, low-spec, security/privacy, licence, finalization, or human-evidence requirements.

An amendment preserves the old decision/history, names approver/date/rationale, identifies impacted tasks and evidence, updates all conflicting bundle files together, and reruns `python tools/validate_bundle.py`.
