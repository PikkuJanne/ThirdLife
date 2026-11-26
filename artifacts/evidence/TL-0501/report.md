# TL-0501 - Dependency and phase scheduling checkpoint

## Base and scope

Work began after TRACE integration on `d207610b0594a9138510b7fd1cb5d341e892fe1b`, fetched and verified as `main`. TL-0404 and TL-0309 are complete. This checkpoint validates an action schedule against a bounded plan snapshot and projects progress from the saved journal. It leaves the existing package and broker routes unchanged.

On 2026-11-26, the deterministic Actions suite passed 181 tests in 442 ms and the focused SQLite action-journal suite passed 9 in 3 s. The baseline reused the clean TRACE build at `0c0b22b989592540523e16c696fa888fcfcf1d7d` after confirming the corresponding tracked source and tests match the merge revision. Environment was the active Windows 11 Pro 26H2 build 26300.9974 native x64 machine. Both runs used Release with `--no-build --no-restore`. The persistence filter selected `ActionJournalStoreTests`.

## Implemented behavior

`ActionSchedule` validates a bounded immutable graph against the action set
of an exact `JournalPlanSnapshot`. It rejects invalid action identities,
dependencies, phases and cycles. Eligible ties follow the reviewed plan's
action order. Dependencies and earlier phases require independent verification
before the projection offers the next action for review.

The projection requires the same job, plan digest and impact digest as the
saved journal. It retains per-action state, checkpoint and result, withholds
the review candidate while an operation requires reconciliation, and keeps
earlier verified actions visible after an independent failure. Historical
plans remain readable and provide no next review candidate.

## Verification

The Release verification ran on the active Windows 11 Pro 26H2 build 26300.9974 native x64 machine with .NET SDK 10.0.400. Tested code commit `ff3ac4622ff9d6731a272f99c24c60191e639f88`.

| Tier | Procedure | Result |
|---|---|---|
| Targeted | `eng/verify.ps1 -Tier Targeted -Restore`, Actions and Persistence projects, filter `FullyQualifiedName~ActionScheduleTests\|FullyQualifiedName~ActionJournalStoreTests` | Passed in 121.959 s. Locked restore, formatting, warnings-as-errors build, 1,203 Quick tests and 31 targeted tests passed. |
| Targeted | `dotnet test tests/ThirdLife.Actions.Tests/ThirdLife.Actions.Tests.csproj -c Release --no-restore`, deterministic selection | 202 passed in 340 ms on committed source. |
| Targeted | `dotnet test tests/ThirdLife.Persistence.Tests/ThirdLife.Persistence.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ActionJournalStoreTests` | 10 passed in 3 s on committed source. The test writes through the ordinary bounded journal API, closes and reopens SQLite, checks exact plan, impact and approval, and verifies the hold and progress after interruption. |

The close-and-reopen test supplies the same fixture graph at each reopen.
The saved journal supplies the action outcomes and existing approval.
Production dispatch and schema versions remain unchanged.

## Remaining TL-0501 work

Bind the validated graph and phase metadata into the canonical plan and impact review before approval. A later implementation must preserve historical plans and approval versions, restore the exact approved graph after reopen, persist typed action progress, and apply the existing admission, reconciliation, independent verification and restart checks at each dispatch. This scheduler checkpoint supplies ordering and journal observation. Live multi-action execution remains gated on those implementation prerequisites and the actual authority for each proposed action.
