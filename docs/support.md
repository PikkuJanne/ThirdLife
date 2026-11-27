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

1. Open the affected job and check its visible result and app version.
2. In **Handover**, preview the sanitized support archive. Review the
   exact file list and contents before choosing a protected local folder.
3. If saving was interrupted, select the original folder when prompted.
   TRACE checks the existing bytes against the saved receipt.
4. Forward a reviewed copy only through the route authorized for that
   workshop or recipient. Keep passwords, recovery material, device
   identifiers, full workshop reports and personal files in their
   existing custody.

Security reports require a private route. The exposure record must name
the recipient contact and allowed transfer channel before partner use.
[Security reporting](../SECURITY.md#2-supported-reporting-path) · [Data preservation](uninstall-and-data-preservation.md)
