# M2 gate — Explainable disposition engine

| Field | Value |
| --- | --- |
| Gate task | `TL-0208` |
| Record state | Technical verification in progress |
| Integrated M2 product source | `bf7076ccee9e5c4c438f4706a68a1021e98316b7` |
| Runtime authority | Active Codex machine, `REF-CODEX-001` revision `2026-08-29.1` |
| Accountable decision | Policy-owner disposition wording and exception governance |

All seven preceding M2 engineering tasks are integrated into `main` through
PRs #18–#24. This gate verifies the integrated product and prepares the
applicable policy-owner decision under D-074.
[Roadmap](../../ROADMAP.md#m2--explainable-disposition-engine)

## Acceptance

| Criterion | Technical evidence |
| --- | --- |
| Every disposition cites policy and evidence | Independent candidate facts and five golden dispositions exercise the evaluator and its rule citations. The v2 workshop report binds the saved assessment, policy ID, version, digest and job-scoped evidence. [Evaluation](../evidence/TL-0202/report.md) · [Dispositions](../evidence/TL-0203/report.md) · [Reports](../evidence/TL-0207/report.md) |
| Blocking failures stay visible | The separate Windows target rules still run for a policy that omits those checks. Stale, missing and contradictory facts cannot satisfy them. Protected rule and authorization-denial cases preserve the original failing state. [Dispositions](../evidence/TL-0203/report.md) · [Exceptions](../evidence/TL-0204/report.md) |
| Exceptions require a reason and attributable history | A permitted exception binds the exact policy bytes, rule, job, fact, operator and time. Replacements append history. The report distinguishes exceptions from passes and pending confirmation. [Exceptions](../evidence/TL-0204/report.md) |
| Policy updates preserve historical jobs | Disposable SQLite tests reject downgrade, changed same-version bytes, stale predecessors and corruption. An authorized reevaluation appends a linked record. Reopening retains the exact earlier policy and report. [Policy history](../evidence/TL-0205/report.md) |
| Reports distinguish facts from organization decisions | Five report goldens, exception and privacy cases, and a loaded native WPF keyboard test cover separate facts, policy, exceptions and disposition. Linked retests retain original failure records. [Reports](../evidence/TL-0207/report.md) · [Retests](../evidence/TL-0206/report.md) |
| New wording and governance have an attributable owner decision | Pending. The candidate wording and rules are prepared for that decision. |

## Verification

The pre-edit determinism, versioning and exception baseline passed 85 tests
in 3.488 s with `dotnet test` against the Policy test project and filters
`PolicyEvaluatorTests`, `PolicyVersioningTests` and `ExceptionTests`.
The integrated source includes the Active Codex machine results from PR #24.

`eng/verify.ps1 -Tier Quick` passed 1,112 .NET tests in 56.703 s and built
the solution with zero warnings or errors.

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
[Prepared policy-owner decision](../evidence/TL-0208/policy-owner-decision.md)
