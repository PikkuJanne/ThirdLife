# TL-0510 system disk health evidence

**Date** 2026-11-27  
**Source base** `7615a339a873613e87827c0530d2448b039f4ce6`  
**Tested source** `3b5dd745e94435650635e9495184348bcf4e5cd6`  
**Environment** Active Codex Windows 11 host, unelevated test process.

## Observed

The production provider resolved the fixed volume containing the Windows directory and
matched its drive letter to one online boot partition. It then matched one online boot
disk by the returned OS disk number. The runner emitted one workshop-restricted inferred
`healthy` fact. No volume path, letter, disk number, device identifiers or raw WMI
output left the provider.

Early implementation attempts returned `WBEM_E_ACCESS_DENIED` during enumeration after
the query had opened. Removing the locatable flag and setting packet privacy on
`IUnknown` alone did not resolve the denial. Setting packet privacy on the actual WMI
service and enumerator interfaces resolved it with the same standard-user token and
fixed local queries. The native integer values arrived in their documented signed
`VARIANT` representation with unsigned CIM types. [Storage sources](../../../docs/inventory/storage.md)

## Checks

| Tier | Command and result | Coverage |
| --- | --- | --- |
| Focused Inventory | `dotnet test tests/ThirdLife.Inventory.Tests/ThirdLife.Inventory.Tests.csproj -c Release --no-restore --filter '(FullyQualifiedName~SystemDiskHealthProviderTests\|FullyQualifiedName~StorageProviderTests\|FullyQualifiedName~StorageSourceTests\|FullyQualifiedName~ProviderContractTests)'`. 134 passed in 7.210 s of test execution. | Storage source and provider regressions, provider contract and both native host storage cases. |
| ActiveMachine | `dotnet test tests/ThirdLife.Inventory.Tests/ThirdLife.Inventory.Tests.csproj -c Release --no-build --no-restore --filter 'Category=ActiveMachine&FullyQualifiedName~SystemDiskHealthProviderTests' --logger 'console;verbosity=detailed'`. 1 passed in 3.535 s of test execution. | The named production provider observed `healthy`. The test checked unelevated execution, single-fact output, provenance and workshop privacy classification. |
| Targeted | `eng/verify.ps1 -Tier Targeted` with Inventory, UI, Reports and Verification projects and their system disk, storage, provider contract, policy projection, app roster, workshop renderer and profile verification filters. Passed in 285.301 s. | Zero Release warnings or errors, formatting, local contract checks and eight work-contract regressions. Quick ran 1,369 .NET cases. The selected filters ran 132 Inventory, 36 UI, 14 Reports and seven Verification cases. Native observations are recorded separately. |

The final run exercised the updated import guard. It requires the two new typed
aliases to the existing System32 `CoSetProxyBlanket` operation and rejects missing,
duplicate or unlisted imports.

## Limits

The Windows status covers the matched OS disk, which may represent virtual storage.
This observation does not establish physical disk lifetime, guest health, aggregate
restart clearance, whole-device acceptance, profile qualification, update installation,
or handover readiness. No disk, account, security setting, package, update, or power
state was changed. The existing `ThirdLife-W11` guest was untouched.

The remaining live reference case requires a trusted health observation on the
existing guest, positive aggregate restart clearance and a qualified exact live
profile. Native update and live multi-action qualification stay with TL-0511.

Integration Quick on the TL-0601 base `1fff725` passed 1,381 .NET cases and
eight work-selection regressions in 380.266 s. Other checks ran on the same
host. WPF took 202 s and the Release build took 23.22 s.
