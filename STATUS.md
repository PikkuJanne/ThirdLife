# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-08-21  
**Snapshot preparation time:** 2026-08-21T21:46:45+02:00  
**Bundle baseline:** 0.3.0  
**Portfolio baseline:** ThirdLife Software Portfolio v2.1  
**Current milestone:** M0 — Foundation and product contract  
**Current task:** `TL-0007` — Create synthetic pilot fixtures and reference inputs  
**Task state:** `done`

## Current state

`TL-0007` is complete. Seven deterministic, non-personal YAML inputs cover an assessment-ready job, an unknown-sanitization blocker, a partial-observation case, a candidate community-laptop policy, Basic and Job Seeker profiles, and four synthetic catalogue placeholders. The exact fixture-set SHA-256 is `1df6bbed058da9397555e747e6d3f4e4443934d261366a54145f2003e5fc8743`.

Janne Vuorela, acting as Pilot Owner and Principal Software Architect & Sole Project Owner, approved the candidate policy values and initial four-capability set at reviewed commit `7afc6c7599523fb56a66774a29e9107e6a9a0aac`. The same named review approved the amended licence and redistribution proposals exactly as written and authorized append-only recording in `TL-0006`; all prior evidence remains intact.

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
| Reviewed checkpoint | `7afc6c7599523fb56a66774a29e9107e6a9a0aac` — exact fixture and matrix bytes approved by the named owner |
| Approval checkpoint | The commit containing this approval metadata; resolve with `git rev-parse HEAD` after commit |
| Publication | Reviewed checkpoint was pushed and fetch-verified equal before approval; approval checkpoint still requires publication |

The configured SSH remote rejects unattended public-key authentication on this machine. Publication uses GitHub CLI's authenticated HTTPS credential bridge without changing the configured remote or exposing a credential.

## Supply-chain state

The governed inventory is now 24 components: the unchanged 20 external build/test components plus four project-created synthetic catalogue placeholders. The catalogue file SHA-256 is `7f80078a24d9fa890738d344d3c705549c45d17d7712ce1f8d543d4ce47f8901`; each placeholder matrix row is bound to those exact bytes and the supply-chain validator recomputes that hash.

The matrix SHA-256 is `32ff63e4e6deb703f978efad368ba54cdc898004106fa443e211d046126ee193`, and its global review state is `Approved`. The approval accepts the contract exactly as written: the new rows use `NOASSERTION`, are non-installable and `not-shipped`, admit no package artifact or binary, and separately withhold installation and redistribution rights. The existing mutable evidence limitation for `xunit.abstractions` and the withheld .NET SDK/CPython redistribution rights remain unchanged; no blanket right was granted.

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
| Named pilot and licence/rights review | Janne Vuorela approved the exact reviewed commit, fixture digest, policy/capability set, and amended matrix contract while preserving all limitations and withheld rights | Recorded 2026-08-21 |
| Post-approval targeted regression | Pilot-fixture 19/19 and supply-chain 21/21 passed; live bundle and supply-chain validators report approved state | 22.956 s + 49.548 s tests |
| Final governed Quick gate | All 120 Python tests, bundle validation, manifest/repository controls, package locks, exact catalogue hash binding, and human-approval checks passed | 111.413 s tests |

The live supply-chain result is dependency-input SHA-256 `a2dd0bbac1fac2757b4c9de284d320bad4488be81f90baa755289a09f578d08f`, matrix SHA-256 `32ff63e4e6deb703f978efad368ba54cdc898004106fa443e211d046126ee193`, and review `approved`.

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

1. Repeat the Full tier in an approved environment that can execute the unsigned assemblies, or after later governed signing/lifecycle work provides an approved path, before a release gate.

## Next dependency-ready task

`TL-0009` — Record initial architecture decisions and project boundaries.
