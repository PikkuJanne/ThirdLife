# TL-0311 authenticated broker transport checkpoint

**Date** 2026-11-25  
**Base** `8f1441be844536e8b4ef88193b3a3e4c1d9c453e`, PR #35  
**Environment** REF-CODEX-001 rev 2026-08-29.1, unelevated interactive Windows 11 x64, .NET SDK 10.0.400 and Python 3.14.7

## Status

The authenticated local transport is implemented. The development broker
compiles and the ordinary apphost rejects an unelevated launch. The UI
returns `Unavailable` before requesting elevation because the current
framework-dependent apphost cannot establish trusted startup. This checkpoint
does not complete TL-0311 or authorize its integration into `main`.

Windows can load the managed host, runtime and startup code before broker
`Main` can validate the caller or request. The direct shell-launch candidate
was removed after review identified that gap and the writable development
deployment. No host consent request was made. The launch decision, missing
toolchain, and remaining proof are documented in the
[broker launch trust boundary](../../../docs/security/broker-launch.md).

## Completed technical checks

| Scope | Observed result |
| --- | --- |
| Local pipe | Native first-instance, overlapped byte pipe with remote clients rejected and a protected logon-SID data ACL. Kernel process and session queries agree with both endpoints. An unlisted logon fixture is denied. The peer token is read at Identification level. |
| Exact request | A fixed bounded ticket pins the known client process, correlation, UTC expiry, nonce proof and canonical request digest. A single ordered batch is checked independently against the compiled no-op registry. The nonce itself is not in the bootstrap arguments. |
| Framing and result | The 4,096-byte cap is checked before payload allocation. A truncated frame differs from normal EOF. Only an exact ordered result for the active action is accepted once. Typed results carry no raw output and do not establish verification. |
| Recovery | Deadline, disconnect, cancellation, partial results and a second request are tested. The broker closes after its batch or a bound, and the client retains partial terminal observations when the connection becomes ambiguous. |
| Launch | The Windows error-code mapping and pending-consent cancellation are simulated. Production UI requests no elevation with this development host. Real UAC, elevated peer identity and failure of the actual elevated process remain unverified. |

## Verification

- Before edits, the existing broker project passed 48 tests in 5.237 s.
- Locked first-party references reuse the broker protocol and compiled action
  contracts. The UI build carries the shared protocol and leaves the development
  apphost with the broker project and its tests. No new package or machine
  configuration was required. Release builds passed with warnings as errors.
- The first focused IPC run found a Windows fixed-width `TokenElevation`
  query failing on a null-buffer size probe. Reading those integer fields
  into a four-byte native buffer corrected the problem. The focused native
  regression passed before running the related suite.
- The focused broker selection passed 30 cases in 5.092 s, including the independently
  invokable `ActiveMachine` unelevated apphost rejection. Native pipe tests
  observed ordered final frames after server disposal followed by clean EOF.
- The focused UI `BrokerBatchClientTests` passed eight cases in 6.490 s,
  including ordered results, reconstructed partial state after stream loss,
  pending consent cancellation and two Windows consent-path fixtures. Fixture
  results do not establish an actual elevation prompt or operator decision.
- `./eng/verify.ps1 -Tier Targeted` with the Broker and UI projects passed
  1,204 .NET tests in 76.187 s, including Quick. The exact checked source tree
  `fa3fe60e9b9bc3f479b5145d5f9af3e04363cc1b` is pushed as checkpoint
  `382e4f4767d0efadaf217536a1e2c138504fa7e7`.
- Clean `./eng/verify.ps1 -Tier Full -Restore` on the same tree passed in
  343.515 s. It ran 289 governance and 1,926 .NET tests, with locked restore,
  format verification and the warnings-as-errors Release build.
- After recording the launch blocker, Quick passed in 69.979 s. The final
  guest limitation also passed the bundle and repository validators.
- The network-disabled Windows Sandbox cross-account profile stopped after
  342.323 s without a guest result. Its window reported that Windows Sandbox
  failed to initialize, with `0x80070780`. A minimal offline startup probe
  without mapped folders or a logon command reproduced that exact error. This
  identifies a Sandbox startup blocker independent of the governed mounts.
  The distinct-account test remains unobserved and no host policy was changed.

## Windows 11 platform reconciliation

The draft incorporates integrated `main` at `af51598904b60f6f4fadf0bc09efe6857a51a95b`.
Both retained broker commits are ancestors of this checkpoint. The client checks the shared
platform contract before identity lookup or launch. The broker independently checks it before
ticket processing, token checks or IPC. The default trusted launcher remains unavailable.
Synthetic refusal cases covered unsupported OS, unsupported native architecture and
unverified identity at each boundary.

- The pre-reconciliation IPC selection passed 29 cases in 8.074 s. Targeted
  with `-Restore` and the Broker and Broker.SecurityTests projects passed 1,248 .NET
  tests, including Quick, in 78.901 s on tree `30b2427c113b6c30b82a128380ce5ec0cdc2e44b`.
- The `test_sandbox_harness.py` unittest selection passed 41 static and function cases
  in 55.167 s. The distinct-account guest was outside this reconciliation run.
- On the active unelevated Windows 11 Pro 26H2 native-x64 host, build 26300.9974,
  `UnelevatedApphostRejectsAValidTicketWithoutStartingAPipe` passed in 1.881 s.
  Two simulated consent-path cases passed in 1.736 s.
- The exercised Broker apphost SHA-256 was
  `e339183b291080f53b3103ba0ce7c7a0b6817fe8bff15acd279f94f6c23590a8`.
  The Broker assembly was `74e6370d888e537c0b997f9cf50acb01bb5cc1be83a651dabfc23391e365ceca`
  and UI assembly was `7a5b3ea94eb920aff32bf6d0e91e4b99de1caf313fade3df18c5a4e2041d3052`.

The reconciliation covers platform admission. Trusted elevated startup, real UAC and
recovery, and the distinct-account guest remain TL-0311 blockers.

## Native broker implementation checkpoint

The project owner authorized native broker and launcher work on 2026-11-25 and
deferred preparing the same-machine Hyper-V guest. The distinct-account test
and its guards remain. This work did not create accounts or start a guest.

The broker now selects Windows x64 Native AOT, requests static System32-only
imports and requires System32-only dynamic search in its entry point. The
developer provisioning utility stages the reviewed executable and digest under
administrator-controlled ACLs before publishing the fixed installation at
`Program Files\ThirdLife\Broker`. The production client validates and holds the
installation through explicit Windows consent, checks the returned process image,
then applies the existing identity and protocol validation. No installed candidate
was present for these checks.

| Exact local AOT input, all 10.0.14 | Verified NuGet archive SHA-256 |
| --- | --- |
| Microsoft.DotNet.ILCompiler | `6744a4a82b31a8568df48e927baaa85378a848966d5712323956f63528ed38b9` |
| runtime.win-x64.Microsoft.DotNet.ILCompiler | `e4279d8ed9bcc01b1d8a3a9b2c7db16f3249e098dc3081e15c6d93ef462e1bce` |
| Microsoft.NET.ILLink.Tasks | `e2a71b32b5c135dc2b0c87ea8fba6c9e677332e00bf959fbcdced239b825b75c` |
| Microsoft.NETCore.App.Runtime.NativeAOT.win-x64 | `76c0f133981113273a0e50dba97a6107eead821f707565443e787f0aa9f9fc69` |

The pinned SDK 10.0.400's default 10.0.11 compiler failed audited restore with
NU1903. The patched packs restored with locked mode and audit active. `dotnet
nuget verify --all` passed both Microsoft author and NuGet.org repository
signatures for the four archives. [Microsoft's advisory](https://github.com/dotnet/runtime/security/advisories/GHSA-p3jp-9v83-76w9)

| Verified Microsoft bootstrapper | Fixed channel catalog advertised and received |
| --- | --- |
| Visual Studio Community 2022 17.14.45, build 17.14.37906.3, SHA-256 `051dffb68c39b9a4f15b830fdae8d08dd5620369e923262c46f27e07a428a68c` | Advertised 30,447,538 bytes, `da49c304df0fff4e75c20849171840a06840e845ce814104d709dfb5cd461668`. Received 17,959,942 bytes, `d1d8109899558618035724949673f69f8c1b5704f542a2e76f31d87e425d5710`. |
| Visual Studio Community 2026 18.12.1, build 18.12.12412.261 | Advertised 31,964,369 bytes, `cc6841a1e5de4eadb9d1dec5c4d75dfe081c2e6b39981c220b6e69c50b557f39`. Received 18,805,701 bytes, `a7194b79768c80787eff90ff29f70d26f6bc89547a87ee21ab0e10cd6663d08f`. |

Both bootstrapper signatures were valid. The fixed official sources were the
[17.14.45 channel](https://download.visualstudio.microsoft.com/download/pr/95995f57-8355-4ff5-8d90-a7e1669fb3b3/a788dfa3e6f50a33f7dee8396fd40d2946c69a776c0cfa5208f56d461fd1a825/VisualStudio.17.Release.chman)
and the [18.12.1 catalog](https://download.visualstudio.microsoft.com/download/pr/646c5a89-4d3a-4f43-985e-9c4c6771f304/cc6841a1e5de4eadb9d1dec5c4d75dfe081c2e6b39981c220b6e69c50b557f39/VisualStudio.vsman).
Both catalog files parsed and claimed
the correct release. A compression check and a cache-miss request reproduced
the first mismatch. Setup was not initiated. The sizes and digests for 17.14.45
also match an independent report. [Microsoft Q&A report](https://learn.microsoft.com/en-us/answers/questions/6070250/visual-studio-build-tools-2022-17-14-45-visualstud)

- On Windows 11 Pro 26H2 x64 build 26300.9974, Release win-x64 publish passed
  locked restore, runtime pack digest validation and managed compilation before
  Microsoft Native AOT targets reported `Platform linker not found` in 4.646 s.
  No native image was linked, inspected, provisioned or elevated.
- The managed launcher focus set passed 28 tests, including missing or unsuitable
  installation, expiry and cancellation, exact ticket arguments, ACL, digest and
  PE loader metadata. Separate deployment checks passed six tests in 2.741 s.
  Read-only checks on this machine reached the expected absent install after
  passing the system-drive and Program Files ACL checks. Unelevated installation
  was refused. These checks exercised the code and fixtures.
- The isolated MSBuild `VerifyBrokerAotRuntimePack` target accepted the actual
  archive in 0.825 s, and rejected a mismatched expected digest in 0.805 s.
  The focused managed broker set passed 33 cases in 7.172 s.
- `./eng/verify.ps1 -Tier Targeted -Restore` with the Broker and Broker.SecurityTests
  projects passed 1,267 tests in 74.896 s, including Quick and a warnings-as-errors
  Release build.
- A clean same-machine worktree at `63eec10f80d10c25fe8567ea03e4007e4102e694`,
  tree `69d99f8ea67476e6ef672144a3561479fa6cbf50`, passed
  `./eng/verify.ps1 -Tier Full -Restore` with 304 governance and 2,001 .NET tests
  in 342.790 s. The Full default test filter excludes actual consent rehearsals.
  Supplementary Windows CI also passed on this source.
- `./eng/generate-sbom.ps1 -ProductVersion 0.7.0-dev -SourceRevision <source commit>`
  completed in 7.035 s on the clean checkpoint. The 32-component CycloneDX
  SHA-256 was `222b8e67b04892c28c09d368e67167cc633426be461c14a57902820c36bfba37`.
  The new matrix is pending release review. The provisioning script SHA-256
  in a Windows checkout is `cd8c8d67d943872b9206fc14379df428406785d10a4b7b1122e9cf248c6a90da`.

An intact vendor-catalogue installation or a separately verified licensed
Microsoft x64 C++ toolchain and Windows 11 SDK is the next build input. Consent
for the rejected catalogs would leave the source-integrity issue unresolved.

## Remaining evidence

- Trusted, fixed elevated startup before .NET hosting or any broker work.
- Actual explicit UAC accept and decline, matching elevated broker token,
  process exit and recovery for interruption of the elevated apphost.
- Guest execution of the distinct standard-account connection case.
- A capability and exact artifact for the supported Windows native build
  toolchain if the project uses the C# Native AOT route.

## Native toolchain recovery and reset reconciliation

The project owner reported the C++ workload and Windows 11 SDK installation.
`vswhere` found one complete instance, Visual Studio Community 2026 18.12.1
(18.12.12412.261), with Microsoft x64 C++ tools. Microsoft-signed
`Launch-VsDevShell.ps1` selected x64 host and target. Compiler 19.52.37029.0
and linker/DUMPBIN 14.52.37029.0 had valid signatures. The VC toolset
directory was 14.52.37025. Signed RC, MT and SignTool resolved with the
10.0.28000.0 SDK. The x64 SDK supplied `kernel32.lib`, `advapi32.lib`
and `ucrt.lib`. The active host remained unelevated.
[Microsoft Developer PowerShell](https://learn.microsoft.com/en-us/visualstudio/ide/reference/command-prompt-powershell)

The pre-edit broker IPC baseline passed 33 cases. The pinned .NET SDK
10.0.400 published the original `642e629` checkpoint in 23.431 s.
After reconciling with reset merge `0492cb7`, all four previously
signature-verified 10.0.14 NuGet archives matched their recorded SHA-256.
Locked Release `dotnet publish ./src/ThirdLife.Broker/ThirdLife.Broker.csproj
-c Release -r win-x64 -p:RestoreLockedMode=true` succeeded in 16.582 s
on the reconciled production sources.

The 2,889,728-byte native executable had SHA-256
`ea3d534d885b46b396e5aa8c31b81666c4fc8d5195a27e86cca464fc90bb3861`.
DUMPBIN observed x64 PE32+, Windows GUI subsystem, ASLR, high-entropy VA,
NX, CET compatibility, security cookie, System32 dependent-load flag
`0x0800`, and zero managed and delay-import directories. Static dependencies
were `ADVAPI32`, `bcrypt`, `KERNEL32`, `ole32` and UCRT API sets for math,
heap, string, conversion, stdio, runtime and locale. The import table contains
`SetDefaultDllDirectories`. MT extracted `asInvoker` and `uiAccess=false`.
The unsigned local candidate returned exit 2 on an ordinary launch without
a ticket. Only the executable and its identity record are provisioning inputs.
[Microsoft linker flag reference](https://learn.microsoft.com/en-us/cpp/build/reference/dependentloadflag)

Before provisioning, the exact reviewed script SHA-256 matched
`cd8c8d67d943872b9206fc14379df428406785d10a4b7b1122e9cf248c6a90da`.
The broker installation was absent. A pinned read-only script invocation
reached that expected absence after the system-drive and Program Files ACL
checks. Windows PowerShell's default policy rejected direct `-File` execution.
The reviewed byte-reading invocation used the signed System32 shell and
executed the same verified script body without changing the execution policy.

The development reset supersedes the original integration hold for safe
isolated work. TL-0316 covers that integration. TL-0317 covers live
qualification, including the owner-deferred distinct-account scenario.
The ordinary package path has no call to the broker launcher.

## Reconciled checkpoint verification

Merge checkpoint `a5fcef310c685045f7ca0f282bb70ea08d4bb51a`,
tree `19a4a15f60f9dfa77280471ad24d33140b2606d2`, retains both the draft
broker head `642e629` and reset merge `0492cb7` as parents.

| Check | Result |
| --- | --- |
| `eng/verify.ps1 -Tier Quick -Restore` | Passed in 106.670 s. Seven focused governance and 1,193 .NET tests. |
| `eng/verify.ps1 -Tier Full -Restore` | Passed in 350.666 s in a clean same-machine worktree. 303 governance and 2,025 .NET tests. Locked restore, format and the Release build passed with zero warnings. |
| Broker active-machine unelevated rejection | One valid-ticket managed apphost case passed in a separate 2.428 s invocation on the reconciled build. The native candidate independently refused a direct launch without a ticket. |
| `eng/run-ui-smoke.ps1` | The reconciled app launched unelevated, responded to external UI Automation and exited normally in 1.371 s. Peak working set was 269,316,096 bytes in this single startup observation. |
| `eng/generate-sbom.ps1 -ProductVersion 0.8.0-dev -SourceRevision <checkpoint>` | Generated the 33-component inventory in 5.564 s. SHA-256 `bf8a544ad588ca6740c75b2e95a394235f0374ec9651ccb4bc5e25f9b0aaff0f`. Release review is pending. |
| GitHub Actions | Supplementary Windows clean-checkout check passed on `a5fcef3`. |

The clean Full run covered the deterministic authentication, launcher, consent
mapping and recovery checks. The clean worktree excluded the original checkout's
untracked attachments and ignored test-result file from fixture copies.
No physical accessibility or cross-hardware observation was added.

## Protected provisioning and live consent

After the specific Windows PowerShell provisioning request, the signed System32
shell ran the previously pinned script and returned exit 0.
An independent read-only `-VerifyOnly` run returned `Verified` for the exact
digest at `Program Files\ThirdLife\Broker`. From the unelevated client,
`InstalledCandidateMatchesTheReviewedDigestAndRejectsDirectWriteAccess` passed.
It compared the executable and digest record, denied direct writes to both, and
opened the production launch guard. Verification and the test took 3.616 s.

`InstalledBrokerCompletesOneNoOpThroughExplicitConsent` passed in 6 s after
Windows granted elevation to the exact installed executable.
The production client checked the returned process image, elevated peer token
and same-user/session identity. The authenticated batch returned one matching
applied no-op, the pipe closed cleanly, the broker exited with code 0, and the
WPF client token remained unelevated. No package or other device action used the
broker.

The live decline case returned `Completed` instead of `Declined` in 3 s.
The second attempt gave 15 s advance notice before opening the request and
returned the same result in 4 s. Both attempts completed one no-op. Windows'
prompt choice could not be independently attributed. The host reported
`EnableLUA=1` and `ConsentPromptBehaviorAdmin=5` on read-only inspection.
No policy settings were changed. The directed Windows cancellation mapping
passed in deterministic tests. Actual decline, elevated interruption recovery
and distinct-account denial remain open. Keep ordinary broker dispatch
unavailable while TL-0317 remains open.
