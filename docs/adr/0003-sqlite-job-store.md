# ADR 0003 — SQLite job store and bounded attachments

## Status and authority

**Status:** Accepted architecture rationale from `TL-0009`.

**Task:** [`TL-0009`](../../TASKS.yaml)

The [current product contract](../product-contract.md) and applicable specifications govern implementation. See the linked task evidence for the verified scope.

## Decision IDs

- [D-011](../../DECISIONS.md) — Local-first data model
- [D-014](../../DECISIONS.md) — Identity and minimization
- [D-018](../../DECISIONS.md) — Facts, policy, and decisions are separate
- [D-028](../../DECISIONS.md) — Persistence
- [D-032](../../DECISIONS.md) — Action journal and completion
- [D-053](../../DECISIONS.md) — Portfolio data ownership

## Context

Jobs must survive interruption, restart, retry, migration, and later historical review without a project-controlled server. Structured state includes evidence, policy/profile/catalogue snapshots, decisions, approvals, journal transitions, verification, finalization, and report metadata. Some later provider contracts may need a bounded raw attachment, but arbitrary provider, installer, exception, or command output is untrusted sensitive input and cannot become a general persistence format.

A database transaction also cannot atomically include an external attachment write, package mutation, Windows restart, or exported file. Recovery therefore needs an explicit split-state model.

## Decision

ThirdLife Setup Core uses SQLite for structured local job state and restrictive per-job directories for only those attachments that a later reviewed typed contract explicitly permits.

- `ThirdLife.Persistence` implements inward-facing repository and migration contracts. `ThirdLife.Core` does not depend on SQLite or persistence implementation types.
- Schema changes use explicit, versioned migrations and transactions where practical. Incompatible change requires a protected backup/recovery plan, corrupt/older/newer/interrupted cases, and truthful rollback limitations.
- Evidence, policy/profile/catalogue snapshots, decisions, approvals, attempts, action transitions, verification, exceptions, and finalization history are append-oriented. Current views may be projected, but historical records are not rewritten to match current policy.
- The authoritative journal/state-transition service validates the prior state, actor/source, correlation, approval digest, and allowed transition before a durable write.
- Per-job paths use internal opaque IDs under registered ThirdLife-owned roots with restrictive permissions, type/count/size limits, atomic creation where supported, and reparse/junction/symlink defenses.
- Raw provider, backend, installer, command, and exception input has zero persistent retention by default. D-028's attachment/raw-report allowance is not a general raw-output store. A future exception must name a typed purpose, maximum size/count, `WORKSHOP_RESTRICTED` access, retention, cleanup, and exclusion from recipient/support output.
- Database, attachment, export, and machine mutations are reconciled after interruption. A successful database commit does not prove a file or machine change, and rolling back SQLite does not undo an external mutation.
- The elevated broker and its package/system backend have no database, job, attachment, or log handle. Structured authenticated results return to the unelevated journal boundary.
- The store contains only Core-owned data. It neither discovers nor retains sibling-private content, credentials, recovery keys, or personal content.

At ADR acceptance, exact paths, migrations, retention enforcement, and deletion behavior remained owned by later implementation and lifecycle tasks. The checkpoint below records the store and gate-decision subsets now implemented by `TL-0102` and `TL-0103`; retention enforcement and deletion remain later work. The approved logical classes and defaults in the [privacy model](../privacy/privacy-model.md) continue to constrain those tasks.

## TL-0102 and TL-0103 implementation checkpoint

As of 2026-08-28, `TL-0102` implements the first bounded structured-store slice and `TL-0103` adds the sanitization-gate decision slice:

- the public `IJobStore` port and typed job/evidence/checkpoint contracts are Core-owned and contain no SQLite or Windows implementation dependency;
- `SqliteJobStore` uses the registered `%LOCALAPPDATA%\ThirdLife\SetupCore\JobStore` root, with arbitrary roots restricted to internal tests;
- schema versions 1 through 3 persist jobs, typed observations, external sanitization evidence, human-test evidence, reversible archive state, append-only store checkpoints, and append-only sanitization-gate decisions bound to the same job's exact evidence ID and policy version;
- `TL-0103` selects sanitization evidence explicitly and uses database append order, not provider timestamps, to require the newest committed record; verified, replacement-storage, and no-donor-storage evidence allow assessment, while missing, unknown, failed, stale, or archived state blocks without a bypass;
- gate recording revalidates job/archive state, evidence ownership, evidence recency, policy version, outcome, and reason inside the same immediate transaction; retrying the same evidence returns the one existing decision, while later evidence makes the prior decision stale without rewriting it;
- each migration is embedded and transactionally recorded with its script digest and resulting schema digest; application identity, ledger, schema, integrity, foreign-key, version, and normalized-payload hashes are checked on reopen;
- first creation builds and validates a complete store under a restrictive random sibling name, removes only its verified journal, then publishes the database through a no-overwrite same-directory handle rename; concurrent creators adopt the complete winner;
- protected ACLs, held object identities, final-path and link-count validation, reparse/junction rejection, internal-ID-derived job directory names, deterministic orphan reconciliation, and bounded file/record counts fail closed; and
- the implementation uses Windows `winsqlite3.dll` through the pinned managed provider closure. The exact 28-component proposal was approved for candidate commit `d6807937f5eff712821c7927ce1953daaa5dfeb8` and matrix SHA-256 `e85f3002175dfadc860f0d2c92de0787f52364f491e50a030c936a5421395418`; those runtime packages remain development/test inputs with native SQLite redistribution, packaging, notices, and release admission withheld exactly as recorded in the supply-chain contract.

This checkpoint does not implement final job/handover completion, general attachments, deletion, retention enforcement, backup/export, incompatible-schema rollback, uninstall cleanup, final packaging, or release authorization. Archive is reversible and preserves evidence and gate-decision history. The narrow verified-journal startup interval documented in [`SECURITY.md`](../../SECURITY.md) remains an explicit same-user/local-administrator residual; a custom SQLite VFS is outside this task.

## TL-0308 action journal checkpoint

Schema v5 adds three tables for immutable plan and impact snapshots,
operator approvals, and correlated action transitions under the existing
protected job store. Migration v5 preserves existing rows and migration
bytes. A v4 binary rejects a v5 store and preserves its contents. The journal
adds no attachment directory or raw output retention.

The repository accepts a new plan from typed planning provenance and verifies
both retained digests on reopen. Approval and dispatch intent check the exact
plan, impact and current sanitization decision in a guarded transaction. An
identical retry returns `AlreadyRecorded`, including after an ambiguous
commit. Only `Recorded` from a new dispatch intent can advance a caller to
dispatch. A reopened Started or Applied record requires reconciliation by
the caller. An independent verification transition follows Applied.
Restart-pending records wait for boot evidence. An independent no-change
confirmation can resolve an interrupted synthetic no-op without dispatch.

## TL-0309 restart recovery

Schema v6 adds immutable restart checkpoints and an append-only disposition
table under the existing protected job store. A maximum of 128 checkpoints per
job bounds storage and discovery. A v5 binary rejects a v6 store. Prior
migration bytes and job records are preserved.

Only the exact latest restart-required Applied entry can open a checkpoint.
Its immutable fields include the job, plan and impact digests, approval, action,
entry, correlation, and UTC issue and expiry. A Windows CurrentUser DPAPI
authenticator binds a digest of those fields to the original guarded database
file. The proof carries no recipient identity, local account string, or plaintext
credential. A copied database retains its job evidence and requires manual
review for checkpoints whose proof no longer matches.

The operator reopens the job and chooses Resume for review or Abandon resume.
One immediate transaction checks the authenticated checkpoint and records the
choice once. Resume also checks the current sanitization gate and approval,
latest action entry, and issue/expiry window. Abandon leaves the action in its
existing state and remains available after expiry. A reopen can show the latest
durable disposition without returning an authenticator. Neither choice marks a
restart observed or an action verified. A principal that can roll the existing
protected database back in place can restore an old pending row, so a local
review checkpoint cannot serve as machine mutation authority.

## Alternatives considered

- **One JSON file per job:** rejected because durable transitions, indexed history, migrations, and concurrent recovery checks require more discipline than ad hoc whole-file replacement provides.
- **A project-controlled server database:** rejected because the product is local-first, offline-capable, and has no required account or service.
- **Persist every raw payload in SQLite:** rejected because arbitrary raw output is unbounded, privacy-sensitive, difficult to migrate, and unsafe to reuse in logs or reports.
- **Let the elevated broker update the job store:** rejected because it would broaden the privileged process's access and mix mutation authority with audit-history authority.

## Consequences

- SQLite provides transactional structured state and practical local replay without a server dependency.
- Migrations, access control, corruption handling, backup/recovery, retention, bounded growth, and cleanup become release obligations.
- Repository abstractions and explicit reconciliation add implementation work, but prevent database success from being confused with filesystem or machine success.
- Attachment support remains narrow and opt-in; providers must normalize to typed evidence instead of persisting troubleshooting text by default.
- Uninstall and explicit job-data deletion must later distinguish ThirdLife-owned data from external exports and unrelated application data.

## References

- [Security data-flow stores and transitions](../security/data-flow.md)
- [Approved privacy model](../privacy/privacy-model.md)
- [Logging and diagnostic export standard](../privacy/logging-standard.md)
- [Modest-hardware bounded-growth rules](../../LOW_SPEC.md)
