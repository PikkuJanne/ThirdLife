# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-08-22  
**Snapshot preparation time:** 2026-08-22T08:39:35+02:00  
**Bundle baseline:** 0.3.1  
**Portfolio baseline:** ThirdLife Software Portfolio v2.1  
**Current milestone:** M0 — Foundation and product contract  
**Current governance action:** `AMD-2026-08-22-ADR-0009` — Reserve ADR 0009 for TL-0401  
**Action state:** approved, synchronized, manifested, and Quick-verified

## Current state

`TL-0009` remains complete at source checkpoint `4872f7c34143a3278b92bcb37c8fcd9a435b03a7`. Its eight accepted architecture records, including completed ADR 0004 at `docs/adr/0004-ephemeral-broker.md`, are unchanged by this amendment.

Janne Vuorela, Principal Software Architect & Sole Project Owner, approved the named resolution on 2026-08-22. Bundle 0.3.1 reserves ADR 0009 for dependency-gated `TL-0401` at `docs/adr/0009-winget-backend.md` and synchronizes only the future task path, matching maintainer-evidence wording, authority records, bundle metadata, navigation, validation, status, changelog, and manifest.

The reservation does not create the future ADR file, select a WinGet backend, approve TL-0401, change the task graph, or establish a portfolio-wide ADR registry. The complete authority, impact, alternatives, rollout, rollback, and historical-evidence treatment are in [`AMD-2026-08-22-ADR-0009`](docs/amendments/2026-08-22-adr-0009-reservation.md).

## Historical TL-0008 transition

The superseded `TL-0008` draft-1 procedure remains preserved at source commit `4fa3ea050fd5e9985fde9cc8218281698d371cc8`, with procedure SHA-256 `ef150dbf14b5db208582b7b526c7e0c6d0a5b912736e9e6519b8918abcf0928b`. No physical hardware walkthrough was performed for that transition.

## Git state

| Field | Verified value |
|---|---|
| Remote | `origin` → GitHub repository `PikkuJanne/ThirdLife` |
| Branch | `codex/adr-0009-numbering-amendment` |
| Source baseline | `4872f7c34143a3278b92bcb37c8fcd9a435b03a7` — completed and published TL-0009 checkpoint |
| History handling | Continued from the fetched baseline; no reset, rebase, force push, or history rewrite |
| Amendment checkpoint | The manifest-bound bundle 0.3.1 commit containing this handoff; resolve from the published branch tip |
| Publication verification | The session completion report records the post-push fetch and local/upstream equality result |

The configured SSH remote rejects unattended public-key authentication on this machine. Publication therefore uses GitHub CLI's authenticated HTTPS credential bridge without changing the configured remote or exposing a credential.

## Verification evidence

| Scope | Result | Duration |
|---|---|---:|
| Pre-change governed Quick baseline | Passed all 139 Python tests plus live bundle and repository controls at `4872f7c` | 109.734 s tests; 00:01:54.794 command |
| Focused ADR-numbering regressions | 28/28 passed, including obfuscated aliases, noncanonical duplicate reservations, premature files, exact authority links, the exact maintainer gate, and active metadata | 8.201 s tests; 9.216 s command |
| Complete bundle-validator regression suite | 109/109 passed within the manifested Quick run | Included in the aggregate below |
| Live bundle validation | Passed: 91 tasks, 8 milestones, 66 frozen decisions, valid DAG; next Codex-ready task `TL-0010` | 3.4 s command |
| Independent adversarial review | Clean after reported bypasses were reproduced, fixed, and covered by regressions; no material finding remains | Read-only review |
| Manifested governed Quick | Passed all 149 Python tests, bundle/manifest validation, repository boundaries, package locks, supply-chain/licence controls, and CI controls | 130.133 s tests; 00:02:15.779 command |

The amendment declares the Quick tier and no Full or extended trigger. Verification uses the repository-local Python 3.14.7 environment. The inherited release-gate limitation remains: Windows Smart App Control on the active machine blocks two unsigned Release test DLL loads, and it was not disabled or bypassed.

## Boundary and risk impact

- **Project vacuum / sibling integration:** No sibling repository was browsed and no sibling identifier, source, runtime, data, service, profile, adapter, schema, SDK, plugin framework, dependency, or acceptance test was introduced.
- **Data / migration:** No runtime data location, database, schema, migration, retention, deletion, export, telemetry, or personal-data behavior changed.
- **Release interface:** Only the bundle/revision metadata moved to 0.3.1; no interface behavior or compatibility promise was populated.
- **Security / privacy:** No executable surface, privilege, network use, logging, package right, licence right, redistribution right, or privacy behavior changed. The validator fails closed on ambiguous ADR 0004 aliases, duplicate ADR 0009 reservations, premature future files, stale metadata, and broken authority linkage.
- **Accessibility / low-spec:** No UI, focus path, screen-reader behavior, background work, cache, memory/storage load, GPU requirement, or runtime resource cost was added.

## Changed paths

- Authority and task contract: `DECISIONS.md`, `ROADMAP.md`, `TASKS.yaml`, and `docs/amendments/2026-08-22-adr-0009-reservation.md`.
- Navigation and release history: `README.md`, `CHANGELOG.md`, and `STATUS.md`.
- Synchronized active metadata: `PROJECT_BOUNDARY.md`, `SECURITY.md`, `ACCESSIBILITY.md`, `LOW_SPEC.md`, `DEVELOPMENT_WORKFLOW.md`, `TESTING.md`, `AGENTS.md`, `CODEX_START_PROMPT.md`, `FUTURE_ASSEMBLY_NOTES.md`, and `RELEASE_INTERFACE.md`.
- Validation and integrity: `tools/validate_bundle.py`, `tools/tests/test_validate_bundle.py`, and `BUNDLE_MANIFEST.sha256` after final regeneration.

No file under `docs/adr/` changed. The unrelated untracked `ThirdLife_Two-Team_Software_Portfolio_Roadmap_v2.1.docx` was present before the amendment and remains untouched and unstaged.

## Outstanding

1. None for the ADR-numbering amendment.
2. Repeat the Full tier in an approved environment that can execute the unsigned assemblies, or after later governed signing/lifecycle work provides an approved path, before a release gate.

## Next dependency-ready task

`TL-0010` — Validate the M0 foundation gate. It is a hybrid gate and remains subject to its complete verification and human-evidence contract.
