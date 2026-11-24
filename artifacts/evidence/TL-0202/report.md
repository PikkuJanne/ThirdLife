# TL-0202 — Deterministic policy evaluation

The evaluator accepts a validated policy, observations from one assessment
snapshot, and the active capability selection. It evaluates every rule in
ordinal ID order. Each result retains its requirement, the condition outcome,
and the IDs, classification and availability of the controlling observations.
The same input produces the same outcome and citation order.

Observed and inferred observations may satisfy ordinary rules when their
available values meet the typed condition. Human-confirmed rules require
attributed human confirmation. Missing, stale, limited, conflicting or
incompatible evidence stays unavailable or pending. An explicitly empty
capability selection makes profile-dependent rules inactive. An unavailable
selection leaves them unresolved. Under policy v1, an unavailable required
fact cannot satisfy its rule.

The evaluator accepts the same exact observation once when repeated. Reusing
its evidence ID for different content fails validation. Two distinct IDs for
the same scalar rule produce a conflict and preserve both citations. Related
rules share the immutable citations for that key.

## Verification

Active machine `REF-CODEX-001` revision `2026-08-29.1`, Windows 11 x64,
.NET SDK 10.0.400, on 2026-11-24.

| Check | Result |
| --- | --- |
| Pre-edit Policy tests on integrated TL-0201 | 72 passed in 3.479 s. |
| `dotnet test tests/ThirdLife.Policy.Tests/ThirdLife.Policy.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~PolicyEvaluatorTests` | 37 passed in 3.150 s. Independent facts cover all 27 candidate rules. Forty seeded permutations cover stable rule and citation order. |
| `eng/verify.ps1 -Tier Targeted -TestProject tests/ThirdLife.Policy.Tests/ThirdLife.Policy.Tests.csproj` | Passed in 53.516 s. Included Quick, 1,187 .NET tests with zero failures or skips, and the Release build with zero warnings or errors. |

The tests exercise rule categories, availability, attribution, exception-free
decision semantics, wrong units and kinds, ID conflicts, case distinctions and
input bounds. Numeric minimums compare the expected integer and unit. A valid
full-charge ratio above 100 percent remains comparable to its threshold.
[Battery observation contract](../../../docs/inventory/battery-report.md)

Full and Extended had no trigger. The evaluator adds a deterministic domain
entry point with the existing project and package graph. GitHub Actions
provides supplementary verification. Verified source
`30641af942589f49fe7025cae9b05af4fe952484` and the completion record use
[PR #19](https://github.com/PikkuJanne/ThirdLife/pull/19).

## Scope

Evaluation is in-memory and bounded to 10,000 observations and 128 capability
entries. It indexes facts once, emits opaque citations, and uses no background
work, operating-system access, network, persistence or privileges. It adds no
UI or released capability. The candidate policy and its JSON projection remain
unchanged. The evidence proves synthetic rule evaluation. Live assessment
projection and disposition are later tasks.
