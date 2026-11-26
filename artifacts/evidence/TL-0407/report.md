# TL-0407 — Package failure diagnostics

**Date** 2026-11-26  
**Base** `f07ca22bf73f5e21f614f33fedd45d3f8802eafd` (`origin/main`, PR #49)  
**Implementation and clean verification source** `589c837690430048b4d3d8557972f1899d16c539`  
**Environment** Active Codex machine, Windows 11 Pro client 26H2 build 26300.9974,
native x64, unelevated, .NET SDK 10.0.400 and Python 3.14.7.

## Application behavior

Package planning reports an unavailable or conflicting installation separately from
installer trust failure, source or staging failure, and download timeout. Applying
the exact approved plan distinguishes missing Windows authorization, declined consent,
download failure before dispatch, installer interruption or failure, and a restart
required by Windows Installer. Preparation timeout remains a saved review outcome.
Its guidance reflects that Windows Installer did not start. The existing connection
loss and cancellation paths still require a fresh plan review and approval before retry.
An out-of-band restart indication (MSI 1641) retains the prior interrupted-review handling.

Independent verification records missing, mismatched and unavailable registration
separately. It also distinguishes missing or untrusted executable, failed launch,
early exit, timeout, cancellation and unconfirmed cleanup. A failed verification
leaves the prior applied result and the verification review in the same correlated
attempt. The reopened job and workshop report use product-authored guidance for the
saved outcome. The journal appends result values and retains its legacy interpretations.

Package results reach the existing protected local logger through one registered event
contract with typed phase, result, category and retryability. The adapter discards
raw exception and backend detail before the first UI, journal, log or support write.
Logs keep the random dispatch correlation for execution and verification. The support
projection includes four to six previously allowlisted scalar fields and excludes
the correlation, package identity, job identity and local metadata. Log retention
uses the existing 14-day default with a configured 1 MiB byte ceiling.
The WPF command waits up to two seconds for the diagnostic write and admits one
outstanding write per workspace. It shows when diagnostic storage has not been
confirmed and publishes the durable package outcome. A diagnostic timeout cannot
authorize or retry an installation.

## Verification

| Check | Observed result |
| --- | --- |
| Baseline `PilotPackageServiceTests` and `PackageSetupTests`, at `f07ca22` with ActiveMachine and Extended excluded | Passed 38 package and 18 WPF tests in 34.148 s across serialized invocations. |
| `eng/verify.ps1 -Tier Targeted`, selecting Packages, Diagnostics, UI, Actions, Persistence and Reports test projects | Passed in 169.430 s. Quick and the selected deterministic projects passed 1,757 .NET tests and eight governance regressions. The Release build had zero warnings. |
| `eng/verify.ps1 -Tier Full -Restore`, separate clean worktree | Passed in 350.288 s. Locked restore, a Release build with zero warnings, 2,137 .NET tests and 304 governance regressions. |
| Extended `PackageDiagnosticsDiscardOversizedMalformedBackendDetails` | Passed 1/1 in 2.855 s. The WPF workflow injected a synthetic exception with identifying fragments, unpaired UTF-16, control characters and a 1 MiB tail. It captured only the typed event and checked the bytes produced by the local serializer and the exact support projection, SQLite history and opened workshop report. Installer calls stayed at zero. |
| Extended `InterruptedInstallerStreamRemovesPartialStagingAndReportsNetworkLoss` | Passed 1/1 in 1.794 s. A synthetic response interrupted an in-progress transfer. Owned partial staging was removed. |
| Extended `KilledPackageApplicationReopensTheSameJobForReviewWithoutDispatch` | Passed 1/1 in 6.423 s. The owned WPF fixture process was stopped during held apply. The same SQLite job reopened and offered read-only registration review without replaying dispatch. |

The diagnostics tests used the protected store at an isolated temporary root and
checked the stored bytes, rejection of a forged retry flag or injected free-form
field, and exact default support bytes. In the separate WPF fixture, a deliberately
held diagnostic write left the restart-pending journal readable and the app
closable. The first Targeted preparations exposed a missing pinned PyYAML install
in the new worktree and a stale checksum for the changed test register. The rerun
used the hash-locked Python dependency and the refreshed checksum.

The [failure injection register](../../../docs/testing/failure-injection.md#tl-0407-malformed-package-diagnostics)
gives the named adversarial selector. The committed synthetic WPF fixture is
`tests/ThirdLife.UI.Tests/PackageSetupTests.Diagnostics.cs` (CRLF checkout SHA-256
`04f3120825279bb72a1edecaffba9f11c9d77c68d4d70b43c64743f55918bed0`).

Clean Release artifact SHA-256 digests

- `ThirdLife.Diagnostics.dll` `5d77edbd0cb89bc5b066636c6ade6f3f02660f099f4436b996a4ca0d8a472691`
- `ThirdLife.Packages.dll` `46dacfc3a51216351a996457cfd6f69000973e3134a91375ff73f002f4abdbd5`
- `ThirdLife.UI.dll` `df223b082aef0a1f7e387896330c5c81d75e5dd4e62f68155a55553c8d10f7c0`

The protected logger was exercised in a temporary root and the WPF entry point
used an injected event sink. A new native install, UAC prompt, physical restart,
and perceptual assistive-technology output were unobserved. Prior live install
and launch observations retain their TL-0412 and TL-0406 authority. The support
projection is in memory. Archive creation, recipient preview and explicit export
remain TL-0606.
