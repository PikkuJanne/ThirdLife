# TL-0702 File History restore attribution review

On 27 November 2026 the active Windows 11 native x64 host enumerated
the registered File History provider manifests with the unelevated
`Get-WinEvent -ListProvider '*FileHistory*'` route. The descriptions
identified two restore-related events in the registered manifests.
Both serve diagnostic failure cases.

| Provider and event | Observed manifest meaning |
| --- | --- |
| FileHistory-Core 205 | Windows could not restore some files and instructs manual recovery. |
| FileHistory-UI 68 | The restore destination was unavailable. |

Both events carry unstructured message text. The attempt to inspect
the named BackupLog channel under the standard user token returned
access denied. The review did not collect live event payloads, read
recipient paths, change channel access, or change any File History
setting. The reviewed manifest entries supply no positive attribution
for the prepared sample.

The protected file comparison continues to distinguish missing,
mismatched and equal bytes. Positive native attribution remains a
separate live requirement. A later candidate needs a supported
evidence route bound to the generated sample and exact Windows
restore. [Current sample workflow](reopen-sample.md) · [Windows File History](https://support.microsoft.com/en-us/windows/experience/backup-recovery/backup-and-restore-with-file-history)
