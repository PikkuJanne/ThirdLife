# ThirdLife Setup Core — Security Policy and Threat Baseline

**Status:** Security specification for the [current product contract](docs/product-contract.md)  
**Bundle version:** 0.8.0

## 1. Security objective

ThirdLife Setup Core is safety-sensitive Windows refurbishment software. Its security objective is not to certify a device as safe. It must collect bounded evidence, enforce an organization policy, execute only approved and attributable machine changes, independently verify outcomes, preserve an audit trail, and state limitations honestly.

Security controls must preserve the standalone project boundary. Future portfolio integration cannot be used to weaken privilege, package, data, or release controls.

## 2. Supported reporting path

The current packages are private engineering artifacts. Publicly supported
versions, the security contact and disclosure handling remain open for
the exact candidate. Repository private vulnerability reporting was
disabled at the 27 November 2026 evidence review. Before exposing a
candidate, record the approved confidential contact, expected response
handling and supported versions.

Stop the affected operation after an unexpected privileged action, trust
mismatch or possible data exposure. Keep vulnerability details, device
records, secrets, diagnostic archives and reproduction material off public
issues. Share a reviewed sanitized record through the authorized channel
when it is available. [Support guidance](docs/support.md)

## 3. Protected assets

- donor and recipient privacy;
- workshop credentials and organization access;
- device ownership and management controls;
- sanitization evidence and job audit history;
- policy, profile, and catalogue integrity;
- package identity, source, publisher, signature/hash, and resolved version;
- elevated broker authority;
- action approval, journal, reboot checkpoints, and verification state;
- release artifacts, dependency locks, SBOM, and update provenance;
- support bundle contents and destinations;
- recipient-controlled backup credentials, recovery material, and accessibility choices.

## 4. Trust boundaries

1. imported policy, profile, catalogue, and release metadata;
2. Windows/CIM/API/provider output;
3. package and update sources;
4. unelevated UI to elevated broker IPC;
5. approved package or broker-backed execution to supported installer, update and system APIs;
6. SQLite and per-job files;
7. report and support-bundle export destinations;
8. recipient-present onboarding operations;
9. later B4 adapters consuming a frozen release interface.

All external data is untrusted until validated. UI validation is never sufficient for privileged execution.

## 5. Security validation environment

All security implementation and runtime testing use the active Codex machine. The project does not require a hardware lab, second physical computer, cloud runner matrix, or another machine acting as an attack client. Cross-user, cross-integrity, replay, path, network, interruption, and package-source cases use local accounts/sessions, the one existing `ThirdLife-W11` guest, deterministic fixtures, stubs, and bounded same-machine workspaces. Run guest states sequentially; do not create another VM or Windows Sandbox instance. A missing environment condition limits only the affected observation or action. Keep actual privilege, approval, trust and recovery requirements for that action.

This constraint does not weaken the threat model. It limits the evidence claim: a passed local adversarial scenario proves the named control and environment, not every hardware, Windows, endpoint-security, network, or organizational configuration. Missing external hardware is not a blocker. Missing coverage of a relevant threat prevents only the affected security claim or dispatch until appropriate same-machine tests, reasoned review, or an applicable scope-bound risk decision resolves it. Continue independent engineering; missing coverage creates no routine owner QA or generic sign-off requirement.

Security tests follow `TESTING.md`: quick checks during iteration, targeted checks after a security/privilege/data boundary change, full regression at defined gates, and extended adversarial or endurance cases only when the changed risk or release gate requires them. Every reproducible defect receives the smallest practical deterministic regression case.

## 6. Security invariants

- The main UI runs unelevated.
- Operations that require ThirdLife-owned privileged code run through an ephemeral broker for the approved batch.
- Suitable package operations may use a qualified supported Windows, WinGet or vendor route with the operating system's consent and elevation. Qualify the route for the exact action and scope. Missing qualification, trust or consent keeps that dispatch unavailable.
- Every route loads the durable approved plan, checks material identity and trust, commits correlated dispatch intent before mutation, and obtains a fresh independent postcondition. Unavailable broker execution leaves independent package resolution, installed-state queries and separately qualified routes available.
- The broker accepts typed, versioned, bounded, expiring, nonce-protected, digest-bound allowlisted requests.
- No profile or catalogue can supply shell commands, PowerShell, arbitrary executable paths, arbitrary URLs, arbitrary registry paths, or unrestricted file operations.
- Profiles are declarative data. Executable behavior stays in reviewed compiled code.
- No permanent LocalSystem service is installed for Core 1.0.
- Unsupported Windows, activation, firmware-password, MDM, Autopilot-style, ownership, or anti-theft controls are never bypassed.
- Source, package ID, publisher, architecture, version, and available signature/hash evidence are verified and recorded.
- Security hash overrides and unreviewed source substitution are not available through the product.
- `applied` is not `verified`.
- Unknown or unavailable evidence is never treated as passed.
- Assessment access requires an append-only sanitization-gate decision bound to the newest committed sanitization evidence ID and exact policy version. A missing or stale decision, unknown or failed evidence, or an archived job blocks access without a bypass.
- No sibling repository, private database, background service, or active branch is a runtime or build dependency.

## 7. Privileged broker requirements

For actions that require ThirdLife-owned privileged code, the broker must independently validate:

- initiating user/session and restrictive IPC ACL;
- protocol version, schema, message size, collection bounds, and unknown fields;
- random session nonce, expiry, correlation IDs, job/action IDs, and replay state;
- approved-plan/content digest and material-change reapproval;
- action type and every parameter;
- package/catalog identity and source policy;
- path normalization, allowed roots, object type, reparse points, junctions, and symlinks;
- cancellation, timeout, result, and partial-write semantics.

Tests must cover cross-user connection, replay, stale request, oversized input, protocol downgrade, unknown action, argument injection, path traversal, junction/symlink attacks, unsafe temporary files, broker termination, UI termination, and UAC decline.

## 8. Package and update supply chain

- Use a small reviewed catalogue, not unrestricted package search.
- Use exact package identities and approved sources.
- Resolve and record exact metadata before approval.
- Require reapproval when resolved material changes.
- Use supported structured Windows, WinGet or vendor contracts. If the supported mechanism requires process invocation, use a fixed trusted executable with bounded typed arguments and structured or invariant status. Never parse localized WinGet display tables.
- A supported installer may elevate via Windows. Keep invocation inputs within the compiled allowlist. Independently verify package identity, version and scope after execution.
- Make the verified artifact and its staging/launch path resistant to substitution at the privilege boundary. Keep the broker unavailable until its own protected startup, identity, IPC and required privilege evidence are verified.
- Keep the package backend replaceable behind a tested adapter.
- Track licence-to-use and right-to-redistribute separately.
- Pin runtime dependencies and release inputs; produce an SBOM.
- Verify release artifacts and third-party downloads where supported and record checksums/provenance regardless.
- Do not silently fetch an unpinned “latest” binary at runtime.

B1 uses generic essentials and synthetic packages. Sibling products enter only through a future B4 compatibility cut against frozen artifacts.

The [dependency inventory](docs/supply-chain/dependencies.md) and [licence matrix](docs/supply-chain/license-matrix.csv) record provenance, integrity, deterministic SBOM generation, audit limits and proposed rights. The current matrix includes reviewed Native AOT build inputs for local engineering and a scoped direct-vendor package route. Release use awaits exact candidate review. Janne Vuorela's prior approvals retain their original 24- and 28-component matrix scope, exact commit and digest, documented limits and withheld rights.

## 9. Data, secrets, and logging

Never place the following in command arguments, ordinary logs, crash reports, support bundles, task evidence, or telemetry:

- passwords, tokens, credentials, recovery keys, encryption keys, or clipboard secrets;
- donor/recipient content;
- usernames, email addresses, Wi-Fi identifiers, personal paths, or full serials in sanitized output;
- package download URLs unless explicitly reviewed;
- unbounded raw provider or installer output;
- Backup Circle repository credentials or keys;
- Charity Cyber Check evidence;
- sibling-application user content.

Use structured allowlisted fields and stable result codes. Redact before persistence, not only at export. Support bundles are previewable and use an allowlist.

An independent package route may fetch only the exact reviewed release artifact. Send no
job, device or operator identity to the package source. Keep any staged installer transient,
bounded and access-restricted, and remove only the route's own files. Record the package,
scope, attempt, normalized result and verification in the guarded job store. Keep staged
paths, redirect URLs and raw installer output out of the workshop report and diagnostics.
Recipient and support projections keep their existing allowlists.

The subordinate [`docs/privacy/privacy-model.md`](docs/privacy/privacy-model.md), [`docs/privacy/logging-standard.md`](docs/privacy/logging-standard.md), and synthetic [`docs/privacy/redaction-test-cases.yaml`](docs/privacy/redaction-test-cases.yaml) define the current `TL-0005` classification, approved retention guidance, diagnostic-field, and expected-redaction contracts. Named privacy-owner approval is recorded for the exact reviewed commit. `TL-0104` implements the first runtime subset: a closed typed event schema, explicit sensitive-value wrappers, redact-before-file-persistence, sanitized exception records/display guidance, an exact fixture-only 25-field in-memory support projection, and protected bounded local sanitized-log retention. The log root and files have restrictive ACLs and validated identities; instances/processes share a bounded lock; corrupt, linked, replaced, reparse, wrong-clock, expired, or over-count objects fail closed; crash residue is recovered without broad deletion. An append becomes a bounded durable `.txn-*` intent before any final record is evicted; the transaction is revalidated immediately before eviction, and the next locked operation validates and either completes an in-window transaction or deletes a verified expired intent, so final `evt-*` records remain within their configured age/byte ceiling and an ambiguous committed write is never reported as a safe blind retry. Verified orphan temporaries are removed before another append stages bytes, preventing ordinary repeated pre-commit crashes from accumulating residue. These controls detect bounded accidental/concurrent replacement but do not make current-user or administrator-controlled local files tamper-proof; a detected change stops mutation and preserves the object for review. Windows active-machine deletion is bound to the validated open file handle; the portable fallback remains path-bound after revalidation and makes no non-Windows production or equivalent deletion-race assurance. Raw output, arbitrary diagnostic fields, database logging, telemetry and upload remain outside the local sanitized diagnostics path. The TL-0104 checkpoint deferred preview UI, archive creation, destination handling and release support.

The TL-0606 Handover route previews both allowed support files and binds local
publication to the exact approved ZIP. After an interrupted export, the reopened
job verifies the existing ZIP against its saved digest in the selected original
folder. A missing, inaccessible or changed copy keeps the export unconfirmed.
Release support and confidential transfer require the applicable approved route.
[Support export and recovery evidence](artifacts/evidence/TL-0606/reopened-support-recovery.md)

## 10. Filesystem and persistence

The TL-0114 [audit workspace](docs/ui/audit-workspace.md) opens the registered local job
store lazily under the standard-user token. External sanitization is an attributable
workshop record. Assessment requires a current allowing decision and separately rejects
synthetic evidence before any live provider dispatch. Fixture composition has its own
store and roster. Manual notes are bounded, workshop-restricted data and are not logged.

The TL-0115 [workshop report](docs/ui/audit-workspace.md#workshop-report) uses a separate
allowlisted workshop projection and protected local output. Opening uses a bounded,
digest-verified artifact in the built-in preview. It does not open a caller path or
run a shell handler. Publication never replaces an existing file. Recipient and
support output policies retain their separate boundaries.

TL-0113's [review shell](docs/ui/accessible-shell.md) requests `asInvoker` and checks the
current token before constructing the main window. Elevated or unverified sessions receive
fixed recovery guidance and exit. The shell performs no job-store, provider, network, broker,
logging or export operation. Navigation and progress presentation create no domain evidence,
gate decision or approval. Unknown evidence remains explicit; no raw exception is displayed.

`TL-0112` adds [read-only assessment orchestration](docs/inventory/assessment-orchestration.md).
Each start/provider/final batch revalidates its exact allowing sanitization decision inside
the existing transaction. Closed system-generated markers bind immutable provider evidence
to explicit snapshots; collisions, malformed correlation and ambiguous writes fail closed.
Only registered standard-user/no-network providers are admitted. Cancellation and timeout
retain worker admission through cleanup, including late cleanup failure. No migration,
new raw-data permission, execution surface or export allowlist is introduced.

`TL-0111` adds [network and key-device enumeration](docs/inventory/device-enumeration.md).
Fixed local numeric WMI projections, SetupAPI class/interface lists and a session digitizer
metric return only class presence, collection state, bounded problem counts and numeric errors.
No SSID, IP, MAC, device name, instance ID, endpoint path or media content is requested or
retained. Sources expose no mutation, network request or configurable query. Class failures
remain unknown; uncertain cleanup blocks reuse. Existing log/support allowlists are unchanged.

`TL-0110` adds [firmware/TPM observations](docs/inventory/firmware-security.md) through fixed local read-only APIs, WMI projections and one no-input readiness method. Denied access stays unknown without elevation. Bounded raw TPM values retain provenance; no owner authorization, keys or arbitrary text is read. Cleanup failure blocks further use of that provider instance. Firmware indicators do not establish clear ownership, and no firmware or TPM mutation is exposed.

`TL-0109` adds a [fixed battery-report operation and temporary-data contract](docs/inventory/battery-report.md).
Only the system powercfg battery XML command can run; no caller command, path or option is
accepted. Its suspended process joins a kill-on-close job before resuming. Raw XML stays in
an operation-owned protected directory, is streamed through a bounded parser and is removed
by validated file identity. Restart recovery accepts only the durable ownership record;
uncertain cleanup blocks further collection. Numeric lifecycle codes are the only failure
details retained. This is a narrow temporary-file operation, with no raw attachment or
export permission. Same-user/local-administrator compromise remains outside the ACL boundary.

`TL-0108` adds a [read-only storage source and normalization contract](docs/inventory/storage.md).
Fixed local WMI projections and their explicit physical-disk/counter association collect
bounded numeric facts. Routing identifiers are transient and excluded from normalized
snapshots, logs and exports. System-volume metadata uses only the Windows-provided local
volume; no caller path or disk-content read is admitted. The provider exposes no repair,
optimization, counter reset, write test or general WMI method surface. Unknown data and source
errors remain explicit, and reported health is not a diagnosis or certification.

`TL-0107` adds read-only OS, activation, and pending-reboot providers. Their
[source and privacy contract](docs/inventory/os-activation-reboot.md) records the closed native
API set, numeric activation-only projection, metadata-only reboot checks, bounded workers,
sanitized failures, and stale-safe lifecycle data. No licensing operation, key/account
collection, registry write, shell command, or network request is introduced. Activation is
not proof of legal licence ownership; missing reboot indicators are not proof that no restart
is needed.

- `TL-0102` implements the registered local store at `%LOCALAPPDATA%\ThirdLife\SetupCore\JobStore`; arbitrary roots are available only to internal deterministic tests.
- A new database is migrated, fingerprinted, integrity-checked, and closed at a restrictive random sibling path before a no-overwrite same-directory handle rename publishes it. A crash therefore leaves either no final database or a complete identified database. Bounded, exactly named initialization residue is reconciled only after ACL, final-path, object-type, link-count, reparse-point, identity, and size checks.
- SQLite migrations are embedded, explicit, versioned, transactional, and bound to immutable script and resulting-schema SHA-256 values in the migration ledger. Application ID, version, ledger, schema, quick-integrity, and foreign-key checks fail closed for unrelated, truncated, newer, altered, or corrupt stores.
- The database, persistent rollback journal, store root, `jobs` root, and per-job directories use protected ACLs limited to the current user, LocalSystem, and local Administrators. Long-lived handles pin expected identities; hard links, reparse points, junctions, symlinks, alternate unexpected journal modes, path replacement, and unsafe ancestors are rejected.
- Per-job directories derive from SHA-256 of validated internal job IDs rather than recipient or device names. `TL-0102` stores no attachments in them; a later typed attachment contract remains required before any file payload is admitted.
- Current hard ceilings are 10,000 jobs, 10,000 evidence records, 10,000 sanitization-gate decisions, and 10,000 checkpoints per job, 256 records per evidence batch, 64 KiB per normalized JSON payload, and 256 MiB each for the database and rollback journal. SQLite `max_page_count` and before/after operation checks enforce the database bound; exceeding a bound fails closed.
- The implemented journal recovery path has a narrow startup interval between verified preflight and the steady-state no-delete-share journal guard. This is a same-user/local-administrator residual rather than a cross-user boundary; no custom SQLite VFS is introduced at `TL-0102`.
- Archive and restore append lifecycle checkpoints and retain evidence and gate decisions. Explicit archived-job deletion saves an operator-approved checkpoint and exact local file manifest. It verifies generated registered paths, file identity, current ownership, access rules, link count and digest on a handle that excludes competing file use. SQLite retains the approved operation and job records when a recorded file is refused. A retry reconciles already absent files before committing job-record removal. Protected local report and guide ownership is saved before publication. [Job-data lifecycle](docs/uninstall-and-data-preservation.md)
- Validate export destinations and prevent path traversal and unintended overwrite.
- Use atomic write/replace patterns where possible.
- Bound file size, archive expansion, record count, retries, timeouts, and concurrency.
- Preserve historical observations, policy versions, approvals, attempts, and verification; do not rewrite history to match current policy.
- On resume, re-observe actual state before retrying privileged work.

## 11. Recipient-controlled operations

Accessibility and backup actions that belong to the recipient require the recipient or authorized organization to be present and in control. ThirdLife must not create cloud identities, retain recovery secrets, or enable encryption without an approved recovery-ownership plan. A sealed handover records these items as pending.

## 12. Future B4 adapter boundary

A future adapter may use only a frozen installer, hash, `RELEASE_INTERFACE.md`, non-sensitive samples, and documented public behavior. It must not access ThirdLife Setup Core private database structures or become mandatory for Core operation. If an adapter needs a product redesign, prefer a manual fallback or narrower adapter and open a formal portfolio decision.

## 13. Security release evidence

Before Core 1.0:

- threat model and residual-risk register;
- package-route adversarial tests and broker tests for any supported broker-dependent action;
- path and export-destination tests;
- secrets/redaction fixture results;
- dependency inventory, SBOM, licence and vulnerability results;
- update, rollback/non-rollback, repair, uninstall, and data-preservation evidence;
- signed or clearly development-labelled artifacts with hashes;
- documented incident stop/use guidance;
- test tier, command/scenario, duration, active-machine profile, and relevant fixture/workload hash;
- skipped security tiers/scenarios and the reason they were not required;
- explicit confirmation that evidence came from the active Codex machine and does not imply an external hardware or network certification;
- human security and privacy approval.
