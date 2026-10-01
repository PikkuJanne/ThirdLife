# TL-0112 — Read-only assessment orchestrator

**Date:** 2026-10-01  
**Branch:** `codex/tl-0112-assessment-orchestrator`  
**Base:** `bbdc15f403f6ca28e8ee57e9404c85988a8d5141`  
**Runtime authority:** active Codex machine only  
**Status:** implementation complete; transient Targeted passed; committed-source binding pending

## Continuity

TL-0111 PR #12 merged before this task began. Its final head `edf9572` is an ancestor of
the base with no unique unmerged commits. Local main fast-forwarded, the full dependency
chain was parsed as done, and a normal task branch started from updated main. The SSH
origin is unchanged; the existing process-scoped HTTPS bridge completed synchronization.
No prior branch was deleted, history rewritten, sibling inspected or next task started.
The unrelated untracked Word reference remains untouched.

## Acceptance mapping

| Criterion | Implementation and proof |
| --- | --- |
| Sanitization blocks assessment | Existing gate inspection plus exact-decision guarded start; narrow pre-dispatch recheck; atomic archive/latest-evidence/decision check inside every write transaction. Missing, stale, failed, unknown, wrong-job and archived cases reject. |
| Independent durable provider commits and rerun | One provider's observations and its closed correlation marker share the existing evidence transaction/checkpoint. A new run appends new IDs; historical records are unchanged. |
| Consistent partial cancellation | Stop dispatch, request provider cancellation, retain complete committed batches and distinguish final cancellation from interrupted/incomplete state. Exact IDs reconcile a write that committed before throwing. |
| Offline assessment | Fixed compiled roster permits only standard-user providers declaring no network use. Orchestration exposes no commands, destinations or network API. Synthetic tests exercise this path; existing provider boundaries remain separately tested. |
| Failure isolation | Existing runner sanitizes exceptions and produces affected unknown facts; independent providers retain their evidence. No policy pass, health score or ready state is created. |
| Deterministic ownership/conflict/freshness | Duplicate provider IDs and all value/failure key collisions reject before execution. All 24 permutations of a four-provider fixture retain identical values, source ownership, conflict/stale/unknown limitations and canonical order. No old-value fallback. |
| Historical lifecycle revision and as-of date | Exact committed lifecycle observations remain in each snapshot. Reload never consults current catalog data; later reruns cannot rewrite earlier source/revision/date/status references. Existing catalog tests own stale/future/contradictory/unrecognized date semantics. |
| Timeout, process interruption and rerun | Manual-clock and barrier tests exercise caller timeout and late cleanup admission. New guarded-batch child-process tests reuse the existing owned-child termination fixture at first insert/before commit/after commit; all three pass in Sandbox; host policy blocked the rebuilt assembly before test execution. |

## Verification and defect handling

Exact commands, durations and failed attempts are in `runs.json`. The selected baseline
passed 95 provider-contract/lifecycle tests in 30.797 seconds. Initial guard tests passed
four Core and 21 Persistence cases. Final focused assessment tests passed 77 cases in
16.526 seconds including build, with no failures or skips.

Host testing after the narrow gate-query change compiled successfully, then all 24 selected
Persistence cases failed before their bodies because Application Control blocked
`ThirdLife.Persistence.dll` with `0x800711C7`. One assembly-identity test reproduced the
same loader failure. The full host tier was not blindly repeated. The existing governed
same-machine Sandbox has closed TL-0112 Quick/Targeted profiles selecting Inventory, Core,
Persistence and UI. The transient run passed all selected tests: Core 83, Inventory 901,
Persistence 113 and UI 3, with zero skips. Shared Targeted (including Quick) took 186.705
seconds; the full guest run took 248.509 seconds. Security controls are unchanged.

Independent review identified late-cleanup races at worker refill and admission release.
The coordinator now checks completed cleanup results before dispatch, after awaited gate
reads, and after all captured operations settle. Deterministic held-commit/held-gate tests
prove another provider cannot start after that failure is learned. Late values remain
withheld. Strict journal tests reject malformed foreign-run references before they can
inflate reference tracking. Final design review found no further required defect.

Compilation initially caught CA1870 character searches and CA1861 test arrays. Cached
search sets and readonly arrays resolved them without disabling analyzers. Harness tests
passed 34 cases; a companion validator invocation initially used system Python without
PyYAML. Selecting the governed virtual environment passed all 37 validator-module tests.
The full Python governance suite passed all 268 tests in 317.894 seconds because runner
controls changed.

Full is not triggered: this additive coordinator composes the unchanged provider runner and
SQLite engine; there is no migration, external dependency/tool/engine replacement, major
refactor, release gate or explicit task trigger. Extended is not triggered: bounded worker,
timeout, permutation and independently selected process-interruption cases fit Targeted;
no endurance, hardware exercise or broad host constraint was introduced.

## Resources and data

`resource-metrics.json` records an actual host run of a hashed synthetic workload: 16
providers with 63 boolean facts each, at concurrency one and four. Both retained 1,008
normalized observations and 1,026 stored observations including markers; normalized JSON
was 483,841 bytes. Collection measured 72.961 / 12.009 ms, process CPU 125 / 15.625 ms,
allocations 4,914,128 / 4,369,416 bytes, and working set 85,479,424 / 87,347,200 bytes.
These include test-host/JIT activity; they are not comparative performance budgets.

The measurement uses an in-memory fixture store, so physical database growth is unmeasured.
The coordinator creates no separate filesystem, temporary-file or cache output. Production
uses the existing protected SQLite store, payload/record/file ceilings and atomic evidence
transactions. At most 16 providers, four workers and 1,042 appended records exist per run;
no second run is admitted while native cleanup remains outstanding. Missing capacity stops
without deleting earlier evidence. Input/workload hashes are in `inputs.json`.

## Boundaries and handoff

- Security/privacy: closed system-generated workshop-only markers, sanitized errors,
  exact gate binding and no new execution/network/export surface.
- Accessibility: no new UI; typed cancellation, unknown, unrun and recovery states support
  the later shell. No human accessibility walkthrough is claimed.
- Modest hardware: default one worker, maximum four; bounded source/output/history parsing,
  narrow gate reads and no GPU or permanent work. Native cancellation limitations remain.
- Data: new normalized observations and bookkeeping in the registered local job store;
  schema remains version 3, with no migration, new location, raw attachment or retention deletion.
- Project boundary/release interface: no sibling or release-interface change.

Implementation contract: `docs/inventory/assessment-orchestration.md`. The implementation
checkpoint will receive a second, committed-source Sandbox binding before completion.
Normal PR publication remains pending. Runtime source is unchanged after the passing
transient run; only documentation and evidence were refined for this checkpoint.
