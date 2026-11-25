# M2 gate — Explainable disposition engine

| Field | Value |
| --- | --- |
| Gate task | `TL-0208` |
| Record state | Done — technical verification and policy-owner approval complete |
| Integrated M2 product source | `bf7076ccee9e5c4c438f4706a68a1021e98316b7` |
| Runtime authority | Active Codex machine, `REF-CODEX-001` revision `2026-08-29.1` |
| Accountable decision | Internal M2 wording and exception governance approved on 2026-11-25 |
| Verified gate checkpoint | `0179b4f989dc2e743d08e463a9ad57fb3d27eb1d` |
| Integration | [PR #25](https://github.com/PikkuJanne/ThirdLife/pull/25) |

All seven preceding M2 engineering tasks are integrated into `main` through
PRs #18–#24. This gate records verification of that product and the
policy-owner decision under D-074.
[Roadmap](../../ROADMAP.md#m2--explainable-disposition-engine)

## Acceptance

| Criterion | Technical evidence |
| --- | --- |
| Every disposition cites policy and evidence | Independent candidate facts and five golden dispositions exercise the evaluator and its rule citations. The v2 workshop report binds the saved assessment, policy ID, version, digest and job-scoped evidence. [Evaluation](../evidence/TL-0202/report.md) · [Dispositions](../evidence/TL-0203/report.md) · [Reports](../evidence/TL-0207/report.md) |
| Blocking failures stay visible | The separate Windows target rules still run for a policy that omits those checks. Stale, missing and contradictory facts cannot satisfy them. Protected rule and authorization-denial cases preserve the original failing state. [Dispositions](../evidence/TL-0203/report.md) · [Exceptions](../evidence/TL-0204/report.md) |
| Exceptions require a reason and attributable history | A permitted exception binds the exact policy bytes, rule, job, fact, operator and time. Replacements append history. The report distinguishes exceptions from passes and pending confirmation. [Exceptions](../evidence/TL-0204/report.md) |
| Policy updates preserve historical jobs | Disposable SQLite tests reject downgrade, changed same-version bytes, stale predecessors and corruption. An authorized reevaluation appends a linked record. Reopening retains the exact earlier policy and report. [Policy history](../evidence/TL-0205/report.md) |
| Reports distinguish facts from organization decisions | Five report goldens, exception and privacy cases, and a loaded native WPF keyboard test cover separate facts, policy, exceptions and disposition. Linked retests retain original failure records. [Reports](../evidence/TL-0207/report.md) · [Retests](../evidence/TL-0206/report.md) |
| New wording and governance have an attributable owner decision | Janne Vuorela approved the exact proposal for the internal M2 candidate. [Decision record](#policy-owner-decision) |

## Policy-owner decision

Janne Vuorela, project owner acting as policy owner, replied “Proposal approved”
in the active Codex conversation on 2026-11-25 (Europe/Berlin). The approval
covers the proposed disposition wording, including the summary when a check
is resolved with an exception, the disposition precedence and Windows target
guard, and the proposed exception governance for the internal M2 candidate.

The approval binds product source `bf7076ccee9e5c4c438f4706a68a1021e98316b7`
and the proposal presented from review checkpoint
`fcecbb1fb672fc2d9aa2b423e94244fa69294ddc`. The proposal SHA-256 is
`be59861a13d6d0d360cfbb4d92fb1b61c23ff180700540cc5c5eef9db254673d`.
Effective policy adoption, individual exception grants, pilot exposure and
release authorization retain their own requirements.
[Exact proposal as presented](../evidence/TL-0208/policy-owner-decision.md)
[D-074](../../DECISIONS.md#d-074)

## Verification

The pre-edit determinism, versioning and exception baseline passed 85 tests
in 3.488 s with `dotnet test` against the Policy test project and filters
`PolicyEvaluatorTests`, `PolicyVersioningTests` and `ExceptionTests`.
The integrated source includes the Active Codex machine results from PR #24.

`eng/verify.ps1 -Tier Quick` passed 1,112 .NET tests in 56.703 s and built
the solution with zero warnings or errors.

`eng/verify.ps1 -Tier Full -Restore` passed in 301.624 s in a clean detached
worktree of the gate checkpoint. It passed 286 governance and 1,572 supported
.NET tests with zero failures or skips, including the determinism, exception,
historical replay, repair, report and keyboard suites. Locked restore,
formatting, and the Release build passed. The gate checkpoint has the same
product source, tests, runners, and fixtures as integrated `main`.

`eng/run-ui-smoke.ps1` launched the same apphost under a standard-user
token, reached Inspect with external UI Automation in 1.496 s, and closed
normally in 1.781 s total. Peak working set was 222,863,360 bytes in this
one startup observation. The worktree remained clean.

| Smoke input | SHA-256 |
| --- | --- |
| `ThirdLife.UI.exe` | `f5a060ab6c80e9a0d6c591b097cb1e2eb5128c574c0cd24d115689aad7913dde` |
| `ThirdLife.UI.dll` | `0c72171e1ec1cbde2626887ec4a02d84f5d6c7474170e130eb54a4b84695ece7` |

The approval checkpoint changes only governance records. The pre-edit
`tools/validate_bundle.py` baseline passed in 3.103 s using the available
custom structural validator. The Full, startup and representative-report
results carry forward against the same product, tests and fixtures.

Extended had no trigger. The supplementary checks on merged PR #24 and the
proposal checkpoint of PR #25 passed.

The compiled v2 renderer produced six synthetic workshop reports in 0.294 s.
Inspection confirmed each of the five disposition headings, the relevant
saved rule and evidence IDs, policy identity, explicit unknown evidence and
distinct exception attribution. The output withheld the deliberately
secret-shaped operator code and omitted private fixture text. The 19,822-byte
artifact has SHA-256
`a5f800d78e703dbe841cdbefaff4b613bbcca99f0a6ebb974f2c90d291b8076a`.
[Representative workshop reports](../evidence/TL-0208/representative-workshop-reports.txt)

## Scope and limits

The approved TL-0007 fixture values remain unchanged. The source YAML has
SHA-256 `53535532a84489d3e23d54939cd3d30a539cb5c38c582b8c5e043a6bb0906007`.
The v1 exact JSON projection has SHA-256
`967ef27449426a99f001ff09cd11b5e637b62a3f1e33566dd935bb8fd876ac88`.
The original policy remains a candidate.

The representative cases use synthetic observations. The bundled Windows
lifecycle revision expired on 2026-10-30. Current live support evaluations
based on that revision remain unavailable. Live processor eligibility,
ownership clearance, exact system-storage linkage, aggregate battery ratio,
network function and swelling confirmation remain unresolved where no
reviewed mapping exists. No physical repair or cross-hardware certification
is claimed. [Lifecycle source](../../docs/inventory/windows-lifecycle-data.md)

GitHub Actions remains supplementary. This gate carries no effective-policy,
pilot, stable-release or deployment authorization.

The bundled candidate has no exception grants. Synthetic tests exercise
reason validation and history. The saved decision preserves visible
exception attribution while private reasons stay out of the captured report.
