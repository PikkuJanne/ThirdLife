# TL-0705 support and limitation evidence

This is a source and evidence review for the private engineering
candidate. It records which claims already have a measured basis and
which still need exact release scope.

| Subject | Evidence used | Limit |
| --- | --- | --- |
| Security and privacy | Current privileged-route, bounded local storage and support-preview review. [Security and privacy audit](../../audits/v0.1-security-privacy.md) | The repository's private vulnerability-reporting endpoint returned disabled on 27 November 2026. The release security contact and handling promise remain unapproved. |
| Accessibility | Earlier end-to-end keyboard and UI Automation observations plus isolated high-contrast and scaled Handover coverage. [TRACE audit](../TL-0413/report.md) · [Handover keyboard evidence](../TL-0608/backup-readiness-controls.md) | Each result applies to its recorded build, path, viewport and method. The scoped fixtures do not establish current audible output or other hardware. |
| Resources | Single-worker and four-worker comparison, reference-machine profile, and hashed workload inputs. [Resource metrics](../TL-0112/resource-metrics.json) · [Input hashes](../TL-0112/inputs.json) · [Reference machine](../../../docs/testing/reference-machine-profile.md) | In-memory assessment fixture. The separately tested 350 ms destination delay used one worker and software rendering. File/network or cross-hardware performance is outside these samples. [Delayed destination](../TL-0702/handover-sample.md) |
| Support and lifecycle | Protected exact support preview and interrupted publication recovery. [Support recovery](../TL-0606/reopened-support-recovery.md) · [Private MSI preflight](../TL-0704/private-msi-preflight.md) | No transfer, release signature, support commitment, or standard-user installed MSI lifecycle was observed. |

The candidate maintenance, contact and limitation pages remain usable
while the release-specific decisions and observations are pending.
[Support](../../../docs/support.md) · [Known limitations](../../../docs/known-limitations.md)

## Verification

On 27 November 2026 the active Windows 11 native x64 host ran
`python tools/validate_bundle.py` and
`python tools/validate_repository.py`.
The three focused security, privacy and testing document contract
checks passed in 1.179 s. The local link pass resolved 25 references
in the support, limitation and evidence pages. The link to the security
reporting heading and the public repository setting were reviewed
directly. This checkpoint documents the named evidence and stops
short of release support or disclosure commitments.

## Support export and recovery documentation

The support procedure now follows the controls in Handover. An operator
reviews and approves the two-file ZIP before choosing a local folder.
The current window can retry saving the publication record. After
reopening, the operator may select an unconfirmed archive by code and
verify the existing file against the recorded digest in the original
folder. A missing, inaccessible or changed file leaves its outcome
unconfirmed. The earlier fixture preserved the approval actor, recorded
the verifying actor and kept the chosen path out of the audit. The
documentation uses those measured behaviors and retains the approved
route requirement for sharing.
[Support procedure](../../../docs/support.md#recover-an-interrupted-export) · [Workspace guidance](../../../docs/ui/audit-workspace.md#sanitized-support-archive) · [Security policy](../../../SECURITY.md#9-data-secrets-and-logging) · [Verified recovery](../TL-0606/reopened-support-recovery.md)

The accessibility claim still covers the recorded keyboard and UI
Automation routes, including the named 240 DPI run. The resource claim
still covers the hashed in-memory workload and the separate simulated
destination delay. Audible perception, broader device coverage and
production performance budgets remain open. Confidential support
handling and the exact stable lifecycle retain their release prerequisites.
[Accessibility audit](../../audits/v0.1-accessibility.md) · [Resource metrics](../TL-0112/resource-metrics.json) · [Delayed destination](../TL-0702/handover-sample.md)

On 28 November 2026, the active Windows 11 native x64 host checked the
documentation against base `d57ce297ff57845046bcd9a78afc070ef7db5f2a`.
The manual reading-order review traced the preview, publication and
recovery controls in their respective display states. A local Python link and
XAML comparison resolved 48 references and checked five exact control
labels in 0.729 s. Three focused Python security, privacy and testing
document checks passed in 1.136 s.

The first `eng/verify.ps1 -Tier Targeted -Restore` invocation verified
repository governance, restored the locked graph and built Release
with zero warnings and errors. All 1,451 Quick and three selected
WPF support cases passed. The Reports filter matched no case and the
wrapper exited 1 after 235.894 s. The corrected `dotnet test` selection
`FullyQualifiedName~Support` in `ThirdLife.Reports.Tests` passed both
cases in 150 ms of test time, including rejection of changed ZIP bytes.

The governed rerun passed in 210.505 s with the locked assets already
restored. All 1,451 Quick, seven selected WPF and two selected Reports
cases passed with zero skips. The new result covers local engineering
verification. Confidential transfer, exact stable installed behavior
and support commitments remain subject to their existing prerequisites.

The integration with the TL-0704 checkpoint updated the installer
limitation and retained the verified recovery evidence. Bundle and
repository validation and 71 local references across the support and
continuity pages passed on the integrated documentation.

```powershell
.\eng\verify.ps1 -Tier Targeted `
  -TestProject @('tests/ThirdLife.UI.Tests/ThirdLife.UI.Tests.csproj',
                 'tests/ThirdLife.Reports.Tests/ThirdLife.Reports.Tests.csproj') `
  -Filter 'FullyQualifiedName~Support'
```
