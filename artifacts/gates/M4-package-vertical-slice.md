# M4 gate — Package installation vertical slice

| Field | Value |
| --- | --- |
| Gate task | `TL-0411` |
| Verification date | 2026-11-26 (Europe/Berlin) |
| Integrated product candidate | `a637198615ca674a8ca8a2fcde0e62edd4ca3d7f` ([PR #64](https://github.com/PikkuJanne/ThirdLife/pull/64)) |
| Evidence branch | `codex/tl-0411-m4-gate` |
| Record state | Technical verification passed. Product and security disposition pending. |
| Runtime authority | Active Codex machine, Windows 11 Pro 26H2 build 26300.9974, native x64 |
| Decision packet | [Proposed internal M4 scope](../evidence/TL-0411/first-write-decision.md), SHA-256 `b6be6d71fe7afb90017500aa22106d7d9ef2ab164b628492f3097ae9317c7c3f` |

## Observed application outcome

In TL-0410 the ordinary unelevated app installed the reviewed Notepad++ 8.9.9.1
vendor MSI at machine scope in the dedicated Windows 11 Hyper-V guest on the
active machine. The app recorded durable intent, backend apply and independent
verification of registration and the isolated launch probe. A separate MSI
query confirmed exact registration. After an actual warm restart, the same
job reopened with a matching journal and byte-identical workshop report,
and a fresh MSI query confirmed registration. The journal had one
approval and one dispatch. Handover remained disabled.
[Native success and restart](../e2e/M4-first-write-slice.md#26-november-2026-supervised-guest-installation) ·
[Same-job continuity](../e2e/M4-first-write-slice.md#26-november-2026-post-restart-verified-job)

Separate native cases exercised UAC decline and interruption of the exact
app-owned installer client. The declined case reopened in review, with
the package absent. After client interruption the app retained failure and
withheld handover. The independent query found the package registered while
the installer service continued independently. Each case retained one approval
and dispatch and a matching report. The final checkpoint restore was
followed by guest re-admission and a residue check covering package registration,
the job store, reports, processes and runner state.
[Native decline](../e2e/M4-first-write-slice.md#26-november-2026-supervised-native-uac-decline) ·
[Native client interruption](../e2e/M4-first-write-slice.md#26-november-2026-supervised-native-installer-client-interruption) ·
[Final guest disposition](../e2e/M4-first-write-slice.md#26-november-2026-final-guest-disposition)

The successful guest run and the later cases used the Release apphost and UI
assembly built from `20247fbd3c785bf6b85e7e7c8f98ac6aa22a46e7`, an ancestor
of the gate candidate. The apphost SHA-256 is
`143289c265e36180be76d605366f59c848081a498daa30d54dad85bde5ca5763` and the
UI assembly SHA-256 is
`f74ee32bb94746fde7e9b2777aca91b11a6f2c6cefdcb6098684b85c40edf476`.
The two source trees differ only in the manifest, task and status records,
evidence, the failure-injection guide, and the two guest harness scripts.
The native evidence retains its per-run driver hashes, exact scopes,
operator choices and environments. TL-0412's earlier physical-host transaction
retains its own artifact and authorization provenance.
[Reviewed guest digests](../e2e/M4-first-write-slice.md#26-november-2026-live-guest-preview) ·
[TL-0412 host evidence](../evidence/TL-0412/report.md)

## Acceptance review

| Criterion | Finding and evidence |
| --- | --- |
| Structured package route | The adapter uses typed Windows Installer registration calls and maps numeric native outcomes. The apply path invokes the absolute Windows system installer with fixed arguments. [Runtime](../../src/ThirdLife.Packages/WindowsInstallerPackageRuntime.cs) · [ADR 0009](../../docs/adr/0009-winget-backend.md) |
| Exact approved identity and source at dispatch | `PilotPackageService` binds source, ID, version, publisher, machine scope, x64 and SHA-256, reacquires the approved artifact and rechecks installed state. The WPF boundary checks the current durable approval and journal immediately before dispatch. Changed metadata and elapsed or withdrawn approval block dispatch in tests. [Adapter](../../src/ThirdLife.Packages/PilotPackageService.cs) · [WPF boundary](../../src/ThirdLife.UI/Features/PlanReview/AuditWorkspace.PackageSetup.cs) |
| Artifact trust and closed inputs | The pinned HTTPS source allows one constrained HTTPS redirect. Protected staging streams to an exact size ceiling and requires the fixed SHA-256 and Windows signature trust while holding the file read-locked. The typed action exposes no installer arguments, alternate artifact path, URL or trust bypass. Adapter fixtures rejected material tuple changes and an injected trust failure before dispatch. The actual Windows signature and digest path is supported by code review and the pinned native successes. |
| Independent verification | Numeric installer success yields applied state. Only independent exact installed registration and the scoped launch probe can mark verified. FI-008 excluded success for missing and wrong registration, and the supported WPF suite covered unavailable registration. [Verification evidence](../evidence/TL-0406/report.md) |
| Failure and recovery | The named fixtures covered transfer, modeled decline, UI loss, owned backend loss and boot checkpoint commit. The native cases above supply actual decline, client loss and warm restart. The saved job retains typed failure and blocks handover when proof is insufficient. [Failure register](../../docs/testing/failure-injection.md) |
| Idempotency and retry | The Full suite covered the verified-plan repeat with one installer call, fresh registration re-observation, a durable three-start retry cap, linked history, and renewed approval after expiry or changed resolution. Eligible retries require proven pre-dispatch failure. [TL-0409](../evidence/TL-0409/report.md) |
| Diagnostic privacy | The Extended package diagnostic test passed against a malformed oversized backend error. The persisted journal, protected event, UI history, workshop report and support projection excluded raw detail. The support projection also omitted workflow correlation. Runtime case reports and receipts remain restricted locally. [TL-0407](../evidence/TL-0407/report.md) · [Privacy contract](../../docs/privacy/privacy-model.md) |
| Accountable disposition | Pending. The packet binds the sole write route, approved-input validity, retained controls and residual risks for one product and security disposition. [Decision packet](../evidence/TL-0411/first-write-decision.md) |

## Active-machine verification

Commands ran in a separate clean checkout at the integrated product candidate
on the active machine, unelevated, with .NET SDK 10.0.400, Python 3.14.7 and
the checked-in pinned PyYAML 6.0.3. The [reference machine profile](../../docs/testing/reference-machine-profile.md)
provides the hardware context. The OS build above reflects this run.

| Check | Result |
| --- | --- |
| Direct `tools/validate_bundle.py` baseline | Passed in 10.852 s. The validator used its custom structural checks. |
| `eng/verify.ps1 -Tier Full -Restore` | Passed in 492.755 s. 304 governance and 2,168 supported .NET tests passed with zero failures or skips. Bundle and repository validation, locked restore, formatting and the Release build passed. |
| `eng/run-m4-package-fixtures.ps1` | 10/10 named selections passed in 60.189 s, including four Extended selections. [Bounded runner output](../evidence/TL-0411/failure-matrix.json) |
| Focused package security checks | 44/44 passed in 6.628 s, with zero failures or skips. The scoped dispatch and trust source review agreed with the negative adapter tests. |
| Named Extended diagnostic privacy test | 1/1 passed in 3.250 s, with zero failures or skips. |

The M4 fixtures use the existing TL-0410 runner with its historical task
label. The output binds the current source and individual selectors and
durations. Its SHA-256 is
`16a4be247ea1bba6f709401d8458f0a9f99b2ecc6561d83498b235af1212f8df`.

The focused security and Extended privacy commands used the same build.

~~~powershell
dotnet test tests/ThirdLife.Packages.Tests/ThirdLife.Packages.Tests.csproj -c Release --no-build --no-restore --nologo `
  --filter '(FullyQualifiedName~PilotPackageServiceTests|FullyQualifiedName~WindowsInstallerPackageRuntimeTests.InstallerStartErrorUsesKnownNativeCode)&Category!=ActiveMachine&Category!=Extended&Category!=SandboxOnly' `
  -- 'RunConfiguration.TreatNoTestsAsError=true'
dotnet test tests/ThirdLife.UI.Tests/ThirdLife.UI.Tests.csproj -c Release --no-build --no-restore --nologo `
  --filter 'FullyQualifiedName=ThirdLife.UI.Tests.PackageSetupTests.PackageDiagnosticsDiscardOversizedMalformedBackendDetails' `
  -- 'RunConfiguration.TreatNoTestsAsError=true'
~~~

| Clean-candidate build input or output | SHA-256 |
| --- | --- |
| `ThirdLife.UI.exe` | `553aa65eb6569df3e124d8f6265e54bd2f5dd1e64c736e2cb1ac522ed8296bf1` |
| `ThirdLife.UI.dll` | `adb5f5fd4d7653253c3c1ca67fccf7351741e6cc8d8ad5b53df32abcc297d75b` |
| Embedded package catalogue | `bf8db490a32f3a71ba6ce35c0a24ef91700d0f97536281fd1d2f69c395c90c89` |
| Embedded package policy | `a31ca86348602d6d378f96502627525c9a3f344f4be6f02afcc9bdb286fedb59` |
| Failure-fixture runner | `31df9b7deed496708c9f4606afe353ecff070b7f5dc266e7a63399bd1865e7ee` |

The product checkout remained Git-clean through these runs.

## Coverage and reopening conditions

- The actual installer completed with status 0. Warm-restart continuity
  and a fresh registration query are observed. The restart-required
  exit and durable boot checkpoint are modeled. Observe native 3010 when
  the exact qualified installer legitimately reports it under applicable
  authority, or after separately qualifying and authorizing another exact
  route. Development cold-start and final acceptance cold boot keep
  their later scope. [D-033](../../DECISIONS.md#d-033)
- The recipient draft is a synthetic review input. The product recipient
  guide remains at `TL-0605` and support archive delivery at `TL-0606`.
  Technical accessibility coverage includes the loaded WPF package control
  names, focus, live regions, cancellation and recovery, and the shared
  double-scale plan fixture. Native package cases used UI Automation on the
  guest desktop. The named full operator matrix at `TL-0608` covers the
  complete keyboard journey, Narrator and applicable display checks. Recipient
  accessibility follows `TL-0701`. [Accessibility matrix](../../docs/testing/accessibility-matrix.md) · [Recipient draft](../evidence/TL-0410/recipient-draft.md) ·
  [Accessibility requirements](../../ACCESSIBILITY.md)
- The download uses a fixed byte ceiling and disk headroom, and the
  checked runtime bounds waits, retries and concurrency. These checks carry
  no modest-hardware throughput or cross-hardware claims.
  [Resource requirements](../../LOW_SPEC.md)
- Complete the gate after the accountable product and security owners
  record the disposition against the exact verified packet. A hold or
  decline keeps the affected scope withheld. Action-specific approval and
  Windows consent continue to apply. Pilot and stable exposure retain
  `TL-0611` and `TL-0710`.
