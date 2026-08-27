# ADR 0003 — SQLite job store and bounded attachments

## Status and authority

**Status:** Accepted for `TL-0009`.

**Task:** [`TL-0009`](../../TASKS.yaml)

This ADR records existing binding decisions as planned architecture constraints. It does not amend [`DECISIONS.md`](../../DECISIONS.md) or [`PROJECT_BOUNDARY.md`](../../PROJECT_BOUNDARY.md), and it is not evidence that the planned behavior is implemented or verified.

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

At ADR acceptance, exact paths, migrations, retention enforcement, and deletion behavior remained owned by later implementation and lifecycle tasks. The checkpoint below records the subset now implemented by `TL-0102`; retention enforcement and deletion remain later work. The approved logical classes and defaults in the [privacy model](../privacy/privacy-model.md) continue to constrain those tasks.

## TL-0102 implementation checkpoint

As of 2026-08-27, `TL-0102` implements the first bounded structured-store slice:

- the public `IJobStore` port and typed job/evidence/checkpoint contracts are Core-owned and contain no SQLite or Windows implementation dependency;
- `SqliteJobStore` uses the registered `%LOCALAPPDATA%\ThirdLife\SetupCore\JobStore` root, with arbitrary roots restricted to internal tests;
- schema versions 1 and 2 persist jobs, typed observations, external sanitization evidence, human-test evidence, reversible archive state, and append-only store checkpoints;
- each migration is embedded and transactionally recorded with its script digest and resulting schema digest; application identity, ledger, schema, integrity, foreign-key, version, and normalized-payload hashes are checked on reopen;
- first creation builds and validates a complete store under a restrictive random sibling name, removes only its verified journal, then publishes the database through a no-overwrite same-directory handle rename; concurrent creators adopt the complete winner;
- protected ACLs, held object identities, final-path and link-count validation, reparse/junction rejection, internal-ID-derived job directory names, deterministic orphan reconciliation, and bounded file/record counts fail closed; and
- the implementation uses Windows `winsqlite3.dll` through the pinned managed provider closure. Those runtime packages remain development/test inputs with redistribution and release admission withheld until the fresh named review in the supply-chain contract is complete.

This checkpoint does not implement general attachments, deletion, retention enforcement, backup/export, incompatible-schema rollback, uninstall cleanup, final packaging, or release authorization. Archive is reversible and preserves evidence. The narrow verified-journal startup interval documented in [`SECURITY.md`](../../SECURITY.md) remains an explicit same-user/local-administrator residual; a custom SQLite VFS is outside this task.

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
