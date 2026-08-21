# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-08-21  
**Snapshot preparation time:** 2026-08-21T20:34:58+02:00  
**Bundle baseline:** 0.3.0  
**Portfolio baseline:** ThirdLife Software Portfolio v2.1  
**Current milestone:** M0 — Foundation and product contract  
**Current task:** `TL-0007` — Create synthetic pilot fixtures and reference inputs  
**Task state:** `review`

## Current state

The automatable part of `TL-0007` is complete. Seven deterministic, non-personal YAML inputs now cover an assessment-ready job, an unknown-sanitization blocker, a partial-observation case, a candidate community-laptop policy, Basic and Job Seeker profiles, and four synthetic catalogue placeholders. The exact fixture-set SHA-256 is `1df6bbed058da9397555e747e6d3f4e4443934d261366a54145f2003e5fc8743`.

The task remains `review` because its required named pilot-owner approval has not yet been recorded. The catalogue additions also changed the governed licence matrix, so the prior `TL-0006` approval remains historical evidence for its old digest and a fresh current-matrix review is required by the fail-closed repository control.

The historical `TL-0008` draft-1 procedure at source commit `4fa3ea050fd5e9985fde9cc8218281698d371cc8`, procedure SHA-256 `ef150dbf14b5db208582b7b526c7e0c6d0a5b912736e9e6519b8918abcf0928b`, remains superseded. No physical hardware walkthrough was performed for that transition.

## Candidate pilot contract

The policy is explicitly `candidate_not_effective`, applies only to supported Windows 11 x64 laptops, and makes no universal or cross-hardware claim. Its proposed thresholds are:

- at least 8 GiB installed memory;
- at least 128 GiB system-storage capacity;
- at least 32 GiB free system storage;
- a 60% battery full-charge ratio as an advisory, not a ready-state blocker.

Blocking or repairable evidence covers the external sanitization gate, OS support, architecture and processor eligibility, storage health, battery presence and swelling inspection, UEFI, Secure Boot, TPM 2.0 readiness, ownership controls, activation, and human-confirmed display, keyboard, pointing, and network function. Audio output, camera, and microphone are profile-dependent for video calling. Missing required evidence remains unknown and yields human review; it never becomes an implicit pass.

The initial generic capability set is `web_browsing`, `document_editing`, `pdf_reading`, and `video_calling`. Basic treats the first three as essential and video calling as optional; Job Seeker treats all four as essential. Browser preference, accessibility preferences, cloud sign-in, and backup onboarding remain deferred recipient choices with no workshop action.

The catalogue identities are:

- `generic.synthetic.web-browser`;
- `generic.synthetic.document-editor`;
- `generic.synthetic.pdf-reader`;
- `generic.synthetic.video-calling`.

All four use version `0.0.0-fixture.1`, are `production_eligible: false`, have no external artifact, and retain pending licence/privacy review plus withheld redistribution. They do not name, fetch, install, execute, or depend on any real or sibling application.

## Git state

| Field | Verified value |
|---|---|
| Remote | `origin` → GitHub repository `PikkuJanne/ThirdLife` |
| Branch | `codex/tl-0007-synthetic-pilot-fixtures` |
| Baseline | `6843d354bd93c351ad5817a4136379dd1e9dccc6` — completed and published TL-0006 checkpoint |
| History handling | Continued from the fetched baseline; no reset, rebase, force push, or history rewrite |
| Review checkpoint | The commit containing the exact fixture and matrix bytes; resolve with `git rev-parse HEAD` after commit |
| Publication | Push and fetched remote-equality verification are required before requesting digest-bound approval |

The configured SSH remote rejects unattended public-key authentication on this machine. Publication uses GitHub CLI's authenticated HTTPS credential bridge without changing the configured remote or exposing a credential.

## Supply-chain state

The governed inventory is now 24 components: the unchanged 20 external build/test components plus four project-created synthetic catalogue placeholders. The catalogue file SHA-256 is `7f80078a24d9fa890738d344d3c705549c45d17d7712ce1f8d543d4ce47f8901`; each placeholder matrix row is bound to those exact bytes and the supply-chain validator recomputes that hash.

The candidate matrix SHA-256 is `32ff63e4e6deb703f978efad368ba54cdc898004106fa443e211d046126ee193`, and its global review state is `Pending`. The new rows use `NOASSERTION`, are non-installable and `not-shipped`, admit no package artifact or binary, and separately withhold installation and redistribution rights. The existing mutable evidence limitation for `xunit.abstractions` and the withheld .NET SDK/CPython redistribution rights remain unchanged.

## Verification evidence

| Scope | Result | Duration |
|---|---|---:|
| Pre-change governed Quick baseline | Passed 100 Python tests plus live bundle/repository validation at baseline `6843d35` | 85.476 s tests |
| Focused pilot-fixture regressions | 19/19 passed | 22.131 s |
| Existing bundle-validator regressions | 80/80 passed after final fixture-validator hardening | 40.907 s |
| Focused supply-chain regressions | 21/21 passed for exact catalogue hashing, 24 governed components, and pending review state | 46.717 s |
| Python compilation and live validators | Compilation, bundle validation, and supply-chain validation passed; 91 tasks, 8 milestones, 66 decisions, valid DAG | 4.371 s combined command |
| Canonical safety scans | Live fixtures passed secret, personal/device identifier, path, URL, command, development-artifact, markup, bounded-YAML, exact generic allowlist, and cross-reference checks | Included above |
| Governed Quick review checkpoint | All 120 Python tests and bundle validation passed; repository validation then stopped at exactly the three pending current-matrix human-review controls | 105.215 s tests |

The live supply-chain result is dependency-input SHA-256 `a2dd0bbac1fac2757b4c9de284d320bad4488be81f90baa755289a09f578d08f`, matrix SHA-256 `32ff63e4e6deb703f978efad368ba54cdc898004106fa443e211d046126ee193`, and review `pending`.

The task declares a targeted tier and no Full/extended trigger. The inherited release-gate limitation remains: Windows Smart App Control on the active machine blocks two unsigned Release test DLL loads, and it was not disabled or bypassed.

## Boundary and risk impact

- **Project vacuum / sibling integration:** No sibling repository was browsed and no sibling identifier, artifact, profile, adapter, data source, service, or dependency was introduced.
- **Data / migration:** Only deterministic `PUBLIC_REFERENCE` fixture data was added. There is no application data, database, migration, telemetry, retention, deletion, or personal-data change.
- **Release interface:** No release-interface field was populated. Synthetic placeholders are not production catalogue admission or release evidence.
- **Security / privacy:** Strict bounded YAML, unique-key parsing, normalized privacy scans, explicit unavailable/null evidence, no arbitrary command/path/URL fields, non-production package placeholders, and digest-bound approvals fail closed.
- **Accessibility / low-spec:** No UI, focus, keyboard, screen-reader, scaling, high-contrast, GPU, service, background activity, cache, or runtime resource cost was added. Accessibility remains an explicit recipient-controlled profile choice.

## Changed paths

- `fixtures/README.md`
- `fixtures/jobs/*.yaml`
- `fixtures/policies/community-laptop-policy.yaml`
- `fixtures/profiles/basic.yaml`
- `fixtures/profiles/job-seeker.yaml`
- `fixtures/catalog/catalog.yaml`
- `tools/validate_bundle.py`
- `tools/tests/test_pilot_fixtures.py`
- `tools/supply_chain.py`
- `docs/supply-chain/dependencies.md`
- `docs/supply-chain/license-matrix.csv`
- `tools/tests/test_supply_chain.py`
- `tools/validate_repository.py`
- `README.md`
- `TASKS.yaml`
- `STATUS.md`
- `BUNDLE_MANIFEST.sha256` after final regeneration

The unrelated untracked `ThirdLife_Two-Team_Software_Portfolio_Roadmap_v2.1.docx` was present before TL-0007 and remains untouched and unstaged.

## Outstanding

1. A named pilot owner must approve the exact candidate policy values and initial four-capability set, bound to the review commit and fixture-set SHA-256.
2. A named licence/rights reviewer must approve the exact changed matrix commit and SHA-256 while preserving all recorded limitations, `NOASSERTION`, no-artifact/not-shipped states, and separately withheld rights. This is approval of the governance contract as written, not blanket redistribution permission.
3. Because the repository validator requires completed `TL-0006` evidence to bind the current matrix digest, recording the renewed matrix approval requires explicit authorization to append that evidence to `TL-0006`; its prior evidence must remain intact.
4. Repeat the Full tier in an approved environment that can execute the unsigned assemblies, or after later governed signing/lifecycle work provides an approved path, before a release gate.

## Next dependency-ready task

After `TL-0007` is approved and completed: `TL-0009` — Record initial architecture decisions and project boundaries.
