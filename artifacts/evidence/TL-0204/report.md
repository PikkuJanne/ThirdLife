# TL-0204 — Human confirmations and governed exceptions

Six guided manual checks project into policy observations using the latest
accepted saved result for each check. The projection preserves evidence ID,
provenance, time, and available operator attribution. An unavailable or
unperformed result stays unavailable. The saved manual-test history remains
available. The mapping covers keyboard, pointing, built-in display, audio
output, microphone and camera at procedure revision `M1-MHT-1`.

An exception requires an explicit authorization ID on the exact policy bytes,
an unmet rule with one attributable evidence ID, an opaque local operator ID,
a bounded reason, and a timestamp. The engine protects the Core target,
critical and human-confirmed rules, do-not-deploy decisions and the guarded
sanitization, compatibility, ownership, activation and hardware-safety facts.
Missing, unavailable and conflicting evidence remains unresolved.

Recording or replacing an exception returns an appended history for the job.
Each record retains its authorization and policy digest, original evidence ID
and any superseded record ID. Evaluation applies only the matching record as
of the evaluation time. The underlying failed rule and its evidence remain
visible. A policy rule report distinguishes Pass, Fail, Exception, Pending
confirmation, Unavailable and Inactive. An exception line includes the opaque
operator code, authorization ID, event time and record ID.

## Verification

Active machine `REF-CODEX-001` revision `2026-08-29.1`, Windows 11 x64,
.NET SDK 10.0.400, on 2026-11-24.

| Check | Result |
| --- | --- |
| Pre-edit Policy and guided manual tests | 126 Policy and 27 Core tests passed in 5.741 s. |
| Core with `FullyQualifiedName~HumanConfirmationProjectionTests\|FullyQualifiedName~ManualTestTests` | 38 passed in 2.514 s. Included 11 new checks for projection, attribution, unavailable evidence, and history. |
| PowerShell `Test-Json -SchemaFile schemas/policy.schema.json` | Nine focused cases passed. The unchanged candidate, a valid authorization identifier and malformed identifier mutations matched the schema. |
| Policy with `FullyQualifiedName~ExceptionTests` | 18 passed in 3.273 s. Covered protected rule denial, provenance and version binding, replacement and replay, report statuses and restricted text exclusion. |
| `eng/verify.ps1 -Tier Targeted -TestProject tests/ThirdLife.Policy.Tests/ThirdLife.Policy.Tests.csproj` | Passed in 52.714 s. Included Quick, 1,233 .NET tests with zero failures or skips, and the Release build with zero warnings or errors. |

Full and Extended had no trigger. No migrations or dependency changes apply to
this task. GitHub Actions is supplementary. Verified source
`6079da400eda959274c93e2670844d522957a9e9` and the completion record use
[PR #21](https://github.com/PikkuJanne/ThirdLife/pull/21).

## Privacy and integration scope

The bounded reason is workshop-restricted. Ordinary serialization, exception
messages, diagnostic strings and the rule detail report exclude the operator
prose and raw evidence values. The current policy has no authorization
entries. Tests exercised proposals using synthetic policy and evidence.

The exception history and rule report are in-memory domain contracts.
Persistence of policy snapshots and decisions belongs to TL-0205, and
workshop report and UI integration belongs to TL-0207. The prepared exception
governance remains subject to the policy-owner decision at TL-0208.
