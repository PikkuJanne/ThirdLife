# TL-0606 sanitized support archive

The ordinary TRACE Handover surface previews both fixed archive files, exact
ZIP size, the archive digest, and handling guidance. The operator confirms
the preview and supplies their workshop code before choosing a local location.
ThirdLife compares the current normalized job and finalization evidence with
the frozen preview before staging the export. The archive writer validates
the protected `ThirdLifeSupport` folder, capacity and file identity, then
publishes the exact bytes through the existing atomic file operation.

The support projection includes only typed current check results, generic
available hardware facts, application and schema versions, recent bounded
package diagnostic results and an opaque support ID. Neither the workshop
record nor the recipient guide is attached. Raw provider text, identifiers,
personal paths and unreviewed attachments stay outside the archive.

SQLite migration 18 preserves the approved support ID, versions, exact ZIP
digest, time and operator in the restricted job record. It stages an intent
before publication and appends the outcome when known. A confirmed ZIP with a
failed receipt write supports a metadata-only retry while the workspace holds
its actual receipt. A process interruption can leave an unconfirmed intent
instead of a published receipt. The UI shows that state on reopening.

## Verification

The engineering base is `c1f48bfddd003ca678ddbf0df76f03f709417ec6` with
TL-0602 and TL-0603. Verification ran on Windows 11 Pro 26H2 native x64,
build 26300.9974, with .NET SDK 10.0.400 on 27 November 2026.

| Check | Result |
| --- | --- |
| `FullyQualifiedName~SupportExportAuditStoreTests` | Two passed in 1 s of test time. Covered migration, staged and terminal history, exact receipt matching and idempotent completion. |
| `FullyQualifiedName~AuditSupportArchiveTests\|FullyQualifiedName~AuditArtifactScanTests\|FullyQualifiedName~AuditRecipientHandoverTests` | Four passed in 16 s of test time. The ordinary Handover route displayed both exact files, withheld approval initially, rejected a stale review without a write and published an identical ZIP after reapproval. The same job reopened with an operator-attributed receipt. |
| Representative ZIP inspection | The production writer published the [synthetic support sample](synthetic-support.zip), 1,143 bytes, SHA-256 `a0708f181b7bc3086aad8047e696315c8648892a68f87be4894043462c20ad17`. Both archive entries matched their rendered preview. Inspection found the manifest versions, generic hardware buckets, bounded check outcomes and one sanitized package-network diagnostic. |
| Governed Targeted before the wheel fixture correction | Validators, eight work-selection regressions, formatting and the warnings-as-errors Release build passed. Quick reported 166 Core, 1,023 Inventory and 205 UI cases passed, with one failure in the old report-pane wheel fixture. The run stopped at Quick in 260.023 s. |
| Focused Release after the wheel fixture correction | All 20 selected cases passed. Diagnostics 3 in 127 ms, protected Reports storage 10 in 570 ms, export audit Persistence 2 in 1 s and WPF 5 in 15 s. The corrected wheel case opened a real long report before testing its original scroll assertions. |

The UI checks used isolated fixtures and a synthetic private-looking value.
The ZIP sample uses invented machine inputs and a modeled package result.
No external transfer or physical assistive-technology observation was made.

## Composition with recipient guide and restart recovery

The candidate incorporating reviewed main `c06e9e145669a36651550e3ab5bd734ee94f7284`
passed `eng/verify.ps1 -Tier Targeted` in 228.464 s on the active Windows 11
host. The Release build and repository validators passed. All 1,411 Quick
.NET tests and 15 selected diagnostics, reports and persistence tests
passed with zero skips. Refresh now updates recipient guide and support
export state whenever the workspace changes. Clean Full remains the
migration gate for the latest combined source.
