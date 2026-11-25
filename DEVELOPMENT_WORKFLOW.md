# ThirdLife Setup Core — Development and GitHub Continuity Workflow

**Bundle version:** 0.8.0  
**Continuity source of truth:** GitHub repository  
**Authoritative runtime hardware:** active Codex machine only  
**Remote CI classification:** supplementary

## 1. Start from known source

Verify source before editing.

```powershell
git remote -v
git branch --show-current
git status --short --branch
git rev-parse HEAD
git log -1 --decorate --oneline
git fetch --all --prune
git status --short --branch
git rev-list --left-right --count 'HEAD...@{upstream}'
git rev-list --left-right --count HEAD...origin/main
git worktree list
```

Record an absent upstream explicitly. Identify the intended repository, base and pull request through GitHub or existing records. Preserve dirty work, divergent histories and unique branch commits. If the checkout is unsafe or ambiguous, resolve source identity before editing it. Prefer a fresh isolated branch or worktree from a verified base. Do not reset, force-push or rewrite another line to resolve divergence. If GitHub is unavailable, commit safe work locally and explain the unverified remote state.

Read `STATUS.md`, the current product contract and affected specifications. Confirm the outcome and actual component dependencies in `TASKS.yaml`, then inspect relevant source and evidence and run the narrowest baseline.

## 2. Integrate coherent work

Keep one intelligible default-branch continuation and use scoped branches based on verified `main` or a documented dependency. An outcome can span multiple tasks and pull requests. When work depends on an unmerged branch, identify the exact base and head, dependent components, owning pull requests and integration order. One delayed branch leaves independent work free to proceed.

A checkpoint may integrate when its scope is verified, production behavior remains safe, and unavailable or unqualified paths are visibly unavailable and unreachable. Keep remaining qualification or consent in the task record. Real execution and release decisions stay bound to the affected capability or exposure. Never infer a capability or release claim from merge state.

Run unattended checks and prepare the exact candidates before requesting any attended decisions. Group pending operator actions in a planned validation window where practical. Record each approval and Windows choice against the action it governs. Continue independent work during the wait, and reuse earlier verification within the scope allowed by [TESTING.md](TESTING.md).

For each meaningful checkpoint:

1. Review the scoped diff, changed task state, affected security/privacy/accessibility/resource requirements, and applicable evidence. Exclude secrets, personal data, raw run output, caches and local setup files.
2. Run the applicable checks from `TESTING.md`. Record any gate or qualification still outstanding. Full applies when triggered by a major shared contract, migration, dependency, release or cumulative integration change.
3. Commit, push and open or update the applicable pull request. Respect enforced review, access and merge policies. Verify base/head and inspect the remote commit and merge result.
4. Preserve inactive branch history and evidence. Before closing a superseded pull request or deleting a branch, verify ancestry and unique intended content, record the disposition, and obtain owner approval for the retirement. Never delete the only copy of work.
5. Verify the worktree and upstream SHAs. Update the continuation with an exact next action and any true external blocker. Keep working toward the authorized outcome when a next step is available.

## 3. Evidence and handoff

`STATUS.md` is a short factual summary derived from `TASKS.yaml`. Record task, command/review, tier, result, date, duration, tested source/artifact, environment or constraint and concise limitation in the task. Use these locations for durable details.

```text
artifacts/evidence/<task>/
artifacts/gates/
docs/reviews/
docs/adr/
```

Keep automated results, actual observations, unavailable behavior, reported defects and decisions distinct. Reuse unchanged relevant evidence with its provenance. Codex owns preparation, technical verification, diagnosis, repair and cleanup. Actual authority for an action remains a prerequisite for that action. Prepare candidates before decisions where safe, with zero routine owner QA or technical sign-off.

A handoff names the observed outcome, exact tested source and artifact, branch/commit/push/merge state, test tiers, limitations and next executable action. When independent engineering is complete and execution remains unavailable, record precisely which action is blocked and how to reopen it.

## 4. Clean-clone continuity and runtime evidence

At named gates, applicable cumulative integration and shared-contract changes, use a separate clean clone or worktree on the active Codex machine. Restore verified assets and locked dependencies. Build/start the project as applicable, run Quick and required broader checks, and confirm the current work and evidence are discoverable without a sibling repository or local-only files.

Use the active Codex machine for authoritative implementation, Windows runtime, provider/hardware, accessibility, performance and release evidence. Its VMs, Windows Sandbox, containers, virtual disks and safe process constraints can support these checks. Cover unavailable hardware variants with deterministic fixtures, sanitized samples, bounded observation and explicit evidence limits. Do not make another physical PC, a hardware lab or cloud matrix a dependency.

GitHub Actions may run supplementary clean-checkout formatting, build, static and deterministic checks. Active-machine evidence remains required for runtime, provider, accessibility, resource, pilot, and release claims.
