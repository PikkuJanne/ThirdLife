# ThirdLife Setup Core — Logging Standard

**Status:** Draft for privacy-owner review  
**Model revision:** TL-0005 draft 1  
**Draft date:** 2026-08-14  
**Authority:** Derived implementation contract under `DECISIONS.md`, `ROADMAP.md`, `PROJECT_BOUNDARY.md`, `SECURITY.md`, `ACCESSIBILITY.md`, and `LOW_SPEC.md`  
**Decision coverage:** D-011, D-013, D-014, D-036, D-037, and D-053

This standard defines the privacy and safety contract that later logging work must implement. It is not a new authority tier, a release interface, or evidence that a logger, redactor, support exporter, retention job, or telemetry path exists. Higher-authority files prevail. The classifications and default retention guidance also require the named privacy-owner approval recorded by `TL-0005` before this task can be complete.

The related [`privacy-model.md`](privacy-model.md) defines product data classes and ownership. [`redaction-test-cases.yaml`](redaction-test-cases.yaml) supplies synthetic examples with exact expected transformations. Runtime implementation belongs to later tasks, including `TL-0104`, `TL-0407`, and `TL-0606`.

The fixture records the stage of each exact output. For `ordinary_log`, `expectation.output` is an exact registered safe-field fragment produced before required closed-envelope assembly; it is never a sink-ready event. The later envelope builder must add and validate every required envelope field, enforce the event registry and 8-KiB limit, and reject the event before persistence if that assembly fails. For `support_export`, the output is an exact allowlisted normalized projection fragment before manifest assembly. Other sinks use the exact post-classification decision output. The machine-readable `expectation_output_contract` in the fixture locks these stage semantics.

## Purpose and invariants

Logging is a bounded local diagnostic aid. A normal workshop job and its diagnostic events do not require or accept recipient identity. Logging is not the job store, action journal, evidence record, approval record, verification record, workshop record, recipient guide, or support bundle. Deleting or losing an operational log must not rewrite any of those durable records. Conversely, a log event cannot prove that an action was applied or verified, change a disposition, authorize elevation, or permit handover.

The following invariants apply to every event producer, formatter, sink, preview, and export:

- classify and transform each field before formatting, serialization, persistence, display, or export;
- accept only registered event codes and closed typed field schemas;
- treat an unknown event, field, type, classification, or sink as rejected rather than as free text;
- use opaque internal job, action, and correlation references instead of a person, account, device, path, or serial identifier;
- keep operational diagnostics local and functional without a ThirdLife account or service;
- never route one record automatically to every audience;
- never use a raw, verbose, console, crash-report, or debug fallback to bypass this standard;
- never weaken redaction, size limits, sink separation, or accessibility in a low-resource mode; and
- preserve an explicit limitation when diagnostics are unavailable or incomplete rather than claiming a complete support record.

## Event envelope

### Closed logical schema

The logical event envelope is finite. Its eventual storage encoding is not selected by this task. Each implementation must validate the following fields before a sink sees any bytes:

| Field | Type and cardinality | Contract |
|---|---|---|
| `schema_version` | Required enum; initially `tl-log-v1` | Selects one reviewed closed schema. Unknown versions are rejected. |
| `event_id` | Required random 128-bit opaque identifier | Unique within the local product record; it contains no embedded time, user, device, or job value. |
| `occurred_at_utc` | Required UTC timestamp rounded to seconds | No local user name, time-zone name, locale, or high-resolution timing is added. |
| `event_code` | Required compiled enum | Stable code registered with its field schema, classification, severity floor, and allowed sinks. It is not caller-provided text. |
| `severity` | Required enum: `Information`, `Warning`, `Error`, or `Critical` | A producer cannot lower the registered minimum severity for an event code. Persistent `Trace` and `Debug` levels do not exist. |
| `component` | Required compiled enum | Identifies a reviewed ThirdLife component, not a process path, assembly path, account, or machine. |
| `phase` | Required enum: `Startup`, `Intake`, `Inspect`, `Decide`, `Prepare`, `Verify`, `Handover`, or `Lifecycle` | Describes product phase only. It does not imply that the phase completed successfully. |
| `outcome` | Required enum: `Started`, `Progress`, `Completed`, `Failed`, `Cancelled`, `TimedOut`, `Unavailable`, or `RequiresReview` | Diagnostic outcome only. `Completed` is not `applied` or `verified`. |
| `job_ref` | Optional opaque internal identifier | May correlate a local operational event with a ThirdLife-owned job. It is never derived from recipient identity or a serial number. |
| `action_ref` | Optional opaque internal identifier | Refers only to a compiled, job-owned action identity. |
| `correlation_ref` | Optional random per-operation identifier | Binds bounded events within one operation. It is not reused as a stable device or person identifier. |
| `result_code` | Optional compiled enum | Uses a stable normalized result; provider text, exception text, exit output, and localized operating-system text are not valid result codes. |
| `duration_bucket` | Optional enum: `Under1Second`, `Under10Seconds`, `Under1Minute`, `Under10Minutes`, `Under1Hour`, `Under24Hours`, `Over24Hours`, or `Unknown` | Avoids unnecessary precise behavioral timing. |
| `item_count` | Optional unsigned integer from 0 through 65,535 | Values above the limit use 65,535 with `count_capped` set; no unbounded collection accompanies the count. |
| `count_capped` | Optional Boolean | States that `item_count` is a lower bound rather than an exact count. |
| `redaction_flags` | Optional set of at most eight compiled enum values | Records only transformations such as `ValueDropped`, `ValueReplaced`, `ValueTruncated`, or `InputRejected`; it never records the original value. |
| event-specific fields | Zero through twelve registered fields | The event-code registry fixes each name, type, classification, and allowed sink. Values may be Boolean, bounded integer, compiled enum, version tuple, opaque internal identifier, or a bounded reviewed identifier. Arbitrary objects, dictionaries, lists, and free-form strings are prohibited. |

A reviewed identifier is at most 128 ASCII characters and must resolve to a known ThirdLife schema, policy, profile, catalogue, provider, package, publisher, version, or source identifier. It cannot be a path, URI, command, argument, environment value, account identifier, user-entered label, or provider-supplied display text. Collections are permitted only when the event registry declares a finite enum set and a maximum count.

The encoded event is at most 8 KiB, including framing. The logger rejects over-limit records before persistence; it does not truncate an unknown serialization boundary or write a partial event. Event producers cannot add a generic `message`, `details`, `data`, `context`, `payload`, or similar escape field.

### Classification and write pipeline

The event-code registry assigns a non-optional privacy classification to every field. A caller cannot downgrade it. A write follows this order:

In operational terms, redaction happens before persistence, not only at support-export time.

The privacy classes from `privacy-model.md` have these logging dispositions:

| Class | Exact classification | Logging disposition |
|---|---|---|
| `PC-01` | Prohibited secret or foreign content | Reject or omit without echoing or deriving a stable identifier. No sink accepts the value. |
| `PC-02` | Recipient-controlled private choice | Do not log the choice value or secret; a governed workflow may record only a compiled pending/completed/verified status in its authoritative job record. |
| `PC-03` | Workshop-confidential record | Keep in the restricted authoritative job/workshop schema. Do not route it to ordinary diagnostics, recipient output, or support. |
| `PC-04` | Internal operational or pseudonymous metadata | Eligible for the closed local event envelope only after field and combination-risk validation. It is not anonymous. |
| `PC-05` | Support-allowlisted projection | Eligible only for a freshly generated support preview using the exact support schema; not for local-log replay. |
| `PC-06` | Recipient-facing projection | Eligible only for the dedicated plain-language recipient-guide schema; not for an event stream. |
| `PC-07` | Public or release metadata | Eligible only when the event schema names the exact public field and a lower classification cannot be inferred from combinations. |
| `PC-08` | Transient untrusted sensitive input | Bound and parse in memory, derive typed normalized values, then discard; never persist or echo the raw representation. |

1. Accept only the typed event code and its registered typed fields.
2. Classify each value before interpolation or serialization. Unknown classification is prohibited.
3. Canonicalize only as required by the declared type and apply the sink-specific drop, replacement, truncation, or rejection rule.
4. Discard the original sensitive representation as soon as the normalized value exists; do not retain it for a second sink or error message.
5. Validate the transformed event against the closed schema, prohibited-field detectors, collection limits, and 8 KiB byte limit.
6. Route only to sinks explicitly allowed for that event code.
7. Persist atomically enough that a torn or partial event is not treated as valid.

If any step fails, the unsafe event is rejected. A separate predeclared `LoggingEventRejected` code may report only the producing component, phase, finite rejection reason, and a bounded aggregate count. It must not include the rejected field name when the name is not registered, the rejected value, serialized bytes, or exception text.

## Prohibited fields

The following never enter the local operational log, UI diagnostic detail, crash report, support preview, support bundle, task evidence, or telemetry. They are also absent from filenames and temporary-path names:

- passwords, passphrases, PINs, tokens, cookies, authorization headers, credentials, private keys, encryption keys, recovery keys, recovery material, Wi-Fi secrets, clipboard contents, and secret-bearing environment values;
- donor or recipient content, file contents, message bodies, recordings, transcripts, documents, screenshots, clipboard data, and arbitrary user-entered text;
- recipient or donor names, usernames, account names, email addresses, phone numbers, postal addresses, user security identifiers, and other direct identity;
- full or transformed device serial numbers, hardware identifiers, product keys, activation keys, stable device fingerprints, machine names, and account-derived device labels;
- Wi-Fi names or identifiers, IP addresses, MAC addresses, network host names, share names, and remote-service identifiers;
- personal or machine-specific paths, filenames derived from personal content, command lines, process arguments, executable paths, registry paths or values, environment blocks, and temporary paths supplied by another component;
- package download URIs, query strings, fragments, redirect targets, request or response headers, response bodies, and signed download parameters;
- raw provider, installer, package, update, process, standard-output, standard-error, or command output;
- exception messages, stack traces, source file paths, serialized exception objects, inner-exception text, and arbitrary exception data;
- unbounded collections, binary payloads, attachments, database rows, journal snapshots, whole configuration objects, and whole report objects; and
- sibling workspaces, private databases, content, assessment evidence, messages, recordings, repositories, credentials, recovery material, or application records.

Hashing, encryption, encoding, compression, tokenization, partial masking, or truncation does not automatically make a prohibited field safe. Full serial numbers are restricted to the dedicated workshop-record schema and never pass through the logger. A future reviewed support contract may explicitly permit a non-full device hint only through a formal privacy change; no such hint is approved here.

## Sink contracts

Audience separation is structural. The operational logger cannot be configured to turn its files into another output class.

| Sink or channel | Source and allowed content | Required separation and failure posture |
|---|---|---|
| Transient operator status | Compiled event/result codes rendered through product resources, plus bounded normalized values allowed for the current screen. | Not persisted merely because it is displayed. Technical detail is expandable and never exposes a prohibited value. |
| Local operational log | The closed event envelope after local-log classification, transformation, validation, and size checks. | Stored only in the documented ThirdLife-owned log location. It is a deletable diagnostic aid, not an audit journal or evidence source. |
| Workshop record | A dedicated allowlisted projection from normalized job, evidence, decision, plan, journal, verification, and finalization repositories. | Not produced by copying or replaying logs. It may contain the full serial and attributable workshop fields explicitly allowed by its own schema, but never secrets or donor/recipient content. |
| Recipient guide | A dedicated plain-language projection of recipient-relevant verified outcomes, limitations, and pending steps. | Not a log sink. It contains no workshop identity, full serial, internal correlation IDs, technical event stream, or workshop secrets. |
| Sanitized support preview and bundle | A newly generated allowlisted projection described in **Support export**. | Not a copy, archive, tail, or filtered version of local log files. Export is blocked if any field lacks an approved support transformation. |
| Telemetry | No source and no accepted fields. | The sink, queue, uploader, endpoint, analytics identity, and background retry path are structurally absent as described in **Telemetry**. |
| Console, debug, crash, and operating-system event channels | No production event routing. | No verbose switch, environment variable, registry value, command-line flag, or support instruction may enable a raw fallback. Product-managed crash upload and raw dump export are absent. |

An event code has an explicit allowed-sink set. Adding a sink or broadening a field to another sink is a privacy-contract change with redaction fixtures and owner review, not a configuration edit. The elevated broker returns bounded structured results to the unelevated workflow; it does not open a log-file handle supplied by the caller or write to an arbitrary destination.

## Raw output and exceptions

Raw provider, package, update, installer, process, and operating-system output is untrusted sensitive input. Prefer structured APIs. When a later provider contract genuinely needs raw bytes to derive normalized evidence, it must:

- use the privacy model's proposed common maximum 4,096-byte in-memory read window unless the provider contract sets a smaller bound; a larger window requires an explicit reviewed contract change;
- set byte, line, record, depth, time, and collection limits before capture;
- keep capture in bounded memory or a separately governed restrictive job attachment, never in the log;
- decode and parse with an explicit format and failure state;
- extract only registered normalized values;
- classify and transform those values before any diagnostic event;
- discard temporary raw material according to the privacy model; and
- report `Unavailable`, `Failed`, or `RequiresReview` without embedding the input that caused the failure.

Passing arbitrary raw text through a redaction expression is not sufficient to make it loggable. A raw attachment, when a later task explicitly permits one, remains workshop-confidential data governed by its own size, access, retention, and deletion contract and is never automatically eligible for support export.

Exceptions follow a closed mapping from exception type and operation boundary to a stable `result_code`. The product may record the component, phase, outcome, correlation reference, and mapped result. It must not call or persist `Exception.ToString()`, an exception message, stack, source, target-site text, data dictionary, inner-exception text, request/response payload, or command output. An unmapped exception becomes a generic `UnexpectedFailure` result with no dynamic text. Operator-facing explanations use compiled static message templates selected by the stable code; they do not interpolate exception or provider text.

Logging and redaction failures do not recursively log themselves. There is no raw emergency file, console dump, alternate directory, operating-system event-log fallback, network crash reporter, or support-only bypass. Development and test diagnostics use synthetic data and the same prohibited-field rules; a development build is not permission to retain a real sensitive value.

## Local retention

The proposed default presented for named privacy-owner review is:

- at most **4 MiB per local operational-log file**;
- at most **5 files**;
- at most **20 MiB total**;
- a **14-day age-expiry threshold**, evaluated at the next write or application startup; and
- rotation or deletion when whichever size, count, total, or age bound is reached first.

Rotation and cleanup run only during a bounded application startup check or immediately before/after a log write. They do not require an always-running service, scheduled task, startup agent, background uploader, or permanent index. A file can physically remain beyond the 14-day threshold while ThirdLife does not run; it becomes due for removal at the next maintenance opportunity. Files use internal non-personal identifiers inside one fixed product-owned log root; neither an event producer nor operator text chooses a log path or filename.

Rotation deletes only positively identified ThirdLife-owned log generations. It never follows a link or reparse point, crosses the owned root, deletes a job attachment, mutates a journal, or treats log deletion as job deletion. A clock anomaly cannot disable the file-count and byte limits. Interrupted rotation is reconciled conservatively on the next application start; an ambiguous file is not copied into another sink.

An explicit whole-job deletion removes every current ThirdLife operational-log generation because any generation may contain the deleted job's opaque `job_ref`. It does not parse and rewrite shared log records selectively. The deletion preview states that unrelated operational diagnostics will also be lost; authoritative job, journal, and evidence records never depend on those logs.

If the logger cannot create, rotate, or remove owned files and cannot restore the 20 MiB quota, it stops persistent logging. It does not expand without bound or fall back to a new location. The current session keeps only a bounded diagnostic-degraded flag and aggregate rejection/drop counters, exposes the limitation to the operator, and continues the governed workflow only when its authoritative job/journal requirements remain satisfied. Restoring diagnostics requires a controlled retry after the storage or permission problem is corrected.

Manual early deletion, future uninstall behavior, and exact physical data locations must be implemented and documented by their owning tasks. No behavior is claimed by this draft beyond the proposed retention contract.

## Support export

**Never copied whole logs:** a support preview is regenerated on explicit operator request from allowlisted normalized projections. It does not copy local operational-log files, raw attachments, crash dumps, database files, journal snapshots, report source objects, or unknown files. This standard neither narrows nor expands the exact support allowlist in `privacy-model.md`; the allowed logical fields are:

- bundle identity: `support_id`, freshly generated for one bundle and unrelated to a job, person, or device identifier;
- product: `product_name`, `product_version`, `build_revision`, `bundle_schema_version`, and `redaction_rules_version`;
- operating system: `os_edition`, `os_build`, `os_architecture`, and `os_support_state`, without machine name, product key, account, tenant, or device identity;
- generic hardware: `hardware_manufacturer`, `hardware_model`, `device_form_factor`, `cpu_architecture`, `installed_memory_bucket`, `storage_media_class`, and `storage_capacity_bucket`, all normalized and without unique hardware identity;
- check result: `check_id`, `availability`, `outcome_code`, `limitation_code`, and bounded `observed_at_utc`, without raw evidence;
- action result: `action_type`, `result_code`, `verification_code`, `restart_state`, and `duration_bucket`, without arguments, approver identity, output, or unrestricted text;
- sanitized error: `error_code`, `error_category`, and `recovery_code`, using only stable reviewed codes;
- package metadata: `source_id`, `package_id`, `publisher`, `resolved_version`, `architecture`, and `scope`, using reviewed normalized values and no download URI, redirect, query, arguments, path, or backend stream;
- configuration provenance: `policy_version`, `profile_id`, `catalog_version`, and `configuration_digest`, without organization free text or a private source path;
- bounded operation summary: `started_at_utc`, `completed_at_utc`, `duration_bucket`, `attempt_count`, and `item_count`, omitted when combination risk is unacceptable; and
- bundle manifest: internal allowlisted `relative_name`, `content_sha256`, `byte_count`, and `generated_at_utc`, without a local source path or destination path. The generation timestamp is frozen before preview; destination-write completion time is separate job metadata and does not mutate previewed bytes.

Every support field has a stable name, type, reason, source projection, transformation, and omission rule. There is no catch-all note, comment, message, exception, metadata, or extra-files field. Optional fields may be removed in preview; the operator cannot add a prohibited field or arbitrary file through the support exporter.

The initial candidate export ceiling is 8 logical files, 2 MiB total uncompressed bytes, 10,000 normalized records, 32 members per collection, nesting depth 8, and 512 UTF-8 bytes per retained scalar. The exporter validates file, byte, record, collection, depth, and scalar limits before rendering the immutable preview. Exceeding a limit blocks export with a stable bounded result; it never triggers truncation of an unknown structure or raw fallback.

The accessible preview groups fields by logical artifact, explains why each category is included or omitted, shows truncation/capped/unknown markers, and exposes the exact generated manifest and content digest. Export regenerates from the same immutable normalized projection and must match the approved preview digest. A material projection, schema, rule, or content change invalidates the preview and requires a new review. Destination, reparse-point, overwrite, capacity, atomic-write, and partial-output controls remain owned by later export tasks.

If classification, transformation, rendering, manifest creation, digest comparison, or destination validation fails, no support bundle is represented as complete. Owned partial output is removed or clearly identified according to the later export contract, and the operator receives a safe retry or manual-support path. Preview and unexported bundle material is limited to the current session; a positively identified orphan becomes cleanup-eligible after 24 hours and is removed at the next application startup, so it can physically remain longer while ThirdLife does not run. ThirdLife does not transmit the bundle; after a successful explicit export, destination handling is outside product control and the UI must say so.

## Telemetry

Telemetry is off by default through structural absence, not through a preselected checkbox. There is no endpoint and no uploader for product diagnostics. Under this baseline the runtime has:

- no telemetry sink, analytics SDK, remote logging provider, crash uploader, or product-controlled collection endpoint;
- no telemetry queue, spool, retry worker, scheduled task, background service, or startup uploader;
- no analytics account, recipient identity, stable device identifier, advertising identifier, or cross-job tracking identifier;
- no logging-triggered network request, DNS lookup, package-source side channel, or hidden diagnostic upload; and
- no setting, command-line flag, environment variable, registry value, profile, or policy field that can enable a nonexistent telemetry route.

Package, Windows Update, catalogue, and optional self-update network use remains separate from diagnostics and receives no log payload. Explicit partner pilot metrics, if later approved, are a separately reviewed, human-initiated export of aggregated or pseudonymized fields. They are not telemetry, do not run in the background, do not block core use, and cannot be created by adding a sink to this event router.

Any future telemetry proposal must first revisit D-013 through formal change control, define consent and withdrawal, update the privacy and threat models, add an explicit network category, obtain privacy approval, and add adversarial tests. A logging implementation task cannot authorize that change.

## Failure and recovery

Logging fails closed for sensitive content and fails bounded for availability:

- schema, type, classification, redaction, or prohibited-field failure rejects the unsafe event before persistence;
- repeated equivalent diagnostic events are rate-limited or coalesced by event code and bounded count, never by storing a sample raw value;
- an in-memory write queue, if used, is bounded to 256 already-sanitized events and never spills to an alternate file or network sink;
- queue pressure drops or coalesces `Progress` and `Information` events before higher-severity events, records only bounded aggregate counters, and never delays a safety-critical operation indefinitely;
- disk-full, permission, quota, lock, corruption, or rotation failure opens a non-recursive diagnostic circuit breaker and stops persistent writes;
- cancellation and shutdown use a bounded flush; failure to flush is reported as incomplete diagnostics rather than blocking indefinitely or inventing a successful write;
- a logger failure cannot change authoritative job, approval, journal, verification, finalization, or handover state; and
- no recovery retries an action, replays a mutation, or reconstructs evidence from a diagnostic log.

The operator sees a persistent-in-session, plain-language diagnostic-degraded state with the affected capability and safe recovery step. If the state means a later support bundle will be incomplete, the preview repeats that limitation. A diagnostic warning never claims that the machine action itself failed or succeeded; authoritative state comes from the journal and independent verification.

## Accessibility and low-spec impact

- Diagnostic availability, rejection, quota, export, cancellation, and recovery states use programmatic names/roles/states, visible focus, screen-reader announcements, high contrast, and text rather than color alone.
- Stored event codes render through bounded compiled plain-language resources. Raw provider or exception text is not needed to make an error understandable.
- Support preview is keyboard operable, logically ordered, and readable at 200% scaling and reduced resolution. It offers a concise summary plus a bounded expert view instead of requiring navigation through an unbounded event stream.
- Redacted, omitted, capped, unavailable, and unknown values are distinguishable in text without exposing the removed value.
- Log writing and support generation stream or chunk data, avoid repeated full-file loading, and preserve UI responsiveness and safe cancellation.
- Queue, event, property, file, age, total-byte, preview, and export bounds are enforced before allocation or output growth becomes unbounded.
- Rotation runs at startup/write boundaries with conservative concurrency; there is no permanent indexing, polling, or GPU requirement.
- Low-resource behavior may reduce progress-event frequency, but it cannot disable prohibited-field checks, sink separation, operator warnings, accessible status, or safe cancellation.
- Later implementation evidence records peak memory, temporary bytes, final output size, elapsed time, cancellation, and recovery against synthetic normal, large, malformed, and adversarial fixtures.

## Privacy-owner review checklist

The named privacy owner must review the exact Git commit and record name, role, date, result, and immutable reference. The review must confirm or require changes to:

- the closed event envelope, field types, byte/count/property limits, and absence of free-form escape fields;
- the complete prohibited-field list, including recipient identity, full serial restriction, secrets, raw output, exceptions, paths, network identifiers, and sibling data;
- classify-and-transform-before-persistence ordering and fail-closed handling of unknown values;
- structural separation of local diagnostics, workshop record, recipient guide, support output, and absent telemetry;
- the proposed 4 MiB × 5 files, 20 MiB total, 14-day age-expiry threshold, write/startup maintenance, possible physical over-retention, and bounded quota-failure behavior;
- support-field allowlists, normalized regeneration, accessible preview, content-digest binding, and explicit post-export limitation;
- exception mapping, raw-output handling, lack of raw debug/crash fallback, and diagnostic failure/recovery semantics;
- synthetic redaction cases and exact expected transformations, including adversarial boundary cases; and
- accessibility, keyboard/screen-reader status, low-spec bounds, cancellation, and resource-evidence obligations.

The owner records one result: approved, approved with recorded conditions, or changes required. Approval of this standard does not implement logging, approve a release, accept a residual privacy risk, or authorize telemetry.

**Review result:** Pending

No named privacy owner has approved this exact revision, its classifications, prohibited and allowed fields, sink contracts, or proposed default retention guidance. This pending draft does not satisfy the human evidence required by `TL-0005`. Privacy-model approval does not authorize implementation or release claims.

Human approval of the classifications and default retention guidance remains pending.
