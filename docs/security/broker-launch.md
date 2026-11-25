# Broker launch trust boundary

TL-0311 limits elevated work to one authenticated batch of compiled no-op actions.
The operator UI stays unelevated. The previous development apphost ran through the
managed .NET host, whose runtime selection and startup hooks can execute before the
broker validates the caller. Its output was in a development directory. That build
remains useful for transport tests. [Earlier checkpoint](../../artifacts/evidence/TL-0311/report.md)

## Native broker

Use the repository SDK 10.0.400 and the matching, explicitly pinned Native AOT inputs
for the broker. The .NET SDK's original AOT compiler package 10.0.11 failed the audited
restore. The broker project pins the patched 10.0.14 compiler, trim tools, and native
runtime pack. It checks the downloaded runtime pack archive's SHA-256 because SDK
package downloads are outside the ordinary NuGet lock graph. [Microsoft advisory](https://github.com/dotnet/runtime/security/advisories/GHSA-p3jp-9v83-76w9)

The publish command for the installed image is
`dotnet publish ./src/ThirdLife.Broker/ThirdLife.Broker.csproj -c Release -r win-x64`.
Use a verified Microsoft C++ x64 toolchain and Windows SDK. Record the source revision,
actual linker and SDK versions, PE import and load-configuration inspection, and exact
publish SHA-256 before provisioning any bytes. The project requests
`/DEPENDENTLOADFLAG:0x800` for static imports. The broker also requires System32-only
dynamic DLL search before it probes Windows identity or opens IPC. Globalization is
invariant to avoid an app-local ICU dependency. [Native AOT interop](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop)

## Provisioning and identity

The TL-0311 provisioning utility is [install-thirdlife-broker.ps1](../../eng/install-thirdlife-broker.ps1).
Use it from a trusted administrator provisioning context with the exact reviewed
script bytes and the separately recorded published SHA-256. The executable path
provided to this utility is a source of bytes. Its SHA-256 and the inspected build
record establish candidate identity. The x64 PE and load-configuration checks also
inspect the staged bytes. Those header fields can occur in other Windows executables.

The utility provisions the fixed system-drive Program Files path
`ThirdLife\Broker`. It creates private staging on the same filesystem, hashes the
source read, verifies the protected staged copies and publishes a new directory
without overwriting. The final directory contains only `ThirdLife.Broker.exe` and
the 64-byte lowercase ASCII `broker.sha256` identity record. The owner and protected
ACL allow Administrators and SYSTEM to write. Standard users have read and execute
access. The utility checks the relevant system ancestors and fails closed on
unsuitable paths or permissions. A repeat with the same verified candidate can
audit the existing install. Replacement and release packaging belong to the
governed installer work.

The administrator supplies the candidate and reviewed SHA-256 with
`-BrokerArtifact` and `-ExpectedSha256`. The independent `-VerifyOnly` and
`-ExpectedSha256` path checks an existing installation without making changes.
If the worktree is used as a source for local verification, pin the script digest
independently in the already trusted provisioning session. Read and hash the bytes
once under read sharing, then invoke those exact bytes. The provisioning context
must trust the administrator's shell and toolchain before any script executes.
The UI launches only the installed broker. [Microsoft installation guidance](https://learn.microsoft.com/en-us/windows/win32/msi/guidelines-for-authoring-secure-installations)

## Unelevated launch and verification

The production client checks the Windows 11 native-x64 platform and resolves the
fixed installed path. It checks protected owners and access lists, rejects
redirected objects and multiply linked files, verifies exact identity and startup
metadata, and holds handles that prevent substitution through the consent request.
It asks Windows for explicit elevation of the exact absolute path with a protected
working directory. It verifies the image path of the process Windows returns
before the same-user, session and IPC checks. Missing or unsuitable installs stay
unavailable. Decline and cancellation preserve the unelevated workflow, and ambiguous
dispatch retains the existing recovery result.

TL-0317 qualified the reviewed no-op candidate through the opt-in rehearsal.
TL-0313 verified durable application orchestration. Ordinary broker-dependent
actions require TL-0318 live application qualification and the TL-0314
privilege decision. Package setup uses its reviewed Windows Installer route.

The host publication, provisioning and consent observations are preserved in
[TL-0311 evidence](../../artifacts/evidence/TL-0311/report.md). TL-0314 retains the
accountable privilege-boundary decision.

TL-0317 uses the dedicated Hyper-V guest on the active machine. The runner checks
the VM identity and Basic console session, provisions or verifies the reviewed
candidate, and starts the consent rehearsals from the unelevated interactive
WPF client. Each rehearsal uses compiled no-op actions. The distinct-account
case reuses the production pipe ACL check under the Hyper-V profile. Follow the
[TL-0317 runner procedure](../../artifacts/evidence/TL-0317/report.md) for DPAPI
credential reuse and the verified guest outcomes.
