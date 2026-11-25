# ThirdLife Setup Core — Testing Strategy and Execution Contract

**Bundle version:** 0.8.0  
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

Qualify application runtime and packaged lifecycle on Windows 11 client on native x64.
Record the exact release, edition, build, OS architecture and tested artifact. Use
deterministic fixtures for platform refusal and historical-record compatibility.
The supplementary Windows Server runner covers build, governance and deterministic tests.

### Engineering ownership, evidence and reuse

Codex owns preparation, functional and regression tests, technical accessibility, diagnosis,
repair, cleanup and evidence. Exercise required behavior through real entry points and use
deterministic regressions to isolate defects. Finishing work creates zero routine human QA,
repair confirmation, retest or owner sign-off. Follow [D-074](DECISIONS.md#d-074) before an
exceptional request. Continue safe engineering with any applicable uncertainty recorded.

Keep objective results, actual observations, reported defects, unexercised behavior and
accountable decisions distinct. A known failure of a required workflow blocks completion.
A missing physical or perceptual observation limits claims. A later decision on a prepared
candidate does not block its preparation or verification. Authorizations required for an
action remain prerequisites to that action. A verified isolated checkpoint may integrate
while an affected capability remains unavailable. Verify that the unavailable action stays
unreachable and record the qualification limit. Test only the applicable route and risks.

Reuse earlier results when the applicable gate allows and relevant code, tests, runner,
profile, dependencies, environment, configuration and workload are unchanged. Retain the
original command, tested commit, artifact, environment, result and limitation. Record the
basis for reuse. Codex rechecks changed interactions, failed cases and new flows.
Documentation and evidence changes receive applicable governance validation. Contract
changes also receive their focused regressions and final complete governance regression.

Before a diagnostic repeat, record the hypothesis and the changed input or expected new
observation. After two materially identical attempts that add no information, change the
method or record the limitation and continue other work. Preserve the reported failure and
record evidence of its resolution or an explicit justified disposition.

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
the manifest. In one invocation Quick entries execute first, followed by distinct Targeted
selections. Paths are resolved against the repository root and compared using Windows path
semantics. Filter text is compared exactly. Only identical project and filter pairs in that
invocation are deduplicated. Separate environments, invocations and risk scenarios retain
their own evidence. Report executed results once.

During governance iteration, run the affected validator or test class first, then related
checks. Retain direct bundle and repository validation for governed files. Run the complete
governance regression once for the final relevant governance change:

```powershell
.\.venv\Scripts\python.exe -m unittest discover -s tools/tests -p "test_*.py"
```

This is a Targeted governance check. The .NET Full tier retains its separate triggers.
After further changes, select checks for the affected behavior and shared validation risk.

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
- after major shared-contract changes or refactors;
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

TL-0111 adds closed Quick and Targeted profiles to the same reusable runner for the host
Application Control loader failure. Targeted runs the shared verifier with Inventory and UI,
including Quick; it adds no native battery or host-mutation scenario. The full locked solution
package closure, pinned Python packages and reachable source history are staged and hashed.
The 4 GiB guest has read-only input mappings and networking only for governed NuGet audit.
Quick has a 180-second command limit; Targeted has 600 seconds; the host retains 30 minutes.
Normal tier targets remain 120/600 seconds. ActiveMachine tests stay outside these profiles.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng/run-tl0102-sandbox.ps1 `
  -Phase Targeted -EvidenceTaskId TL-0111 -PreflightOnly
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng/run-tl0102-sandbox.ps1 `
  -Phase Targeted -EvidenceTaskId TL-0111 -PersistEvidence
```

Use `-Phase Quick` for the independently selectable Quick profile. A transient run without
`-PersistEvidence` may verify the in-progress source overlay before a checkpoint; source and
tracked state must remain unchanged throughout it. Persistent results require a committed,
unchanged tracked worktree. The same binding, bounded-result and verified-cleanup guards
remain enforced. Direct-host policy compatibility and other physical hardware are unverified.

TL-0112 reuses the same bounded runner with its own Quick and Targeted profiles. Targeted
runs the shared verifier for Inventory, Core, Persistence and UI, including the owned-child
process interruption tests. It uses the unchanged deterministic filter and adds no native
hardware scenario. Quick/Targeted retain 180/600-second command limits, a 4 GiB guest,
locked dependency and source/history hashes, bounded output and verified cleanup. Prior
profiles are unchanged. Use `-EvidenceTaskId TL-0112 -Phase Targeted -PreflightOnly` first,
then omit `-PreflightOnly` for transient verification or use `-PersistEvidence` against
committed, tracked-clean source. Networking remains only for governed NuGet audit.

TL-0113 adds closed Quick and Targeted profiles for a host Application Control failure
loading the rebuilt UI test assembly. Targeted selects the UI test project and includes
the normal Quick projects. The unchanged deterministic filter excludes ActiveMachine,
Extended and SandboxOnly tests. The standard-user apphost smoke remains a separate host
check; the guest's component tests do not prove unelevated apphost startup or human
assistive-technology behavior. No extra native or host-mutation scenario is admitted.

Both profiles stage the full locked solution package closure, pinned Python packages and
reachable source history. They retain a 4 GiB guest, read-only input mappings, verified
source/package/result bindings, bounded output, cleanup and the 30-minute host limit.
Quick/Targeted command limits remain 180/600 seconds and the normal tier targets remain
120/600 seconds. Guest networking is enabled only for governed NuGet audit.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng/run-tl0102-sandbox.ps1 `
  -Phase Targeted -EvidenceTaskId TL-0113 -PreflightOnly
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng/run-tl0102-sandbox.ps1 `
  -Phase Targeted -EvidenceTaskId TL-0113 -PersistEvidence
```

Use `-Phase Quick` for the independently selectable Quick profile. A run without either
switch verifies a transient source overlay; source and tracked state must remain unchanged
throughout it. Persistent evidence requires committed, tracked-clean source. No host or
guest security policy is changed. Direct-host loader compatibility, human review and other
hardware remain separate evidence or explicit limitations.

A new large task-specific runner requires:

- technical reason;
- why the reusable runner cannot express the scenario;
- owner;
- retirement/replacement condition.

TL-0317 uses `eng/run-thirdlife-hyperv.ps1` and the dedicated profile
`eng/thirdlife-hyperv-profile.json`. It runs the protected broker scenarios on
the active machine's Windows 11 Hyper-V guest through PowerShell Direct and
the existing Basic console session. The named guest cases are independent of
the default Full filter. Follow the [runner procedure and case
criteria](artifacts/evidence/TL-0317/report.md) for the reviewed candidate,
guest admission, interactive consent observations and cleanup.

TL-0313 reuses the same guest runner for the durable application flow. Its
application cases use an isolated WPF and SQLite fixture, the protected installed
broker and the synthetic Basic profile's three essential no-ops. The preview case
exercises plan construction and reopening without an approval or dispatch. The interactive
cases require the operator to approve the exact displayed plan in WPF and
make the separate Windows consent choice in the Basic console. Follow the
[TL-0313 procedure](artifacts/evidence/TL-0313/report.md) for the candidate
binding, case selection and evidence limits.

The independent TL-0313 Extended scenario terminates an owned WPF process after
the journal commits dispatch intent, then opens the same SQLite job and exercises
recovery. Its held fixture backend makes no broker calls. Run the single scenario
on the unelevated active Windows 11 machine.

```powershell
dotnet test tests/ThirdLife.UI.Tests/ThirdLife.UI.Tests.csproj -c Release --no-restore `
  --filter "FullyQualifiedName~KilledNoOpApplicationReopensTheSameJobAndReconcilesWithoutDispatch"
```

TL-0116 adds an independently invokable, unelevated Windows desktop scenario. From the
verified Release checkout, run:

```powershell
pwsh -NoProfile -File eng/run-m1-audit-journey.ps1 -VerifyAccessibility
```

The driver opens the isolated WPF fixture, sends keyboard input to its owned window and
waits for saved intake, assessment, manual-result, reopen and report outcomes. The optional
probe observes real cross-process UI Automation events and proportional Win32 wheel input.
The runner emits bounded JSON with source and binary digests, outcomes, duration and cleanup.
The standard-user product shell smoke checks startup only. The reusable Sandbox verifier
cannot exercise the offered interactive desktop route or a standard-user token. Codex owns
this driver and will retire it when a shared externally driven WPF harness covers the same
ordinary-input, persistence, isolation and artifact assertions. Keep the in-process tests for
deterministic failure and recovery coverage. No Windows settings changes are needed.

## 7. Required layers

| Layer | Responsibility |
|---|---|
| Unit/component | Domain invariants, parsers, validators, policy, redaction, persistence, migrations, journals, reports, failure semantics. |
| Provider/engine | Fixed structured invocation, provenance, cancellation, timeout, unavailable/denied behavior, cleanup, hostile input. |
| End to end | Complete primary journey, interruption, recovery, export, restart, repair, uninstall where applicable. |
| Accessibility | Keyboard, focus, names/roles/states, announcements, scaling, contrast, plain language, cancellation/recovery. |
| Modest hardware | Same-machine constraints, bounded memory/temp/cache/database, no-GPU, conservative concurrency, graceful degradation. |
| Security/privacy | Threat cases, privilege/IPC, malicious input, path/link attacks, secrets, update provenance, support sanitization, no telemetry. |
| Observations and decisions | Preserve actual prior or exceptional observations under D-074. Record applicable accountable decisions and technical limits with their provenance. |

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
- Use relevant bounded active-machine observations and approved same-machine scenarios.
- Represent absent capabilities truthfully with fixtures.
- A workflow pass is not hardware certification.

Codex runs technical accessibility checks. Record any actual historical or exceptional
observation with assistive technology, build, procedure, result and limits. Missing
perceptual evidence limits the claim and creates no routine human QA assignment.

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
