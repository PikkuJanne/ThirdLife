# Controlled pilot deployment and rollback

The package currently available is a private, unsigned development preview
for Windows 11 client on native x64. Its WPF app uses the locally installed
Windows Desktop Runtime. The current archive contains application binaries.
ThirdLife pilot exposure has a separate decision at TL-0611.
[Package observations](../../artifacts/evidence/TL-0610/development-payload.md)

## Check a private package

Use the full source revision, archive SHA-256 and SBOM SHA-256 from the
reviewed evidence for the package. Keep those inputs separate from the files
being checked. In the reviewed checkout, use the pinned project Python.

```powershell
python tools/verify_development_package.py $packageDirectory `
  --source $reviewedRevision `
  --archive-sha256 $reviewedArchiveSha256 `
  --sbom-sha256 $reviewedSbomSha256
```

The check verifies the archive and SBOM bytes, matching source, receipt,
bounded flat payload and AMD64 apphost. A mismatch stops package use. Keep
the reported package identity and review it against the evidence before
selecting another copy. This check reports the identity of a private
development build. Signature and distribution approval are controlled at
candidate freeze.

For an authorized engineering smoke check, unpack the verified archive
into a fresh local directory and run `ThirdLife.UI.exe` under the standard
workshop token. Check that Inspect opens and the process exits normally.
Use an isolated job for that check. Keep the archive, its receipt and SBOM
together in restricted local storage.

## Preserve workshop records

Application files and workshop data have different owners. Keep existing
job stores, guide copies, exported reports and support archives in their
registered or explicitly chosen locations. The app can migrate the job
store on open. Starting a previous binary against a newer schema may fail.
Use the documented recovery procedure for the exact schema once qualified.
Preserve the source store and record the last known successful build and
job state while recovering. [Data locations](../data-locations.md) · [Uninstall contract](../uninstall-and-data-preservation.md)

The installer and its upgrade, repair, rollback and removal behavior
still require qualification on the existing `ThirdLife-W11`. Keep this
deployment procedure scoped to private unpack-and-run verification until
the installer and the data-preservation paths have passed.

## Private installer preflight

The private MSI authoring tool takes a separately verified ZIP and SBOM.
From a reviewed Windows checkout with the pinned Python, provide their
reviewed hashes and a new local output path.

```powershell
python -m tools.build_development_msi $packageDirectory $newOutputDirectory `
  --source $reviewedRevision --archive-sha256 $reviewedArchiveSha256 `
  --sbom-sha256 $reviewedSbomSha256 --version $privateMsiVersion
```

The output includes the MSI, its exact receipt and the local authoring
inputs. The package targets Windows 11 client build 26300 on native x64.
The Windows Installer preflight verified and opened the two exact private
versions on ThirdLife-W11. Installed lifecycle and native app launch are
pending. Use the staged runtime only for the scoped qualification process.
The proven unpack-and-run procedure remains available for engineering.
[Installer preflight evidence](../../artifacts/evidence/TL-0704/private-msi-preflight.md)
