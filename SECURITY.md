# ThirdLife Setup Core — Security Policy and Threat Baseline

**Status:** Binding baseline; expanded and evidenced by roadmap tasks  
**Bundle version:** 0.3.1

## 1. Security objective

ThirdLife Setup Core is safety-sensitive Windows refurbishment software. Its security objective is not to certify a device as safe. It must collect bounded evidence, enforce an organization policy, execute only approved and attributable machine changes, independently verify outcomes, preserve an audit trail, and state limitations honestly.

Security controls must preserve the standalone project boundary. Future portfolio integration cannot be used to weaken privilege, package, data, or release controls.

## 2. Supported reporting path

A public security contact and disclosure process are **TBD before preview release**. `TL-0610` and `TL-0705` must replace this placeholder with the approved reporting route, supported versions, expected response handling, and any coordinated-disclosure policy.

Do not place vulnerability details, device records, secrets, diagnostic archives, or exploit proof in a public issue without review.

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
5. broker to package/update/system APIs;
6. SQLite and per-job files;
7. report and support-bundle export destinations;
8. recipient-present onboarding operations;
9. later B4 adapters consuming a frozen release interface.

All external data is untrusted until validated. UI validation is never sufficient for privileged execution.

## 5. Security validation environment

All security implementation and runtime testing use the active Codex machine. The project does not require a hardware lab, second physical computer, cloud runner matrix, or another machine acting as an attack client. Cross-user, cross-integrity, replay, path, network, interruption, and package-source cases are created with local accounts/sessions, local VMs or Windows Sandbox when supported, deterministic fixtures, stubs, and bounded same-machine environments.

This constraint does not weaken the threat model. It limits the evidence claim: a passed local adversarial scenario proves the named control and environment, not every hardware, Windows, endpoint-security, network, or organizational configuration. Missing external hardware is not a blocker; missing coverage of a relevant threat remains a blocker until a deterministic same-machine test, reasoned review, or explicit human-approved limitation exists.

Security tests follow `TESTING.md`: quick checks during iteration, targeted checks after a security/privilege/data boundary change, full regression at defined gates, and extended adversarial or endurance cases only when the changed risk or release gate requires them. Every reproducible defect receives the smallest practical deterministic regression case.

## 6. Security invariants

- The main UI runs unelevated.
- Privileged work occurs only through an ephemeral broker for the approved batch.
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

The broker must independently validate:

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
- Prefer structured WinGet and Windows Update APIs over localized screen parsing.
- Keep the package backend replaceable behind a tested adapter.
- Track licence-to-use and right-to-redistribute separately.
- Pin runtime dependencies and release inputs; produce an SBOM.
- Verify release artifacts and third-party downloads where supported and record checksums/provenance regardless.
- Do not silently fetch an unpinned “latest” binary at runtime.

B1 uses generic essentials and synthetic packages. Sibling products enter only through a future B4 compatibility cut against frozen artifacts.

The subordinate [`docs/supply-chain/dependencies.md`](docs/supply-chain/dependencies.md) and [`docs/supply-chain/license-matrix.csv`](docs/supply-chain/license-matrix.csv) define the current dependency classes, exact provenance/integrity records, deterministic SBOM procedure, vulnerability-review limitations, and installation/redistribution proposals. Janne Vuorela's prior approval remains bound only to its historical 24-component commit and digest. Janne Vuorela, acting as Dependency and Licence Owner, approved the current 28-component matrix for exact candidate commit `d6807937f5eff712821c7927ce1953daaa5dfeb8` and matrix SHA-256 `e85f3002175dfadc860f0d2c92de0787f52364f491e50a030c936a5421395418`. The approval accepts the proposals exactly as written: all recorded limitations and withheld rights remain binding, and no blanket redistribution permission, native SQLite redistribution, release authorization, or legal conclusion is claimed.

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

The subordinate [`docs/privacy/privacy-model.md`](docs/privacy/privacy-model.md), [`docs/privacy/logging-standard.md`](docs/privacy/logging-standard.md), and synthetic [`docs/privacy/redaction-test-cases.yaml`](docs/privacy/redaction-test-cases.yaml) define the current `TL-0005` classification, approved retention guidance, diagnostic-field, and expected-redaction contracts. Named privacy-owner approval is recorded for the exact reviewed commit; later runtime implementation and verification remain pending.

## 10. Filesystem and persistence

- `TL-0102` implements the registered local store at `%LOCALAPPDATA%\ThirdLife\SetupCore\JobStore`; arbitrary roots are available only to internal deterministic tests.
- A new database is migrated, fingerprinted, integrity-checked, and closed at a restrictive random sibling path before a no-overwrite same-directory handle rename publishes it. A crash therefore leaves either no final database or a complete identified database. Bounded, exactly named initialization residue is reconciled only after ACL, final-path, object-type, link-count, reparse-point, identity, and size checks.
- SQLite migrations are embedded, explicit, versioned, transactional, and bound to immutable script and resulting-schema SHA-256 values in the migration ledger. Application ID, version, ledger, schema, quick-integrity, and foreign-key checks fail closed for unrelated, truncated, newer, altered, or corrupt stores.
- The database, persistent rollback journal, store root, `jobs` root, and per-job directories use protected ACLs limited to the current user, LocalSystem, and local Administrators. Long-lived handles pin expected identities; hard links, reparse points, junctions, symlinks, alternate unexpected journal modes, path replacement, and unsafe ancestors are rejected.
- Per-job directories derive from SHA-256 of validated internal job IDs rather than recipient or device names. `TL-0102` stores no attachments in them; a later typed attachment contract remains required before any file payload is admitted.
- Current hard ceilings are 10,000 jobs, 10,000 evidence records, 10,000 sanitization-gate decisions, and 10,000 checkpoints per job, 256 records per evidence batch, 64 KiB per normalized JSON payload, and 256 MiB each for the database and rollback journal. SQLite `max_page_count` and before/after operation checks enforce the database bound; exceeding a bound fails closed.
- The implemented journal recovery path has a narrow startup interval between verified preflight and the steady-state no-delete-share journal guard. This is a same-user/local-administrator residual rather than a cross-user boundary; no custom SQLite VFS is introduced at `TL-0102`.
- Archive and restore append lifecycle checkpoints and never delete evidence or sanitization-gate decisions. Retention enforcement, explicit job deletion, backup/export, incompatible-migration recovery, attachment lifecycle, and uninstall cleanup remain later governed work.
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
- broker and package adversarial tests;
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
