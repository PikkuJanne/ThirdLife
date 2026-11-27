# ADR 0010 — Recipient-controlled Windows File History

**Status** Proposed within `TL-0702` candidate work  
**Date** 2026-11-27

Windows 11 File History provides a supported setup and restore interface in
Control Panel. The older configuration and service-control APIs carry Microsoft
deprecation notices. Use the Windows interface for configuration and the
backup and restore operations. The proposed application launcher is scoped
to the fixed File History item. [Windows File History](https://support.microsoft.com/en-au/windows/experience/backup-recovery/backup-and-restore-with-file-history) · [Canonical item](https://learn.microsoft.com/en-us/windows/win32/shell/controlpanel-canonical-names) · [API deprecation](https://learn.microsoft.com/en-us/windows/win32/api/fhcfg/nn-fhcfg-ifhconfigmgr)

The read-only candidate checks an explicitly selected ready local volume
separately from the Windows system volume, measures available capacity and
requires a declared backup scope. The recipient retains the destination
selection, credentials and recovery authority in Windows. A review binds to
its current job, handover sequence and transient opaque volume binding.

A configured result requires an independently matched harmless sample
restored into a separate verified location. A later volume, context or
scope change requires a fresh review. The candidate covers the domain and
the read-only destination adapter. The combined launcher write and test
command was rejected by automatic approval review as blocked by policy.
The release candidate still needs the registered launch path, protected
restore probe, and live backup/restore with exact destination authorization.
