# ThirdLife Setup Core — Privacy Model

**Status:** Draft contract complete; named privacy-owner approval pending  
**Model revision:** TL-0005 review 1  
**Draft date:** 2026-08-21  
**Authority:** Derived from D-011, D-013, D-014, D-036, D-037, D-053, D-058, D-059, D-061, and D-063  
**Approval result:** Pending — this document does not satisfy the human evidence required by `TL-0005`

This model defines the privacy classes, data map, default retention guidance, output separation, and review rules that later persistence, logging, reporting, support, and uninstall work must implement. It does not claim that those controls are implemented. Higher-authority decisions and the canonical **Owns / Does not own** boundary prevail.

## Privacy invariants

1. ThirdLife Setup Core is local-first. It sends no recipient or device data to a ThirdLife service.
2. A normal workshop job uses a random internal job ID. Recipient name, email, account, address, phone number, or other identity is unnecessary.
3. Full serial numbers and other full device identifiers are restricted to the workshop record and its protected backing job data. They never appear in filenames, temporary paths, recipient guides, ordinary logs, or default support exports.
4. Workshop, recipient, and support outputs are separate schemas. A more restricted schema is never reused as the input file for a less restricted output.
5. Collection and export use field allowlists. Detect-and-redact is a defense in depth, not permission to collect arbitrary text.
6. Raw provider, command, backend, installer, exception, standard-output, and standard-error content is untrusted sensitive input before normalization, even when a sample appears harmless.
7. Passwords, credentials, tokens, recovery material, personal content, and sibling-private data are not ThirdLife data and are not retained.
8. Telemetry is off by default. There is no analytics SDK, account, stable analytics device identifier, or background uploader.
9. Unknown classification, failed redaction, or a field absent from an audience allowlist fails closed: omit or reject the field, preserve a stable bounded reason, and require review when the missing information affects the workflow.
10. Exported bytes leave ThirdLife control. The product minimizes and previews them, but cannot control later copying or disclosure.

## Classification model

Classification attaches at collection and can only move to a less restrictive audience through an explicit, tested projection. A container takes the highest-sensitivity class of any field it contains; hashing, encoding, truncating, or pseudonymizing a value does not automatically make the container public or anonymous. A record does not become safe merely because it is stored locally.

| Class | Meaning and examples | Permitted handling | Prohibited handling |
|---|---|---|---|
| `PUBLIC_REFERENCE` | Public product documentation; published schema identifiers; reviewed public catalogue metadata; wholly synthetic fixtures. | Repository, release documentation, and public samples after provenance/privacy review. | Mixing in a live job value or presenting synthetic evidence as an observed device result. |
| `OPERATIONAL_SAFE` | Random internal correlation IDs; product/build/schema versions; stable component, event, action, result, availability, and limitation codes; bounded counts, durations, and coarse resource buckets. | Allowlisted local diagnostics and, where separately listed, sanitized support output. | Free-form values, reversible encodings of restricted data, or identifiers derived from a name, serial, path, network value, or secret. |
| `WORKSHOP_RESTRICTED` | Full internal job/device identity including full serial; sanitization evidence; normalized observations; policy and profile snapshots; plans, approvals, exceptions, action/verification history; attributable operator records; exact technical limitations. | Protected local job store and the full technical workshop record; explicit access and retention policy. | Recipient guide, ordinary diagnostics, diagnostic filenames, or default support export. |
| `RECIPIENT_GUIDE` | Plain-language installed capabilities, update/file/backup/accessibility next steps, preparation date, support contact, and limitations selected for handover. | The recipient guide schema and its explicit preview/export. | Workshop accounts, donor/previous-owner detail, full device identifiers, technical secrets, credentials, or internal action history that the recipient does not need. |
| `SUPPORT_SANITIZED` | A deterministic projection containing only the fields enumerated in [`logging-standard.md`](logging-standard.md), after redaction and operator preview. | Preview-bound diagnostic export and its minimal workshop-side audit metadata. | Raw records, arbitrary attachments, personal destination paths, unreviewed optional fields, or automatic upload. |
| `RAW_UNTRUSTED_SENSITIVE` | Provider/backend/installer/command output; exception text and stack traces; arbitrary imported error details; temporary reports before parsing. | Bounded in-memory parsing with timeout/cancellation; a provider-specific raw attachment only if a later reviewed contract explicitly permits it as `WORKSHOP_RESTRICTED`. | Wholesale database copy, ordinary logs, recipient output, support output, or fallback display after sanitization fails. |
| `SECRET_OR_PERSONAL_CONTENT_EXCLUDED` | Passwords, authentication/session tokens, cookies, private keys, recovery keys, Wi-Fi credentials, personal files/content, clipboard, message, recording, browser-history, or form contents. | Do not collect. A workflow may record only a safe state such as `configured`, `deferred`, `not_available`, or `requires_recipient`, without the value. | Persistence, logging, screenshots, reports, support export, hashing for correlation, or ThirdLife custody. |
| `SIBLING_PRIVATE_EXCLUDED` | Sibling workspaces, documents, recordings, transcripts, messages, job-search or assessment evidence, schedules, repositories, application records, private databases, credentials, recovery/backup keys, and product-private logs/settings. | No B1 runtime or data flow. A future B4 project may use only exact frozen public releases, public documentation, and non-sensitive samples under its own privacy review. | Discovering, opening, scanning, indexing, referencing, copying, converting, exporting, retaining, deleting, or treating the data as a Core interface. |

`SECRET_OR_PERSONAL_CONTENT_EXCLUDED` and `SIBLING_PRIVATE_EXCLUDED` are handling labels for rejected input, not owned storage classes.

## Identity and linkage

- Create job IDs, action IDs, correlations, and support IDs from cryptographically random opaque values. Do not derive them from a recipient, operator, Windows account, device serial, hostname, MAC address, or another stable external identifier.
- A support export receives a fresh opaque `internal_support_id`. The protected workshop record may map it to its job; the exported ID does not encode that job or device identity.
- The workshop record may store a full serial only when it is needed to distinguish the device and access is restricted. The full value never flows through the general diagnostic pipeline.
- The current support allowlist omits the serial entirely. A later privacy-reviewed schema and policy may permit a fixed truncation/suffix only after explicit preview; a per-export checkbox alone cannot add it. Full serial inclusion is never a supported diagnostic option.
- Operator attribution belongs in `WORKSHOP_RESTRICTED`. Diagnostics use an opaque actor ID or role only when operationally necessary; they do not include the operator's name or Windows username.
- No product record requires recipient identity. If an organization keeps an external recipient or asset register, it remains outside ThirdLife; Core neither imports nor links it by default.

## Logical data map

Physical paths and implemented deletion behavior remain owned by later persistence, logging, reporting, packaging, and `TL-0703` lifecycle tasks. Those tasks must register every actual location without weakening this logical contract.

| Data surface | Core action | Class | Local/persistent posture | Audience/export posture |
|---|---|---|---|---|
| Product configuration and reviewed policy/profile/catalogue | Read, validate, and snapshot the exact version used | `PUBLIC_REFERENCE` or `WORKSHOP_RESTRICTED` when organization-specific | Active configuration is local; a job-bound snapshot follows job retention | Public metadata only unless a separate organization export is explicitly selected |
| Job, normalized observation, decision, plan, approval, journal, verification, and finalization state | Create and retain | `WORKSHOP_RESTRICTED` | Protected local job store; append/history semantics remain attributable | Projected separately into workshop, recipient, or support schema |
| Full serial and other full device identifiers | Reference and retain only as necessary for the workshop record | `WORKSHOP_RESTRICTED` | Protected job field; never used in a path or ordinary diagnostic | Full value only in workshop record; support defaults to omit |
| Provider/backend/installer/command/exception input | Parse and normalize | `RAW_UNTRUSTED_SENSITIVE` | Memory-only by default; no wholesale persistence | Never recipient/support output; no raw fallback |
| Explicit provider attachment | Copy only under a later reviewed typed contract | `WORKSHOP_RESTRICTED` | Bounded protected per-job attachment; default is no attachment | Not automatically inherited by any report or bundle |
| Sanitized application log | Retain bounded derived events | `OPERATIONAL_SAFE` | Protected local rotating store; redact before the first persistent write | May feed support only through the support field allowlist and preview |
| Temporary/preview/rendering files | Generate for a named operation | Highest class represented by their bytes | Internal random names, restrictive creation, bounded lifetime, cleanup on success/failure/restart | No path is logged; preview bytes must match exported bytes |
| Migration/recovery copy | Create only for a named versioned migration or recovery operation | Same class as the source aggregate | Protected, bounded, attributable copy; never a hidden second retention tier | No report/support inheritance; deletion follows verified migration/recovery policy |
| Package/update cache | Retrieve reviewed artifacts and bounded retrieval metadata | Executable bytes remain untrusted; privacy-bearing metadata is `WORKSHOP_RESTRICTED` | Restricted, bounded, and governed by later package/cache policy | Never a support attachment; download URLs are prohibited diagnostics |
| Technical workshop record | Generate from the workshop schema | `WORKSHOP_RESTRICTED` | Optional job-bound rendered copy follows job retention | Explicit operator-selected export; later handling is the workshop's responsibility |
| Recipient guide | Generate from its independent schema | `RECIPIENT_GUIDE` | Any job-bound rendered copy follows job retention | Explicit preview/export to the recipient; no workshop secrets |
| Support preview and bundle | Derive from the support allowlist | `SUPPORT_SANITIZED` | Preview/staging is temporary; retain only minimal export audit metadata | Explicit preview and export; never automatic upload |
| Repository, build, test, and release evidence | Generate from public inputs and synthetic/non-sensitive fixtures | `PUBLIC_REFERENCE` | Versioned repository/release evidence only; no live job or machine-specific private data | Public only after provenance, secret, path, and privacy review |
| Telemetry/analytics | None | None | No store, device identifier, SDK queue, or uploader | Partner metrics require a separate explicit aggregated/pseudonymized export and privacy approval |
| Recipient secrets/personal content | None | `SECRET_OR_PERSONAL_CONTENT_EXCLUDED` | No reference, copy, conversion, export, retention, deletion, or custody | Record only a safe state without the secret/content value |
| Sibling-private domains | None | `SIBLING_PRIVATE_EXCLUDED` | No discovery, reference, copy, conversion, index, export, retention, deletion, or database/log access | No B1 interface or support-export inclusion |

## Three output contracts

### Technical workshop record

The technical record is the only output class permitted to contain the full internal job/device identity and full serial. It may contain attributable sanitization evidence, observations, policy/profile/catalogue versions, disposition, confirmations and exceptions, actions, exact package versions, verification, operator/timestamps, and limitations. Its renderer must distinguish observed, inferred, human-confirmed, unavailable, policy, action, and verification state. Access and export are explicit because this output is not sanitized support data.

### Plain-language recipient guide

The recipient guide is independently projected. It explains installed capabilities, normal updates, files and backup next steps, accessibility entry points, support contact, preparation date, deferred choices, and limitations. It contains no donor/previous-owner detail, workshop account or credential, full serial, recovery key, password, package command, diagnostic trace, or unnecessary security internal.

### Sanitized diagnostic bundle

The bundle is generated only from the enumerated support schema in `logging-standard.md`. Every field and file is shown in an accessible preview. Export binds the exact preview manifest and content digest to the bytes written, revalidates the destination, writes atomically, and records only sanitized audit metadata. Unknown files and arbitrary operator-selected attachments are rejected. Subsequent storage or forwarding is outside ThirdLife control and must be explained before export.

## Proposed default retention guidance

The following values are the proposed safe defaults for later implementation. They are deliberately marked **not approved** until a named privacy owner reviews the classifications and durations. Organization policy may shorten them. An extension must name the data class, reason, accountable owner, review date, and deletion condition; silent indefinite retention is not permitted.

| Data | Proposed default | Start/cleanup rule | Required implementation behavior |
|---|---|---|---|
| Active or interrupted job and workshop evidence | Retain while the job is active; then 180 days after handover, do-not-deploy closure, or explicit abandonment | Unresolved recovery/blocker state pauses ordinary deletion only under a named review record | Warn before expiry, preserve attributable history until deletion, and make deletion distinct from reversible archive |
| Job-bound policy/profile/catalogue snapshots and rendered workshop/recipient copies | Same as the owning job | Delete with the explicit job-data deletion operation | Do not leave orphaned files; exported copies are not silently deleted |
| Sanitized operational logs | 14 days | Rotate by age and by a separately measured, configured byte ceiling; whichever occurs first | Delete whole records/files safely; report cleanup failure only with a stable sanitized code |
| Raw provider/backend/installer/command/exception content | Process lifetime only; zero persistent retention by default | Release immediately after bounded normalization or failure | No database/log/support fallback; an explicitly contracted raw attachment becomes workshop-restricted and follows job retention |
| Temporary, preview, and staging files | Operation lifetime; stale owned files no longer than 24 hours | Remove on success, cancellation, and failure; sweep only verified owned stale files at next startup | Never follow links/reparse points or delete outside the registered internal root; preserve truthful cleanup failure |
| Support preview and application-owned staged bundle | Preview session only | Remove after export/cancel/failure and on the bounded stale-file sweep | Keep only export audit metadata, not a duplicate archive |
| User-exported support bundle | Outside product control; recommend deletion when the support case closes and no later than 30 days absent a documented need | Operator/support recipient owns deletion | Show handling guidance before export; ThirdLife cannot claim deletion of an external copy |
| User-exported workshop record or recipient guide | Outside product control | Workshop/recipient policy owns deletion | Explain the audience and sensitivity at export; do not record a personal destination path in ordinary logs |
| Support export audit metadata | Same as the owning job | Delete with explicit job-data deletion | Retain only support ID, schema version, content digest, export time, and protected operator attribution |
| Migration/recovery copy | Until migration/recovery is verified, then 7 days | Start only for a named operation; a failure retains the original and records a bounded review state | Later persistence work must prove safe cleanup, access denial handling, and no orphan/partial copy before enabling the default |
| Unreferenced superseded configuration | 90 days after supersession | A version referenced by a retained job follows that job instead | Never rewrite the snapshot of a historical job |
| Package/update cache | 30 days after last verified use by default; keep longer only while an active/recoverable job explicitly references the exact artifact | Evict by age and by a separately measured byte ceiling after checking active plan, resume, provenance, and recovery references | Evict only cache-owned artifacts; never retain raw provider/output text with the artifact, and let later package work tighten this pending default when supply-chain or rollback evidence requires it |
| Secrets, recovery material, personal content, telemetry, and sibling-private data | Zero | Never collect or persist | A discovered attempted value is rejected/redacted; do not retain it for debugging |

Changing an approved retention default is a privacy and threat-model review trigger. Later code must test expiration, interrupted cleanup, access denial, full disk, corrupt metadata, clock changes, links/reparse points, and the distinction between archive, export, and deletion. Until implementation exists, this table is guidance rather than a deletion claim.

## Access, export, and deletion rules

- Use least-privilege access to local stores. The elevated broker has no job, attachment, or log handle.
- Encrypting or restricting a file does not change its classification or authorize broader collection.
- A database transaction cannot prove deletion of an external file or export. Reconcile split state and show an explicit recovery/review result.
- Deletion acts only on registered ThirdLife-owned roots and exact internal IDs after final-object and reparse/link validation. It never scans or deletes sibling/private content.
- Uninstall must later distinguish application removal from a separate explicit decision about ThirdLife-owned job data. This model does not authorize uninstall behavior before `TL-0703`/release implementation and verification.
- A privacy incident preserves only the minimum sanitized incident metadata needed to investigate; it never justifies copying the suspected secret or personal content into another log.

## Review and implementation gates

Before a later task adds a field, store, provider, raw attachment, report, crash record, support file, network flow, or retention change, it must record:

1. owning task and purpose;
2. exact source and privacy class;
3. collection necessity and less-sensitive alternative;
4. local store and access boundary;
5. audience allowlist and exact safe representation;
6. retention/deletion/recovery behavior;
7. adversarial redaction, bounds, interruption, and export tests; and
8. whether the threat model, privacy-owner approval, or release interface needs review.

Primary downstream owners include `TL-0104` logging/redaction, the inventory/provider tasks, `TL-0407` package diagnostics, `TL-0602` artifact detection, `TL-0604`–`TL-0606` audience outputs, `TL-0609` final evidence, and `TL-0703` implemented data-location/retention/uninstall documentation. Each may tighten this contract; none may broaden it silently.

The fixture file [`redaction-test-cases.yaml`](redaction-test-cases.yaml) is wholly synthetic and defines expected redacted forms for the later logging implementation. Passing a fixture validator proves only that the contract is internally complete; it does not prove an unimplemented redactor works.

## Privacy-owner approval record

The approving reviewer must inspect the exact commit and record:

- name and privacy-owner role;
- review date and exact commit/reference;
- approval or required changes for every classification;
- approval or corrected values for every proposed default retention row;
- confirmation that recipient identity is unnecessary and the full serial boundary is acceptable;
- confirmation that the support allowlist and prohibited-field list are sufficiently minimal;
- confirmation that raw output, telemetry, secrets, and sibling-private data handling is acceptable; and
- explicit result: approved, approved with recorded conditions, or changes required.

**Current privacy-owner:** Pending  
**Current privacy-owner role:** Pending  
**Current review date:** Pending  
**Reviewed commit/reference:** Pending  
**Approval scope:** Pending — field/context classifications, default retention guidance, redaction/omission, and support-export allowlist  
**Conditions/residual risks:** Pending  
**Current result:** Pending — automated checks cannot supply this human evidence.
