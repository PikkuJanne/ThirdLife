# TL-0604 technical workshop record

The Handover report action renders a deterministic, printable UTF-8 record
from the saved job. The report schema is `workshop-technical-v3`. It identifies
the renderer build and retains each saved plan's exact plan, impact, catalog,
profile, policy and reviewed-evidence digests. It includes saved plan approvals
with opaque operator codes and UTC times. A linked repair and retest report their
saved assessment and evidence references. The record retains the earlier
decision and its original policy when the saved disposition is historical.

The finalization section reports each current result and any required recheck.
It distinguishes automated observations from operator confirmations, lists
earlier confirmations with their original scope and applicable reason, and
keeps unknown or missing evidence visible. Generation checks the finalization
evidence version and history before and after gathering saved source records.
Saving another confirmation clears the active report preview. Earlier saved
copies remain in the protected report folder.

The added fields come from the existing local job, verified plan journal,
repair journal and finalization projection. They stay in the
`WORKSHOP_RESTRICTED` report within the existing bounded report store. Rendering
uses typed results, a value allowlist and the existing output limit. The report
consumes no raw provider output or recipient information. Its evidence records
the report rendering and saved context. Whole-device qualification and actual
handover sign-off remain separate operations.

## Verification

The engineering base is `be6069969f66b0971fd30454cef74cc9199e912a` (TL-0601).
Verification used Windows 11 Pro 26H2 native x64, build 26300.9974, and the
repository's pinned .NET SDK 10.0.400 on 27 November 2026.

| Check | Result |
| --- | --- |
| Release reports project after locked restore | 39 deterministic tests passed in 396 ms of test time. The technical records preserved the golden dispositions for ready, repair, human review, historical alternative OS and do not deploy. The cases also covered build and plan provenance, hostile text, withheld values, cross-job rejection, and current and historical finalization. |
| Focused Release WPF and SQLite | 10 tests passed in 11 s of test time. The ordinary report route reopened persisted repair and retest records, saved approvals, plan digests and the finalization history. Changing the modeled boot preserved the same confirmation as earlier evidence and returned the checklist item to an unresolved state. |
| Governed Targeted with locked restore | Passed in 267.992 s. The repository and task validators, eight work-selection regressions, formatting and the warnings-as-errors build passed. Quick ran 1,368 deterministic executions. The report selection passed 29 cases and the WPF selection passed 10 more with zero skips. |
| Earlier report and policy selection | Eight tests passed and one active-desktop focus check failed before generating a report. The same report-button focus assertion failed in isolation on the clean `84e47d5` source with unchanged application and test sources relative to `7615a33`. |

The governed command was `eng/verify.ps1 -Tier Targeted -Restore` with
`-TestProject` set to the Reports and UI test projects and this shared filter:

```text
FullyQualifiedName~WorkshopReportRendererTests|FullyQualifiedName~PolicyReportTests|FullyQualifiedName~WorkshopFinalizationReportTests|FullyQualifiedName~StorageReplacementRequiresNewIntakeAndPersistsLinkedRetestAcrossReopen|FullyQualifiedName~EssentialSkipSurvivesReopenAndAppearsInVerifyAndWorkshopReport|FullyQualifiedName~AuditReportTests|FullyQualifiedName~AuditFinalizationTests
```

The selections used isolated jobs and typed fixture inputs. The modeled boot
change did not restart Windows. Test coverage includes the text preview and
stored bytes. Sending a report to an external recipient, physical printing and
perceptual assistive-technology coverage were outside the observed scope.

Integration used TL-0601 main `1fff72535d508102cfa6f47eca54df7cccf30402`.
Production and test sources match the Targeted checkpoint. The combined
continuity Quick passed in 364.706 s with 1,368 .NET cases and eight
work-selection regressions. The desktop suite took 262 s while other suites
ran on the same host. [Integration PR](https://github.com/PikkuJanne/ThirdLife/pull/84)
