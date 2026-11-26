# TL-0405 — Package progress and interruption recovery

The baseline passed 17 package service fixtures and seven WPF package setup
fixtures on `2cd1bb1c68122d7533ad2e111b0cfecdbe600fb9`. The package baseline
uses the same selectors as the prior [continuation evidence](../TL-0313/report.md).
The runs in this record used the active Windows 11 Pro 26H2 native x64 machine
(build 26300.9974), .NET SDK 10.0.400, and PowerShell 7.6.9. The process scenario
asserted that both the launcher and its WPF fixture were unelevated.

## Observed behavior

| Scenario | Saved job and application result |
| --- | --- |
| Progress | The adapter emitted bounded typed download and install stages with the action ID and attempt correlation. WPF presented them as polite live status with keyboard focus. The fixture observed the durable intent before the download-stage announcement and inspected the UI Automation peer. The install stage disabled ThirdLife's cancel command before the adapter entered apply. |
| Download cancellation | The operation stopped before installer dispatch. The journal kept the exact plan, approval and attempt and recorded a failed attempt with the pre-dispatch cancellation checkpoint. Reopen performed no install. Rebuilding repeated installed-state and source checks. A new approval and an explicit install command produced a separate correlated attempt that verified independently. |
| Download connection loss | The journal recorded a typed pre-dispatch network failure and retained the plan and approval. WPF and the reopened report provided retry guidance. Injected backend URL text did not appear in the persisted entries or operator output. Fresh checks, approval and an explicit install were required before the independently verified fixture retry. |
| Installer timeout or cancellation | Timeout and installer-reported cancellation produced separate review outcomes. The wait deadline did not terminate the installer or report rollback. An active timeout retained the fixture artifact until the backend finished and a late result left the saved review state in place. Read-only registration review could not clear the retry block. |
| WPF termination | The owned WPF fixture process terminated during held apply after a separate process observed its durable intent. The reopened job showed that no final result had been saved. A read-only registration check made one inspection and left the exact plan, approval, intent and correlation unchanged. There was no reacquisition or repeat dispatch, and the fixture directory was removed. |
| Verified outcome | An exact machine-wide registration observation was necessary for the verified WPF and saved report outcome. A conflicting or unconfirmed observation did not verify the action. |

The bound on installer-result observation is ten minutes. Preparation uses a
two-minute cooperative deadline. HTTP and file operations take the cancellation
token, and a synchronous native trust call may return after the deadline. The
installer launch path revalidates the durable approval and exact request before
mutation. The action journal still uses independent verification and appended
transitions. Historical enum values, approvals and journal rows remain readable.

## Verification

| Scope and source | Command or selector | Result |
| --- | --- | --- |
| Baseline at `2cd1bb1` | `dotnet test tests/ThirdLife.Packages.Tests/ThirdLife.Packages.Tests.csproj -c Release --filter "FullyQualifiedName~PilotPackageServiceTests&Category!=ActiveMachine"`, then `dotnet test tests/ThirdLife.UI.Tests/ThirdLife.UI.Tests.csproj -c Release --filter "FullyQualifiedName~PackageSetupTests&Category!=ActiveMachine"` | 24 passed in 34.703 s combined wall time. |
| Targeted at `a9315be` | `eng/verify.ps1 -Tier Targeted -TestProject` with Actions, Packages, Persistence, Reports and UI test projects | 8 work-contract and 1,560 .NET tests passed in 145.116 s. Includes Quick with 1,201 .NET tests and a warning-free Release build. |
| Extended at `a9315be` | `dotnet test tests/ThirdLife.UI.Tests/ThirdLife.UI.Tests.csproj -c Release --no-build --no-restore` with the exact named filter in [TESTING.md](../../../TESTING.md) | 1 passed in 6.176 s wall time. |
| Clean Full at `a9315be` | `eng/verify.ps1 -Tier Full -Restore` in a detached clean worktree | 304 governance and 2,067 .NET tests passed in 368.793 s. Locked restore, formatting and a warning-free Release build passed. |
| Clean Extended at `a9315be` | Same named filter on the clean Full build | 1 passed in 5.843 s wall time. The build and test worktree remained clean. |
| Completion record | `eng/verify.ps1 -Tier Quick` with the TL-0405 task set to done | 8 work-contract and 1,201 .NET tests passed in 102.792 s. |

The clean source was `a9315be2e53df316e1104ef1544d581621d96720`. The Release
`ThirdLife.UI.dll` SHA-256 was
`3d11a667f97699a916e2c363a37088f1e16767d7ef02854bbd52b5e4ebef11be`.
The `ThirdLife.UI.Tests.dll` SHA-256 was
`f2c79c0d5f51994a6dbfd767f9f9f333b5be07b2037a92d0de8294241eda6ad7`.
The implementation and evidence are in [PR #47](https://github.com/PikkuJanne/ThirdLife/pull/47).

The first WPF run found a fixture assertion that expected the word “cancelled”
where the UI said “Cancellation was recorded.” The assertion was corrected to
check the persisted cancellation and review guidance. The first Targeted
invocations identified stale governed file hashes and formatting differences in
the edited C# files. The bundle hashes were refreshed and the repository
formatter applied before the passing run.

## Interrupted transfer follow-up

On 2026-11-26, a clean rerun at `2cd1bb1` passed the 17 package and seven
WPF baseline fixtures in 28.695 s combined wall time. A clean rerun at merge
`05ba81d` passed Targeted in 179.948 s and the named WPF termination case
in 6.892 s. Both ran on the active native x64 Windows 11 machine described above.

Follow-up source `053758d542a09385f84fded5f79076ad7292f94a` is based on `05ba81d`.
The [FI-002 scenario](../../../docs/testing/failure-injection.md#tl-0405-interrupted-package-transfer)
uses the production copy and private Windows staging path with a synthetic HTTP
response. The fixture observed 131,072 staged bytes before the stream raised an
I/O exception. The runtime classified the failure as a network interruption,
omitted the raw exception detail, and removed the exact partial file and staging
directory. It made one fixture request. The payload was 128 KiB of zero bytes
(SHA-256 `fa43239bcee7b97ca62f007cc68487560a39e19f74f3dde7486db3f98df8e471`).
The saved-job retry check remains covered by the separate WPF network fixture.

| Scope | Command or selector | Result |
| --- | --- | --- |
| Targeted | `eng/verify.ps1 -Tier Targeted -Restore -TestProject` with Actions, Packages, Persistence, Reports and UI | 8 work-contract and 1,560 .NET tests passed in 153.137 s. Formatting and warnings-as-errors Release build passed. |
| Extended transfer | `dotnet test tests/ThirdLife.Packages.Tests/ThirdLife.Packages.Tests.csproj -c Release --no-build --no-restore` with the exact FI-002 filter linked above | 1 passed in 1.774 s wall time. No staging residue. |
| Extended WPF termination | `dotnet test tests/ThirdLife.UI.Tests/ThirdLife.UI.Tests.csproj -c Release --no-build --no-restore` with the exact termination filter in [TESTING.md](../../../TESTING.md) | 1 passed in 6.247 s wall time. The same job reopened for read-only review without repeat dispatch. |

The Release `ThirdLife.Packages.dll` SHA-256 was
`80acad1703e83bf7821f77098caa77d7ac1e06123ea615c1fb3597c4fcaaa01e`.
The Release `ThirdLife.Packages.Tests.dll` SHA-256 was
`adf4f069eeec9d1e250610a1b75459afcc0b9f0772bc82af348c0df4275dd103`.
The first Targeted invocation on the follow-up stopped at documentation
validation because the matrix links used parent-directory paths. Repository
relative text references passed validation. The scoped follow-up reuses the
original clean Full evidence for the unchanged shared contracts.

## Evidence limits and continuation

The application interruption tests used a controlled package adapter, isolated SQLite and actual
WPF windows. This record covers technical UI Automation properties, binding
updates and focusability. It makes no perceptual screen-reader claim. The
fixtures made no live package download, Windows Installer, Windows consent, or
host network change. The existing live package transaction keeps its original
[TL-0412 evidence](../TL-0412/report.md).

The non-mutating registration check leaves uncertain states in review. Automatic
reconciliation of a process loss or reboot and verification after that recovery
remain in TL-0408. TL-0406 can proceed with independent package and application
launch verification.
