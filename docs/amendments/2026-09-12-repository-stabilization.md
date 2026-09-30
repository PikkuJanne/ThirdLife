# Approved amendment — repository stabilization and continuation discipline

**Amendment ID:** `AMD-2026-09-12-REPOSITORY-STABILIZATION`  
**Bundle target:** 0.4.0  
**Status:** Approved for application; cumulative merge pending owner review  
**Prepared:** 12 September 2026  
**Approved:** 30 September 2026  
**Approver:** Janne Vuorela, project owner, through the current repository session  
**Scope:** Repository integration, evidence ergonomics, test execution, and TL-0107 contract clarification  
**No product-scope expansion:** Yes

## Authorization condition

The project owner supplied `ThirdLife-repository-review-update-v0.4.0.zip` and directed:
“Codex should perform this stabilization as its next job and stop before merging. It should
not begin `TL-0107` in the same session.” This authorizes the named stabilization and its
synchronized amendment. The attached prompt supplies the implementation scope; it is not an
independent source of approval. Approval covers application of the amendment, not exact-byte
approval of the resulting implementation or tests.

Merging the cumulative pull request, closing older pull requests, deleting branches, and
rewriting history remain outside this session's authorization.

## Evidence prompting the amendment

At review time:

- `main` was at `1bb776f710bfa76f485bbee8d4b182b7a5284cd9`;
- the advanced line `codex/tl-0106-system-inventory` was at
  `89303ab15ed749754290b7c17e5f6cdb27719a00`;
- the advanced line was 42 commits ahead and zero behind `main`;
- draft PRs #4–#6 did not represent the current implementation line;
- PR #6 still described the superseded physical-device-pool gate;
- remote CI was named authoritative despite the active-machine-only decision;
- the Quick entry point omitted formatting, build, and .NET tests;
- `STATUS.md` and task evidence had grown beyond concise handoff use;
- TL-0107 and TL-0106 both referred to architecture evidence without a frozen ownership rule.

## Adopted decisions when approved

This amendment adds:

- D-067 — completed task work is integrated before the next roadmap task;
- D-068 — remote CI is supplementary;
- D-069 — compact live status and referenced durable evidence;
- D-070 — reusable runners and declarative test profiles;
- D-071 — one semantic owner or explicit reconciliation per evidence fact;
- D-072 — versioned, dated, attributable, stale-safe lifecycle knowledge.

The adopted decision text is in [`DECISIONS.md`](../../DECISIONS.md#d-067--completed-task-work-is-integrated-before-the-next-roadmap-task).
The supplied ZIP retains the proposed patch text as historical input.

## Files affected

Full replacements:

- `STATUS.md`
- `DEVELOPMENT_WORKFLOW.md`
- `TESTING.md`
- `CODEX_START_PROMPT.md`
- `.github/workflows/verify.yml`
- `eng/verify.ps1`
- `eng/test-manifest.json`

Semantic amendments:

- `DECISIONS.md`
- `AGENTS.md`
- `README.md`
- `CHANGELOG.md`
- `TASKS.yaml`
- governance bundle manifest
- dated repository review under `docs/reviews/`
- existing governance validators and focused regressions that encode bundle version,
  decision IDs, workflow shape, and current-versus-historical prompt requirements
- README and change-control verification examples and durable stabilization evidence
- two Inventory package-lock records and a focused project-reference validator regression,
  after clean locked restore reproduced NU1004 for already-declared Core references;
  external package metadata and the approved licence matrix remain unchanged

The supplied helper required a completely empty porcelain status. The repository's prior
handoff already documents one unrelated untracked portfolio Word reference. Its origin and
digest were checked before editing; it remains untouched and unstaged. The reviewed helper
steps were applied with an explicit guard accepting only that exact known file and digest.
All tracked files were clean before application. No unexplained work was discarded or hidden.

The delivered overlays require integration corrections: validators must recognize 0.4.0
and D-067–D-072, Targeted must actually include Quick, and current docs must describe the
implemented command selection. These corrections preserve existing safety checks and task
execution history; they do not authorize TL-0107 implementation.

## Task-graph effect

No completed task becomes incomplete and no backlog task becomes done.

- TL-0002 receives a non-destructive evidence clarification: its historical GitHub run remains
  recorded but is supplementary rather than authoritative.
- TL-0107 receives D-071/D-072 references and criteria for distinct architecture evidence,
  versioned lifecycle data, activation privacy, and pending-reboot uncertainty.
- TL-0112 receives a deterministic overlapping-evidence reconciliation requirement.
- TL-0107 remains the next product task only after cumulative integration into `main`.

## Rollout

1. Verify the advanced lineage and create the stabilization branch.
2. Archive the old status snapshot.
3. Apply overlays and patches.
4. Run bundle/repository validation.
5. Run active-machine Quick, relevant Targeted, and cumulative-integration Full.
6. Commit and push.
7. Open one cumulative pull request to `main`.
8. Obtain owner review.
9. Merge without rewriting history.
10. Close superseded PRs and remove stale branches only after reachability checks.
11. Begin TL-0107 from the updated default branch.

## Rollback

Before merge, discard only the stabilization branch if the project owner rejects the update.
Do not alter the advanced implementation branch.

After merge, rollback by a normal revert pull request. Preserve the archived prior
`STATUS.md`, amendment record, and pull-request history. Do not force-reset `main`.

## Security, privacy, accessibility, and resource impact

The amendment changes no product data schema, privilege boundary, package behavior, device
setting, or user workflow. It strengthens provenance, evidence ownership, runtime-test
authority, and feedback speed. Existing single-machine, privacy, accessibility, and
modest-hardware claim limits remain binding.
