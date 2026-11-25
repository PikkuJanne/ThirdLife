# TL-0303 compiled action registry and contracts

**Date** 2026-11-25  
**Base** `c91c20143496485d1b989f74def9ba7a010f7d9d`, PR #27  
**Environment** REF-CODEX-001 rev 2026-08-29.1, Windows 11 x64, pinned .NET SDK 10.0.400

## Contract

| Criterion | Evidence |
| --- | --- |
| Compiled allowlist | The request root has a private-protected constructor and the two concrete request types are sealed. The registry validates construction and the versioned decoder allowlists the same two kinds. |
| Typed bounded parameters | A no-op accepts an action ID and an empty parameter object. Package plans carry a validated source ID, exact package ID and version, publisher, architecture, and scope. Synthetic identities remain reserved for no-op plans. |
| Preapproval declarations | The registry owns privilege and scope plus restart, network, verification, rollback, retry, and backend availability. The caller cannot pass these declarations in the request. |
| Portable serialization | The v1 wire contract has an explicit version, canonical nonempty action ID, exact property sets, a 4,096-byte limit, strict UTF-8 and duplicate rejection. Invalid input produces a fixed error. |
| Architecture | `ThirdLife.Actions` remains a portable standalone assembly. Core and Policy add no dependency on Actions. Dispatch, authenticated IPC, approval and backend execution belong to later tasks. |

## Boundaries

The package request carries metadata for plan review. Its registry definition
requires a separately implemented package backend and independent package
provenance checks. The no-op request has no operation parameters. This task
adds no dispatch, elevation, package, storage, or network operation. There are
no UI changes, runtime dependencies, migrations, or release-interface claims.

## Verification

The pre-change Actions baseline passed one test in 2.436 s.

`.\eng\verify.ps1 -Tier Targeted -TestProject tests\ThirdLife.Actions.Tests\ThirdLife.Actions.Tests.csproj`
passed in 58.361 s with 1,112 Quick and 36 Actions tests. Neither Full nor
Extended was triggered for this portable contract and normal task integration.

The focused Actions project passed all 36 tests in 2.568 s. These cover
both request kinds, machine and user scope, declared impact and verification,
canonical round trips, strict size and encoding limits, rejected malformed
parameters, and the closed public request shape. A separate project reference
review confirmed that Actions and Core have no project dependencies and
Policy references only Core.

On .NET 10.0.14, 1,000 warm decode/encode round trips of the 104-byte
no-op request took 37.79 ms and allocated 5,058,384 bytes on the measuring
thread, including the PowerShell loop. The fixture SHA-256 was
`53c806512e72f817208c493b9be207fa499223de1a8f8d2581a85d6f0d10d426`.
