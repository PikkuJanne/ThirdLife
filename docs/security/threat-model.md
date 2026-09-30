# ThirdLife Setup Core — v0.1 Threat Model

**Status:** Approved initial model  
**Model revision:** TL-0004 approved 1  
**Draft date:** 2026-08-14  
**Security-owner approval:** **Approved**  
**Approving owner and role:** PikkuJanne — Security owner  
**Approval date:** 2026-08-14  
**Approval reference:** reviewed commit 917b5ebd5f5e4cf273a087a05dd381da54324235  
**Authority:** Derived analysis under `DECISIONS.md`, `ROADMAP.md`, `PROJECT_BOUNDARY.md`, and `SECURITY.md`

**Post-approval maintenance:** 2026-08-21 — `TL-0005` traceability/status annotation only. The approved base remains commit `917b5ebd5f5e4cf273a087a05dd381da54324235`; threats, residual decisions, and security approval were not changed or re-approved.

This document identifies security risks in the planned ThirdLife Setup Core architecture. It is not a new authority tier, a certification, or evidence that a planned control has been implemented. Frozen decisions and the canonical **Owns / Does not own** boundary prevail.

## Purpose and review posture

The model asks what can go wrong while ThirdLife collects inventory, evaluates imported catalogue/profile data, resolves packages and updates, crosses the privilege boundary, persists a journal, verifies outcomes, and produces reports or exports. It uses data-flow decomposition and STRIDE-style prompts, then maps each high-risk threat to binding decisions and one or more roadmap tasks.

Control status has three meanings:

- **Planned:** a roadmap task owns the control, but implementation and verification do not yet exist.
- **Implemented:** code or documentation exists, but the required verification evidence is incomplete.
- **Verified:** the owning task's required automated, Windows, adversarial, and human evidence exists.

At this revision, every product control in the threat register is **planned** unless a completed task is explicitly named. A task reference is a traceability link, not proof that the mitigation works. The named security owner approved this initial model and selected a mitigation treatment for each residual risk; this does not approve an implemented control or authorize a release.

## Scope

Included flows are:

1. read-only inventory and normalized evidence;
2. imported policy, profile, catalogue, and package metadata;
3. package resolution, download, privileged installation, and independent verification;
4. unelevated UI to ephemeral elevated broker communication;
5. local job store, attachments, action journal, and resume checkpoints;
6. workshop, recipient, log, and sanitized support outputs;
7. structured Windows Update scan, approval, installation, restart, and re-verification; and
8. dependency, build, package/release metadata and artifact provenance through deterministic repository verification and release freeze.

The detailed boundaries and numbered flows are in [`data-flow.md`](data-flow.md). Concrete misuse paths are in [`abuse-cases.md`](abuse-cases.md).

## Assumptions and explicit exclusions

- The supported preparation target is Windows 11 x64. Windows 10 is audit-only and cannot reach the normal ready state.
- Sanitization is an external prerequisite. Core accepts bounded external sanitization evidence; it does not erase donor media or certify that an external process was truthful.
- Firmware passwords, activation, processor/TPM/Secure Boot requirements, MDM, Autopilot-style enrollment, anti-theft state, and ownership controls are never bypassed. Detection yields a blocker or human review.
- The refurbished operating system, local API responses, provider output, local files, current clock, and even administrator-controlled state can be malformed, stale, deceptive, or compromised.
- The ordinary operator is authorized to prepare the device but can make mistakes, approve misleading content, or be socially engineered.
- A local standard user may attempt cross-user access. A local administrator or compromised kernel can defeat application-level confidentiality and tamper resistance; the product must detect or limit what it can and disclose that residual risk.
- B1 runs in a project vacuum. There is no sibling repository, service, private database, runtime process, or active-branch dependency.
- Future B4 adapter misuse is modeled only as an abuse case. This model does not design an adapter, API, shared schema, or live B1-to-B4 data flow.
- Donor/recipient content preservation, malware cleanup, existing-PC repair, imaging, and product-key installation are outside the v0.1 runtime boundary.

## Risk method

Likelihood and impact are rated **Low**, **Medium**, or **High**. Initial risk describes exposure before the planned controls. Target residual risk is a review target, not an accepted result.

| Rating | Likelihood guide | Impact guide |
|---|---|---|
| Low | Requires unusual access, timing, or a separately compromised trusted component. | Bounded inconvenience with no false safety state, privilege gain, or sensitive-data disclosure. |
| Medium | Plausible for a local user, operator mistake, malformed source, or ordinary interruption. | Recoverable job loss, limited disclosure, or action delay without uncontrolled privileged mutation. |
| High | Practical at a privileged, supply-chain, cross-user, or approval boundary, or likely under hostile input. | Unauthorized privileged action, false verified/ready state, ownership bypass, material privacy loss, or unrecoverable corruption. |

A threat is **High** when either both dimensions are at least Medium and one is High, or when a single failure could cross the privilege/ownership boundary or falsely authorize handover. Every High entry below has a roadmap task mapping. Residual acceptance belongs to the named security owner and later release gates.

## Protected assets

| Asset ID | Asset | Required property |
|---|---|---|
| AST-01 | Donor and recipient privacy | No personal content or unnecessary identifiers enter product records or exports. |
| AST-02 | Workshop credentials and organization access | No credentials, tokens, Wi-Fi secrets, or remote-support access leak or persist unexpectedly. |
| AST-03 | Ownership, activation, management, and sanitization boundaries | Evidence remains attributable and uncertain; prohibited controls are never bypassed. |
| AST-04 | Policy, profile, and catalogue integrity | Only bounded declarative data influences reviewed compiled behavior. |
| AST-05 | Package/update identity and provenance | Source, ID, publisher, version, architecture, and available trust evidence remain bound to approval. |
| AST-06 | Elevated broker authority | Only the initiating user/session's current approved allowlisted batch can execute. |
| AST-07 | Plan approval and action journal | History is attributable, append-oriented, recoverable, and never converts applied into verified. |
| AST-08 | Local job store and attachments | Records remain consistent, access-restricted, bounded, and migration-safe. |
| AST-09 | Verification and finalization state | Fresh independent evidence controls completion and handover; unknown never becomes pass. |
| AST-10 | Reports and support exports | Audience schemas stay separate and exported content matches the operator's preview. |
| AST-11 | Release and dependency integrity | Locks, SBOM, licences, source revisions, artifacts, and hashes remain attributable. |
| AST-12 | Recipient-controlled choices and recovery material | Personal settings and secrets stay under the present recipient's control; any organizational authority is explicit, documented, and limited to a governed workflow. |

## Actors and capabilities

| Actor ID | Actor | Legitimate capability | Potential misuse or limitation |
|---|---|---|---|
| ACT-01 | Workshop operator | Creates jobs, reviews evidence/plans, approves work, handles UAC, and exports outputs. | Mistake, social engineering, unsafe destination, inappropriate exception, or false confirmation. |
| ACT-02 | Present recipient; or an explicitly authorized organization only where a governed workflow permits it | A present recipient participates only in explicitly recipient-controlled setup. A separately authorized organization may act only for a documented organizational ownership/recovery decision explicitly allowed by policy. | Secrets may be disclosed accidentally; absent recipient control or explicit governed organizational authority leaves the choice pending. |
| ACT-03 | Other local standard user/session | Uses the same Windows machine without authority over the active job. | Attempts IPC, file, checkpoint, or export access and replay. |
| ACT-04 | Local administrator or compromised OS | Controls many files, processes, APIs, time sources, and security descriptors. | Can tamper with evidence or binaries and lie through providers; application controls cannot provide tamper-proof assurance. |
| ACT-05 | Malicious or compromised metadata/package source | Supplies catalogue, package, publisher, version, installer, update, or error data. | Substitution, downgrade, payload compromise, malformed/unbounded output, or stale metadata. |
| ACT-06 | Fault or interruption | Power loss, full disk, network loss, reboot, hung backend, broker/UI termination, or clock change. | Creates partial machine/store state, ambiguity, duplicate execution, or unavailable evidence. |
| ACT-07 | Support recipient/export consumer | Receives a deliberately sanitized bundle or report. | Stores or forwards exported data beyond ThirdLife's control. |
| ACT-08 | Future B4 integrator | May later consume a frozen release and public interface sheet. | Attempts private data access, mandatory coupling, unversioned/latest integration, or sibling-specific behavior in B1. |
| ACT-09 | Dependency, build-action, or release source | Supplies package metadata/artifacts or release inputs to the repository/build/release process. | Dependency confusion, compromised publisher/action, lock/SBOM/licence omission, artifact substitution, or mutable release reference. |
| ACT-10 | Developer or maintainer | Changes project-local governance, dependencies, code, data, tests, and release inputs through authorized tasks/review. | Introduces convenience coupling, weakens a boundary/check, adds an unowned execution surface, or misstates implementation/evidence. |

## Trust boundaries

The boundaries are intentionally separate; “local machine” is not a single trusted zone.

| Boundary ID | Boundary | Security consequence |
|---|---|---|
| `TB-UI` | Human/imported input into the unelevated UI and workflow process | UI input is untrusted and UI validation never authorizes privileged execution. |
| `TB-PROVIDER` | Windows/CIM/API/provider output into normalized inventory evidence | The local OS can return malformed, stale, unavailable, localized, or deceptive data. |
| `TB-BROKER` | Unelevated `P-07` journal/transition client, after an attributable UI request and durable started/dispatch-intent commit, across authenticated named-pipe IPC to the elevated ephemeral broker | Durable approval/dispatch authority never comes from UI or plan-service assertion; caller/session, ACL, nonce, expiry, correlation, protocol, size, plan digest, action type, and parameters still require independent broker validation. |
| `TB-SYSTEM` | Elevated broker to package/update/system APIs and child installer processes | Least authority, exact allowlists, bounded lifetime, structured results, and ambiguous partial-state handling are required. |
| `TB-PACKAGE-SOURCE` | Catalogue/package/update metadata and artifacts crossing a network or external-source boundary | Identity, provenance, staleness, redirects, source policy, material changes, and trust evidence require validation/reapproval. |
| `TB-JOB-STORE` | Exact unelevated callers propose observations, approvals, broker results, verification, or read projections through `P-07` to SQLite/attachments/snapshots/journals/logs/checkpoints | `P-07` alone authorizes durable transitions; the elevated backend `P-05` has no database, job, attachment, or log handle. Local access control, bounds, transactional state, migration, corruption, replay, and DB/filesystem split-state still matter. |
| `TB-EXPORT` | Report/support writer to a user-selected directory, removable device, or share | Destination type, canonical target, reparse points, overwrite, partial writes, capacity, and preview/content binding require checks. |
| `TB-RECIPIENT` | Workshop machine state to recipient-controlled personal choices/secrets | Sealed handover must defer personal setup; secrets never enter ThirdLife metadata or ordinary outputs. |
| `TB-RELEASE-SUPPLY` | Dependency/build/release metadata and artifacts into deterministic verification and freeze | Exact source/version/hash/signature where available, dependency locks, provenance, licence/SBOM records, source revision, and immutable release artifacts require review. |
| `TB-FUTURE-B4` | Frozen release artifacts/public documentation to a future separate B4 project | There is no B1 runtime edge; later use must be version-bounded, optional, public, privacy-reviewed, and retain a manual fallback. |

## Threat register

### THR-001 — Declarative data becomes arbitrary execution

**Initial risk:** High  
**Likelihood:** Medium  
**Impact:** High  
**Boundaries/flows:** `TB-UI`, `TB-BROKER`, catalogue/profile and approval flows  
**Abuse cases:** `AC-001`, `AC-002`  
**Decisions:** D-022, D-023, D-030  
**Planned controls/tasks:** `TL-0201`, `TL-0301`, `TL-0302`, `TL-0303`, `TL-0310`, `TL-0312`  
**Control status:** Planned  
**Target residual risk:** Low — only compiled bounded action types remain expressible; unknown fields/actions fail closed.  
**Review trigger:** Any new action type, metadata field that influences execution, general expression language, or backend escape hatch.

### THR-002 — Broker spoofing, cross-user access, replay, or confused-deputy execution

**Initial risk:** High  
**Likelihood:** Medium  
**Impact:** High  
**Boundaries/flows:** `TB-BROKER`, `TB-SYSTEM`; approval, request, result, cancellation, and resume flows  
**Abuse cases:** `AC-002`, `AC-003`, `AC-004`  
**Decisions:** D-029, D-030, D-031  
**Planned controls/tasks:** `TL-0303`, `TL-0307`, `TL-0309`, `TL-0310`, `TL-0311`, `TL-0312`, `TL-0313`, `TL-0404`, `TL-0609`  
**Control status:** Planned  
**Target residual risk:** Medium — same-machine privileged IPC retains implementation and compromised-OS risk even with caller/session ACLs, nonce, expiry, replay state, bounded framing, digest binding, and broker-owned allowlists.  
**Review trigger:** Protocol or authentication change, new caller model, remote IPC, persistent service proposal, or new privileged primitive.

### THR-003 — Package or update substitution after review

**Initial risk:** High  
**Likelihood:** Medium  
**Impact:** High  
**Boundaries/flows:** `TB-PACKAGE-SOURCE`, `TB-SYSTEM`; resolution, approval, download, install, and verification flows  
**Abuse cases:** `AC-005`, `AC-006`, `AC-012`  
**Decisions:** D-022, D-025, D-043  
**Planned controls/tasks:** `TL-0006`, `TL-0301`, `TL-0307`, `TL-0401`, `TL-0402`, `TL-0403`, `TL-0404`, `TL-0406`, `TL-0504`, `TL-0508`  
**Control status:** Planned  
**Target residual risk:** Medium — exact source/ID/publisher/version/architecture/scope and available trust evidence reduce substitution, but a trusted publisher or upstream service can still be compromised.  
**Review trigger:** Source/backend change, unavailable artifact hash, publisher change, catalogue rollback, redirect/cache behavior, or newly approved update class.

### THR-004 — Provider output is malformed, stale, deceptive, or treated as pass

**Initial risk:** High  
**Likelihood:** Medium  
**Impact:** High  
**Boundaries/flows:** `TB-PROVIDER`, `TB-JOB-STORE`; inventory, policy, and verification flows  
**Abuse cases:** `AC-010`, `AC-014`  
**Decisions:** D-007, D-015, D-018  
**Planned controls/tasks:** `TL-0101`, `TL-0104`, `TL-0105`, `TL-0106`, `TL-0107`, `TL-0108`, `TL-0109`, `TL-0110`, `TL-0111`, `TL-0112`, `TL-0202`, `TL-0507`  
**Control status:** Partially implemented — `TL-0105` supplies and verifies the closed typed provider/normalization boundary, provenance, value/count/source bounds, cancellation/timeout outcomes, and fail-closed unavailable/error mapping. `TL-0106` adds the first concrete Windows provider: fixed local read-only SMBIOS, installed-memory, logical-processor, and emulation-aware operating-system-architecture sources; a 1 MiB firmware-table ceiling; strict structural parsing; field-local text/range/cardinality rules; central/populated-processor filtering; transient buffer clearing; identity-silent implicit rendering; and deterministic unavailable/access-denied/malformed/cancellation/timeout coverage. Later Windows providers, cross-provider conflict/freshness policy, persistence, policy evaluation, and fresh re-observation remain planned in their owning tasks.  
**Target residual risk:** Medium — typed normalization, provenance, bounds, timeouts, explicit unavailable state, and fresh re-observation cannot make a compromised OS truthful.  
**Review trigger:** New provider/API, parser, temporary report, localized fallback, or claim based on a single weak source.

### THR-005 — Job store or journal tampering produces false history or unsafe resume

**Initial risk:** High  
**Likelihood:** Medium  
**Impact:** High  
**Boundaries/flows:** `TB-JOB-STORE`; observation, approval, transition, attachment, migration, and resume flows  
**Abuse cases:** `AC-003`, `AC-009`, `AC-011`  
**Decisions:** D-028, D-032, D-033  
**Planned controls/tasks:** `TL-0102`, `TL-0103`, `TL-0205`, `TL-0308`, `TL-0309`, `TL-0313`, `TL-0408`, `TL-0409`, `TL-0505`  
**Control status:** `TL-0102` implements and verifies the bounded protected SQLite store, transactional migrations, integrity/identity checks, append-only evidence/checkpoints, and reversible archive projection. `TL-0103` adds and verifies an append-only sanitization-gate decision bound transactionally to the newest committed evidence and exact policy version; cancellation, process interruption, tamper, restart, stale-evidence, and ordered cross-instance write cases fail closed without rewriting history. Later approval/action journals, attachment handling, broader resume semantics, retention, and recovery controls remain planned.  
**Target residual risk:** Medium — restrictive ACLs, explicit migrations, transactional monotonic transitions, authenticated job/action-bound checkpoints, and re-observation limit ordinary corruption but do not make local records tamper-proof against an administrator.  
**Review trigger:** Schema migration, multi-process writer, attachment format, checkpoint format, retention change, or rollback feature.

### THR-006 — Path, reparse-point, temporary-file, or destination attacks redirect access

**Initial risk:** High  
**Likelihood:** Medium  
**Impact:** High  
**Boundaries/flows:** `TB-JOB-STORE`, `TB-SYSTEM`, `TB-EXPORT`; attachment, temp, action, and export flows  
**Abuse cases:** `AC-007`, `AC-018`  
**Decisions:** D-030, D-035  
**Planned controls/tasks:** `TL-0102`, `TL-0109`, `TL-0303`, `TL-0310`, `TL-0311`, `TL-0312`, `TL-0602`, `TL-0606`, `TL-0609`  
**Control status:** Planned  
**Target residual risk:** Medium — allowed roots, final-object/type checks, reparse/junction/symlink rejection, restrictive temporary creation, bounded atomic writes, and safe overwrite policy reduce but cannot eliminate hostile-filesystem behavior.  
**Review trigger:** Any new file operation, user-selected destination, archive format, removable/share target, or privileged path.

### THR-007 — Logs, records, reports, or support exports disclose sensitive data

**Initial risk:** High  
**Likelihood:** Medium  
**Impact:** High  
**Boundaries/flows:** `TB-PROVIDER`, `TB-JOB-STORE`, `TB-EXPORT`, `TB-RECIPIENT`; error, persistence, report, preview, and export flows  
**Abuse cases:** `AC-008`, `AC-018`  
**Decisions:** D-014, D-036, D-037, D-053  
**Planned controls/tasks:** `TL-0005`, `TL-0104`, `TL-0115`, `TL-0207`, `TL-0308`, `TL-0407`, `TL-0604`, `TL-0605`, `TL-0606`, `TL-0609`  
**Control status:** `TL-0005` supplies the named-owner-approved contract, and `TL-0104` implements and verifies the local diagnostics subset: safe sensitive-value representations, exception sanitization before the first sink, canonical protected files, ACL/root/link/reparse/path-replacement checks, cross-process locking, exact fixture execution, a fixture-only 25-field support projection, telemetry absence, bounded transaction-before-eviction recovery, and explicit post-commit ambiguity. The overall threat remains partially implemented: database/provider logging integration, report rendering, production digest provenance, support preview/archive/destination/export controls, and later subsystem-specific logging integrations remain planned.  
**Target residual risk:** Medium — allowlisted schemas and redaction before persistence reduce leakage; an operator can still export to an unsafe destination or forward an approved artifact.  
**Review trigger:** New logged field, raw backend/provider capture, crash reporting, export field, report audience, or telemetry proposal.

### THR-008 — Interruption causes blind replay, duplicate mutation, or false verification

**Initial risk:** High  
**Likelihood:** Medium  
**Impact:** High  
**Boundaries/flows:** `TB-BROKER`, `TB-SYSTEM`, `TB-JOB-STORE`, `TB-PACKAGE-SOURCE`; execution, result, journal, restart, and verification flows  
**Abuse cases:** `AC-009`, `AC-012`  
**Decisions:** D-032, D-033, D-034  
**Planned controls/tasks:** `TL-0308`, `TL-0309`, `TL-0311`, `TL-0313`, `TL-0405`, `TL-0408`, `TL-0409`, `TL-0503`, `TL-0505`, `TL-0509`, `TL-0510`  
**Control status:** Planned  
**Target residual risk:** Medium — durable checkpoints, actual-state reconciliation, retry limits, separate verification, and requires-review states cannot guarantee rollback for every installer/update or sudden power loss.  
**Review trigger:** New mutation, retry policy, restart behavior, non-rollback action, or ambiguous backend result.

### THR-009 — Windows Update applies unapproved work or fails to converge truthfully

**Initial risk:** High  
**Likelihood:** Medium  
**Impact:** High  
**Boundaries/flows:** `TB-PACKAGE-SOURCE`, `TB-SYSTEM`, `TB-JOB-STORE`; scan, approval, install, restart, rescan, and cold-boot verification flows  
**Abuse cases:** `AC-012`  
**Decisions:** D-025, D-032, D-034  
**Planned controls/tasks:** `TL-0503`, `TL-0504`, `TL-0505`, `TL-0507`, `TL-0509`, `TL-0510`, `TL-0609`  
**Control status:** Planned  
**Target residual risk:** Medium — structured APIs, explicit approved classes/identities, finite convergence, restart checkpoints, and fresh scans still inherit Windows Update service, policy, WSUS, driver/firmware, and rollback limitations.  
**Review trigger:** Update class expansion, driver/firmware update proposal, service/WSUS assumptions, convergence-limit change, or recovery-material implication.

### THR-010 — False sanitization or ownership evidence enables an unsafe workflow

**Initial risk:** High  
**Likelihood:** Medium  
**Impact:** High  
**Boundaries/flows:** `TB-UI`, `TB-PROVIDER`, `TB-JOB-STORE`; intake, evidence, policy, and finalization flows  
**Abuse cases:** `AC-013`  
**Decisions:** D-007, D-009, D-035  
**Planned controls/tasks:** `TL-0103`, `TL-0107`, `TL-0110`, `TL-0112`, `TL-0506`, `TL-0601`, `TL-0602`, `TL-0607`, `TL-0609`  
**Control status:** `TL-0103` implements and verifies the fail-closed sanitization-gate subset: decisions bind the newest committed sanitization evidence ID and its exact policy version, and missing, stale, unknown, failed, or archived state cannot authorize assessment. Ownership/management detection, finalization, handover, and the remaining external-truth mitigations are still planned; Core does not independently prove an external sanitization act or legal ownership.  
**Target residual risk:** Medium — attributable evidence, explicit unknown/failed states, ownership/management blockers, and human review cannot independently prove an external sanitization act or legal ownership.  
**Review trigger:** Any proposal to erase media, remove management/ownership controls, install keys, or convert external confirmation into an automated guarantee.

### THR-011 — Early cross-project coupling or later B4 misuse breaks isolation

**Initial risk:** High  
**Likelihood:** Medium  
**Impact:** High  
**Boundaries/flows:** `TB-FUTURE-B4`, `TB-JOB-STORE`, `TB-RECIPIENT`; no B1 runtime flow is authorized  
**Abuse cases:** `AC-015`, `AC-016`  
**Decisions:** D-048, D-049, D-050, D-051, D-053, D-055  
**Planned controls/tasks:** `TL-0003`, `TL-0009`, `TL-0301`, `TL-0302`, `TL-0303`, `TL-0304`, `TL-0508`, `TL-0703`, `TL-0706`, `TL-0709`  
**Control status:** Planned; `TL-0003` verified the initial governance documents, naming rules, and task-graph boundary. Comprehensive product dependency, catalogue/profile, data-access, and release audits remain planned.  
**Target residual risk:** Low — B1 has no live edge; future B4 may use only exact frozen artifacts and public documentation, with optional version-bounded behavior and a manual fallback.  
**Review trigger:** Sibling name in active catalogue/profile/code/tests, shared schema/service proposal, private data access, “latest” compatibility, or mandatory adapter behavior.

### THR-012 — Resource exhaustion or hostile rendering denies safe completion

**Initial risk:** Medium  
**Likelihood:** Medium  
**Impact:** Medium  
**Boundaries/flows:** `TB-PROVIDER`, `TB-BROKER`, `TB-JOB-STORE`, `TB-EXPORT`; parse, IPC, storage, render, and archive flows  
**Abuse cases:** `AC-002`, `AC-008`, `AC-010`, `AC-014`  
**Decisions:** D-030, D-037  
**Planned controls/tasks:** `TL-0104`, `TL-0105`, `TL-0112`, `TL-0310`, `TL-0312`, `TL-0510`, `TL-0604`, `TL-0606`, `TL-0707`  
**Control status:** `TL-0104` implements and verifies the diagnostics resource-bound subset: diagnostic values, event fields, serialized records, final-record age/bytes, every non-lock root entry, pending operations, and cross-instance/process concurrency are bounded; tests cover pre-commit residue cleanup, committed recovery, age cutoffs, degraded capacity, corrupt/wrong-clock input, full-disk injection, and Windows handle-bound deletion. The overall threat remains partially implemented. The portable fallback is path-bound and supplies no non-Windows production/deletion-race assurance; provider parsing, UI rendering, archives, broader workload measurements, and the remaining task-specific resource controls remain planned.  
**Target residual risk:** Low — when enforced, depth/count/byte/time/concurrency bounds, streaming, cancellation, escaping, and partial-output cleanup reduce resource exhaustion and fail closed before false completion; workloads beyond measured and published bounds remain `RR-008`.  
**Review trigger:** New parser, renderer, archive, raw attachment, or large fixture class.

### THR-013 — Misleading presentation or social engineering obtains unsafe approval

**Initial risk:** High  
**Likelihood:** Medium  
**Impact:** High  
**Boundaries/flows:** `TB-UI`, `TB-PACKAGE-SOURCE`, `TB-BROKER`; plan preview, UAC, and approval flows  
**Abuse cases:** `AC-005`, `AC-017`  
**Decisions:** D-020, D-025, D-029  
**Planned controls/tasks:** `TL-0304`, `TL-0305`, `TL-0306`, `TL-0307`, `TL-0313`, `TL-0403`, `TL-0508`  
**Control status:** Planned  
**Target residual risk:** Medium — complete plain-language impact/source/privilege/restart/verification preview and material-change reapproval reduce, but cannot eliminate, deliberate operator approval of harmful trusted content.  
**Review trigger:** Hidden action, reduced preview, batch approval expansion, new exception class, or UAC prompt that cannot be tied visibly to the approved plan.

### THR-014 — Dependency, build, or release provenance is incomplete or substituted

**Initial risk:** High  
**Likelihood:** Medium  
**Impact:** High  
**Boundaries/flows:** `TB-RELEASE-SUPPLY`; dependency resolution, build verification, packaging, signing/development labeling, evidence collection, and release freeze flows  
**Abuse cases:** `AC-019`  
**Decisions:** D-043  
**Planned controls/tasks:** `TL-0002`, `TL-0006`, `TL-0610`, `TL-0704`, `TL-0706`, `TL-0708`, `TL-0709`, `TL-0710`  
**Control status:** Deterministic SDK/direct-package pins and locked restore are verified by `TL-0002`; dependency ownership/licence/SBOM, release-artifact provenance, lifecycle proof, and freeze evidence remain planned.  
**Target residual risk:** Medium — immutable inputs, lock/SBOM/licence/provenance review, exact source revision, reproducible verification, hashes/signatures where available, and release-gate review reduce but cannot eliminate compromise of an approved upstream or build environment; see `RR-002`.  
**Review trigger:** Dependency/action/tool/source/version change, lock drift, missing licence/SBOM record, build-environment change, artifact rebuild, signature/development-label change, or release candidate replacement.

## Control-to-roadmap summary

| Control family | Binding decisions | Primary owning tasks | Required proof later |
|---|---|---|---|
| Non-executable metadata and compiled allowlist | D-022, D-023, D-030 | `TL-0201`, `TL-0301`–`TL-0304`, `TL-0310`, `TL-0312` | Schema adversarial tests, DTO/static review, broker unknown-action tests. |
| Approval and material-change binding | D-025, D-030, D-032 | `TL-0306`, `TL-0307`, `TL-0310`, `TL-0313`, `TL-0403` | Mutation tests for every material field and digest-bound execution evidence. |
| Authenticated ephemeral privilege | D-029, D-030, D-031 | `TL-0310`–`TL-0313` | Cross-user/session, pipe ACL, replay, expiry, process lifetime, UAC decline, crash tests. |
| Package/update trust | D-024, D-025, D-034, D-043 | `TL-0006`, `TL-0401`–`TL-0407`, `TL-0504`, `TL-0508` | Source/substitution/hash/publisher/version/architecture tests, SBOM/licence review, independent verification. |
| Journal, resume, and verification truth | D-032, D-033 | `TL-0308`, `TL-0309`, `TL-0406`, `TL-0408`, `TL-0409`, `TL-0505`, `TL-0507`, `TL-0509` | Failure injection at transition boundaries, restart reconciliation, cold-boot evidence. |
| Privacy-separated outputs | D-014, D-036, D-037 | `TL-0005`, `TL-0104`, `TL-0604`–`TL-0606`, `TL-0609` | Adversarial redaction, hostile rendering, preview/export digest, destination/path tests. |
| Sanitization/ownership/finalization blockers | D-007, D-009, D-035 | `TL-0103`, `TL-0506`, `TL-0601`, `TL-0602`, `TL-0607`, `TL-0609` | Unknown/failed evidence and management/ownership indicators block; bypass absence review. |
| Project-vacuum and frozen-release boundary | D-048–D-055 | `TL-0003`, `TL-0009`, `TL-0508`, `TL-0703`, `TL-0706`, `TL-0709` | Dependency/data-access/catalog/profile/release audit with no live sibling edge. |
| Dependency, build, and release provenance | D-043 | `TL-0002`, `TL-0006`, `TL-0610`, `TL-0704`, `TL-0708` | Exact reviewed inputs/locks, ownership/licence/SBOM, clean-build/source identity, and immutable artifact/hash/freeze evidence. |

## Residual-risk register

These residuals were reviewed with the initial model. Each owner-decision cell uses `Accept`, `Mitigate`, `Avoid`, `Transfer`, or `Block`, followed by an em dash and a concrete rationale, condition, owner, or gate. Approval of this model is not release authorization.

| Residual ID | Linked threats | Proposed residual and affected asset | Detection, recovery, or manual fallback | Review trigger / blocking gate | Owner treatment/decision |
|---|---|---|---|---|---|
| `RR-001` | `THR-002`, `THR-004`, `THR-005`, `THR-006` | A compromised OS or local administrator can subvert local IPC/filesystem checks, falsify provider output, or tamper with local records; the audit is not tamper-proof. | Caller/session controls, final-object checks, cross-source evidence, provenance, fresh re-observation, physical/human tests, and visible uncertainty; stop/use external investigation when compromise is suspected. | IPC/path/provider/trust change; blocks `TL-0609` and `TL-0710` if unowned. | Mitigate — retain the planned caller, path, provenance, re-observation, and human checks; re-review at the listed trigger and block `TL-0609` or `TL-0710` while unowned. |
| `RR-002` | `THR-001`, `THR-003`, `THR-014` | A curated catalogue, signed publisher, package/dependency/build source, action, or update service can itself be compromised; a schema/reviewer can miss dangerous declarative meaning; and some backends may not expose strong artifact hashes. | Strict non-executable schema/action registry, adversarial/reviewer checks, exact metadata/provenance/locks, signature/hash where available, catalogue/release freeze, reapproval, SBOM/licence/vulnerability review, independent verification, and emergency stop. | Catalogue/schema/action/source/publisher/dependency/build/release trust change or active incident; blocks applicable package/release gates. | Mitigate — require the planned schema, provenance, lock, reapproval, SBOM, licence, vulnerability, freeze, and independent-verification controls before each applicable gate. |
| `RR-003` | `THR-003`, `THR-008`, `THR-009` | Installers and Windows Update can make non-transactional machine changes; power loss may leave ambiguous state without safe rollback. | Journal attempted/applied state, actual-state reconciliation, requires-review outcome, documented non-rollback/recovery, cold boot and re-verification. | New mutation or rollback assumption; blocks applicable write/release gate. | Mitigate — require durable journaling, actual-state reconciliation, truthful requires-review outcomes, documented recovery, and cold-boot re-verification before write and release gates. |
| `RR-004` | `THR-013` | An authorized operator can approve a harmful but plausibly presented action or UAC prompt. | Complete accessible preview, exact source/impact/privilege display, material-change reapproval, operator training, and stop path. | Approval UX or action scope change; blocks `TL-0314`/release if high-severity ambiguity remains. | Mitigate — require complete accessible preview, exact impact and privilege disclosure, reapproval, training, and a stop path; block `TL-0314` or release on high-severity ambiguity. |
| `RR-005` | `THR-010` | External sanitization or ownership evidence can be false even when recorded correctly. | Attributable confirmation, policy blocker for unknown/conflict, human review, no erase/unlock/bypass capability, and truthful limitation in reports. | Evidence-source change or ownership conflict; blocks handover and release. | Mitigate — keep sanitization and ownership external, attributable, conflict-blocking, and human-reviewed with no erase, unlock, or bypass capability; unresolved conflict blocks handover and release. |
| `RR-006` | `THR-007` | After an operator exports an approved report/bundle, ThirdLife cannot control later copying, storage, or disclosure. | Minimize and preview content, bind preview digest to export, warn about destination, allow secure manual transfer, and record only sanitized export metadata. | Export schema/destination change; blocks `TL-0609` if minimization is inadequate. | Mitigate — minimize and preview export content, bind preview to export, warn about destination handling, and block `TL-0609` if the reviewed privacy controls are inadequate. |
| `RR-007` | `THR-011` | A future integrator may ignore the frozen-release/manual-fallback boundary outside this repository. | Public boundary documentation, exact release hashes/interface, no private runtime interface, optionality, independent disablement, and future portfolio-owner review. | B4 starts or a compatibility claim is proposed; blocks `TL-0709`/`TL-0710` if boundary is violated. | Mitigate — preserve the frozen-release, public-interface, optionality, disablement, and manual-fallback boundary; re-review in B4 and block `TL-0709` or `TL-0710` on violation. |
| `RR-008` | `THR-012` | Inputs or workloads beyond measured and published byte/count/depth/time/disk/concurrency limits may still exhaust a supported device or leave recoverable partial state. | Conservative hard limits, preflight/headroom, streaming, cancellation, bounded cleanup, representative low-spec/adversarial measurements, and documented manual recovery. | New parser/renderer/archive/workload class or limit change; blocks `TL-0707`/release if unbounded or unmeasured. | Mitigate — require conservative limits, preflight headroom, streaming, cancellation, bounded cleanup, measurements, and manual recovery; unbounded or unmeasured release workloads block `TL-0707`. |

## Security-owner review and approval

The reviewer must examine the exact Git revision and record:

- name and security-owner role;
- review date and exact commit/reference;
- scope and assumptions accepted or corrected;
- every High threat and its task/control mapping;
- a decision for each `RR-001` through `RR-008`, including conditions or required changes;
- any new threat, owner, blocking task, test, or release-gate condition; and
- explicit result: approved, approved with recorded conditions, or changes required.

**Current review result:** Approved. The named security owner approved this initial model and recorded a treatment for every residual risk; this is not release authorization and does not replace later security reviews.

## Maintenance triggers

Update this model when a task changes a trust boundary, adds a provider/parser/action/network source/file operation/export field, implements a mitigation, discovers an abuse case, accepts a residual, changes recovery behavior, or prepares a release review. Preserve stable threat and residual IDs; record supersession rather than silently deleting history.

## External reference baseline

- [OWASP Threat Modeling Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Threat_Modeling_Cheat_Sheet.html) — system decomposition, trust boundaries, DFDs, threat identification, response, and review.
- [Microsoft: Named Pipe Security and Access Rights](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights) — explicit security descriptors, access checks, and logon-SID/session restriction.
- [Microsoft: Reparse Points and File Operations](https://learn.microsoft.com/en-us/windows/win32/fileio/reparse-points-and-file-operations) — filesystem behavior that differs from ordinary path assumptions.
- [MITRE CWE-77](https://cwe.mitre.org/data/definitions/77.html) and [CWE-22](https://cwe.mitre.org/data/definitions/22.html) — command-injection and path-traversal weakness classes.
