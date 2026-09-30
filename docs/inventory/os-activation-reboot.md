# OS, activation, and reboot inventory — TL-0107

## Contract and sources

These providers return bounded observations through `InventoryProviderRunner`. They run
without elevation, request no network access, write no files or settings, and contain no
shell execution. They do not mark a job ready. Assessment orchestration remains TL-0112.

| Provider | Collected input | Evidence and limitation |
| --- | --- | --- |
| `windows-operating-system` | Numeric .NET OS version, `GetProductInfo` edition code, .NET OS architecture | `os.version`, `os.build`, `os.edition_code`, mapped `os.edition`, and `os.architecture`; no localized display text or product ID |
| Lifecycle mapping in OS provider | Compiled reviewed data and UTC evaluation date | `os.support_target`, `os.family`, and `os.lifecycle.*`; source, revision, as-of date, freshness deadline, evaluation date, release, and reason are retained |
| `windows-activation` | SLC status integers for the fixed Windows application | `activation.state`; unanimous known status only, no legal ownership conclusion |
| `windows-pending-reboot` | Three fixed local registry indicators in the 64-bit view | `reboot.{cbs,windows_update,file_rename}.{indicator,confidence,collection_status,recovery}` and `reboot.pending_status`; incomplete indicators with explicit confidence |

Version and architecture are observations. Edition labels and lifecycle conclusions are
inferences from exact numeric facts and versioned mappings. Lifecycle coverage and refresh
are documented in [the data record](windows-lifecycle-data.md). Unknown or stale data cannot
return supported. Windows 10 remains audit-only.

`system.architecture` remains owned by TL-0106. The OS provider publishes `os.architecture`
with distinct provenance. Tests retain conflicting values in either provider order and reject
duplicate OS keys. Reconciliation belongs to TL-0112; collection order selects no winner.

References:

- [Environment.OSVersion](https://learn.microsoft.com/en-us/dotnet/api/system.environment.osversion?view=net-10.0)
  documents actual Windows version reporting on .NET 5 and later.
- [GetProductInfo](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getproductinfo)
  defines edition codes. Server and specialized editions do not inherit a client lifecycle.

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
(40 currently), activation to 128 status integers, and output to fixed descriptor cardinality.
Registry metadata is checked without allocating the reported value size.

Synchronous Windows calls cannot be forcibly cancelled. The runner returns a typed timeout
or cancellation promptly and discards late evidence; the worker retains ownership until
cleanup completes. A fresh request on the same busy instance returns unavailable. Callers
must reuse instances rather than create replacement instances to bypass this bound. The
future orchestrator owns batch-level concurrency. No native hard-termination claim is made.

There are no UI controls or interactive host changes in this task. Existing classifications,
fixed error codes, and recovery guidance remain available for accessible presentation in
later UI tasks. Keyboard, focus, screen readers, scaling, and contrast are unchanged and no
human accessibility review is claimed. Tests prove deterministic cancellation/recovery and
bounded output; active measurements describe only the recorded machine and test process.

Captured replay uses only published non-identity OS facts from `REF-CODEX-001`, revision
`2026-08-29.1`. Lifecycle conclusions are recomputed for the explicit replay date; they are
not represented as a historical support decision. No activation or reboot capture is invented.
Windows 10 and missing/failure variants use deterministic synthetic equivalents. The release
interface remains a draft; no released runtime compatibility or broad hardware claim is added.
