# ThirdLife Setup Core — Minimal Release Interface Sheet

**Status:** Draft placeholder for release freeze with incomplete engineering preview  
**Bundle version:** 0.8.0  
**Pilot draft task:** `TL-0610`  
**Stable completion task:** `TL-0706`  
**Stable-release gate:** `TL-0710`

This sheet is completed for a frozen preview or stable release. It documents ordinary standalone behavior for later black-box deployment. It is **not a shared application API**, plugin contract, or permission to build a sibling adapter before B4.

At `TL-0610`, verified pilot behavior is populated and the sheet remains clearly marked preview/incomplete. At `TL-0706`, it is completed for the frozen Core 1.0 candidate. Fields that remain unknown during implementation stay **TBD**. Unsupported behavior is recorded as **Not supported** with a reason; it is not invented to make future integration appear easier.

## Observed private preview

The engineering preview is a framework-dependent Windows 11 AMD64 ZIP.
Its unpacked desktop application was observed to open Inspect under an
unelevated workshop token and exit normally. The source revision, archive
and SBOM digests are recorded with the artifact. The same-machine
private MSI observations are recorded below. Pilot exposure and stable
freeze remain open.
[Package evidence](artifacts/evidence/TL-0610/development-payload.md)
The newer unpacked engineering candidate also opened Prepare and
displayed the exact Basic and Job Seeker configuration previews.
Both previews listed the profile, catalogue and policy files. Saving
Job Seeker through the native folder picker produced the exact ZIP
shown in its preview.
[Unpacked candidate checks](artifacts/evidence/TL-0706/private-interface.md)

[Deployment](docs/pilot/deployment-rollback.md) ·
[Operator runbook](docs/pilot/operator-runbook.md) ·
[Support and incidents](docs/pilot/support-and-incident.md) ·
[Metrics and evidence](docs/pilot/metrics-export.md) ·
[Support](docs/support.md) · [Known limitations](docs/known-limitations.md)

## 1. Product identity

| Field | Value |
|---|---|
| Product family | ThirdLife |
| Active product | ThirdLife Setup Core |
| Reverse-domain application ID | TBD before preview |
| Product version | TBD at release freeze |
| Publisher | TBD before preview |
| Licence | TBD at release freeze; the TL-0006 dependency/licence matrix review does not select the final product licence |
| Platform scope (D-005) | Windows 11 client on native x64 only. Release-qualified editions, builds, and servicing window are TBD at freeze. |
| Team / queue | Team B / B1 |
| Maintenance state | Active development until `TL-0710` |

## 2. Release artifact

| Field | Value |
|---|---|
| Installer/package name | TBD |
| Artifact hash | TBD at freeze |
| Signature/verification method | TBD; development artifacts must be clearly labelled when unsigned |
| Source/offline-media location | TBD |
| Package size | TBD from release artifact |
| Immutable GitHub release tag | TBD at freeze |
| Source commit | TBD at freeze |
| Dependency-lock revision | TBD at freeze |
| SBOM/third-party notices | TBD at freeze |

`TL-0006` provides the governed sources for these still-unfrozen fields: the dependency-input digest and licence-matrix digest emitted by `eng/generate-sbom.ps1`, the generated SBOM SHA-256, the exact source revision supplied to that command, and the human licence/rights review bound to the matrix digest. These controls do not substitute development values for the release-frozen artifact, dependency lock, third-party notices, or approval.

## 3. Install, update, repair, and remove

The reviewed private 0.3.3 and 0.3.6 MSI pair passed the native
per-user lifecycle on ThirdLife-W11. Windows Installer could still
remove the previous version after a canceled upgrade. Normal upgrade, repair,
downgrade refusal and uninstall preserved the same populated archived
job and its database bytes. The private 0.3.6-to-0.3.8 upgrade
preserved the database through MSI replacement. The newer reader
migrated it to schema 22 and reopened the same archived job after
repair and uninstall. The older reader refused the migrated store
without changing its bytes. Private 0.3.8 automatically rolled back
fresh-install and repair cancellation. After canceled removal, a same-version
reinstall recovered Windows Installer registration and normal removal succeeded.
The installed window and exact release package remain open.
[Private lifecycle](artifacts/evidence/TL-0704/populated-job-lifecycle.md)
[Private schema-22 lifecycle](artifacts/evidence/TL-0704/schema-22-private-lifecycle.md)
[Private standalone audit](artifacts/evidence/TL-0709/private-candidate-boundary.md)

Prepare the production package with its release product and upgrade identity,
reviewed publisher and signature or verification method, admitted runtime
dependencies and notices, and exact source and artifact digests. Inspect the
native x64 MSI for its Windows 11 admission rule, registered per-user
resources, maintenance options and restart behavior. Verify the reviewed
payload and exact supported pilot upgrade source on ThirdLife-W11.
TL-0708 freezes the qualified bytes and evidence after the matrix and
interface checks. The private installer has its own product family and
build-specific admission.
[Private candidate ownership](artifacts/evidence/TL-0703/private-package-ownership.md)
[Dependency admission](docs/supply-chain/dependencies.md)

Qualify install, upgrade, repair and removal against D-005. Record the exact Windows 11 edition, release, build, native x64 architecture, tested artifact and limits. Document at `TL-0706`:

- normal UI or command path;
- privilege requirement and UAC behavior;
- supported silent options, if independently useful and actually supported;
- restart behavior;
- update and repair behavior;
- rollback limits and non-rollback warnings;
- data left after uninstall;
- explicit option for preserving or removing ThirdLife-owned job data;
- confirmation that sibling-application data is not removed.

No permanent privileged service is expected for Core 1.0.

## 4. Interactive launch

| Field | Value |
|---|---|
| Executable/launch path | Open `ThirdLife.UI.exe` from the verified private ZIP with the other archive files and pinned .NET Windows Desktop runtime. The installed shortcut and stable installation paths remain open. |
| Normal interactive launch | Observed under the active Windows 11 x64 host's unelevated token. [Unpacked candidate evidence](artifacts/evidence/TL-0706/private-interface.md) |
| File associations | None promised during B1 unless independently useful to Core |
| Command-line options | None promised; document only options useful to Core users, testing, automation, or support |
| Custom URI/plugin API | Not supported in B1 |

## 5. Data locations

The data map identifies the current registered application and workshop paths,
including configuration, jobs, staging, logs, reports and exports. The retention
guide records their current cleanup behavior. Ordinary uninstall preserves
workshop data. The separate **Jobs** action requires exact approval for
permanent deletion of an archived job and its attributed local files.
TL-0704 verifies the installation and removal inventory of the exact stable
package.
[Data locations](docs/data-locations.md) · [Retention](docs/retention-and-cleanup.md)
· [Uninstall and data preservation](docs/uninstall-and-data-preservation.md)

## 6. Inputs

Expected Core-owned inputs include:

- organization policy/profile/catalogue files in the validated ThirdLife formats;
- external sanitization evidence entered or imported through approved bounded fields;
- human test observations;
- supported Windows/provider evidence;
- user-selected backup destination/configuration information during recipient-present onboarding.

No sibling-project workspace or private database is a required input.

## 7. Outputs and export

Expected outputs include:

- technical workshop record;
- plain-language recipient guide;
- previewable sanitized diagnostic bundle;
- [validated local profile, policy and catalogue export](docs/profile-policy-export.md);
- non-sensitive sample artifacts for black-box tests.

For every output, document whether source data is referenced, copied, transformed, or omitted. Recipient and support outputs follow their separate privacy classes.

## 8. Offline and network behavior

Core job management, inventory, policy evaluation, manual tests, prior evidence review, and report generation must function without a ThirdLife server or account.

Network-dependent categories are shown explicitly:

- package-catalog update;
- software package download;
- Windows Update;
- optional application self-update, if implemented.

The B1 release does not promise a portfolio offline package cache or suite deployment medium; those belong to B4.

## 9. Resource behavior and hardware-evidence limits

At `TL-0706`, record:

- active reference-machine profile: Windows edition/build, CPU, installed memory, storage type/free space, GPU status, and relevant toolchain versions;
- measured startup work, elapsed time, CPU time, peak memory, temporary storage, cache growth, output size, and any long-running-operation checkpoints;
- workload/fixture identifiers and cryptographic hashes;
- conservative concurrency defaults, configurable limits, CPU/no-GPU fallback, cancellation, pause/resume, and low-resource mode;
- disk-space preflight and rollback reserve behavior;
- same-machine constraints actually used, such as no-GPU, reduced concurrency, low priority, bounded low-space volume, offline/interrupted network, or slow destination;
- test tier, command/scenario, duration, result, and skipped cases with rationale;
- observed limitations and unsupported environments.

The active Codex machine is the only physical validation hardware, and the existing `ThirdLife-W11` is the only Windows guest for the complete lifecycle. Sequential states of that guest, worktrees, isolated workspaces, process constraints, and deterministic fixtures are engineering evidence, not a cross-hardware certification. No release task may introduce an additional guest or alternate Windows-baseline requirement. Missing live evidence constrains its specific capability or claim while candidate engineering continues; it cannot establish a false pass or silently remove promised functionality. The release may state that the product is **designed for modest supported hardware** and report the observations above. It must not claim manufacturer coverage, broad modest-hardware validation, or minimum specifications that were not actually tested. See `LOW_SPEC.md` and `TESTING.md`.

## 10. Privilege and security

Document:

- exact actions requiring elevation;
- qualified Windows/WinGet/vendor routes, required consent, and their exact action limits;
- ephemeral broker lifecycle for ThirdLife-owned privileged code and no always-on privileged service;
- unavailable or unqualified action capabilities and verified limits;
- accepted untrusted inputs and validation boundaries;
- package/update provenance and verification;
- secrets handling;
- support-bundle review;
- known security limitations;
- public reporting route.

## 11. Support bundle

Document:

- how to create and preview it;
- exact allowlisted contents;
- excluded categories;
- redaction behavior;
- maximum size and retention;
- safe destination requirements.

It excludes user documents, recordings, message bodies, browser history, credentials, keys, sibling-app content, and full identifying fields by default.

## 12. Sample artifacts

The private engineering sample manifest records exact hashes, byte counts
and expected outcomes for synthetic intake, the historical workshop
report, both recipient guide variants and the sanitized support ZIP.
The job files are engineering fixtures for comparing expected states.
[Synthetic sample manifest](artifacts/evidence/TL-0706/sample-manifest.json)

At freeze, provide non-sensitive samples and hashes for:

- a synthetic job;
- accepted and blocked sanitization evidence;
- normalized assessment observations;
- policy/disposition output;
- plan/journal/verification output;
- workshop record;
- recipient guide;
- sanitized support bundle.

Samples must not create a dependency on another portfolio repository.

## 13. Source continuity

Complete for preview and stable releases:

| Field | Value |
|---|---|
| GitHub repository/access route | TBD |
| Default and release branch policy | TBD; governed by `DEVELOPMENT_WORKFLOW.md` |
| Immutable release tag and commit | TBD at freeze |
| Dependency-lock revision | TBD at freeze |
| Clean-clone setup command | TBD after implementation exists |
| External-asset restoration | TBD; use verified manifests/checksums where required |
| Quick-tier command from clean clone | TBD |
| Last verified clean-clone result | TBD; active Codex machine only |

No required release state may exist only in a local worktree, IDE setting, chat transcript, or unpushed file.

## 14. Validation evidence

At preview/stable freeze, record:

- quick, targeted, full, and extended tiers run;
- exact commands or named manual scenarios, duration, result, source commit, and relevant fixture/workload hash;
- active reference-machine profile and same-machine constraints;
- skipped tiers/scenarios and rationale;
- known flaky or quarantined tests, their owner, task, reason, and removal condition;
- accessibility, security/privacy, offline, update, repair, rollback/non-rollback, uninstall, migration, recovery, and data-preservation evidence;
- explicit statement that no external hardware matrix, second physical computer, or cross-hardware certification is implied.

## 15. Known limitations

The release sheet must state at least:

- Windows versions/builds and architectures not supported;
- unavailable or provider-dependent hardware evidence;
- hardware classes and configurations not observed on the active Codex machine;
- package/update/backend limitations;
- rollback or migration constraints;
- accessibility limitations and assistive technologies not exercised;
- same-machine constrained scenarios not run or inconclusive;
- backup/accessibility onboarding limitations;
- unsupported sibling versions and the fact that no B4 adapters are included;
- absence of security, sanitization, reliability, hardware-certification, or fraud guarantees.

## 16. Future B4 consumption

After `TL-0710`, B4 may use this frozen sheet, installer, hashes, samples, and public documentation to create a separate ThirdLife-owned catalogue entry or adapter. B4 must not infer undocumented database access or require changes to the frozen Core release.

## 17. Release contact and approval

| Field | Value |
|---|---|
| Issue/support route | TBD before preview |
| Security route | TBD before preview; see `SECURITY.md` |
| Release owner | TBD |
| Interface revision | Draft 0.8.0 |
| Approved stable version | Not yet approved |
