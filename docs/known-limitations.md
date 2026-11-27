# ThirdLife Setup Core known limitations

This page describes engineering evidence for the current standalone
Windows 11 client, native x64 candidate. It applies to the named builds
and environments. Candidate freeze, controlled pilot and stable
distribution have separate records.
[Product contract](product-contract.md) · [Release interface](../RELEASE_INTERFACE.md)

| Operation | Observed scope and remaining limit |
| --- | --- |
| Windows Update | The existing guest scan did not offer an eligible update for the authorized action. Actual installation and independent post-installation verification remain pending until an eligible exact offer is available for the qualification. [Servicing evidence](../artifacts/evidence/TL-0504/report.md) |
| Recipient settings | Assisted Toggle Keys has exact review and reversal controls. Unelevated native state reads and the controlled route were verified. Actual recipient-approved setting mutation and reversal remain separate. [Toggle Keys evidence](../artifacts/evidence/TL-0701/toggle-keys.md) |
| Backup and restore | Assisted File History checks the explicitly selected local drive and can prepare and check a protected sample. A matching fixture copy leaves backup pending. The recipient controls the destination and recovery material. Actual backup and attributable Windows restoration remain unobserved. [Backup evidence](../artifacts/evidence/TL-0702/handover-sample.md) |
| Standalone MSI | The corrected private MSI pair passed install, repair, upgrade, downgrade refusal and removal with synthetic file preservation on the existing guest. Installed window observation, populated SQLite preservation, signing and distribution remain open. [MSI evidence](../artifacts/evidence/TL-0704/interactive-lifecycle.md) |
| Technical accessibility | The named screen layout, keyboard and UI Automation checks cover the reported routes. Physical display settings, perceived announcements and coverage across assistive technologies depend on the recorded scope. [Accessibility requirements](../ACCESSIBILITY.md) · [Handover evidence](../artifacts/evidence/TL-0608/backup-readiness-controls.md) |
| Hardware and resource measurements | The available evidence comes from one active reference machine with named workloads. It establishes the reported bounds and recovery outcomes for those runs. General minimum hardware, other hardware, external lab and peripheral certification remain outside this scope. [Resource evidence](../LOW_SPEC.md) |
| Integration | Core operates within the B1 standalone boundary. Sibling integration and B4 compatibility remain outside this work. [Project boundary](../PROJECT_BOUNDARY.md) |
