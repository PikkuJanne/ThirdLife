# M1 — Historical operator attempts

**Status:** The operator check was withdrawn on 24 November 2026 under [D-074](../../../DECISIONS.md#d-074). No replacement test, display or assistive-technology change, scope acknowledgement or M1 approval is requested. The [old handout at its inspected revision](https://github.com/PikkuJanne/ThirdLife/blob/674638a972915cf533a62f37a9f81b4b0985f6ca/artifacts/evidence/TL-0116/human-check.md) preserves the retired instructions.

The later owner report in the active continuation states that **Start assessment remained disabled** in the corrected route. The supplied instructions already included confirming and saving intake. The report did not establish the exact input event, control state, stored evidence or point of rejection in that attempt. Assessment, manual capture, reopen and report outcomes were not supplied for that attempt. The [technical M1 gate](../../gates/M1-audit-only.md) records the independent engineering diagnosis and evidence.

## First operator attempt

The first review used source `da746d306623f860ce64cd1143738087486886ca` and
the exact build and fixture identity recorded in preparation commit
`b4ca16c1cf699b49c23ec1fc14c305fe12d863b4`. The operator reported at approximately
17:35 CET on 24 November 2026:

> Narrator amazingly off. Stuck at Start assessment, as it is greyed out. Mousepad scrolling is way too fast it is uncontrollable.

The visible fixture had an open job and the prepared inputs. Its status said no
external result was saved and offered no direct action beside the disabled
assessment control. Focus was at the job identifier. The isolated store contained
one job, zero sanitization evidence records and zero gate decisions. The lock
correctly prevented assessment. The exact prefilled inputs, checkbox and save
path then passed a focused automated rehearsal. The original rehearsal had
populated the fields itself, so it did not exercise that preparation path.

Narrator's process and Quick Start window were visible on the same Windows desktop.
Speech was unavailable to the operator. At follow-up, Windows read 3840×2400 at
59 Hz, 250% scale and contrast off. That reading does not establish which display
samples the operator completed. No assessment, manual capture, reopen, report or
display outcome was reported. Scope acknowledgement and approval were not given.

Codex closed the fixture normally and verified cleanup. The test launcher exited
successfully after 916.662s of window lifetime, including preparation and waiting.
At 17:38:10 CET, Windows read back the original 3840×2400 at 59 Hz, 250% scale,
contrast off and Narrator off.

## First attempt setup and build

| Evidence field | Recorded value |
| --- | --- |
| Tested commit | `da746d306623f860ce64cd1143738087486886ca` |
| Standard-user apphost SHA-256 | `8c3c9f5fed5e8c27b343626073f7f319be0b1fccc0b06f06e59a0ddfdb7e7824` |
| Review launcher and production assembly SHA-256 | `ThirdLife.UI.Tests.dll` `3c1bc6f7059f3f6a0c4a34c3e0ca93687c2bff4619638df69e3d951a1bf30886`, `ThirdLife.UI.dll` `f5cac0f7d32a6d4dffb5fdc4b40d806a69c82b30492d2212697611c89b7f6379`, `ThirdLife.Reports.dll` `7d8500be732862bf8a44a104df0027b3c4aeb1bcd1ed8758953a3d9a4a20db1d` |
| Context, reference profile and standard-user token | Active Windows 11 Pro x64 build 26300, `REF-CODEX-001` revision `2026-08-29.1`. Exact-candidate clean worktree, SDK 10.0.400, Release. Independent main apphost smoke and named fixture rehearsal both verified the standard-user token. |
| Candidate fixture digest and labelled available/unavailable/failure examples | `M1AuditFixture.cs` SHA-256 `92128eeae83336d1d99d12a1ca85dde8c116c241d526bdb40d58543a0b3a3c0c`. Explicitly synthetic intake and three example providers with collected, unavailable and failed outcomes. No donor keyboard sample. |
| Launch receipt | Exact Release launcher started at 17:22:41+01:00 on 24 November 2026. The active window was observed at 17:23:05+01:00 with Intake selected, no job open and the audit controls link available. |
| Windows settings | Baseline verified at 17:16:13 CET on 24 November 2026. 3840×2400 at 59 Hz, 250% scale, contrast off and Narrator off. At 17:22:29 CET, the same resolution and refresh, 200% scale, contrast off and a running Narrator process were observed. Operator did not hear Narrator. Restoration verified at 17:38:10 CET. |
| Operator role and response | Project owner acting as operator. See [first operator attempt](#first-operator-attempt). |

## Corrected attempt setup and reported result

| Evidence field | Recorded value |
| --- | --- |
| Corrected candidate source | `edb9b03ba813f5de67238d84dd424ad6da572257` |
| Standard-user apphost SHA-256 | `105d0445b6b49c0e9bce86970abe8b6e7b692abab99c45d4738644eca1c327b2` |
| Review launcher and production assembly SHA-256 | `ThirdLife.UI.Tests.dll` `240f6220ecc12e11f5a9c5c7d6beb97b2e67103ddb0bab8395b1b41f11a2f2bb`, `ThirdLife.UI.dll` `f6028e00339048dd7c879679bee2bc5d1c12fdf842cd5b1f336da1fbaa25d77d`, `ThirdLife.Reports.dll` `c50e97d210a0350baa10a772d3fa2b17b3ed3e35231d18835992df187055a09c` |
| Candidate fixture | Same `M1AuditFixture.cs` digest `92128eeae83336d1d99d12a1ca85dde8c116c241d526bdb40d58543a0b3a3c0c`. Separate fresh SQLite store and synthetic providers. |
| Launch receipt | Corrected exact Release build launched at 17:52:00 CET on 24 November 2026. At 17:53:39 CET the fresh fixture showed Audit job and Create job was available. The store had no open job. |
| Windows settings | Actual 3840×2400 at 59 Hz, 200% scale and contrast off verified at 17:51:48 CET. Enabled Narrator with the primary Windows Settings switch, read the switch back as On, minimized Narrator Home, and left Settings on System → Display. Original restoration baseline is 250%, contrast off and Narrator off. |
| Narrator transcription | In the fresh fixture, Speech recap showed the ThirdLife window, the audit controls link and the Audit job group. This verifies recognisable screen-reader output. It records transcription only. No audible-output success was established. |
| Reported corrected-route outcome | Start assessment remained disabled. Subsequent workflow and perceptual outcomes were unperformed or unreported. |

| Observation | Coverage in this review | Result |
| --- | --- | --- |
| A11Y-001 and A11Y-002 | New controls and restored focus through the keyboard-only audit and report route. | Unperformed or unreported |
| A11Y-003 and A11Y-004 | Narrator semantics and status on the new gate, assessment, manual and report controls. | Unperformed or unreported |
| A11Y-005 | Actual 200% scale on new controls. | Unperformed or unreported |
| A11Y-006 | Reachability at the supported reduced resolution. | Unperformed or unreported |
| A11Y-007 | Focus, state and meaning in the actual contrast theme. | Unperformed or unreported |
| Touchpad follow-up | A short two-finger scroll over the corrected audit view. | Unperformed or unreported |
| A11Y-008 and A11Y-010 | Plain next steps, one unavailable manual result, labelled unavailable and failure examples, and usable reopen/report interpretation. | Unperformed or unreported |
| A11Y-009 | Close/reopen context only. This samples recovery navigation. Automated cancellation and process interruption are separate. | Unperformed or unreported |
| Scenario-limit acknowledgement | Operator's explicit response. | Unperformed or unreported |
| M1 gate decision | Project owner's explicit response. | Unperformed or unreported |

See the [gate record](../../gates/M1-audit-only.md) for objective evidence and
[original shell observations](../TL-0113/human-walkthrough.md) for unchanged scope.
