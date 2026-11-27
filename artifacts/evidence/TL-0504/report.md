# TL-0504 — Structured Windows Update

## Application and persisted evidence

In Prepare, an operator with an open job and a current allowing intake decision can select **Scan for Windows updates**. The unelevated application asks Windows Update Agent (WUA) to search the service configured by Windows for applicable, nonhidden installation offers. It disables automatic WUA self-upgrade on the searcher. A successful response includes the current restart flag and up to 64 distinct update IDs with revision, classification and maximum download bytes where available. The scan can be cancelled from the keyboard. Search does not start at application startup or when a job is reopened. [WUA search criteria](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iupdatesearcher-search) · [Searcher properties](https://learn.microsoft.com/en-us/windows/win32/wua_sdk/iupdatesearcher-properties)

Prepare shows the time and result of the last attempt and the last successfully collected inventory. Verify opens the saved result for the same job. A failed or cancelled scan preserves the prior inventory and its timestamp. An empty successful scan establishes only what that service offered at that time. A missing receipt, partial result or unavailable restart observation cannot establish that Windows is current.

SQLite migration 007 adds a journal to existing jobs. Each entry binds the job, intake decision, action or predecessor, time and typed evidence. The store enforces the transitions in a transaction and checks record hashes when reopening. Each job admits 128 entries of at most 32 KiB each. Only bounded identity and status fields enter the protected store. Update titles, URLs, localized messages and raw WUA errors are excluded. Earlier schema readers refuse a newer store. Reopening preserves existing jobs and their original policy and action history.

## Download, installation and restart

The compiled actions identify a single update by GUID, revision, class and maximum download size. The controlled workflow requires a ready, current, update-specific fixture policy decision, exact scan, operator approval and passing resource checks before dispatch. It rechecks the offer separately before download and install, records both intents before calling the adapter and saves the outcomes, including reported progress and reboot status. An overlapping read-only scan cannot authorize a second dispatch or erase the first operation's receipt.

After a successful install receipt the workflow rescans applicability and opens a separate WUA session to query the exact installed GUID and revision. It records the observation against that receipt. An empty, mismatching, partial or unavailable installed-state result leaves the action in review. A later read-only scan under the same intake can finish this check. Verify shows the exact revision, observation and time. A successful install followed by another offer of that very revision also requires review.

Each further pass needs a fresh exact approval. The workflow stops after three download attempts per job, including failed attempts. A restart checkpoint needs a successful scan with Windows reporting no pending reboot and a lower observed Windows kernel uptime than the saved baseline. A late reopen with a longer uptime can leave the restart unconfirmed. A missing or failed installation receipt keeps the action in review. Whole-device cold-boot acceptance remains separate.

The prepared WUA writer checks the Windows-configured service and permits only an unambiguous built-in Windows Update authority for mutation. It re-selects the exact offered object and compares its revision, class and size, restricts the handler to Windows component-based servicing, and rejects drivers, bundles, interactive updates and unaccepted licence terms. Source checks around lookup and preparation leave machine settings intact. The public production agent exposes read-only scanning and returns unavailable from download and install. Protocol v1 rejects all update actions. [WUA handler identity](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iupdate-get_handlerid) · [WUA permissions](https://learn.microsoft.com/en-us/windows/win32/wua_sdk/secured-methods-and-properties-in-the-wua-api) · [Current broker protocol](../../../docs/security/broker-protocol.md)

## Verification

The base was `1b1c7d89688bfeae9e8fbe9a6ed3cae1b96d8d7c` on `origin/main`. The active machine ran Windows 11 Pro 26H2 build 26300.9974, native x64, .NET SDK 10.0.400 and Python 3.14.7. The initial clean worktree lacked PyYAML. Installing the hash-pinned dependency in its local environment enabled the baseline. On 2026-11-27, `eng/verify.ps1 -Tier Quick -Restore` passed all 1,242 selected tests in 146.487 s. Its cold Release build took 27.78 s and WPF tests about one minute. The warm Quick target is 120 s.

Two focused WPF regressions exercised intake refusal, execution from Prepare, keyboard focus, UI Automation activation, the polite status region, cancellation, Verify, and reopening the same SQLite job after an offline scan. When switching stages the harness originally asserted focus or visibility before the WPF layout pass. Draining the dispatcher after navigation made the assertions observe the rendered controls. Both cases passed in the focused run. The command was `dotnet test tests/ThirdLife.UI.Tests -c Release --no-restore --filter FullyQualifiedName~WindowsUpdateWorkspaceTests`.

The SQLite integration cases use isolated job stores, an explicitly synthetic update policy, controlled providers and boot observations. They cover empty and nonempty scans, service failure, partial or failed mutations, exact-scope drift, interrupted migration and operations, overlapping connections, restart and the three-pass limit. Separate fixtures exercise the WUA automation object's closed selection and job/result handling. The initial Targeted run passed 1,833 cases in 188.392 s at the first implementation checkpoint `c072d9b4e86a26f45713ab97f72e958badd6d556`. The command was `eng/verify.ps1 -Tier Targeted` with the Actions, Broker, Broker.SecurityTests, Packages and Persistence test projects. The SQL resource was normalized to its repository LF form for the clean build.

The host's WUA API reported 8.0. Its agent product and wuapi.dll both reported version 1511.2611.5012.0. An unelevated online scan with the production adapter took 15.154 s. Windows reported three driver offers, one restricted software offer and no pending restart. None qualified for the prepared writer. A separate exact installed-state search confirmed a previously installed revision in 7.940 s. The host test selected its input from at most eight history entries and independently queried the actual installed state. ThirdLife did not install that update. The native cases can be run with `dotnet test tests/ThirdLife.Packages.Tests -c Release --no-build --no-restore --filter "FullyQualifiedName~WindowsUpdateAgentTests&Category=ActiveMachine" --logger "console;verbosity=detailed"`.

The technical candidate was `ac3c96f1b89169ce8fffb0d4dfefde17b01ae369`. Targeted with `-TestProject` set to Packages and Persistence and `-Filter "FullyQualifiedName~WindowsUpdate&Category!=ActiveMachine"` passed all 1,298 cases in 132.687 s, including Quick, 25 adapter cases and 20 store/workflow cases. After the compiled action descriptions were tightened to name the exact downloaded or installed revision check, all 38 Actions registry cases passed. Repeating both host native cases found the same four offers, with the scan taking 6.047 s and the positive installed-only lookup taking 11.062 s. The test host reported 17.962 s.

Candidate Quick passed 1,253 cases in 146.135 s while clean Full and guest qualification were running. WPF took 78 s compared with 61 s in the first Targeted run. The separate unelevated `eng/run-ui-smoke.ps1` reached Inspect using UI Automation and closed the application normally in 1.415 s. The Release apphost SHA-256 was `e7ef49604bc273d1078145dd39ce2fee649cfe8fe710d1293f09b38fcea26a05`. The WPF assembly SHA-256 was `51a99e46ebe87ce059068fff86417c0f4decce3828305856955d8b484909898a`. These checks cover technical accessibility and startup in the exercised paths.

A clean detached checkout of `ac3c96f1b89169ce8fffb0d4dfefde17b01ae369` passed `eng/verify.ps1 -Tier Full -Restore` in 433.080 s. All 304 governance tests and 2,317 deterministic .NET tests passed, with zero skips. Its working tree stayed clean. The clean Release adapter had SHA-256 `acffff32a9bfe5b00843ee15b0d211361b6094e4fe1b6cb3bceb4817736856a5`. Running the two native cases with that artifact again confirmed the previously installed revision in 22.832 s and the same four nonhidden offers in 7.375 s. Interruption and restart coverage for ThirdLife-initiated update work used fixtures.

### Same-machine guest

The already running Generation 2 Windows 11 x64 guest on build 26300.9974 ran `ActiveMachineScansOnlyTheConfiguredWindowsUpdateService` under an interactive limited token. Its wuapi.dll reported version 1511.2611.5012.0. The candidate Release adapter and test assembly were staged with .NET SDK 10.0.400 under a single owned directory after validating the guest, checkpoint and payload hashes. The configured service offered one restricted software update, no eligible update and no pending restart. The online scan took 36.745 s. The test took 36.778 s and staging through cleanup took 167.327 s. The temporary task, staging and owned processes were confirmed absent. The guest stayed running throughout.

The in-guest command below uses the staged SDK, test DLL and owned results directory. The harness required one passing named test in its TRX and removed that file along with the temporary guest and host staging. The observed guest platform facts cover client product type, native x64, OS build and WUA file version. Edition, display release and API major/minor were not collected in the guest.

```powershell
$case = 'ThirdLife.Packages.Tests.WindowsUpdateAgentTests.ActiveMachineScansOnlyTheConfiguredWindowsUpdateService'
& $sdk vstest $test "/TestCaseFilter:FullyQualifiedName=$case" '/Logger:trx;LogFileName=scan.trx' "/ResultsDirectory:$results" -- RunConfiguration.TreatNoTestsAsError=true
```

| Guest workload | SHA-256 |
| --- | --- |
| ThirdLife.Packages.dll | `f8cb090083e4c37aea4d2df06b06b9ca298cfc5a4ca4918b68fdca1c14ec31c0` |
| ThirdLife.Packages.Tests.dll | `7ac7daf8c43d4cb80f4a1a4a2fac63dfd0b077eb15fabbce01828415fbc869b0` |
| Staged test payload | `40df4f41fa7c79e420a662e57c6a006801d2fd2586281aa3452c1a10ad076965` |
| Limited-token test driver | `fa9e2f403de0ac7e6aa80c1fbd48f7457c4ed4b1aa4a88d9978203e899e2298d` |

### Supplementary check

The first completed hosted Quick run timed out in the existing inventory test `ObserveAsyncYieldsBeforeABlockingSourceCompletes` while waiting one second for a worker to start. The product provider and its collection deadline were unchanged. The test now allows ten seconds for worker scheduling, keeps its release gate closed until the assertion proves the caller has yielded, and bounds the fixture's own blocking wait. The 28 neighboring deterministic inventory provider cases passed in 3 s with `DOTNET_PROCESSOR_COUNT=2`. The command was `dotnet test tests/ThirdLife.Inventory.Tests -c Release --no-restore --filter "FullyQualifiedName~SystemInventoryProviderTests&Category!=ActiveMachine"`. [Initial hosted result](https://github.com/PikkuJanne/ThirdLife/actions/runs/45966672339)

The evidence checkpoint passed `eng/verify.ps1 -Tier Targeted` with the Inventory project and the same provider filter in 118.260 s. All 1,281 cases passed, including the complete Quick selection and the 28 focused cases. This checkpoint changed the inventory test and evidence only. Its product source matches the clean Full candidate.

## Remaining native scope

TL-0504 remains open for native download, installation, restart and rescan qualification. The current privilege boundary covers no-op broker actions and the reviewed package policy covers one MSI. Neither authorizes Windows servicing. Prepare the WUA-specific authenticated broker and effective policy for the intended Windows environment, including source, firmware exclusion, servicing eligibility, disk and rollback headroom, power, and independent verification. Qualify the composed route in the same-machine guest and obtain the applicable privilege decision before enabling ordinary dispatch. [Broker qualification](../TL-0317/report.md) · [Privilege and application gates](../../../TASKS.yaml)

For a live qualification run, use a suitable offered security or critical CBS update, save the exact action approval and use the applicable Windows consent prompt. Record native download, per-update installation status, any actual restart and the fresh scan under the same job. Previous package or no-op approvals do not extend to Windows Update. No host update installation or restart was requested during this checkpoint.

## Exact offer review in Prepare

After a successful scan, select an update in Prepare and choose **Check selected offer online**. The application reopens the current intake and saved scan before and after asking the configured service for that exact revision. A changed offer, later scan, intake change or cancellation invalidates the result. Reopening the job restores the saved inventory and leaves a fresh network check to the operator. The transient check creates no new approval or journal entry.

The review reports the first eligibility condition observed, pending restart, current servicing support, power and caller-available space at the Windows component store and download cache. A selectable offer also shows what the compiled download and installation would change and how the installed revision would be verified. The check accounts for the transfer and installation recommendation together when both locations share a volume. An absent recommendation is shown as unknown. A rollback reserve still needs its own basis. [WUA installation-space recommendation](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iupdate-get_recommendedharddiskspace)

Prepare also shows whether WUA reports that this exact revision supports uninstall. Windows can omit either that property or its disk recommendation. The other observations remain visible when either is unavailable. Establishing a recovery procedure and measuring its required headroom remain separate qualification steps. [WUA uninstall support](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iupdate-get_isuninstallable)

The read-only lookup and prepared writer inspect the configured service registration through the same WUA session used for the lookup. The fixed Windows interfaces can also run under Native AOT. The protected broker continues to implement its previously qualified no-op protocol. An authenticated servicing request, independent approval and intent validation, update-specific effective policy and qualified rollback reserve remain engineering prerequisites for a live transaction.

### Repeating the guest assessment

From a clean committed checkout on the active machine, use the shared Hyper-V runner. Inspect returns the identity of the configured running Generation 2 guest. Prepare builds the Packages tests with the locked inputs and verifies the staged SDK. The assessment runs a single read-only test under the interactive limited token. Its local result records bounded offer counts, lookup status, eligibility reason, artifact digest, version and cleanup. It requires no broker provisioning or servicing consent.

```powershell
$guest = ./eng/run-thirdlife-hyperv.ps1 -Scenario Inspect
$candidate = ./eng/run-thirdlife-hyperv.ps1 -Prepare -WindowsUpdateAssessment | Select-Object -Last 1
./eng/run-thirdlife-hyperv.ps1 -Scenario WindowsUpdateAssessment -ExpectedVMId $guest.VMId -PayloadSha256 $candidate.PayloadSha256
```

### Observations on 2026-11-27

The offer-review checkpoint was `0bbbe8a6ade0f7c8c8fd3c2a005c859b65bab87a`, based on merged PR 73 at `52a12906c1b07ddc1587bd07598f4dfecd8d0e5a`. The initial locked baseline passed 25 WUA adapter cases. The initial Targeted checks found incorrect line endings in new C# files and a public init setter on the inventory resource result. The result now exposes get-only observations, and the existing read-only provider contract regression passed. Targeted with Packages and Persistence and filter `FullyQualifiedName~WindowsUpdate&Category!=ActiveMachine` then passed 1,308 tests in 138.756 s, including Quick. The six focused WPF/SQLite cases covered keyboard operation, cancellation, invalidation and reopening using isolated synthetic jobs. The unelevated shell reached Inspect through UI Automation and exited normally in 1.355 s. These WPF cases and the native service observations used separate entry points.

| Native environment | Exact offer check |
| --- | --- |
| Host, Windows 11 Pro 26H2 x64 build 26300.9974 | Four offers, comprising three drivers and one restricted software update. The exact software revision used an unsupported handler. Servicing and power checks passed. Windows reported no pending restart. Both fixed locations had observable space and shared a volume. WUA supplied no installation-space recommendation. The exact lookup took 7.997 s. |
| Same-machine Generation 2 guest, Windows 11 x64 build 26300.9974 | One offer, zero selectable. The fresh limited-token assessment confirmed an unsupported handler. The source-bound run took 104.127 s including staging and verified cleanup. The collected guest result contains the selection counts and eligibility reason. |

Both machines reported wuapi.dll `1511.2611.5012.0`. On the host, the three `WindowsUpdateAgentTests` in the `ActiveMachine` category passed in 1.7728 minutes, including an independent positive query for a previously installed revision. The guest payload SHA-256 was `063668e23dc124c63dcc5eee1e787c48c8b266c9c7fcfcb10a570968c9858c07`, with adapter SHA-256 `77d387dfff35820f36184698203869bf4f6241c088a8e741d42eebc96d2c383b`. Neither environment presented an eligible servicing transaction.

A temporary unelevated Windows x64 Native AOT probe compiled the fixed service-registration reader with the broker's 10.0.14 AOT toolchain on SDK 10.0.400. It activated an in-process WUA session, checked that same session four times and released the acquired interfaces. Four reads identified the built-in authority in 0.132 s. Independent WUA Automation enumeration agreed. The probe image SHA-256 was `dcf14c5c454b7dcd80870fde9a7c97e2f1b5d519173c162dcdc070cb7d1dc0b7`. This exercised service identification. It used no privileged broker session and performed no update operation.

## Approval and phase handoff

Under the controlled policy, an approval now records two distinct action IDs, one for download and one for installation. It binds those IDs and their compiled impacts to the originating approval, job, intake, scan, policy decision and exact revision. Prepare uses the same compiled impact descriptions. Scanning requires the standard WUA agent. The download and installation descriptors each require a qualified servicing broker.

SQLite migrations 008 and 009 version the stored approvals and phase outcomes. Existing payloads and hashes remain intact. Earlier approvals remain readable. Execution requires fresh review for an approval that predates phase binding. A reader that supports only an earlier schema refuses to open the upgraded store.

The controlled channel can wait for consent during setup. Before handoff, the coordinator checks the exact fresh offer, resources, current intake and policy, phase identity, deadline and journal state. A receipt must match the dispatched phase to be attributable. The production WUA mutation methods remain unavailable. No controlled result establishes native installation or independent broker authorization.

Channel setup can end before an action is sent. The store records that outcome against its own preparing entry. A fresh scan can also settle an interrupted setup atomically, allowing a new review. A committed send intent with a missing or mismatching receipt still requires workshop review after reopening. A backwards wall-clock adjustment preserves the actual observed time of a correlated receipt. Journal sequence records its order.

On 2026-11-27 the intent-binding baseline on the active machine passed 20 SQLite workflow cases in 7 s. At `ce85b22063b48aae9e557dc4dd27efaefc481623`, the same filtered test project passed 30 cases in 10 s. Quick with locked restore passed 1,253 tests in 129.420 s. Review identified that a refused pre-send installation could prevent a fresh approval, and that a deadline or clock change during setup could misstate the result. At `ffee8c6ff03e1d836c7c15c35196d408786b4c86`, the filtered suite passed 36 cases in 13 s. The cases used real SQLite, controlled channels, clocks and update offers. They exercised concurrent decisions, interrupted setup and handoff, migration, replay refusal, receipt mismatch and reopening. After correcting a formatting failure, Quick with locked restore passed 1,253 tests in 122.948 s. The warm Quick target is 120 s.

### Integrated verification on 2026-11-27

The integrated technical source is `710e5a867d15905c471bd8658539be94f6950f8a`. On the active Windows 11 Pro x64 host, the nine focused WPF cases passed in 4.790 s. The integrated Targeted run passed 1,406 cases in 142.383 s with zero skips. It included Quick, 38 action registry cases, 31 WUA adapter cases and 75 persistence cases.

```powershell
$projects = 'tests/ThirdLife.Actions.Tests/ThirdLife.Actions.Tests.csproj',
    'tests/ThirdLife.Packages.Tests/ThirdLife.Packages.Tests.csproj',
    'tests/ThirdLife.Persistence.Tests/ThirdLife.Persistence.Tests.csproj'
./eng/verify.ps1 -Tier Targeted -TestProject $projects -Filter '(FullyQualifiedName~WindowsUpdate|FullyQualifiedName~ActionRegistryTests|FullyQualifiedName~MigrationAndCorruptionTests)&Category!=ActiveMachine&Category!=Extended&Category!=SandboxOnly'
```

The separate Extended check terminated owned WPF instances after dispatch intent and reopened the same jobs on schema 009. Both existing `KilledNoOpApplicationReopensTheSameJobAndReconcilesWithoutDispatch` cases passed in 11.601 s. They cover the neighbouring no-op recovery workflow using its controlled backend. WUA-specific interruption and late-result checks used controlled channels and real SQLite. The unelevated `eng/run-ui-smoke.ps1` reached Inspect through UI Automation and exited normally in 1.461 s. Its Release apphost SHA-256 was `f0edbd0344ebf5ec4bc981caac8359d9deb10fde174639dc1b9d6422e4c89868`.

The integrated host run passed all three native WUA cases in 1.2603 minutes. The exact restricted offer recheck took 8.734 s. Windows reported an unsupported installer, uninstall unavailable, no disk recommendation and no pending restart. The separate lookup confirmed a previously installed revision in 7.531 s. The host adapter SHA-256 was `e921dd96ec143c890a6d4b49b2b5036274c3d8c2158bf24976376abf72546bfd`.

The limited-token guest repeated the assessment from the integrated commit. Its single offer still had an unsupported handler, and WUA reported that the revision did not support uninstall. The run took 169.075 s including staging and verified cleanup. Its payload SHA-256 was `f2586854d68549d6d345a154d82f66dbaa49be1a7c122971ff342e48760e3e6d` and adapter SHA-256 was `3683523d1d83ba9f994695344f587dda7c51c6333e540baf5f04ecfcceae8b3d`. Both native environments still reported Windows build 26300.9974 and wuapi.dll 1511.2611.5012.0.

In a clean detached checkout of `710e5a867d15905c471bd8658539be94f6950f8a`, `eng/verify.ps1 -Tier Full -Restore` passed in 411.907 s. All 304 governance tests and 2,348 deterministic .NET tests passed, with zero skips. Locked restore, formatting and the warnings-as-errors Release build passed. The clean Release adapter had SHA-256 `78496902687eafaa96387a7e615d15718af64bd54143095383742cd4f12c9b70`. Its separate native exact-offer test passed in 25.022 s, again finding four offers, zero selectable revisions and an unsupported handler with uninstall unavailable. The worktree remained clean.

The final evidence checkpoint passed `eng/verify.ps1 -Tier Quick` with 1,262 .NET cases in 124.506 s, 4.506 s above the warm target. The Release build took 7.80 s. WPF took 65 s, compared with 61 s on the isolated phase-binding checkpoint and 62 s in integrated Targeted. The technical source and tests match the clean Full candidate. [Integration PR 74](https://github.com/PikkuJanne/ThirdLife/pull/74)

### Remaining work at PR 74

Implement the protected Windows Update request and native handler for the approved phase and qualify their independent authorization checks. Bind the effective servicing policy to a supported recovery procedure and its disk requirements. Reopen native transaction qualification when the guest offers an eligible security or critical CBS revision. Obtain approval for that exact planned action and applicable Windows consent, record the per-update download and installation receipts, then rescan and independently verify the installed revision after any required restart. TL-0314 governs ordinary privileged dispatch. The offer checks and technical tests confer no servicing authority.

## Protected Windows Update servicing

The v2 request carries the exact revision, class, transfer ceiling, both compiled phase descriptors, job, intake, scan, policy and approval. The standard-user application records approval and can reopen it before an operator explicitly starts servicing. At handoff, SQLite checks current evidence and policy, including evidence added with an earlier timestamp. Migration 010 retains earlier rows and hashes. The elevated process rechecks the authenticated peer, approved scope and Windows state. A confirmed pre-Begin refusal, Windows consent decline, an interrupted handoff and an independently verified installed revision have distinct durable outcomes.

The broker obtains its own configured WUA session, holds the exact matched CBS offer, checks the current Windows target, reboot, power and storage, and uses a separately authorized grant for each phase. The compiled policy binds a reviewed recovery procedure, working space, rollback reserve, organization policy and qualification authority. The active review catalog is empty. The fixed native call is reachable only after those checks pass. [Protocol and supervised qualification](../../../docs/security/windows-update-broker.md)

The positive operator workflow, phase transitions, cancellation and restart reconciliation used controlled providers. The negative provider cases used Windows itself. The same-machine guest and the client image in its available installation media have the same Windows build and revision. No new baseline was installed.

On the active Windows 11 Pro native x64 host (build 26300.9974, wuapi.dll 1511.2611.5012.0, .NET SDK 10.0.400), the integrated technical source was `5e8beed49977ba3e80af7ada63260b3e5211ea7a`, based on `b893288340fd274e827a86ce3fe8f20cf498393f`. The original focused WPF baseline passed nine tests. During iteration, the real SQLite workflow exposed a difference between the evidence used by rules and the complete stored assessment. The corrected store binds the full append-only evidence count and rejects a stale policy write when another connection inserts backdated evidence. Quick detected that the Windows build revision reader was missing from the inventory provider’s list of reviewed native imports. The import is now a nonpublic, System32-only OS-provider read and both exact import checks pass.

| Check | Observed result |
| --- | --- |
| Integrated Targeted (includes Quick) | 1,704 test executions, zero skips, 222.617 s. Locked dependencies were restored before this run. Includes 50 targeted UI, 92 persistence, 133 broker and five broker security cases. The separate complete Python governance discovery passed 304 cases in 323.472 s. |
| Standard-user WPF shell | External UI Automation reached Inspect and the application exited normally in 2.406 s. Apphost SHA-256 `1a23eca46e2dc3ea29c0c552aafe8f14e0aa013dba399bcc6c2f292990df1a90`. |
| Extended recovery | Both `KilledNoOpApplicationReopensTheSameJobAndReconcilesWithoutDispatch` cases passed in 18.368 s wall time. They terminate and reopen owned application instances on schema 010 with a controlled no-op action. |
| Native host adapter | Two ActiveMachine broker cases passed in 68.197 s wall time. The real exact lookup rejected both an absent revision and a restricted Windows offer with zero authorization calls. Real empty downloader and installer objects exposed the expected nonforced, quiet options. |

In the final technical source, `80aad3735506c0b06fb3f140b6d002725a27f951`, a malformed native result or search warning can be recorded as an authenticated refusal before Begin. A malformed result after admission leaves the native outcome unconfirmed. Both regression cases passed. Targeted with the entire deterministic broker project included Quick and passed 1,414 test executions in 152.849 s.

The final source produced a 3,657,216-byte NativeAOT win-x64 broker in 21.396 s. Its SHA-256 was `9e7246c1876bf8fd278ca1677c48c1585861f626b577b24e1ce8a2f059ee9b62`. The build used SDK 10.0.400, pinned AOT runtime 10.0.14, Windows SDK 10.0.28000.0 and MSVC linker 14.52.37029.0. The image was an unsigned AMD64 native GUI executable with an asInvoker manifest, no CLR or delay imports and System32-only dependent loads. Two unelevated no-ticket launches exited with code 2. All 21 observed DLLs resolved to System32. The image was not installed or started with elevated rights. The separate protected v2 refusal rehearsal remains unobserved and requires its own installed candidate and actual Windows consent.

The final source was also checked against the host's configured update service. The single `ActiveMachineAssessesOneExactOfferedRevisionWithoutMutation` case passed in 59.130 s wall time. Windows offered four revisions, none selectable. The exact restricted software lookup reported an unsupported handler and no uninstall support. The servicing and power checks passed, both storage locations were observable on the same volume, and the machine reported no pending restart.

The final managed broker assessment ran under the limited interactive token in the same-machine Generation 2 Windows 11 guest (build 26300.9974, wuapi.dll 1511.2611.5012.0). The absent revision and a real unsupported offer were both rejected with zero authorization calls. One case passed in 152.559 s, including staging and verified cleanup. No elevated broker or grant was used. The payload SHA-256 was `2ad079bddfbb7a9dfe092298c5a50f2a0c5b5fbce41b002be5aec22a7733fbc6`, the managed broker assembly SHA-256 was `4662e3c6ced28bbda4f80e99033bdbc6ae41687c96a88ff53b9df6422e0da375`, and the local runner receipt SHA-256 was `f0dac8e56356e42ae1f1d84b22c24de4683b6f0355debf52792132dff792d97d`. Reproduce using the Inspect, Prepare and Run sequence above with `WindowsUpdateBrokerAssessment` in place of `WindowsUpdateAssessment`.

A separate clean detached checkout of the integrated source `5e8beed49977ba3e80af7ada63260b3e5211ea7a` passed `eng/verify.ps1 -Tier Full -Restore` in 566.064 s. All 304 governance tests and 2,421 deterministic .NET cases passed with zero skips. Locked restore for all 26 projects, formatting and the Release build with warnings as errors passed. Native Windows and Extended checks above were run separately.

The final application adapter separately enumerated the guest's complete applicable offer list at 10:44 UTC on 2026-11-27. It found one offer and zero selectable revisions. Exact lookup succeeded and reported an unsupported handler and uninstall unavailable. The `WindowsUpdateAssessment` case passed in 197.850 s including verified cleanup. The payload SHA-256 was `8b463cd2ffd6439ff017710da6919c207f2fef865ef51efaa925b1c4c4fc0724`, adapter SHA-256 `ce15e63c6b1616f6232e5ccb0010b7fce89afd72a1884853ab5d4ac695e7941e`, and local receipt SHA-256 `4d5c0fb4c1fd18112a2faa91738d93442558afbb7374e092157ec6d87fefa5ad`. The application and native adapter checks used the same Windows and WUA versions. Neither run initiated servicing.

The final technical commit passed `eng/verify.ps1 -Tier Full -Restore` in a new clean detached checkout on the active machine in 560.757 s. Locked restore, formatting, the warnings-as-errors Release build, 304 governance tests (423.312 s) and 2,423 deterministic .NET tests across 13 assemblies all passed with zero skips. The checkout remained clean. The bundle validator used its built-in structural validation with the locked Python environment.

When the updated evidence and task state were being checked concurrently with the clean builds and guest tests, Quick passed 1,279 cases in 205.181 s. The warm target is 120 s. The Release build took 17.83 s and the WPF test group took 89 s. In the earlier integrated Targeted run those phases took 10.36 s and 76 s. The separate governance suite on the blocked task state passed all 304 tests in 414.593 s.

After the Full and guest runs ended, the same warm Quick command passed all 1,279 cases in 119.836 s. The Release build took 2.21 s and the WPF group took 65 s. This run met the 120 s target.

### Live transaction qualification

The native transaction remains unobserved. Resume on the existing `ThirdLife-W11` when a meaningful change or a qualification run identifies a selectable security or critical CBS revision from its configured service. Bind the exact offer and target to effective organization policy, supported recovery and measured space. Record the operator's approval for the actual job. For the supervised route, provision the reviewed protected broker and each phase grant under its independent authority, then obtain the applicable Windows consent. Collect both phase receipts, record any required restart, rescan and independently confirm the exact installed revision. Ordinary broker dispatch remains subject to TL-0314. [Integration PR 75](https://github.com/PikkuJanne/ThirdLife/pull/75)
