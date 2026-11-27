# TL-0703 configuration and ownership checkpoint

**Date** 2026-11-27  
**Source base** `be6069969f66b0971fd30454cef74cc9199e912a`  
**Tested source** `3b5081e9a128e2bc423b69f4d4de0e0817d7e554`  
**Environment** Active Codex Windows 11 host.

The checkpoint maps the registered production roots from their owning source.
The files explain current retention and cleanup and identify the remaining
release-package and job-data deletion evidence. The inventory covered the
production job store, protected reports, diagnostics, battery and package
staging, appearance preference, pinned configuration and broker provisioning.
[Data locations](../../../docs/data-locations.md)
[Retention and cleanup](../../../docs/retention-and-cleanup.md)
[Uninstall and data preservation](../../../docs/uninstall-and-data-preservation.md)

The configuration model returned the exact validated JSON bytes and digests
for Basic, Job Seeker and the reviewed plain-text editor profile. Both
declarative parsers retain the owned byte snapshot that they already validate
and hash. The model rejects a catalogue with a different digest even when
its ID and version match, and rejects a policy outside the profile's exact
reference. The bundled resources supply the policy bytes. The model neither
opens a destination nor activates or imports configuration.
[Export format](../../../docs/profile-policy-export.md)

| Check | Result |
| --- | --- |
| `dotnet test tests/ThirdLife.UI.Tests/ThirdLife.UI.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~WorkshopConfigurationExportTests'` | 4 passed in 380 ms of test execution. Fixed file names, exact digests, schema round trips, synthetic status, and mismatched catalogue and policy rejection. Dependencies were restored in locked mode. |
| Source and document link review | All local links in the four ownership documents resolved. Registered roots and scopes were checked against the production constructors and deployment script. |
| `eng/verify.ps1 -Tier Targeted -Restore` with configuration, policy versioning, plan, app, SQLite path and migration, workshop report store and sanitized log store filters | Passed in 498.112 s. Formatting, locked restore, warning-free Release build and eight work-contract regressions. Quick passed 1,371 .NET cases. The seven targeted selections passed 285 cases. |

The app export writer and destination handling, a release installer file
inventory, and actual uninstall or job deletion qualification remain open.
No application install, uninstall, job deletion, profile adoption, or external
transfer was executed for this checkpoint.
