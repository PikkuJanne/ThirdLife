# TL-0116 external fixture route

## Reproduced fault

The owner reported that **Start assessment** remained disabled on the second attempt.
The offered build still existed at detached source `edb9b03ba813f5de67238d84dd424ad6da572257`.
Before launching, Codex matched its SHA-256 digests to the [original launch receipt](human-check.md).

| Assembly | SHA-256 |
| --- | --- |
| `ThirdLife.UI.Tests.dll` | `240f6220ecc12e11f5a9c5c7d6beb97b2e67103ddb0bab8395b1b41f11a2f2bb` |
| `ThirdLife.UI.dll` | `f6028e00339048dd7c879679bee2bc5d1c12fdf842cd5b1f336da1fbaa25d77d` |
| `ThirdLife.Reports.dll` | `c50e97d210a0350baa10a772d3fa2b17b3ed3e35231d18835992df187055a09c` |

On 24 November 2026, Codex ran the offered Release launcher twice on the active
machine with a standard-user token, isolated synthetic storage and the desktop's
existing settings. The old command was `dotnet test tests/ThirdLife.UI.Tests/ThirdLife.UI.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName=ThirdLife.UI.Tests.M1OperatorReviewTests.OpenPreparedFixtureForOperatorReview -- RunConfiguration.TreatNoTestsAsError=true` with process-local `THIRDLIFE_M1_OPERATOR_REVIEW=1`.
Each launched window started with a blank job and disabled **Start assessment**.
Cross-process UI Automation inspected controls. Windows delivered ordinary keyboard
events to the focused window without assigning any input fields, gate state or service
result directly.

| Diagnostic | Outcome |
| --- | --- |
| Space on the focused confirmation after Create, then Tab and Space on Save. Approximately 5.6s including startup and close. | The expected synthetic values were supplied. Confirmation changed from Off to On. Save showed the verified synthetic record and allowing gate state. **Start assessment** became enabled and received focus. |
| Enter on the focused confirmation after Create, then Tab and Enter on Save. Approximately 4.4s including startup and close. | The same untouched fixture supplied the expected inputs and focused the confirmation. Enter left the checkbox Off. Save executed and displayed the explicit confirmation error. The external result was still absent and **Start assessment** stayed visibly disabled. |

The old instructions offered both Enter and Space. The WPF checkbox accepted Space
and ignored Enter. The Save handler therefore rejected the Enter route before writing
an intake record. `CanAssess` correctly stayed false without the saved evidence and
allowing decision. The owner's exact input modality was unrecorded. The diagnostic
establishes a fault in an instructed route with the reported symptom.

Both old launcher invocations returned a passing test result because their windows
closed before timeout. The pass from the Enter run demonstrates that this exit condition
could coexist with a failed audit journey.

## Correction and outcome assertions

The confirmation checkbox now accepts Enter through the standard WPF
`KeyboardNavigation.AcceptsReturn` property. An operator still explicitly confirms the
record. Save still validates the fields and persists evidence and its gate decision
before assessment becomes available. Fixture data remains isolated from live collection.

`eng/run-m1-audit-journey.ps1` drives the same Release fixture construction and modal
WPF window from another process. It binds Tab, Shift+Tab and Enter to the owned
window and starts with the untouched blank fixture. Enter confirmation and normal
Save enable and focus Start assessment. The keyboard route continues through the
read-only assessment, an unavailable keyboard sample, close/reopen of that job, review
of the saved Keyboard result, and the visible workshop report. The launcher then
reads the stored records and checks the evidence origins, assessment, manual result and
report content. It compares the report file bytes with the opened preview and checks
the privacy and size limits exercised by the separate in-process journey. A timeout or
closing before the outcomes fails the scenario.

The development Release build and external run including the technical accessibility
probes passed with normal cleanup in 9.649s.
The same-machine fixture proves keyboard delivery and audit behavior for the synthetic
scenario. The independent perceptual limits and integrated build verification belong to
the [M1 gate](../../gates/M1-audit-only.md).
