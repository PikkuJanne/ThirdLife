# ThirdLife repository review — 12 September 2026

## 1. Executive judgment

The project is **substantially healthier than the default branch makes it appear**. The most
advanced branch contains a coherent governed architecture, completed M0 evidence, core domain
and persistence work, privacy-safe diagnostics, inventory-provider contracts, and a carefully
bounded first Windows inventory provider.

The correct action is **not** to restart ThirdLife or replace its roadmap. The immediate need
is to consolidate the real implementation lineage into the default branch, remove obsolete
pull-request ambiguity, correct a few governance/testing contradictions, and then continue
with `TL-0107`.

## 2. Repository state reviewed

| Item | State at review |
|---|---|
| Default branch | `main` at `1bb776f710bfa76f485bbee8d4b182b7a5284cd9` |
| Advanced lineage | `codex/tl-0106-system-inventory` at `89303ab15ed749754290b7c17e5f6cdb27719a00` |
| Relationship | Advanced branch was 42 commits ahead and 0 behind `main` |
| Main progress | Merged through `TL-0004` |
| Advanced progress | M0 complete; `TL-0101`–`TL-0106` complete |
| Next product task | `TL-0107` |
| Open pull requests | Draft PRs #4, #5, and #6 |
| Open issues | None found |

### Pull-request findings

- **PR #4** points to an older TL-0005 branch that diverged from the advanced lineage.
- **PR #5** points to an older TL-0006 branch even though the advanced lineage contains later
  dependency/licence/SBOM work.
- **PR #6** still describes the obsolete physical-device-pool requirement and therefore
  contradicts the current v2.1 single-machine baseline.
- No cumulative pull request currently represents the actual advanced lineage.

These open drafts are not harmless documentation clutter: they make it unclear which branch
is authoritative and create a realistic risk that an older line could be merged after newer
work.

## 3. Work worth preserving

### 3.1 Governance and product boundaries

The repository has strong, unusually explicit controls:

- standalone Team B/B1 scope;
- no sibling-repository dependency;
- frozen project-vacuum rules;
- local-first operation;
- unelevated UI and ephemeral broker design;
- evidence/policy separation;
- privacy classes for workshop, recipient, and support outputs;
- active-machine-only validation;
- staged Quick/Targeted/Full/Extended testing;
- explicit limits on hardware claims.

These are aligned with the portfolio and should remain intact.

### 3.2 Implemented M1 foundations

The advanced branch contains:

- typed core evidence and job models;
- SQLite persistence and migrations;
- sanitization-gate state and durable job lifecycle;
- structured, fail-closed diagnostic logging and redaction;
- closed inventory-provider contracts;
- cancellation, timeout, partial-evidence, and failure semantics;
- a bounded Windows system-inventory provider;
- deterministic synthetic fixtures, captured replay, and active-machine smoke evidence.

### 3.3 TL-0106 implementation quality

The first concrete Windows provider is a solid foundation:

- fixed local read APIs rather than a generic command/query surface;
- bounded raw-SMBIOS processing;
- explicit maximum sizes and cardinalities;
- field-local malformed-data handling;
- buffer clearing on disposal and abort paths;
- full serial retained only for the protected workshop observation;
- value-silent implicit rendering;
- no policy/readiness inference inside the evidence provider;
- no shell, WMI, registry, network, service, elevation, or mutation surface;
- adversarial tests for placeholders, malformed structures, conflicts, denial, cancellation,
  architecture emulation, and privacy leakage.

This code should be reviewed and integrated, not replaced merely because it is not yet on
`main`.

## 4. Required improvements

## P0 — complete before TL-0107

### P0.1 Consolidate the real lineage into `main`

Create one cumulative stabilization branch from the advanced lineage and one pull request to
`main`. Do not begin another product task on top of an unmerged 42-commit stack.

Acceptance:

- advanced commit is an ancestor of the stabilization branch;
- all intended TL-0005–TL-0106 work is present;
- no older PR branch contributes unique intended work that is missing;
- active-machine required checks pass;
- the cumulative PR is reviewed before merge;
- `main` becomes the continuation source after merge.

### P0.2 Retire obsolete and divergent pull requests

After ancestry/content verification and project-owner approval:

- close PR #4 as superseded by the cumulative integration;
- close PR #5 as superseded by the cumulative integration;
- close PR #6 as superseded and explicitly note that its hardware-pool gate must not be used;
- retire duplicate remote branches only after their intended commits are reachable from
  merged `main`;
- do not force-push or rewrite history.

### P0.3 Correct remote-CI terminology

The current workflow job calls itself **authoritative**, while the binding project decisions
say that runtime and release evidence is produced on the active Codex machine and remote CI
is optional/non-authoritative.

Update:

- workflow and job names;
- comments and documentation;
- new evidence language;
- TL-0002 historical evidence with a non-destructive clarification.

Do not erase the historical run. Reclassify it as a supplementary clean-checkout result.

### P0.4 Make the Quick tier a real code-feedback tier

The current `eng/verify.ps1 -Tier Quick` runs governance/validator work but skips formatting,
build, and .NET tests. That conflicts with `TESTING.md` and allows code regressions to survive
a “Quick passed” checkpoint.

The revised Quick tier must include:

- direct bundle and repository validation;
- formatting verification;
- warnings-as-errors Release build;
- fast Core and Inventory tests;
- no active-machine, destructive, broad Sandbox, or extended matrix;
- a default two-minute target with duration reporting.

Move the complete Python mutation/regression suite to Full or a named targeted governance
run. Add a Targeted command that accepts project and filter arguments.

## P1 — apply during stabilization

### P1.1 Compact `STATUS.md`

`STATUS.md` should be an operational handoff snapshot, not the permanent home of long design
essays. Archive the current long snapshot before replacing it.

Keep in live status:

- milestone and selected task;
- branch, commit, upstream, and working-tree state;
- latest test commands/durations;
- current result/blocker;
- next executable action;
- short claim limits.

Move detailed immutable evidence to `artifacts/evidence/<task>/` or the task’s durable review
record and retain compact references in `TASKS.yaml`.

### P1.2 Reduce `TASKS.yaml` evidence bloat

The machine graph should remain parseable and useful to automation. Do not continue embedding
multi-page narratives in each task entry.

For new work:

- keep evidence entries short and structured;
- point to committed evidence artifacts;
- retain command, tier, result, duration, environment, commit, digest, and limitation;
- keep architecture discussion in ADRs/reviews rather than task YAML.

Existing history may remain until a controlled compaction migration is designed; do not
rewrite completed evidence casually.

### P1.3 Replace one-off giant harness growth with reusable profiles

The current task-named Sandbox scripts are useful evidence but are very large and later tasks
reuse a script named for an earlier task.

Adopt:

- one reusable host runner;
- one reusable guest runner;
- declarative per-task/per-tier profiles;
- compatibility wrappers for existing evidence;
- scenario-level result files and hashes.

Do not add another thousand-line task-specific runner for each task.

### P1.4 Remove TL-0008 transition from the active start path

Keep the transition and old procedure for audit history, but normal Codex sessions are now at
M1/TL-0107. `CODEX_START_PROMPT.md`, `DEVELOPMENT_WORKFLOW.md`, and README navigation must no
longer imply that the current repository is still waiting at TL-0008.

### P1.5 Establish merge-before-next-task discipline

A completed task branch should be integrated into the default branch before the next roadmap
task begins. A temporary stack is permitted only for a named recovery or cumulative
stabilization operation and must have a recorded collapse plan.

This prevents a second 42-commit unpublished default-branch gap.

## P2 — schedule, do not block stabilization

### P2.1 Signed release identity

Commits are currently unsigned in the reviewed history. Do not rewrite them. Before preview
or stable release, use signed release tags and signed/verified release artifacts as already
required by the roadmap.

### P2.2 Evidence artifact schema

Add a small schema for `artifacts/evidence` records after the repository is integrated. It
should support command, tier, timestamps, duration, commit, environment, fixtures, hashes,
result, cleanup, and limitations without duplicating raw logs.

### P2.3 Runtime-budget trend

Start recording Quick/Targeted duration trends in a compact machine-readable file. Treat
growth as a regression signal rather than repeatedly expanding the default test command.

## 5. TL-0107 contract improvements

`TL-0107` is the correct next product task, but its contract needs two clarifications before
implementation.

### 5.1 One semantic owner per evidence fact

`TL-0106` already emits `system.architecture`. `TL-0107` must not silently emit a second value
under the same key. It may either consume that fact or emit a distinct
`os.installation_architecture` observation. `TL-0112` must then use an explicit,
deterministic conflict/freshness rule. First-wins and last-wins behavior are prohibited.

### 5.2 Version and date lifecycle knowledge

Operating-system support status changes over time. The provider must preserve:

- edition, product type, version/build, and architecture as observations;
- lifecycle source identity;
- lifecycle-data revision;
- “as of” date;
- supported/audit-only/unknown mapping;
- stale or unrecognized build behavior.

Stale, absent, contradictory, or unrecognized lifecycle data cannot produce a passing support
fact.

### 5.3 Activation privacy and scope

Activation remains read-only. The provider must not:

- install/change a key;
- invoke activation;
- bypass licensing;
- log partial product keys, product IDs, account identifiers, or raw licensing output;
- turn an activation state into a legal-licensing conclusion.

### 5.4 Pending reboot semantics

Each indicator needs source and confidence. Absence of one indicator does not prove that no
reboot is pending. Conflicting sources remain explicit rather than being collapsed to false.

## 6. Stabilization acceptance gate

Repository stabilization is complete only when all of the following are true:

- [ ] Stabilization branch descends from `89303ab15ed749754290b7c17e5f6cdb27719a00`
      or a verified newer descendant.
- [ ] The worktree was clean before applying the update.
- [ ] Old `STATUS.md` is archived.
- [ ] Governance amendment D-067–D-072 is recorded.
- [ ] `STATUS.md`, `DEVELOPMENT_WORKFLOW.md`, `TESTING.md`, and
      `CODEX_START_PROMPT.md` are current.
- [ ] Workflow terminology says supplementary/non-authoritative.
- [ ] Quick includes formatting, build, and fast code tests.
- [ ] Targeted verification is independently invokable.
- [ ] TL-0107/TL-0112 conflict and lifecycle-data clarifications are applied.
- [ ] Bundle/repository validators pass.
- [ ] Active-machine Quick passes within or explicitly reports against its target.
- [ ] Relevant Targeted tests pass.
- [ ] Full is run because this is a cumulative integration/protected merge.
- [ ] One cumulative pull request to `main` exists.
- [ ] Merge, stale-PR closure, and branch deletion remain pending owner approval.
- [ ] After merge, a fresh branch from updated `main` is used for `TL-0107`.
