# TL-0703 private package ownership

The active Windows 11 x64 host opened the exact unsigned private
0.3.7 MSI read-only through the Windows Installer database API on
28 November 2026. The package verifier checked the independently
reviewed digests and linked the MSI, desktop ZIP and SBOM to
`8440ae0cd7e6797a8c348ce5094396d30e443595`.

| Artifact | SHA-256 |
| --- | --- |
| Private MSI | `f0c914b5e84746c54fcdc1048e68d081b399a118b7b7aee1d290ad5efe7612f2` |
| Desktop ZIP | `0e6f5284816e8f5cd9681892dede324386ccad565b83c32fa739a133c4d30eb6` |
| SBOM | `ec05116c68d7c3416ab0f288ff399fc419ecf3b0772bc75350d4365fa67d3d78` |
| Embedded cabinet | `9c1f0beacdcb83de9d43cead8eca71ab6a81bf5488bc183ad570f045f3d41bc8` |

The native queries accounted for all 15 MSI tables. One desktop
feature owns 19 file components under
`%LOCALAPPDATA%\Programs\ThirdLife Private Preview` and 19 per-user
registration markers under `HKCU\Software\ThirdLife\PrivateInstaller`.
The upgrade table binds the dedicated private product family. The
four custom actions set fixed installer properties. The standard
execute sequence limits file and registry removal to the registered
components. The declared tables include no shortcuts, services or
executable custom actions. The platform check reads the native Windows build's registry
value. Application job data, workshop settings and broker deployment
are separate from these installer resources.

Native extraction in a disposable fixture matched every embedded
cabinet member byte for byte to the corresponding ZIP payload file.
The read-only inventory and extraction took 2.485 seconds. A focused
Windows Installer and package verifier regression passed all 11 tests
in 1.398 seconds. The ownership regression builds a separate two-file
fixture, reads its native database, and supplies the same assertions
used for the 19-file private artifact. A repeat native audit with the
complete property, media and execute sequence assertions passed in
0.143 seconds.

The exact source review compared runtime path and export changes
with the previous registered data map. The protected File History
sample folders and chosen archive destinations are documented there.
The inspection exercised the known package and source. It did not
run setup or inspect any operator or sibling workspace.
[Data locations](../../../docs/data-locations.md)

The independent guest run verified removal and identical populated
job data for the exact private 0.3.6 lifecycle. The 0.3.7 MSI inspection
supplies its package inventory. Stable package inventory, redirected
install paths, installed-window behavior and the separate job-data
deletion workflow remain unverified.
[Exact private 0.3.7 preflight](../TL-0708/reviewed-msi-preflight.md)
[Private 0.3.6 guest lifecycle](../TL-0704/populated-job-lifecycle.md)

## Exact private 0.3.8 after job-data deletion

Two independent clean detached checkouts of
`11d2b792fbaefd23ebd374e4e67d1cd2777f50d2` produced the same private
ZIP, MSI, SBOM and receipts on the active Windows 11 Pro native x64
host. `eng/package-development-preview.ps1 -RunName tl-0703-ownership
-ProductVersion 0.3.8-dev` and the native MSI author took 34.754 and
35.086 seconds including the corresponding checks. An early rehearsal
at `e2d1bdf` used different run names. The generated regex assemblies encoded
those different build inputs. The verified pair held the run name
constant across unequal checkout paths.

| Artifact | SHA-256 |
| --- | --- |
| Private MSI | `37c2e904f24005faa27c0694f5569074e27118ad565ef6bf6934513131aa2319` |
| Desktop ZIP | `2de9c759481ca5ecbe596c2cc88fbd0bdcb6bb7160e2bcac91dee2c145150742` |
| SBOM | `6f0b028af2ee5de1025db999a00499a1fac0956c6bf50d6b7a05f7dc9564d36c` |
| Embedded cabinet | `2c586c80d30dcc2b031b169dc7d9e6fa0591cd3f1608275fff5c68826fbc720f` |

Read-only Windows Installer queries verified the complete 15-table
inventory and isolated extraction matched all 19 native cabinet
members to the reviewed ZIP in 0.245 seconds. The installer owns the
same per-user desktop files and registration scope. The job store,
report and guide roots and original sample root remain application
data. Eleven focused native installer and verifier checks passed
in 1.437 seconds. Runtime ownership and deletion are covered by the
application path tests for the same source. [Job-data evidence](job-data-deletion.md)

The source-path review found the new ownership and approval tables
inside the registered job store, deterministic staging names in the
existing protected output folders, and a directory check restricted
to the selected job's derived path. The installed and runtime paths
are listed in the data map. [Data locations](../../../docs/data-locations.md)

The current `ThirdLife-W11` guest reported Windows 11 Pro 26H2
build 26300.9974 on native x64. The native transaction fixture needs
a signed-in standard-user Basic console session. The current read-only
preflight found no qualified session. The 0.3.8 evidence is limited to
the read-only candidate and host application checks. The exact 0.3.6
native preservation evidence remains applicable to that version.
TL-0704 carries the stable package inventory, installing and removing
that package, and verifying preserved data on the existing guest.
