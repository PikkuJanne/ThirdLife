# Windows lifecycle data

TL-0107 established the offline catalog. The [Windows 11 platform amendment](../amendments/2026-11-25-windows-11-only.md)
defines its current scope. Historical observations retain the original catalog provenance.

## Reviewed offline revision

The OS provider uses `WindowsLifecycleCatalog.Current` and the immutable rows in
`src/ThirdLife.Inventory/Windows/WindowsLifecycleData.cs`. It reads no network data,
user-controlled file, registry support label, or policy override to decide lifecycle state.

| Field | Value |
| --- | --- |
| Source identity | `microsoft.windows-client.release-health` |
| Data revision | `thirdlife.windows-lifecycle.2026-11-25.v1` |
| As of | 2026-11-25 |
| Fresh through | 2026-12-25, inclusive |
| Maximum age | 30 calendar days |
| Row limit | 64 (12 rows in this revision) |

The source identity resolves to the Microsoft references below. The data revision identifies
the reviewed interpretation of those sources, including exact edition/build coverage and the
ThirdLife preparation boundary in D-005. The Git revision and evidence artifact digest retain
the exact compiled data. No independent runtime download or executable content is involved.

## Source references

Reviewed on 2026-11-25.

- The release-health table gives exact builds and ISO calendar dates for the General Availability
  Channel. It also identifies release 26H1 as device-specific. [Windows 11 release information](https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information)
- The lifecycle tables identify edition families and historical deadlines. Their Pacific-time
  end timestamps can fall on the next calendar day compared with release-health. [Home and Pro lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/windows-11-home-and-pro) · [Enterprise and Education lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/windows-11-enterprise-and-education)
- The existing native query returns a numeric edition code. It requests only edition metadata. [GetProductInfo](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getproductinfo)

Release-health ISO calendar dates remain the mapping's date basis. This review cross-checked
the retired releases against their lifecycle tables and retained the existing date basis
where Pacific-time timestamps differ. Evaluation uses the provider's injected UTC calendar
date. The listed end date is inclusive. The following UTC date is unsupported. The separate
freshness bound can make a record unknown before an edition's deadline. The historical Windows 11
21H2 availability uses the release-information date, 2021-10-04.

## Covered mappings

| Windows 11 release | Exact build | Home / Pro group end | Enterprise / Education group end |
| --- | ---: | --- | --- |
| 21H2 | 22000 | 2023-10-10 | 2024-10-08 |
| 22H2 | 22621 | 2024-10-08 | 2025-10-14 |
| 23H2 | 22631 | 2025-11-11 | 2026-11-10 |
| 24H2 | 26100 | 2026-10-13 | 2027-10-12 |
| 25H2 | 26200 | 2027-10-12 | 2028-10-10 |
| 26H2 | 26300 | 2028-10-10 | 2029-10-09 |

Build 28000 identifies Windows 11 26H1. The launch check can recognize this client build
on native x64. Its device-specific servicing applicability is outside the reviewed
preparation coverage, so servicing remains unknown. Future and Insider-only builds
absent from the reviewed identity data cannot inherit a release.

| Edition group | Reviewed `GetProductInfo` codes |
| --- | --- |
| Home / Pro | `0x65` Home, `0x62` Home N, `0x63` Home China, `0x64` Home Single Language, `0x30` Pro, `0x31` Pro N, `0xA1` Pro for Workstations, `0xA2` Pro for Workstations N, `0xA4` Pro Education |
| Enterprise / Education | `0x04` Enterprise, `0x1B` Enterprise N, `0x79` Education, `0x7A` Education N |

Windows 10 build identities remain useful for reporting a verified out-of-scope platform.
They have no rows in the live servicing catalog. Prior records may contain `audit_only`
from revision `thirdlife.windows-lifecycle.2026-09-30.v1` or earlier. Those records
keep their original support facts. [Windows 10 release information](https://learn.microsoft.com/en-us/windows/release-health/release-information)

## Failure and freshness behavior

- Only a recognized Windows 11 workstation build and reviewed edition, observed as x64, with current
  coherent data and an unexpired release date can return `supported`.
- A verified Windows Server role, older Windows client, or non-x64 Windows 11 is outside
  platform scope. An ended Windows 11 release in the current snapshot retains its
  `SupportEnded` servicing reason.
- Missing, stale, future-dated, unrecognized, malformed, or contradictory data returns
  `unknown` for servicing. A known Windows 11 family remains available from platform identity
  when the catalog is stale. A missing or conflicting product type cannot establish that identity.
- Source identity, revision, and as-of date must match this reviewed revision exactly.
  Invalid metadata is not republished as validated provenance. Missing rows publish no
  revision metadata. Recognized metadata remains available for stale or invalid row data so
  its limitation can be traced.
- Every build/edition-group pair has one row. Duplicate pairs fail closed, even when their
  values match. All rows for a build must agree on family, release, and availability date.
  Validation covers the whole catalog before lookup and never chooses a conflicting row by
  order. Dates, bounded release tokens, groups, and row count are checked before matching.
- Tests inject a calendar date and an internal catalog to exercise these states. Production
  has no constructor or external data-loading surface for changing the catalog.

LTSC/LTSB, IoT, Enterprise evaluation, SE, multi-session, Insider-only builds, and
other unlisted editions remain outside the reviewed servicing coverage. The edition check
still applies when products share a base build. The lifecycle mapping answers the edition
and release servicing question. The provider leaves cumulative-patch installation,
hardware eligibility, support entitlement, and complete readiness to their respective evidence rules.

## Refresh and historical evidence

After 2026-12-25 this revision produces no supported result. A maintainer must review the
official sources, resolve disagreements, update exact mappings and the revision/as-of
constants together, update this record, and run the date-boundary and captured-data tests.
Changing the freshness or source policy requires the review described in D-072. Unknown
coverage requires a reviewed mapping; an operator cannot override a stale or unknown result.

The provider stores the source, data revision, as-of date, freshness deadline, evaluation date,
and resolved family/release with each observation when available. Refreshing the compiled
catalog affects later observations only. Earlier jobs retain their original evidence and are
not rewritten to use new lifecycle conclusions.
