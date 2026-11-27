# TL-0703 operator configuration export

**Date** 2026-11-27  
**Source base** `53128354e2dbf626038b324bb869b0768425d114`  
**Environment** Active Codex Windows 11 host, with isolated test output folders.

The normal Prepare stage allowed a configuration selection before opening a
job. The preview showed the exact archive and its three fixed JSON members.
Choosing a test folder saved the preview's exact ZIP bytes under a protected
`ThirdLifeConfiguration` child. The chooser cancellation retained the preview.
An occupied destination name preserved its earlier bytes. A selection changed
while the picker was open prevented writing the stale archive.
[Operator workflow](../../../docs/profile-policy-export.md)

The reviewed publisher is the exact `ProtectedArtifactPublication.cs` blob
`df0c076802d2c249fd53c50e23f40c2dac491a94` from
TL-0606 source `ab82e160af787d9eb9c20fd374e2407aba1e1c46`. The existing
`DiagnosticStoreSecurity` dependency matched. The store pins the chosen local
directory, applies the shared restricted staging and atomic publication
routine, and reports an unconfirmed result when publication cannot be verified.
The chosen path and ZIP are outside the job record.

| Check | Result |
| --- | --- |
| `dotnet test tests/ThirdLife.UI.Tests/ThirdLife.UI.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~WorkshopConfigurationExportTests'` | 7 passed in 2 s of test execution. Exact deterministic ZIP contents for the three compiled configurations, mismatched configuration rejection, WPF preview and save, collision and picker handling, cancellation, obstructed folder and junction scope. |
| `dotnet test tests/ThirdLife.Reports.Tests/ThirdLife.Reports.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ConfigurationArchiveDestinationTests` | 10 passed in 121 ms of test execution. Typed local destination validation for relative, nonlocal, device, traversal and alternate stream strings. Selection opens no output. |

The verification covers the compiled configuration sets and a local archive
copy. Registration or reimport of user-supplied configuration, explicit
job-data deletion, release package inventory and actual uninstall verification
remain separate lifecycle work.

## Shared-writer composition

The configuration workflow was also verified with the support export and
workshop artifact detector on source checkpoint
`7af7721aba6135574486acc3286150309463ab66`. The exact shared publisher blob
was reused once. Targeted passed in 272.300 s on the active Windows 11 host.
All 1,418 Quick .NET tests and 29 selected Reports and WPF tests passed with
zero skips. The combined check includes configuration destination validation,
Prepare preview/save and support export on the same application build.

The integration candidate against reviewed main
`2c8fadd3a6122f53768ef088f97b3681e485a9d2` passed Quick in 202.675 s.
All 1,418 Quick .NET tests passed. The WPF assembly accounted for 118 s
of the measured run. Source changes since the Targeted checkpoint were
restricted to task and evidence metadata.
