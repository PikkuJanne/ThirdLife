# TL-0509 single-guest cold-start verification

## Environment and procedure

The Extended scenario ran on 27 November 2026 in the existing
`ThirdLife-W11` Hyper-V guest on the active Codex machine. The guest ran
Windows 11 Pro 26H2, build 26300.9974, native x64. PowerShell Direct bound
each stage to the same VM and ran the isolated test host under the existing
guest administrator account. The test used native Windows boot and System
event records and the production AuditWorkspace and protected SQLite
paths. Package registration, installer behavior, launch probes, Windows
Update and the separate profile collection used controlled inputs in an
isolated job.

The Windows restarts use the [standing guest restart authority](../../e2e/M4-first-write-slice.md#standing-guest-restart-authorization).
The host checked VM identity, guest quiescence, current Windows boot and
the saved job binding before issuing each request. The warm boundary used
`shutdown.exe /r /t 0`. At the cold boundary the host requested
`shutdown.exe /s /t 0`, observed Hyper-V `Off`, confirmed that state again
after three seconds, and used `Start-VM` for the same guest.

Each test process used the same opaque `THIRDLIFE_COLD_BOOT_RUN` value
(32 hexadecimal digits) and one `THIRDLIFE_COLD_BOOT_STAGE` value, in order
`prepare`, `request`, `same-boot`, `verify`, and `reopen`. Use the first
two invocations on either side of the warm restart. Use the fourth and
fifth after the cold start. Run the named case from a private Release
test output directory with the reviewed .NET SDK 10.0.400:

```powershell
dotnet vstest ThirdLife.UI.Tests.dll '--TestCaseFilter:FullyQualifiedName=ThirdLife.UI.Tests.PackageSetupTests.GuestColdBootReopensTheApprovedJobAndRunsFreshVerification'
```

The case makes only fixture writes. The independently authorized host
steps perform the power transitions and collect bounded evidence. Keep
the job, plan, approval, checkpoint and VM binding in private run records.

## Tested candidate

The warnings-as-errors Release UI build used base
`891936e20eebacf5ca59248299b5420e018c8c7e` and source tree
`f6a5830f1e2cf21ac5ca66acbe025ed8d73fdeb8`. The SHA-256 of the staged
Release payload was
`bcb8894851336696e326cfb5d74f7ab15d6c484c90854d8f65d32f331cc83ef9`.
The host and guest verified the archive before execution. The private
SDK matched the SHA-512 stated in `eng/thirdlife-hyperv-profile.json`.

| Release assembly | SHA-256 |
| --- | --- |
| ThirdLife.UI.dll | `d3648e6fd989709c3f6646c152a2138fb01bf7ac89bfdda47ea121a3d0943431` |
| ThirdLife.Persistence.dll | `b760e161a609b7c395233be0340150c57b5bf3c0c89f3bbf2e300ca9c70fbc92` |
| ThirdLife.Verification.dll | `0010abc517c9dfc0d21e59eaf60874be51d2c86bb63d0f58bdb850a114291c62` |
| ThirdLife.Inventory.dll | `f9c24ab264ba8a406ebe811716fc4244f8f4d1a90db0fcf0977c1f4abac08844` |
| ThirdLife.Reports.dll | `51e7f9ec1fde9e9da4c1c56507982f9646da0ebdcdbf72769a170d385d0fbe56` |
| ThirdLife.UI.Tests.dll | `85eda7becbffd9645c442beb787fee5b10a9092ea606081ddc6934549de97ab7` |

## Observations

| Stage | Result |
| --- | --- |
| Prepare, boot 7, 12.967 s test time | The ordinary package and maintenance workflow saved the approved job at its final restart boundary. One modeled installation and its launch probe completed. |
| Warm restart, boot 7 to 8, 41.217 s host time | User32 event 1074 identified an orderly restart. Kernel-Power event 109 reported reset action 5. The saved job remained available on the next Windows boot. |
| Request, boot 8, 1.224 s test time | The same job advanced through the final maintenance restart. Verify saved a cold-boot checkpoint under the same plan and approval. |
| Reopen on boot 8, 0.800 s test time | A new process recovered the exact checkpoint and still held post-boot verification at the shutdown requirement. |
| Cold start, boot 8 to 9, 53.466 s host time | User32 event 1074 recorded shutdown. The host observed Hyper-V `Off` at 13:43:50.368 UTC, then requested power-on at 13:43:53.386 UTC. |
| Verify, boot 9, 1.409 s test time | The native Windows evidence admitted the saved checkpoint. The application made one fresh update scan and one fresh modeled launch probe, and saved the exact package version and the blocked profile outcome under the original approval and checkpoint. |
| Reopen on boot 9, 1.166 s test time | A separate process reopened the current post-boot record and generated the restricted workshop report with the exact version. Reopening performed zero scans, launch probes, acquisitions or installs. |

On the cold transition, Kernel-Power event 109 reported power action 4.
Kernel-General event 13 recorded shutdown before event 12 recorded the
later startup. Kernel-Boot event 20 reported a successful last shutdown
and prior boot counter 8. Kernel-Boot event 27 reported full-boot type 0.
Win32_OperatingSystem reported the new Windows boot at 13:43:58.057 UTC.
The process-independent job and checkpoint bindings matched in all
respective phases. Each of the five exact test invocations passed one case.

The warm transition had ordered event record IDs, but Windows stamped the
Kernel-General startup 1.252 seconds earlier than shutdown. That time
ordering would leave a cold-boot request unqualified. The warm test
stage preceded the cold-boot request.

The host removed and verified the absence of the guest test payload,
private SDK and isolated fixture store after retaining the results. The
same VM remained running. The run used the existing guest state and
performed no checkpoint creation or restoration.

The preserved private host receipt set is about 16 KB and includes the
hash-verified source delta from the implementation checkpoint. The task's
host temporary staging directory, `%TEMP%\ThirdLife-TL0509-Host`, retains
about 8.7 MB of archived payloads and run copies after the execution
policy rejected the host cleanup command.

## Scope

This verifies an actual same-machine VM cold start and the application's
fresh verification and saved continuity. The controlled profile deliberately
left readiness blocked. The recorded package version and launch probe
come from the controlled adapter. Native package servicing, native Windows
Update and whole-device clearance have separate evidence requirements.
Physical host power-cycle evidence remains unobserved. D-033 still requires
the actual cold boot for final product acceptance. Retain the active-machine
cold-boot and continuity qualification at the applicable stable release gate.
