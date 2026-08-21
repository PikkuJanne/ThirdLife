# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-08-21  
**Snapshot preparation time:** 2026-08-21T17:49:52+02:00  
**Bundle baseline:** 0.3.0  
**Portfolio baseline:** ThirdLife Software Portfolio v2.1  
**Current milestone:** M0 — Foundation and product contract  
**Current task:** `TL-0006` — Create dependency, license, and SBOM controls  
**Task state:** `in_progress`

## Current state

The automatable TL-0006 implementation is present and its focused checks, locked restore, working-tree SBOM inspection, and point-in-time vulnerability queries pass. The remaining task-specific automation is exact-commit clean-checkout SBOM proof. A defensive Full-tier run passed every step through the zero-warning Release build, but Windows Smart App Control blocked two unsigned DLL loads in the final test phase; the same two project tests are blocked from the untouched TL-0005 baseline. After the clean-checkout proof, the task moves to `review`; it cannot become `done` until a named human reviews the licence and redistribution proposals against the exact matrix digest and reviewed commit.

The current inventory contains 20 external components: 14 NuGet test-only packages and 6 build-only components (3 GitHub Actions, PyYAML, .NET SDK, and CPython). There are zero runtime dependencies and zero catalogue applications. Project-to-project references are not external packages.

## Git state

| Field | Verified value |
|---|---|
| Remote | `origin` → GitHub repository `PikkuJanne/ThirdLife` |
| Branch | `codex/tl-0006-dependency-license-sbom-controls` |
| Implementation baseline | `b5c435c92e55f1d326963fc4aea9d4dd50525d37` — completed TL-0005 checkpoint |
| History handling | Continued from the fetched baseline; no reset, rebase, force push, or history rewrite |
| Checkpoint | Not yet created; implementation verification is in progress |
| Upstream | Not yet configured for this new task branch |

The configured SSH remote rejected unattended public-key authentication on this machine in the preceding task. Publication will use GitHub CLI's authenticated HTTPS credential bridge without changing the configured remote or exposing a credential.

## Supply-chain outcome

The implementation now provides:

- centrally pinned direct NuGet versions and exact resolved lock closures;
- an exact, sorted 20-row dependency/licence matrix with accountable owner, upstream publisher, version, source, purpose, declared licence, provenance, integrity mechanism, distribution plan, limitations, and separate installation/redistribution proposals;
- hash-required, wheel-only installation of the sole Python build dependency;
- full-commit GitHub Action pins and exact .NET/Python toolchain pins;
- NuGet audit policy with direct and transitive review and no suppression path;
- a standard-library validator and deterministic CycloneDX 1.6 generator;
- exact one-to-one reconciliation for future catalogue identities without defining executable catalogue behavior before `TL-0301`;
- exact-HEAD source provenance that rejects fabricated, stale, dirty, or incomplete governed input claims;
- fail-closed human approval binding to a real reviewed commit and exact matrix SHA-256.

The licence matrix SHA-256 is `f88260289c14c8b3d651d6149f560f083232bf131ee5398563462e1df4e9ca73`. Its global review state is `Pending`; every row-level conclusion remains explicitly proposed.

## Material limitations

- `xunit.abstractions` 2.0.3 publishes a legacy mutable licence URL. Its Apache-2.0 conclusion remains a proposal, and redistribution is withheld unless the human review accepts adequate immutable evidence or the dependency is changed.
- The .NET SDK and CPython are build toolchains, not release payloads. Redistribution remains withheld pending exact installer provenance, hashes/signatures, applicable licences, and notices.
- NuGet `contentHash` values are restore-integrity metadata, not independently computed `.nupkg` hashes or publisher-signature proof.
- The PyYAML SHA-256 admits only `pyyaml-6.0.3-cp314-cp314-win_amd64.whl`; no sdist, other wheel, platform, or architecture is admitted.
- Vulnerability observations are point-in-time and source-dependent; zero returned records are not a guarantee of safety or advisory completeness.
- The Full tier is not green on the active machine because the enforced Smart App Control policy blocks two unsigned test DLL loads. The policy was not disabled or bypassed. `TL-0006` has no declared Full-tier trigger, but Full verification must be repeated in an approved executable/signing environment before a release gate.

## Verification evidence

| Scope | Result | Duration |
|---|---|---:|
| Pre-change governed Quick baseline | Passed; 80 tests plus bundle/repository validation | 63.168 s test |
| Final focused supply-chain regressions so far | 20/20 passed | 43.408 s test |
| Locked NuGet restore | Passed for all 26 projects with the exact graph | 9.877 s |
| NuGet direct/transitive advisory query | Exit 0; 26 projects/frameworks; 0 vulnerable top-level and 0 vulnerable transitive records | 6.700 s |
| Exact PyYAML/PyPI release check | Identity, wheel, yanked state, SHA-256, and schema matched; 0 non-withdrawn records | 0.372 s |
| Two working-tree SBOM generations and structural inspection | Byte-identical; 20 unique components, 21 dependency records, complete reference closure | 3.6 s including validation/generation summary |
| Defensive governed Full tier | 100 tests, validators, locked restore, format, and zero-warning Release build passed; final .NET tests failed on two Smart App Control blocks | 80.007 s Python tests; 21.35 s build; about 146 s wall |
| Isolated current and detached TL-0005 baseline diagnosis | Both affected project tests exited 1 under the same Smart App Control block; policy was preserved | 18.757 s baseline comparison plus isolated rerun |

Current working-tree evidence:

- dependency-input SHA-256: `f42a8ab7e4b2e47aaeb28225411a491db284072982cb6a7e540f61010d30a2f4`;
- dependency contract SHA-256: `725d6de01db6a94d7009ee956fc2675e2bbfb1bf45c301e8e4ae340f97657fb8`;
- deterministic working-tree SBOM SHA-256: `78f54c595d27287cf29377772ef0f41cbc2e823e472ca3df60d9ed3c91c438c4`, 104277 bytes;
- NuGet raw response SHA-256: `52947476747cce6e5f8919ef06d50ec212c537709525fc3c1c9254460cb38316`;
- PyPI raw response SHA-256: `c3f35597bc2f08cc990c2a5fe57bef6687b3a3d7c61d8b0ba4cc067777eb1def`.

The source revision was intentionally omitted from the working-tree SBOM. Exact-commit source binding is tested adversarially but will be exercised from a clean checkpoint checkout before the automatable work is declared complete.

## Preserved project provenance

`TL-0005` remains complete with Janne Vuorela's privacy-owner approval bound to commit `118240955b01ea4a0b941b00d357ea165b035981`. The historical `TL-0008` draft-1 physical-device procedure at source commit `4fa3ea050fd5e9985fde9cc8218281698d371cc8`, archived procedure SHA-256 `ef150dbf14b5db208582b7b526c7e0c6d0a5b912736e9e6519b8918abcf0928b`, remains superseded and must not be executed; no physical hardware walkthrough was performed for the v0.3.0 transition.

## Boundary and risk impact

- **Project vacuum / sibling integration:** No sibling repository, component, profile, catalogue entry, adapter, data source, or release dependency was introduced.
- **Data / migration:** No application data, database, migration, personal data, retention, telemetry, or cleanup behavior changed. Ignored `artifacts/sbom/` and `artifacts/audit/` files are local developer/release evidence only.
- **Release interface:** `RELEASE_INTERFACE.md` maps the implemented dependency digest, matrix review, SBOM, provenance, and audit evidence to later release fields without filling release-specific values or inventing an interface.
- **Security / privacy:** Exact pins, bounded inputs, safe URL/path checks, cache exclusion, truthful hash semantics, approval binding, and source-revision verification reduce substitution and false-evidence risk. Network access occurs only in explicit developer audit commands, not in the Core application or offline generator.
- **Accessibility / low-spec:** No UI, keyboard, focus, screen-reader, scaling, high-contrast, background activity, service, GPU use, or runtime resource cost was added. Generation is foreground, bounded, single-process work over checked-in text inputs.

## Changed paths

- `.gitattributes`
- `.github/workflows/verify.yml`
- `.gitignore`
- `Directory.Build.props`
- `README.md`
- `RELEASE_INTERFACE.md`
- `SECURITY.md`
- `TASKS.yaml`
- `eng/generate-sbom.ps1`
- `eng/verify.ps1`
- `tools/requirements.txt`
- `tools/supply_chain.py`
- `tools/tests/test_supply_chain.py`
- `tools/validate_bundle.py`
- `tools/validate_repository.py`
- `docs/supply-chain/dependencies.md`
- `docs/supply-chain/license-matrix.csv`
- `STATUS.md`
- `BUNDLE_MANIFEST.sha256` after final regeneration

The unrelated untracked `ThirdLife_Two-Team_Software_Portfolio_Roadmap_v2.1.docx` was present before TL-0006 and remains untouched and unstaged.

## Outstanding

1. Regenerate the governed bundle manifest, run the governed Quick gate, and create a scoped implementation checkpoint.
2. Generate and inspect a source-bound SBOM from a clean checkout of that exact commit, record the evidence, move `TL-0006` to `review`, and rerun the exact-tree Quick gate.
3. Obtain a named human licence/redistribution review bound to the exact reviewed commit and matrix SHA-256 before `done` or any release gate.
4. Repeat the Full tier in an approved environment that can execute the unsigned test assemblies, or after later governed signing/lifecycle work provides the approved execution path; do not weaken Smart App Control.

## Next dependency-ready task

`TL-0007` — Create synthetic pilot fixtures and reference inputs. Do not start it while TL-0006 remains the selected active task.
