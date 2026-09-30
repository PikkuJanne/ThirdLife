# Storage observations — TL-0108

## Scope and ownership

`StorageProvider` supplies read-only observations to the existing inventory runner. It does
not evaluate policy, choose a deployment disposition, diagnose a fault, calculate a health
score, or claim remaining device life. The assessment orchestrator remains TL-0112 work.

Physical-disk evidence belongs to `storage.physical_disk.*`. System-volume evidence belongs
to `storage.system_volume.*`. No disk is inferred to contain Windows, and no health value is
assigned to the system volume. The older `storage.system.*` pilot fixtures remain synthetic
future policy inputs; this provider does not give their ambiguous capacity/health names a new
meaning or change those fixtures.

## Sources and privacy

The Windows source uses three fixed local WMI projections in
`root\Microsoft\Windows\Storage`: physical disks, reliability counters, and their documented
association. Counter ownership is established from that association. A numeric-looking disk
ID alone does not establish a relationship.

WMI object paths and association references are bounded transient routing data. They are
compared against objects read from the local namespace, never interpolated into a query or
used as an executable path. They do not leave the Windows source. Each snapshot sorts its
unique disk routes before assigning ordinals. Duplicate, ambiguous, orphaned or mismatched
associations cannot select a counter by enumeration order.

The provider exposes only fixed keys, numeric values, reviewed enum labels, and a bounded
numeric operational-status list. It requests no serial, friendly name, location, free-form
operational detail, model, manufacturer, user file, or disk content. No arbitrary WMI method,
remote namespace, query, command, path, raw SMART payload or write operation is exposed.

The system-volume source obtains the Windows directory from Windows, resolves its local
fixed volume, and reads free-space metadata. Directory and volume paths stay internal. It
does not enumerate files or create a test file. Network or unsupported volume paths are
unavailable.

There is no provider file, cache, database, report, diagnostic export, telemetry, package,
privilege elevation or permanent service. Normalized observations retain the existing
`WorkshopRestricted` classification. Future report/support projections still need their own
reviewed allowlists; these fields are not automatically added to them.

## Evidence fields

Physical observations use source references `storage.msft.physical_disk.00` through `.03`.
These labels group facts within one snapshot. They are not durable device identities and
must not be used to match disks across observations, reboots or hardware changes.

| Physical-disk suffix | Value and unit |
| --- | --- |
| `capacity_bytes` | Provider-reported physical size, bytes |
| `media_type` | Reviewed HDD, SSD or storage-class-memory label; unspecified stays unavailable |
| `bus_type` | Reviewed bus label, including SATA or NVMe; no media inference from bus |
| `reported_health` | Windows-reported healthy, warning or unhealthy; unknown stays unavailable |
| `operational_status_codes` | Up to 16 numeric codes, preserving source order; no diagnosis |
| `wear` | Consumed wear indicator, `percent_consumed`; 100 is the provider's estimated limit |
| `temperature` | Provider-reported `degrees_celsius` |
| `read_errors_total`, `read_errors_uncorrected` | Independent reported counts |
| `write_errors_total`, `write_errors_uncorrected` | Independent reported counts |
| `power_on_hours` | Provider-reported hours |
| `reliability_collection_status` | Fixed collection-result label |
| `reliability_native_error_code` | Numeric Windows error, when supplied; no exception message |

`storage.physical_disk_count` records a completed bounded enumeration. The accompanying
`storage.physical_disk.collection_status` and `.native_error_code` describe enumeration.
A failed or oversized enumeration cannot claim a complete count or publish its first four
disks as the whole inventory. Independently collected system-volume facts can remain usable.

The system-volume fields are:

- `caller_capacity_bytes`: total bytes available to this caller, potentially quota-limited;
- `caller_free_bytes`: free bytes available to this caller;
- `total_free_bytes`: total free bytes reported for the volume;
- `native_error_code`: a numeric source error when supplied.

All capacity/free-space units are bytes. Caller-free bytes cannot exceed caller capacity or
total-free bytes. Total-free bytes may exceed caller capacity when quotas apply. No ratio,
physical capacity or volume-to-disk mapping is derived from these three values.

## Failure and resource semantics

Null, absent, unsupported, denied, malformed or unrepresentable fields remain not available.
A present numeric zero remains distinct from a missing value. Known warning/unhealthy states,
all bounded operational codes, and error counters are preserved alongside missing facts.
Unknown enum values cannot become healthy. Wear above 100 is preserved within the source's
byte range and is not clamped or converted into an estimated remaining lifespan.

Unsigned values exceeding the existing signed 64-bit evidence range are unavailable as
malformed data. Source failures use typed states and numeric codes; raw exception text is
never a fallback. Native cleanup failure prevents affected WMI facts from being published.

The descriptor fits the existing maximum: four disks times fourteen fields, seven singleton
facts, and one possible provider-failure observation, at most 64 observations. The source
bounds related rows and routing strings. Native WMI allocation and connection latency are
owned by Windows; the managed boundary bounds copying and retained output.

The runner stops waiting after five seconds. A single-flight guard remains occupied until
the underlying source and cleanup return, so retries cannot accumulate workers on the same
provider instance. Cancellation discards late evidence. The future orchestrator must reuse
instances and bound batch concurrency. There is no polling, automatic retry, GPU use or
permanent background indexing.

## Verification and claim limits

Deterministic variants cover HDD, SATA SSD, NVMe, multiple disks, missing counters, permission
failures, malformed inputs, explicit errors, association ambiguity, limits and cancellation.
Active-machine results and resource observations are recorded separately in
`artifacts/evidence/TL-0108/`. A successful collection means the read completed; reported health
and counters are not a hardware, future-reliability, performance or deployment guarantee.
No write test, repair, optimization, counter reset, device stress or second physical machine
is part of this task.

## Primary API references

- [Physical-disk property definitions](https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-physicaldisk)
- [Reliability counter definitions](https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-storagereliabilitycounter)
- [Physical-disk/counter association](https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-physicaldisktostoragereliabilitycounter)
- [Quota-aware volume free-space API](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getdiskfreespaceexw)
- [System Windows directory](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getsystemwindowsdirectoryw)
- [Volume-path resolution](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getvolumepathnamew)
- [WMI numeric representations](https://learn.microsoft.com/en-us/windows/win32/wmisdk/numbers)
- [Bounded WMI enumeration](https://learn.microsoft.com/en-us/windows/win32/api/wbemcli/nf-wbemcli-ienumwbemclassobject-next)
- [WMI connection limits](https://learn.microsoft.com/en-us/windows/win32/api/wbemcli/nf-wbemcli-iwbemlocator-connectserver)
