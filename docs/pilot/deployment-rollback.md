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
candidate freeze. For the companion private MSI, add
`--installer-directory`, `--installer-sha256` and `--installer-version`
to the same command. Use the version and MSI digest from the separate
reviewed record. The preflight checks the MSI bytes and its receipt
against the ZIP, SBOM and source before any installer execution.

For an authorized engineering smoke check, unpack the verified archive
into a fresh local directory and run `ThirdLife.UI.exe` under the standard
workshop token. Check that Inspect opens and the process exits normally.
Use an isolated job for that check. Keep the archive, its receipt and SBOM
together in restricted local storage.

## Preserve workshop records

Application files and workshop data have different owners. Keep existing
job stores, guide copies, exported reports and support archives in their
registered or explicitly chosen locations. The app can migrate the job
store on open. Preserve the source store and record the last successful
build and job state while recovering. Use the reviewed build compatible
with the saved schema.
[Data locations](../data-locations.md)
[Update, repair and recovery](../uninstall-and-data-preservation.md#update-repair-and-recovery-for-the-private-preview)

The reviewed private 0.3.3 and 0.3.6 guest lifecycle preserved a
populated archived job through cancellation, repair, upgrade and
uninstall. In the separate 0.3.6-to-0.3.8 run, MSI upgrade preserved
the schema-21 database bytes before the exact newer persistence reader
migrated the store to schema 22. The older reader refused the migrated
store. The newer reader reopened the same job after repair and
uninstall. Installed window and controlled-pilot qualification remain open.
[Exact private lifecycle](../../artifacts/evidence/TL-0704/populated-job-lifecycle.md)
[Private schema-22 lifecycle](../../artifacts/evidence/TL-0704/schema-22-private-lifecycle.md)

With the earlier 0.3.4 installer, cancellation can restore all old
files without fully restoring the old product registration. If the
exact previous files have been independently verified and the job
store is still at schema 21, use the reviewed previous 0.3.3 package to
reinstall its registration. Follow with its normal uninstall only
when application removal is intended. The tested recovery preserved
the archived job. Keep the job store and exported copies in place
throughout recovery.

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
The corrected 0.3.2 and 0.3.3 pair used a standard-user fixture on the
existing guest. The 0.3.3 desktop payload also opened Inspect unelevated
from its independently verified unpacked ZIP on the host. Use the staged
runtime for the scoped qualification process.
[Guest MSI lifecycle](../../artifacts/evidence/TL-0704/interactive-lifecycle.md)
