# TL-0302 workshop and recipient profile schemas

**Date** 2026-11-25  
**Base** `e393f77fe5bf848708d13aae03e63e1cfd0ed02e`, PR #26  
**Environment** REF-CODEX-001 rev 2026-08-29.1, Windows 11 x64, pinned .NET SDK 10.0.400 and Python 3.14.7

## Contract

| Criterion | Verification |
| --- | --- |
| Distinct roles | The workshop section has generic capability IDs, explicit intent and bounded reason codes. The recipient section contains deferred choices that require the recipient's presence. |
| Exact catalog binding | Parsing requires a validated catalog with the same ID, version, and synthetic mode. Each requested capability must exist in that snapshot. The result carries its content digest. |
| Essential and optional intent | Basic marks video calling optional. Job Seeker marks it essential. The exact policy reference identifies the required checks, which the policy engine applies to active capabilities. |
| Recipient control | The four known choice types have fixed deferred and non-workshop states. Account sign-in and backup onboarding carry no credentials or settings payload. |
| Closed schema | The parser rejects unknown fields, action-shaped input, unknown choice types, duplicate identities, URLs, traversal, malformed UTF-8, and input over 32,768 bytes. |
| B1 boundary | Profiles express generic capabilities. The plan resolver owns mapping them to compiled actions. The schema carries no executable or external package input. |

## Provenance and limits

| Fixture | Bytes | SHA-256 |
| --- | ---: | --- |
| Basic JSON | 1,914 | `1df05d77dde99d20d72933d964ff068f16c80b6613738110af834e8e949ed75c` |
| Job Seeker JSON | 1,950 | `2c491c5dd5109cfb5674a19a1e26674a07e7b7e5c6349e774f1d7629683dea6b` |

Bundle validation compares both JSON projections with the approved YAML
byte for byte. The TL-0007 source set continues to hash to
`1df6bbed058da9397555e747e6d3f4e4443934d261366a54145f2003e5fc8743`.
Original profile files and the supply-chain approval were preserved.

Profile parsing uses bounded in-memory data and has no UI, persistence,
network or privilege effects. A future planner must match the referenced
policy ID and version to its actual policy result. This task introduces no
new dependency, release-interface entry, or cross-hardware claim.

## Isolated verification

- Before edits, the Catalog project passed all 65 tests in 2.581 s.
- The focused projection regression passed in 3.897 s. The related
  pilot-fixture suite passed 21 tests in 23.024 s.
- PowerShell `Test-Json` passed 26 cases, including both governed profiles,
  a reviewed sample, and 23 rejected invalid inputs.
