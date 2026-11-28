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

On ThirdLife-W11, removal of the exact private 0.3.6 MSI removed its
19 registered desktop files. The lifecycle fixture reopened the same populated
archived job after repair, upgrade, cancellation recovery and final uninstall.
TL-0704 verifies removal and preservation for the exact stable package, including
retained jobs, adjacent unrelated decoys and lifecycle interruption.
[Private populated lifecycle](../artifacts/evidence/TL-0704/populated-job-lifecycle.md)
[Retention and cleanup](retention-and-cleanup.md)
