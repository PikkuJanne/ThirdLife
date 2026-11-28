# Uninstall and data preservation

The private per-user MSI registers **ThirdLife Setup Core (Private Preview)**
with Windows Installer. Use that product's Uninstall action in Windows
Installed apps under the account that installed it. Windows Installer removes
its registered desktop files and component markers. The private development ZIP
runs from its extraction folder. The broker provisioner has its own deployment
scope. Stable install, removal and package inventory verification remain in
TL-0704.
[Private populated lifecycle](../artifacts/evidence/TL-0704/populated-job-lifecycle.md)
[Private package ownership](../artifacts/evidence/TL-0703/private-package-ownership.md)
[Broker provisioner](../eng/install-thirdlife-broker.ps1)

## Update, repair and recovery for the private preview

Sign in as the same standard Windows user who installed the preview. Close
ThirdLife and check the selected MSI against its reviewed package record.
Use the newer reviewed MSI for an upgrade and the exact installed version
for repair or removal. The private lifecycle fixture uses Windows Installer
with `/i` for installation and upgrade, `/fa` for repair, and `/x` for removal,
followed by the exact MSI path. It uses `/qn /norestart` and keeps installer
logs in restricted local storage. After an upgrade or repair, reopen the job
and check its saved history before continuing workshop work.
[Private package preflight](pilot/deployment-rollback.md)
[Windows Installer options](https://learn.microsoft.com/windows-server/administration/windows-commands/msiexec)

If a private 0.3.8 uninstall is canceled, first check the owned files and
Windows Installer registration. The guest observed all 19 files restored
with the product and desktop feature still advertised. For that verified
state, reinstall the same reviewed 0.3.8 MSI under the same Windows user
and check that the desktop feature and components are installed. Then use
normal uninstall if application removal is intended. If the files or MSI
identity differ from the reviewed package, keep the job store and installation
as found for support review.
[Verified private recovery](../artifacts/evidence/TL-0704/schema-22-private-lifecycle.md#fresh-install-and-uninstall-cancellation)

The newer app can migrate the job store the first time it opens it. Before
upgrading, save the workshop report and recipient guide to their intended
protected destinations. The exports preserve readable copies of those
documents. After migration, use a reviewed build that supports the saved
schema. If the previous build refuses the newer store, keep the database
and its rollback journal in place. Reinstall a compatible reviewed build,
reopen the same job and check its history and saved copies. If the
compatible build also fails to reopen it, preserve the store for support.
[Registered data roots](data-locations.md)
[Support](support.md)

## Keep or delete job data

The **Jobs** surface can archive the current job and restore it later
by job code. The job keeps its evidence and records both transitions.
For an archived job, **Check saved copies and original samples** compares
report and guide handover receipts and recorded original File History samples
with their saved lengths and digests. The separate deletion review also includes
ownership reservations made before publication. Earlier files without a reliable
receipt and copies at other destinations follow their own retention arrangements.
[Retention and cleanup](retention-and-cleanup.md)

Application removal and job-data deletion use separate choices.

- **Remove the application.** Remove only owned application, shortcut, registration
  and broker deployment artifacts confirmed by the actual release package. Preserve
  the workshop job store, job-bound evidence and local reports and guides by default.
  Published ZIPs stay in the chosen destinations. Windows-owned accessibility
  settings, backup selections and installed vendor packages stay with their owners.
- **Permanently delete job data.** Open the job in **Jobs** and archive it if
  needed. Select **Review job-data deletion** to check the archived job code and
  its recorded local files. Enter your workshop operator code, confirm the
  displayed scope and select **Permanently delete job data**. Pending action
  recovery or support publication can make deletion unavailable. For an older
  archive with a later evidence checkpoint, restore the job and archive the
  current records before preparing a new deletion review.

The job store saves the approval against the exact archive checkpoint and local
file manifest before attempting removal. It checks the registered roots and
verifies the names, lengths and digests on the held file handles. An absent file
can be reconciled on retry. An unavailable, changed or unsafe file retains the
job and approval. Review the retained file names, address locks or other
ownership problems, then reopen the job and choose **Continue approved job-data
deletion**. Opening a pending job displays its state. The operator starts the
retry. Restore is unavailable while deletion is pending.

Successful removal deletes the job's SQLite records and its recorded local
reports, guides and original File History samples. It retains policy snapshots
referenced by other jobs. New local reports and guides have job-bound ownership
records before publication. Confirmed staging copies use the same recorded ID.
Historical copies without reliable receipts, exported copies, recovered samples,
Windows backups and unrelated directories retain their own retention arrangements.
[Registered data roots](data-locations.md)

Schema 22 stores local ownership and deletion approvals. Ordinary application
removal preserves the existing schema version. Reinstall a build that supports
the saved schema to reopen the database. Earlier builds report a newer store
as unavailable and leave it in place.

On ThirdLife-W11, the private 0.3.6 cancellation run recovered the
previous installer and the archived job. The separate 0.3.6-to-0.3.8
run preserved the store during MSI upgrade. The newer persistence reader
migrated it to schema 22. The same archived job and synthetic report and
guide copies survived repair and removal. The exact stable package still
requires lifecycle and data-preservation verification in TL-0704.
[Private populated lifecycle](../artifacts/evidence/TL-0704/populated-job-lifecycle.md)
[Private schema-22 lifecycle](../artifacts/evidence/TL-0704/schema-22-private-lifecycle.md)
[Retention and cleanup](retention-and-cleanup.md)
