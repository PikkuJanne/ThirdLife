# TL-0604 durable backup sample status in the workshop record

The restricted workshop record projects the latest sample status from
the saved job. It distinguishes the current or earlier Handover scope
and leaves backup pending. The report receives only the typed state
and whether its scope is earlier. Sample identifiers, hashes, operator
codes, personal paths, volume labels and recovery material stay outside
this projection.

Generation checks the exact latest sample and saved Handover scope
again before writing. Recording a new sample result clears the open
preview while preserving earlier issued files in protected storage.

On 27 November 2026 the active Windows 11 x64 host ran
`eng/verify.ps1 -Tier Targeted -Restore` from source
`6e087d261c10ff3bc49e7fbeef54acdf9f955005`. The Reports and WPF
selection shared the filter
`WorkshopReportRendererTests|PolicyReportTests|AuditBackupReadinessTests|AuditReportTests`.
The 225.708 s run passed 1,446 Quick, 31 Reports and eight WPF cases.
TRACE generated and opened the saved unavailable result, cleared that
preview on a subsequent matching result, reopened the job and issued
a new report. The preview contained the new pending status and omitted
the exact sample filename and digest. Renderer fixtures exercised
earlier scope, unavailable status and cross-job rejection. The WPF
fixture used read-only native drive discovery and independently prepared
test copies. Actual Windows File History and report transfer were outside
the observation. [Sample continuity](../TL-0702/sample-journal.md)
