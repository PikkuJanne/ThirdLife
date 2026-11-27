# TL-0704 private lifecycle with a populated archived job

On 27 November 2026 the existing ThirdLife-W11 guest ran Windows 11
Pro build 26300.9974 on native x64. A scoped synthetic standard-user
account ran the native installer fixture on its Basic console
session. The fixture verified the expected SID, unelevated token,
profile and exact package digests before dispatch.

| Private MSI | SHA-256 |
| --- | --- |
| 0.3.3 | b83a7e3a9637b3b3cae5566d6881e093173c740b41a6f21340ce2c217fd17b19 |
| 0.3.4 | cce70ae130dbf5eb3bc2f750d7ffd807b057784ff15b21001c334d8269dead55 |

The 0.3.4 ZIP, MSI and SBOM were independently reproduced from source
71ec3b3bda6229d62b38263cb8422606d954e975. The fixture used the
production SQLite store to create one synthetic job, append typed
evidence and archive it after installing 0.3.3. The reopened model
held the same job, observation, archive state and three history
checkpoints.
[Private build and independent readback](../TL-0708/private-reproducibility.md)

The native installer returned zero for clean install, repair, upgrade
and uninstall. Repair reconstructed an exact owned file. All 19
payload files matched the intended installed version after install,
repair and upgrade. The older package was refused with 1603 after the
upgrade, and the newer payload remained intact. Uninstall removed all
owned installation files and preserved the unrelated synthetic decoy.

The production SQLite store reopened and verified the archived job
after repair, upgrade and uninstall. The guarded database bytes were
identical after each of the three checks. The database SHA-256 was
b5167e9c602179fdbc2827a9f48ce228571b309e6a3d3e0bee33d25fb69cbb2a.
The scheduled task returned zero in 31.979 s. The fixture sources and
restricted receipts remain in the ignored local fixture workspace.

The run covers a populated schema-21 store and the exact private
package pair. The app window was outside the data-preservation run.
The earlier blocked window observation still requires an interactive
desktop with its Windows first-use privacy choices resolved.
[Earlier guest lifecycle and desktop observation](interactive-lifecycle.md)

## Native upgrade cancellation and recovery

The first 0.3.4 cancellation fixture watched file action messages. It
received no matching notification and the upgrade completed normally.
The second fixture waited for an independently verified replacement
file inside the transaction and requested cancellation through
Windows Installer. Windows returned 1602. It restored all 19 old
payload bytes and the archived job. The subsequent uninstall returned
zero but left the 19 files. The engine reported the old feature as
Advertise and every component as Absent. Reinstalling the exact 0.3.3
package restored its registration. Its normal uninstall then removed
all 19 owned files and preserved the same job. This recovery was
verified in the fixture against the exact schema-21 store.
[Windows Installer external cancellation](https://learn.microsoft.com/en-us/windows/win32/msi/returning-values-from-an-external-user-interface-handler)

A diagnostic 0.3.5 build moved removal of the previous product after
InstallExecute and before InstallFinalize. Windows Installer then
recognized the newer product but retained thirteen older application
files under its normal file version rules. The diagnostic package was
removed through Windows Installer before starting the corrected run.
The 0.3.6 authoring uses that late removal sequence, resets the upgrade
lists before installed-product discovery, and selects exact file
replacement only during a detected major upgrade. The fixed property
assignments and standard actions operate within the per-user package.
[Removal sequence](https://learn.microsoft.com/en-us/windows/win32/msi/removeexistingproducts-action)
· [Related-product discovery](https://learn.microsoft.com/en-us/windows/win32/msi/findrelatedproducts-action)
· [File replacement](https://learn.microsoft.com/en-us/windows/win32/msi/reinstallmode)

### Corrected private 0.3.6

The source is fa2fa35812a8960a35c3131bc902f2d7eee49b39. Two clean
host checkouts produced identical ZIP, SBOM, MSI and receipt bytes
in 39.291 s and 39.655 s. The independent verifier checked the second
build against the reviewed hashes and accepted all 19 members and the
AMD64 apphost. Ten focused authoring and package-verifier checks passed.
Read-only inspection of the exact MSI found 15 declared tables, four
fixed property actions and 19 per-user registry rows. The native
cabinet extracted from the embedded stream matched all 19 ZIP members.
An isolated installer session confirmed that both fixed list resets
clear synthetic preloaded values before discovery.

| Exact artifact | SHA-256 |
| --- | --- |
| 0.3.6-dev ZIP | 44e87e7f973a9c5bddab497025f610a445bc307b005b9df408fd542871d6eff0 |
| 0.3.6-dev SBOM | 7c8ced8ac671741c72154b3ac1da80aa269946d27cea31b3522db9148c8df525 |
| 0.3.6 MSI | 1a4a679db5cdc0cdd88c01277acd5aa9064a40582f05bd44d91c0dcbb3b25c9c |

The same guest fixture independently observed a verified new file,
requested cancellation and observed Windows Installer result 1602.
All 19 previous payload bytes and the archived job remained
readable. Normal uninstall of that recovered version removed all
19 owned files and left the matching job database. The subsequent
ordinary upgrade to 0.3.6 installed all 19 reviewed bytes,
reconstructed the missing runtime configuration during repair,
refused downgrade with 1603 and removed the private package.
Reopening the protected data after the final uninstall verified
the original observation, archive state, checkpoints and same
database hash. The corrected fixture returned zero in 34.195 s.

Six completed fixture tasks and the disposable account were removed.
The fixture signed out and the encrypted host copy of its disposable
credential was removed. Secondary Logon was Manual and Stopped.
The protected guest stage
C:/ProgramData/ThirdLife-PrivateLifecycle-7043110b26ee4a50919c110d9decbe6c
and the unloaded synthetic profile retain the restricted receipts
and test job. The exact installation contained zero files after
removal.

The exact 0.3.6 ZIP also opened Inspect under the active host standard
user token in 1.518 s and closed normally by 1.717 s. The driver
verified all 19 unpacked bytes before starting the app. The tested UI
assembly SHA-256 was
1c85b885823410e0c7e7bbce2230a5c3d0e3d0eea56a8c0c9ac0cae78ee3428f.

Clean detached Full verification at the same source passed 320 repository
checks and 2,688 .NET tests in 600.796 s.

The installed-window observation, recovery across a changed SQLite
schema, interruption at other installer phases and final stable
qualification remain open.
