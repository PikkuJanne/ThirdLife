# TL-0113 — Human shell walkthrough

**Procedure revision:** 1  
**Execution state:** Not run; human evidence pending  
**Scope:** Review-only audit shell on the active Codex machine  
**Cases:** Applicable shell portions of A11Y-001 through A11Y-007

## Bind the result to the build

The verified Release build is from `518744b751c531680e24a99b964c3b05a9b7c434` on
`codex/tl-0113-accessible-shell`. Its apphost SHA-256 is
`cb2d204146ee7dbb3b2bbffa83495646fdc1832ba859f4b9ea4545618c6955a8` and UI DLL SHA-256 is
`eab61e2df46c8ef41f845a358f0db5347dae8afc2864742be243e386b4046861`.
Evidence-only follow-up commits retain that tested source; use these existing Release files
and verify the hashes. If you rebuild, record the new source/artifact hashes and rerun the
smoke before the walkthrough. Human result fields below are still pending.

Launch the verified build from a normal terminal:

```powershell
& .\src\ThirdLife.UI\bin\Release\net10.0-windows\ThirdLife.UI.exe
```

Use a normal, unelevated terminal and a committed build supplied with this task. Before
starting, record the following. Use an opaque reviewer label and role, not a personal name.
Record only sanitized version/settings information; omit usernames, machine names,
personal paths, screenshots, recordings and speech transcripts.

| Field | Recorded value |
| --- | --- |
| Reviewer label / role | Pending |
| Task / source branch / full commit | TL-0113 / Pending / Pending |
| Tracked source unchanged since build | Pending |
| Release apphost and UI assembly SHA-256 values | Pending |
| Reference-machine profile revision | Pending |
| Windows edition / build / architecture | Pending |
| Direct host or same-machine hosted environment | Pending |
| Narrator version / relevant settings | Pending |
| NVDA version / source, if already available and approved | Pending; optional |
| Display resolution / scale / text size / contrast before review | Pending |
| Start / end with timezone / duration | Pending |

The following commands identify the normal Release artifact from the repository root.
Do not paste the full terminal transcript into evidence. If testing another approved
location, record its artifact identity without retaining a personal path.

```powershell
git rev-parse HEAD
git branch --show-current
git diff --quiet HEAD --
Get-FileHash -Algorithm SHA256 src/ThirdLife.UI/bin/Release/net10.0-windows/ThirdLife.UI.exe
Get-FileHash -Algorithm SHA256 src/ThirdLife.UI/bin/Release/net10.0-windows/ThirdLife.UI.dll
```

The `git diff` command must return exit code zero. Also verify that no untracked product
source was included in the build. Record hashes of any additional product assemblies shipped
with the tested build. Rebuild and start a new result if source or an assembly changes. A
later documentation-only result commit may reference this exact tested source commit and
artifact hashes.
If policy prevents launching the artifact, record `Not available` and the sanitized blocker;
do not disable the policy.

## Independently runnable cases

Start each case from a fresh normal application launch. Test only compiled workflow text
and empty state; do not enter recipient, device or account information. Use Tab, Shift+Tab,
arrow keys and Enter/Space as appropriate to the standard control. Put the pointer aside
for keyboard cases. Record a failing case before proceeding with a focused correction and
new source-bound rerun.

### A11Y-001 / A11Y-002 — Keyboard and focus

1. Launch normally. Confirm there is no UAC prompt and initial focus is visible on Intake.
2. Tab to Inspect and activate it. Confirm the review says no job is open and no assessment
   has been run. Return to Intake and confirm its distinct starting requirements.
3. Reach "Skip to stage review" with Shift+Tab and activate it. Confirm focus reaches the
   selected review. Tab through Requirements, Evidence, Plan and "How to read evidence";
   expand and collapse the evidence explanation using the keyboard.
4. Reach and read Activity ("No operation is running"), Battery safety and the unknown
   readiness status. Activate "Return to workflow stages" and confirm focus returns to the
   selected stage. Read all six stage names and each unavailable stage's criteria.
5. Confirm later stages cannot be activated. Focus the named Workflow stages group using
   Shift+Tab from Intake. In a small window, use PageDown and PageUp as its help describes
   to read every later stage's criteria. Use Tab/Shift+Tab through the remaining content;
   verify no trap, lost focus or unreachable text.
6. Close using the keyboard, reopen and confirm navigation did not create evidence or a plan.

Pass requires a complete review of the available audit shell without mouse assistance.
This is not completion of the future durable M1 audit workflow.

### A11Y-003 / A11Y-004 — Narrator semantics and navigation announcements

1. Start built-in Narrator using the usual Windows shortcut or accessibility settings, then
   launch the shell. Record Narrator and Windows versions without retaining spoken output.
2. Use the keyboard to repeat the previous case. Check meaningful control names and roles,
   current-stage status, disabled state, headings, empty evidence/plan meaning, expanded state
   of "How to read evidence" and the battery warning.
3. Change between Intake and Inspect. Confirm the changed view/status is announced clearly
   without silence or repeated unchanged announcements. Read each later stage's criteria.
4. Close the shell and restore Narrator to its prior state.

Production has no assessment/progress simulation. Record live operation progress,
cancellation and recovery as `Not run — not exposed by this review shell`; their automated
fixture results are separate evidence. If NVDA is already installed through an approved
procedure, repeat this case and record a separate result. Otherwise record its absence as a
limitation; this procedure does not authorize an installation.

### A11Y-005 / A11Y-006 — 200% scaling and reduced resolution

1. Record current display settings. Through supported Windows display settings, select
   200% scaling and a supported reduced resolution in the active-machine test environment.
   Record the exact chosen values. If either setting is unavailable, record that case as
   `Not available`; do not force an unsupported mode.
2. Relaunch the shell and repeat the keyboard case. Inspect wrapping, headings, focus,
   status, battery warning and disabled-stage criteria. Every essential item must remain
   readable or reachable through logical scrolling, without overlap or clipped controls.
3. Record a separate large-text check if performed, including its exact setting. Automated
   long-string/scale fixtures do not establish that a Windows setting was exercised.
4. Restore the original resolution, scale and text size and record restoration.

### A11Y-007 — High contrast and meaning without color

1. Record the current contrast/theme state. Select a supported Windows contrast theme.
2. Relaunch and repeat the keyboard case. Check readable content, visible focus/selection,
   disabled stages and their criteria. Status must remain understandable from text.
3. Restore the original contrast/theme state and record restoration.

## Result record

Use `Pass`, `Fail`, `Not available` or `Not run` for each result. Use `Human confirmed` only
after the human actually completes and records the case. Each row needs a start/end time
and duration, exact settings, concise observations and any defect/limitation. Add separate
rows for reruns so failed evidence remains available.

| Case | Result | Evidence class | Time / duration | Observations / limitation |
| --- | --- | --- | --- | --- |
| A11Y-001 keyboard journey | Not run | Not available | Pending | Human result pending |
| A11Y-002 focus and order | Not run | Not available | Pending | Human result pending |
| A11Y-003 Narrator names/roles/states | Not run | Not available | Pending | Human result pending |
| A11Y-004 navigation announcements | Not run | Not available | Pending | Human result pending |
| A11Y-004 live operation progress | Not run | Not available | Pending | No operation exposed in review shell; automated fixtures are separate |
| A11Y-005 200% scaling | Not run | Not available | Pending | Human result pending |
| A11Y-006 supported reduced resolution | Not run | Not available | Pending | Human result pending |
| A11Y-007 high contrast | Not run | Not available | Pending | Human result pending |
| Optional NVDA repeat | Not run | Not available | Pending | Availability/version pending |

**Settings restored:** Pending.  
**Defects and focused rerun references:** Pending.  
**Human acknowledgement of source/build binding and recorded results:** Pending.  
**Claim limit:** The recorded shell cases cover one active machine and the named settings
and assistive technology. They do not certify other hardware, complete the M1 gate or prove
unimplemented workflow stages. TL-0113's required human evidence remains pending until the
applicable keyboard and screen-reader results are actually recorded.
