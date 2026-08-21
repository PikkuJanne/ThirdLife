# Dependency, licence, provenance, and SBOM controls

This document is the governed supply-chain contract introduced by `TL-0006`. It covers every external component used to build, validate, test, or distribute ThirdLife Setup Core and every catalogue application identity, including project-created synthetic placeholders. It does not approve a component or grant legal advice. Dated audit checkpoints below are point-in-time technical evidence only. The licence and rights statements in `license-matrix.csv` are proposals. The global review table at the end of this document is the sole current pending/approved state, so row text remains stable when a review is bound to a matrix digest.

## Current scope

The checked-in matrix has exactly these current counts:

| Scope | Count | Distribution |
|---|---:|---|
| Runtime dependencies | 0 | None |
| Build-only dependencies and toolchains | 6 | Three GitHub actions are `remote-ci-only`; PyYAML and both toolchains are `not-shipped` |
| Test-only NuGet dependencies | 14 | `not-shipped` |
| Catalogue application identities | 4 | Project-created synthetic `production_eligible: false` placeholders; non-installable, no artifact or binary, and `not-shipped` |
| **Total governed supply-chain components** | **24** | 20 external build/test components plus 4 synthetic catalogue placeholders |

The 14 NuGet rows are the complete non-project package closure discovered in the test-project `packages.lock.json` files. Project-to-project references are part of this repository, not third-party components, and are not matrix rows. There are currently no external packages in a production project. The four catalogue rows correspond exactly to the project-created identities in `fixtures/catalog/catalog.yaml`; they are synthetic fixture metadata, not external applications, packages, installers, artifacts, or binaries. Each is marked `production_eligible: false` in the fixture, carries `NOASSERTION`, and withholds installation and redistribution rights separately.

An exact runtime count of zero is still governed inventory. A future runtime dependency or external or production catalogue application requires a complete matrix row, reviewed source and integrity evidence, and the update workflow below before admission. A catalogue row must identify the exact application identity, source or clearly declared synthetic origin, publisher, version, installation rights, redistribution rights, provenance, and distribution plan. The presence of a synthetic placeholder identity is not production admission or package approval. Absence from the matrix is a fail-closed condition, not implicit approval.

The accountable ThirdLife role in every current row is `Principal Software Architect`. `owner` names that project role; `upstream_publisher` separately names the external author/publisher or the explicit project-created synthetic origin. The owner is not presented as an external upstream publisher.

## Governed inputs

| Input | Control |
|---|---|
| `Directory.Packages.props` | Central direct NuGet versions; projects may not override them |
| `Directory.Build.props` | Locked restore and NuGet audit policy |
| `.gitattributes` | Cross-platform byte normalization for the matrix and other governed text inputs |
| `src/**/packages.lock.json`, `tests/**/packages.lock.json` | Exact resolved NuGet closure and NuGet `contentHash` values |
| `tools/requirements.txt` | Exact PyYAML release and the admitted CPython 3.14 Windows x64 wheel SHA-256 |
| `tools/supply_chain.py`, `eng/generate-sbom.ps1` | Validator/generator implementation and pinned-toolchain wrapper |
| `eng/verify.ps1` | Governed repository verification entry point that invokes the validator regressions |
| `global.json` | Exact .NET SDK version with roll-forward disabled |
| `eng/verify.ps1`, `.github/workflows/verify.yml` | Exact Python version and full-commit GitHub action pins |
| `fixtures/catalog/catalog.yaml` | Exact current synthetic catalogue identities and their shared fixture-file SHA-256; no executable package behavior |
| `docs/supply-chain/license-matrix.csv` | Ownership, purpose, scope, upstream evidence, proposed rights, distribution plan, integrity, provenance, and limitations |

[NuGet's lock-file guidance](https://learn.microsoft.com/nuget/consume-packages/package-references-in-project-files#locking-dependencies), [locked restore documentation](https://learn.microsoft.com/dotnet/core/tools/dotnet-restore), [pip's repeatable-install guidance](https://pip.pypa.io/en/stable/topics/secure-installs/), and [GitHub's guidance to pin actions to a full commit SHA](https://docs.github.com/en/actions/security-for-github-actions/security-guides/security-hardening-for-github-actions#using-third-party-actions) are the upstream control references.

### Matrix fields

The CSV headers and their order are contractual so generation and review remain deterministic.

| Field | Meaning |
|---|---|
| `component_type` | Ecosystem or control type: `github-action`, `nuget`, `pypi`, `toolchain`, or future `catalog-application` |
| `component_id` | Exact case-preserved upstream identity |
| `version` | Exact release or annotated action tag corresponding to the pinned material |
| `relationship` | `direct`, `transitive`, `ci`, or `toolchain` |
| `scope` | `runtime`, `build-only`, `test-only`, or `catalog-application` |
| `owner` | Accountable ThirdLife role |
| `upstream_publisher` | External author, project, foundation, publisher, or explicit project-created synthetic origin |
| `source` | Exact primary download or immutable source location where available; a clearly labelled non-routable sentinel only when a synthetic no-artifact row must satisfy the HTTPS field contract |
| `purpose` | Narrow project need served by the component |
| `declared_license` | Upstream's declaration; this is evidence, not ThirdLife approval |
| `license_evidence` | Primary upstream licence evidence, immutable when available |
| `proposed_license_conclusion` | Proposed interpretation; approval state is recorded only in the global review table |
| `proposed_installation_rights` | Installation/use proposal, separate from redistribution |
| `proposed_redistribution_rights` | Redistribution proposal and required conditions, separate from installation |
| `distribution_plan` | `not-shipped`, `remote-ci-only`, or a future explicitly reviewed release plan |
| `integrity_algorithm` | Meaning of the integrity value, without treating a version as a digest |
| `integrity_value` | Exact governed digest/commit, or a manifest field reference when the mechanism is a version pin |
| `provenance_reference` | Checked-in and primary upstream evidence connecting identity to the pin |
| `limitations` | Unresolved, mutable, platform-specific, or distribution-specific gaps |

Rows are sorted case-insensitively by `(component_type, component_id)`. Every field is required. The strings `version-pin` and `not-shipped` describe explicit mechanisms/states; an empty field is invalid.

### Integrity and provenance interpretation

- A NuGet lock-file `contentHash` is restore-integrity metadata recorded by NuGet. It is **not** an independently computed hash of the downloaded `.nupkg`, a signature result, or proof of publisher identity. The matrix therefore labels it `nuget-content-sha512` and preserves the exact base64 value without calling it an artifact hash.
- The PyYAML SHA-256 covers only `pyyaml-6.0.3-cp314-cp314-win_amd64.whl` from the exact `files.pythonhosted.org` URL in the matrix. It does not admit an sdist or a wheel for another Python version, architecture, or operating system.
- A GitHub action's integrity value is the full 40-character Git commit SHA used by the workflow. Its human-readable release tag is descriptive; the SHA is the execution pin.
- `.NET SDK` and `CPython` use the governed `version-pin` mechanism, referencing `global.json#sdk.version` and `.github/workflows/verify.yml#python-version` respectively. A version pin is not a binary digest: no exact Windows x64 installer hash is currently governed. They are not release payloads; any proposal to redistribute either toolchain must stop until exact binary provenance, hash/signature evidence, applicable licences, and complete third-party notices are recorded and reviewed.
- `xunit.abstractions` 2.0.3 exposes a legacy `licenseUrl` to a mutable `master` branch rather than a licence expression or immutable licence artifact. Its Apache-2.0 conclusion and both rights statements remain proposals with an explicit immutable-evidence gap.
- The four `catalog-application` rows use `sha256` over the exact bytes of `fixtures/catalog/catalog.yaml`; all four therefore carry the same file digest, `7f80078a24d9fa890738d344d3c705549c45d17d7712ce1f8d543d4ce47f8901`. This proves only which project-created fixture bytes declared the identities. It is not an installer or application-artifact hash. Because matrix `source` and `license_evidence` fields require HTTPS, these no-artifact rows use deliberately non-routable `example.invalid` sentinels and `NOASSERTION`; neither is upstream evidence or a licence conclusion. Installation and redistribution remain separately withheld.

## Deterministic offline SBOM

From a clean checkout with the pinned Python 3.14.7 toolchain available, generate the SBOM without network access:

```powershell
$sourceRevision = (git rev-parse HEAD).Trim()
powershell -NoProfile -File .\eng\generate-sbom.ps1 `
  -ProductVersion 0.3.0-dev `
  -SourceRevision $sourceRevision
```

The default output is:

```text
artifacts/sbom/thirdlife-setup-core.cdx.json
```

The wrapper accepts a different `-OutputPath`, a release-frozen `-ProductVersion`, and an optional lowercase 40-character `-SourceRevision`. Omit the source revision for a working-tree SBOM. When it is supplied, the generator requires it to equal the checked-out `HEAD` and requires the complete governed input set and bytes, including the matrix, to match that commit; a fabricated, stale, or dirty provenance claim fails closed. The generator reads only checked-in manifests, lock files, the workflow, and the matrix. It performs no restore, package download, licence lookup, vulnerability lookup, or other network request. For the same bytes and options, it writes the same canonical UTF-8 CycloneDX 1.6 JSON bytes; volatile timestamps and random serial numbers are not inputs. The [CycloneDX 1.6 JSON specification](https://cyclonedx.org/docs/1.6/json/) is the format authority.

Confirm byte determinism and retain the reported digest:

```powershell
powershell -NoProfile -File .\eng\generate-sbom.ps1 -ProductVersion 0.3.0-dev -SourceRevision $sourceRevision
$first = (Get-FileHash .\artifacts\sbom\thirdlife-setup-core.cdx.json -Algorithm SHA256).Hash
powershell -NoProfile -File .\eng\generate-sbom.ps1 -ProductVersion 0.3.0-dev -SourceRevision $sourceRevision
$second = (Get-FileHash .\artifacts\sbom\thirdlife-setup-core.cdx.json -Algorithm SHA256).Hash
if ($first -cne $second) { throw "SBOM output was not deterministic." }
```

Generation fails closed when a discovered NuGet, PyPI, GitHub action, toolchain, or catalogue component is absent or inconsistent in the matrix; when a required field is empty; when ordering or identity is ambiguous; or when a governed pin conflicts with the source manifests. Before the later `TL-0301` catalogue schema exists, the validator extracts only bounded top-level application `id` and `version` values from `fixtures/catalog/*.yaml`, rejects ambiguous YAML features, and requires those identities to match matrix rows one-to-one. That bootstrap check does not define executable catalogue behavior, make a placeholder installable, or replace the future schema and package review. The generated document contains a dependency-input digest, licence-matrix SHA-256, source revision when supplied, and component provenance/integrity references. It is inventory evidence, not a completed legal or vulnerability review.

## Dependency update workflow

1. State the narrow purpose, accountable owner, relationship, scope, and distribution plan. Prefer no new dependency when a small, maintainable standard-library implementation is sufficient.
2. Add or update the matrix row using primary upstream evidence. Record installation and redistribution proposals separately. Mutable, missing, or contradictory licence/provenance evidence is unresolved and blocks release use.
3. Pin the exact material: central NuGet version plus regenerated lock closure; exact Python artifact SHA-256; full GitHub commit SHA; exact toolchain version; or exact catalogue identity, publisher, source, installer digest/signature policy, and version.
4. For an intentional NuGet update, edit the central pin, regenerate locks with `dotnet restore ThirdLife.sln --force-evaluate --configfile NuGet.Config`, inspect every lock-file change, then return to `dotnet restore ThirdLife.sln --locked-mode --configfile NuGet.Config`. Normal verification must not rewrite the lock graph.
5. Run the expected test tier and the triggered Full tier for a dependency change. Generate and inspect the offline SBOM, compare its component set to the intended diff, and retain its SHA-256.
6. Run the point-in-time ecosystem audits below. A network, schema, source, or advisory failure is `not audited`; it is never converted to a pass.
7. Bind a named human licence/rights review to the reviewed commit and exact matrix SHA-256. Do not release while the table remains pending or a component limitation is incompatible with the distribution plan.

A version, source, publisher, licence, integrity value, purpose, scope, relationship, or distribution-plan change invalidates the earlier component review. “Latest”, a floating action tag, an unbounded package source, a missing hash, an undocumented tool, or an undeclared catalogue application fails closed.

## Locked restore and vulnerability review

These commands define repeatable review procedures. Their presence in this document is **not** a claim that an audit ran or found no vulnerabilities.

### NuGet

Run the Full tier so the audited, locked restore policy in `Directory.Build.props` is active:

```powershell
.\eng\verify.ps1 -Tier Full
```

After that exact restore, capture the full direct and transitive package result as machine-readable JSON without changing the graph:

```powershell
New-Item -ItemType Directory -Force .\artifacts\audit | Out-Null
dotnet package list `
  --project ThirdLife.sln `
  --vulnerable `
  --include-transitive `
  --configfile NuGet.Config `
  --format json `
  --output-version 1 `
  --no-restore `
  > .\artifacts\audit\nuget-vulnerabilities.json
if ($LASTEXITCODE -ne 0) { throw "NuGet vulnerability query failed." }
```

Inspect the JSON, record the UTC review time and configured sources, and hash the evidence file. The [`dotnet package list` vulnerability options](https://learn.microsoft.com/dotnet/core/tools/dotnet-package-list) and [NuGet audit behavior](https://learn.microsoft.com/nuget/concepts/auditing-packages) are the upstream references.

Limitations: the advisory query is network- and source-dependent and is point-in-time evidence. `--no-restore` is valid only after the exact locked restore in the same checkout; stale assets invalidate the result. A timeout, inaccessible source, authentication response, malformed JSON, unsupported output schema, warning that prevents a complete audit, or missing transitive result is `not audited`. No result certifies that an advisory is complete, that a package is safe, or that upstream metadata is truthful.

### PyPI exact-release check

The Python build dependency is intentionally limited to one admitted wheel, so the current check uses the official [PyPI JSON API](https://docs.pypi.org/api/json/) directly and introduces no additional audit package. Run this bounded exact-release procedure while online:

```powershell
$uri = 'https://pypi.org/pypi/PyYAML/6.0.3/json'
$expectedFile = 'pyyaml-6.0.3-cp314-cp314-win_amd64.whl'
$expectedHash = '4a2e8cebe2ff6ab7d1050ecd59c25d4c8bd7e6f400f5f82b96557ac0abafd0ac'

$response = Invoke-WebRequest -UseBasicParsing -Uri $uri
$raw = [string]$response.Content
if ([Text.Encoding]::UTF8.GetByteCount($raw) -gt 1MB) { throw 'PyPI response exceeded 1 MiB.' }
$metadata = $raw | ConvertFrom-Json
foreach ($property in 'info', 'urls', 'vulnerabilities') {
  if ($null -eq $metadata.PSObject.Properties[$property]) { throw "Missing PyPI field: $property" }
}
if ($metadata.info.name -cne 'PyYAML' -or [string]$metadata.info.version -cne '6.0.3') {
  throw 'PyPI identity/version mismatch.'
}
if ([bool]$metadata.info.yanked) { throw 'PyYAML 6.0.3 is yanked.' }
$wheel = @($metadata.urls | Where-Object { $_.filename -ceq $expectedFile })
if ($wheel.Count -ne 1 -or [bool]$wheel[0].yanked) { throw 'Expected wheel is missing or yanked.' }
if ([string]$wheel[0].digests.sha256 -cne $expectedHash) { throw 'Expected wheel hash changed.' }
$active = @($metadata.vulnerabilities | Where-Object { [string]::IsNullOrWhiteSpace([string]$_.withdrawn) })
if ($active.Count -ne 0) { throw "PyPI reports $($active.Count) non-withdrawn vulnerability record(s)." }
```

Record the UTC review time, endpoint, result, and a SHA-256 of the raw response when this check becomes release evidence. A request, size, JSON/schema, identity, wheel, yanked-state, digest, or vulnerability-field failure is `not audited` or a failed audit, never a pass.

Limitations: this checks only the exact PyYAML release and admitted Windows x64 wheel, and only vulnerability records returned by PyPI at that moment. It is not a general Python environment scanner, does not cover an sdist or other wheel, does not prove the absence of unpublished vulnerabilities, and does not replace human licence/provenance review. Adding a separate audit tool would itself be a dependency change requiring this workflow.

### TL-0006 implementation audit checkpoint

The following checks ran from the active Windows checkout on 2026-08-21 after a successful locked restore. They establish the task implementation checkpoint; they do not remain current indefinitely and must be repeated for a release or dependency change.

| Ecosystem | UTC start | Command or endpoint | Result | Duration | Evidence SHA-256 |
|---|---|---|---|---:|---|
| NuGet | `2026-08-21T15:34:22Z` | `dotnet package list --project ThirdLife.sln --vulnerable --include-transitive --configfile NuGet.Config --format json --output-version 1 --no-restore` against the sole configured `nuget.org` audit source | Exit 0; 26 projects and 26 target frameworks inspected; 0 vulnerable top-level and 0 vulnerable transitive package records | 6.700 s | Raw UTF-8 response: `52947476747cce6e5f8919ef06d50ec212c537709525fc3c1c9254460cb38316` |
| PyPI | `2026-08-21T15:20:07Z` | `https://pypi.org/pypi/PyYAML/6.0.3/json` | Identity matched; admitted CPython 3.14 Windows x64 wheel present and not yanked; wheel SHA-256 matched; 0 non-withdrawn vulnerability records; response `last_serial` 31526105 | 0.372 s | Raw response: `c3f35597bc2f08cc990c2a5fe57bef6687b3a3d7c61d8b0ba4cc067777eb1def` |

Environment: Windows, .NET SDK 10.0.400, and CPython 3.14.7. The NuGet result is limited to advisories returned through the configured source for the already-restored graph. The PyPI result is limited to the exact release and wheel named above. Neither zero-result observation proves the absence of unpublished, delayed, incomplete, or incorrectly mapped advisories, and neither substitutes for the named human licence and redistribution review recorded below.

## Release-interface evidence mapping

`RELEASE_INTERFACE.md` remains the release authority; development values must not be copied into frozen fields as guesses.

| Release-interface evidence | TL-0006 source |
|---|---|
| Product licence | The current 24-component candidate matrix review is approved and bound below; the final product licence remains TBD at release freeze |
| Dependency-lock revision | Frozen source commit plus dependency-input digest emitted in the SBOM |
| SBOM/third-party notices | Generated CycloneDX file, its SHA-256, matrix digest, review record, and later release-specific notices |
| Source commit | Exact checked-out `HEAD` supplied as `-SourceRevision`, with every governed input verified against that commit |
| Package/update provenance and verification | Matrix source/integrity/provenance fields plus locked-restore and audit evidence |
| External-asset restoration | Exact pins and hashes in the governed inputs; toolchain installer hashes remain an explicit gap because toolchains are not shipped |

This task does not produce a release artifact hash, signature, installer, third-party-notices bundle, immutable release tag, or final product-licence decision. Those remain release-gate evidence.

## Boundary, data, accessibility, and resource impact

- **Project vacuum:** all inventory is project-local or public generic tooling. The four catalogue identities are generic project-created synthetic placeholders; there is no sibling repository, sibling package/profile/catalogue entry, sibling adapter or data access, cross-project release edge, or shared service.
- **Data and migration:** no application data, personal data, database, migration, telemetry, or retention behavior changes. The catalogue file is deterministic public-reference fixture data, and generated SBOM/audit files are local release evidence under `artifacts/`; neither is application runtime data.
- **Network and privilege:** offline SBOM generation is local, foreground, and unelevated. The explicit vulnerability procedures are developer/release operations that require network access; the Core application does not gain background network activity or privilege.
- **Accessibility:** no UI, interaction, focus, keyboard, screen-reader, scaling, high-contrast, or user-visible error path is added.
- **Modest hardware:** generation scans a finite set of checked-in text files, uses conservative single-process work, and writes one bounded JSON artifact. It adds no GPU requirement, resident service, background index, cache, or runtime memory/storage cost. Results apply only to the active reference machine and deterministic repository inputs; they make no cross-hardware claim.

## Human licence and rights review

The global review is approved for the exact 24-component matrix and reviewed commit recorded below. The approval accepts the proposals and limitations exactly as written; it does not convert a withheld right into an allowed right or authorize blanket installation, redistribution, production use, or release.

| Field | Value |
|---|---|
| Review status | Approved |
| Reviewer | Janne Vuorela |
| Role | Principal Software Architect & Sole Project Owner |
| Review date | 2026-08-21 |
| Result | Approved without conditions |
| Reviewed commit | 7afc6c7599523fb56a66774a29e9107e6a9a0aac |
| Matrix SHA-256 | 32ff63e4e6deb703f978efad368ba54cdc898004106fa443e211d046126ee193 |

Janne Vuorela supplied the explicit approval in the Codex task, acting as Principal Software Architect & Sole Project Owner. The governed result `Approved without conditions` means no additional unrecorded condition was added; all limitations already written into the contract remain binding. In particular, the approval preserves the mutable licence-evidence limitation for `xunit.abstractions`, withheld redistribution of `.NET SDK` and `CPython` pending exact installer provenance, hashes/signatures, applicable licences, and notices, and the four placeholders' `NOASSERTION`, non-installable, no-artifact, `not-shipped`, and separately withheld-rights state. It grants no blanket redistribution right and is not legal advice, a final product-licence decision, production admission, or release authorization.

A material version, source, publisher, licence, integrity, purpose, scope, relationship, distribution-plan, or matrix change invalidates this review and requires a new named approval bound to the changed commit and digest.
