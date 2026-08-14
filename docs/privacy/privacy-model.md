# ThirdLife Setup Core — Privacy and Data Lifecycle Model

**Status:** Draft for privacy-owner review  
**Model revision:** TL-0005 draft 1  
**Draft date:** 2026-08-14  
**Privacy-owner approval:** **Pending**  
**Approving owner and role:** Pending  
**Approval date:** Pending  
**Reviewed source commit:** Pending  
**Approval reference:** Pending  
**Authority:** Derived analysis under `DECISIONS.md`, `ROADMAP.md`, `PROJECT_BOUNDARY.md`, `SECURITY.md`, `ACCESSIBILITY.md`, and `LOW_SPEC.md`  
**Decision coverage:** D-011, D-013, D-014, D-036, D-037, and D-053

This draft translates the binding product and project boundaries into a candidate privacy contract for later implementation tasks. It is not a new authority tier, a legal-compliance determination, a release approval, or evidence that persistence, logging, redaction, support export, retention, deletion, or uninstall controls have been implemented or verified. Higher-authority repository documents prevail if any statement conflicts.

No privacy-owner approval is recorded in this draft. Every control, schema, default, period, limit, and lifecycle behavior below is **planned or proposed** until a named privacy owner approves this exact revision and later implementation tasks provide verification evidence.

## 1. Purpose and scope

This model defines the proposed:

- data classifications and minimization rules for ThirdLife Setup Core;
- logical data map for product-owned, transient, excluded, and exported data;
- separation between the local job store, ordinary diagnostics, workshop record, recipient guide, sanitized support bundle, explicit pilot metrics, and public evidence;
- support-field allowlist and diagnostic prohibitions;
- default retention, cleanup, deletion, repair, update, and uninstall guidance;
- project-vacuum exclusions; and
- human review needed before `TL-0005` can be complete.

The model covers logical data classes rather than unverified filesystem paths. Exact installed locations, access-control behavior, migration behavior, cleanup results, and remaining-data behavior must be populated from implemented and verified lifecycle work, including `TL-0703`. This draft does not pre-populate `RELEASE_INTERFACE.md`.

## 2. Draft interpretation and privacy principles

In this draft, **must**, **must not**, **default**, and **required** express a proposed implementation contract. They do not claim current runtime behavior.

The candidate design is governed by these principles:

1. **Local first and offline capable.** Product-owned job, observation, plan, journal, and report data is proposed to remain local. No recipient or device data is proposed to be sent to a ThirdLife service.
2. **No recipient identity requirement.** A normal workshop job is proposed to use random internal identifiers and to succeed without recipient name, email, account, or other identity.
3. **Purpose and audience separation.** A field permitted in a restricted workshop record is not thereby permitted in an ordinary log, recipient guide, support bundle, pilot-metrics export, filename, command argument, or task evidence.
4. **Minimize before persistence.** Untrusted source material is proposed to be bounded, parsed into typed values, and redacted or rejected before persistence. Export-time filtering alone is insufficient.
5. **Deny unknown support fields.** A sanitized support bundle is proposed to be built from an exact allowlist of normalized fields, not by copying logs, a database, attachments, or raw provider output.
6. **No hidden measurement.** Product telemetry is proposed to remain absent and off by default. Any pilot measurement is a separate, explicit, previewed operator export of aggregated or pseudonymized data.
7. **Explicit lifecycle.** A reference, copy, conversion, export, retention, deletion, migration, or uninstall action is proposed to identify its data class and effect.
8. **Bounded resource use.** Records, logs, attachments, caches, temporary artifacts, previews, and exports are proposed to have byte, count, age, depth, and time bounds appropriate to later implementation.
9. **Truthful limitations.** Pseudonymous does not mean anonymous, ordinary filesystem deletion does not prove secure erasure, and application access controls do not defeat a local administrator or compromised operating system.
10. **Project vacuum.** ThirdLife Setup Core is proposed to neither ingest nor manage sibling workspaces, private databases, content, assessment evidence, repositories, credentials, recovery keys, backup keys, or backups.

## 3. Proposed data classifications

Classification is contextual. The same normalized fact can require a stricter classification when combined with a full serial, exact timestamp, or workshop identity. A less-sensitive projection must be deliberate and must not mutate the source record's classification.

| Class | Name | Examples | Proposed permitted purpose and channels | Proposed default handling |
|---|---|---|---|---|
| `PC-01` | Prohibited secret or foreign content | Passwords, tokens, credentials, recovery or encryption keys, clipboard secrets, donor or recipient content, and sibling-private data | No ThirdLife purpose or sink; only detection sufficient to reject, omit, or stop processing | Do not collect, copy, hash as a substitute for removal, persist, log, export, or include in task evidence |
| `PC-02` | Recipient-controlled private choice | Accessibility, account, backup, encryption, and recovery choices made by a present recipient; an organization acts only where a separately governed workflow explicitly permits an organization-owned choice and its authority is recorded | Recipient-present workflow status, or the narrow explicitly authorized organization-owned status | Record only choice status, scope, authority mode, pending state, and bounded verification; never secret values, inferred personal preferences, or unauthorized key copies |
| `PC-03` | Workshop-confidential record | Full serial, internal job-device linkage, external sanitization evidence, normalized evidence, approvals, action history, verification, operator attribution | Restricted local workshop record and its explicitly selected workshop export | Use internal IDs for paths and correlations; restrict access; never project by default to logs, guide, support, metrics, or public evidence |
| `PC-04` | Internal operational or pseudonymous metadata | Random job, action, attempt, correlation, and support IDs; stable event and result codes; versions and bounded timestamps | Local product operation and structured diagnostics; explicitly reviewed projections | Do not call anonymous; avoid persistent cross-job identifiers; bound and rotate where used diagnostically |
| `PC-05` | Support-allowlisted projection | Product/build versions, normalized check outcomes, generic hardware characteristics, stable error codes | Previewable sanitized support bundle only | Generate from an exact field allowlist; deny unknown fields; omit values that fail validation or classification |
| `PC-06` | Recipient-facing projection | Installed capability guidance, deferred choices, known limitations, safe next steps | Plain-language recipient guide | Exclude workshop secrets, previous-owner information, credentials, full serial, operator identity, and unsupported assurances |
| `PC-07` | Public or release metadata | Product version, public documentation, artifact hash, SBOM, licence record, known limitation, non-sensitive synthetic sample | Repository, release evidence, and public documentation | Keep non-sensitive and attributable; never use a public artifact as a route for job or device data |
| `PC-08` | Transient untrusted sensitive input | Raw Windows/provider values, command or installer output, exception text, imported text, and child-process streams | Bounded in-memory normalization only, unless an explicit provider contract approves a bounded attachment | Treat as hostile; byte, time, count, and depth bound; parse to typed results; discard by default; never fall back to raw persistence |

Raw command output is always transient untrusted sensitive input. It is never a diagnostic, journal, report, support, or task-evidence field merely because a sanitizer is available.

### 3.1 Identity and serial-number rule

A proposed normal job has a random internal `job_id` that is not derived from a person, serial number, device name, username, MAC address, or other stable device identifier. Recipient identity fields are not part of the proposed minimum job contract. Optional free-form recipient identity is not proposed.

For this model, **workshop record** includes the restricted local record fields used solely to produce and preserve the full workshop artifact. A full serial may be held only in that restricted workshop-record domain. It is proposed to be absent from generic observation projections, ordinary logs, log or temporary filenames, package commands, recipient guides, support bundles, pilot metrics, task evidence, and public samples. The proposed default support behavior is omission, not truncation. A future truncated-serial field would require an explicit schema, purpose, privacy review, and adversarial tests.

### 3.2 Candidate normalization bounds

The common proposed defaults are a maximum of 512 UTF-8 bytes for a retained scalar, 32 members in a collection, nesting depth 8, and a 4,096-byte in-memory read window for raw untrusted input. A provider contract may impose a smaller limit. Exceeding a bound is proposed to produce a stable rejection or unavailable code without echoing the source value; truncation is permitted only for an explicitly reviewed non-secret field whose schema defines the exact result. The raw-input window is not a persistence allowance. The privacy owner must review these bounds for the exact model revision; later provider-specific, adversarial, and low-spec verification still remains necessary after design approval.

## 4. Complete logical data map

The logical stores named below correspond to the planned architecture in the approved security data flow; they are not evidence that a physical store or path exists. “Reference” means retain an internal ID or digest without copying the external content. “Export” means create audience-specific bytes after preview. An exported file leaves ThirdLife's control after the write.

| ID | Data set and purpose | Classification | Planned source and local owner | Planned references, copies, and outputs | Candidate lifecycle and exclusions |
|---|---|---|---|---|---|
| PD-01 | Random internal job, action, attempt, correlation, and support identifiers used for attribution and recovery | `PC-04` | Generated locally by the job and journal contracts; authoritative copy proposed in the local job store | Referenced by normalized records and bounded logs; support receives a fresh support ID rather than the job ID | Not derived from a person or device; no global tracking ID; job-bound IDs follow the owning job, while short-lived correlation IDs follow bounded log retention |
| PD-02 | Recipient identity, contact details, account identifiers, and personal profile | `PC-01` by default because normal jobs do not need it | No proposed source or product owner | No copy, reference, filename, log, report, support, metric, or public output | Do not request or persist; reject or omit unexpected fields; a future operational need requires a frozen-decision review rather than a free-form field |
| PD-03 | Full serial, hardware UUID, service tag, and the restricted job-to-device identity needed by a full workshop record | `PC-03` | Structured device observation into the restricted workshop-record domain | Full value may appear only in the restricted local workshop record and explicitly selected workshop-record export | No generic diagnostic copy; omit from guide, support, metrics, public evidence, paths, and commands; deletion follows the whole workshop job rather than editing history in place |
| PD-04 | External sanitization evidence: method, operator attribution, date, media reference, result, verification, and policy version | `PC-03` | Operator and bounded external evidence; owned by the local job | Used by policy, finalization, and full workshop record; support may receive only a stable outcome or limitation code if later allowlisted | Never copy donor-media content or raw external records wholesale; preserve provenance and unknown state; follow the whole job lifecycle |
| PD-05 | Normalized device, OS, firmware, storage, battery, update, security, activation, management, key-device, and human-test evidence | `PC-03` at source, with explicit `PC-05` or `PC-06` projections | Typed providers and attributable human confirmations into the local job store | Policy, verification, workshop record, and audience-specific projections may reference typed fields | Raw provider text is not this data set; provenance, time, availability, and bounds remain attached; personal identifiers are excluded from lower-class projections |
| PD-06 | Application configuration and approved policy, profile, catalogue, and organization selections or immutable job snapshots | `PC-03` for organization/job snapshots; `PC-07` for public built-in definitions | Reviewed declarative sources and operator selection; local configuration store and job-bound snapshot owner | Job evaluation references an exact version or digest; public definitions may enter release evidence | No scripts, arbitrary paths, commands, URLs, sibling entries, credentials, or personal content; superseded job snapshots remain with history until whole-job deletion |
| PD-07 | Policy decisions, dispositions, explanations, confirmations, exceptions, and their policy version | `PC-03` | Deterministic policy evaluation and attributable operator confirmation; local job owner | Plan, finalization, and workshop record consume these records; support receives only reviewed stable outcomes or limitation codes | Preserve historical meaning and source policy; do not rewrite an old decision to match current policy; whole-job deletion only |
| PD-08 | Resolved plans, impact previews, exact approvals, approver attribution, content digests, and decline or defer state | `PC-03` | Plan and approval workflow; local job owner | Journal and broker authorization reference exact IDs and digests; workshop record may include the approved impact | No command strings, credentials, download URLs, or recipient identity; material change creates a new approval rather than overwriting history |
| PD-09 | Action journal, broker request/result metadata, restart checkpoints, stable error codes, and attempt history | `PC-03` with bounded `PC-04` diagnostic projections | Journal service is proposed as authoritative owner; broker supplies authenticated bounded results | Verification, resume, finalization, and workshop record reference the journal; logs receive only stable codes and bounded correlations | Preserve monotonic history; never store raw child output as a journal field; ambiguous state remains visible; whole-job deletion is atomic at the job boundary |
| PD-10 | Independent verification, cold-boot, finalization, handover, and sign-off state | `PC-03` with `PC-06` recipient projection | Verification and finalization workflows; local job owner | Workshop record receives complete bounded evidence; guide receives plain-language result and limitations; support receives reviewed stable outcomes | Applied is not verified; unknown remains unknown; no safety certification or numeric health score; follows the whole job lifecycle |
| PD-11 | Package and update source identity, package ID, publisher, version, architecture, scope, provenance, signature or hash evidence, and bounded download cache | Metadata is `PC-03` in a job and `PC-07` when public; executable bytes remain untrusted until verified | Reviewed catalogue, structured package/update source, and bounded cache | Exact metadata binds plan, approval, execution, verification, and workshop record; support receives only reviewed metadata fields | Download URLs, arguments, raw streams, credentials, and query strings are excluded; cached bytes use a separate bounded lifecycle and never become a sibling-suite cache |
| PD-12 | Recipient-controlled accessibility, account, backup, encryption, and recovery choice status | `PC-02` | A present recipient controls recipient choices; an organization is a source only for an explicitly governed organization-owned choice with recorded authority; the local job records status only | Guide may show chosen or pending guidance; workshop record may show bounded completion status without a secret | Never infer a recipient preference or retain passwords, recovery material, backup repository credentials, cloud identity, or clipboard values; sealed handover records pending |
| PD-13 | Ordinary structured diagnostic events used to explain product operation and bounded failures | `PC-04` only | Product components through the planned structured logging contract; local log sink | Local troubleshooting only; a support bundle is regenerated from normalized allowlisted data rather than copying these files | Redact before persistence; no full serial, identity, network identifier, personal path, URL, raw output, or secret; candidate rotation is defined below |
| PD-14 | Raw provider, command, installer, update, exception, imported-text, and child-process payloads | `PC-08` | Untrusted local OS, external source, or child process | No direct copy or output; a bounded parser may derive `PC-03`, `PC-04`, or `PC-05` typed values | Memory-only and discard by default; reject, omit, or emit a stable code on failure; never persist or echo the raw value as a fallback |
| PD-15 | Explicitly permitted per-job attachments needed by a reviewed evidence or report contract | `PC-03`, never implicitly `PC-05` | A named provider or operator action under a future bounded contract; per-job attachment store | Local job may reference internal attachment ID, digest, type, size, and purpose; no default support or guide copy | Allowlisted types, counts, sizes, restrictive access, internal names, and reconciliation required; donor content and sibling data are prohibited; delete with the whole job or earlier explicit action |
| PD-16 | Temporary, preview, staging, partial-export, and renderer artifacts | Inherit the strictest source class, normally `PC-03` or `PC-08` | Product operation in a Core-owned temporary location | Used only for the current bounded operation; preview bytes may be digest-bound to an export | Random internal names, restrictive creation, capacity checks, cleanup on completion, cancellation, or failure, and startup recovery; never use a name, serial, username, or destination personal path in the temporary name |
| PD-17 | Migration backup, rollback snapshot, and storage-recovery metadata for ThirdLife-owned data | Inherits source, normally `PC-03` | Versioned migration or repair workflow; Core-owned recovery location | References exact schema, source revision, time, result, and digest; not a report or support artifact | Create only when needed for safe migration; restrict access; retain through verified rollback window; do not claim secure erasure or include sibling paths/data |
| PD-18 | Full technical workshop record and its export metadata | `PC-03` | Audience-specific projection of the authoritative job; export selected by operator | May include full serial and complete bounded history required by the workshop; internal export metadata may retain schema, digest, time, and result | Not a support bundle or recipient guide; exported file leaves product control; destination personal path is not proposed for ordinary logs |
| PD-19 | Plain-language recipient guide and its export metadata | `PC-06` | Audience-specific projection of finalized normalized records | Contains capabilities, safe next steps, deferred choices, and limitations for the recipient | No full serial, recipient identity requirement, workshop/operator secrets, credentials, recovery keys, previous-owner data, raw evidence, or unsupported assurance; exported file leaves product control |
| PD-20 | Previewable sanitized support bundle, manifest, and export metadata | `PC-05` | Fresh allowlisted projection of normalized records; operator explicitly previews and exports | Includes only the field allowlist in this model; internal metadata may retain support ID, schema, digest, export time, and result | Never copy whole logs, database, attachments, raw streams, or destination path; preview bytes must bind to exported bytes; exported file leaves product control |
| PD-21 | Explicit partner pilot metrics export | `PC-04` aggregated or pseudonymized; never asserted anonymous without evidence | Generated only on an explicit operator action from reviewed fields | Separate previewed artifact for a named pilot purpose; no background collection or upload | No analytics SDK, account, device identifier, stable cross-job identifier, automatic queue, or retrying uploader; exported artifact leaves product control and requires purpose-specific review |
| PD-22 | Repository, CI, task, security/privacy, accessibility, low-spec, dependency, and release evidence | `PC-07` | Synthetic tests, governed reviews, exact source revision, and release process | Stored in this repository or approved release evidence location and may become public | Durable where governance requires, but no real job/device record, secret, personal identifier, raw diagnostic archive, private approval material, or sibling content; fixtures remain synthetic and bounded |

## 5. Planned sink and channel separation

Permission in one channel does not flow automatically to another. The following matrix is a candidate routing contract for later implementations.

| Channel | Purpose | Proposed inputs | Explicit exclusions |
|---|---|---|---|
| `CH-01 Local authoritative job store` | Recoverable job, decision, approval, journal, and verification history | `PC-03` records plus internal `PC-04` identifiers | `PC-01`, raw `PC-08` payloads, sibling data, and unnecessary recipient identity |
| `CH-02 Restricted attachment store` | Only attachments named by a reviewed contract | Bounded `PC-03` attachments and internal metadata | Donor or recipient content, sibling content, unreviewed raw output, arbitrary files or paths |
| `CH-03 Ordinary structured log` | Bounded local operational diagnostics | Stable `PC-04` event/result codes, product versions, short correlations, bounded safe scalars | Full serial, names, accounts, network identifiers, personal paths, package URLs, raw output, secrets, database rows, or attachments |
| `CH-04 Temporary and preview area` | Atomic work, rendering, preview, and recovery | Minimum operation-specific fields with inherited classification | Human-readable sensitive filenames, indefinite storage, unrelated job data, sibling paths, or use as a hidden archive |
| `CH-05 Process arguments and child environment` | Exact compiled operation parameters | Only reviewed typed non-secret parameters required by an allowlisted action | Credentials, recipient identity, clipboard data, full serial, personal paths supplied through metadata, and arbitrary command text |
| `CH-06 Full workshop record` | Technical and attributable workshop evidence | Reviewed `PC-03` projection, including full serial where required | Donor content, recipient secrets, sibling data, raw streams, unsupported certification |
| `CH-07 Recipient guide` | Accessible plain-language handover | `PC-06` projection | Workshop secrets, full serial, operator identity, previous-owner information, recipient credentials, recovery material, raw diagnostics |
| `CH-08 Sanitized support bundle` | Previewable bounded troubleshooting artifact | Exact `PC-05` allowlist only | Whole logs/database/attachments, unknown fields, full serial, identity, network identifiers, paths, URLs, raw output, secrets, sibling data |
| `CH-09 Explicit pilot metrics export` | Partner-reviewed pilot learning | Reviewed aggregate or pseudonymized `PC-04` values | Automatic telemetry, background queue/upload, device IDs, full serial, person/job linkage, free text, raw diagnostics |
| `CH-10 Crash and framework diagnostics` | Explain a local failure without hidden reporting | Stable local error code and bounded sanitized state only, if a later task adds this sink | Automatic crash upload, dumps, stack traces with paths, memory, environment, raw exception text, or secret-bearing framework defaults |
| `CH-11 Task, CI, and release evidence` | Durable proof and public release traceability | `PC-07` synthetic results, command summaries, hashes, approvals, and limitations | Real device/job content, secrets, personal paths, full serial, diagnostic archives, or private sibling information |

No proposed channel authorizes a ThirdLife-hosted data service. Package metadata/download, Windows Update, catalogue update, and optional self-update are product network categories, not telemetry channels and not permission to attach job or device data.

## 6. Proposed support allowlist

The sanitized support schema is proposed to deny unknown fields and unknown nested members. An allowed field remains subject to type, length, range, collection-count, and combination-risk validation. Empty or invalid fields are proposed to be omitted rather than replaced with raw source text.

| Group | Proposed exact field or bounded structure | Privacy condition |
|---|---|---|
| Bundle identity | `support_id` | Fresh random ID created for the bundle; not `job_id`, serial-derived, or reusable across exports |
| Product | `product_name`, `product_version`, `build_revision`, `bundle_schema_version`, `redaction_rules_version` | Public build metadata only; exact revision may be omitted for a public release if release policy chooses a public version instead |
| Operating system | `os_edition`, `os_build`, `os_architecture`, `os_support_state` | No product key, account, tenant, hostname, device name, or activation secret |
| Generic hardware | `hardware_manufacturer`, `hardware_model`, `device_form_factor`, `cpu_architecture`, `installed_memory_bucket`, `storage_media_class`, `storage_capacity_bucket` | Bounded normalized values; no serial, service tag, hardware UUID, MAC, device name, or exact user-selected label |
| Check result | `check_id`, `availability`, `outcome_code`, `limitation_code`, `observed_at_utc` | Stable IDs and vocabulary; bounded timestamp; no raw evidence or free text |
| Action result | `action_type`, `result_code`, `verification_code`, `restart_state`, `duration_bucket` | No arguments, command line, child output, approver identity, or unrestricted message |
| Sanitized error | `error_code`, `error_category`, `recovery_code` | Stable reviewed code only; a bounded plain-language message requires its own template ID, not raw exception text |
| Package metadata | `source_id`, `package_id`, `publisher`, `resolved_version`, `architecture`, `scope` | Reviewed normalized metadata only; no download URL, redirect, query, installer arguments, cache path, or raw backend stream |
| Configuration provenance | `policy_version`, `profile_id`, `catalog_version`, `configuration_digest` | Only reviewed non-personal identifiers and digests; no organization free text or private source path |
| Bounded operation summary | `started_at_utc`, `completed_at_utc`, `duration_bucket`, `attempt_count`, `item_count` | Apply collection and range bounds; omit if combination would identify a person or workshop unnecessarily |
| Bundle manifest | `relative_name`, `content_sha256`, `byte_count`, `generated_at_utc` | Internal allowlisted relative names only; the generation timestamp is frozen before preview; no local source path, destination path, username, share, or volume label |

The support bundle is proposed to contain files generated specifically from this schema. A log filename or database filename in the bundle manifest would not make the underlying file allowable.

The initial candidate support projection is bounded to 8 logical files, 2 MiB total uncompressed bytes, 10,000 normalized records, 32 members per collection, nesting depth 8, and 512 UTF-8 bytes per retained scalar. Exceeding any bound blocks export with a stable result; it does not silently omit an unknown boundary or copy raw material. `generated_at_utc` is frozen with the normalized projection before preview, and the preview digest covers it. The actual destination-write completion time is recorded separately as workshop-confidential job metadata and does not mutate the previewed bundle bytes.

## 7. Proposed prohibited diagnostic fields

The default action is omission or rejection before persistence. Hashing, encoding, truncating, or replacing only part of a prohibited value is not proposed as permission to retain it. A future exception requires a named purpose, exact channel and field, bounded representation, threat/privacy review, adversarial tests, and owner approval.

| Prohibited category | Examples | Proposed behavior |
|---|---|---|
| Authentication and recovery secrets | Passwords, PINs, passphrases, bearer/session/API tokens, cookies, credentials, recovery keys, encryption keys, product keys, clipboard secrets, IPC nonces or tokens | Reject or omit; never echo; emit only a stable reason code |
| Donor or recipient content | Documents, media, message bodies, transcript text, form content, browser data, clipboard content | Do not inspect for diagnostics; stop an accidental ingestion and record only a stable category code |
| Person and organization identifiers | Names, email addresses, phone numbers, recipient identity, operator free text, account names, organization-private labels | Omit from ordinary logs and support; use reviewed role or random internal ID where attribution is required in the workshop record |
| Windows and account identifiers | Usernames, SIDs, tenant IDs, enrollment IDs, cloud account IDs, profile names | Omit from ordinary logs and support; never use in filenames |
| Network and nearby-device identifiers | Wi-Fi SSID or BSSID, IP or IPv6 address, MAC address, hostname, device name, Bluetooth name | Omit; record only a stable availability or check outcome when needed |
| Unique hardware identity | Full serial, service tag, hardware UUID, firmware UUID | Restrict full value to workshop record; omit from ordinary diagnostics, guide, support, metrics, and evidence |
| Personal or destination paths | User-profile paths, filenames, local or UNC paths, share names, export destination, volume label | Omit; use an internal ID, allowlisted relative name, destination type, or stable path-validation result |
| URL and command material | Package download or redirect URLs, query strings, arbitrary URI, installer arguments, command line, shell text, environment variables | Omit; record reviewed source/package IDs and stable result codes only |
| Raw technical payloads | Provider, command, installer, package, update, stdout, stderr, exception, stack trace, dump, memory, framework diagnostic, or localized table text | Bound and parse in memory; persist only typed normalized fields or stable failure codes; never copy wholesale |
| Unbounded inventories | Arbitrary application, file, content, process, registry, account, or network enumeration beyond a reviewed provider contract | Reject or cap at the provider boundary; no content scan or whole-machine support dump |
| Sibling-private data | Workspaces, private databases, documents, recordings, transcripts, message evidence, job-search records, charity-assessment evidence, backup repositories, schedules, credentials, or keys | Do not discover, open, reference, copy, convert, index, export, delete, or test with it |

## 8. Candidate retention and cleanup guidance

The values below are proposed privacy defaults for owner review, not implemented behavior or legal retention advice. A workshop may need an approved policy that is stricter or longer, but a policy change must not weaken prohibited-field rules, silently rewrite history, or create sibling ownership. Later lifecycle tasks must turn approved guidance into exact locations, controls, tests, and user-visible behavior.

| Data | Candidate default | Candidate deletion or cleanup trigger | Required safeguards and evidence |
|---|---|---|---|
| Prohibited data and raw untrusted payloads | Do not persist; hold only for the bounded normalization operation | Discard immediately after successful parse, rejection, timeout, cancellation, or failure | Test that failures do not fall back to raw output and memory/output bounds apply |
| Draft job with no machine mutation or safety-relevant history | Surface for operator retention review after 30 days of inactivity; do not silently delete | Explicit whole-job deletion after preview of affected records and attachments | Preserve a deletion result without preserving prohibited source content; final behavior belongs to lifecycle implementation |
| Active, interrupted, failed, or review-required job | Retain until recovery, reconciliation, or explicit governed abandonment | Explicit whole-job deletion only after warning that recovery/audit evidence will be lost | Never age-delete an ambiguous started action; preserve monotonic history while the job exists |
| Finalized job, workshop-confidential history, and full serial | Surface for workshop-policy review 180 days after finalization; no silent automatic purge in this draft | Explicit whole-job deletion under approved workshop policy | Delete the job as a coherent unit rather than rewriting individual historical facts; disclose backup/export copies |
| Approved snapshots, decisions, plans, approvals, journal, and verification | Follow owning job | Whole-job deletion only | Preserve exact historical version and attribution while retained |
| Explicit per-job attachments | No longer than the owning job; earlier review when purpose ends | Explicit attachment deletion where contract permits, or whole-job deletion; reconcile orphans | Restrictive access, allowlisted type/count/size, internal names, no donor/sibling content |
| Ordinary structured logs | Candidate 14-day age-expiry threshold, no more than five rotated files of 4 MiB each, and 20 MiB total; size/count limits apply on every write and startup maintenance evaluates age | On a log write or application startup, rotate and clean oldest first; cleanup on explicit data removal; no background maintenance task | A file can remain past the age threshold while the application never runs; remove it at the next maintenance opportunity. If the 20 MiB quota cannot be restored, logging fails boundedly without falling back to another sink |
| Temporary, preview, staging, and partial files | Current operation only; candidate 24-hour orphan-cleanup eligibility threshold | Cleanup on success, cancellation, and failure; startup recovery removes eligible orphaned artifacts after verifying Core ownership | An orphan can remain past 24 hours while the application never runs; remove it at the next startup. Bound total size/count; restrictive creation; safe reparse/path checks; cleanup result uses stable codes |
| Package/update cache | Eligible for cleanup immediately after independent verification or terminal failure; candidate 7-day cleanup-eligibility threshold | Capacity-aware cache cleanup at the next startup or relevant package/update operation, explicit operator cleanup, update/repair cleanup, or uninstall | An eligible artifact can remain beyond 7 days until the next maintenance opportunity. Never delete an artifact still needed for a known rollback/recovery path; no sibling release cache |
| Support preview and unexported bundle | Current preview session only; candidate 24-hour orphan-cleanup eligibility threshold | Cleanup after export, cancellation, preview replacement, or failure; next-startup recovery removes eligible owned orphans | Preview bytes must match the reviewed manifest/digest; an orphan can physically remain past the threshold until the next startup; preview cache uses internal names |
| Exported workshop record, recipient guide, support bundle, or pilot metrics file | ThirdLife does not control retention after successful export | Operator or receiving organization deletes under its policy | Before export, explain audience and that destination copies, sync, backups, and forwarding are outside product control |
| Internal export metadata | Retain support/workshop/guide schema, digest, time, result, and random internal ID with owning job where applicable | Whole-job deletion, or bounded operational cleanup for a standalone support export | Do not retain destination personal path, share name, recipient identity, or exported payload as generic metadata |
| Migration or repair backup | Keep until migration/repair is verified, the prior schema rollback window has passed, and at least one successful reopen is evidenced; candidate cleanup-eligibility threshold 7 days after all conditions become true | Cleanup at the next startup or relevant migration/repair operation only after all conditions are true, otherwise explicit review | An eligible backup can remain beyond the threshold until the next maintenance opportunity. Never treat DB rollback as rollback of machine state; record cleanup result and limitation |
| Application configuration | Retain until superseded, reset, explicit data removal, or approved uninstall choice | Versioned replacement or explicit removal | Keep organization-private settings local; no credentials; job-bound snapshots follow their jobs |
| Repository, task, and release evidence | Durable under repository/release governance | Governed history rewrite or artifact-retention process only | Synthetic and non-sensitive; do not use task YAML or public CI as a diagnostic dump |
| Pilot metrics working set | No background local history; generate only for the explicit preview/export operation | Discard after export, cancellation, or failure | No uploader, retry queue, stable device ID, person/job linkage, or free text |

The proposed 30-day and 180-day review points, 14-day log age-expiry threshold, 20-MiB/five-file hard limits, 24-hour orphan-cleanup eligibility threshold, and 7-day cache/migration eligibility thresholds are candidate defaults requiring explicit privacy-owner approval and later low-spec/security evidence. Age-based cleanup runs only at the documented write, startup, or relevant-operation maintenance point, with no background task, so physical over-retention is possible while the application does not run. These are not support or legal claims.

## 9. Candidate deletion, update, repair, and uninstall behavior

The following is guidance for later lifecycle design; no behavior is claimed to exist:

- **Deletion unit.** A proposed job deletion operates on the whole ThirdLife-owned job, its job-bound snapshots, attachments, and internal export metadata. It must not edit individual historical outcomes to create a more favorable record. Because a current operational-log generation may contain an opaque `job_ref`, whole-job deletion also removes every current ThirdLife operational-log generation rather than selectively rewriting shared log records; the preview discloses that unrelated diagnostics will be lost.
- **Archive is not deletion.** Archive is reversible state and cannot satisfy a deletion request or shorten a retention period by itself.
- **Preview and confirmation.** Destructive removal is proposed to show the categories, job identity, attachment count, exported-file limitation, and recovery consequence before confirmation. Ambiguous, in-progress, or interrupted action state requires an additional stop/review path.
- **Accessible lifecycle interaction.** Deletion, retention, reset, repair, and uninstall previews and results use standard keyboard-operable controls, predictable focus and focus return, programmatic names/roles/states, screen-reader announcements, logical order at 200% scaling and reduced resolution, color-independent status, plain-language consequences, cancellation before mutation, and a discoverable recovery/retry path after partial failure.
- **Normal filesystem deletion.** A deletion result is proposed to mean the application removed its owned references and files using supported filesystem behavior. It must not claim cryptographic erasure, SSD secure erase, removal from external backups/sync, or defeat of a local administrator.
- **Update and repair.** The candidate default is to preserve jobs, attachments, configuration, approvals, history, logs needed for the bounded recovery window, and exported artifacts. Migrations are proposed to be versioned and backed up before incompatible change.
- **Standard uninstall.** Uninstall preserves ThirdLife-owned jobs, attachments, configuration, and workshop records unless the operator separately chooses an explicit reviewed “remove ThirdLife data” action. The candidate safe default removes application binaries and Core-owned disposable cache, temporary, preview, and ordinary-log data.
- **Explicit data removal.** A separate removal choice is proposed to enumerate ThirdLife-owned data categories and remaining exported files. It must not broaden into arbitrary path deletion and must fail closed on an unresolved path, junction, reparse point, ownership mismatch, or active recovery dependency.
- **External exports.** Uninstall and internal data deletion are proposed not to delete workshop records, recipient guides, support bundles, or pilot metrics already exported to operator-selected locations. Those artifacts are outside product control.
- **Sibling safety.** Update, repair, deletion, reset, and uninstall must never discover, migrate, retain, rewrite, or delete sibling application workspaces, private databases, content, caches, repositories, credentials, or recovery keys.
- **Evidence.** Later implementation must test normal, cancelled, interrupted, corrupt, older/newer-schema, low-space, cross-user, path-traversal, junction/reparse, missing-file, and partial-cleanup cases before lifecycle behavior can be published.

## 10. Project-vacuum exclusions

The exclusions below are data boundaries, not merely support-filter rules. ThirdLife Setup Core is proposed to avoid accessing the data in the first place.

| Excluded domain | Examples | Proposed handling |
|---|---|---|
| PaperWorkShell | Documents, templates, form data, workspaces, private database | Do not inspect, index, reference, copy, convert, export, migrate, or delete |
| CaptionKit | Media, models, recordings, transcripts, captions, corrections | Do not inspect, index, reference, copy, convert, export, migrate, or delete |
| Scam Explainer | Message bodies, evidence, legitimacy decisions, case history | Do not inspect, index, reference, copy, convert, export, migrate, or delete |
| Job Application Studio | Application records, drafts, deadlines, rankings, workspaces | Do not inspect, index, reference, copy, convert, export, migrate, or delete |
| Charity Cyber Check | Authorization, assessment evidence, beneficiary data, findings, certification material | Do not inspect, index, reference, copy, convert, export, migrate, or delete |
| Backup Circle | Repositories, backup sets, schedules, credentials, encryption or recovery keys, engine state | Do not inspect, index, reference, copy, convert, export, migrate, or delete |
| Any sibling implementation | Private database, internal source, active branch, logs, background process, development fixture, service, or release schedule | No runtime, build, test, support, retention, or lifecycle dependency |
| Portfolio-wide private data | Central identity, behavioral record, shared workspace, shared content library, credentials, recovery material | Do not create or consume during B1 |
| Future B4 integration | Sibling catalogue, adapter state, compatibility matrix, offline suite cache, deployment media | No B1 data set or flow; future work may consume only frozen public release inputs under a separate decision |

A generic installed-package observation does not authorize opening that application's workspace or private data. A later B4 task may define optional public behavior, but it cannot retroactively make sibling data ThirdLife-owned.

## 11. Residual privacy limitations and review triggers

Even if the proposed controls are later implemented correctly, these limitations remain:

- A local administrator, compromised operating system, kernel component, debugger, filesystem backup, or endpoint-management product can read or alter local data beyond application-level guarantees.
- The full serial in the restricted workshop record remains identifying. Access restriction, purpose limitation, explicit deletion, and export handling reduce but do not eliminate that exposure.
- Several individually generic support fields can become identifying when combined with an exact time, rare hardware model, workshop context, or external knowledge. The preview and allowlist require combination-risk review.
- Pseudonymous random identifiers can still be linked through retained context. They are not anonymous and must not become stable cross-job or cross-export tracking IDs.
- Raw provider, installer, update, and exception sources can contain secrets unexpectedly. Bounds and typed parsing reduce exposure but cannot make a compromised operating system truthful.
- Redaction rules can have false negatives and false positives. Later automated adversarial fixtures and human review are required; this document alone is not redaction evidence.
- Exported files, screenshots, manual copies, printouts, synced destinations, removable media, and forwarded support bundles leave ThirdLife's control after operator action.
- Age-based log and orphan cleanup runs only when ThirdLife starts or writes; eligible data can physically remain beyond the proposed threshold while the application does not run.
- Supported filesystem deletion may leave recoverable blocks, journal records, filesystem snapshots, sync copies, or backups. No secure-erasure claim is proposed.
- Required audit/recovery history and data minimization can conflict. Whole-job deletion and explicit review avoid silent history rewriting but require the privacy owner and workshop policy to select an acceptable retention posture.
- Exact physical paths, ACLs, storage encryption behavior, migration recovery, uninstall implementation, and cleanup success are unknown until later tasks implement and verify them.
- This model does not determine statutory retention, controller/processor roles, lawful basis, records-of-processing requirements, or jurisdiction-specific notices. A deploying organization remains responsible for applicable legal review.

Material review triggers include a new data field or sink, new network or crash-reporting capability, change to retention or uninstall defaults, collection of recipient identity, serial use outside the workshop record, support allowlist change, raw attachment contract, new provider, cloud/service proposal, pilot-metrics change, or any sibling/public-adapter data flow. A material trigger requires privacy review and, when it changes a trust boundary or residual risk, security-model review.

## 12. Privacy-owner review and approval

No approval is present in this draft. `TL-0005` must remain in review until a named privacy owner approves the classifications and default retention guidance for an exact committed revision. Repository ownership alone is not privacy approval; the person must explicitly act in the privacy-owner role.

The reviewer is asked to record an explicit result for every item:

- [ ] `PR-01` — Confirm this document is derived guidance and does not weaken D-011, D-013, D-014, D-036, D-037, D-053, or the canonical Owns / Does not own boundary.
- [ ] `PR-02` — Approve, reject, or condition each classification `PC-01` through `PC-08`, including the rule that a projection never lowers the source classification implicitly.
- [ ] `PR-03` — Approve normal job creation without recipient identity and the absence of optional free-form recipient identity from the proposed minimum contract.
- [ ] `PR-04` — Approve the full-serial restriction to the workshop-record domain and default omission from logs, filenames, commands, guide, support, metrics, task evidence, and public samples.
- [ ] `PR-05` — Review every data-map row `PD-01` through `PD-22` for purpose, owner, classification, copy/reference/export behavior, and exclusion.
- [ ] `PR-06` — Approve the separation of `CH-01` through `CH-11` and confirm that logs, workshop records, recipient guides, support bundles, pilot metrics, crash diagnostics, and public evidence are not interchangeable.
- [ ] `PR-07` — Approve every proposed support field and nested member in the allowlist, the deny-unknown rule, and omission of invalid values.
- [ ] `PR-08` — Approve every prohibited diagnostic category and the rule to omit or reject before persistence rather than rely on export-time redaction.
- [ ] `PR-09` — Approve treating raw provider, command, installer, update, exception, and child-process payloads as bounded `PC-08` input that is discarded by default.
- [ ] `PR-10` — Approve telemetry absence/off-by-default and the strict separation of explicit previewed aggregate or pseudonymized pilot-metrics export from background collection or upload.
- [ ] `PR-11` — Approve or condition the 30-day draft-job review point, 180-day finalized-job review point, 14-day log age-expiry threshold and 20-MiB/five-file hard limits, maintenance-point-only cleanup, possible physical over-retention, bounded quota failure, 24-hour orphan-cleanup eligibility threshold, and 7-day cache/migration cleanup-eligibility thresholds.
- [ ] `PR-12` — Approve the no-silent-delete posture for active, ambiguous, failed, review-required, and historical job data, plus coherent whole-job deletion.
- [ ] `PR-13` — Approve the candidate update, repair, standard-uninstall, explicit-data-removal, remaining-export, and no-secure-erasure guidance.
- [ ] `PR-14` — Confirm the project-vacuum exclusions cover sibling workspaces, private databases, content, evidence, recordings, messages, application records, repositories, backup data, credentials, and recovery keys.
- [ ] `PR-15` — Accept, reject, or condition each residual limitation and identify any additional privacy review trigger.
- [ ] `PR-16` — Confirm that later implementation must supply exact paths, ACL and cross-user tests, adversarial redaction fixtures, retention/cleanup tests, migration/uninstall evidence, accessible preview evidence, and low-spec bounds before release claims.

An approval record is proposed to contain:

| Field | Required value |
|---|---|
| Result | Approved, approved with enumerated conditions, or rejected |
| Approving owner and role | Stable name or authenticated handle followed by “Privacy owner” |
| Approval date | ISO `YYYY-MM-DD` |
| Reviewed revision | `TL-0005 draft 1` or a later exact model revision |
| Reviewed source | Full commit SHA containing this document, `logging-standard.md`, and `redaction-test-cases.yaml` |
| Checklist disposition | Explicit result for `PR-01` through `PR-16`, including any condition owner and gate |
| Approval reference | Durable review artifact, preferably an authenticated GitHub review, issue, or comment URL |

Approval of this model would approve the documented classifications and candidate defaults only. It would not prove that a runtime control works, approve a release, authorize telemetry, authorize sibling-data access, or waive later security, privacy, accessibility, low-spec, Windows, lifecycle, and adversarial verification.
