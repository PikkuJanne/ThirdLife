# TL-0704 private Windows Installer candidate

On 27 November 2026, `tools/build_development_msi.py` authored two
private unsigned packages using the Windows-provided cabinet compiler
and Windows Installer database API. It validates the private app ZIP
and source-bound SBOM first. It builds only an embedded application
cabinet and standard MSI action tables. The definition uses a dedicated
per-user application directory and private installer-registration key.
It carries zero custom actions, removal wildcards or service tables.
The private platform condition uses Windows 11 client build 26300 on
native x64. The prototype retains a distinct private upgrade family
and has no release publisher or signature.

| Version | Application source | SHA-256 |
| --- | --- | --- |
| 0.3.0 | `2be6a208b1e26cb7d73b3e1bda58a554d582fe75` | `fc57cdb9a90262cecc55129879c1673b600ce53853a3119064615495d4269dc7` |
| 0.3.1 | `08bd6ae3ce7012de47ed1623510c1c8779632ca5` | `fcb437dda3ec037f63dc870f4b5ecb8e9437e4cb8da6928ab2994481bdc5cec3` |

Each MSI has 1,232,896 bytes. The latest app ZIP has SHA-256
`58861ba792a188c82f1f23a921710629d32e8f39a46e996824f85f2740bc24c7`
and SBOM SHA-256
`4eb406c47fd581bdfd305b01ea42a254745f30f6a3abf294167fc69a420aa525`.
A prototype cabinet round trip matched all 19 members to the
published payload. The final native packaging and ZIP test selections
ran nine cases in 392 ms and passed at
`e1625b2f029e89e3c4fba4df61bf7eed12df9300`.

The clean Full checkout `6ee0ba7d932520111fe5c8dc17e8efd9693fea9f`
passed 319 governance and 2,680 .NET tests in 467.589 s. The later
tightening of private MSI platform admission changes only the authoring
and its focused Python test. The application source, test projects and
Full runner have identical bytes. The final native SQL test uses exact
equality because the Windows Installer SQL dialect rejected a prefix
query in the first focused attempt.

Both final MSI byte streams were copied to the existing
`ThirdLife-W11` guest and verified again. The Windows Installer API
verified and opened both packages read-only. The private platform
condition returned true for both on Windows 11 client x64 build 26300.
No setup sequence was invoked. The local preflight directory under the
guest workshop administrator's temp folder is
`ThirdLife-PrivateMsiPreflight-44a78defc9c34c37830b489abc72a60b`.

The guest had no interactive workshop shell. A scoped disposable
standard-user launch probe through Secondary Logon failed in
`Start-Process` before the child ran. A changed attempt with the manual
Secondary Logon service explicitly running returned the same
`InvalidOperationException`. Both owned fixture accounts were
verified removed. The service was restored to manual and stopped.
This limits standard-user lifecycle execution in the current session.

The existing pinned x64 SDK archive was reused to stage a private
runtime beside the MSI pair. The staged runtime reported
`Microsoft.WindowsDesktop.App` and `Microsoft.NETCore.App`
10.0.11. The 76,012,358-byte runtime archive SHA-256 is
`bbbe1ea2d8ba22e84baba9d322ccf43a077bd80c1237ee259ab7baee5ee3eb69`.
Its use is scoped to the future qualification process. No system-wide
runtime install, guest restart, Windows Update operation or app launch
occurred.

The pair supplies the inputs for a sequential test of install, startup,
upgrade, repair, rollback and removal, with preserved job data and
unrelated fixtures. Those outcomes require a verified standard-user
guest session. The final installed-file inventory and separate
authorized job-data removal are still open. The contents in the
private host and guest staging directories remain out of deliverables.
[Windows Installer standard actions](https://learn.microsoft.com/en-us/windows/win32/msi/using-standard-actions) · [MSI cabinet](https://learn.microsoft.com/en-us/windows/win32/msi/including-a-cabinet-file-in-an-installation)

## Guest process diagnosis

A first native Secondary Logon attempt rejected the command parameters.
The corrected fixed-path runner kept the command below the documented
1,024-character limit and created the disposable user's process. The
process exited with `0xC0000142` before the fixture script wrote its
receipt or invoked Windows Installer. The exact fixture account was
removed. Its unloaded profile and the protected staging directory
`C:\ProgramData\ThirdLife-PrivateLifecycle-2b7d05c5a07040ca83942f9f46dad2bd`
were retained. There was no install log or owned application directory.
After the fixture profile unloaded, Secondary Logon was verified stopped
with its original manual start type. The currently available
noninteractive route therefore leaves actual lifecycle execution pending.
[Native logon constraints](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-createprocesswithlogonw)

A compiled fixture removed the PowerShell script dependency. Secondary
Logon started its pinned local .NET host, which exited with CoreCLR
initialization failure `0x80008089`. No installer log or application
directory existed. The fixture account was removed and Secondary Logon
returned to its prior manual and stopped state. The unloaded profile
and protected staging folder
`C:\ProgramData\ThirdLife-PrivateLifecycle-1cc326a9168a490798f7276f09c75bc2`
were retained. [Host error reference](https://github.com/dotnet/runtime/blob/main/docs/design/features/host-error-codes.md)

The password-authenticated limited scheduler fixture registered, but
the demand start left it ready with no run. A short exact task event
trace reported event 101 and `0x80070569` for the requested logon type.
The existing guest authority does not grant the fixture that batch
logon. The owned task and account were verified removed and the
diagnostic channel returned to disabled. The protected staging folder
`C:\ProgramData\ThirdLife-PrivateLifecycle-c4ec7034878b4760b56d7dfadd804d91`
remains with no installer log. A supported standard-user guest session
is still required for actual installation and application observation.
[Windows result code](https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--1300-1699-)

The later interactive fixture located and corrected the Windows
Installer launch condition and observed the exact private lifecycle.
[Interactive lifecycle follow-up](interactive-lifecycle.md)
