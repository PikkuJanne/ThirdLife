# TL-0702 recipient File History readiness

TRACE Handover adds a read-only readiness check for the present recipient.
The operator enters the estimated scope and confirms that the recipient
controls recovery. The selected local volume must be ready, have a stable
native volume identity distinct from the Windows system volume, and have
capacity for the declared scope. The in-memory review uses an opaque volume
binding. Sealed handover leaves backup pending without showing the check.

The model rejects stale handover, changed destination and insufficient
space before a reviewed action. It distinguishes pending, unbound,
mismatched and verified harmless-restore observations. Only controlled
tests provide restore samples at this checkpoint. The screen displays
prerequisite results and retains backup pending.

On 27 November 2026, active Windows 11 native x64 host and .NET SDK
10.0.400, the composed source `c9446391db4d742e9aa464c901c755cd40f51569`
passed Targeted with locked restore in 218.210 s. It ran 1,446 Quick and
nine selected .NET tests without skips. The focused selection covers the
File History model and WPF, persisted handover integration, and host
admission and native system-volume rejection. The native check observed
the presence of the Windows File History interface files and refusal of
the system volume, subfolder and invalid route. A first composed run
using partially restored project assets failed to compile. The governed
locked restore resolved that failure. The integration also includes
`c212728172dab2ac6810839f22268382ba6bc047`, which adds documentation and
does not change the composed app, tests or verification runner.

Automatic approval review rejected the combined command to write and
test the proposed fixed File History launcher as blocked by policy.
That launcher is pending. No Windows backup configuration or restore ran.
The protected restore probe and the native live evidence for an eligible
isolated destination remain open. Actual recipient scope, destination and
recovery control remain subject to exact authorization.
[File History route](../../../docs/adr/0010-recipient-file-history.md)
