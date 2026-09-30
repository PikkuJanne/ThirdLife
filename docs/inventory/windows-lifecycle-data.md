# Windows lifecycle data — TL-0107

## Reviewed offline revision

The OS provider uses `WindowsLifecycleCatalog.Current` and the immutable rows in
`src/ThirdLife.Inventory/Windows/WindowsLifecycleData.cs`. It reads no network data,
user-controlled file, registry support label, or policy override to decide lifecycle state.

| Field | Value |
| --- | --- |
| Source identity | `microsoft.windows-client.release-health` |
| Data revision | `thirdlife.windows-lifecycle.2026-09-30.v1` |
| As of | 2026-09-30 |
| Fresh through | 2026-10-30, inclusive |
| Maximum age | 30 calendar days |
| Row limit | 64; this revision contains 40 |

The source identity resolves to the Microsoft references below. The data revision identifies
the reviewed interpretation of those sources, including exact edition/build coverage and the
ThirdLife preparation boundary in D-005. The Git revision and evidence artifact digest retain
the exact compiled data. No independent runtime download or executable content is involved.

## Source references

Reviewed on 2026-09-30:

- [Windows 11 release information](https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information):
  exact build/release identities and ISO calendar dates for general-availability servicing.
- [Windows 11 Home and Pro lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/windows-11-home-and-pro)
  and [Windows 11 Enterprise and Education lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/windows-11-enterprise-and-education):
  edition families and historical end dates.
- [Windows 10 release information](https://learn.microsoft.com/en-us/windows/release-health/release-information):
  exact Windows 10 build/release identities. No Windows 10 support entitlement is inferred.
- [GetProductInfo](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getproductinfo):
  numeric edition constants. These constants identify an edition, not a product key, account,
  licence ownership, activation entitlement, or recipient identity.

The release-information calendar dates are the mapping's date basis. Evaluation uses the
provider's injected UTC calendar date. A listed end date is inclusive; the following UTC date
is unsupported. The separate freshness bound can make a record unknown before that deadline.
The historical Windows 11 21H2 availability uses the release-information date, 2021-10-04.

## Covered mappings

| Windows 11 release | Exact build | Home / Pro group end | Enterprise / Education group end |
| --- | ---: | --- | --- |
| 21H2 | 22000 | 2023-10-10 | 2024-10-08 |
| 22H2 | 22621 | 2024-10-08 | 2025-10-14 |
| 23H2 | 22631 | 2025-11-11 | 2026-11-10 |
| 24H2 | 26100 | 2026-10-13 | 2027-10-12 |
| 25H2 | 26200 | 2027-10-12 | 2028-10-10 |
| 26H2 | 26300 | 2028-10-10 | 2029-10-09 |

26H2 was listed as generally available on 2026-09-29 when the data was reviewed. A larger
build number does not inherit that mapping. Build 28000 / 26H1 is deliberately unrecognized
in this revision because its device-specific applicability needs a separately reviewed
mapping.

| Edition group | Reviewed `GetProductInfo` codes |
| --- | --- |
| Home / Pro | `0x65` Home, `0x62` Home N, `0x63` Home China, `0x64` Home Single Language, `0x30` Pro, `0x31` Pro N, `0xA1` Pro for Workstations, `0xA2` Pro for Workstations N, `0xA4` Pro Education |
| Enterprise / Education | `0x04` Enterprise, `0x1B` Enterprise N, `0x79` Education, `0x7A` Education N |

Windows 10 audit identities are 1507/10240, 1511/10586, 1607/14393, 1703/15063,
1709/16299, 1803/17134, 1809/17763, 1903/18362, 1909/18363, 2004/19041,
20H2/19042, 21H1/19043, 21H2/19044, and 22H2/19045. With a recognized client
edition and current data these return `audit_only`, as required by D-005. ESU enrollment and
activation cannot turn Windows 10 into a supported preparation target. This revision does
not collect ESU enrollment or claim Windows 10 vendor servicing entitlement.

## Failure and freshness behavior

- Only a recognized Windows 11 build and reviewed edition, observed as x64, with current
  coherent data and an unexpired release date can return `supported`.
- Known non-x64 Windows 11 and ended mapped releases return `unsupported`.
- Missing, stale, future-dated, unrecognized, malformed, or contradictory data returns
  `unknown`. Future or unknown builds, unknown editions, and unknown architecture also
  return `unknown`.
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

Windows Server, LTSC/LTSB, IoT, Enterprise evaluation, SE, multi-session, Insider-only
builds, and other unlisted editions remain unknown. Shared base builds do not bypass the
edition check. Servicing-channel, cumulative-patch installation, hardware eligibility,
support entitlement, and complete preparation readiness are not proven by this lifecycle
mapping. In particular, a base build does not establish that every required security update
is installed. The provider retains lifecycle evidence for later policy evaluation; it does
not mark a job ready.

## Refresh and historical evidence

After 2026-10-30 this revision produces no supported result. A maintainer must review the
official sources, resolve disagreements, update exact mappings and the revision/as-of
constants together, update this record, and run the date-boundary and captured-data tests.
Changing the freshness or source policy requires the review described in D-072. Unknown
coverage requires a reviewed mapping; an operator cannot override a stale or unknown result.

The provider stores the source, data revision, as-of date, freshness deadline, evaluation date,
and resolved family/release with each observation when available. Refreshing the compiled
catalog affects later observations only. Earlier jobs retain their original evidence and are
not rewritten to use new lifecycle conclusions.
