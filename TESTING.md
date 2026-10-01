# ThirdLife Setup Core — Testing Strategy and Execution Contract

**Bundle version:** 0.4.0  
**Portfolio baseline:** ThirdLife Software Portfolio v2.1  
**Authoritative runtime hardware:** active Codex machine only  
**Remote CI:** supplementary

## 1. Principles

ThirdLife needs fast feedback and complete risk-relevant evidence.

- Run the smallest check capable of disproving the change.
- Reduce each defect to a focused deterministic regression.
- Escalate by risk and gate, not by habit.
- Keep slow scenarios independently invokable.
- Record what did not run and why.
- Do not convert one-machine, VM, fixture, or constraint evidence into broad hardware claims.

The machine-readable tier/budget selection lives in `eng/test-manifest.json`.

## 2. Quick tier

**Default target:** 120 seconds on the active Codex machine.

Run:

```powershell
.\eng\verify.ps1 -Tier Quick
```

Quick includes:

- pinned toolchain check;
- direct bundle and repository validation;
- formatting verification;
- warnings-as-errors Release build;
- fast deterministic Core tests;
- fast deterministic Inventory tests excluding active-machine/extended scenarios;
- UI component and accessibility contract tests for the implemented WPF surface.

Quick is required after small fixes and before each checkpoint commit.

Quick does not include:

- all validator mutation/regression tests;
- broad Sandbox/VM matrices;
- destructive or host-mutating operations;
- active-machine capability observations;
- extended fault/resource scenarios;
- complete solution end-to-end testing.

A budget overrun is recorded and investigated. It is not hidden by removing meaningful code
checks.

In a clean checkout, add `-Restore`:

```powershell
.\eng\verify.ps1 -Tier Quick -Restore
```

## 3. Targeted tier

**Default target:** 10 minutes unless the task records a justified alternative.

Example:

```powershell
.\eng\verify.ps1 -Tier Targeted `
  -TestProject tests\ThirdLife.Inventory.Tests\ThirdLife.Inventory.Tests.csproj `
  -Filter "FullyQualifiedName~ProviderContractTests"
```

Targeted includes Quick plus the changed subsystem, nearby regressions, and risk-specific
integration/failure checks.

Pass several projects as a PowerShell array to `-TestProject`. A filter matching no tests
fails verification. Without an explicit filter, Targeted uses the deterministic default in
the manifest. Governance changes also run this independently invokable Targeted set:

```powershell
.\.venv\Scripts\python.exe -m unittest discover -s tools/tests -p "test_*.py"
```

Required before pushing a feature/fix and after changes to:

- data or migration;
- privilege/IPC;
- security/privacy;
- package/update;
- network/filesystem;
- provider/engine;
- installer/lifecycle;
- accessibility;
- report/export.

When direct host policy blocks a legitimate test host, use the governed active-machine
Sandbox/VM profile and record that limitation. Do not weaken host security.

## 4. Full tier

Run:

```powershell
.\eng\verify.ps1 -Tier Full -Restore
```

Full includes:

- complete Python governance regression tests;
- bundle and repository validation;
- locked dependency restore;
- formatting;
- warnings-as-errors Release build;
- complete supported .NET test suite.

The default Full filter excludes `ActiveMachine`, `Extended`, and `SandboxOnly` categories.
Those scenarios run separately when their task/risk requires them, in the declared
environment. For the current read-only inventory smoke on the unelevated active Windows
host, use Targeted with the Inventory project and `-Filter "Category=ActiveMachine"`.
Record each separate result; a default Full pass does not claim these scenarios passed.

Use the approved active-machine clean environment when host policy blocks direct execution.

Full is required:

- at milestone, preview, pilot, stable, and protected-merge gates;
- for cumulative default-branch integration;
- after major refactors;
- after migrations;
- after dependency/engine changes;
- when a task explicitly triggers it.

Do not rerun Full after every small edge-case fix. Run the focused regression and Targeted
set first.

## 5. Extended tier

Extended scenarios include:

- large/adversarial fixtures;
- endurance;
- repeated interruption/restart/resume;
- low-space/offline/interrupted-network/slow-destination;
- no-GPU/conservative-concurrency/low-priority;
- restore/integrity drills;
- broad hostile-input matrices.

Run only the named scenario required by the changed risk or explicit gate. Each scenario must
be independently invokable and produce a durable scenario-level result.

## 6. Test manifest and reusable runners

`eng/test-manifest.json` declares:

- tier budgets;
- fast test projects and filters;
- default authority;
- remote-CI classification;
- extended-scenario policy.

Future clean-environment execution should use reusable host/guest runners with declarative
profiles. Existing task-named scripts remain historical evidence or compatibility wrappers.

The v0.4.0 cumulative gate starts in a separate clean worktree on this physical machine with
the shared `eng/verify.ps1` runner and declarative test manifest. Bootstrap Python from the
pinned README procedure, then run Quick with `-Restore` and Full. If host policy blocks the
test assemblies, the existing Sandbox harness has one allowlisted Full profile for this
amendment:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng/run-tl0102-sandbox.ps1 `
  -Phase Full -EvidenceTaskId AMD-2026-09-12-REPOSITORY-STABILIZATION -PersistEvidence
```

The filename is retained for compatibility. The profile stages exact source/history and
hash-checked locked packages, restores the guest-private cache, then invokes the shared
`eng/verify.ps1 -Tier Full -Restore`. It keeps the checked-in NuGet configuration and enabled
Full audit, a 4 GiB disposable guest, read-only input mappings, bounded output, exact source
binding, and verified cleanup. Only this combined Full command has a 1,200-second timeout;
the host retains its 30-minute limit. Use `-PreflightOnly` without `-PersistEvidence` to verify
staging and cleanup first. No host or guest security policy is changed.

Other task-specific Sandbox phases remain bound to their historical source revisions.
Their old Quick restore order and success markers are incompatible with v0.4.0; do not use
those legacy phases for this gate or count their historical results as a new run. Future
profiles must reuse the shared verifier and retain focused invocation and safety checks.

TL-0107 adds a Quick-only profile for the final verification when host Application Control
blocks test-assembly loading. It reuses this harness and the shared verifier; its declarative
profile is recorded in `eng/test-manifest.json`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng/run-tl0102-sandbox.ps1 `
  -Phase Quick -EvidenceTaskId TL-0107 -PersistEvidence
```

Use `-PreflightOnly` without `-PersistEvidence` first. Persistent results require a committed,
unchanged tracked worktree. This profile stages the complete locked solution package closure,
then runs `eng/verify.ps1 -Tier Quick -Restore` with audit enabled. Guest networking is enabled
for the governed NuGet audit. The shared Quick command has a 180-second hard limit to allow a
cold 4 GiB guest restore/build; the normal Quick target remains 120 seconds. Bootstrap and
staging time are reported separately from the shared tier. Existing source/history/package
bindings, read-only inputs, bounded results, and verified cleanup remain enforced. ActiveMachine
tests are excluded by the unchanged Quick filters; host provider smoke evidence remains separate.
This does not establish direct-host policy compatibility or cross-hardware certification.

TL-0109 has separate allowlisted Quick and Targeted profiles for the same host Application
Control blocker. Targeted invokes the shared verifier with the Inventory and UI test projects,
including Quick and the unchanged deterministic filter, then requires the three exact methods
declared in the manifest: `ActiveMachineCancellationTerminatesTheOwnedJobAndVerifiesCleanup`,
`ActiveMachineProductionSourceHasBoundedSanitizedMetricsAndVerifiedCleanup`, and
`SandboxBatteryCollectionRecordsOnlyAvailabilityAndBoundedMetrics`. This additional command
has a 120-second limit and requires all three per-test pass markers plus the test-run success
marker. It verifies native process cancellation, workspace cleanup and the complete provider
through the inventory runner in the same-machine guest;
the production source may truthfully report no battery there. Both profiles stage the complete locked
solution closure and pinned Python/history inputs, restore with audit enabled, and retain the
same 4 GiB guest, read-only mappings, result/source binding, cleanup, and 30-minute host limit.
The shared commands are bounded to 180 seconds for Quick and 600 seconds for Targeted; the
normal tier targets remain 120 and 600 seconds. The exact selection, environment, risk trigger,
and budgets are declared in `eng/test-manifest.json`.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng/run-tl0102-sandbox.ps1 `
  -Phase Quick -EvidenceTaskId TL-0109 -PersistEvidence
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng/run-tl0102-sandbox.ps1 `
  -Phase Targeted -EvidenceTaskId TL-0109 -PersistEvidence
```

Run the chosen phase with `-PreflightOnly` and without `-PersistEvidence` first. Persistent
results require committed, unchanged tracked source. Quick continues to exclude ActiveMachine
tests. Targeted admits only the three declared checks beyond its deterministic selection;
the fixed-report test with its standard-user assertion remains host-only. Physical-host battery
observation and UI accessibility evidence are recorded separately. A guest pass
does not establish direct-host policy compatibility or cross-hardware certification.

A new large task-specific runner requires:

- technical reason;
- why the reusable runner cannot express the scenario;
- owner;
- retirement/replacement condition.

## 7. Required layers

| Layer | Responsibility |
|---|---|
| Unit/component | Domain invariants, parsers, validators, policy, redaction, persistence, migrations, journals, reports, failure semantics. |
| Provider/engine | Fixed structured invocation, provenance, cancellation, timeout, unavailable/denied behavior, cleanup, hostile input. |
| End to end | Complete primary journey, interruption, recovery, export, restart, repair, uninstall where applicable. |
| Accessibility | Keyboard, focus, names/roles/states, announcements, scaling, contrast, plain language, cancellation/recovery. |
| Modest hardware | Same-machine constraints, bounded memory/temp/cache/database, no-GPU, conservative concurrency, graceful degradation. |
| Security/privacy | Threat cases, privilege/IPC, malicious input, path/link attacks, secrets, update provenance, support sanitization, no telemetry. |
| User evidence | Representative operator/recipient/proxy walkthroughs, terminology, recovery, usefulness. |

## 8. Deterministic fixtures and provider facts

Fixtures are bounded, versioned, non-sensitive, attributable, reproducible, and independently
runnable.

A fixture proves application behavior for represented data. It does not prove that every real
provider or device behaves identically.

A normalized evidence key has one semantic owner. Related provider observations use distinct
keys/provenance or an explicit deterministic orchestrator reconciliation rule. Provider order
must not select the outcome.

Externally changing lifecycle data includes source, revision, and as-of date. Controlled
clocks test current, stale, absent, future/unknown, and contradictory revisions.

## 9. Defect workflow

1. Stop a broad run when enough evidence exists.
2. Create the smallest practical deterministic reproduction.
3. Add/update a focused regression.
4. Run it first.
5. Run the related Targeted set.
6. Run broader scope only when triggered.
7. Preserve failed and corrected evidence.
8. Update compact task evidence and live status.

Blind rerun is not success. Quarantine requires owner, task, reason, risk, and removal
condition.

## 10. Safety and isolation

Use temporary workspaces, controlled clocks/seeds, stubs, disposable databases/disks,
bounded timeouts, cancellation, and cleanup verification.

Do not:

- change firmware, TPM, Secure Boot, ownership/management, or activation to create a case;
- disable security controls for test convenience;
- use unknown media;
- place secrets/personal data in fixtures or logs;
- disrupt host storage/network/boot without an explicit task, checkpoint, recovery plan, and
  approval.

## 11. Manual hardware and accessibility evidence

Guided hardware tests remain a product feature. Development does not require every hardware
class.

- Deterministically test state, pause/resume, unavailable/failure, evidence, and cleanup.
- Walk through only capabilities available on the active machine when a named task requires it.
- Represent absent capabilities truthfully with fixtures.
- A workflow pass is not hardware certification.

Human accessibility evidence runs on the active machine at named tasks/gates and records
assistive technology, build, procedure, result, and limitations.

## 12. Remote CI

The workflow is a supplementary clean-checkout check. It cannot replace any required
active-machine runtime, hardware, accessibility, resource, gate, pilot, or release evidence.

Historical remote results remain valid as historical supplementary results; do not rewrite
history to relabel them.

## 13. Evidence record

A durable execution record contains:

- task and source commit/branch;
- tier and exact command/procedure;
- timestamps/duration;
- reference-machine profile;
- hosted environment/constraint;
- fixture/workload IDs and hashes;
- result;
- artifact path;
- cleanup/recovery;
- defect/limitation;
- skipped tiers and rationale.

Live `STATUS.md` summarizes the latest relevant records. `TASKS.yaml` references durable
evidence rather than embedding long reports.

## 14. Release claims

Release evidence includes the test manifest, clean-clone result, active reference-machine
profile, constraints/workload hashes, resource measurements, accessibility/security/privacy
results, defect dispositions, and explicit limitations.

Release wording may say **designed for modest hardware** and report observed results. It may
not claim unperformed cross-hardware certification or untested minimum specifications.
