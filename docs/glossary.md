# ThirdLife Setup Core — Glossary

**Status:** Governed working vocabulary  
**Authority:** Exact frozen decisions and versioned schemas/policies prevail over this summary

Terms are deliberately narrow. Similar everyday words do not relax the evidence, state, approval, or portfolio boundaries below.

## Evidence

An attributable record about a device, job, decision, action, or verification result. Evidence records its classification, source or operator, collection time, provenance, and whether a value is available. Evidence is **observed**, **inferred**, **not available**, or **human confirmed**. Missing evidence is unknown; it is not a pass. Product/job evidence is distinct from a `TASKS.yaml` evidence entry, which records proof that task work or review occurred.

## Requirement

A stable, versioned policy or profile condition that an outcome must meet. A requirement defines its decision effect, expected evidence, and whether it is blocking, repairable, advisory, profile-dependent, or human-confirmed. A product requirement is not itself an observation, cannot rewrite evidence, and is distinct from a task acceptance criterion.

## Blocker

An unresolved condition that prevents a defined transition, such as preparation, action execution, ready disposition, handover, pilot authorization, or stable release. A blocker remains visible until new evidence resolves it or a versioned policy permits a named, attributable exception. Prohibited rules and safety boundaries cannot be overridden by an ordinary exception. A product blocker is distinct from the task workflow state `blocked`, although a concrete product or evidence issue may cause that task state.

## Disposition

An explainable, reproducible policy conclusion derived from immutable evidence and a named policy version. The five dispositions are **Ready to prepare**, **Repair and retest**, **Human review required**, **Alternative operating system candidate**, and **Do not deploy**. Every disposition cites its controlling requirements and evidence; it is not a numeric score or a fact about all possible uses of the device. **Ready to prepare** is a device/job result, not the `TASKS.yaml` state `ready`.

## Applied

The journal state meaning an approved action reports that its mutation attempt took effect. Applied is not complete, successful handover, or proof of the target outcome. It remains distinct from verified, failed, skipped, rolled back, and requires-review states.

## Verified

The journal state meaning a separate, bounded verification step independently observed the approved target outcome. Backend or installer success and exit code zero are insufficient by themselves. Verification records the method, evidence, time, expected identity/state, and limitations; final acceptance may require a cold boot and fresh verification.

## Frozen release

An exact, immutable release candidate or release identified by product version, source revision, dependency lock, artifacts, cryptographic hashes, provenance/signing record, SBOM and licence evidence, documentation, known limitations, and non-sensitive samples. “Latest,” an active branch, or an unrecorded rebuilt binary is not the same frozen release.

## Compatibility cut

A future B4-owned, version-bounded record that states how exact frozen product releases work together, what was tested, supported limitations, and the manual fallback. A compatibility cut never means automatic support for a newer release and never creates a dependency between active product branches.

## Adapter

An implementation boundary that converts a typed ThirdLife contract to a structured platform or backend contract. Core may use project-local provider, persistence, package, update, or reporting adapters when they independently serve Core and preserve domain direction.

A **sibling adapter** has the narrower portfolio meaning: optional integration code owned by the future Team B / B4 assembly project that uses documented public behavior of exact frozen releases. It is version-bounded, independently disableable, privacy-reviewed, and paired with a manual fallback. It does not justify private database access, shared infrastructure, or B1 implementation.

## Related vocabulary

### Observation

A read-only provider or human-test result before policy evaluation. An observation becomes evidence only with the required source, time, classification, availability, and provenance.

### Policy

Versioned organization input that evaluates evidence and produces requirement outcomes and a disposition. Policy is declarative and non-executable. A policy change does not rewrite a historical job.

### Profile

Declarative, outcome-based input describing workshop capabilities and separately deferred recipient choices. A profile resolves through reviewed catalogue capabilities and compiled actions; it contains no arbitrary command, script, URL, executable path, registry path, or sibling identifier.

### Plan and approval

A **plan** is the deterministic ordered set of proposed compiled actions, reasons, impacts, privileges, verification, rollback, network, and restart expectations. **Approval** is an attributable decision bound to the exact resolved plan/content digest. A material change invalidates approval and requires a new review.

### Exception

A policy-authorized, attributable decision to proceed despite a rule outcome. It records operator identity, reason, authorization, time, and visible report history. An exception cannot override a prohibited rule or erase the original evidence.

### Ordinary diagnostics

Allowlisted local operational events used to understand product state and failures. They are not the authoritative workshop journal, do not require recipient/operator identity, contain no raw provider/backend/installer/command output, and are redacted before persistence under `docs/privacy/logging-standard.md`.

### Workshop record, recipient guide, and sanitized support output

The **workshop record** is the restricted technical output and the only output class permitted to contain full internal device identity such as the full serial. The **recipient guide** is an independent plain-language projection with no workshop secrets or unnecessary identity. **Sanitized support output** is a third independent allowlisted projection whose exact fields/files are previewed and digest-bound before explicit export; it is never a copy of the workshop database or log directory.

### Redaction and omission

**Redaction** replaces a detected prohibited value with its exact fixed non-sensitive marker before persistence. **Omission** excludes the field entirely from a target schema. Neither makes arbitrary collection acceptable: unknown fields fail closed, secrets/personal content are not retained, and support output begins from an allowlist.

### Pseudonymous internal identifier

A fresh opaque random job, action, correlation, or support identifier that does not encode or derive from a person, account, device serial, hostname, network identifier, or other stable external value. It remains controlled operational/workshop metadata; hashing an external identifier does not automatically create a safe pseudonymous identifier.

### Project vacuum

The B1 development posture in which ThirdLife Setup Core has no source, binary, runtime, test, data, schema, service, branch, or release-schedule dependency on a sibling portfolio project.

### Controlled pilot

The M0-through-M6 v0.1 deployment authorized only by human gate `TL-0611`. It is bounded partner evidence, not the standalone stable release and not completion of Team B/B1.

### Standalone stable release

ThirdLife Setup Core 1.0 after the complete M7 evidence package and human gate `TL-0710`. It remains independently useful and does not authorize B4 adapter or suite work.

### Release interface

The human-readable black-box facts in `RELEASE_INTERFACE.md` for a verified preview or frozen release: identity, lifecycle, launch, data, inputs/outputs, offline/network/resource behavior, privilege, support, samples, and limitations. It is not a shared runtime API.
