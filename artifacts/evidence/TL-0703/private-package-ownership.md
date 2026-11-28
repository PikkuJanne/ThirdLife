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
