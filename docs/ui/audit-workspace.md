# Audit workspace and package setup

**Jobs** starts the unelevated TRACE workflow. Create a job or open one by
its code. The stable workshop header shows a selectable copy of the code.
Closing the job preserves saved evidence. The store opens on the first create
or open action.

## Intake and assessment

Record the outcome from an actual external sanitization record for the device. Select the
method where applicable, enter the workshop operator and media codes and the record's date,
and confirm the record and lawful ownership. The capture stores the date at UTC midnight
and records the separate capture timestamp. The fixed intake policy identifier is
`m1-intake-v1`. An explicitly unknown outcome has no event attribution. The gate remains
blocked for missing, failed, unknown, stale or archived evidence.

Creating a job brings keyboard focus to external intake. **Go to external sanitization**
beside the blocked assessment returns to the required intake action. When a saved record
permits assessment, focus advances from the save form to **Start assessment**. Starting
the assessment still requires a separate action.

The saved record and gate decision are separate durable transactions. If a record commits
before its decision, reopen the job and complete the saved gate. Reopening alone does not
grant assessment access. If storage reports an ambiguous error, review the committed job
before retrying.

Touchpad and mouse-wheel input scroll the viewport in proportion to small wheel
movements. Scrollable evidence lists and the report preview take the movement while
they have room. Keyboard navigation remains available throughout the workspace.

Start assessment explicitly after the gate allows it. The job stays open and shell
navigation is available during collection. Cancellation requested and a committed cancelled
outcome have separate text states. A reopened job uses the newest committed assessment
start in append order and shows its actual committed providers. Earlier-gate results
are marked historical. An interrupted run stays
incomplete, and providers without a committed attempt stay unrun. Assessment completion
shows the collected evidence and leaves device readiness unknown.

Production uses a fixed roster of reviewed, standard-user Windows providers with network
use disabled. Test composition uses an isolated store and a separately labelled synthetic
roster. Its intake is tagged as synthetic at the point of capture. The live workspace
blocks writes to jobs with fixture evidence. The assessment orchestrator also refuses
live dispatch against a job with synthetic intake or device observations, even if the
gate was created through another entry point.

## Manual checks

In **Inspect**, choose **Hardware checks** and select a check. The compiled
guide describes its procedure, pass and fail criteria, and cleanup.
Manually record one of **Pass**, **Fail**, **Not run**, and **Not available**. An unavailable
result requires an applicable reason. Use a brief workshop observation without personal
data. Detected devices alone cannot pass any manual check. The guide preserves the operator,
capture time, result, evidence class, reason and bounded notes.

A synthetic job can exercise **Not run** and **Not available** with explicit fixture
provenance. A physical pass or failure cannot be recorded from that workspace.

Closing and reopening preserves all saved attempts and suggests the next unfinished
check. A new attempt appends a record and links to the previous visible attempt. An
essential failure remains visible as a blocker candidate. M1 supplies no readiness
approval.

The job store keeps its existing SQLite schema and registered local path. Earlier
human-test records remain readable. The four stored outcomes retain their existing wire
tokens, with the historical `not_tested` token displayed as **Not run**. Guided capture
adds an optional details field to the evidence payload. Older prerelease binaries with
strict payload readers cannot reopen jobs containing the enriched records. Preserve the
store and use a compatible build to reopen such jobs.

## Machine-wide package setup

The first package workflow is scoped to the reviewed Notepad++ `8.9.9.1` x64
Windows Installer package, source `notepad-plus-plus-releases`. It applies to a
writable workshop job with current verified external sanitization, replacement storage
or no donor storage, lawful ownership and a current completed live assessment. The package
plan requires supported Windows 11 client on native x64 and evidence meeting the current
operation-scoped policy. Unknown, stale, contradictory or synthetic admission evidence
leaves the action unavailable. The existing whole-device disposition and readiness rules
continue to govern device acceptance, finalization and handover.

1. Open the job, complete intake and run the read-only assessment. Open **Prepare**,
   select **Plain-text editor (machine-wide setup)** and choose **Build plan review**.
   Building the plan reads actual installed state and checks the exact signed installer
   from the reviewed source. An existing installation, unavailable state, changed
   artifact or inadequate staging/system free space prevents dispatch.
2. Review the exact package, source, requested machine scope, network use, disk impact,
   Windows consent, restart limits, recovery and verification. Enter the responsible
   workshop operator code and choose **Approve plan** only with actual authority for
   the shown machine change. ThirdLife saves the approval for the exact plan.
3. Choose **Install approved package**. ThirdLife checks that the job, plan and approval
   still apply, records dispatch intent and rechecks artifact trust and installed state.
   Windows Installer applies the approved machine-wide operation. Respond to any
   Windows consent prompt only with actual authority for that action. The ThirdLife
   window stays unelevated.
4. Open **Verify** to review the saved setup journal. ThirdLife independently queries Windows Installer's
   machine registration for the exact product and version. The report distinguishes
   verified installation from failure, restart pending, cancellation and review.
   Close and reopen the job by its code, then generate a workshop report from the saved
   transaction.

**Cancel package operation** requests cancellation. After installer dispatch Windows
Installer may still be completing work. An interrupted or uncertain result can require
review of actual installed state. ThirdLife does not automatically retry, uninstall,
restart or undo the installer operation. The route may clean up its own transient installer
files. The workshop journal and report omit raw installer output, download redirects and
staging paths.

This scope is one package action. The exact candidate, evidence and limits govern its
qualification. Pilot and public exposure retain their separate decisions.

[Product contract](../product-contract.md) ·
[Backend rationale](../adr/0009-winget-backend.md) ·
[Release interface](../../RELEASE_INTERFACE.md)

## Final cold-boot verification

At the final full-maintenance verification boundary, open **Verify** and choose
**Save cold-boot checkpoint**. The request stays with the current job, approved plan
and local Windows session. Save your work and use Windows Run to enter
`shutdown /s /t 0`. Wait for the device to power off, turn it on and reopen the same
job in **Verify**. The application leaves shutdown and power-on to the operator.

When Windows confirms an orderly full shutdown and the next full startup, choose
**Run post-boot verification**. This reads the configured Windows Update service,
collects fresh operating-system and hardware evidence, checks the exact approved
package registration and reruns the reviewed bounded launch probe. A warm or hybrid
startup, missing evidence, a changed job or an expired checkpoint requires a new
checkpoint. The request expires after 24 hours.

Verify and the restricted workshop report show the saved Windows sessions, exact
package version, probe outcome and remaining blockers. Applicable physical device
checks and finalization are still required for handover.

[Windows shutdown command](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/shutdown)

## Handover mode

Open **Handover** and save the **Assisted** or **Sealed** mode using your
workshop code. In assisted handover, record whether the present recipient
requests guidance, defers it, or declines it in each setup area. Record
the choice without entering a name, account, password, destination or key.
Saving a guidance choice keeps the recipient in control of actual setup.

Sealed handover leaves all recipient settings pending while you complete
the machine-level checks. Changing the saved mode starts a fresh set of
guidance choices and requires you to review the workshop checklist again.
The earlier records remain in the job history.

## Workshop finalization

In **Handover**, choose a workshop check and follow its guidance. Save the
result with your local workshop operator code. When a check permits **Not
applicable**, choose the reason that fits what you confirmed. Use **Could not
confirm** when the evidence is insufficient or when an earlier result no
longer holds. Never enter account names, network names, passwords, recovery
material or other secret values as an operator code.

The checklist distinguishes saved operator confirmations from the independent
pending-restart check. A missing or failed required result holds handover.
Open **Saved confirmations for this check** to review earlier outcomes. A new
boot or a relevant change to the durable job makes earlier confirmations
historical and requires another check. Use **Refresh checklist** after
reopening or reviewing a change. The checklist records outcomes and leaves
workshop cleanup to the operator. Whole-device verification and the overall
handover decision remain separate.

## Workshop report

Open **Handover** for the current job, then choose **Generate workshop report**.
Generation rereads the durable job and writes a deterministic UTF-8 text report.
The report includes the renderer build, saved policy and preparation digests,
and each plan approval. Findings, manual results, repairs, retests, package
transactions, independent verification and finalization retain their sources
and timestamps. An earlier confirmation remains in the record when it needs
a recheck. Review handover eligibility and sign-off separately.

Use **Open workshop report** to read the verified generated copy in the keyboard-
accessible preview. The preview opens the issued artifact for the current job.
Selecting a different job or saving a new finalization result clears the open
action and preview. Earlier copies remain in the protected workshop report
folder. Generate a fresh copy when newer evidence is available.

## Recipient guide

Save the job's assisted or sealed handover mode, then choose **Preview
recipient guide**. The guide uses the saved guidance choices and current
independent software checks. It shows pending choices and unavailable
evidence in plain language.

Review the preview, then choose **Save printable guide**. ThirdLife
rechecks the underlying facts and writes the reviewed HTML to the protected
`%LOCALAPPDATA%\ThirdLife\SetupCore\RecipientGuides` folder. Open the
saved file in a browser to read or print it. A changed mode or set of facts
requires a new preview. The operator controls whether to share the copy.

The destination is the protected per-user directory
`%LOCALAPPDATA%\ThirdLife\SetupCore\WorkshopReports`. The report uses opaque
filenames and an explicit restricted workshop format. The generated copy may contain
workshop device identifiers. Unreviewed keys, raw text and free-form manual notes are
omitted from the report. The underlying workshop records retain the notes.

An inaccessible destination, collision, cancellation before publication, or an
unreadable or modified saved file produces a textual outcome. The original saved
bytes are preserved. Rendering is capped at 1,048,576 characters. The store limits
each artifact to 8 MiB and the folder to 64 files and 64 MiB, with a 4 MiB free-space
reserve. It does not delete published reports automatically. Explicit retention and
job deletion remain with the later lifecycle work.

## Sanitized support archive

In **Handover**, choose **Preview support archive**. Review the two tabs,
the total ZIP size and the exact archive digest. The preview displays the
contents of both files. The support schema uses fixed check and package
outcomes, versions and available generic hardware.

Record your workshop operator code and confirm the reviewed archive. Choose
a local location for the protected `ThirdLifeSupport` folder. ThirdLife
checks that the reviewed facts still match before publishing. A changed job
requires a fresh preview and approval. Use any exported copy only for its
support case and review its retention after the case closes.

The saved job records an export intent and, when confirmed, publication of the
exact ZIP. If a ZIP was saved and writing its receipt failed, **Retry saving
the export record** completes only the receipt. After an interruption, review
any saved intent with an unconfirmed outcome before sharing a related copy.
