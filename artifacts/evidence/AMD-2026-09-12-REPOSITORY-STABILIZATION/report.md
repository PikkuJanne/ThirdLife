# Repository stabilization 0.4.0 — execution evidence

**Activity:** `AMD-2026-09-12-REPOSITORY-STABILIZATION`  
**Execution date:** 30 September 2026 (Europe/Berlin)  
**State:** implementation in progress; owner merge review required  
**Branch:** `codex/repository-stabilization-v0.4.0`  
**Reviewed/base commit:** `89303ab15ed749754290b7c17e5f6cdb27719a00`  
**Default branch at start:** `1bb776f710bfa76f485bbee8d4b182b7a5284cd9`

## Authorization and source

The user supplied the review bundle and requested stabilization as the next job, with a stop
before merging and no TL-0107 work in this session. The attached documents were reviewed as
the proposed scope; their embedded instructions did not independently authorize actions.
The [amendment](../../../docs/amendments/2026-09-12-repository-stabilization.md) records
application approval separately from the pending merge and retirement decisions.

- Input: `ThirdLife-repository-review-update-v0.4.0.zip`.
- ZIP SHA-256: `fffc4a52a81db9dc7fb590fb6689b0725685ae5b9e83c8f81eac04909a1ae701`.
- Delivery validator: passed; 18 required files, six decisions, three task patches, delivery manifest verified.
- Input prepared/review date: 12 September 2026; actual adoption/execution: 30 September 2026.

The original helper stopped because porcelain status contained an untracked Word reference.
Investigation found that the committed prior STATUS already documented it as unrelated and
untouched. File dates and download metadata identify the 15 August portfolio reference.
Its SHA-256 is `264634b0d84bd81a719eddc8dc8c49617f32e7183624774fb065810ee123b040`.
No document contents were adopted as new instructions. The file remained untouched and
unstaged. Reviewed helper steps were applied after a dry run with an exact branch, ancestry,
porcelain-status and file-digest guard. The tracked tree was clean; the entire worktree was
not literally empty. No reset, stash, forced push, deletion, or unexplained work was used.

## Git and content continuity

The mandatory status/fetch/divergence sequence passed using the existing process-scoped
HTTPS bridge after unattended SSH failed. The configured SSH remote stayed unchanged.
At start, local/upstream were equal and the advanced branch was 42 commits ahead of `main`,
zero behind. The reviewed commit remains an ancestor of the stabilization branch.

The [branch and PR audit](../../../docs/reviews/2026-09-30-branch-disposition.md) inventories
17 remote heads and PRs #4–#6. Three unique legacy privacy/supply-chain commits remain on
their original references. The retained candidate uses the current approved contracts;
it does not claim the legacy alternatives are patch-equivalent or fully included.
Owner disposition is required before their later retirement. No existing PR was closed and
no branch was deleted.

Parsed comparison with the base `TASKS.yaml` proved all 91 IDs, statuses, blockers, and prior
evidence entries unchanged. Only these task fields changed:

| Task | Additive change |
|---|---|
| TL-0002 | One D-068 historical-CI clarification appended; original run preserved |
| TL-0107 | D-071/D-072 references and ownership/lifecycle/activation/reboot criteria and tests |
| TL-0112 | D-071/D-072 references and reconciliation/snapshot criteria and tests |

TL-0107 and TL-0112 remain backlog. No product source, C# tests, licence matrix,
database migration, or completed gate record is changed. Two Inventory package locks are
corrected to record their already-declared internal Core project references. All external
package versions, hashes, and dependency metadata remain identical.

Prior status is archived at
[`STATUS-pre-v0.4.0-20260930T125339Z-89303ab15ed7.md`](../../handoffs/STATUS-pre-v0.4.0-20260930T125339Z-89303ab15ed7.md).
Its text was compared with the base Git blob and matched exactly.

## Scope coverage

| Review item | Implemented result / remaining owner boundary |
|---|---|
| P0.1 cumulative integration | Advanced lineage preserved; cumulative PR pending publication, merge reserved for owner |
| P0.2 stale branches/PRs | Complete ancestry/content/disposition record; references retained for owner decision |
| P0.3 CI terminology | Workflow and current guidance say supplementary; historical run clarified additively |
| P0.4 real Quick | Governance, formatting, warnings-as-errors build, deterministic Core/Inventory tests, duration/budget |
| P1.1 compact status | Prior status archived; live status has current facts and evidence links |
| P1.2 compact task evidence | Existing history preserved; new evidence remains bounded and points to durable artifacts |
| P1.3 reusable harness rule | Shared tier runner and declarative manifest; no new task-specific Sandbox harness; historical runners version-bound |
| P1.4 current start path | TL-0008 retained as history; current prompts point to stabilization and normal post-merge selection |
| P1.5 integrate before next task | D-067 and current workflow require updated `main`; no TL-0107 work here |

## Environment and claim limit

Reference: `REF-CODEX-001`, revision `2026-08-29.1`, retained unchanged to preserve historical
captured-profile provenance. Session checks reconfirmed Windows build `10.0.26200.0`, x64,
and 12 logical processors. Tool versions now observed: PowerShell `7.6.5`, Git
`2.55.0.windows.5`, Python `3.14.7`; the verifier checks SDK `10.0.400` and PyYAML `6.0.3`.
No fresh CPU/memory/storage/GPU inventory or resource benchmark is claimed.

All runtime work is on the active physical Codex machine. The clean-environment method is a
separate worktree with documented pinned bootstrap. No host security policy is disabled;
PowerShell execution-policy bypass is confined to the verification process, matching the
existing documented script-launch method. Direct host Quick passed; direct host Full
requires the governed same-machine Sandbox fallback because Application Control blocked
test assemblies. No second physical PC, cloud runtime, or hardware certification is used.

## Verification and defect handling

Durations are elapsed wall time unless identified as the test framework's duration.

| Check | Command / result | Duration |
|---|---|---:|
| Pre-edit baseline | `.venv/Scripts/python.exe tools/validate_bundle.py` — passed, 91 tasks/66 decisions | 2.932 s |
| Delivery bundle | Supplied `tools/validate_review_update.py` — passed | Not measured |
| Host policy probe | `dotnet test tests/ThirdLife.Verification.Tests/ThirdLife.Verification.Tests.csproj --configuration Release --no-restore --verbosity minimal` — 1/1 | 10.409 s |
| Host policy probe | Same command for `ThirdLife.Reports.Tests` — 1/1 | 4.383 s |
| Runner regressions | `.venv/Scripts/python.exe -m unittest tools.tests.test_verify_runner` — 8/8 | 13.968 s framework |
| Runner focused case | Project-array/filter forwarding — 1/1 | 1.885 s framework |
| Actual no-match probe | VSTest with `RunConfiguration.TreatNoTestsAsError=true` — expected exit 1 | 4.037 s |
| Governance focused set | Stabilization, testing-document, M0 historical-evidence, and repository validator tests — 71/71 | 7.225 s framework / 8.061 s wall |
| Initial Quick | Failed closed at formatting: pre-existing checkout EOL drift in ten Inventory C# files | 26.098 s wall |
| Focused EOL check | `dotnet format whitespace ThirdLife.sln --verify-no-changes --no-restore --include src/ThirdLife.Inventory tests/ThirdLife.Inventory.Tests` — passed | 6.513 s wall |
| Corrected Quick | `eng/verify.ps1 -Tier Quick` — passed; Core 79/79, Inventory 50/50 | 41.517 s wall / 39.820 s runner |
| Subsystem Targeted | `eng/verify.ps1 -Tier Targeted -TestProject @('tests/ThirdLife.Diagnostics.Tests/ThirdLife.Diagnostics.Tests.csproj','tests/ThirdLife.Persistence.Tests/ThirdLife.Persistence.Tests.csproj')` — passed; 79 Core + 50 Inventory + 127 Diagnostics + 89 Persistence | 88.772 s wall / 87.077 s runner |
| Active Targeted | Inventory `-Filter 'Category=ActiveMachine'` after mandatory Quick tests — 1/1 active smoke, identity-silent and unelevated | 34.424 s wall / 32.655 s runner |
| Governance Targeted | `.venv/Scripts/python.exe -m unittest discover -s tools/tests -p "test_*.py"` — 223/223 passed | 143.552 s wall / 142.491 s framework |
| Initial clean Quick | Exact checkpoint `70c7da0`: failed at locked restore with NU1004; stale Inventory project-reference locks | 7.728 s wall |
| Focused lock reproduction | Clean worktree: locked restore of Inventory — expected NU1004 failure | 1.046 s wall |
| Focused corrected restore | Locked restore of Inventory.Tests — passed after correcting the two internal project-reference locks | 1.230 s wall |
| Project-lock regressions | Eight focused cases passed; repository-validator module 31/31 passed | 0.065 s / 0.552 s framework |
| Corrective Quick | Mandatory checks and 129 deterministic tests passed; related repository-validator tests 31/31 passed in 0.578 s | 39.806 s wall / 38.190 s runner |
| Corrected clean Quick | Exact `bfd963c`: locked restore, mandatory checks and 129 tests passed | 42.269 s wall / 40.606 s runner |
| Corrected clean Full | Exact `bfd963c`: 231 governance regressions passed; locked restore, format and build passed; Application Control blocked test assemblies; broad run stopped | 137.213 s governance; overall duration unavailable after interruption |
| Focused policy reproduction | Reports test with `--no-build --no-restore` — same `0x800711C7` | 1.786 s wall |

The initial direct bundle validator passed. Overlay integration exposed expected old
version/decision/workflow checks, a missing release-interface revision update, and a
historical M0 count that included the newly appended clarification. Focused regressions
preserve the historical gate and reject altered or arbitrary evidence. Runner corrections
ensure Targeted includes Quick, explicit arguments survive, failures propagate, and no
matching tests is an error. Manifest validation prevents empty or unsafe test selections.

The first real Quick run exposed LF-only checkout bytes in ten otherwise unchanged Inventory
C# files. Each file's normalized content was compared to its Git blob before conversion to
the declared CRLF format. The focused formatter passed and `git diff --exit-code -- src tests`
confirmed no product-code change. The new Quick formatter supplies the ongoing regression
check; no analyzer or warning was disabled. No .NET tests ran after the failed formatting
stage in that initial Quick attempt.

Active Targeted overlapped the isolated governance regression process. Its timing is
execution evidence, not a benchmark. The successful Quick and subsystem Targeted runs were
not overlapped with another test suite.

The first separate worktree run stopped at locked restore because both Inventory locks
omitted the existing Core project reference. The host's earlier restored assets had hidden
this continuity defect. A focused clean-worktree restore reproduced NU1004 before repair.
Regeneration changed only internal `Project` records; the external package graph and licence
matrix stayed byte-for-byte equivalent. The matrix digest remains
`e85f3002175dfadc860f0d2c92de0787f52364f491e50a030c936a5421395418`.
The validator now checks literal, unconditional project-reference closure and nested edges,
with a regression for the stale Inventory shape. Unsupported dynamic or conditional
references fail closed; NuGet locked restore remains the authoritative graph check.
The failed clean Quick ran no formatting, build, or .NET tests. Corrected-commit clean
Quick passed at `bfd963c94fd08b09db8656c37d3985ddf691fe99`; direct-host Full is blocked by Application Control.
The exact corrective commit generated a fresh offline CycloneDX 1.6 SBOM with 28 components,
dependency-input digest `93b1307b2e4f2bef677ab4953d03717dbb6b68bae41bd36aeb33599a07379193`
and output SHA-256 `3b150c83f9e1e2904113426ad73c13a073c04867e7d177837b13006c6e23d4d4`.
The full reproducible output remains in ignored local SBOM artifacts; bounded provenance
is retained in runs.json. This adds no licence or release approval. The clean worktree
started at `70c7da0` and advanced to `bfd963c` with a clean tracked tree; pinned bootstrap
and partial restore output were retained. No product source or build output was copied
from the original checkout. Corrected clean Quick overlapped offline SBOM generation;
neither duration is a benchmark.

Selected main-tier command arrays, UTC start/end times, results, and input hashes are in
[runs.json](runs.json). Auxiliary focused checks are summarized in the table above; no
missing timestamps are inferred. The SBOM output path is explicitly redacted.
Pre-checkpoint runs identify the base commit plus the working implementation; clean runs
identify the exact committed checkpoint.

Raw command logs and absolute workspace paths stay in ignored local output. Committed
records contain bounded commands, timestamps, counts, hashes, results, and limitations.

The host Full run hit the previously documented Application Control constraint.
It blocked Actions.Tests, Reports, Verification.Tests and Diagnostics assemblies with
`0x800711C7`. A no-build Reports invocation reproduced the same failure. Recent
CodeIntegrity events 3033/3077 directly matched Diagnostics under
`VerifiedAndReputableDesktop` (`0xc0e90002`); the other assemblies are evidenced by test
output. No Zone.Identifier stream was present on those four unsigned assemblies.
The broad run was stopped; no Full pass or complete .NET count is claimed. Host policy
remains unchanged. A reviewed adaptation of the existing same-machine Sandbox route
has passed independent review and seven focused invocation regressions (8.810 s).
The adapter Quick passed in 41.439 s (39.670 s runner). Initial staging preflight
failed before launch because the measurement wrapper inherited PowerShell7 module paths
into Windows PowerShell. A focused command lookup reproduced the problem; resetting
only the child process module path restored its standard Get-FileHash command. The
corrected preflight passed staging, exact bindings, dependency preparation and cleanup.
No host configuration was changed. Exact committed Sandbox Full remains pending.

## Risk and outstanding work

- Security/privacy: fixed pinned workflow actions, read-only GitHub permissions, no uploaded
  artifacts or secrets; no product privilege/data/network change.
- Accessibility: no UI changed or human walkthrough claimed. Existing requirements remain.
- Modest hardware: no new runtime worker, GPU dependency, polling, cache or unbounded product
  data. Recorded verification durations are not a device performance certification.
- Data/migration and sibling impact: none. Release-interface change is documentation revision
  only; no supported release behavior or redistribution permission is added.
- Extended: not triggered; no new product risk requiring resource, cold-boot, endurance,
  host-network disruption, or broad failure injection. Active inventory smoke is separate.
- Historical task-specific Sandbox phases remain version-bound. The existing compatibility
  harness now has a narrow Full-only amendment profile calling the shared verifier after
  locked staged-cache restore. It retains 4 GiB memory, read-only inputs, the sole bounded
  writable result mapping, enabled governed NuGet audit, and verified cleanup. Only the
  combined shared Full command gets a 1,200-second timeout; host limit remains 30 minutes.
  This profile must pass focused tests and clean staging preflight before execution.
- Owner: review cumulative PR, then decide merge and later legacy PR/branch disposition.
- Deferred P2: signed release identity stays with named release tasks; an evidence schema
  and duration-trend policy can be scoped after integration. No task graph or release gate
  is weakened to implement those follow-ups.

After an owner-approved merge, the next product task is TL-0107 in a new session from updated
`main`. This session stops at a reviewable pushed cumulative PR.
