# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-09-30  
**Bundle baseline:** 0.4.0  
**Portfolio baseline:** ThirdLife Software Portfolio v2.1  
**Current milestone:** M1 — Audit-only vertical slice  
**Current activity:** `AMD-2026-09-12-REPOSITORY-STABILIZATION` — review; required verification complete  
**Last completed product task:** `TL-0106` — system inventory  
**Next product task after merge:** `TL-0107` — OS lifecycle and activation inventory

## Git and continuation

| Field | Verified state |
|---|---|
| Repository | `PikkuJanne/ThirdLife` |
| Branch | `codex/repository-stabilization-v0.4.0` |
| Reviewed and starting commit | `89303ab15ed749754290b7c17e5f6cdb27719a00` |
| `main` at session start | `1bb776f710bfa76f485bbee8d4b182b7a5284cd9` |
| Ancestry | Starting advanced line contains `main`, 42 ahead / 0 behind |
| Upstream | `origin/codex/repository-stabilization-v0.4.0`; Full-verified checkpoint `861bb14` pushed; final evidence commit is reported in the session handoff |
| Worktree | Implementation complete; explained unrelated Word reference remains untouched and excluded |
| Runtime authority | active Codex machine only; remote CI is supplementary |
| Integration | One cumulative PR prepared for owner review; no merge authorized in this session |

SSH authentication is unavailable unattended. Git synchronization uses the previously
documented process-scoped HTTPS `insteadOf` bridge; the configured remote is unchanged.

## Outcome and scope

M0 and `TL-0101`–`TL-0106` remain complete. This amendment updates integration discipline,
test selection, evidence storage, and future provider contracts. No product task state or
prior evidence is removed. `TL-0107` and `TL-0112` remain backlog.

The owner authorized application on 30 September. D-067–D-072 are adopted; cumulative merge,
closure of PRs #4–#6, and branch retirement remain separate owner decisions. Unique legacy
privacy and supply-chain implementations are preserved for that disposition review.

## Verification

| Tier | Latest result |
|---|---|
| Baseline | `tools/validate_bundle.py` passed in 2.932 s before editing |
| Quick | Clean worktree passed in 42.269 s; final evidence Quick passed in 40.083 s (runner 38.330 s), 79 Core + 50 Inventory |
| Targeted | 345 subsystem tests in 88.772 s; 223 governance tests in 143.552 s; separate active smoke 1/1 in 34.424 s including Quick |
| Full | Passed at `861bb14` in governed same-machine Sandbox: all 355 .NET tests, shared Full 342.486 s; wrapper 456.452 s including launch/binding/cleanup |
| Extended | No new product/resource/interruption risk trigger; not run |
| Supplementary CI | Optional; not used as task/gate evidence |

The first clean Quick at `70c7da0` failed locked restore (NU1004). The two Inventory locks
omitted existing internal Core references. A focused reproduction and regression caught
the defect; corrected locks leave all external packages and the licence matrix unchanged.
Corrected clean Quick passed. Full then encountered the known host policy block
(`0x800711C7`); a focused Reports test reproduced it. Host security is unchanged.
The reviewed Sandbox adaptation passed seven focused tests and staging preflight.
Exact-commit Sandbox Full passed with source bindings and cleanup verified. The direct-host
policy limitation remains; this adds no cross-hardware or direct-host compatibility claim.

## Durable records

- [Approved amendment](docs/amendments/2026-09-12-repository-stabilization.md)
- [Review input](docs/reviews/2026-09-12-repository-review.md)
- [Branch/PR disposition](docs/reviews/2026-09-30-branch-disposition.md)
- Prior detailed status: `artifacts/handoffs/STATUS-pre-v0.4.0-20260930T125339Z-89303ab15ed7.md`
- [Stabilization execution evidence](artifacts/evidence/AMD-2026-09-12-REPOSITORY-STABILIZATION/report.md)

## Stop point and next action

Review the pushed stabilization branch and its cumulative PR to `main`. Required active-machine
verification is complete. Stop for owner review. Do not merge, close existing PRs, or delete branches.
Do not start `TL-0107` in the stabilization session.

After an owner-approved merge, fetch updated `main` and select `TL-0107` in a new session.
No sibling integration, product data migration, privilege change, release behavior, human
walkthrough, cold boot, or cross-hardware certification is claimed here.
