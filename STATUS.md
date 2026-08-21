# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-08-21  
**Snapshot preparation time:** 2026-08-21T16:01:19+02:00  
**Bundle baseline:** 0.3.0  
**Portfolio baseline:** ThirdLife Software Portfolio v2.1  
**Current milestone:** M0 — Foundation and product contract  
**Current task:** `TL-0005` — Define the privacy and logging model  
**Task state:** `review`

## Current state

The automatable part of `TL-0005` is complete. The repository now contains a governed privacy model, logging standard, and machine-validated synthetic redaction fixture set. They define classifications, retention guidance, audience-separated outputs, prohibited diagnostic content, the exact sanitized-support allowlist, raw-input handling, and telemetry-off behavior before logging code is built.

`TL-0005` is correctly in `review`, not `done`. Its required human evidence is still absent: a named privacy owner must approve the classifications and default retention guidance. The documents and fixtures explicitly preserve that pending state and do not claim runtime redaction, logging, deletion, export, or telemetry controls already exist.

The commit containing this file is the TL-0005 checkpoint. Because a commit cannot embed its own hash, resolve that identity with `git rev-parse HEAD`; the session completion report records the resolved hash and the post-push equality check.

## Git state

| Field | Verified value |
|---|---|
| Remote | `origin` → GitHub repository `PikkuJanne/ThirdLife` |
| Branch | `codex/tl-0005-privacy-logging-model` |
| Session baseline | `c77966ba90a31b99e790a2d1097b598c1f127961` |
| History handling | Continued from the fetched baseline; no reset, rebase, force push, or history rewrite |
| Checkpoint | The commit containing this file; resolve with `git rev-parse HEAD` |
| Upstream | Created by the first checkpoint push to `origin/codex/tl-0005-privacy-logging-model` |
| Final push invariant | Before handoff, current HEAD must equal the fetched upstream; the completion report records the resolved hash |

The configured SSH remote rejected unattended public-key authentication on this machine. Fetch and checkpoint publication use GitHub CLI's authenticated HTTPS credential bridge without changing the configured remote or exposing a credential.

## Preserved testing-transition provenance

The historical `TL-0008` draft-1 physical-device procedure remains **superseded** and must not be executed. Its source commit was `4fa3ea050fd5e9985fde9cc8218281698d371cc8`, and its archived procedure SHA-256 is `ef150dbf14b5db208582b7b526c7e0c6d0a5b912736e9e6519b8918abcf0928b`. No physical hardware walkthrough was performed for the v0.3.0 transition; the active testing contract remains the same-machine, tiered contract in `TESTING.md`.

## Privacy outcome

The contracts establish these fail-closed invariants:

- recipient identity is unnecessary for ordinary workshop jobs;
- a full serial number is restricted to the technical workshop record and omitted from recipient and support outputs;
- sibling workspaces, private databases, personal content, assessment evidence, and backup or recovery keys are outside ThirdLife-owned data;
- raw command or provider output is untrusted sensitive input, is not persisted, and may produce only a bounded typed projection;
- support exports use an exact allowlist, are preview-bound, and require explicit operator approval;
- telemetry and background upload remain off by default;
- workshop record, recipient guide, and sanitized support bundle remain distinct output contracts.

The privacy model defines eight classifications: public reference, operational safe, workshop restricted, recipient guide, support sanitized, raw untrusted sensitive, secret or personal content excluded, and sibling private excluded.

## Proposed default retention guidance

These are proposed, ready-for-review design defaults, not approved or implemented cleanup behavior:

| Data | Proposed default |
|---|---|
| Active job records and typed history | Active job plus 180 days |
| Sanitized application logs | 14 days with a size cap |
| Raw provider or command output | Process lifetime only; zero persistent retention |
| Temporary and staging data | Remove on success; reap abandoned data within 24 hours |
| Exported support bundle | Operator-controlled file; guide recommends removal within 30 days |
| Unreferenced superseded profile, policy, and catalogue snapshots | 90 days after supersession unless a retained job still references them |
| Migration and recovery backup | Successful verification plus 7 days |
| Package cache | 30 days unless an active job or rollback still references it |
| Secrets, personal content, sibling-private data, and telemetry payloads | Zero owned retention |

## Governed artifacts

| Artifact | Recorded SHA-256 |
|---|---|
| `docs/privacy/privacy-model.md` | `a10e3248703ceb359a005173dccde9941de4595cf10b382ecb3d9acbb819f5ee` |
| `docs/privacy/logging-standard.md` | `5f0d27cd93ba921b768f621401b91710d3f04263fe47152cb1baf8ffec6eb880` |
| `docs/privacy/redaction-test-cases.yaml` | `6f43b2f31565f614218b7a58cbf8b43b157efeb03767ec51d60ddc5a3f0e354a` |

The fixture contains 56 contiguous, wholly synthetic cases. It covers recipient identifiers, full serial handling, secrets and recovery material, personal and sibling-private data, raw output, URLs and paths, exact digest binding, ordinary allowlisted metadata, unknown fields, and telemetry suppression.

## Verification evidence

| Scope | Result | Duration |
|---|---|---:|
| Pre-change validator tests | 54/54 passed | 13.140 s test / 14.073 s wall |
| Pre-change live bundle validator | Passed: 91 tasks, 8 milestones, 66 decisions, valid DAG | 1.330 s wall |
| Final privacy-focused regression tests | 25/25 passed | 29.177 s test / 29.945 s wall |
| Final complete validator regression suite | 79/79 passed | 42.366 s test / 43.207 s wall |
| Final live bundle validator before handoff metadata | Passed: 91 tasks, 8 milestones, 66 decisions, valid DAG | 1.328 s wall |
| Initial governed Quick tier | Passed | 25.536 s wall |

The exact-tree Quick tier is run after this status record and the bundle manifest are finalized. Its result is recorded in the session completion report without changing the verified tree afterward. Full and extended tiers have no `TL-0005` triggers and are not required for this documentation/schema task.

The validator now fails closed on privacy-contract drift, including:

- exact Markdown retention and support-field tables;
- exact fixture schema, case IDs, actions, persistence, and support outcomes;
- secret-like test input before any synthetic-data exemption;
- aliases, anchors, merge keys, duplicate or non-string mapping keys, unsafe scalar types, and bounded hostile YAML;
- false approval wording, placeholder reviewers, incomplete scope, and approval references that do not bind the current governed blobs to a real Git commit.

The optional `jsonschema` package is unavailable in the active environment, so the repository's custom structural checks ran. That is an informational warning, not a skipped governed check.

## Boundary and risk impact

- **Project vacuum / sibling integration:** No sibling repository was browsed or coupled. Sibling-private data is explicitly excluded and adversarially tested.
- **Data / migration:** No runtime data, database, or migration was added. The task defines future retention and deletion contracts only.
- **Release interface:** No public runtime interface was added or guessed. `RELEASE_INTERFACE.md` is unchanged.
- **Security / privacy:** The task reduces design ambiguity and adds fail-closed semantic validation. Human privacy approval and later runtime implementation remain required.
- **Accessibility / low-spec:** No UI, background work, runtime storage, network activity, or resource-consuming service was added. Later user-visible export and cleanup work must preserve plain language, keyboard access, bounded work, cancellation, and recovery.
- **Security baseline provenance:** `docs/security/threat-model.md`, `docs/security/data-flow.md`, and `docs/security/abuse-cases.md` contain traceability/status annotations only. They do not claim a new security review or change the previously approved threat set, residual risks, or sign-off.

## Changed paths

- `docs/privacy/privacy-model.md`
- `docs/privacy/logging-standard.md`
- `docs/privacy/redaction-test-cases.yaml`
- `tools/validate_bundle.py`
- `tools/tests/test_validate_bundle.py`
- `README.md`
- `SECURITY.md`
- `docs/product-contract.md`
- `docs/glossary.md`
- `docs/security/threat-model.md`
- `docs/security/data-flow.md`
- `docs/security/abuse-cases.md`
- `TASKS.yaml`
- `STATUS.md`
- `BUNDLE_MANIFEST.sha256`

The unrelated untracked `ThirdLife_Two-Team_Software_Portfolio_Roadmap_v2.1.docx` was present before this task and remains untouched and unstaged.

## Outstanding human evidence

A named privacy owner must review and approve all of the following before `TL-0005` may move from `review` to `done`:

1. the complete classification model;
2. every proposed default retention period and cleanup trigger;
3. the redaction and omission behavior represented by the synthetic fixtures;
4. the prohibited diagnostic content and exact sanitized-support allowlist.

The approval record must identify the reviewer and privacy-owner role, date the review, cover the complete scope, record conditions or residual risks, and bind the reviewed governed artifacts to a real commit. No such approval is fabricated here.

## Next dependency-ready task

`TL-0006` — Create dependency, license, and SBOM controls.
