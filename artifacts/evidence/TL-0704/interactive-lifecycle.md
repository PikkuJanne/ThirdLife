# TL-0704 private MSI lifecycle on the existing guest

On 27 November 2026 ThirdLife-W11 ran Windows 11 Pro, build
26300.9974 and native x64. The scoped C++ probe verified the disposable
standard-user SID and profile under Secondary Logon. A noninteractive
test then showed `msiexec.exe` exiting with `0xC0000142` before creating
an installer log. Loading the system Windows Installer DLL directly
was also unavailable in that context. These observations qualify the
failure before package evaluation.
[Windows result code](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-erref/596a1078-e883-4972-9bbc-49e60bebca55)

The existing Basic console had a Windows sign-in screen. The disposable
fixture was added to the built-in Users group and signed in through its
own selected tile. The native supervisor verified its account and
profile on session 1. An interactive limited task used that already
logged-on token. The original two packages remained unsigned private
artifacts. The first actual 0.3.0 install reached LaunchConditions and
returned 1603. Its log reported legacy compatibility values
`VersionNT64=603` and `WindowsBuild=9600` while the host query and
64-bit Windows registry identified the eligible build.

The corrected MSI reads `CurrentBuildNumber` from the native
machine registry before evaluating the exact build. A fixed Type 51
property assignment first clears any supplied build value. AppSearch
then reads the 64-bit value. Missing evidence leaves the property set
to an unavailable sentinel. Windows Installer still tests the x64 and
client indicators. The new log confirmed the reset followed by
`TL_NATIVE_BUILD=26300`. The authored table has no executable custom
action. [AppSearch](https://learn.microsoft.com/en-us/windows/win32/msi/appsearch-table) · [Registry locator](https://learn.microsoft.com/en-us/windows/win32/msi/reglocator-table) · [Native x64 indicator](https://learn.microsoft.com/en-us/windows/win32/msi/msix64)

| Version | Exact desktop source | MSI SHA-256 |
| --- | --- | --- |
| 0.3.2 | `2be6a208b1e26cb7d73b3e1bda58a554d582fe75` | `8ebb2a32c98f0430233b3422173ee3bcc7a5273a2d8b480f5fff3410420add70` |
| 0.3.3 | `ee09076c6cba111546615ae4e06c86ad1aca0aa6` | `b83a7e3a9637b3b3cae5566d6881e093173c740b41a6f21340ce2c217fd17b19` |

The authoring fix is `00b305b1a075727d788a5a084bf5e3e7cafcf67b`.
Nine focused package authoring and verifier tests passed. The 0.3.3
archive SHA-256 is
`b1f9bcda66519cdf815fa16490db27ba34715bd677d347adc08123b9497a57aa`
and its CycloneDX SBOM SHA-256 is
`b85ccac4c15d560d402a273b67444ba4e8072f307795ebf53e8e984134016f7d`.

The normal guest `msiexec` route returned zero for clean install,
repair, upgrade and uninstall. It reconstructed the one removed
owned runtime configuration file during repair. All 19 installed
files matched their respective reviewed payload after install,
repair and upgrade. An attempt to reinstall the older package
returned 1603 with the expected current-version LaunchConditions
message and preserved the newer bytes. Uninstall removed all
19 install files. The job-directory and unrelated synthetic
sentinels retained their original hashes.

The exact 0.3.3 app archive was also verified and extracted on the
active host. The 19 unpacked entries matched the published bytes.
Its application assembly SHA-256 was
`75a6b919f858870cdc26ba37f6765083abfb61385fabb008f7967245db30b646`.
External UI Automation reached Inspect in 1.258 s under the
unelevated host token and the app closed normally by 1.449 s.

The installed app check observed no main window during its bounded
attempt. The physical guest display was occupied by Windows first-use
privacy choices for the fresh test account. These choices were left
pending. Installed main-window verification requires an authorized
interactive desktop that has completed those choices. An earlier
private unpacked host launch has its own scope. Restore after an
interrupted installer transaction and preservation of a populated
SQLite job through the exact MSI still need their own evidence.
[Unpacked launch and export](../TL-0706/private-interface.md) · [Job archive verification](../TL-0703/reversible-job-archive.md)

The temporary tasks and accounts were verified removed. The exact
fixture session was signed out, its local credential copy removed,
and Secondary Logon verified Manual and Stopped. The protected
guest lifecycle directory
`C:\ProgramData\ThirdLife-PrivateLifecycle-f3c04e910efc4fbebc34ce37507e396d`
and unloaded fixture profile retain the restricted logs and synthetic
sentinels. Earlier diagnostic directories and unloaded profiles
remain protected. No actual privacy choice, public distribution or
release signing was performed.

A clean detached active-host run of `eng/verify.ps1 -Tier Full -Restore`
passed 319 repository checks and 2,688 .NET tests in 488.785 s from the
authoring fix source. The separately invoked focused package checks
inspect the exact standard-action order, the native registry view,
the fixed reset and the admission condition.
