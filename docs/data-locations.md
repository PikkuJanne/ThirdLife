# Standalone data locations

The current application uses the signed-in workshop account's local Windows data.
The paths below are relative to Windows-known folders and contain no donor or
recipient identity in their filenames. The installer and release checks for
additional paths are tracked in TL-0704.

| Owner and classification | Current location | Content and scope |
| --- | --- | --- |
| Workshop settings | `HKCU\Software\ThirdLife\SetupCore` | The `Appearance` value stores the selected light or dark appearance. Windows high contrast still takes precedence when active. [Appearance setting](../src/ThirdLife.UI/Themes/TraceAppearance.cs) |
| Reviewed configuration | Embedded application resources | Hash-pinned profile, policy, catalogue and servicing-policy JSON. Profiles and catalogues carry the exact validated bytes. Policy evaluation and preparation retain the applicable policy and plan versions. [Configuration export model](profile-policy-export.md) |
| Exported configuration | `ThirdLifeConfiguration` beneath the chosen local folder | User-owned ZIP with the three validated configuration documents. The publisher stages a restricted `.configuration-<token>.tmp` file there for atomic publication. [Configuration export](profile-policy-export.md) |
| Workshop-restricted job history | `%LOCALAPPDATA%\ThirdLife\SetupCore\JobStore` | Protected `thirdlife-jobs.sqlite3` and its paired SQLite rollback journal `thirdlife-jobs.sqlite3-journal`. The tables cover schema history, jobs, bounded typed evidence, archive and sanitization checkpoints, policy snapshots/decisions, plans and approvals, action/update/restart/maintenance journals, independent verification and finalization confirmations. [Store and migrations](../src/ThirdLife.Persistence/Migrations) |
| Job file scope | `JobStore\jobs\j-<sha256>` under the same root | Protected directory derived from the opaque job ID. Normal inventory and verification write typed rows. General raw-provider attachment storage is still pending. [Store layout](../src/ThirdLife.Persistence/RestrictedStoreLayout.cs) |
| Job store initialization/recovery residue | `.thirdlife-jobs.sqlite3-initialize-<token>.tmp` and its paired journal under the job-store root | Private staged SQLite initialization. Opening a store reconciles verified initialization artifacts and rejects unsafe or corrupt state. This stage is separate from a recovery backup. [Store implementation](../src/ThirdLife.Persistence/SqliteJobStore.cs) |
| Workshop-restricted reports | `%LOCALAPPDATA%\ThirdLife\SetupCore\WorkshopReports` | Bounded UTF-8 text copies `workshop-<token>.txt`, private `.workshop-<token>.tmp` staging and `.workshop-write.lock`. Only a store-issued artifact opens through the current report view. [Report store](../src/ThirdLife.Reports/WorkshopReport/WorkshopReportStore.cs) |
| Sanitized operational diagnostics | `%LOCALAPPDATA%\ThirdLife\SetupCore\Logs` | Schema-validated `evt-*.json` records, `.tmp-*.json` and `.txn-*.json` transaction artifacts and `.diagnostic-store.lock`. The production package diagnostics entry point uses a 1 MiB byte limit and the default 14-day limit. [Log store](../src/ThirdLife.Diagnostics/Logging/SanitizedLogStore.cs) [Production settings](../src/ThirdLife.UI/Features/PlanReview/AuditWorkspace.PackageDiagnostics.cs) |
| Battery inventory temporary files | `%LOCALAPPDATA%\ThirdLife\SetupCore\BatteryReports` | The controlled battery report route uses a generated `report-<token>.xml`, a binary ownership marker `pending.bin` and `.lock`. It parses bounded data and cleans the verified files within the guarded workspace. [Battery report workspace](../src/ThirdLife.Inventory/Windows/BatteryReportWorkspace.cs) |
| Temporary package staging | `%LOCALAPPDATA%\ThirdLife-package-<token>` | One private directory per reviewed package operation, including the exact staged installer. The runtime removes its owned file when allowed and deletes the directory only when empty. Long-lived installer cache management remains pending. [Package staging](../src/ThirdLife.Packages/PackageStagingDirectory.cs) |
| Ephemeral broker deployment | `%ProgramFiles%\ThirdLife\Broker` and temporary sibling `.broker-stage-<token>` | Protected `ThirdLife.Broker.exe` and `broker.sha256`. The engineering provisioner verifies identity and ACL before publication. The broker handles approved typed requests and correlated results. [Broker provisioner](../eng/install-thirdlife-broker.ps1) |

Windows Update's native cache and installed vendor packages remain Windows/vendor
lifecycle data. Working copies that an operator exports or moves to another
folder follow that destination's access and retention arrangements. The release
inventory must also account for the recipient guide and previewed support archive
once their writer paths are integrated. [Privacy model](privacy/privacy-model.md)

The application uses its registered paths and job identifiers. It has no shared
portfolio identity or private workspace, repository, recording, content or key
store belonging to another application. [Project boundary](../PROJECT_BOUNDARY.md)
