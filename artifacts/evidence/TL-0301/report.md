# TL-0301 application catalog schema

**Date** 2026-11-25  
**Base** `8517ed65c36ad9af30a21a824fd8f51e67333870`  
**Environment** REF-CODEX-001 rev 2026-08-29.1, Windows 11 x64, pinned .NET SDK 10.0.400 and Python 3.14.7

## Contract

| Criterion | Verification |
| --- | --- |
| Stable capability and catalog versions | The four governed generic capabilities resolve to distinct entries. The catalog and entry versions use bounded semantic versions. The parser binds the exact bytes to SHA-256. |
| Exact package identity and platform | Source, package ID, opaque exact version, scope, architectures, and minimum OS are required. The exact verification tuple must match the package tuple. |
| Review and rights | Publisher, licence and privacy reviews, their UTC dates, the review window, and distinct installation and redistribution states are validated. All four synthetic entries have pending reviews and withheld rights. |
| Impact declarations | Reboot, service, update, network, and two disk estimates are required. The metadata can explicitly declare unknown impact or an unavailable estimate. |
| Closed input | The parser rejects unknown or missing fields, duplicate properties and identities, unsupported token values, non-UTF-8, and inputs over 65,536 bytes or the nesting bound. It does not include input in errors. |
| Freshness and downgrade | Caller-supplied time checks review dates and expiry. A supplied trusted current snapshot rejects a different catalog identity, a lower version, different bytes under the same version, and a reviewed-to-synthetic replacement. |
| B1 boundary | The governed sample has four synthetic identities and zero external artifacts. The reviewed examples use fictional metadata. This parser has no package discovery, backend, filesystem access, or state changes. |

## Provenance

| Input | SHA-256 |
| --- | --- |
| Original TL-0007 YAML | `7f80078a24d9fa890738d344d3c705549c45d17d7712ce1f8d543d4ce47f8901` |
| Original approved TL-0007 fixture set | `1df6bbed058da9397555e747e6d3f4e4443934d261366a54145f2003e5fc8743` |
| Runtime JSON projection, 5,957 bytes | `60880321e29f0db5deb77c85bd10d1c5926ca1b068d930445695580891c1bdce` |
| Catalog v1 JSON Schema | `d92ab28178af7f5297e87f6a1a9c97ac06bf99b4f14c058321f212cfe698160c` |
| Approved licence matrix | `e85f3002175dfadc860f0d2c92de0787f52364f491e50a030c936a5421395418` |

## Failure isolation

- The pre-change Catalog assembly baseline passed one test in 2.605 s.
- Repository verification caught the new JSON projection in the YAML-only
  inventory. A focused regression verifies the exact projection path leaves the
  inventory and digests unchanged and rejects an unrecognized JSON path.
  Direct supply-chain validation passed with 28 components and approved matrix
  status. The affected pilot-fixture suite passed 21 checks in 20.239 s.
- The working checkout's Python discovery copied an older ignored
  `TestResults` artifact into test repositories. Isolating the approval case
  reproduced Git's staging rejection. The clean worktree passed that case and
  exposed a separate exact-source-revision mismatch. The checked-out inventory
  and committed Git inventory now both exclude exactly the governed JSON
  projection. Bundle validation checks its contents against the original YAML.
- The formatter found LF endings in the three new production C# files. After
  normalizing those files to repository CRLF, the focused format check passed.
- PowerShell `Test-Json` accepted the governed synthetic sample and a
  fictional reviewed sample. It rejected reserved-licence and command-text
  mutations. The runtime tests additionally cover freshness, identity equality,
  byte limits and trusted-snapshot ordering.

## Resource and exposure limits

An active-machine PowerShell measurement ran 1,000 warm parses of the exact
JSON projection with the current snapshot supplied. On .NET 10.0.14 the
loop took 240.81 ms and allocated 24,186,088 bytes on its measuring thread,
including PowerShell loop overhead. This is a bounded workload observation
on REF-CODEX-001. The task adds no UI controls or user interaction, and
requires no GPU, background work, network, storage migration, package
license decision, new dependency, or release-interface entry.

## Governed runtime verification

`.\eng\verify.ps1 -Tier Targeted -TestProject tests\ThirdLife.Catalog.Tests\ThirdLife.Catalog.Tests.csproj`
passed in 69.838 s on the active machine at the Catalog implementation
checkpoint. The Quick steps passed static validation, formatting, the Release
build with zero warnings, and 1,112 tests across Core, Inventory and UI. The
Targeted selection added 65 Catalog tests. Quick passed again in 64.575 s
after the Git source-revision correction. That correction changed only Python
governance.

In the clean worktree, CycloneDX generation in memory bound to correction
commit `2f8c494cd813238fc4d5fc5b615957ead306ef1f`. The sorted JSON document
SHA-256 was `7cede10117a63ade6f366da6dbd156bb28ad6b2de06b171cb34b724dc60b35d5`.

`python -m unittest discover -s tools/tests -p 'test_*.py' -v` passed 287 tests
in 216.866 s in the clean worktree at that commit. The test run used the
pinned Python environment on the active machine. The worktree was clean
before and after verification.

The change introduces a metadata contract and test fixtures. The normal
task integration is unprotected, with no database migration, major refactor,
dependency change, product execution path, or named Full or Extended trigger.
