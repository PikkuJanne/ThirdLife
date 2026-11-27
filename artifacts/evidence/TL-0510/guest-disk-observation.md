# TL-0510 scoped guest disk observation

On 27 November 2026 the existing `ThirdLife-W11` guest was running Windows
11 client native x64, build 26300.9974. Its temporary-volume headroom was
sufficient. The installed active-host evidence in the parent record remains
valid. [Host provider evidence](system-disk-health.md)

The single read-only production-provider selection was
`ThirdLife.Inventory.Tests.SystemDiskHealthProviderTests.ObservesTheSystemDiskHealthThroughTheProductionProvider`.
The checked archive came from clean source
`b15fcb617f484751b1fee009bcd28326ff626dc3`. Its test assembly SHA-256 was
`e4eb660977afd88378829494c17975731ae078c287a4338bea73ecee2bda4cf3`.
The guest task ran one selected case in 11.608 s. The case rejected the
elevated token before calling the provider. It recorded zero passes and no
guest disk-health result. PowerShell Direct and staging took 76.489 s total.

An isolated standard-account route then reached Windows Task Scheduler.
The first preparation rejected an overlong description before account
creation. After the description was shortened, `Register-ScheduledTask`
returned `Access is denied` for the limited distinct-user task. A follow-up
read confirmed zero retained probe accounts and zero retained tasks. The
owned temporary stage remains under
`%LOCALAPPDATA%\Temp\ThirdLife-TL0510-Disk-8dfa6e6f132f4eb4909bc911e13279e8`
in the guest and the matching ignored host worktree directory holds the
sanitized receipt. The retained stage is outside packaged output.

The missing observation is the production disk provider under a verified
standard-user token on this exact guest. Revisit after a supported limited
session or runner becomes available. These checks did not evaluate Windows
Update offers or the aggregate restart and live-profile qualifications.
