# ThirdLife Setup Core

This repository develops ThirdLife Setup Core: a local-first, auditable Windows refurbishment workflow for volunteer and staff refurbishers. Development follows the frozen decisions, standalone Team B/B1 boundary, and dependency-ordered task graph checked into this repository.

**Current project:** Team B / B1 — ThirdLife Setup Core  
**Pilot gate:** `TL-0611` — controlled v0.1 partner pilot  
**Standalone release gate:** `TL-0710` — ThirdLife Setup Core 1.0  
**Next Team B project after stable:** Scam Explainer  
**Future integration project:** Team B / B4 — ThirdLife Deployment and Suite Assembly

The governed roadmap contains **91 tasks**, **8 milestone gates**, and **57 frozen decisions**. Current implementation progress and the next dependency-ready work are recorded in `TASKS.yaml`; do not infer them from this README.

## What changed in portfolio-aligned bundle 0.2.0

- Recast the repository explicitly as **ThirdLife Setup Core**, Team B/B1, while retaining `ThirdLife.*` code identity.
- Froze the project-vacuum rule: no sibling repository, branch, database, service, schema, or active release dependency.
- Deferred all sibling-specific catalogue/profile/adapter/compatibility work to the future B4 suite project.
- Added the binding `PROJECT_BOUNDARY.md` and non-binding `FUTURE_ASSEMBLY_NOTES.md` deferral log.
- Added the required project baselines `SECURITY.md`, `ACCESSIBILITY.md`, and `LOW_SPEC.md`.
- Added `RELEASE_INTERFACE.md`, completed only from verified preview/stable behavior rather than speculative APIs.
- Split controlled-pilot authorization (`TL-0611`) from the independently releasable Core 1.0 gate (`TL-0710`).
- Added M7 work for recipient-controlled accessibility, basic operating-system backup onboarding, installer/update/uninstall hardening, black-box samples/interface documentation, stable release evidence, and portfolio-boundary review.
- Set the post-release transition to **Scam Explainer**; B4 integration remains later.

See `CHANGELOG.md` for the file-level change record.

## Governance and repository contents

| File | Role |
|---|---|
| `ROADMAP.md` | Binding product scope, portfolio position, architecture, milestone sequence, gates, test/evidence model, and Team B transition. |
| `DECISIONS.md` | Highest-authority frozen project, safety, privacy, architecture, portfolio-boundary, and delivery choices. |
| `PROJECT_BOUNDARY.md` | Canonical **Owns / Does not own**, data boundary, project-vacuum rules, and future B4 late-integration contract. |
| `SECURITY.md` | Threat baseline, privilege/package/data/update controls, reporting placeholder, and release security obligations. |
| `ACCESSIBILITY.md` | Operator and recipient accessibility requirements, test evidence, and limitation policy. |
| `LOW_SPEC.md` | Resource budgets, constrained-test method, benchmark evidence, and graceful-degradation rules. |
| `TASKS.yaml` | Machine-readable DAG with 91 tasks, dependencies, deliverables, acceptance, verification, executor, environment, and evidence. |
| `AGENTS.md` | Codex operating contract: read order, task selection, allowed edits, project-vacuum rules, architecture, testing, and reporting. |
| [`docs/product-contract.md`](docs/product-contract.md) | Concise product identity, outcome, delivery cuts, Team B queue, standalone rules, and quality-baseline map. |
| [`docs/non-goals.md`](docs/non-goals.md) | Explicit existing-PC, bypass, optimizer, sibling-domain, shared-infrastructure, and early-B4 exclusions. |
| [`docs/glossary.md`](docs/glossary.md) | Governed meanings for evidence, requirements, blockers, dispositions, action state, and frozen-release integration terms. |
| [`docs/change-control.md`](docs/change-control.md) | Exact authority order, task-state limits, governed-amendment process, contradiction stop rule, and review checklist. |
| [`docs/security/threat-model.md`](docs/security/threat-model.md) | Assets, actors, threats, control/task mappings, residual-risk decisions, and named-owner approval state tracked by `TL-0004`. |
| [`docs/security/data-flow.md`](docs/security/data-flow.md) | Accessible diagram and textual inventory of processes, stores, flows, validation, recovery, and distinct trust boundaries. |
| [`docs/security/abuse-cases.md`](docs/security/abuse-cases.md) | Stable adversarial scenarios with detection, fail-closed behavior, recovery, task traceability, and residual risks. |
| [`docs/supply-chain/dependencies.md`](docs/supply-chain/dependencies.md) | Dependency classes, provenance controls, vulnerability-review method, SBOM scope, limitations, and release evidence mapping. |
| [`docs/supply-chain/license-matrix.csv`](docs/supply-chain/license-matrix.csv) | Machine-readable component ownership, purpose, source, licence evidence, and separate installation/redistribution decisions. |
| [`eng/generate-sbom.ps1`](eng/generate-sbom.ps1) | Offline deterministic CycloneDX development/release SBOM entry point; release mode fails closed until human rights review is complete. |
| `CODEX_START_PROMPT.md` | First-session prompt and reusable execution, review, gate, security, accessibility, recovery, deferral, release-interface, and handoff prompts. |
| `RELEASE_INTERFACE.md` | Human-readable black-box release sheet, populated as a preview draft at `TL-0610`, completed at `TL-0706`, and frozen before `TL-0710`; not a shared API. |
| `FUTURE_ASSEMBLY_NOTES.md` | Non-binding B4 backlog for cross-project ideas; nothing here can block or expand B1. |
| `CHANGELOG.md` | Bundle 0.2.0 modification record. |
| `TASKS.schema.json` | JSON Schema for the task graph and portfolio metadata. |
| `tools/validate_bundle.py` | Structural/semantic validator for files, schema, decisions, DAG, gates, authority, naming, and portfolio-boundary markers. |
| `tools/requirements.txt` | Pinned validator dependencies. |
| `BUNDLE_MANIFEST.sha256` | SHA-256 manifest generated after validation; excludes itself. |

## Implementation scaffold

`ThirdLife.sln` contains the standalone ThirdLife Setup Core production boundaries defined by the binding roadmap:

- `ThirdLife.Core`, `ThirdLife.Persistence`, `ThirdLife.Inventory`, `ThirdLife.Policy`, and `ThirdLife.Catalog`;
- `ThirdLife.Packages`, `ThirdLife.Actions`, `ThirdLife.Broker.Protocol`, and the ephemeral `ThirdLife.Broker` executable;
- `ThirdLife.Verification`, `ThirdLife.Reports`, `ThirdLife.Diagnostics`, and the unelevated `ThirdLife.UI` WPF application.

Production projects live under `src/`; their focused xUnit projects live under `tests/`. Only `ThirdLife.UI` enables WPF. Production-to-production references are added only when a selected implementation task introduces an approved contract, keeping the initial dependency graph acyclic and avoiding speculative coupling.

## Authority order

Apply files in this order:

1. `DECISIONS.md`
2. `ROADMAP.md`
3. `PROJECT_BOUNDARY.md`
4. `SECURITY.md`
5. `ACCESSIBILITY.md`
6. `LOW_SPEC.md`
7. `AGENTS.md`
8. `TASKS.yaml`
9. `CODEX_START_PROMPT.md`
10. `README.md`

`RELEASE_INTERFACE.md` records verified release facts. `FUTURE_ASSEMBLY_NOTES.md` is explicitly non-binding. A lower document cannot weaken a higher one.

## Quick start

Clone the repository on Windows and enter its root:

```powershell
git clone git@github.com:PikkuJanne/ThirdLife.git
Set-Location ThirdLife
```

Install the exact .NET SDK and Python versions named under **Repository verification** below. Create the ignored Python environment once and install the pinned validator dependency:

```powershell
py -3.14 -m venv .venv
.\.venv\Scripts\python.exe -m pip install --no-deps --require-hashes --only-binary=:all: --index-url https://pypi.org/simple --requirement tools\requirements.txt
```

Run the complete clean-checkout gate:

```powershell
.\eng\verify.ps1
```

For development, read the authority files in the required authority order and use the default selection rule in `AGENTS.md`. The validator prints the currently Codex-ready task IDs from live `TASKS.yaml` state. Work on exactly one selected task and publish its verified result before choosing another.

## Milestone chain

| Milestone | Increment | Gate |
|---|---|---|
| M0 | Foundation, governance, project boundary, and quality baselines | `TL-0010` |
| M1 | Audit-only job, evidence, inventory, human tests, and report | `TL-0116` |
| M2 | Explainable policy and disposition engine | `TL-0208` |
| M3 | Declarative planning, journal, and narrow privilege boundary | `TL-0314` |
| M4 | One-package write-capable vertical slice | `TL-0411` |
| M5 | Full machine profile, updates, restarts, and cold-boot verification | `TL-0511` |
| M6 | Finalization, reports, audits, package, and controlled v0.1 pilot | `TL-0611` |
| M7 | Recipient-controlled accessibility/basic backup, lifecycle hardening, interface sheet, and Core 1.0 stable release | `TL-0710` |

The chain is strict:

```text
M0 → M1 → M2 → M3 → M4 → M5 → M6 → M7
```

M4 proves the architecture. M6 authorizes only a controlled pilot. M7 is the Team B/B1 project-exit gate.

## Current work versus future suite assembly

### Allowed now in B1

- Standalone device assessment and explainable disposition.
- Generic curated free essentials and synthetic package fixtures.
- Package/update planning and execution through supported structured mechanisms.
- Per-action elevation through an ephemeral allowlisted broker.
- Local profiles, accessibility, basic operating-system backup onboarding, verification, finalization, handover, and sanitized support.
- Ordinary installer/uninstaller, documented data locations, open outputs, samples, and release interface facts.

### Deferred to B4

- PaperWorkShell or other sibling catalogue entries.
- Composite Job Seeker/Student/Family/Community suite profiles containing portfolio applications.
- Sibling-specific launch/open/file-association adapters.
- Compatibility cuts, adapter version ranges, offline suite media, and cross-product black-box tests.
- Any custom cross-app command or structured adapter.

B4 will consume exact frozen releases and public release information. It will not consume active branches, internal databases, or private classes.

## Task selection and state

Unless the user names a valid task, Codex follows `AGENTS.md`:

1. all direct and transitive prerequisites must be complete as represented by the graph;
2. executor must permit Codex work;
3. prefer `ready`, then dependency-ready `backlog`;
4. choose the lowest milestone, then priority, then task ID;
5. implement one task only.

Normal implementation sessions may update only:

```yaml
status: review
evidence:
  - summary: "Unit and integration tests passed"
    result: passed
    environment: "Windows 11, .NET 10"
    date: "YYYY-MM-DD"
    reference: "artifacts/tests/TL-XXXX.txt"
blocked_reason: "Only when status is blocked"
```

Do not weaken dependencies, decision references, executor, acceptance criteria, verification, or milestone assignment to obtain `done`.

| State | Meaning |
|---|---|
| `backlog` | Specified, not selected. |
| `ready` | Dependencies and required inputs are available. |
| `in_progress` | Active session owns the task. |
| `blocked` | A concrete blocker and unblock condition are recorded. |
| `review` | Implementation exists but verification or human evidence remains. |
| `done` | Complete task contract and evidence exist. |
| `cancelled` | Authorized human scope removal. |

## Human evidence

Codex cannot manufacture:

- physical-device or cold-boot results;
- workshop volunteer, recipient, or proxy observations;
- NVDA/Narrator/accessibility sign-off;
- security/privacy, licence, release, or portfolio-boundary approvals;
- partner pilot authorization; or
- Core 1.0 release authorization.

For `hybrid` tasks, Codex completes the automatable portion and leaves `review` or `blocked`. For `human` tasks, it may prepare an evidence index/checklist but never mark the gate complete.

## Repository verification

The verification toolchain is pinned to .NET SDK `10.0.400` by `global.json`, Python `3.14.7`, and the exact Python dependency in `tools/requirements.txt`. On a clean Windows checkout, create the ignored local environment once:

```powershell
py -3.14 -m venv .venv
.\.venv\Scripts\python.exe -m pip install --no-deps --require-hashes --only-binary=:all: --index-url https://pypi.org/simple --requirement tools\requirements.txt
```

Then run the complete repository gate from the repository root:

```powershell
.\eng\verify.ps1
```

The scripts resolve repository paths relative to themselves, so an absolute script path also works from another directory. Git Bash on Windows may run `./eng/verify.sh`. Non-Windows hosts fail clearly because the authoritative solution includes WPF and Windows-targeted projects.

The verifier runs governance-validator regression probes, then checks the governed bundle and machine-readable portfolio metadata, the bundle hash manifest, solution and WPF boundaries, exact SDK and central package policy, project-local NuGet lock files, the hardened Windows workflow, and a byte-for-byte deterministic development SBOM generated from checked-in inputs. It then checks formatting, the Release build with warnings as errors, and all Release tests. Restore uses only `NuGet.Config` and `--locked-mode`; it does not rewrite the dependency graph.

Generate an inspectable CycloneDX development SBOM without changing the repository:

```powershell
.\eng\generate-sbom.ps1 -OutputPath "$env:TEMP\ThirdLife.development.cdx.json"
```

The generator is offline and records the checked-in source/development dependency inventory. It does not claim to describe a future installer or release payload. Release mode remains blocked until the licence matrix has named human approval.

When an intentional package-version change is approved, update `Directory.Packages.props`, run `dotnet restore ThirdLife.sln --configfile NuGet.Config --force-evaluate`, inspect every lock-file change, and rerun the full verifier. Environment-specific checks must be reported truthfully. Do not disable security, accessibility, low-spec, provenance, analyzer, or failing-test gates.

## Stable Core 1.0 handoff

`TL-0710` requires exact release artifacts, hashes, source revision, dependency lock, SBOM/licence evidence, known limitations, samples, installer/update/repair/uninstall/data-preservation evidence, security/privacy/accessibility/low-spec/offline results, sanitized support evidence, and completed `RELEASE_INTERFACE.md`.

After human approval:

1. ThirdLife Setup Core enters bounded maintenance.
2. Team B starts **Scam Explainer**.
3. Future integration notes remain dormant until B4 is the active Team B project.
4. B4 may later adapt frozen products through install/launch/open/guidance and optional version-bounded adapters with manual fallback.

A truthful `review` or `blocked` state is valid. A false `done` is not.
