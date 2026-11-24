# TL-0201 — Versioned policy schema and validator

The policy assembly reads bounded UTF-8 JSON and returns an immutable policy
document. The v1 contract requires policy identity, version, target and effective
metadata. It has five requirement categories, three compiled condition shapes
and explicit outcomes for unmet and unavailable evidence. Required rules block
readiness and route unavailable evidence to human review. Advisory rules carry
no blocking disposition. [v1 JSON Schema](../../../schemas/policy.schema.json)

The approved TL-0007 YAML and its fixture-set digest are unchanged. Bundle
validation compares the JSON fixture byte for byte with a deterministic
projection from the original YAML. The runtime test reads that projection.
The policy still has candidate status and no effective dates.
[Fixture contract](../../../fixtures/README.md)

| Input | SHA-256 |
| --- | --- |
| v1 JSON Schema | `013d2fb81b5e7580d7463203f09e252712ad6158fceaa143be30a3f1fee25199` |
| Candidate YAML | `53535532a84489d3e23d54939cd3d30a539cb5c38c582b8c5e043a6bb0906007` |
| Candidate JSON projection | `967ef27449426a99f001ff09cd11b5e637b62a3f1e33566dd935bb8fd876ac88` |

## Verification

Active machine `REF-CODEX-001` revision `2026-08-29.1`. Windows 11 x64, .NET SDK
10.0.400, PowerShell 7.6.9, Python 3.14.7 and PyYAML 6.0.3. Deterministic runtime
and governance checks ran on 2026-11-24.

| Check | Result |
| --- | --- |
| Pre-edit `dotnet test tests/ThirdLife.Policy.Tests/ThirdLife.Policy.Tests.csproj -c Release --no-restore` | Passed, 1 test in 2.45 s. |
| Pre-edit `python -m unittest tools.tests.test_pilot_fixtures` | Passed, 20 tests in 17.695 s. |
| Focused `dotnet test` Policy project with `FullyQualifiedName~PolicySchemaTests` | Passed, 71 tests in 3.11 s. Cases include all 27 candidate rules, depth and byte bounds, malformed UTF-8, duplicate properties and rule IDs, path-like strings, closed field and condition vocabularies, and invalid decision combinations. |
| `python -m unittest tools.tests.test_pilot_fixtures` | Passed, 21 tests in 19.896 s. The projection rejects duplicate properties, changed scalar types and extra bytes while preserving the approved source digest. |
| PowerShell `Test-Json -SchemaFile schemas/policy.schema.json` | Passed, 48 candidate and adversarial cases in 0.808 s. The published schema describes structural constraints. The runtime enforces duplicate properties and rule IDs, byte and depth bounds, lexical integer shape, and effective-date order. |
| `eng/verify.ps1 -Tier Targeted -TestProject tests/ThirdLife.Policy.Tests/ThirdLife.Policy.Tests.csproj` | Passed in 54.499 s. Included Quick, the Release build with zero warnings and errors, and 1,150 tests with zero failures or skips. |

The first repository Targeted run failed formatting on new C# files with LF
line endings. The files were converted to the existing repository CRLF style
before the rerun.

The first Full run stopped at a local-workspace failure. A focused supply-chain
test reproduced that a local ignored `TestResults` file was copied into the
test's disposable repository, where Git rejected staging it. The clean
candidate worktree excludes local artifacts and leaves the original workspace
in place.

## Scope

Validation is synchronous and bounded to 65,536 bytes, depth eight, 128 rules,
and 16 values per list. It uses no background worker, GPU, privilege change,
external execution, network operation or persistence. Malformed input produces
a fixed diagnostic without exposing supplied names or values. The policy
project reuses Core types through an internal project reference. The NuGet
package set, UI, data locations and release interface are unchanged.

Evaluation begins at TL-0202. Adopting an effective policy remains a governed
decision. The schema and synthetic cases establish input behavior on the active
machine. Physical hardware, live assessment coverage and modest-hardware
performance were outside these checks.
