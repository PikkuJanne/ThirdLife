# TL-0205 — Policy versioning and historical replay

A policy snapshot stores the accepted UTF-8 JSON bytes. Its SHA-256 is the
lowercase hexadecimal digest of those exact bytes, including whitespace and
line endings. Parsing, storage and historical reads verify the policy identity
and digest. Semantic version order compares numeric components without integer
overflow. A release sorts above a prerelease with the same numeric version.
The store rejects downgrade and different bytes for an existing version.

The v4 migration adds policy snapshot and decision tables within the existing
restricted SQLite job store. An append binds the exact policy snapshot, job,
assessment ID, timestamp, evidence IDs, capability selection and report
output in one transaction. Reevaluation requires the prior decision for that
assessment and the same saved evidence set. It appends a record with a
predecessor ID. A byte-identical retry of a committed record is idempotent.
Existing reports load their saved wording and exact policy without consulting
the current version. Historical records restored from JSON are read-only
results for purposes of a new append.

## Verification

Active machine `REF-CODEX-001` revision `2026-08-29.1`, Windows 11 x64,
.NET SDK 10.0.400, on 2026-11-24. Database tests used disposable synthetic
stores on this machine.

| Check | Result |
| --- | --- |
| Pre-edit Policy and migration tests | 144 Policy and 37 Persistence tests passed in 11.154 s. |
| Policy with `FullyQualifiedName~PolicyVersioningTests` | 29 focused cases passed in 3.207 s. The added capability replay case also passed in Targeted, bringing the new tests to 30. Coverage includes exact bytes and hash mismatch, version ordering, immutable decision capture, safe report replay and scope validation. |
| Persistence with `FullyQualifiedName~PolicyHistoryStoreTests` | 10 passed in 5.383 s. Includes the v3-to-v4 migration, crash-window convergence, reopen, version conflict, immutable evidence linkage, retry and corruption cases. |
| `eng/verify.ps1 -Tier Targeted` with the Policy and Persistence test projects | Passed in 87.852 s. Included Quick and 1,386 .NET tests with zero failures or skips. |
| `eng/verify.ps1 -Tier Full -Restore` in a clean detached worktree | Passed in 301.530 s on source `b33c1a65db3587ad00aa3436217af741b25692f7`. Passed 286 governance and 1,537 .NET tests with zero failures or skips. Locked restore, format and Release build succeeded. The migration and internal project reference triggered Full. |

The first Targeted run reached an older oversized-ledger fixture that
hardcoded schema version 4 as an invalid extra migration. The fixture now
inserts one above the current version and retains the corruption and
no-mutation assertions. Its isolated rerun passed in 3.791 s.

Extended had no trigger. GitHub Actions is supplementary. The source and
completion evidence use [PR #22](https://github.com/PikkuJanne/ThirdLife/pull/22).

## Data and claim limits

The migration preserves previous jobs and evidence. Policy bytes remain
bounded to 65,536 bytes and the decision payload to 2 MiB. The existing store
file guards, job evidence bounds, cancellation and migration transaction apply.
The decision history limits are 256 records per job and 1,024 policy snapshots
per store. The migration and test runs used no workshop database or alternate
physical machine.

The repository requires each referenced evidence ID to exist in the same job.
Assessment entry points supply the exact saved assessment and its projected
facts. Dual digests detect corruption and accidental mismatch inside the
existing local trust boundary. Captured workshop rule reports exclude raw
observations and private exception prose. The original candidate remains
pending adoption.
