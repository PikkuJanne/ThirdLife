# TL-0114 audit workspace

The unelevated six-stage shell exposes **Go to audit controls**. The workspace
creates a durable job with generated job and device codes. Copy the displayed job code to
reopen it. Opening or closing the view leaves existing evidence intact. The job store opens
on the first create or open action.

## Intake and assessment

Record the outcome from an actual external sanitization record for the device. Select the
method where applicable, enter the workshop operator and media codes and the record's date,
and confirm the record and lawful ownership. The capture stores the date at UTC midnight
and records the separate capture timestamp. The fixed intake policy identifier is
`m1-intake-v1`. An explicitly unknown outcome has no event attribution. The gate remains
blocked for missing, failed, unknown, stale or archived evidence.

The saved record and gate decision are separate durable transactions. If a record commits
before its decision, reopen the job and complete the saved gate. Reopening alone does not
grant assessment access. If storage reports an ambiguous error, review the committed job
before retrying.

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

The compiled guide describes safe applicable checks, pass/fail criteria and cleanup.
Manually record one of **Pass**, **Fail**, **Not run**, and **Not available**. An unavailable
result requires an applicable reason. Use a brief workshop observation without personal
data. Detected devices alone cannot pass any manual check. The guide preserves the operator,
capture time, result, evidence class, reason and bounded notes.

A synthetic job can exercise **Not run** and **Not available** with explicit fixture
provenance. A physical pass or failure cannot be recorded from that workspace.

Closing and reopening preserves all saved attempts and suggests the next unfinished
check. A new attempt appends a record and links to the previous visible attempt. An
essential failure remains visible as a blocker candidate. M1 supplies no readiness
approval. Cold-boot checks remain in their later assigned task.

The job store keeps its existing SQLite schema and registered local path. Earlier
human-test records remain readable. The four stored outcomes retain their existing wire
tokens, with the historical `not_tested` token displayed as **Not run**. Guided capture
adds an optional details field to the evidence payload. Older prerelease binaries with
strict payload readers cannot reopen jobs containing the enriched records. Preserve the
store and use a compatible build to reopen such jobs.
