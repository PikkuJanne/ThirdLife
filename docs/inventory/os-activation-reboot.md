# OS, activation, and reboot inventory — TL-0107

## Contract and sources

These providers return bounded observations through `InventoryProviderRunner`. They run
without elevation, request no network access, write no files or settings, and contain no
shell execution. The [Windows 11 platform amendment](../amendments/2026-11-25-windows-11-only.md)
sets the current live provider and application scope.

| Provider | Collected input | Evidence and limitation |
| --- | --- | --- |
| `windows-operating-system` | Numeric .NET OS version, Win32 workstation product type, `GetProductInfo` edition code, and .NET native OS architecture | `os.version`, `os.build`, `os.edition_code`, mapped `os.edition`, `os.architecture` and `os.family` |
| Lifecycle mapping in OS provider | Compiled reviewed Windows 11 data and UTC evaluation date | `os.support_target` and `os.lifecycle.*` retain the support result and available source, revision, as-of date, freshness deadline, evaluation date, release, and reason |
| `windows-activation` | SLC status integers for the fixed Windows application | `activation.state`; unanimous known status only, no legal ownership conclusion |
| `windows-pending-reboot` | Three fixed local registry indicators in the 64-bit view | `reboot.{cbs,windows_update,file_rename}.{indicator,confidence,collection_status,recovery}` and `reboot.pending_status`; incomplete indicators with explicit confidence |

The operating system source uses numeric, read-only APIs. It queries the product type
using the Win32 workstation, domain controller, and server codes. The type comparison
uses no version fields. The shared platform gate then confirms a Windows 11 client build
and native OS x64 using the exact reviewed build identities and the Windows 11 build 22000
floor. The existing .NET 10/WPF stack and queried Windows APIs require no higher product
build floor. Startup uses this check independently of the reviewed edition's servicing dates.
[Lifecycle coverage and refresh](windows-lifecycle-data.md)

Version and architecture are observations. Family, edition, and lifecycle labels are
inferences from numeric facts and reviewed mappings. A known Windows 11 family remains
available when stale lifecycle data withholds support. A confirmed Windows 10 workstation
or Server role is out of scope for live work.

`system.architecture` remains owned by TL-0106. The OS provider publishes `os.architecture`
with distinct provenance. Tests retain conflicting values in either provider order and reject
duplicate OS keys. Reconciliation belongs to TL-0112; collection order selects no winner.

- .NET reports the actual Windows version and the native OS architecture, including in an emulated process. [Environment.OSVersion](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/environment-osversion-returns-correct-version) · [RuntimeInformation.OSArchitecture](https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/7.0/osarchitecture-emulation)
- The Windows product type identifies workstation, domain controller, and server roles. [IsWindowsServer](https://learn.microsoft.com/en-us/windows/win32/api/versionhelpers/nf-versionhelpers-iswindowsserver) · [OSVERSIONINFOEXW](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-osversioninfoexw) · [VerifyVersionInfoW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-verifyversioninfow)
- The edition code determines eligibility for the reviewed lifecycle mapping. [GetProductInfo](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getproductinfo)

## Activation privacy and interpretation

The native adapter calls only `SLOpen`, `SLGetLicensingStatusInformation`, `LocalFree`, and
`SLClose`. The application identifier is the fixed Windows licensing application, not a
machine or account identifier. The API receives null SKU and right-name selectors. The owned
native result includes SKU metadata; only the status integer is copied into managed evidence.
The native buffer is freed and its context closed before publication.

The adapter never requests a partial product key, product ID, account identifier, licensing
text, activation, key installation, refresh, or bypass. Multiple rows must agree on a known
status. Empty, conflicting, malformed, or unavailable results stay unknown. A licensed API
status is reported as activated, not as proof of licence ownership or entitlement.

No provider logs, diagnostic events, or exports are generated. Existing runner failures
contain only fixed error/recovery codes. Native-buffer and exception canaries, serialized
evidence/error scans, and the all-provider import/source checks guard this boundary. All
normalized observations retain `WORKSHOP_RESTRICTED` classification.

References:

- [SLGetLicensingStatusInformation](https://learn.microsoft.com/en-us/windows/win32/api/slpublic/nf-slpublic-slgetlicensingstatusinformation)
- [SL_LICENSING_STATUS](https://learn.microsoft.com/en-us/windows/win32/api/slpublic/ns-slpublic-sl_licensing_status)
- [SLLICENSINGSTATUS](https://learn.microsoft.com/en-us/windows/win32/api/slpublic/ne-slpublic-sllicensingstatus)
- [Microsoft SDK header](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/slpublic.h)
  defines the `SLFreeMemory` alias to `LocalFree`.

## Pending reboot is partial evidence

Fixed locations under HKLM are the CBS `RebootPending` key, Windows Update `Auto Update`
`RebootRequired` key, and Session Manager `PendingFileRenameOperations` value metadata.
Only key existence, numeric error codes, value type, and byte count are read. File names,
personal paths, and rename payload contents are never allocated or copied.

Each available indicator is inferred and has `indicator_only` confidence. Plausible rename
metadata is a conservative present indicator, including an empty-looking four-byte value;
the provider cannot validate operation pairs without reading the contents. Invalid type or
size remains unavailable. Access denial and other failures retain source-specific status
and recovery guidance while unrelated observations remain usable.

One present indicator produces `pending_indicated`. With all indicators absent, unavailable,
or malformed, the aggregate remains unknown. These checks are incomplete and can be stale;
they never produce a global false or guarantee that a restart is unnecessary. No restart,
registry write, update operation, or permission change is performed.

References:

- [Microsoft's historical indicator description](https://devblogs.microsoft.com/scripting/determine-pending-reboot-statuspowershell-style-part-1/)
  motivates the bounded CBS/Windows Update heuristics; it is not a universal restart contract.
- [RegQueryValueExW](https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regqueryvalueexw)
  permits metadata queries with a null data buffer.
- [MoveFileExW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-movefileexw)
  documents the pending rename value. This mutating API is not imported or invoked.

## Resource, recovery, and accessibility limits

OS and reboot providers have a two-second caller timeout; activation has five seconds.
Each provider instance admits one worker, including after a caller timeout until the native
read and cleanup return. There is no polling, retry loop, background service, cache, GPU,
temporary output, database write, or migration. The lifecycle catalog is bounded to 64 rows
(12 currently), activation to 128 status integers, and output to fixed descriptor cardinality.
Registry metadata is checked without allocating the reported value size.

Synchronous Windows calls cannot be forcibly cancelled. The runner returns a typed timeout
or cancellation promptly and discards late evidence; the worker retains ownership until
cleanup completes. A fresh request on the same busy instance returns unavailable. Callers
must reuse instances rather than create replacement instances to bypass this bound. The
future orchestrator owns batch-level concurrency. No native hard-termination claim is made.

Inventory publishes fixed statuses and recovery guidance for accessible presentation.
Tests prove deterministic cancellation, recovery, and bounded output. Active measurements
describe the recorded machine and test process only.

Captured replay uses only published non-identity OS facts from `REF-CODEX-001`, revision
`2026-08-29.1`. Synthetic replay evaluates lifecycle at an explicit date. A saved historical
job instead retains its original observations, including its original support conclusion.
Rejection and unavailable cases use deterministic synthetic equivalents.
