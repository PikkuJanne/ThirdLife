# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-11-25  
**Bundle baseline:** 0.7.0  
**Runtime authority:** active Codex machine only. Remote CI is supplementary.  
**Current milestone:** M3 — Safe planning and privilege boundary  
**Current task:** TL-0311 — Blocked

## Continuity

PR #37 integrated the [Windows 11 platform amendment](artifacts/evidence/TL-0315/report.md)
into `main` at `af51598`. Draft PR #36 retains the broker checkpoint and applies the
shared Windows 11 client/native-x64 check before client launch and broker startup.

The original workspace still contains the documented untracked Word reference.

## Outcome and evidence

The draft now configures Windows x64 Native AOT, a protected installation and
the production launcher. The audited, patched .NET packs restored. Native
publish stopped at the missing Microsoft linker. Two Microsoft installation
catalogs failed their advertised size and SHA-256 checks. There is no published
or installed native candidate on the active machine.

Targeted passed 1,267 tests. Clean Full on checkpoint `63eec10` passed 304
governance and 2,001 .NET tests. The 32-component SBOM was generated.

The owner deferred the Hyper-V guest. Real consent, elevated recovery and the
distinct-account denial remain outstanding. [TL-0311 evidence](artifacts/evidence/TL-0311/report.md)

## Next action

Acquire a verified Microsoft x64 linker and Windows SDK, then continue
[native publish and protected deployment verification](docs/security/broker-launch.md)
on the active machine. Keep PR #36 draft through the remaining TL-0311 evidence.
