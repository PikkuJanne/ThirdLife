# TL-0406 — independent package and launch verification

**Date** 2026-11-26  
**Base** `b37e10782d6d72cc36e97441c63a713b69f47f33` (`origin/main`, PR #48)  
**Targeted checkpoint** `efe204cd364365b6d8f4d59e7935afde36e8d421`  
**Clean Full and active probe source** `31dc389f5b2c7ddf86d3126d17f9394037015e24`  
**Environment** Active Codex machine, Windows 11 Pro 26H2 build 26300.9974, native x64,
unelevated, pinned .NET SDK 10.0.400 and Python 3.14.7.

## Approved package workflow

The production profile and catalog revision disclose the automatic empty launch before
approval. The coordinator records Windows Installer's applied result with a pending
verification checkpoint. A separate verifier reads the package registration and performs
the launch check before it can record success. The approval, dispatch, apply and verification
remain correlated. Wrong or absent registration prevents launch. Early exit, timeout,
cancellation and unconfirmed cleanup leave the saved attempt in review. A restart request
leaves verification pending. The recovery button only queries registration. Reopened
jobs and workshop reports retain the verification method recorded at the time, including
the registration-only result from TL-0412.

## Executable provenance and isolation

The exact machine ProductCode `{A23B657D-838B-4C9B-8F9D-386E50C1A76B}` returned the
reviewed PackageCode `{18CD7780-F2FF-4F13-9070-BBE80D108501}`, version `8.9.9.1`, publisher
`Notepad++ team (MSI installer)`, installed state `5` and machine assignment `1`.
The read-only Windows Installer database check opened the cached `LocalPackage` for that
ProductCode. The 7,933,952-byte MSI matched SHA-256
`e3b0607a092ce2604fbdac027db2c94915995e8e568ebd226f8c5493c2f61a80` and the valid signer
thumbprint `1E8E0D13B608BA908572C1A129FAEC5D228DF8A2` from TL-0412. `File` and `Media`
mapped the reviewed members to the embedded `product.cab`. `_Streams.ReadStream` read the
7,262,163-byte cabinet in bounded blocks and the inbox `expand.exe -F:<member>` extracted
three exact members in a private temporary directory. The directory was removed. The
installed counterparts had the same lengths and hashes.

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| `notepad++.exe` | 8,594,928 | `5518ad65f576b993c48da1878bda83a62f7a101d246aefab472379ca8800390c` |
| `langs.model.xml` | 584,703 | `fb2a98a1cd701cf5c05723e337666b786ed9d5cb5dbd6c93c11e0d5c80a8a671` |
| `stylers.model.xml` | 258,540 | `782c193c8a962d746735f7aa75c25ba3ecae5662e867cf27e4df60f906bdea38` |

The probe guards the directory chain, rejects redirected file handles and holds the
reviewed staged bytes read-only through the launch. The fixed `doLocalConf.xml` marker and
vendor flags `-multiInst -nosession -noPlugin` keep the copied image in local mode without
restoring a session, opening a document or loading plugins. The job limits execution to one
process and 256 MiB of commit. It uses a private inactive desktop and recognizes input idle
and the process's enabled `Notepad++` main window for a 300 ms observation. Startup has an
8-second polling budget and explicit teardown has a 2-second polling budget. The job is
kill-on-close and the adapter verifies that its process signaled and the job emptied.
Only named files and the empty `plugins` directory are removed. Unexpected artifacts cause
cleanup to be reported as unconfirmed. A killed WPF process can leave its restricted
workspace, and the pending saved checkpoint reports the missing cleanup confirmation.
[Vendor local mode and startup code](https://github.com/notepad-plus-plus/notepad-plus-plus/blob/v8.9.9.1/PowerEditor/src/Parameters.cpp#L1285-L1354)
· [Windows creation-time job assignment](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute)

## Verification results

The baseline at `b37e107` passed all 26 deterministic package tests and all 11 selected
WPF package tests. A concurrent baseline build first hit a shared SourceLink output file
lock. Serialized builds passed with the same source and test filters.
The first Full run exposed a relative evidence link in the ADR. The isolated ADR fixture
copies only governed documents. All 27 ADR contract tests passed after switching to a
repository-relative evidence reference.

The first active-machine launch reached `CleanupUnconfirmed` after the editor created two
additional local XML files and an empty plugin directory. The owned residue was removed.
The focused rerun included those exact startup artifacts in cleanup and passed. It observed
exact registration before and after the probe, an enabled editor window on the inactive
desktop, and no change in editor process or private staging-directory count.

| Check | Result |
| --- | --- |
| `eng/verify.ps1 -Tier Targeted`, with Actions, Packages, UI, Persistence and Reports test projects | Passed in 141.728 s. Quick and the selected deterministic projects passed 1,586 .NET tests plus eight governance tests. |
| `eng/verify.ps1 -Tier Quick`, after the ADR correction | Passed in 97.084 s. |
| `eng/verify.ps1 -Tier Full -Restore`, separate clean worktree | Passed in 340.072 s. 304 governance tests, 2,093 .NET tests, locked restore and zero build warnings. |
| Active-machine package verification | Passed 1/1 in 2.923 s. The exact registration remained present and the probe left no process or staging directory. |
| FI-008 WPF false success | Passed 2/2 in 3.075 s. Missing or wrong registration remained in review and the probe was never called. |
| FI-009 launch outcomes | Passed 6/6 WPF cases in 5.015 s and 8/8 package adapter cases in 1.713 s. The fixtures covered success, launch failures, cancellation and cleanup review. |
| WPF incomplete apply | Passed 2/2 in 2.998 s. Declined consent and restart-pending cases recorded their own outcomes without launching. |
| Existing Extended WPF process interruption | Passed 1/1 in 5.888 s. Killing the fixture during a held installer operation reopened the same job for review without repeating dispatch. |
| `JournalTests` standalone | 45 passed. Covers the legacy registration result and the new verifier-only transition after the package launch checkpoint. |
| WPF `PackageSetupTests` without Extended | 18 passed, including accessible plan disclosure, durable applied state before launch, review after failed verification, and saved report continuity. |

The standalone active probe is `VerifyPreviouslyInstalledReviewedPackageWithIsolatedLaunch`
in `tests/ThirdLife.Packages.Tests/PilotPackageServiceTests.cs`. The
[failure-injection register](../../../docs/testing/failure-injection.md#tl-0406-package-verification-fixtures)
lists the independent FI-008 and FI-009 invocations.

Clean Release artifact digests

- `ThirdLife.Packages.dll` SHA-256 `3a5ce324ee710b184e00fd1ef5dbb00abf839d31072d80f00ce412cd214328a3`
- `ThirdLife.UI.dll` SHA-256 `ef57f559e5401b39681099e550a2bf8d6045975f05b7ba9e9ece19dc53db199b`

The live case uses the previously authorized TL-0412 installation. It exercises the shipped
verification adapter and the reviewed installed bytes with isolated settings. The composed
WPF approval, apply, verifier, SQLite reopen and report are exercised with fixtures.
Native crash, hang and interruption of the launch itself were unobserved. Keyboard and
automation checks cover the WPF operator controls and status. Perceptual assistive-technology
output, a new install transaction, cold boot, and release exposure remain outside this evidence.
