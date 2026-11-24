# TL-0203 — Disposition and explanation generation

The disposition engine evaluates the policy rules and three required Windows
target checks against the same immutable snapshot. The target checks require
Windows 11, x64, and recognized support evidence. A policy can omit its own
operating-system rules and the target checks still run.

The precedence is **Do not deploy**, **Human review required**,
**Alternative operating system candidate**, **Repair and retest**, then
**Ready to prepare**. Unavailable required evidence stays unresolved, including
stale lifecycle facts and conflicting observations. Known ownership blockers
control when there are also incomplete checks. Advisory findings carry
through as rule outcomes without changing readiness.

Every decision contains the policy identity, all evaluated policy and target
outcomes, and the controlling rule and evidence citations. The concise
explanation previews up to four controlling rules and three evidence IDs per
rule. Full structured citations remain available for report detail. Decision
output uses the five governed labels and carries no aggregate health score.

## Verification

Active machine `REF-CODEX-001` revision `2026-08-29.1`, Windows 11 x64,
.NET SDK 10.0.400, on 2026-11-24.

| Check | Result |
| --- | --- |
| Pre-edit Policy tests on integrated TL-0202 | 109 passed in 3.559 s. |
| `dotnet test tests/ThirdLife.Policy.Tests/ThirdLife.Policy.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~DispositionTests` | 17 passed in 3.292 s. Covered golden text for all five dispositions, storage and battery repair, known unsupported target versus missing or stale target, policy rule omission, distinct lower-priority findings, citation preview bounds, and serialized output. |
| `eng/verify.ps1 -Tier Targeted -TestProject tests/ThirdLife.Policy.Tests/ThirdLife.Policy.Tests.csproj` | Passed in 53.959 s. Included Quick, 1,204 .NET tests with zero failures or skips, and the Release build with zero warnings or errors. |

The added target-traceability regression verifies that an unsupported Windows
finding and its evidence ID survive when a separate ownership blocker controls
the disposition. Full and Extended had no trigger for this domain-only change.
GitHub Actions is supplementary. Verified source
`d79fbd6edde69af36218ca8fdecdd050a781f7e7` and the completion record use
[PR #20](https://github.com/PikkuJanne/ThirdLife/pull/20).

## Scope

The engine adds no UI, persistent data, dependency, privilege or release
interface. It evaluates the snapshot once and reuses immutable evidence
references. The fixtures establish deterministic decisions and explanations.
Real assessment facts use provider-specific keys and retain lifecycle and
availability limitations. Their production mapping and operator presentation
are later tasks. The explanation wording remains prepared for the policy-owner
decision at TL-0208.
