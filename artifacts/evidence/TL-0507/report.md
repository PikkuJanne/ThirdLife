# TL-0507 final machine profile verification

## Verified workflow

Verify offers **Run final profile verification** at the saved final maintenance boundary. The command queries the configured Windows Update service, collects a separate read-only provider pass and checks the exact reviewed package registration. It leaves the Inspect assessment, candidate policy decision, approval and execution history intact. The exact package plan provides the profile and action scope. The pinned community laptop candidate provides the wider machine baseline. Its candidate status and separate digest remain visible.

The result checks current Windows support and build, native x64, activation, update applicability and restart, security, caller-available space, storage evidence, key devices and active profile capabilities. The artifact links the required capability to its approved action and records resolved and observed versions independently. A mismatch, removal or missing observation holds an essential capability. Candidate rules preserve the difference between an unavailable observation and a known failure. Device presence and manual function remain separately evaluated.

SQLite migration 13 stores a bounded append-only result under the approved plan, approval, maintenance revision, fresh update scan, boot session and package journal. The transaction rechecks the current intake and reviewed evidence count. A new boot, update journal entry, package journal entry, maintenance context or reviewed evidence makes the earlier result historical. Reopening Verify or generating the restricted workshop report shows the saved result and its status. The WPF surface uses native controls, a polite status announcement, keyboard focus restoration and a focusable read-only evidence field.

## Scope and limits

The supported runtime route verifies the currently reviewed one-package workshop profile. Basic and Job Seeker requirements are covered by controlled profile inputs. TL-0508 retains catalog, policy and profile adoption. Cold-boot re-verification remains TL-0509, native update qualification remains TL-0511, and pilot exposure remains TL-0611.

A scan with unsupported offers remains unresolved. A successful empty scan records the configured service response. A clear Windows Update restart flag covers that service. Clearing the aggregate restart rule requires positive evidence for the wider rule. System-volume space uses caller-available bytes. Unmapped physical disk capacity and health cannot settle the policy's system-disk requirements. A missing ownership clearance or current guided review remains a blocking finding. Each retains its own evidence requirement.

The controlled WPF workflow used the ordinary Verify action and a temporary restricted store with controlled adapters. It reopened the exact registered version `8.9.9.1` and detected a modeled downgrade to `8.9.9.0` with a new scan under the same plan and approval. It recorded one original modeled install and one modeled launch probe across both passes. Native provider observations are recorded separately from that workflow. No new package or Windows Update was dispatched for TL-0507.

On the active Windows 11 Pro 26H2 native x64 host, build 26300.9974, the production update agent completed a configured-service search in 37.348 seconds. It returned one restricted offer and three driver offers. Windows Update reported no pending restart. This was an applicability query without servicing. It establishes neither complete update currency nor the wider restart state.

## Data and resources

The local workshop record uses the existing restricted database and report store. It retains the verification times, exact approved action links and versions, policy and profile digests, bounded build/space/update measurements, allowlisted check statuses and policy outcomes. Transient provider values are projected before serialization. Raw provider data, Windows account or device identifiers, secrets, paths and unsolicited network destinations have no new projection. Recipient and support exports are outside this change. The existing job retention policy covers the saved result and rendered report. Schema migration and atomic writes protect earlier records. The new table allows up to 64 results of at most 64 KiB per job within the existing database ceiling. Provider admission and native cleanup use the established inventory runner bounds. Collection is on demand.

## Active-machine checks on 27 November 2026

The base was `43220c173a295ae8ed2a50daff7ed7e344ec8dd9`. Checks ran on Windows 11 Pro 26H2 build 26300.9974, native x64, with .NET SDK 10.0.400 and Python 3.14.7.

| Check | Result and scope |
| --- | --- |
| Baseline `dotnet test tests/ThirdLife.UI.Tests -c Release --filter FullyQualifiedName~FullMaintenanceReviewResumesThroughFinalRestart` | Both existing continuation cases passed in 5 s of test time. The base stopped at the independent verification handoff. |
| Focused Inventory, `FullyQualifiedName~AssessmentOrchestratorTests` | All 49 cases passed in 541 ms of test time. The detached pass detected changed state, preserved Inspect evidence and refused a changed intake. The combined native Inventory `Category=ActiveMachine` run passed 13 cases. A battery test saw 269 process handles against its limit of 256 while sharing the test host. Running `ActiveMachineProductionSourceHasBoundedSanitizedMetricsAndVerifiedCleanup` alone passed in 482 ms, observed 44 handles and verified cleanup. |
| Focused Verification, `dotnet test tests/ThirdLife.Verification.Tests -c Release --no-restore` | All 8 cases passed in 296 ms of test time, including stale evidence, removal, downgrade, low space, missing essential capabilities, pending restart and unsupported offers. The final focused run checked that an offline scan emits no offer count. |
| Focused application, `FullyQualifiedName~FullMaintenanceReviewResumesThroughFinalRestartWithoutReinstalling` | Both WPF cases passed in 7 s of test time. UI Automation invoked Verify, checked the polite focusable status and reopened the exact version and modeled downgrade in the restricted report. |
| Focused store, `FullyQualifiedName~FullMaintenanceReopensAtEveryBoundary` | All five cases passed in 11 s of test time. Coverage includes schema 12 migration at the final boundary, exact phase and approval continuity, duplicate and stale scan refusal, package journal binding and historical status after a later boot. |
| Targeted, including Quick | `eng/verify.ps1 -Tier Targeted -TestProject` with the Verification, Persistence, Reports, Packages and Policy test projects passed in 234.197 s. The format gate, warnings-as-errors build, validators, eight work-contract cases and 1,871 .NET tests passed. |
| Native Windows Update, `FullyQualifiedName~ActiveMachineScansOnlyTheConfiguredWindowsUpdateService` | Passed in 38.922 s. The 37.348 s search result above covers the configured service and active host. |
