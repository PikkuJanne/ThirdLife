# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-09-30  
**Bundle baseline:** 0.4.0  
**Current milestone:** M1 — Audit-only vertical slice  
**Current task:** `TL-0107` — OS lifecycle and activation inventory; review

## Continuity

- Cumulative PR #7 merged at `74b6c8cbc7b4426eebf38139ed3374f66e3ff8be`; updated main contains all prior completed work.
- Current branch: `codex/tl-0107-os-lifecycle-activation`, created from that updated main.
- Implementation checkpoint `c144221b2010592ebf5635182396ee6cf80e5b1e` is pushed; local and upstream matched after publication.
- Normal PR #8 is open against main; TL-0107 is not yet integrated. The verification/handoff follow-up is reported in the session handoff.
- Legacy PR/branch disposition is unchanged; no closure, deletion, or history rewrite performed.
- The documented unrelated Word roadmap reference remains untouched and excluded.
- Git synchronization uses the documented process-scoped HTTPS bridge; configured SSH origin is unchanged.
- Runtime authority: active Codex machine only; remote CI is supplementary.

## Outcome

Read-only OS facts, separate architecture provenance, versioned offline lifecycle mapping,
activation status, and source-specific reboot indicators are implemented. Unknown/stale or
contradictory data cannot satisfy support. Activation contains no keys, product IDs, account
identifiers, raw licensing output, or legal ownership conclusion. Missing reboot indicators
never prove that no reboot is pending. No UI, dependency, migration, privilege, or host change.

## Verification

- Baseline: 20 provider-contract tests passed in 13.242 s; no pre-existing runtime failure.
- Targeted, including Quick: passed in 38.786 s; 79 Core + 222 Inventory tests, format,
  validators, and Release solution build with zero warnings/errors.
- Completion Quick passed in 29.580 s; three separate unelevated active-machine smoke tests passed (activation, OS, reboot).
- Final handoff Quick is blocked by host Application Control (0x800711C7) loading Core tests; focused no-build reproduction confirmed the same block.
- Same-machine Sandbox Quick passed (79 Core + 222 Inventory; 90.883 s shared tier); exact committed-source evidence is pending. Host security is unchanged.
- Full and Extended: no trigger. Remote CI is not used as task evidence.
- Detailed results, input digests, resource observation, acceptance mapping, and limitations:
  `artifacts/evidence/TL-0107/report.md`, `runs.json`, and `inputs.sha256`.

## Limits and next action

Native calls cannot be forcibly aborted; one worker per instance stays occupied until cleanup.
Lifecycle revision `thirdlife.windows-lifecycle.2026-09-30.v1` expires after 2026-10-30.
Specialized/unlisted editions and builds remain unknown. Reboot indicators are incomplete.
No patch-currency, hardware, licence-ownership, complete-readiness, or cross-hardware claim.

Review PR #8, then integrate TL-0107 into main. The next dependency-ready task is
`TL-0108` — storage inventory and reliability evidence, only after integration. It is not started.
