# ThirdLife Setup Core — Dependency, Licence, and SBOM Controls

**Status:** Draft for licence-owner review  
**Control revision:** TL-0006 draft 1  
**Inventory date:** 2026-08-14  
**Review result:** Pending  
**Reviewing owner and role:** Pending  
**Review date:** Pending  
**Reviewed source commit:** Pending  
**Reviewed matrix SHA-256:** Pending  
**Generated SBOM SHA-256:** Pending  
**Approval reference:** Pending  
**Authority:** Derived control record under `DECISIONS.md`, `ROADMAP.md`, `PROJECT_BOUNDARY.md`, `SECURITY.md`, `AGENTS.md`, and `TASKS.yaml`

This document records the current source and development dependency baseline for ThirdLife Setup Core. It is not a new authority tier, legal advice, a release approval, or evidence that a future installer has the same contents. Frozen decisions and the canonical **Owns / Does not own** boundary prevail.

No licence or redistribution approval is recorded. After the automated implementation and verification are complete, `TL-0006` remains `review` until a named human approves the exact matrix revision as described in [Human licence and redistribution review](#human-licence-and-redistribution-review); release mode remains unavailable until then.

## Control outcomes and exclusions

This baseline implements the documentation side of D-043 by keeping component ownership, exact versions, sources, purposes, candidate licence evidence, content integrity where currently available, and installation and redistribution decisions in one machine-readable matrix. It also defines how a deterministic development SBOM and later release evidence are derived.

It does not:

- choose or implement a package-installation backend, WinGet adapter, catalogue schema, or WinGet Configuration/DSC path under D-024 through D-026;
- approve a package, licence, installation right, redistribution right, third-party notice, release candidate, or release gate;
- claim that the development SBOM describes a future installer, runtime payload, Windows component, or recipient machine;
- fill any `RELEASE_INTERFACE.md` placeholder before verified release behavior exists under D-052;
- inspect, reference, or depend on a sibling project under D-055 and D-056; or
- add telemetry, an uploader, a background dependency scanner, a service, or a product runtime dependency.

## Current inventory

[`license-matrix.csv`](license-matrix.csv) contains exactly one row for each unique current external source/development component. Repeated use of the same NuGet package across test projects remains one component row.

| Dependency class | Direct | Transitive | Platform | Total | Current meaning |
|---|---:|---:|---:|---:|---|
| `test_only` | 4 | 10 | 0 | 14 | NuGet packages used only by the thirteen test projects. |
| `build_only` | 4 | 0 | 2 | 6 | PyYAML, three pinned GitHub Actions, the .NET SDK, and Python. |
| `runtime` | 0 | 0 | 0 | 0 | No third-party production/runtime package is introduced by the current scaffold. |
| `catalog_application` | 0 | 0 | 0 | 0 | No application catalogue entry exists yet. |
| **Total** | **8** | **10** | **2** | **20** | Current unique external components. |

The `runtime` count of zero is deliberately narrow. It means that the current production projects have no third-party `PackageReference` dependency. It does not decide whether a future release is framework-dependent or self-contained, and it does not claim that the operating system or .NET runtime will be absent from the eventual deployment model. That packaging decision and its exact payload remain future release work.

The current inventory excludes repository-owned project references, source files, .NET framework references supplied by the SDK, Windows itself, and the mutable GitHub-hosted runner image. Those are not rows in the twenty-component third-party matrix. The SDK and Python are included because the checked-in verification workflow selects exact toolchain versions. The runner image remains an environment limitation recorded below.

### NuGet test graph

`Directory.Packages.props` centrally pins the four direct test packages and prohibits central version overrides. The project-local `packages.lock.json` files record the complete resolved graph and NuGet SHA-512 content hashes. Across all current test locks, the unique graph contains:

- direct: `coverlet.collector` 6.0.4, `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3, and `xunit.runner.visualstudio` 3.1.4;
- transitive: `Microsoft.CodeCoverage` 17.14.1, `Microsoft.TestPlatform.ObjectModel` 17.14.1, `Microsoft.TestPlatform.TestHost` 17.14.1, `Newtonsoft.Json` 13.0.3, `xunit.abstractions` 2.0.3, `xunit.analyzers` 1.18.0, `xunit.assert` 2.9.3, `xunit.core` 2.9.3, `xunit.extensibility.core` 2.9.3, and `xunit.extensibility.execution` 2.9.3.

The production locks contain no NuGet package row. Central transitive pinning remains disabled; the reviewed direct packages select the transitive graph, and the lock files freeze the resolution.

### Build and validation inputs

The six build-only inputs are:

- PyYAML 6.0.3, pinned with the SHA-256 of the Windows CPython 3.14 x64 wheel in `tools/requirements.txt`;
- `actions/checkout`, `actions/setup-dotnet`, and `actions/setup-python`, each selected by an exact 40-hex Git commit in `.github/workflows/verify.yml`;
- .NET SDK 10.0.400, selected with roll-forward disabled in `global.json`; and
- Python 3.14.7 x64, selected by the authoritative Windows workflow.

A Git commit ID is provenance for an exact Action revision; it is not recorded as a downloaded-archive content hash. Likewise, the current repository does not record the .NET SDK or Python installer content hash. Their matrix hashes therefore remain `not_recorded` instead of being inferred. These gaps must be considered again when release inputs are frozen.

## Sources of truth and reconciliation

The governed inputs have distinct jobs:

| Input | Governs |
|---|---|
| `global.json` | Exact .NET SDK and disabled roll-forward. |
| `Directory.Packages.props` | Exact direct NuGet versions and central version policy. |
| `src/*/packages.lock.json` and `tests/*/packages.lock.json` | Complete resolved NuGet graphs and NuGet SHA-512 content hashes. |
| `NuGet.Config` | Allowed package and audit sources and source mapping. |
| `tools/requirements.txt` | Exact Python package version and accepted wheel SHA-256. |
| `.github/workflows/verify.yml` | Exact GitHub Action revisions and Python version/architecture. |
| `docs/supply-chain/license-matrix.csv` | Ownership, purpose, candidate licence evidence, separate rights decisions, and human-review status. |

The SBOM generator must reject a missing, extra, duplicated, differently versioned, or differently classified matrix component. It must not repair the matrix, resolve an unpinned “latest” version, substitute a source, or silently omit an unknown component.

Every dependency, tool, Action, or future catalogue application change requires all affected pins, locks, matrix rows, tests, documentation, and generated evidence to be reviewed together. A change to identity, source, owner/publisher, version, digest, purpose, dependency class, licence evidence, rights, or inclusion invalidates prior component approval and any SBOM or release evidence derived from it.

## Licence and rights posture

`license_expression` is a candidate SPDX expression taken from exact package metadata or exact upstream evidence where available. It is not an approval. `NOASSERTION` means the checked-in evidence is insufficient to make a reliable expression claim.

Installation and redistribution are separate decisions:

- `installation_rights` asks whether the project may obtain and use the component in the stated build/test role;
- `redistribution_rights` asks whether ThirdLife may copy or bundle it in distributed material; and
- `bundled_in_release` records the present inclusion decision, not a licence conclusion.

Every current rights field is `pending_human_review`, every current review status is `pending`, and every current component is `bundled_in_release=no`. “Not currently bundled” does not prove that redistribution would be permitted. A future change to `yes` requires an exact payload review, licence and notice decision, and new approval.

Two rows require particular care:

- `xunit.abstractions` 2.0.3 has no embedded SPDX expression in its legacy NuGet metadata and points to a mutable upstream licence URL. Its expression remains `NOASSERTION` until an immutable licence source for the exact package is reviewed.
- the .NET SDK row remains `NOASSERTION` because source-code licensing and Microsoft binary-distribution terms must not be collapsed into one unreviewed expression.

## Vulnerability review

### Dated result

On 2026-08-14, the following Windows command completed with exit code 0 for the 26-project solution and emitted no `NU1900` through `NU1905` audit warning:

```powershell
dotnet restore ThirdLife.sln --configfile NuGet.Config --locked-mode --force-evaluate -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=low
```

This is evidence that the chosen NuGet audit reported no known low-or-higher advisory for the resolved direct and transitive NuGet graph at that time. It is not a statement that the packages are vulnerability-free.

Repository policy keeps `NuGetAudit=true`, `NuGetAuditMode=all`, and `NuGetAuditLevel=low` explicit so SDK-default changes cannot silently narrow coverage. Locked restore remains the dependency-resolution gate.

### Limitations

- Advisory data is network-sourced and time-varying. A clean result is only a dated observation; a later database update can change it.
- The NuGet audit covers the NuGet package graph. It does not audit PyYAML, GitHub Actions, the .NET SDK, Python, Windows, the runner image, or a future catalogue application.
- No separate Python or GitHub Action vulnerability scanner is introduced by `TL-0006`; adding one would itself add a tool and dependency graph requiring pinning, provenance, licence review, and approval.
- An audit with unavailable or unsuitable vulnerability data must remain unavailable or failed. It must not be summarized as “no vulnerabilities.” `NU1900` through `NU1905` are therefore material review signals.
- Known-advisory matching does not detect a compromised but not-yet-reported upstream, malicious maintainer, build-system compromise, dependency confusion outside the enforced sources, or a licence/provenance defect.
- Any advisory suppression requires a separately reviewed, exact advisory decision and must not be added merely to obtain a green build.

Non-NuGet components remain subject to dated upstream advisory and provenance review before an applicable release gate. Their absence from the NuGet audit is recorded, not treated as a pass.

## Deterministic SBOM contract

`eng/generate-sbom.ps1` produces an offline, deterministic CycloneDX 1.7 JSON document from the checked-in source/development inputs. Development mode is the default and uses product version `0.0.0-development` with inventory scope `source-and-development`.

For identical governed inputs, development output is byte-for-byte stable:

- component order and serialized properties are stable;
- the output contains no current timestamp, random serial number, machine-specific absolute path, username, or environment-derived product identity;
- component content digests come only from checked-in locks/manifests;
- GitHub Action commit IDs remain provenance rather than being mislabeled as content hashes;
- the complete detected component set is reconciled with the exact matrix; and
- output is written atomically as UTF-8 with LF line endings.

Generate an inspectable development SBOM from a clean checkout after installing the documented exact toolchain and hashed Python requirement:

```powershell
.\eng\generate-sbom.ps1 -OutputPath "$env:TEMP\ThirdLife.development.cdx.json"
```

The normal full verification command also exercises deterministic SBOM generation and repository reconciliation:

```powershell
.\eng\verify.ps1
```

Release mode must fail closed unless the caller supplies an explicit product version and 40-hex source revision and every matrix row is `approved` with non-pending installation and redistribution decisions. At this draft revision those conditions are intentionally unsatisfied. A generated development SBOM must never be renamed or described as a release SBOM.

The generator describes the governed source/development inventory, not the eventual installed file set. Release packaging, payload inspection, third-party notices, artifact hashes/signatures, and final SBOM comparison remain release-task responsibilities.

## Release-interface evidence mapping

`RELEASE_INTERFACE.md` remains unchanged and its placeholders remain truthful. The following mapping shows how later verified release evidence can populate them without guessing now:

| Future release-interface evidence | `TL-0006` control/input | Evidence still required later |
|---|---|---|
| Licence | Exact approved matrix revision and SHA-256, named approver, date, approval reference, and resolved notice obligations. | Confirm the frozen release payload has not changed and include final notices. |
| Source revision/dependency lock | Explicit 40-hex source revision; `global.json`; `Directory.Packages.props`; all lock files; hashed Python requirement; exact Action revisions. | Freeze the release source commit and dependency-input hashes. |
| Dependency provenance | Matrix owner/source/purpose fields, exact pins, NuGet content hashes, PyYAML wheel hash, and Action commit provenance. | Verify the actual release build used those reviewed inputs and record unavailable hashes truthfully. |
| Dependency and SBOM hashes | Checked-in package digests plus the SHA-256 of the exact generated SBOM. | Freeze and hash the final SBOM and evidence index. |
| SBOM/third-party notices | CycloneDX 1.7 generator, approved matrix, and notice decisions. | Generate from and compare with the frozen release payload; publish approved notices. |
| Release artifact hash/signature/source/size | No value is supplied by this task. | Produce from exact release bytes during packaging/freeze work. |

This mapping satisfies readiness for later evidence collection; it does not populate or approve a release field.

## Human licence and redistribution review

**Review state:** **Pending — no approval has been recorded.**

Before `TL-0006` can be marked `done`, a named human licence/release owner must review the exact commit and exact CSV bytes and record durable evidence that:

- all 20 component identities, versions, owners, sources, purposes, classes, directness values, and provenance references match the governed inputs;
- every candidate licence expression and evidence reference applies to the exact component/version, with `NOASSERTION` rows resolved or explicitly rejected;
- installation rights and redistribution rights have each been decided independently for every row;
- `bundled_in_release=no` has not been used as a substitute for a redistribution decision;
- required notices, attribution, source-offer, or other obligations are recorded without assuming a future payload;
- SDK, Python, Action, and package provenance limitations are accepted or block the task/release as appropriate;
- the current zero runtime and zero catalogue-application counts are accurate and do not pre-approve a future addition;
- no component, source, catalogue entry, or acceptance claim creates a sibling-project dependency; and
- the dated vulnerability result and its non-NuGet/time-of-check limitations are understood.

Durable approval metadata must include the approver’s name, licence/release-owner role, approval date, exact reviewed commit, SHA-256 of `license-matrix.csv`, and a stable approval reference such as a GitHub review or approved evidence record. The approved rows must then use explicit non-pending rights values and `review_status=approved`; a narrative approval that leaves the machine-readable matrix pending is insufficient.

This approval covers only the exact reviewed baseline. Any later component, version, source, digest, licence, rights, purpose, class, or release-inclusion change returns the affected row to `pending` and requires a new review before the applicable release gate.

## Security, privacy, accessibility, and low-spec impact

- Security: locks, exact sources, digests where recorded, fail-closed reconciliation, dated vulnerability review, and human rights review address the dependency portion of `THR-014`/`AC-019`; later artifact provenance and release freeze controls remain outstanding.
- Privacy: the inventory and deterministic SBOM contain no personal content, secrets, donor/recipient data, username, machine path, random device identifier, telemetry, or sibling data.
- Accessibility: the deliverables are plain Markdown, CSV, JSON, and standard command output; no UI or keyboard/screen-reader journey changes.
- Low-spec: generation is an explicit bounded foreground operation over twenty metadata rows and checked-in manifests. It creates no background task, service, index, unbounded cache, or runtime memory/storage load.
- Project vacuum: every row is a generic public build/test input. There is no sibling package, repository, service, schema, fixture, catalogue entry, or release dependency.

## Residual limitations

- Human licence and separate-rights review is pending, so release-mode generation and task completion remain gated.
- `xunit.abstractions` and the .NET SDK have `NOASSERTION` licence expressions pending exact review.
- SDK and Python installer content hashes are not checked in; Action commit IDs are not download content hashes.
- The Windows runner label identifies the authoritative environment but not immutable runner-image contents.
- The selected vulnerability audit does not cover six non-NuGet build inputs and cannot detect unreported compromise.
- The development SBOM is source/development inventory evidence, not a release-payload SBOM, installer inventory, notice file, signature, or release approval.
