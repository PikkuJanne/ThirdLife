# ThirdLife Setup Core — Logging and Diagnostic Export Standard

**Status:** Approved initial privacy contract  
**Standard revision:** TL-0005 approved 1  
**Draft date:** 2026-08-21  
**Authority:** [`privacy-model.md`](privacy-model.md), D-011, D-013, D-014, D-036, D-037, and D-053

This standard defines what later ThirdLife logging, error, and support-export code may accept and emit. It is a design contract, not a claim that a logger or redactor exists. Unknown fields and unsafe values fail closed; production code must not fall back to raw text for convenience.

## Required architecture

1. Callers submit a reviewed event type and typed values, not a message template plus arbitrary arguments.
2. Each event type owns an exact field allowlist, type, maximum length/count, privacy class, safe renderer, and support-export eligibility.
3. Sensitive domain values are not implicitly string-convertible for logging. A caller must request an explicitly reviewed safe representation.
4. Redaction and bounds are applied before the first database, file, UI, report, or queue write. A later export performs the support allowlist and redaction again.
5. Raw provider/backend/installer/command/exception streams are parsed in a bounded transient adapter. The adapter returns typed normalized results and stable error codes; it never returns a raw diagnostic string as the production contract.
6. Unknown keys, nested objects, duplicate keys, invalid encodings, control characters, and values exceeding bounds are rejected or replaced as a whole with the appropriate fixed redaction marker.
7. Logging failure cannot authorize a mutation, convert unknown to pass, hide a blocker, or make applied become verified. The workflow retains a bounded visible `diagnostic_unavailable`/`diagnostic_rejected` state where operationally relevant.
8. There is no telemetry or background upload path. Support data leaves the application only through explicit preview-bound export.

## Ordinary event envelope

The following fields are the complete cross-event envelope. An event-specific contract may use a subset and add only separately registered allowlisted fields.

| Field | Type and constraint | Privacy class | Notes |
|---|---|---|---|
| `schema_version` | Reviewed semantic/version identifier | `OPERATIONAL_SAFE` | Reject unsupported versions; no silent downgrade. |
| `event_id` | Fresh opaque random ID | `OPERATIONAL_SAFE` | Not derived from device/user/job values. |
| `event_code` | Registered stable enum | `OPERATIONAL_SAFE` | Primary diagnostic meaning; never user-provided text. |
| `component` | Registered stable enum | `OPERATIONAL_SAFE` | No assembly path, executable path, or hostname. |
| `phase` | Registered stable enum | `OPERATIONAL_SAFE` | Such as `collect`, `plan`, `execute`, `verify`, `export`, or `cleanup`. |
| `severity` | Registered enum | `OPERATIONAL_SAFE` | Severity does not imply product safety/disposition. |
| `occurred_at_utc` | Offset-aware UTC timestamp | `OPERATIONAL_SAFE` locally | Support export includes only when its schema explicitly needs it. |
| `correlation_id` | Fresh opaque operation ID | `OPERATIONAL_SAFE` locally | Do not reuse a hardware, account, package URL, or external service ID. |
| `job_id` | Random internal ID | `WORKSHOP_RESTRICTED` | Permitted in protected job diagnostics only; map to a fresh `internal_support_id` before support export. |
| `action_id` | Random internal ID | `WORKSHOP_RESTRICTED` locally | Support uses action type/state, not the internal ID, unless a later privacy-reviewed schema permits a fresh export alias. |
| `result_code` | Registered stable enum | `OPERATIONAL_SAFE` | Prefer over exception/backend prose. |
| `availability` | Registered enum | `OPERATIONAL_SAFE` | Preserve observed/inferred/not-available/human-confirmed distinctions where applicable. |
| `duration_ms` | Bounded non-negative integer | `OPERATIONAL_SAFE` | Include only for a registered event; never an unbounded timer or wall-clock trace. |
| `safe_message_key` | Registered resource key | `OPERATIONAL_SAFE` | Rendering uses fixed product-authored text with typed safe substitutions only. |
| `retryability` | Registered enum | `OPERATIONAL_SAFE` | Never recommends bypassing trust, ownership, OS, activation, or verification controls. |

No free-form `message`, `details`, `data`, `context`, `payload`, `command`, `arguments`, `environment`, or arbitrary dictionary field is permitted.

## Event-specific allowed value families

An event registration may use these families only when needed:

- product, build, schema, policy, profile, and catalogue version identifiers validated against their own contracts;
- stable provider/check/action/result/verification/limitation IDs and enums;
- Boolean state and bounded non-negative counts;
- coarse reviewed resource buckets rather than exact unique hardware values;
- approved catalogue package identity, display name, publisher, and version as structured fields, never a download URL or installer argument;
- normalized OS family/version/architecture and generic hardware manufacturer/model/class when the owning event explicitly permits them;
- a redaction marker from the fixed vocabulary below; and
- a product-authored localized message chosen by stable code, with substitutions restricted to separately safe typed values.

An allowlisted field is still omitted when its value is unavailable or fails validation. Do not stringify an object or append raw source text to explain why validation failed.

## Prohibited diagnostic fields and values

The following are prohibited from ordinary logs and default support output. Detection removes the value; encryption, encoding, truncation, hashing, or a debug flag does not make it ordinary diagnostic data.

| Category | Prohibited examples | Safe alternative |
|---|---|---|
| Person identity | Recipient, donor, operator, account-holder, or contact names; initials when identifying; email, phone, postal address | Random internal ID locally; fresh `internal_support_id`; role enum |
| Account identity | Windows/local/domain/cloud username, UPN, SID when externally linkable, tenant/account ID, user-profile folder | Opaque local actor ID or role; stable authentication/result code |
| Device identity | Full serial, BIOS/baseboard/disk serial, asset tag, hardware UUID, Windows product/device ID, hostname/device name | Generic manufacturer/model/class; full serial only in workshop record; optional reviewed four-character suffix |
| Network identity | SSID, BSSID, Wi-Fi profile, MAC, IP address, gateway, DNS/search suffix, public IP, nearby network, Bluetooth address/name when identifying | Network capability/state enum and stable error code |
| Paths and content names | User profile/personal path, filename derived from user content, UNC/share path, removable-volume label, export destination path, registry value containing user data | Registered internal root ID, fixed artifact kind, destination type enum |
| URLs and source details | Package download URL, redirect, query string, signed URL, proxy URL, arbitrary URI, referrer | Approved source/catalogue ID and package ID/version as typed fields |
| Credentials and secrets | Password, PIN, passphrase, API/session/OAuth token, cookie, authorization header, connection string, private key, certificate private material, Wi-Fi secret, remote-support secret | `configured`/`not_configured`/`requires_recipient` or stable failure code without the value |
| Recovery/encryption material | Recovery key, backup key, BitLocker recovery password, seed phrase, escrow secret, raw key protector data | Ownership/status enum and explicit recipient/organization handoff state |
| Personal or donor content | Document/media/message/recording/transcript contents or excerpts, clipboard, browser history/form data, search terms, screenshots, camera/microphone capture | Content-presence is not scanned; use a scoped capability/result code |
| Raw execution/provider data | Command line, arbitrary arguments, script, stdout, stderr, provider/backend/installer output, localized tables, temporary reports, exception message, stack trace, dump | Typed normalized observation/result plus stable code and safe product-authored guidance |
| Sibling-private data | Sibling workspace, database, record, content, assessment evidence, backup/recovery key, logs/settings, private schema, or path | No collection or interface; public frozen release metadata only in a future separately owned B4 task |
| Environment/process detail | Environment variable values, process command line, module/search paths, open handles, memory dump, raw registry data, machine-specific development path | Registered component/phase/build revision and stable code |
| Unbounded/free-form data | Arbitrary map/object, unknown key, nested payload, binary/base64 blob, control/escape sequence, markup/formula content | Reject; record a fixed `diagnostic_rejected` reason and bounded counts only |

## Raw input normalization

All raw provider, backend, installer, command, exception, and temporary-report content enters as `RAW_UNTRUSTED_SENSITIVE`.

1. Enforce byte, record, depth, time, and rate bounds before parsing; use cancellation and a fixed encoding policy.
2. Prefer structured supported APIs. Localized command tables are not a production contract.
3. Parse only expected typed fields; reject duplicate/conflicting records and preserve explicit unavailable/failed state.
4. Normalize values into domain/provider result types with source, time, availability, and provenance.
5. Emit stable codes and safe typed values. Do not carry the raw buffer inside an exception or result object that might later be logged.
6. Release the raw buffer at the end of the bounded operation. Default persistent retention is zero.
7. If parsing or redaction cannot classify a value confidently, omit/drop the complete value and emit a fixed rejection marker/code. Never expose the original as a troubleshooting fallback.

A future provider may persist a bounded raw attachment only through an explicit reviewed contract naming purpose, type, maximum size/count, `WORKSHOP_RESTRICTED` access, retention, cleanup, and exclusion from reports/support. That exception does not change the logging rule.

## Fixed redaction representation

The canonical forms and adversarial examples are checked in as [`redaction-test-cases.yaml`](redaction-test-cases.yaml). Implementations must preserve those exact ASCII markers and must not include a value length, hash, prefix, or suffix that enables correlation. The sole planned exception is the separately previewed optional serial-suffix support field described below.

Redaction is applied before escaping/rendering and again at the support projection boundary. Secret/recovery patterns take precedence over general number, path, URL, and identifier patterns so that overlapping text cannot leak. After replacement, outputs are checked for prohibited patterns, Unicode/control spoofing, markup/formula injection, size limits, and unknown fields. Failure rejects the field or export; it does not disable a check.

## Sanitized support schema

The support bundle is an independent allowlisted projection, not a copy of a log directory or job database. The following 25 names exactly match `support_export_allowlist` in `redaction-test-cases.yaml`. A support record may repeat the applicable scalar fields, but may not introduce arbitrary nested data. Fields not listed here are prohibited until a later privacy-reviewed schema version explicitly adds them.

### Default fields

| Field | Constraint |
|---|---|
| `schema_version` | Exact reviewed support schema version. |
| `manifest_version` | Exact reviewed manifest version. |
| `internal_support_id` | Fresh opaque random export ID with no encoded job/device/user value. |
| `application_version` | Installed ThirdLife Setup Core version. |
| `build_version` | Reviewed source/build revision identifier. |
| `os_version` | Normalized OS version/build, not a product/device ID. |
| `hardware_architecture` | Fixed architecture enum. |
| `memory_bucket` | Reviewed coarse capacity bucket, not module identifiers. |
| `storage_class` | Reviewed generic storage class, not disk identity or path. |
| `event_time_utc` | Offset-aware UTC time only for an included event. |
| `export_created_at_utc` | Exact preview/export creation time. |
| `check_id` | Registered check ID. |
| `check_outcome` | Registered bounded outcome enum. |
| `action_code` | Registered compiled action code, not a command or argument. |
| `result_code` | Registered stable result code. |
| `component_id` | Registered stable component ID. |
| `operation_type` | Registered bounded operation enum. |
| `evidence_state` | Registered evidence/availability state. |
| `sanitized_error_category` | Registered category; no backend/exception prose. |
| `retryable` | Boolean determined by reviewed recovery policy. |
| `duration_ms` | Bounded non-negative duration for the included operation. |
| `bounded_count` | Bounded non-negative count whose meaning is fixed by the record schema. |
| `limitation_code` | Registered stable limitation code. |
| `preview_content_digest_sha256` | Digest of exact preview bytes; never a hash of omitted personal data. |
| `export_content_digest_sha256` | Digest of exact exported bytes for preview/export equality. |

The current schema has no optional diagnostic fields. “Explicit reviewed inclusion” means selecting only a field or fixed file that a future privacy-owner-approved schema already permits; it never means typing an arbitrary value or attaching an arbitrary file. A future approved schema may add a fixed serial truncation/suffix, but a per-export checkbox cannot authorize a full serial, and the current schema exports neither full nor truncated serial.

Secrets, recovery material, personal content, recipient/donor/operator names, usernames, network identifiers, full serials, personal paths, download URLs, raw output, crash dumps, arbitrary attachments, and sibling-private data can never become optional support fields.

## Preview-bound export procedure

1. Freeze a read-only snapshot of normalized records.
2. Project only the support schema, validate every type/bound, redact, escape, and reject unknown fields.
3. Build the complete fixed file list and deterministic bytes; compute the manifest/content digest.
4. Present every field and file, its purpose, optional-inclusion state, total size, retention warning, and exact destination type in an accessible keyboard/screen-reader preview. No item is hidden behind color or hover.
5. Require explicit operator approval. A changed snapshot, schema, option, or byte invalidates approval and requires a new preview.
6. Revalidate the final destination object, ACL/type, reparse/link status, overwrite policy, and capacity immediately before a bounded atomic write.
7. On success, store in the protected workshop record only `internal_support_id`, schema/manifest versions, content digest, export time, and operator attribution. Do not put the personal destination path in ordinary diagnostics.
8. On cancellation/failure, leave the existing destination unchanged, remove only a verified owned partial file, retain the preview for safe retry when appropriate, and show a sanitized recovery path.

There is no send, upload, remote-support, or analytics shortcut. Manual transfer of the exact reviewed archive remains the support path.

## Exceptions and crash handling

- Map known failures at the adapter boundary to stable result codes and product-authored guidance.
- Unhandled exception messages, inner exceptions, stack traces, dumps, environment values, command lines, and loaded-module paths are prohibited from production persistence/display/support output.
- A local development test runner may show its own process output to a developer, but those bytes are not product diagnostics, task evidence, or support artifacts and must not be checked in when they contain machine-specific or sensitive values.
- Crash recovery records only a fresh correlation ID, component, phase, build revision, stable crash/result code, timestamp, and whether durable state may be ambiguous. It does not infer that a mutation failed or succeeded.

## Retention, bounds, and cleanup

Logging code must implement the approved 14-day sanitized-log default from `privacy-model.md` together with a measured/configured byte ceiling. Retention is enforced by both age and size. Individual values, records, collections, files, queues, retries, and render/export operations also require reviewed bounds; dropping data due to a bound remains an explicit sanitized state.

Temporary and staged diagnostic bytes use random internal names, restrictive creation, and operation-scoped lifetime. Cleanup handles interruption, access denial, full disk, corrupt metadata, wrong clock, and links/reparse points without broad deletion. A cleanup failure never copies a prohibited path/value into another log.

## Verification contract for later implementation

`TL-0104` and later diagnostic/report tasks must:

- execute every synthetic fixture and confirm the exact expected log and support form;
- fuzz casing, separators, nesting, duplicate/unknown keys, Unicode confusables, control characters, markup/formulas, encodings, and overlapping secret/path/URL/identifier patterns;
- test unhandled exceptions, stdout/stderr, localized/malformed/oversized provider/backend output, cancellation, and redaction failure;
- inspect representative local logs and exact preview/export bytes for prohibited fields;
- prove default support output contains only the enumerated fields and optional fields require a fresh explicit preview;
- prove no telemetry SDK, background uploader, hidden network category, recipient identity dependency, or sibling data access exists; and
- record durations, bounds, fixture hashes, environment, cleanup, and claim limitations on the active Codex machine under the applicable test tier.

Automated fixture/schema checks in `TL-0005` validate the design artifacts only. They do not claim a production redactor, logger, retention job, or support exporter exists; the separate approval record covers only human contract review.

## Change and approval rule

Adding a logged field, raw attachment, exception detail, support field/file, new output audience, telemetry proposal, or retention change triggers privacy and threat-model review. A lower-authority implementation cannot silently broaden this standard. The privacy-owner approval record is maintained in `privacy-model.md`; it is approved for the exact reviewed commit.
