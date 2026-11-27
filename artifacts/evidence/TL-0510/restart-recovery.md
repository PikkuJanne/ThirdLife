# TL-0510 unresolved servicing and restart clearance

Verify can receive an empty configured Windows Update scan and a false
restart flag while earlier servicing recovery is still unresolved. In this
state the restart check remains unavailable. A current affirmative restart
indication remains a failed check. Finalization also holds a persisted
historical pass when the same result reports unresolved update recovery.
Earlier verification values and provenance remain saved.

The activity uses the adopted restart requirement and preserves the
separate scope of the Windows Update restart flag. The Windows Installer
pending-file-rename property also covers a limited condition. Positive
aggregate clearance remains required for the native full-profile case.
[Windows Update Agent contracts](https://learn.microsoft.com/en-us/windows/win32/api/_wua/) ·
[Windows Installer restart property](https://learn.microsoft.com/en-us/windows/win32/msi/msisystemrebootpending)

On 27 November 2026, the active Windows 11 host passed seven focused
ProfileVerificationTests cases in 649 ms and six focused durable
maintenance cases in 18 s. The controlled tests exercised an empty scan,
a clear indication, unresolved recovery and the retained older result.
The baseline for the focused change was `1fff725`. The provider and workshop
record from `4eb5f4f` are the integration base. No live scan, dispatch,
sign-off or restart occurred for this correction.

The integration Targeted selection passed in 295.294 s. It ran Quick,
seven Verification cases and six Persistence cases with zero skips
under the locked Release build with zero warnings or errors. Quick
covered 1,382 .NET cases and the eight work-selection regressions.
The merge trial against main `244e51d` passed both repository validators
and eight focused maintenance and handover-mode persistence cases in
20 s of test time.
