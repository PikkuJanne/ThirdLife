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
