# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-10-01  
**Bundle baseline:** 0.4.0  
**Runtime authority:** active Codex machine only; remote CI is supplementary  
**Current milestone:** M1 — Audit-only vertical slice  
**Current task:** TL-0110 — UEFI, Secure Boot, and TPM inventory; done, integration pending

## Continuity

TL-0109 PR #10 is merged into main `104664eb4d226fbc59d5b834ea5f862756ebc63f`.
Its head `14cbe14` is an ancestor. Local main fast-forwarded before creation of
`codex/tl-0110-firmware-security`; all dependencies are done. No completed-task stack.
TL-0110 normal PR #11 is open into main. Implementation commit `98175080554ec7bf74ee4dc7e1358655adf449c1` is pushed and remote-verified. Merge PR #11 before the next roadmap task begins.

The SSH origin is unchanged; fetch/push use the documented process-scoped HTTPS bridge.
Prior branches and the unrelated untracked Word reference remain untouched. No sibling
work, history rewrite, branch retirement or next-task implementation.

## Outcome and verification

Read-only firmware mode, separate Secure Boot capability/state, bounded raw TPM evidence
and observable firmware lock indicators are implemented. Missing/denied/conflicting values
remain unknown; no firmware, TPM, password, ownership or management mutation is exposed.

Targeted including Quick passed in 60.823 s: controls/format, zero-warning Release build,
Core79/Inventory625/UI3, then Inventory625 repeated. Native smoke passed: UEFI, Secure Boot
enabled and TPM2.0 present. Capability/readiness remain unknown; TPM WMI denied0x80041003.
The earlier supplementary-CI cancellation-fixture race is repaired and regression-tested.
Full/Extended are not triggered. No cross-hardware or clear-ownership claim.

Durable evidence: `artifacts/evidence/TL-0110/report.md`, `runs.json`, `active-machine.json`
and `inputs.json`. One compact task entry; detailed logs remain outside the handoff.

## Next action

Review/merge normal PR #11, then fetch and verify main ancestry.
Next dependency-ready task: **TL-0111 — Implement network and key-device enumeration**.
Do not start it until TL-0110 is integrated into updated main.
