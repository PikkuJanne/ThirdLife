# ThirdLife Setup Core

This repository develops ThirdLife Setup Core: a local-first, auditable Windows refurbishment workflow for volunteer and staff refurbishers. Development follows the frozen decisions, standalone Team B/B1 boundary, and dependency-ordered task graph checked into this repository.

**Current project:** Team B / B1 — ThirdLife Setup Core  
**Roadmap bundle:** 0.3.0 / ThirdLife Software Portfolio v2.1  
**Physical validation scope:** active Codex machine only  
**Pilot gate:** `TL-0611` — controlled v0.1 partner pilot  
**Standalone release gate:** `TL-0710` — ThirdLife Setup Core 1.0  
**Next Team B project after stable:** Scam Explainer  
**Future integration project:** Team B / B4 — ThirdLife Deployment and Suite Assembly

The governed roadmap contains **91 tasks**, **8 milestone gates**, and **66 frozen decisions**. Current implementation progress and the next dependency-ready work are recorded in `TASKS.yaml` and `STATUS.md`; do not infer them from this README.

## What changed in portfolio-aligned bundle 0.3.0

- Made GitHub the continuity record while keeping runtime test evidence local to the active Codex machine.
- Replaced active hardware-lab, second-PC, lower-performance-device, volunteer-pool, and authoritative remote-runner requirements with deterministic fixtures, sanitized samples, safe same-machine constraints, bounded host observation, and explicit limitations.
- Added quick, targeted, full, and extended test tiers with task-level triggers and focused deterministic regressions.
- Recast modest-hardware readiness as continuous bounded-resource engineering; one-machine evidence is not cross-hardware certification.
- Superseded `TL-0008 draft 1` and its immediate `MHT-001`–`MHT-021` device-pool walkthrough while preserving the historical commit and digest.
- Retained the manual functional-test feature as a product-workflow specification for later named implementation and review tasks.

See `CHANGELOG.md` for the file-level change record.

## Governance and repository contents

| File | Role |
|---|---|
| `ROADMAP.md` | Binding product scope, portfolio position, architecture, milestone sequence, gates, test/evidence model, and Team B transition. |
| `DECISIONS.md` | Highest-authority frozen project, safety, privacy, architecture, portfolio-boundary, and delivery choices. |
| `PROJECT_BOUNDARY.md` | Canonical **Owns / Does not own**, data boundary, project-vacuum rules, and future B4 late-integration contract. |
| `SECURITY.md` | Threat baseline, privilege/package/data/update controls, reporting placeholder, and release security obligations. |
| `ACCESSIBILITY.md` | Operator and recipient accessibility requirements, test evidence, and limitation policy. |
| `LOW_SPEC.md` | Modest-hardware engineering, sanitized reference profile, same-machine constraints, resource evidence, and claim boundaries. |
| `DEVELOPMENT_WORKFLOW.md` | Git synchronization, divergence safety, checkpoint, push, clean-clone, and handoff rules. |
| `TESTING.md` | Quick, targeted, full, and extended tiers; fixtures, environments, defect workflow, and evidence rules. |
| `TASKS.yaml` | Machine-readable DAG with 91 tasks, dependencies, deliverables, acceptance, verification, mutable history, and test-tier triggers. |
| `STATUS.md` | Factual current branch/commit/test/handoff state; it cannot weaken higher authority. |
| `AGENTS.md` | Codex operating contract: read order, task selection, allowed edits, project-vacuum rules, architecture, testing, and reporting. |
| [`docs/product-contract.md`](docs/product-contract.md) | Concise product identity, outcome, delivery cuts, Team B queue, standalone rules, and quality-baseline map. |
| [`docs/non-goals.md`](docs/non-goals.md) | Explicit existing-PC, bypass, optimizer, sibling-domain, shared-infrastructure, and early-B4 exclusions. |
| [`docs/glossary.md`](docs/glossary.md) | Governed meanings for evidence, requirements, blockers, dispositions, action state, and frozen-release integration terms. |
| [`docs/change-control.md`](docs/change-control.md) | Exact authority order, task-state limits, governed-amendment process, contradiction stop rule, and review checklist. |
| [`docs/security/threat-model.md`](docs/security/threat-model.md) | Assets, actors, threats, control/task mappings, residual-risk decisions, and named-owner approval state tracked by `TL-0004`. |
| [`docs/security/data-flow.md`](docs/security/data-flow.md) | Accessible diagram and textual inventory of processes, stores, flows, validation, recovery, and distinct trust boundaries. |
| [`docs/security/abuse-cases.md`](docs/security/abuse-cases.md) | Stable adversarial scenarios with detection, fail-closed behavior, recovery, task traceability, and residual risks. |
| [`docs/privacy/privacy-model.md`](docs/privacy/privacy-model.md) | Privacy classes, Core data map, audience separation, approved default retention guidance, exclusions, and exact-commit owner-approval record. |
| [`docs/privacy/logging-standard.md`](docs/privacy/logging-standard.md) | Typed logging envelope, prohibited fields, raw-input rules, exact support allowlist, preview-bound export, and verification contract. |
| [`docs/privacy/redaction-test-cases.yaml`](docs/privacy/redaction-test-cases.yaml) | Wholly synthetic exact redaction/omission/support projections and truthful privacy-review metadata. |
| [`docs/supply-chain/dependencies.md`](docs/supply-chain/dependencies.md) | Dependency classes, provenance and integrity rules, SBOM/audit procedure, release-evidence mapping, and human-review state. |
| [`docs/supply-chain/license-matrix.csv`](docs/supply-chain/license-matrix.csv) | Exact dependency owner, version, source, purpose, declared licence, proposed rights, integrity, provenance, and limitation records. |
| [`fixtures/README.md`](fixtures/README.md) | Non-personal pilot job, candidate policy, profile, and synthetic catalogue fixture contract. |
| [`docs/testing/reference-machine-profile.md`](docs/testing/reference-machine-profile.md) | Sanitized active-machine and toolchain facts for reproducible evidence. |
| [`docs/testing/capability-risk-matrix.md`](docs/testing/capability-risk-matrix.md) | Hardware/provider variants mapped to deterministic coverage or explicit limitations; not device inventory. |
| [`docs/testing/same-machine-constraints.md`](docs/testing/same-machine-constraints.md) | Safe, reversible, independently invokable constraint profiles and claim limits. |
| [`docs/testing/manual-hardware-tests.md`](docs/testing/manual-hardware-tests.md) | Detailed manual-test product-workflow specification; no TL-0008 physical walkthrough. |
| [`docs/testing/failure-injection.md`](docs/testing/failure-injection.md) | Individually addressable interruption scenarios with later task/gate triggers. |
| [`docs/testing/accessibility-matrix.md`](docs/testing/accessibility-matrix.md) | Automated and later human accessibility coverage on the active machine. |
| [`docs/history/TL-0008-draft-1-superseded.md`](docs/history/TL-0008-draft-1-superseded.md) | Full audit-preserved draft-1 procedure with a do-not-execute banner. |
| `CODEX_START_PROMPT.md` | First-session prompt and reusable execution, review, gate, security, accessibility, recovery, deferral, release-interface, and handoff prompts. |
| `TL-0008_TRANSITION.md` | Approved migration from the obsolete device-pool gate to same-machine validation. |
| `CODEX_TL0008_TRANSITION_PROMPT.md` | Copy-ready instructions for this one-time transition. |
| `RELEASE_INTERFACE.md` | Human-readable black-box release sheet, populated as a preview draft at `TL-0610`, completed at `TL-0706`, and frozen before `TL-0710`; not a shared API. |
| `FUTURE_ASSEMBLY_NOTES.md` | Non-binding B4 backlog for cross-project ideas; nothing here can block or expand B1. |
| `CHANGELOG.md` | Bundle version and migration history. |
| `TASKS.schema.json` | JSON Schema for the task graph, single-machine metadata, and test-tier fields. |
| `tools/validate_bundle.py` | Structural/semantic validator for files, schema, decisions, DAG, gates, authority, naming, and portfolio-boundary markers. |
| `tools/supply_chain.py` | Standard-library dependency-contract validator and deterministic CycloneDX 1.6 generator. |
| `tools/merge_task_contracts.py` | Reviewed merger that updates canonical contracts while preserving live task execution history. |
| `tools/requirements.txt` | Source-, binary-, version-, and hash-pinned validator dependencies. |
| `eng/generate-sbom.ps1` | Clean-checkout SBOM entry point; writes only the requested generated artifact. |
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
7. `DEVELOPMENT_WORKFLOW.md`
8. `TESTING.md`
9. `AGENTS.md`
10. `TASKS.yaml`
11. `STATUS.md`
12. `CODEX_START_PROMPT.md`
13. `README.md`

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
.\.venv\Scripts\python.exe -m pip --isolated install --no-deps --requirement tools\requirements.txt
```

Run the quick documentation/schema/static tier during governed documentation work:

```powershell
.\eng\verify.ps1 -Tier Quick
```

Run `.\eng\verify.ps1 -Tier Full` only when a task or gate triggers the full restore/build/test suite.

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
    tier: targeted
    result: passed
    environment: "Windows 11, .NET 10"
    date: "YYYY-MM-DD"
    duration: "00:02:34"
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

- an unperformed active-machine manual walkthrough or cold-boot result;
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
.\.venv\Scripts\python.exe -m pip --isolated install --no-deps --requirement tools\requirements.txt
```

For documentation/schema/static work, run the quick tier from the repository root:

```powershell
.\eng\verify.ps1 -Tier Quick
```

For a named full-tier trigger, run `.\eng\verify.ps1 -Tier Full` (the default when `-Tier` is omitted). The scripts resolve repository paths relative to themselves, so an absolute script path also works from another directory. Git Bash on Windows forwards the same arguments through `./eng/verify.sh`. Runtime evidence comes from the active Windows Codex machine; optional remote checks are non-authoritative.

The quick tier runs governance-validator regressions, bundle/schema checks, the manifest, repository boundaries, exact toolchain/package policy, and static continuity controls. The full tier then performs locked restore, formatting verification, a warnings-as-errors Release build, and all Release tests. Restore uses only `NuGet.Config` and `--locked-mode`; it does not rewrite the dependency graph.

Generate the dependency SBOM without a restore, network request, package cache, or external SBOM tool:

```powershell
.\eng\generate-sbom.ps1
```

The default output is the ignored `artifacts/sbom/thirdlife-setup-core.cdx.json`. Supply `-ProductVersion` and the lowercase `git rev-parse HEAD` value as `-SourceRevision` when preparing release evidence; the generator rejects any revision or governed input bytes that do not match that checkout. The generator validates the exact matrix against every lock file, the hash-pinned Python requirement, the pinned CI actions, the toolchain pins, and all current or future catalogue identities before it writes a canonical CycloneDX 1.6 document. See [`docs/supply-chain/dependencies.md`](docs/supply-chain/dependencies.md) for the separate licence/rights review and time-sensitive vulnerability-audit procedure.

When an intentional package-version change is approved, update `Directory.Packages.props`, run `dotnet restore ThirdLife.sln --configfile NuGet.Config --force-evaluate`, inspect every lock-file change, and rerun the triggered full verifier. Environment-specific checks must be reported truthfully. Do not disable security, accessibility, modest-hardware, provenance, analyzer, or failing-test gates.

## Stable Core 1.0 handoff

`TL-0710` requires exact release artifacts, hashes, source revision, dependency lock, SBOM/licence evidence, known limitations, samples, installer/update/repair/uninstall/data-preservation evidence, security/privacy/accessibility/offline and same-machine modest-hardware results, sanitized support evidence, and completed `RELEASE_INTERFACE.md`. Those results do not imply cross-hardware certification.

After human approval:

1. ThirdLife Setup Core enters bounded maintenance.
2. Team B starts **Scam Explainer**.
3. Future integration notes remain dormant until B4 is the active Team B project.
4. B4 may later adapt frozen products through install/launch/open/guidance and optional version-bounded adapters with manual fallback.

A truthful `review` or `blocked` state is valid. A false `done` is not.
