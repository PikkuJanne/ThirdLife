# TL-0206 — Device class, repair, and linked retest

The workspace lets the operator select a device class and review saved repair
findings. The bounded policy selector requires an exact class match and
rejects ambiguous matches. Tests compare a 50 percent charge ratio with
explicit synthetic mobile and stationary policies. The original laptop
candidate is the only policy bundled for review. A stationary selection
requires its own policy to evaluate thresholds.

A repair records the component, completed work, operator code, time,
assessment and failure reference. The repair area shows the retained prior
finding, including after a retest and reopen. It offers read-only retest
collection and contains no hardware procedure.

Recording storage replacement and invalidating old sanitization are a single
transaction. The normal assessment route also honors the invalidation.
Linked retest requires a fresh, consistent verified replacement-storage gate.
The orchestrator verifies the original assessment membership and commits the
new assessment start and repair link together under that gate. Interrupted
attempts retain their links and can be followed by distinct retries.

## Verification

Active machine `REF-CODEX-001` revision `2026-08-29.1`, Windows 11 x64,
.NET SDK 10.0.400, on 2026-11-24. Test jobs and observations are synthetic.

| Check | Result |
| --- | --- |
| Pre-edit assessment and audit workflow tests | 42 Inventory and 10 UI tests passed in 12.254 s. |
| Core with `FullyQualifiedName~RepairRetestTests` | Four passed in 2.339 s. Verified typed repair history, atomic invalidation, guarded new retest, and retained evidence. |
| Policy with `FullyQualifiedName~DeviceClassPolicyTests` | Three passed in 3.211 s. Covered distinct thresholds on identical evidence and exact class selection. |
| Inventory focused repair/roster checks | Six passed in 3.866 s. Verified atomic start/link rollback and retry, evidence membership, and reserved marker ownership. |
| Persistence archived mixed-batch check | Passed. The existing stored history and checkpoint remained intact when the repair and invalidation append was rejected. |
| UI with `FullyQualifiedName~RepairWorkflowTests` | Two passed in 6.789 s. Used a disposable SQLite job and a loaded WPF window to check both assessment routes, new evidence, retained history, keyboard navigation, automation names and the polite validation status. |
| `eng/verify.ps1 -Tier Targeted` with the Policy class and Persistence guarded-evidence filters | Passed 1,124 .NET tests including Quick in 59.641 s. Build succeeded with zero warnings or errors. |

The first repository check caught import order in the audit workspace. The
existing import convention was restored before the passing rerun.

## Data and scope

The local workshop store appends bounded typed journal markers and retains
the original and new observations. The existing SQLite schema, guarded
transactions and job evidence limits apply. The repair and retest marker
namespace is reserved against inventory providers. Operator codes and
evidence IDs remain workshop-restricted. The read-only recommendation uses
known candidate failures and adverse storage health observations. It leaves
ambiguous evidence for review and performs no component changes.

No physical repair, replacement-media sanitization, or hardware-class
certification was performed. The original policy remains a candidate.
