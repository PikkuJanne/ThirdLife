# Read-only assessment orchestration — TL-0112

## Entry and authorization

`AssessmentOrchestrator` accepts an existing `IJobStore`, a fixed compiled provider roster,
a clock and a concurrency limit. It never loads plugins, commands, network destinations or
provider types from metadata. The roster contains 1–16 providers; concurrency defaults to
one and is configurable up to four. All providers must declare standard-user privilege and
no network use. The existing reviewed providers own their native read-only boundaries.
This declaration is not an operating-system sandbox for arbitrary third-party code.

Registration rejects duplicate provider identities and every collision among value and
failure keys, including matching declarations. The `assessment.` keys and
`assessment-orchestrator` identity are reserved. Related source facts retain distinct keys.
The recorded ownership rule is `exclusive_evidence_owner_v1`; there is no first/last-wins
merge, fallback to a preferred provider, or implicit freshness preference.

Assessment starts only after the current job has an allowing persisted sanitization
decision. A guarded start record binds the run to that exact decision. The store rechecks
archive state, newest committed sanitization evidence, policy and allowing decision inside
the same immediate transaction as every assessment batch. Missing, stale, blocked or
archived state rejects the write.

A small read transaction rechecks that decision before each provider dispatch. The SQLite
implementation reads the current gate records without reloading observation history. A
gate can still change after dispatch; already-started read-only collection may finish, but
its guarded commit is refused. No database transaction spans native provider work.

## Collection, cancellation and cleanup

The orchestrator reuses `InventoryProviderRunner` for normalization, timeout, provenance and
sanitized failures. It captures the underlying provider task separately from the runner's
caller-facing timeout. A synchronous provider entry point runs on the bounded worker too.
An outstanding native operation keeps admission after timeout or cancellation. Further
dispatch stops when a returned result still has pending cleanup, and another assessment
on that orchestrator remains busy until all captured operations settle.

An explicit cleanup failure permanently blocks reuse of that orchestrator, including a
failure arriving after the caller stopped waiting. Late values never replace committed
timeout/cancellation evidence. A cleanup failure learned while a commit or gate read is
pending is checked before dispatch resumes. Recovery requires investigating cleanup and
creating a fresh process, consistent with the underlying provider contracts.

Cancellation before a durable start performs no assessment. After start, cancellation stops
new dispatch, requests cancellation from active providers and records their truthful
cancelled/unknown results where the gate still permits it. Each result commit finishes as
one bounded transaction; cancellation does not expose a partial batch. A final cancelled
record distinguishes this case from an interrupted run with no final record.

An unavailable or failing provider does not erase another provider's successful evidence.
`Completed` means every requested provider collection completed; it is not a policy pass,
functional-test result, health score or handover authorization. `RequiresReview` retains
failures and unrun providers. `Incomplete` means no final record exists. Provider entries
with no committed attempt have a null outcome and no observations.

## Durable records and recovery

The existing SQLite observation store and schema version 3 are reused. There is no migration
or alternate database. Three closed, versioned, system-generated observation types provide
explicit correlation:

- `assessment.start`: generated run ID, job ID, gate decision ID, canonical requested roster
  and ownership-rule revision;
- `assessment.provider`: one provider outcome and exact observation IDs, atomically appended
  with those observations and the existing store checkpoint;
- `assessment.finish`: the final collection outcome.

Records use `WORKSHOP_RESTRICTED`, a generated source reference and bounded scalar JSON with
closed fields and enum values. They contain no commands, paths, device identities, raw
exceptions or raw provider payloads. Markers are internal bookkeeping and are excluded from
the snapshot's device observations. Existing log, recipient and support allowlists are
unchanged.

Reconstruction validates the historical allowing gate, marker schema, append order, unique
references, provider attribution and whole-batch membership. It does not infer membership
from timestamps. Duplicate outcomes, reused/foreign references, conflicting key ownership,
invalid final states and oversized input fail closed. A historical gate need not still be
current for its earlier snapshot to remain readable.

A process interruption leaves each batch wholly absent or committed. Reopening by assessment
ID shows the exact committed providers and explicitly unrun roster entries. A fresh rerun
creates a new assessment ID and new evidence IDs; it never rewrites or silently fills an old
snapshot. If a write throws after commit, the orchestrator rereads and compares every intended
ID and immutable payload. An exact complete match acknowledges the committed write. An
absent, partial or mismatched batch stops the run with a sanitized recovery error; no blind
write retry or destructive repair occurs.

## Lifecycle and historical meaning

Snapshots retain the exact committed `os.lifecycle.*` and `os.support_target` observations,
including source, revision, as-of date, fresh-through date, evaluation date and unavailable
states. Loading history does not consult `WindowsLifecycleCatalog.Current`. The existing
versioned catalog remains responsible for its reviewed freshness window and for rejecting
stale, contradictory, future, missing or unknown support knowledge.

There is no current-value view assembled from several assessments. Consumers select an
explicit snapshot. A new failed or unknown attempt cannot inherit an older supported value.
Failed OS collection retains its recorded unknown lifecycle observations; an unrun OS
provider has a null outcome and no lifecycle observations.

## Bounds and user-facing scope

At most 16 provider operations and four simultaneous workers exist per assessment instance.
Each provider contributes at most 64 normalized observations plus one marker; start and
finish add two records, for at most 1,042 new records per run. Marker text is capped at
4,096 characters. Existing 256-record batches, 10,000 evidence records per job, 64 KiB
normalized payload and 256 MiB database/journal limits remain enforced. Capacity failure
preserves earlier batches; this task adds no retention deletion or automatic retry loop.

Only normalized evidence and bookkeeping are appended to the registered job store under
`%LOCALAPPDATA%\ThirdLife\SetupCore\JobStore`. There is no additional attachment, cache,
temporary directory, telemetry, elevation, restart, GPU requirement or permanent worker.
Underlying providers retain their documented native allocation and cancellation limitations.

No UI is added. Typed phase outcomes, explicit unrun/unknown states, cancellation and stable
recovery codes are available for the later accessible shell. No human keyboard, screen-reader
or peripheral walkthrough is claimed. Tests and bounded measurements cover the active Codex
machine and synthetic inputs; they do not certify other hardware or physical device function.
