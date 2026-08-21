# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-08-21  
**Snapshot preparation time:** 2026-08-21T18:30:41+02:00  
**Bundle baseline:** 0.3.0  
**Portfolio baseline:** ThirdLife Software Portfolio v2.1  
**Current milestone:** M0 — Foundation and product contract  
**Current task:** `TL-0006` — Create dependency, license, and SBOM controls  
**Task state:** `done`

## Current state

`TL-0006` is complete. Its focused checks, locked restore, point-in-time vulnerability queries, deterministic working-tree inspection, and exact-commit clean-checkout SBOM proof pass. Janne Vuorela, Principal Software Architect & Sole Project Owner, approved the complete licence and redistribution proposal contract without conditions on 2026-08-21, bound to reviewed commit `11711bcab68c5c23fea4705a49ca7ff2d021d485` and matrix SHA-256 `f88260289c14c8b3d651d6149f560f083232bf131ee5398563462e1df4e9ca73`.

The approval accepts the contract exactly as written. It preserves the `xunit.abstractions` evidence limitation and withheld toolchain redistribution; it is not blanket redistribution permission, legal advice, a final product-licence decision, or release authorization.

A defensive Full-tier run passed every step through the zero-warning Release build, but Windows Smart App Control blocked two unsigned DLL loads in the final test phase; the same two project tests are blocked from the untouched TL-0005 baseline. This environmental limitation is recorded without weakening or bypassing the policy.

The current inventory contains 20 external components: 14 NuGet test-only packages and 6 build-only components (3 GitHub Actions, PyYAML, .NET SDK, and CPython). There are zero runtime dependencies and zero catalogue applications. Project-to-project references are not external packages.

## Git state

| Field | Verified value |
|---|---|
| Remote | `origin` → GitHub repository `PikkuJanne/ThirdLife` |
| Branch | `codex/tl-0006-dependency-license-sbom-controls` |
| Implementation baseline | `b5c435c92e55f1d326963fc4aea9d4dd50525d37` — completed TL-0005 checkpoint |
| History handling | Continued from the fetched baseline; no reset, rebase, force push, or history rewrite |
| Implementation checkpoint | `46de0c8597f462ad083284797e6f1fa09d52d47b` — clean-checkout SBOM source revision |
| Reviewed contract checkpoint | `11711bcab68c5c23fea4705a49ca7ff2d021d485` — exact matrix reviewed by the named owner |
| Approval checkpoint | The commit containing this file; resolve with `git rev-parse HEAD` after commit |
| Upstream | `origin/codex/tl-0006-dependency-license-sbom-controls`; approval-session baseline was equal at `11711bcab68c5c23fea4705a49ca7ff2d021d485` |

The configured SSH remote rejected unattended public-key authentication on this machine in the preceding task. Approval-checkpoint publication uses GitHub CLI's authenticated HTTPS credential bridge without changing the configured remote or exposing a credential.

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

The licence matrix SHA-256 is `f88260289c14c8b3d651d6149f560f083232bf131ee5398563462e1df4e9ca73`. Its global review state is `Approved`; row text remains explicitly proposed so the exact human decision and the underlying evidence are not conflated.

## Material limitations

- `xunit.abstractions` 2.0.3 publishes a legacy mutable licence URL. The approved contract preserves that evidence limitation and withholds redistribution until stronger immutable evidence is recorded and reviewed or the dependency is changed.
- The .NET SDK and CPython are build toolchains, not release payloads. Redistribution remains withheld pending exact installer provenance, hashes/signatures, applicable licences, and notices.
- NuGet `contentHash` values are restore-integrity metadata, not independently computed `.nupkg` hashes or publisher-signature proof.
- The PyYAML SHA-256 admits only `pyyaml-6.0.3-cp314-cp314-win_amd64.whl`; no sdist, other wheel, platform, or architecture is admitted.
- Vulnerability observations are point-in-time and source-dependent; zero returned records are not a guarantee of safety or advisory completeness.
- The Full tier is not green on the active machine because the enforced Smart App Control policy blocks two unsigned test DLL loads. The policy was not disabled or bypassed. `TL-0006` changed dependency governance, validation, and evidence but did not change any component identity, version, source, or restored dependency graph, so the dependency-change Full trigger did not apply and the task declares no other Full-tier trigger. Full verification must still be repeated in an approved executable/signing environment before a release gate.

## Verification evidence

| Scope | Result | Duration |
|---|---|---:|
| Pre-change governed Quick baseline | Passed; 80 tests plus bundle/repository validation | 63.168 s test |
| Pre-approval focused supply-chain regressions | 20/20 passed | 43.408 s test |
| Locked NuGet restore | Passed for all 26 projects with the exact graph | 9.877 s |
| NuGet direct/transitive advisory query | Exit 0; 26 projects/frameworks; 0 vulnerable top-level and 0 vulnerable transitive records | 6.700 s |
| Exact PyYAML/PyPI release check | Identity, wheel, yanked state, SHA-256, and schema matched; 0 non-withdrawn records | 0.372 s |
| Two pre-approval working-tree SBOM generations and structural inspection | Byte-identical; 20 unique components, 21 dependency records, complete reference closure | 3.6 s including validation/generation summary |
| Exact-commit clean-checkout SBOM proof | Passed twice from detached checkpoint `46de0c8`; exact `HEAD` provenance, deterministic bytes, complete reference closure, clean ignored-output behavior | 19.019 s wall including worktree creation and cleanup |
| Defensive governed Full tier | 100 tests, validators, locked restore, format, and zero-warning Release build passed; final .NET tests failed on two Smart App Control blocks | 80.007 s Python tests; 21.35 s build; about 146 s wall |
| Isolated current and detached TL-0005 baseline diagnosis | Both affected project tests exited 1 under the same Smart App Control block; policy was preserved | 18.757 s baseline comparison plus isolated rerun |
| Named licence/rights review | Janne Vuorela approved the exact reviewed commit and matrix without conditions while preserving all recorded limitations and withheld rights | Explicit approval at 2026-08-21T16:30:41Z |
| Post-approval targeted regression | The live approved review state and exact Git/matrix binding passed all 20 focused supply-chain tests | 43.440 s |
| Post-approval working-tree SBOM inspection | Two outputs were byte-identical CycloneDX 1.6 documents with approved review metadata, 20 components, 21 dependency records, and complete reference closure | SHA-256 `dda4cb9363deecd8abfe24e3da819dfdda5436ff0c6a69e210e17262a6b9fbcb`; 104278 bytes |

The exact-tree Quick gate is run after this status record and the bundle manifest are finalized. Its result, the final metadata commit, push, and fetched remote-equality check are recorded in the session completion report without changing the verified tree afterward.

Current approved working-tree evidence and preserved clean-checkout checkpoint evidence:

- dependency-input SHA-256: `f42a8ab7e4b2e47aaeb28225411a491db284072982cb6a7e540f61010d30a2f4`;
- approved dependency contract SHA-256: `c5c7d45e3aa6d96b4fa577579d5eeba40aaceedfdd29951b12fa1e61aca0ea89`;
- approved deterministic working-tree SBOM SHA-256: `dda4cb9363deecd8abfe24e3da819dfdda5436ff0c6a69e210e17262a6b9fbcb`, 104278 bytes;
- deterministic source-bound clean-checkout SBOM SHA-256: `b7044cf40d344933a051e9ccdeb7e385654a712f76aab61f5fa9011bc4810c36`, 104399 bytes;
- clean-checkout dependency-input SHA-256: `6c95b753c9d1fe72bae2f55ce10d1d19dc2271c9e81e953a202c0db457211728`;
- NuGet raw response SHA-256: `52947476747cce6e5f8919ef06d50ec212c537709525fc3c1c9254460cb38316`;
- PyPI raw response SHA-256: `c3f35597bc2f08cc990c2a5fe57bef6687b3a3d7c61d8b0ba4cc067777eb1def`.

The working-tree SBOM intentionally omitted source revision. The clean-checkout SBOM embeds and verifies source revision `46de0c8597f462ad083284797e6f1fa09d52d47b`; the disposable checkout was clean before generation and remained clean afterward except for ignored SBOM artifacts, then was removed.

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

1. Repeat the Full tier in an approved environment that can execute the unsigned test assemblies, or after later governed signing/lifecycle work provides the approved execution path; do not weaken Smart App Control. This is a release-gate limitation, not missing TL-0006 human evidence.

## Next dependency-ready task

`TL-0007` — Create synthetic pilot fixtures and reference inputs.
