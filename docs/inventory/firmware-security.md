# Firmware and TPM observations — TL-0110

## Scope and data flow

`FirmwareSecurityProvider` runs through the existing inventory runner as a standard user.
It reads fixed local sources, without network access, elevation, a process launch, or a
background service. Its public constructor accepts no paths, queries, methods or commands.
Core/domain code has no new platform dependency. Collection is explicit; this task adds
no UI journey, policy decision, database write, report field or support-export allowlist.

| Source | Owned facts | Limits |
| --- | --- | --- |
| `GetFirmwareType` | `firmware.mode`, raw type/error code | BIOS 1 / UEFI 2; other values unknown |
| HKLM 64-bit `SYSTEM\CurrentControlSet\Control\SecureBoot\State`, `UEFISecureBootEnabled` | `secure_boot.enabled`, raw DWORD/error | Query-only; exactly one DWORD, value 0/1 |
| Local `root\Microsoft\Windows\DeviceGuard`, `Win32_DeviceGuard.AvailableSecurityProperties` | `secure_boot.capable`, bounded raw numeric list/error | One instance; at most 16 uints |
| SMBIOS Type 24 | Four firmware password/reset states, raw settings byte; inferred firmware lock indicator | Existing 1 MiB reader; at most 1,024 structures; unique Type 24 |
| `Tbsi_GetDeviceInfo` | `tpm.present`, `tpm.version`, raw return/structure/version/interface/revision codes | Fixed 16-byte output; no TPM command submission |
| Local `root\CIMV2\Security\MicrosoftTpm`, fixed `Win32_Tpm` projection | Spec/manufacturer versions, manufacturer code and enabled/activated/owned **initial** flags | One instance; versions at most 128 characters, restricted numeric/hex notation |
| `Win32_Tpm=@`, `IsReadyInformation` | `tpm.ready`, raw boolean, information mask and return/error/status | One synchronous no-input read; no configurable method or object path |

Manufacturer code identifies the vendor, not an individual device. Arbitrary manufacturer
text, owner authorization, endorsement keys/certificates, recovery material, serials and
instance paths are not read. WMI variants and COM objects are released on the worker that
acquired them. SMBIOS bytes can contain identifiers: they are not decoded and are cleared
in `finally`. Only bounded allowlisted values leave the sources, in memory and classified
as workshop restricted by the runner. Exceptions yield fixed statuses/numeric codes;
messages and raw source objects are not logged or persisted.

## Meaning and reconciliation

Each normalized key has one owner. Related sources retain different keys and provenance.

- Secure Boot capability and enabled state are independent. DeviceGuard code 2 establishes
  capability; sole code 0 explicitly reports no relevant capability. An empty array or a
  valid array without code 2 stays unknown. Duplicate, mixed-zero or unrecognized codes
  retain the bounded raw list but cannot establish capability.
- BIOS plus enabled Secure Boot, or explicit absent capability plus enabled Secure Boot,
  makes the conflicting derived state unknown. Both raw observations remain available.
- TBS version 1 means 1.2; version 2 means 2.0. The Windows SDK describes structure revision
  1 while the Learn structure page describes 2; both are accepted and the exact revision
  is retained. Other revisions/versions cannot satisfy the normalized version/presence.
- Only TBS `0x8028400F` establishes no OS-compatible TPM exposed through that API. A missing
  WMI instance or denied WMI access never establishes physical TPM absence. TBS absence
  conflicting with a WMI instance withholds presence/version/readiness. Differing known
  TBS/WMI versions withhold the version, regardless of read order.
- SpecVersion reconciliation accepts three nonempty comma-separated numeric/hex components,
  permitting dot-separated revision parts. The first component must be 1.2 or 2.0. Other
  bounded notation remains raw expert evidence and cannot confirm the normalized version.
- Initial WMI flags do not establish current readiness or legal ownership. Readiness
  requires a successful dedicated method result and typed boolean/information outputs.
  A true boolean with information outside `0xC0` is conservatively withheld as conflicting:
  documented bits `0x40` and `0x80` describe positive states; the other documented bits
  describe provisioning needs/errors, and unknown bits cannot confirm readiness. The
  original boolean and entire uint mask remain available. No corrective operation is
  inferred or offered from this mask.
- SMBIOS Type 24 two-bit fields map 0 disabled, 1 enabled, 2 not implemented, 3 unknown.
  Power-on or administrator password enabled yields only an inferred **present firmware
  lock indicator**. Missing/disabled/not-implemented indicators never prove clear ownership.
  MDM, Autopilot, theft status, disk locks and firmware states not exposed by these sources
  are unverified. This provider does not publish `ownership.lock_state=clear`.

Unavailable/denied/unsupported input stays unknown. Independent successful facts remain
usable. Failure precedence is malformed, denied, failed, unavailable; conflicts are explicit
per-field limitations even when transport collection completed. Cleanup failure withholds
all facts and permanently blocks that provider instance. There is no numeric health score,
compatibility bypass, certification or automatic readiness decision.

## Cancellation, resources and recovery

The descriptor declares 38 singleton facts, plus one possible runner failure observation,
and a 15-second caller timeout. Only one worker per provider instance is admitted. DeviceGuard
checks a 3-second deadline with at most 32 enumeration calls; TPM checks 6 seconds with at
most 64 calls. Each enumeration request waits at most 100 ms; each source checks cancellation
and elapsed time between native reads. Bounded inputs also bound managed decoding work.

Synchronous native/COM operations cannot be forcibly interrupted and may outlive these
deadlines. The runner stops waiting, discards late evidence and retains the worker's
admission until native cleanup completes. A cancelled read can be retried after cleanup;
uncertain cleanup requires a fresh process after review, not another worker. These limits
do not cap allocations inside Windows WMI or certify all low-performance hardware.
No GPU, temporary file, cache, retained token, registry write, firmware mutation, TPM
initialization/clear, password bypass or ownership change is introduced.

IsReadyInformation is documented as requiring local administrator access. This provider
does not elevate: denied readiness remains unknown. No new accessibility control is added;
existing runner availability, failure, cancellation and recovery semantics are preserved.

## Verification and sources

Deterministic fixtures cover enabled/disabled/legacy, missing/denied/malformed, synthetic
locks, native data shapes, contradictory sources, cleanup failure, interruption and retry.
The read-only active-machine observation and hashed input set are in
[`artifacts/evidence/TL-0110/`](../../artifacts/evidence/TL-0110/). It proves only the recorded
unelevated run on that machine. No host security setting was changed to obtain coverage.

Primary contracts reviewed 2026-10-01:

- [GetFirmwareType](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getfirmwaretype)
- [Microsoft Secure Boot inventory registry fallback](https://support.microsoft.com/en-us/servicing/os/windows/2025/11/sample-secure-boot-inventory-data-collection-script)
- [DeviceGuard available security properties](https://learn.microsoft.com/en-us/windows/security/hardware-security/enable-virtualization-based-protection-of-code-integrity#use-win32_deviceguard-wmi-class)
- [DMTF SMBIOS 3.7.1, Type 24](https://www.dmtf.org/sites/default/files/standards/documents/DSP0134_3.7.1.pdf)
- [TBS device info](https://learn.microsoft.com/en-us/windows/win32/api/tbs/nf-tbs-tbsi_getdeviceinfo), [structure](https://learn.microsoft.com/en-us/windows/win32/api/tbs/ns-tbs-tpm_device_info), [Microsoft SDK header](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/shared/tbs.h)
- [Win32_Tpm properties](https://learn.microsoft.com/en-us/windows/win32/secprov/win32-tpm)
- [IsReadyInformation](https://learn.microsoft.com/en-us/windows/win32/secprov/win32-tpm-isreadyinformation)
