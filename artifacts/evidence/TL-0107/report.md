# TL-0107 â€” OS lifecycle and activation inventory

**Date:** 2026-09-30  
**Task state:** Done; normal pull-request integration is the next continuity step  
**Branch:** `codex/tl-0107-os-lifecycle-activation`  
**Base:** `74b6c8cbc7b4426eebf38139ed3374f66e3ff8be`  
**Environment:** active Codex machine only; standard-user Windows 11 x64  
**Runtime authority:** local; remote CI is supplementary

## Continuity and scope

The session read the operating contract and synchronized the configured repository.
Unattended SSH did not finish; the documented process-scoped HTTPS bridge fetched safely
without changing origin. Local stabilization head and upstream matched at `4cf8270`.
GitHub confirmed cumulative PR #7 merged at 13:45:22 UTC on 2026-09-30. Its complete head is
an ancestor of `origin/main`, with 0 unique commits and the merge commit one ahead. Local
main fast-forwarded, and this task branch started at the merge. All TL-0107 transitive
dependencies are done. No temporary stack, PR closure, branch deletion, or history rewrite
was used. Legacy disposition records remain intact.

The unrelated untracked Word roadmap reference was already documented by the prior handoff.
It was not opened, changed, staged, or committed. No sibling repository was accessed.

## Implemented behavior

- Read-only OS version/build, numeric edition and mapped label, and distinct `os.architecture`.
- Exact build/edition lifecycle mapping, separate from facts, with source, revision, as-of
  date, expiry, evaluation date, release, family, and reason retained in normalized evidence.
- Read-only Windows SLC activation status. Only unanimous known statuses are represented;
  no key, product ID, account identifier, raw licensing output, or legal ownership claim.
- Three bounded reboot indicators, each with source, confidence, collection status, and
  recovery. A positive indicator is retained; absent indicators never prove no pending reboot.
- One worker per provider instance. Cancellation and timeout return fixed failure codes,
  discard late evidence, retain the worker slot until native cleanup, and permit later retry.

No new package, schema, migration, database write, privilege, settings change, network use,
shell, cache, background service, or release interface is introduced. The providers remain
unwired to the future TL-0112 assessment orchestrator.

Design and primary-source references: `docs/inventory/os-activation-reboot.md` and
`docs/inventory/windows-lifecycle-data.md`. Security and resource notes are linked from the
binding documents. Existing privacy-owner-approved files and licence inventory are unchanged.

## Acceptance and verification coverage

| Task criterion / verification | Evidence |
| --- | --- |
| Windows 11 x64 versus Windows 10 audit-only | Exact build/edition fixture matrix; D-005 fixed target; synthetic Windows 10 equivalent |
| Read-only activation, no key installation/bypass | Four reviewed read/resource imports; all-provider native/source boundary tests; native fake verifies call and cleanup sequence |
| Unknown/unsupported lifecycle cannot pass | Current, end date, next day, 30-day boundary, stale, missing, future, unknown source/revision/build/edition/architecture, malformed and duplicate/conflicting data tests |
| Reboot source and confidence | Three fixed registry sources; per-source `indicator_only`; mixed failures, all-absent, malformed metadata, recovery tests |
| Activated/unactivated/provider failures | Status 0â€“3 fixtures; conflicting/missing/invalid/oversized rows; HRESULT/exception/cancellation/timeout/cleanup fixtures |
| Architecture ownership and order | Disjoint descriptors; conflicting OS/system values preserved in both orders; complete observation set with duplicate OS architecture rejected in both orders |
| Lifecycle attribution/history | Source/revision/as-of/fresh-through/evaluation date serialized; immutable catalog copies; earlier serialized observation unchanged after later stale evaluation |
| Licensing data excluded | Synthetic native-buffer and exception canaries; serialized evidence/failure scans; source excludes logging/diagnostic sinks and sensitive queries; native status-only projection |
| Missing reboot indicator does not prove absence | Each/all-absent and mixed-unavailable cases preserve aggregate unknown; no global false result exists |
| Captured-data tests | Only published OS facts replayed from `REF-CODEX-001`; replay evaluation date is explicit and not a historical support claim |
| Active Windows verification | Three independent unelevated, bounded smoke tests passed; Windows 10 uses synthetic equivalent |

## Test results

Exact commands and durations are in `runs.json`.

| Check | Result | Command wall time |
| --- | --- | ---: |
| Narrow pre-edit provider-contract baseline | 20/20 passed; no pre-existing runtime failures | 13.242 s |
| Activation focused tests | 42/42 passed | 5.207 s |
| OS/lifecycle/reboot/boundary focused set | 129/129 passed | 5.076 s |
| Strengthened duplicate-architecture regression | 2/2 passed, both orders with a complete observation set | 3.984 s |
| Activation active smoke | 1/1 passed | 2.222 s |
| OS active smoke | 1/1 passed | 1.795 s |
| Reboot active smoke | 1/1 passed, with truthful unavailable sources allowed | 1.726 s |
| Completion Quick | Passed; 79 Core + 222 Inventory, zero warnings/errors | 29.580 s (runner 29.324 s) |
| Shared Targeted tier, including Quick | Passed; 79 Core + 222 Inventory; selected Inventory 222 repeated by governed runner | 38.786 s (runner 38.401 s) |

Targeted included the pinned toolchain, bundle/DAG, repository/manifest/locks/licence checks,
format verification, and complete Release solution build with zero warnings/errors. Python
used the existing pinned local environment; optional `jsonschema` was absent, so the existing
custom validator ran as documented. No check or analyzer was disabled.

Full was not triggered: this task has no named trigger and changes no dependency/engine,
migration, major architecture, milestone/release/protected-merge gate. Cumulative integration
Full belongs to the already merged PR #7. Extended was not triggered; bounded adversarial,
failure, timeout, and recovery cases ran in Targeted. No clean-clone gate was required for
this ordinary provider task. No remote runtime evidence was used.

## Defect handling and review

The first compile attempt found test-only xUnit overload/syntax and analyzer issues (2.810 s;
no tests executed). Typed arrays, explicit intentional noncooperative waits, analyzer-correct
assertions, and disposal resolved them before focused execution. The original TL-0106 native
API test was scoped to its three owned files; a new all-provider import and source test
retains the broader no-write/no-shell/no-network/no-logging boundary across every Windows file.

The first new handoff validation rejected missing required authority wording and its PR URL
under the governed path scanner. Correcting the wording and recording the PR number made the
unchanged validator pass. No product runtime failure or test quarantine occurred.

Two independent read-only reviews found no actionable correctness or privacy defect.
The final diff review checks key/product/account leakage, native calls, arbitrary paths/URLs,
registry access rights, lifetime/cleanup, evidence ordering, unknown states, dependencies,
task-field scope, and hardware/release wording. The existing task contract is unchanged;
only TL-0107 execution state/evidence is updated.

## Reference machine and resource observation

Reference profile: `REF-CODEX-001`, revision `2026-08-29.1`. This session reconfirmed
Windows `10.0.26200.0`, x64 and .NET SDK `10.0.400`. Local shell is PowerShell `7.6.5`
(newer than the historical profile); the shared verifier uses Windows PowerShell. Python is
`3.14.7`. There was no physical/VM/storage/network/security constraint or hardware change.

One isolated OS-provider smoke observed 33.154 ms elapsed, 46.875 ms process CPU,
73,875,456 bytes test-host peak working set, 14 observations, and 7,034 bytes serialized
normalized output. There was one provider worker and no provider file write. Peak memory
belongs to the whole test host, not only the provider. This is a bounded observation, not a
benchmark budget or minimum-hardware certification. Activation's initial native allocation
belongs to Windows SLC; the 128-row limit bounds managed copying, not that allocation.

No UI was changed. Fixed status/classification/recovery semantics preserve information for
later accessible presentation. No keyboard/screen-reader/scaling/contrast walkthrough or
human accessibility approval is claimed. Tests exercise cancellation and recovery without
weakening those states for resource reasons.

## Durable inputs and limitations

`inputs.sha256` binds source/test workload files, the published capture, and source notes
using checked-out bytes under `.gitattributes`. Its SHA-256 is
`bdd24c5aedeba3e115a2cef7e65d4130b8275a58fc09181218444e29793c11d8`.
The task commit binds the report, structured results, manifest, code, and task state together.

- Native calls cannot be forcibly aborted; single-flight is per instance. The future
  orchestrator must reuse instances and bound batch concurrency.
- Lifecycle revision `thirdlife.windows-lifecycle.2026-09-30.v1` expires after 2026-10-30.
  Unlisted editions/builds, including the device-specific 26H1 mapping, remain unknown.
- Base build lifecycle does not prove patch currency, hardware eligibility, servicing channel,
  activation, legal ownership, or complete preparation readiness.
- Multiple Windows SKU statuses may legitimately differ; conflicting activation remains unknown.
- Reboot heuristics are incomplete. Rename payloads are never read; plausible metadata may
  conservatively indicate pending work even when an underlying list is empty or stale.
- Registry handle cleanup uses `SafeRegistryHandle` disposal; no injected native close-failure
  claim is made. Smoke success does not imply every source is accessible or every PC is covered.
- Windows 10 and hardware/provider variants are synthetic; no second machine was used.

**Next dependency-ready task:** TL-0108 â€” storage inventory and reliability evidence,
only after this task is integrated into updated main. It was not started.
