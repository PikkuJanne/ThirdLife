# Battery observations — TL-0109

## Scope and evidence ownership

`BatteryReportProvider` supplies observations through the existing inventory runner. The
public constructor accepts no path, executable, arguments or report. Policy and assessment
orchestration remain later tasks. This operation never changes power settings, exercises a
battery, estimates endurance, diagnoses a fault or assigns a health score.

`battery.present` and `battery.count` describe a completed snapshot. Per-pack facts use
`battery.pack.design_capacity_mwh`, `battery.pack.full_charge_capacity_mwh`,
`battery.pack.cycle_count` and `battery.pack.full_charge_ratio_percent`. Source references
`battery.powercfg.report.pack.00` through `.03` group one snapshot only. They are not durable
identities. Multiple packs stay separate; no average, worst-pack choice or enumeration-order
overwrite is introduced. The older synthetic `battery.full_charge_ratio_percent` policy input
and human-owned `battery.swelling_inspection` are not claimed by this provider.

Capacity and cycles are observed facts. The ratio is inferred as full-charge capacity divided
by design capacity times 100, rounded to two decimal places with midpoint-to-even rounding.
It is available only when both capacities are valid positive mWh values. A full-charge value
above design capacity and the resulting ratio above 100 are preserved without a diagnosis.
Zero and UINT32_MAX are unavailable source sentinels, including zero cycles (unsupported).
Missing, duplicate, malformed or relative-unit capacity never produces a percentage. Cycles
remain independent of capacity availability.

The UI states that battery reports cannot detect swelling and swelling requires human
inspection. It uses standard WPF text and scrolling controls, system colors, wrapping and
automation names/help text. It makes no automatic physical-inspection finding.

## Fixed Windows operation

The adapter obtains the Windows system directory and invokes its `powercfg.exe` with the
single compiled operation `/batteryreport /xml /output report-<internal GUID>.xml /duration 1`.
There is no shell, stdout/stderr capture, PATH lookup, caller option, elevation or network.
The process starts suspended with no inherited handles. It joins an unnamed job with
kill-on-close before resuming. Success requires the primary process to exit and the job to
have no active processes. Failure, timeout and cancellation terminate and wait for the job.

An explicit `GetSystemPowerStatus` battery flag of 128 establishes absence without running
powercfg. The unknown flag 255 does not. Previous owned artifacts are recovered before this
shortcut. A valid report with an explicit empty `Batteries` element also establishes absence.
Denied, missing, malformed, nonzero-exit or timed-out collection cannot claim no battery.

## Temporary data and privacy contract

The registered location is the Windows local application-data folder plus
`ThirdLife\SetupCore\BatteryReports`. Packaged execution resolves the Windows-provided
redirected folder and pins its handle-resolved location before creating report files.
Only the default known-folder root permits this one-time binding: both aliases must have
the fixed product suffix, safe local ancestors, matching file identity and private ACLs.
Subsequent checks still require the exact canonical path. This is a
narrow operation-lifetime exception to memory-only raw collection, required by the fixed
Windows report API. The generated XML is `RAW_UNTRUSTED_SENSITIVE`: it can contain machine
identity, serials and usage history even though only capacity and cycle fields are needed.
It is never copied to the job store, logs, evidence artifacts, support bundles or reports.
No raw report, path, exception message or device identifier leaves this adapter.

The directory and its files have protected ACLs for the current user, LocalSystem and local
Administrators. Reparse points, extra hard links, unexpected ownership/permissions and file
identity changes fail closed. A fixed empty `.lock` prevents concurrent writers across
processes. In-process collection is also shared and single-flight. One random report filename
and a bounded `pending.bin` ownership record belong to the current operation. The record
contains an internal ID, file identity and creation time, and is flushed before powercfg
starts. It contains no report content or hardware identity.

Cleanup uses an exclusive handle to the exact validated file identity. It removes the report
and then its ownership record on success, failure, cancellation and timeout. The protected
empty root and `.lock` remain for reuse. The next collection immediately recovers a verified
previous operation before creating another report, including when the battery is now absent.
This is earlier than the 24-hour stale-file default when the application is running again;
an offline or stopped application cannot perform a timed sweep. Future application startup
orchestration must invoke this recovery before further battery collection.

Unknown, replaced, linked or corrupt objects are preserved for human cleanup review. A crash
between empty file creation and ownership-record commit can leave an unregistered empty file;
powercfg cannot start in that interval. A cleanup failure discards measurements, reports
`cleanup_incomplete` and blocks further source collection in the process. Do not use recursive
deletion or broaden permissions to recover. An operator must inspect the registered root and
the reported cleanup state; collection can resume in a new process after safe remediation.

## Parsing, limits and failure states

The stream parser accepts only the reviewed battery XML namespace and report version 1.1.
It checks exact structural paths and validates the whole document. DTDs and external entities
are prohibited. Ignored identity/history sections are streamed without retaining their text.
Unknown schema/version or ambiguous containers are invalid, rather than silently empty.
Duplicate numeric fields invalidate that field; related independent fields remain available.

Bounds are four batteries, 4 MiB of input bytes and XML characters, depth 16, 100,000 XML
nodes and 64 attributes per element. Numeric parsing is invariant ASCII unsigned decimal.
The byte bound applies to UTF-8 and UTF-16 alike. Native report growth is checked every
100 ms against a 4 MiB soft ceiling. Windows may write past that ceiling between checks;
detected excess is terminated, discarded and cleaned. This is not a filesystem quota.
The adapter preflights at least 16 MiB of caller-available disk space.

The command limit is 20 seconds, termination wait is bounded at five seconds, and the caller
runner waits at most 30 seconds. A cancelled caller can return before native cleanup ends;
it receives no late facts or unverified cleanup claim. The source retains its slot until
cleanup ends and blocks admission if cleanup cannot be verified. Synchronous native and
filesystem calls are not forcibly interruptible; deadlines are checked between calls, so
the source can retain its slot beyond the caller deadline until cleanup returns.
No scheduled background polling, retry loop,
permanent service, GPU, cache, battery stress or background indexing is introduced.

Lifecycle facts use fixed `battery.report.collection_status`, `.cleanup_complete`,
`.native_error_code` and `.exit_code` keys. Numeric Windows/exit codes preserve their full
unsigned range. Source collection status and overall normalization outcome are separate:
a collected report can contain a malformed field and yield an invalid-data provider outcome.
The maximum is 22 facts plus one provider-failure observation. Normalized observations remain
`WorkshopRestricted`; no export allowlist or database migration changes.

## Provenance and claim limits

The reviewed XML shape came from the active machine's Microsoft-signed `energy.dll`, version
10.0.26100.8328, SHA-256
`248da0a76c4244151b04706152fb2e4398edae44704c3e1d4a01249fea0e22af`.
This establishes the tested version's emitter shape, not a promise that future Windows
versions retain it. Unknown layouts fail closed. Fixtures are synthetic and contain no
captured personal report. Runtime results, input hashes and limitations belong in
`artifacts/evidence/TL-0109/`.

- [Powercfg battery report options](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options)
- [Battery information units and unknown cycle count](https://learn.microsoft.com/en-us/windows/win32/power/battery-information-str)
- [ACPI battery capacity and cycle conventions](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/acpi-battery-and-power-subsystem-firmware-implementation)
- [System power status flags](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-system_power_status)
- [Windows job object termination](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)

One machine and represented fixtures do not prove battery health, swelling, endurance,
charging behavior, every manufacturer's firmware, or cross-hardware compatibility.
