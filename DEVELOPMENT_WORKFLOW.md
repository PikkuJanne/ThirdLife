# ThirdLife Setup Core — Development and GitHub Continuity Workflow

**Bundle version:** 0.4.0  
**Continuity source of truth:** GitHub repository  
**Authoritative runtime hardware:** active Codex machine only  
**Remote CI classification:** supplementary

## 1. Purpose

The pushed repository must be sufficient for another Codex session or another machine to
continue safely without chat history, IDE state, local-only files, or human memory.

Continuity requires more than pushing a task branch. Completed task work must return to one
unambiguous default-branch line before the next roadmap task begins.

## 2. Start every session safely

Before proposing or changing implementation:

```powershell
git remote -v
git branch --show-current
git status --short --branch
git rev-parse HEAD
git log -1 --decorate --oneline
git fetch --all --prune
git status --short --branch
git rev-list --left-right --count HEAD...@{upstream}
```

If no upstream exists, record that fact and identify the intended remote branch from the
documented branch/PR state. Do not guess.

Stop before implementation when:

- the remote is unexpected;
- the task branch is not the intended branch;
- the worktree contains unexplained changes;
- local and remote histories diverge;
- the expected completed-task/default-branch integration cannot be identified;
- preserving work would require reset, force push, or history rewriting.

Then read:

1. `STATUS.md`;
2. `DECISIONS.md`;
3. `ROADMAP.md`;
4. `PROJECT_BOUNDARY.md`;
5. `SECURITY.md`;
6. `ACCESSIBILITY.md`;
7. `LOW_SPEC.md`;
8. this file;
9. `TESTING.md`;
10. `AGENTS.md`;
11. the selected `TASKS.yaml` entry and its durable evidence.

Run the narrowest relevant baseline before editing.

## 3. Branch lifecycle

### 3.1 Normal rule

Use one short-lived branch per roadmap task:

```text
codex/tl-0107-os-lifecycle-activation
```

The branch contains one coherent task outcome. It must not contain sibling work, future B4
integration, unrelated cleanup, or the next task.

### 3.2 Merge before the next task

A product task is not ready for handoff to the next product task until:

- intended changes and evidence are committed and pushed;
- a pull request or documented direct-integration path exists;
- required review/gate evidence is complete;
- the task is integrated into `main`;
- `main` contains the reported commit or merge commit;
- stale task branches are classified;
- live `STATUS.md` points to updated `main`.

Do not begin the next roadmap task on top of an unmerged completed-task stack.

### 3.3 Temporary stacked-work exception

A stack is allowed only for a named recovery, cumulative stabilization, or approved
dependency sequence. Record:

- why integration cannot yet occur;
- exact base and head;
- included task IDs;
- owning pull request;
- collapse/merge plan;
- stop condition;
- branch-retirement plan.

The 0.4.0 cumulative stabilization is such an exception. It ends when the advanced TL-0106
line is integrated into `main`.

## 4. Checkpoint commits

Commit and push coherent buildable checkpoints:

- after each meaningful unit;
- before pausing or ending a session;
- before changing machines/worktrees;
- before a risky migration or mutating test;
- before human review;
- before updating a cumulative pull request.

A checkpoint:

- references the task or amendment;
- includes related source, tests, fixtures, schemas, migrations, docs, and state together;
- passes Quick;
- excludes secrets, personal data, caches, generated raw logs, and local environments;
- records broader verification still pending.

## 5. Pull-request and branch discipline

Before opening a pull request:

- fetch/prune;
- verify base/head;
- inspect commit range and changed files;
- verify the branch contains no unrelated task;
- update status/evidence;
- run required active-machine tiers.

Before closing a superseded PR or deleting a branch:

1. verify its intended commits are reachable from the retained line or intentionally rejected;
2. record any unique commits/content;
3. obtain project-owner approval;
4. add a clear supersession/retirement note;
5. never delete the only copy of intended work.

Do not merge an older divergent PR merely to make it disappear.

## 6. Push and handoff

A session handoff is complete only when:

- changes are committed and pushed;
- local/upstream SHAs match or the exact difference is documented;
- `TASKS.yaml` and concise `STATUS.md` are current;
- durable evidence artifacts are committed;
- tests run/not run and rationale are recorded;
- the worktree is clean except for explicitly documented local artifacts;
- the next action is executable without the prior chat.

Verify:

```powershell
git status --short --branch
git rev-parse HEAD
git rev-parse @{upstream}
```

## 7. Evidence storage

`STATUS.md` is a current snapshot, not an archive.

New detailed evidence belongs in:

```text
artifacts/evidence/TL-XXXX/
artifacts/gates/
docs/reviews/
docs/adr/
```

A task evidence entry remains compact and contains:

- task;
- command/review;
- tier;
- result;
- duration;
- environment/constraint;
- commit/artifact/digest;
- concise limitation.

Do not paste raw logs, secrets, personal data, or multi-page design analysis into
`TASKS.yaml`.

## 8. Clean-clone continuation

At milestone, preview, pilot, stable, cumulative-integration, and named task gates, use a
separate clean clone/worktree or allowed hosted environment on the active physical machine.

The clean environment must:

1. restore verified external assets from checked-in manifests;
2. restore locked dependencies;
3. build/start the project;
4. run Quick;
5. locate the current task and evidence;
6. contain no dependency on sibling repositories or undocumented local files.

This proves source continuity, not multi-hardware support.

## 9. Physical and runtime test scope

All authoritative implementation and validation occur on the active Codex machine, including
host checks, clean clones/worktrees, Windows Sandbox, VMs, containers, virtual disks,
constraints, and human walkthroughs.

Do not create dependencies on:

- another physical PC;
- a lower-performance device;
- a volunteer lab;
- missing peripheral classes;
- GitHub-hosted runtime behavior;
- a cloud hardware matrix.

Use deterministic fixtures, sanitized captured samples, safe same-machine constraints,
bounded observation, and explicit limitations.

## 10. Remote CI

GitHub Actions is permitted only as a supplementary clean-checkout signal. It may run format,
build, static, and deterministic tests, but it cannot satisfy:

- active-machine runtime evidence;
- hardware/provider observation;
- accessibility walkthroughs;
- performance/resource evidence;
- milestone/pilot/stable gates;
- release authorization.

Workflow and evidence text must not call remote output authoritative.

## 11. Divergence and recovery

When histories diverge:

1. stop implementation;
2. preserve worktree and identify commits;
3. inspect actual changes and ownership;
4. choose the documented merge/rebase strategy;
5. do not use `reset --hard`, force push, or history rewriting for convenience;
6. request owner authorization before discarding or rewriting work.

If GitHub is unavailable, local commits may continue when safe, but no handoff, merge,
machine switch, or claim of remote publication is complete.

## 12. Historical TL-0008 transition

The TL-0008 single-machine transition is complete. The transition prompt and superseded
physical-device procedure remain historical audit records only.

Normal task selection comes from live `STATUS.md` and `TASKS.yaml`. Do not rerun the old
device-pool walkthrough.

## 13. End-of-session report

```text
Task/amendment:
Branch:
Base:
Commit:
Push/upstream:
Working tree:
Default-branch integration state:
Pull request:

User outcome:
Changed files:
Durable evidence:

Testing:
- Quick:
- Targeted:
- Full:
- Extended:
- Supplementary CI:

Security/privacy:
Accessibility/modest hardware:
Data/migration:
Project boundary/release interface:
Known limitations:
Owner decision required:
Next action:
```
