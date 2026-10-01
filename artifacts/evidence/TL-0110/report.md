# TL-0110 — UEFI, Secure Boot and TPM inventory

**Date:** 2026-10-01  
**State:** Done; [normal PR #11](https://github.com/PikkuJanne/ThirdLife/pull/11) open, default-branch integration pending  
**Branch:** `codex/tl-0110-firmware-security`  
**Base:** `104664eb4d226fbc59d5b834ea5f862756ebc63f`  
**Authority:** active Codex machine only; remote CI supplementary

## Continuity

TL-0109 PR #10 merged at 11:05:55 UTC on 2026-10-01. Its complete head `14cbe14`
is an ancestor of main, with zero unique commits and one merge commit ahead. Local main
fast-forwarded before this task branch was created. All selected-task dependencies are
done. Required authority documents, source contracts, tests and prior evidence were read.
Only TL-0110 was selected. The configured origin is unchanged; the documented process-scoped
HTTPS bridge was used. Prior branches and the unrelated untracked Word reference are preserved.

The initial Release baseline passed 21 provider-contract/cancellation tests in 20.733 s.
The reviewed TL-0109 supplementary CI failure was a pre-existing TL-0106 fixture race:
it allowed only one second for thread-pool source entry before exercising cancellation.
The focused repair controls the runner clock, keeps its declared two-second timeout,
preserves cancellation/no-value assertions and adds bounded cleanup. Its regression passed.
No test was disabled or quarantined; the separate production-timeout test remains.

## Acceptance evidence

| Criterion | Implementation and proof |
| --- | --- |
| Separate Secure Boot capability and enabled state | Separate DeviceGuard and fixed registry owners/provenance; positive, disabled, missing, malformed and conflicting fixtures |
| Denied/unsupported is not a pass | Typed unknown values, sanitized numeric status, independent facts retained; native denied-access run preserved in `active-machine.json` |
| No firmware/TPM/password mutation | Fixed read-only APIs/projections and no-input `IsReadyInformation`; exact import/COM call allowlists; no elevation, shell, arbitrary method or input |
| Raw TPM expert evidence | Bounded spec/manufacturer version notation, numeric device/return/error/information fields and raw initial/readiness booleans; contradictory derived fields withheld |
| Enabled/disabled/unavailable/synthetic lock cases | Provider, platform, DeviceGuard, TPM, native-value decoder and lifecycle fixtures; no host settings changed |
| Actual active-machine smoke | Unelevated Windows physical-host run; UEFI, Secure Boot enabled and TPM2.0 present; capability/readiness unknown, TPM WMI `0x80041003` denied |

Source/normalization details and reviewed primary sources are in
[`docs/inventory/firmware-security.md`](../../../docs/inventory/firmware-security.md).
The source and test files containing deterministic fixtures are byte-hashed in `inputs.json`.

## Resource and uncertainty limits

The captured observation took 5,094.916 ms, with 343.75 ms process CPU, 1,118,792 allocated
managed bytes and an 82,702,336-byte process working-set snapshot. These metrics include
test-host/native initialization; they are not isolated product peaks or cross-hardware bounds.
The test command took 6.886 s. A prior passing smoke was repeated only because normal
console verbosity omitted its passing-test state payload; detailed output supplied the
required reviewable actual-state artifact.

Thirty-eight singleton facts plus a possible failure observation, one admitted worker,
bounded native records/arrays/version text, and bounded SMBIOS parsing limit application work.
Native COM calls can outlive the 15-second caller wait; the worker remains admitted until
cleanup. Unknown cleanup blocks reuse. No background service, GPU, network use, cache,
temporary file or persistence is added. No firmware password, MDM/Autopilot or ownership
clearance is inferred from missing indicators. Standard-user denied readiness is expected;
the application does not elevate to fill it.

## Verification and review

Targeted passed in 60.823 s (62.466 s including command startup): Quick controls, format,
zero-warning Release solution build, 79 Core / 625 Inventory / 3 UI tests, followed by
625 Inventory tests again. Final handoff Quick passed in 52.583 s (54.245 s command),
with the same 79/625/3 counts. Zero failures or skips. Exact commands, durations, failed
attempts and final tier results are in `runs.json`.
Focused tests first covered native parsing and normalization; lifecycle tests then covered
manual-clock timeout, admission through cancellation, cleanup precedence, later-source
suppression and safe retry. Independent reviews found and fixed cleanup/admission races,
late-native-read publication, and simultaneous read/cleanup error precedence before handoff.

New-test compilation issues were limited to nullable generic constraints and repeated
constant-array analyzer warnings. A format check caught import order. Bundle validation
caught a missing required runtime-authority phrase in the in-progress handoff. Each was
corrected before rerunning its focused check. Windows PowerShell5 would not execute the
verification script; installed PowerShell7.6.5 ran it under its existing RemoteSigned policy.
No policy, analyzer, trust check or test assertion was relaxed.

Expected tier is Targeted, including Quick. Full/Extended are not triggered: no dependency,
engine, schema/migration, major refactor, protected release merge, milestone gate or named
scenario trigger. No Sandbox/harness change was required. Actual observation was selected
separately so deterministic verification cannot claim physical hardware evidence.

Final reviews cover bounded inputs/output, sanitized exceptions, no arbitrary execution,
native resource ownership, immutable source provenance and deterministic reconciliation.
Existing WPF controls/keyboard flows are unchanged; the Quick UI checks remain required.
No sibling, data/migration, persistence/export or release-interface change is introduced.

## Handoff

Implementation commit `98175080554ec7bf74ee4dc7e1358655adf449c1` was pushed, then verified
against upstream and live remote. Normal PR #11 must merge into main before TL-0111 begins. No prior PR is closed,
branch deleted, history rewritten or next task started by this work. One compact task
evidence entry points here; detailed evidence stays out of `STATUS.md` and `TASKS.yaml`.
