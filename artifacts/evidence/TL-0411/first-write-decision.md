# TL-0411 first write-capable product and security decision

**Prepared** 2026-11-26 (Europe/Berlin)  
**Product candidate** `a637198615ca674a8ca8a2fcde0e62edd4ca3d7f`  
**Decision scope** Internal M4 package-installation qualification  
**Requested authorities** Accountable product owner and security owner

## Proposed disposition

Approve the first write-capable slice for internal M4 use on the active
Codex machine or its admitted disposable guest, running supported Windows 11
client on native x64. The sole write is the machine-wide
installation of Notepad++ 8.9.9.1 by the Windows-owned installer after
the ordinary unelevated ThirdLife application previews and durably records
approval of the exact plan. Any Windows consent applies to that action.
Require independent exact machine registration and the disclosed isolated
empty-launch probe before the job can record verified success. Preserve
requires-review outcomes, bounded proven-safe retry, and the workshop
report's record of the original attempt.

| Reviewed package input | Bound value |
| --- | --- |
| Source and package | `notepad-plus-plus-releases` / `Notepad++.Notepad++` |
| Artifact and scope | `npp.8.9.9.1.Installer.x64.msi`, signed vendor release, native x64, machine-wide |
| MSI SHA-256 | `e3b0607a092ce2604fbdac027db2c94915995e8e568ebd226f8c5493c2f61a80` |
| Catalogue and policy validity | Until `2026-12-25T00:00:00Z`. A fresh reviewed input is required on expiry |

The scope permits direct vendor acquisition for local use. Separate
decisions cover controlled pilot exposure (`TL-0611`), public release
(`TL-0710`), any vendor binary distribution, and any additional write route.
An approval of this packet gives no action-specific authority. Any
material change in source, version, installer, package trust, requested scope
or privilege route requires applicable review and fresh plan approval.

## Evidence and residual risks

The active-machine gate checks bind candidate source, locked inputs, the
failure matrix and reviewed security and privacy controls. The actual
Hyper-V guest transactions used Release artifacts from
`20247fbd3c785bf6b85e7e7c8f98ac6aa22a46e7` with unchanged production and
test inputs at the gate candidate. The ordinary app verified the machine
installation, preserved the same job and workshop report through an actual
warm restart, and withheld handover in separate native decline and
installer-client interruption cases. The guest was restored and independently
checked for residue. [Gate evidence](../../gates/M4-package-vertical-slice.md) ·
[Native run provenance](../../e2e/M4-first-write-slice.md)

- **Local and upstream trust.** The route relies on the Windows host, its
  local administrators, and the reviewed signed upstream artifact.
  Restrict acquisition to the pinned source and exact bytes, verify
  Windows signature trust, and stop on a trust or ownership mismatch.
  Keep ordinary consent and the exact preview bound to each action.
  [ADR 0009](../../../docs/adr/0009-winget-backend.md) ·
  [Threat model, RR-001, RR-002, RR-004 and RR-005](../../../docs/security/threat-model.md#residual-risk-register)
- **Partial machine change.** The Windows Installer service may continue
  after the app-owned client exits. The interruption case subsequently found
  exact MSI registration while the saved job retained failure. Require
  review and fresh observation where state is uncertain. The slice offers no
  automated rollback. The isolated restore was specific to the validation
  guest. [Native run provenance](../../e2e/M4-first-write-slice.md)
- **Reboot and evidence coverage.** The approved installer completed without
  a native MSI restart-required result. The current/later boot and atomic
  checkpoint cases use fixtures. The separate actual warm restart establishes
  same-job continuity. Preserve later cold-start and final cold-boot acceptance
  requirements, fixture scope for the recipient draft, and the technical
  accessibility limits. [Verification and cold boot](../../../DECISIONS.md#d-033) ·
  [Recipient draft](../TL-0410/recipient-draft.md)
- **Privacy and distribution.** Vendor acquisition reveals ordinary connection
  information, and Windows may check certificate-chain status. Keep workshop
  evidence restricted and retain typed package diagnostics. Recipient
  guide and sanitized support archive delivery follow their own later
  requirements. Mirroring, distribution caches, bundling and redistribution
  remain outside this scope. [Privacy model](../../../docs/privacy/privacy-model.md) ·
  [Licence matrix](../../../docs/supply-chain/license-matrix.csv)

Record one **approve**, **hold**, or **decline** disposition, naming the
decision-maker and applicable role or roles, date, reviewed candidate and
packet, and any rationale or conditions. The gate remains pending while the
product and security disposition is outstanding.
