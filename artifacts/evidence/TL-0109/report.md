# TL-0109 — Battery report generation and parsing

**Date:** 2026-10-01  
**State:** Done; normal PR/default-branch integration pending  
**Branch:** `codex/tl-0109-battery-report`  
**Base:** `262d22335d10b1a7d532f73a850a6877ddaa3c24`  
**Runtime authority:** active Codex machine only; remote CI is supplementary

## Continuity and baseline

TL-0108 PR #9 merged on 2026-09-30 at 16:10:10 UTC. Its complete head
`9451bfc430b492a87da5e571d169360a35c0d30a` is an ancestor of main, with no unique
commits and one merge commit ahead. Main fast-forwarded before this branch was created.
The process-scoped HTTPS bridge preserved the configured SSH origin. Prior branches and the
known unrelated untracked Word reference remain untouched. No sibling repository was accessed.

All transitive dependencies of TL-0109 are done. Binding documents, provider contracts,
relevant ADRs and approved privacy/logging contracts were reviewed before implementation.
The task requires Targeted verification and has no named Full or Extended trigger.

Both narrow baselines encountered the previously documented Windows Application Control
error 0x800711C7: Core.dll prevented Inventory provider-contract assertions, and UI.dll
prevented the UI assembly assertion. Exact commands/results are in `runs.json`. This is
pre-existing host loading behavior; security policy and analyzer settings remain unchanged.
The existing governed runner supplies exact TL-0109 profiles in same-machine Windows Sandbox.

## Acceptance mapping

| Requirement | Implementation and verification |
| --- | --- |
| Invariant XML and missing fields | Streaming parser accepts the reviewed namespace/version, invariant unsigned decimal and independent typed field availability. Synthetic culture, missing, duplicate, malformed, DTD/entity, depth, byte, attribute and pack-count cases. |
| Percentage absent for invalid/unavailable inputs | Positive mWh capacities are both required; relative units, unknown sentinels and malformed operands yield no percentage. Per-pack inferred ratios preserve values above 100 without a health conclusion. |
| No battery distinct from failure | Explicit power-status absence or valid empty report establishes absence. Denied, malformed, unknown, timeout and source failure stay unavailable/invalid. |
| Internal filenames and cleanup | Fixed command with an internal GUID filename; protected ACLs, pinned identity, exclusive lock, durable bounded ownership record and exact-owned handle deletion. Fixtures cover success, failure, timeout, cancellation, crash recovery, concurrent writer, junction, hard link, replacement and uncertain cleanup. |
| Swelling requires human inspection | Compiled main-window notice uses standard WPF controls, system colors, wrapping, scrolling, keyboard focus and automation text. Component checks exercise loaded-window focus and enlarged long text. No human inspection is inferred. |
| Run actual provider on active machine | Physical native source collected one battery and verified cleanup; final wrapper/source composition is selected explicitly in the same-machine Sandbox profile because host Application Control blocks the rebuilt assemblies. Guest pass markers are recorded without claiming a specific battery capability. |

The public provider accepts no caller command, arguments or paths. Six internal helper
files implement bounded XML decoding, fixed System32 powercfg execution and the owned
workspace. The source launches suspended, assigns a kill-on-close job before resuming, and
requires process exit plus zero active job members before cleanup. Unknown termination or
cleanup withholds all measurements and prevents another collection in that process.

The temporary XML can contain sensitive machine identity/history. It is not retained,
exported, logged or copied into persistence. Only reviewed normalized fields and fixed
numeric failure/lifecycle metadata leave the source. Details and location/recovery rules are
in `docs/inventory/battery-report.md`; no export allowlist, database or approved privacy
contract was broadened. Legacy aggregate policy keys retain their existing ownership.

## Verification and defect handling

Exact commands, durations and failed-attempt records are in `runs.json`.

- Source/workspace host fixtures passed, including 30 final workspace tests in 6.216 s.
- Frozen Python governance suite passed 256 tests in 244.284 s before the additional exact
  Sandbox provider-wrapper selector; its focused harness/manifest regressions are recorded separately.
- Final standalone Quick passed in 103.396 s (158.640 s guest total): 79 Core, 476 Inventory
  and 3 UI tests, controls, format and warnings-as-errors solution build.
- Committed Targeted passed in 117.161 s, including Quick and repeated Inventory/UI selection;
  the three required native/provider checks also passed (1.582 s reported test time). Whole
  guest duration was 178.700 s. Zero failures or skips. Source stayed unchanged and tracked-clean.
- Full and Extended are not triggered: no dependency change, major refactor, migration,
  protected release merge, milestone gate or named scenario trigger. Native cancellation
  checks are independently selected within this task's Targeted profile.

Failures were reduced before rerunning related sets:

1. Actual XML contained 18 attributes on an ignored history node, exceeding the initial
   bound of 16. Raised the bounded limit to 64 with adversarial boundary regressions.
2. Packaged execution redirected the known-folder alias. The default root now binds once
   to the pinned canonical identity, after local ancestry, product suffix, owner and ACL
   checks. Injected arbitrary fixture paths retain strict canonical equality.
3. Git 2.56.0.windows.1 rejected the uppercase `NUL` global-config path before guest startup.
   The harness now holds a newly created empty config file open against modification;
   no host Git configuration or security control is imported.
4. A fixed native factory name collided with the static `Process.Start` ban. Renamed it
   `LaunchFixedReport`; the original guard remains unchanged.
5. The UI test asserted focus before the Loaded binding ran. The test now loads the real
   window through a bounded STA dispatcher and retains logical, keyboard and automation
   focus assertions.
6. Analyzer findings were corrected without suppression. Quick project-list assertions
   were updated when the changed UI component became part of Quick. One Python run was
   invalidated by an overlapping manifest edit; the recorded frozen rerun passed.

## Active observation and resource limits

`active-battery.json` contains only minimized availability, counts and process metrics.
The physical standard-user fixed report completed in 272.175 ms, used 46.875 ms measured
process CPU, produced 18,771 bytes and exposed one battery with design/full-charge capacity;
cycle count was unavailable. Production default-source collection completed in 244.447 ms,
with 712,704 bytes private-memory growth and 29 additional process handles. Immediate native
cancellation completed and cleaned in 15.047 ms. These are deltas, not peak benchmarks.

Those physical observations preceded final startup allocation-order hardening and the
factory rename. The subsequent rebuilt Inventory.dll was blocked by the existing host
Application Control policy. The final native Sandbox step therefore requires exact passing
markers for production-source cleanup, real native cancellation and source/provider/runner
composition; all three passed. Normal console verbosity preserves their exact pass markers
but not the guest capability values. No fixture substitutes for that step, and no guest result
certifies the physical battery.

Bounds: four packs, 4 MiB XML, depth 16, 100,000 nodes, 64 attributes, 23 observations,
16 MiB disk preflight, one in-process writer and one cross-process lock, 20-second command,
five-second termination wait and 30-second caller limit. Native file growth is polled every
100 ms; this is a soft ceiling, not a quota. Synchronous native calls may outlast a caller
deadline; the source retains its slot and cleanup obligation. No permanent service, scheduled
polling, GPU requirement, stress test, cache or background indexing is introduced.

## Accessibility, security and claim limits

The notice states: “Battery reports cannot detect swelling. Swelling requires human
inspection.” Compiled component checks cover names/roles/help, system colors, keyboard
focus and enlarged/wrapped/scrollable text. This is automated component evidence, not a
screen-reader, high-contrast or human operator walkthrough. No new human evidence item
is required by this task.

Native imports and ownership are exactly allowlisted. Public arbitrary execution, network,
raw logging and unrestricted mutations remain prohibited. Root/file ACLs, identities,
reparse and hard-link checks and interrupted ownership records were reviewed independently.
Protected empty root and lock may remain; uncertain artifacts are preserved for safe human
review, never recursively deleted. Recovery occurs on the next collection, including when
no battery is currently exposed.

No physical swelling, battery health, endurance, charging behavior, manufacturer coverage,
minimum-hardware or cross-hardware certification follows. No sibling, persistence migration,
release interface or approval-policy change. B1 inventory orchestration and later manual
workflow remain their own tasks.

## Publication and next task

Implementation/runtime commit: `40e97bb3afd951d5c7387b66183dd131adbfdf63`.

[Committed Targeted audit](../../audit/TL-0109/targeted-17d780a390b533aafd998ebfe074be9b4e39e29537214481ada0ee02956ba204-a77ed7db8779ddf1e066c89eae1357bb.json) and its adjacent manifest bind
that commit to source digest `17d780a390b533aafd998ebfe074be9b4e39e29537214481ada0ee02956ba204`,
result SHA-256 `08b9b729f424a96ebea76ab955caeb8a15cfa78ba0e9133a801c51709761f12b`,
SDK 10.0.400, Python 3.14.7, Git 2.56.0.windows.1, the locked dependency closure and a 4 GiB
same-machine guest. `inputs.sha256` binds 28 runtime source/test/profile/toolchain inputs.
Later completion records change documentation/evidence only; those runtime hashes remain exact.

[Normal PR #10](https://github.com/PikkuJanne/ThirdLife/pull/10) targets main. Implementation
and evidence are pushed, with local/upstream equality verified. Integration is pending; no
branch was retired. Only the documented unrelated Word reference remains untracked. Remote
CI is supplementary. The task uses one compact YAML evidence entry, a current STATUS snapshot
and six detailed evidence/audit files totaling less than 40 KiB.

Do not start the next dependency-ready task,
`TL-0110 — Implement UEFI, Secure Boot, and TPM inventory`, until TL-0109 is merged.
