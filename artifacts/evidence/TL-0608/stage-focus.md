# TL-0608 programmatic stage focus

Untouched main `1fff72535d508102cfa6f47eca54df7cccf30402` reproduced two active-host WPF failures. The M1 synthetic journey created its job and left focus on the shell scroll viewer. The policy journey reopened its job and could not focus Generate report immediately after Handover was shown.

The production stage transition now completes layout before returning to the caller that moves keyboard focus. The existing active-host journeys retain their original assertions. Both passed after the repair. The M1 journey reached the Intake confirmation and reopened its saved workshop report. The policy journey focused Generate report, opened the artifact and verified its saved policy provenance.

| Check | Result |
|---|---|
| `dotnet test tests/ThirdLife.UI.Tests/ThirdLife.UI.Tests.csproj --configuration Release -p:RestoreLockedMode=true` with the two fully qualified cases below | On untouched main, both cases failed in 7 s at the focus assertions. |
| Same cases with a Release build after the stage layout repair and `--no-restore` | Both passed in 12 s. |
| `eng/verify.ps1 -Tier Targeted -Restore -TestProject tests/ThirdLife.UI.Tests/ThirdLife.UI.Tests.csproj` with the same explicit filter | Passed in 377.974 s. Quick passed 1,367 tests and both selected active-host journeys passed in 7 s. The Quick WPF selection took 209 s while the separate clean Full candidate ran on the same host. |

The cases were `ThirdLife.UI.Tests.M1AuditJourneyTests.OperatorRouteReopensSavedFixtureAndPreviewsItsWorkshopReport` and `ThirdLife.UI.Tests.PolicyWorkflowTests.SupportedDesktopAssessesEvaluatesReopensAndExportsAnIsolatedJob`. The active Windows 11 Pro x64 build 26300 host ran .NET SDK 10.0.400 on 2026-11-27. The journeys exercised actual WPF keyboard focus and UI Automation with controlled job evidence. The complete M6 accessibility audit and perceptual observations remain separate from this repair.
