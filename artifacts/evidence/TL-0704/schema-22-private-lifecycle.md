# TL-0704 private MSI upgrade across a job-store migration

On 28 November 2026, the existing ThirdLife-W11 guest ran Windows 11 Pro
26H2 build 26300.9974 on native x64. The Basic console used a dedicated
synthetic standard-user profile. The limited interactive fixture verified its
SID and profile, both package hashes, both 19-file inventories and the exact
persistence assemblies before Windows Installer ran.

| Exact private MSI | Source revision | SHA-256 |
| --- | --- | --- |
| 0.3.6 | `fa2fa35812a8960a35c3131bc902f2d7eee49b39` | `1a4a679db5cdc0cdd88c01277acd5aa9064a40582f05bd44d91c0dcbb3b25c9c` |
| 0.3.8 | `11d2b792fbaefd23ebd374e4e67d1cd2777f50d2` | `37c2e904f24005faa27c0694f5569074e27118ad565ef6bf6934513131aa2319` |

[0.3.6 package and cancellation](populated-job-lifecycle.md)
[0.3.8 package ownership and reproducibility](../TL-0703/private-package-ownership.md)

The pinned x64 .NET Desktop Runtime 10.0.11 was extracted in the protected
fixture stage from the previously reviewed archive. The guest received 44
other hash-verified files. One orderly restart refreshed the sign-in account
list under the standing grant. Boot readback and the expected Windows shutdown
event confirmed the restart. The first probe produced the expected schema and
assembly digest but its PowerShell launcher returned no child exit code. The
fixture stopped in preflight with Windows Installer idle. A revised launcher
captured the native process result and bounded its output. The earlier receipt
and streams were preserved before retrying from the same clean install state.

The corrected task returned zero in 31.075 seconds.

| Operation | Independent observation |
| --- | --- |
| Clean install 0.3.6 | Windows Installer returned 0. All 19 installed files matched the reviewed payload. The exact packaged persistence assembly created and read back a synthetic archived schema-21 job. |
| Upgrade to 0.3.8 | Windows Installer returned 0 and installed all 19 reviewed newer files. The closed schema-21 database bytes remained identical before the newer reader opened it. |
| Open with the newer reader | The exact 0.3.8 persistence assembly migrated the store to schema 22. Readback matched the original archived job, evidence and checkpoint fingerprint. |
| Try the older reader and installer | The exact 0.3.6 reader returned the newer-store refusal. The migrated database bytes stayed identical. Windows Installer returned 1603 for the old package and the 19 newer files remained intact. |
| Repair 0.3.8 | After the fixture removed its installed runtime configuration, Windows Installer repair returned 0 and restored all 19 reviewed files. The same archived job reopened with identical schema-22 database bytes. |
| Uninstall 0.3.8 | Windows Installer returned 0. No owned desktop file or component registry marker remained. The exact newer reader reopened the same archived job with identical schema-22 bytes. Two synthetic copies representing a report and guide at a separately chosen location preserved their bytes. |

| Closed database readback | SHA-256 |
| --- | --- |
| Schema 21 before and after MSI upgrade | `432d83c7946733df333bf8ff10b743a053412b40efc046f79b9398661f0bd412` |
| Schema 22 after application-store migration, older-reader refusal, repair and removal | `b4d1fc92e13fc7f7b0adac9005b3f933aeaaacdf39e09cc92b70e99c91ce1c7d` |

The store checks loaded the byte-exact packaged persistence assemblies using
the isolated fixture. The Windows first-use screen still requested privacy
choices. Installed window verification remains pending for a qualified
desktop. The synthetic copies exercise installer preservation. The exact
production package and controlled-pilot upgrade source retain their own
qualification.

## Repair cancellation and recovery

The same limited profile reinstalled the reviewed 0.3.8 MSI and verified
all 19 files and the Windows Installer product and feature state. The
fixture removed one owned file, `Microsoft.Data.Sqlite.dll`, from its
synthetic install. During repair, the native Installer callback requested
cancellation after independently reading the restored file's exact digest.
Windows Installer returned 1602.

The post-cancellation inventory matched the state before repair. The
target file was absent, the other 18 files matched the package and the
product and feature remained installed. The bounded fixture reopened the
same schema-22 archived job and checked its database and copy digests.
A normal repair returned zero and restored all 19 files. Normal uninstall
returned zero, cleared the product and feature state and removed the
owned payload. The same job and both copies remained readable with
unchanged bytes. The task returned zero in 6.890 seconds.
[Windows Installer callback results](https://learn.microsoft.com/en-us/windows/win32/msi/returning-values-from-an-external-user-interface-handler)

## Fresh-install and uninstall cancellation

The independent 0.3.8 fixture requested fresh-install cancellation after
reading the first new file's reviewed digest. Windows Installer returned
1602 and restored the starting state with no installed files, product,
feature or component markers. The schema-22 job and both selected-copy
sentinels reopened unchanged. The next normal install returned zero.

The uninstall callback observed the same file disappear and requested
cancellation. Windows Installer returned 1602 and restored the 19
reviewed files. The product and feature readback did not match the
pre-uninstall state. The fixture stopped before dispatching any further
operation. A separate guest inspection found the 19 component markers
and the unchanged protected database digest. Automatic registration
rollback remained incomplete.

The recovery fixture bound its operation to the original failure receipt.
It independently verified all 19 files, the MSI package code, the archive
and both copy digests. Windows Installer reported the product and desktop
feature as Advertised, with all 19 component states Unknown. The per-user
component markers remained present.

An exact 0.3.8 reinstall returned zero. The product was installed, its
desktop feature and 19 components were Local, and the package code and
payload matched the reviewed installer. Normal uninstall then returned
zero. Independent readback found the product and feature Unknown, the
component marker key absent and zero installed files. The newer reader
reopened the same archived job with the matching database digest and both
synthetic copy digests matched. The recovery task returned zero in 4.937
seconds. Use the exact-package recovery procedure for this observed state.
[Private installation recovery](../../../docs/uninstall-and-data-preservation.md#update-repair-and-recovery-for-the-private-preview)

## Guest cleanup

The final audit found zero owned desktop files and no component marker key.
The scoped ThirdLife service, scheduled-task and startup inventories held
zero entries before and after the fixture. The fixture account signed out.
Its five completed tasks, account and encrypted host credential copy were
removed. The protected guest staging files, unloaded synthetic profile and
restricted receipts retain the observed state. The archived database digest
matched after the profile unloaded. The sole remaining Windows Installer
process belonged to the Manual-start Windows service in session zero.

## Host verification

The isolated active-host checkout started at
`d57ce297ff57845046bcd9a78afc070ef7db5f2a`. The implementation,
lock files and private package authoring match the 0.3.8 source.
Targeted with locked restore passed in 227.625 seconds, including the 48
selected persistence tests. Eleven focused native package authoring
and verifier tests passed in 1.735 seconds. Full with locked restore
passed 322 repository checks and 2,711 .NET tests in 544.411 seconds.
Full covers deterministic checks. The native transaction receipts above
record the separate guest evidence.

Quick passed all 1,451 tests in 170.733 seconds. A repeat started with
no other active test host and passed in 160.953 seconds. Both exceeded
the 120-second target and the 231-case WPF selection took about 100
seconds in each. The checkpoint changes evidence and documentation,
and the measured timing limit remains open.
