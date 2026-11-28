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
the recorded local reports, guides and generated original File History samples
with their saved lengths and digests. The review reports matches and
unverified counts for each group. Earlier files without a receipt may remain.
Recovered samples and exported copies follow the retention of their destinations.
[Retention and cleanup](retention-and-cleanup.md)

Application removal and job-data deletion use separate choices.

- **Remove the application.** Remove only owned application, shortcut, registration
  and broker deployment artifacts confirmed by the actual release package. Preserve
  the workshop job store, job-bound evidence and local reports and guides by default.
  Published ZIPs stay in the chosen destinations. Windows-owned accessibility
  settings, backup selections and installed vendor packages stay with their owners.
- **Delete ThirdLife-owned job data.** This choice remains pending. It requires
  explicit approval of the affected records, verification of the registered root,
  exact job identities and final file handles, and a recoverable result identifying
  retained data after failure. Exported copies and unrelated directories follow
  their owners' retention policies. [Registered data roots](data-locations.md)

The deletion workflow needs a durable approval tied to the selected archived
job and its exact records and attributed local files. If an approved file step
is interrupted, reopening the job needs to show the outstanding operation and
which file results remain unverified. Job-record removal follows reconciliation
of those file results. Files without reliable job attribution need independent
review under the workshop's retention policy.

Report and guide receipts are saved after publication. Interruption can leave
a local file with no saved receipt. Reports generated from archived jobs also
remain outside the handover receipt workflow. A durable ownership record before
publication is needed to include future copies reliably in a deletion review.

On ThirdLife-W11, removal of the exact private 0.3.6 MSI removed its
19 registered desktop files. The lifecycle fixture reopened the same populated
archived job after repair, upgrade, cancellation recovery and final uninstall.
The exact stable package and separate deletion workflow require their own
verification with retained jobs, adjacent unrelated decoys, locked and denied
paths, links, interruption and later reopening.
[Private populated lifecycle](../artifacts/evidence/TL-0704/populated-job-lifecycle.md)
[Retention and cleanup](retention-and-cleanup.md)
