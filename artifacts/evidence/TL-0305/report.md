# TL-0305 impact and preapproval estimates

**Date** 2026-11-25  
**Base** `096e33eafbc5b0f06ddbc1c5c20b2982664aa941`, PR #29  
**Environment** REF-CODEX-001 rev 2026-08-29.1, Windows 11 x64, pinned .NET SDK 10.0.400

## Contract

| Criterion | Evidence |
| --- | --- |
| Unknown stays explicit | Unknown byte estimates remain null per action. Each aggregate carries the known subtotal, number of unknown actions and a nullable total of declared estimates. |
| Per-action impact | Each row keeps its action ID, reason, intent, scope, elevation, restart, compiled network category, declared network state, verification and rollback. |
| Consistent network vocabulary | The compiled package category is Software download. A catalog claim of no network is conflicting because the plan has no resolved offline route. Independently declared byte estimates remain visible. |
| Exact estimate snapshot | The immutable result carries the plan digest and impact contract version. Computation reads only the plan's pinned metadata and compiled declarations. There is no clock, filesystem, cache, package resolver or network lookup. |
| Synthetic and blocked cases | A compiled no-op has known zero bytes and no restart or network effects. It retains its declared elevation. A blocked plan has no impact rows because it proposes no actions. |

Download and installed estimates describe different quantities. These values
do not measure peak staging space, free space, cache reuse or rollback headroom.
The plan-review surface will retain the displayed estimate with approval.
This task introduces no UI, persistence, privilege activation, network use,
dependency change, or release-interface change.

## Verification

The pre-change Actions project passed 61 tests in 4.184 s.

The first compile caught a contract-version getter that the analyzer treated
as static. The snapshot now stores its version as immutable instance data.
The focused rerun passed seven tests in 3.895 s. They cover known, partial,
unknown and conflicting declarations, no-op and blocked plans, and unchanged
earlier estimates after a revised catalog supplies different bytes.

`.\eng\verify.ps1 -Tier Targeted -TestProject tests\ThirdLife.Actions.Tests\ThirdLife.Actions.Tests.csproj`
passed 1,180 tests in 58.296 s, including all 68 Actions tests. Full and
Extended were outside the triggers for this pure projection.
