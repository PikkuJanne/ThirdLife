# TL-0111 — Network and key-device enumeration

**Date:** 2026-10-01  
**Branch:** `codex/tl-0111-device-enumeration`  
**Base:** `72c26dc808e9b24a6fb6dd431f205957eb41fd58`  
**Runtime authority:** active Codex machine only  
**Status:** done; normal PR integration pending

## Continuity and scope

PR #11 merged TL-0110 into main before this task began. Its final head `1c2249a` is an
ancestor of the base. Local main fast-forwarded; every transitive TL-0111 dependency was
parsed and verified done. The SSH origin remained unchanged; the existing process-scoped
HTTPS bridge completed fetch after the SSH attempt stalled. No history was rewritten,
branch retired, prior PR closed, sibling inspected or next task started. The unrelated
untracked Word reference is preserved.

## Acceptance evidence

| Requirement | Implementation and proof |
| --- | --- |
| Eleven required device classes | Fixed network numeric projection and SetupAPI/session sources; typed provider maps Wi-Fi, Ethernet, Bluetooth, audio, camera, microphone, display, keyboard, pointing, touch and USB controllers to owned evidence keys. All class outcomes and camera/touch variants have deterministic tests. |
| No network identities or personal device names | Source DTOs contain only enums, booleans and bounded integers. Queries omit identities. Exact native imports, closed API surface, numeric WMI properties, malformed private strings and exception canaries are checked. No log/support allowlist was expanded. |
| Errors attach to the relevant class | Per-class collection status and numeric error, unknown affected presence, retained independent facts; malformed/denied/unavailable/missing/duplicate/oversized cases tested. |
| Presence is separate from function | `devices.functional_test=not_performed`; endpoint/interface/session keys explicitly state their source scope. Empty interface lists do not prove physical absence. Three-case focused regression covers the corrected naming. |
| Read-only provider | Exact System32 read/status/free imports and fixed local WMI projection. No commands, device configuration, recording, elevation, network request or filesystem mutation. Native resources release in finally; tests cover cancellation, cleanup and admission. |
| Synthetic missing/error device trees and webcam/touch variants | Source fakes cover zero/one/two/256 cameras, overflow, device problems, missing/denied/error devices and touch/pen/zero/unknown flags. Provider fixtures cross camera/touch presence variants and every class status. |
| Representative runtime verification | Separate unelevated active-machine observation passed. Other physical variants use deterministic fixtures under D-059/D-065/D-066; no hardware certification or functional-test claim. |

## Verification

Exact commands, failed attempts and durations are in [runs.json](runs.json). Baseline passed
25 tests. Focused new-provider/source/boundary run passed 201; the interface-absence
regression passed three. No test was skipped or disabled in those runs.

The host Targeted run passed governance, formatting, a zero-warning Release build,
Core79 and Inventory824. Its UI phase failed to load the unchanged UI assembly with
Application Control error `0x800711C7`. A single assembly test reproduced the same load
failure before its body. Host security remains enabled; the established same-machine
Sandbox runner is used to finish required Quick/Targeted coverage.

The transient same-machine Sandbox Targeted run passed in 137.344 seconds (199.616
seconds including preparation). It ran Quick governance/format/build, Core79,
Inventory824 and UI3, then the Inventory824/UI3 targeted selection; zero failures or
skips. Source digest: `d40593b7570cf7f2df2afa1e7188a8c270b100d2681c92ba45411da951dbcc20`.
The complete Python governance suite passed 261 tests in 283.387 seconds. The focused
Sandbox/manifest contract suite passed 40 tests in 93.418 seconds. Preflight passed.

The existing D-070 runner gained only TL-0111 Quick/Targeted declarative profiles and
their exact allowlist contracts. A 4 GiB same-machine guest restores locked dependencies;
networking is restricted by purpose to governed NuGet audit. Source/harness/configuration
hashes, bounded results, timeouts and cleanup remain enforced.

Committed-source Targeted passed in 123.805 seconds (180.917 seconds total), with the same
79/824/3 and 824/3 counts, zero failures/skips and a zero-warning Release build. The
tracked-clean source was unchanged throughout the run. Its bound implementation commit is
`eb83e4f26c292498f2d9223f12a3adeea402f1bc`; source digest is
`6698f9a5abf53009c424f2a9fa0bfc30d3fa3fd2700e8ef28c49aa21cad853e6`.
The append-only result and manifest under `artifacts/audit/TL-0111/` retain hashes for source,
history, harness, guest configuration, locked package closure and the bounded sanitized
result. Result SHA-256 is
`de72c6d4f1556480c06907cf83b324b93006ea9b11d27bbc9388a9d3279af3e1`.
Runner cleanup completed. No raw guest output or machine identities were retained.
Final handoff changes only task state and evidence; all implementation/test/harness input
hashes still match the verified snapshot. Fresh bundle/repository controls check that handoff.
PR-triggered remote CI is supplementary and is not required evidence. A final handoff edit initially decoded
UTF-8 as the Windows default encoding, changing punctuation in three unrelated YAML lines
and the report title. Bundle validation caught it; explicit UTF-8 restored the text. A
structural comparison confirms only TL-0111 status/evidence differ from the checkpoint.

Full is not triggered: no milestone/release gate, major refactor, migration or dependency
change. Extended is not triggered: task trigger lists are empty, and bounded lifecycle,
malformed-input and overflow tests answer this provider's risk within Targeted. No host
network disruption, cold boot, driver change or peripheral exercise was performed.

## Active-machine observation and resources

[active-machine.json](active-machine.json) preserves the minimized final observation.
Network WMI returned access denied `0x80041003`; Wi-Fi/Ethernet remain unknown. Camera,
audio render/capture, monitor, keyboard, pointing, touch-session and USB host-interface
presence were observed. No Bluetooth radio interface was enumerated. None of these facts
establish physical function or physical absence outside the named source scope.

Collection took 121.7843 ms; test-host CPU delta 109.375 ms, allocated-byte delta 196,736,
working/peak working set 85,561,344 bytes, normalized evidence JSON 24,414 bytes. These
metrics include test-host overhead and are not an approved performance budget. Provider
file/temp/cache/database growth is zero by its read-only contract. Native SetupAPI/WMI
allocations are outside managed iteration bounds.

The source bounds class input to eleven, devices to 256 plus a sentinel, and output to
45 facts plus a failure observation. A 15-second caller timeout preserves one worker's
admission until native cleanup. Uncertain cleanup blocks reuse; normal cancellation can
retry after cleanup. Manual-clock tests prove late evidence is withheld and worker counts
do not grow while a timed-out call remains active.

## Reviews, boundaries and limitations

Independent review identified the interface/physical-absence wording risk. Scoped keys,
documentation and the focused regression resolve it. Subsequent review found no remaining
actionable cleanup, cancellation, arbitrary-input, privacy or evidence-ownership defect.
An initial build rejected constant test arrays under CA1861; reusable readonly arrays fixed
the warnings without changing analyzer policy.

- Security/privacy: numeric/class data only; no identities, content, secrets, raw logs,
  arbitrary queries, paths or execution surface. Existing allowlists remain unchanged.
- Accessibility: no new UI. Fixed semantic keys and explicit unknown/failure/cancellation
  states are available to later accessible presentation. No unperformed human review claimed.
- Modest hardware: no GPU, polling, permanent worker or persistent growth; bounded input,
  output and native admission. No low-performance or cross-hardware certification.
- Data/migration: no schema migration, store, attachment or export change.
- Release interface: unchanged; no released behavior is invented.
- Project boundary: no sibling work or shared portfolio interface.

Detailed source meanings, recovery, API references and exclusions are in
[`docs/inventory/device-enumeration.md`](../../../docs/inventory/device-enumeration.md).

## Handoff

Implementation `eb83e4f26c292498f2d9223f12a3adeea402f1bc` was pushed and verified against
upstream. The final evidence checkpoint preserves its tested implementation. Normal
[PR #12](https://github.com/PikkuJanne/ThirdLife/pull/12) is open; this task is not yet
integrated into main. TL-0112 is dependency-ready
but must wait for this task's PR to merge into updated main. No owner approval, human
walkthrough or release sign-off is required by TL-0111. The known host-policy and source
availability limits remain documented. One compact task entry points to durable evidence;
STATUS remains a current snapshot.
