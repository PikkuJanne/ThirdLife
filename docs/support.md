# ThirdLife Setup Core support

ThirdLife Setup Core is in active development. The current packages are
private engineering artifacts. Supported versions, the release maintenance
window and the approved recipient contact are still open. See the recorded
release status before providing a package to anyone.
[Release interface](../RELEASE_INTERFACE.md)

## When to stop work

Stop the affected operation if the reported package or publisher changes,
an unexpected privileged action appears, an independent check fails,
or data may have been exposed. Keep the current job and its saved
disposition. Reopen the same job after interruption and read the Verify
or Handover state before retrying. Leave the device in workshop custody
while a required handover check remains unresolved.
[Operator runbook](pilot/operator-runbook.md)

## Prepare a support record

1. Open the affected job. In **Handover**, choose **Preview support
   archive** and review both files, the ZIP size and the digest.
2. Enter your workshop operator code and approve the exact preview.
   Select **Choose folder and export** and choose a local location for
   the protected `ThirdLifeSupport` folder.
3. Check the publication status before sharing. If the job shows an
   unconfirmed archive, follow the recovery procedure below.
4. Share the confirmed, reviewed copy only through the route authorized
   for that workshop or recipient. Keep passwords, recovery material,
   device identifiers, full workshop reports and personal files in
   their existing custody.

## Recover an interrupted export

If the current window offers **Retry saving the export record**, use it
to finish recording the publication. After reopening the job, select its
code under **Unconfirmed support archive**, enter your workshop operator
code, and select **Choose original folder and verify archive**. Choose
the same local parent folder you selected for the export. ThirdLife checks
the existing ZIP against the saved digest and records who verified it.
An inaccessible, missing or changed copy keeps the export unconfirmed.
Keep any related copy in local custody while its result is unconfirmed.
[Verified recovery](../artifacts/evidence/TL-0606/reopened-support-recovery.md)

Security reports require a private route. The exposure record must name
the recipient contact and allowed transfer channel before partner use.
[Security reporting](../SECURITY.md#2-supported-reporting-path) · [Data preservation](uninstall-and-data-preservation.md)
