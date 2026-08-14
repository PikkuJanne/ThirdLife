# ThirdLife Setup Core — Minimal Release Interface Sheet

**Status:** Draft placeholder; not a published compatibility contract  
**Bundle version:** 0.2.0  
**Pilot draft task:** `TL-0610`  
**Stable completion task:** `TL-0706`  
**Stable-release gate:** `TL-0710`

This sheet is completed for a frozen preview or stable release. It documents ordinary standalone behavior for later black-box deployment. It is not a shared application API, plugin contract, or permission to build a sibling adapter before B4.

At `TL-0610`, verified pilot behavior is populated and the sheet remains clearly marked preview/incomplete. At `TL-0706`, it is completed for the frozen Core 1.0 candidate. Fields that remain unknown during implementation stay **TBD**. Unsupported behavior is recorded as **Not supported** with a reason; it is not invented to make future integration appear easier.

## 1. Product identity

| Field | Value |
|---|---|
| Product family | ThirdLife |
| Active product | ThirdLife Setup Core |
| Reverse-domain application ID | TBD before preview |
| Product version | TBD at release freeze |
| Publisher | TBD before preview |
| Licence | TBD after dependency/licence review |
| Supported operating systems | Windows 11 x64 support window to be frozen from release evidence |
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
| Source revision/dependency lock | TBD at freeze |
| SBOM/third-party notices | TBD at freeze |

## 3. Install, update, repair, and remove

Document at `TL-0706`:

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
| Executable/launch path | TBD |
| Normal interactive launch | Required |
| File associations | None promised during B1 unless independently useful to Core |
| Command-line options | None promised; document only options useful to Core users, testing, automation, or support |
| Custom URI/plugin API | Not supported in B1 |

## 5. Data locations

Exact paths are finalized from implementation evidence. The interface sheet must identify:

- application configuration;
- local policies, profiles, and catalogue metadata;
- SQLite job database(s);
- per-job attachment/evidence directories;
- cache and temporary paths;
- package/update staging metadata;
- logs;
- generated workshop/recipient reports;
- sanitized support bundles;
- migration/backup copies used by ThirdLife itself;
- retention and uninstall behavior.

ThirdLife Setup Core does not own or document sibling private data locations as if they were part of its workspace.

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
- documented local profile/policy export where implemented;
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

## 9. Resource behavior

At `TL-0706`, record measured practical CPU/RAM/storage evidence, startup and peak-memory results, temporary-space needs, no-GPU behavior, low-resource controls, and large-download warnings. Unsupported claims remain explicit. See `LOW_SPEC.md`.

## 10. Privilege and security

Document:

- exact actions requiring elevation;
- ephemeral broker lifecycle and no always-on privileged service;
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

## 13. Known limitations

The release sheet must state at least:

- Windows versions/builds and architectures not supported;
- unavailable or provider-dependent hardware evidence;
- package/update/backend limitations;
- rollback or migration constraints;
- accessibility limitations;
- low-spec configurations not physically tested;
- backup/accessibility onboarding limitations;
- unsupported sibling versions and the fact that no B4 adapters are included;
- absence of security, sanitization, reliability, or fraud guarantees.

## 14. Future B4 consumption

After `TL-0710`, B4 may use this frozen sheet, installer, hashes, samples, and public documentation to create a separate ThirdLife-owned catalogue entry or adapter. B4 must not infer undocumented database access or require changes to the frozen Core release.

## 15. Release contact and approval

| Field | Value |
|---|---|
| Issue/support route | TBD before preview |
| Security route | TBD before preview; see `SECURITY.md` |
| Release owner | TBD |
| Interface revision | Draft 0.2.0 |
| Approved stable version | Not yet approved |
