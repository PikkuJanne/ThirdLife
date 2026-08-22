# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-08-22  
**Snapshot preparation time:** 2026-08-22T06:36:47+02:00  
**Bundle baseline:** 0.3.0  
**Portfolio baseline:** ThirdLife Software Portfolio v2.1  
**Current milestone:** M0 — Foundation and product contract  
**Current task:** `TL-0009` — Record initial architecture decisions and project boundaries  
**Task state:** `done`

## Current state

`TL-0009` is complete. Eight accepted architecture decision records now translate the binding decisions and project boundary into planned, reviewable constraints for the Windows/WPF stack, evidence and policy separation, SQLite persistence, the ephemeral broker, the replaceable package seam, privacy-separated reports, standalone late binding, and the minimal release-interface envelope.

The records do not amend `DECISIONS.md` or `PROJECT_BOUNDARY.md`, select the later WinGet backend, populate speculative release-interface values, or claim that any later runtime behavior is implemented. The validator requires the exact eight-file set, section structure, decision/task references, semantic boundary statements, valid local links/fragments, and visible README navigation. Governed ADRs reject comments, fenced or indented code, raw HTML, images, reference-style links, unsafe schemes, repository escapes, and hidden contract evidence.

## Historical TL-0008 transition

The superseded `TL-0008` draft-1 procedure remains preserved at source commit `4fa3ea050fd5e9985fde9cc8218281698d371cc8`, with procedure SHA-256 `ef150dbf14b5db208582b7b526c7e0c6d0a5b912736e9e6519b8918abcf0928b`. No physical hardware walkthrough was performed for that transition.

## Recorded architecture outcomes

- `0001-windows-wpf-stack.md`: Windows/.NET/WPF choice, inward dependency direction, pure Core, UI-only WPF, and separate broker host.
- `0002-evidence-policy-separation.md`: immutable attributable evidence, versioned policy, reproducible decisions, and fresh verification.
- `0003-sqlite-job-store.md`: transactional local SQLite state, versioned migrations, bounded reviewed attachments, and split-state recovery.
- `0004-ephemeral-broker.md`: unelevated UI, mutually authenticated bounded IPC, typed digest-bound requests, replay controls, and no permanent service.
- `0005-package-adapter.md`: backend-neutral typed package seam, generic capabilities, exact reviewed identities, trust revalidation, and deferred `TL-0401` backend selection.
- `0006-report-privacy-classes.md`: independent `WORKSHOP_RESTRICTED`, `RECIPIENT_GUIDE`, and `SUPPORT_SANITIZED` projections with no raw-output fallback.
- `0007-standalone-late-binding-boundary.md`: strict B1 project vacuum and optional future B4 consumption of exact frozen public release facts only.
- `0008-minimal-release-interface-envelope.md`: a verified human-readable black-box release sheet, not an API, SDK, schema, plugin contract, or private-state exposure.

## Git state

| Field | Verified value |
|---|---|
| Remote | `origin` → GitHub repository `PikkuJanne/ThirdLife` |
| Branch | `codex/tl-0009-architecture-decisions-boundaries` |
| Baseline | `40a46c3e70bd495887e2e244c5d34bcbbb42adc6` — completed and published TL-0007 approval checkpoint |
| History handling | Continued from the fetched baseline; no reset, rebase, force push, or history rewrite |
| TL-0009 checkpoint | The commit containing this handoff; resolve with `git rev-parse HEAD` after commit |
| Publication | Pending final commit, authenticated HTTPS push, fetch, and local/upstream equality check |

The configured SSH remote rejects unattended public-key authentication on this machine. Publication uses GitHub CLI's authenticated HTTPS credential bridge without changing the configured remote or exposing a credential.

## Verification evidence

| Scope | Result | Duration |
|---|---|---:|
| Pre-change governed Quick baseline | Passed 120 Python tests plus live bundle and repository controls at `40a46c3` | 127.436 s tests |
| Focused ADR contract regressions | 19/19 passed, including hidden-content, unsafe-link, fragment, path, and contract-drift cases | 2.928 s |
| Complete bundle-validator regression suite | 99/99 passed | 43.952 s |
| Live bundle validation | Passed: 91 tasks, 8 milestones, 66 frozen decisions, valid DAG | Included above |
| Project-reference review | Passed: 13 production projects, zero production-to-production references, 15 test references, and only `ThirdLife.UI` enables WPF | Static review |
| Independent adversarial review | No material validator bypass, decision contradiction, boundary conflict, or remaining correctness finding | Read-only review |
| Manifested governed Quick candidate | Passed all 139 Python tests, bundle/manifest validation, repository boundaries, package locks, supply-chain controls, licence approval, and CI controls | 111.043 s tests; 00:01:56.034 command |

The task declares the Quick tier and no Full or extended trigger. The repository-local Python 3.14.7 environment was used; system Python lacks PyYAML and is not the governed tool environment. The inherited release-gate limitation remains: Windows Smart App Control on the active machine blocks two unsigned Release test DLL loads, and it was not disabled or bypassed.

## Boundary and risk impact

- **Project vacuum / sibling integration:** No sibling repository was browsed and no sibling identifier, source, runtime, data, service, profile, adapter, schema, SDK, plugin framework, dependency, or acceptance test was introduced.
- **Data / migration:** No runtime data location, database, schema, migration, retention, deletion, export, telemetry, or personal-data behavior changed. ADR 0003 documents later constraints only.
- **Release interface:** No `RELEASE_INTERFACE.md` field was populated. ADR 0008 limits later entries to implemented and verified black-box release facts.
- **Security / privacy:** Broker, package trust, raw-data, report-class, and late-binding constraints were made explicit and fail closed. There is no new executable surface, privilege, network use, logging, or redistribution right.
- **Accessibility / low-spec:** No UI, custom control, focus path, screen-reader behavior, background work, cache, memory/storage load, GPU requirement, or runtime resource cost was added. The ADRs preserve the binding accessibility and modest-hardware obligations.

## Changed paths

- `docs/adr/0001-windows-wpf-stack.md`
- `docs/adr/0002-evidence-policy-separation.md`
- `docs/adr/0003-sqlite-job-store.md`
- `docs/adr/0004-ephemeral-broker.md`
- `docs/adr/0005-package-adapter.md`
- `docs/adr/0006-report-privacy-classes.md`
- `docs/adr/0007-standalone-late-binding-boundary.md`
- `docs/adr/0008-minimal-release-interface-envelope.md`
- `README.md`
- `tools/validate_bundle.py`
- `tools/tests/test_validate_bundle.py`
- `TASKS.yaml`
- `STATUS.md`
- `BUNDLE_MANIFEST.sha256` after final regeneration

The unrelated untracked `ThirdLife_Two-Team_Software_Portfolio_Roadmap_v2.1.docx` was present before TL-0009 and remains untouched and unstaged.

## Outstanding

1. None for `TL-0009`; no human evidence is required by its task contract.
2. Before `TL-0401`, resolve the human-facing `ADR-004` naming ambiguity between this task's `docs/adr/0004-ephemeral-broker.md` and that future task's separate required path `docs/adr/ADR-004-winget-backend.md`; use full paths until the governed task contract is amended by its owner.
3. Repeat the Full tier in an approved environment that can execute the unsigned assemblies, or after later governed signing/lifecycle work provides an approved path, before a release gate.

## Next dependency-ready task

`TL-0010` — Validate the M0 foundation gate. It is a hybrid gate and remains subject to its complete verification and human-evidence contract.
