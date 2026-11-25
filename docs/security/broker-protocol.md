# Broker protocol v1

TL-0310 defines the bounded payload contract shared by the unelevated client and
the ephemeral broker. This version carries synthetic no-op actions. Package
requests are outside this protocol version. A qualified independent Windows or
vendor package route uses its own typed adapter and the same plan and journal controls. [ADR 0004](../adr/0004-ephemeral-broker.md) ·
[D-030](../../DECISIONS.md#d-030)

## Payload

Each payload is one strict UTF-8 JSON object of 1–4,096 bytes with at most four
levels of nesting. The codec rejects a UTF-8 byte-order mark, invalid encoding,
comments, trailing content, duplicate keys, unknown fields, and unsupported
versions or message kinds. Property names and wire values are case-sensitive.

Every kind carries these fields.

| Field | Format |
| --- | --- |
| `protocol_version` | JSON integer `1`. |
| `kind` | A message kind from the table below. |
| `correlation_id` | Nonzero lowercase UUID in canonical hyphenated form. |
| `nonce` | 32 bytes represented as exactly 64 lowercase hexadecimal characters. |
| `expires_at_utc` | Valid Gregorian UTC timestamp in the exact 28-character form `yyyy-MM-ddTHH:mm:ss.fffffffZ`. |

The remaining fields are specific to each kind.

| Kind | Additional required fields | Purpose |
| --- | --- | --- |
| `hello` | None | Offer the session context. |
| `hello_ack` | None | Acknowledge the session context. |
| `request` | `job_id`, `approval_id`, `plan_sha256`, `impact_sha256`, `actions` | Describe the exact approved job and its ordered batch. |
| `progress` | `action_id`, `sequence`, `stage` | Report `accepted` or `running` for an action. |
| `result` | `action_id`, `sequence`, `status` | Report `applied`, `failed`, `cancelled`, or `requires_review`. |
| `cancel` | `action_id` | Ask to cancel that action. |
| `error` | `code` | Report `invalid_message`, `unauthorized`, `expired`, `replay`, `invalid_state`, or `unavailable`. |

The job ID follows the Core opaque identifier contract. It contains 1–128
ASCII letters, digits, hyphens, or underscores. The validator also rejects
reserved Windows device names. Approval and action IDs use the same UUID
format as the correlation ID. Each digest uses exactly 64 lowercase
hexadecimal characters. A progress or result sequence is a JSON integer from
1 through 32.

A request carries one to four distinct action IDs. Each element in `actions`
uses the compiled action contract with exactly `contract_version` set to
integer `1`, `action_id` set to the action UUID, `type` set to `no_op`, and
`parameters` set to an empty object. The protocol offers typed no-op requests
and omits executable text, paths, URLs, registry addresses, and file
operations. The error payload contains its fixed code. Decoder failures
produce a fixed diagnostic and omit input values and parser exception details.

A cancellation is a request. A terminal result reports the outcome of the
attempt. An `applied` result still requires separate, fresh verification before
the journal accepts a verified state. [D-032](../../DECISIONS.md#d-032)

The JSON Schema validates decoded values and closed message shapes. The
runtime additionally enforces byte length, nesting, unique keys, integer
tokens, Gregorian dates, and unique action IDs. The codec provides a
canonical serialization and SHA-256 for a request, including its session
fields, plan and impact digests, and ordered actions. The expiry helper accepts
a deadline strictly later than the supplied current time and at most two
minutes ahead. [Protocol schema](../../schemas/broker-protocol.schema.json)

## Security checks

| Abuse case | TL-0310 controls | TL-0311 integration |
| --- | --- | --- |
| AC-002 | Strict decoding and bounds reject unknown versions, kinds, fields, and operations. The compiled action codec checks each action shape. | The pipe applies the payload size before allocation and enforces session state and pacing. |
| AC-003 | The envelope carries the nonce, expiry, correlation, and exact plan, impact, approval, and action identifiers. The codec can hash the canonical request. | The authenticated session pins that request hash at bootstrap and checks the current deadline, correlation, nonce, replay, and monotonic message sequence. |
| AC-004 | Message kinds distinguish the hello and acknowledgement payloads. | Operating-system peer identity and restrictive pipe permissions establish the user and session. The transport enforces which role may send each kind. |

The existing threat model governs authorization at dispatch and handling of
ambiguous termination. TL-0310 verifies codec and schema behavior. TL-0311
owns authenticated IPC, elevation, dispatch, cancellation handling, and
process teardown. [Threat model](threat-model.md) ·
[Abuse cases](abuse-cases.md) · [Security requirements](../../SECURITY.md#7-privileged-broker-requirements)
