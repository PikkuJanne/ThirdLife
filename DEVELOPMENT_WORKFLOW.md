# ThirdLife Setup Core — Development and GitHub Continuity Workflow

**Bundle version:** 0.3.1  
**Continuity source of truth:** GitHub repository  
**Runtime hardware scope:** active Codex machine only

## 1. Purpose

The active Codex machine is replaceable; the pushed repository state is not. Every Codex session must be recoverable from GitHub without relying on chat history, local-only files, IDE state, or memory.

This workflow also enforces the portfolio’s single-machine validation baseline. Source continuity may move to another machine later through a clean clone, but no task depends on another machine being available at the same time, a hardware lab, a remote runtime runner, or an unpushed worktree.

## 2. Start-of-session sequence

Before proposing or changing implementation:

1. Read this file completely.
2. From the repository root, identify the intended remote and branch:

   ```powershell
   git remote -v
   git branch --show-current
   git status --short --branch
   git rev-parse HEAD
   git log -1 --decorate --oneline
   ```

3. Fetch without rewriting local work:

   ```powershell
   git fetch --all --prune
   ```

4. Compare local and upstream state:

   ```powershell
   git status --short --branch
   git rev-list --left-right --count HEAD...@{upstream}
   ```

   If no upstream is configured, record that fact and use the repository’s documented branch policy. Do not guess a remote branch.

5. Stop and report before implementation when:

   - the remote is unexpected;
   - the branch is not the intended task branch;
   - the worktree contains unexplained changes;
   - local and remote histories diverge;
   - the expected commit cannot be found; or
   - a rebase/reset/force push would be required without explicit authorization.

6. Read `STATUS.md`.
7. Read `DECISIONS.md`, `ROADMAP.md`, `PROJECT_BOUNDARY.md`, `SECURITY.md`, `ACCESSIBILITY.md`, `LOW_SPEC.md`, `TESTING.md`, `AGENTS.md`, and the selected `TASKS.yaml` entry.
8. Run the smallest relevant baseline command before editing and record pre-existing failures.

Do not start substantive work from an accidentally stale branch.

## 3. Branch policy

Use the repository’s existing documented branch strategy. When no project-specific exception exists, prefer a short-lived branch named after the task, for example:

```text
task/TL-0008-single-machine-validation
```

A branch must contain one coherent task outcome. Do not mix unrelated cleanup, future B4 integration, or sibling-project work into the task branch.

Direct work on the default branch is permitted only when the repository has explicitly adopted that solo-development model and the branch is protected by the same checkpoint, verification, and push rules.

## 4. Checkpoint commits

Create a coherent, buildable commit and push after every meaningful completed unit, and always before:

- pausing work;
- ending a session;
- switching machines or worktrees;
- starting a risky migration;
- running a destructive or broad mutating test;
- handing the task to another Codex session; or
- requesting human review.

A checkpoint commit should:

- reference the task ID;
- contain the intended source, tests, fixtures, schemas, migrations, documentation, and task/status updates together;
- exclude generated outputs, caches, local virtual environments, raw logs, personal data, and secrets;
- pass the quick tier; and
- state any known targeted/full/extended tests still pending.

Suggested commit form:

```text
TL-0008: adopt same-machine validation baseline
```

## 5. Push and handoff rule

A session or task handoff is not complete until:

- intended changes are committed;
- the commit is pushed;
- the remote branch contains the reported commit;
- `TASKS.yaml` and `STATUS.md` reflect the current state;
- test commands, tiers, durations, and results are recorded;
- tests not run and the reason are recorded;
- the worktree is clean except for explicitly documented local artifacts; and
- the next action is executable without the previous chat.

Verify before reporting completion:

```powershell
git status --short --branch
git rev-parse HEAD
git rev-parse @{upstream}
```

Do not report a commit as pushed when the two commit IDs do not match or the upstream check was not possible.

## 6. Clean-clone continuation

At milestone, pilot, preview, and stable-release gates—and when the task explicitly requires it—create a separate clean clone or clean worktree on the same physical Codex machine.

The clean environment must be able to:

1. restore required external assets from checked-in manifests and documented verified sources;
2. install/restore locked dependencies;
3. build/start the project;
4. run the quick tier; and
5. locate current task/status/testing instructions.

The clean clone proves repository continuity. It does not create a second-hardware claim.

Do not make continuation depend on:

- an untracked local database;
- a manually installed undocumented binary;
- IDE-specific state;
- a secret that is not described through a safe example/secret-management path;
- a file outside the repository with no restoration manifest; or
- a sibling repository.

## 7. Secrets, personal data, and large assets

Never commit:

- passwords, tokens, private keys, recovery keys, credentials, or signing secrets;
- donor, recipient, operator, or beneficiary personal data;
- serial numbers, asset tags, device names, SSIDs, IP addresses, or personal paths in fixtures/evidence;
- raw hardware or diagnostic logs containing identifying data;
- caches, generated reports from real jobs, or local test databases; or
- machine-specific virtual environments and IDE caches.

Provide safe examples such as `.env.example` only when needed. Required large assets use Git LFS or a versioned download manifest containing source, licence, hash, size, and restoration command.

## 8. Test execution and hardware scope

All tests run according to `TESTING.md` on the active Codex machine. This includes:

- host Windows checks;
- clean clones/worktrees;
- virtual machines, Windows Sandbox, or containers hosted on the active machine;
- resource constraints and synthetic fault scenarios; and
- human accessibility/operator/recipient walkthroughs.

Do not create a task dependency on:

- another physical computer;
- a lower-performance device;
- a volunteer hardware pool;
- a cloud CI runner or GitHub Actions runtime matrix; or
- missing peripheral/equipment classes.

GitHub stores source, history, issues, and release records. It is not the authoritative runtime test environment for this portfolio.

## 9. Divergence and recovery

When local and remote branches diverge:

1. stop implementation;
2. preserve the worktree and identify local/remote commits;
3. inspect the actual changes;
4. choose the documented safe merge/rebase strategy;
5. do not use `git reset --hard`, force push, or history rewriting merely to obtain a clean state; and
6. request explicit authorization when recovery would discard or rewrite someone else’s work.

When GitHub is temporarily unavailable, local commits may continue only when safe, but:

- do not declare a handoff complete;
- do not switch machines;
- do not claim the remote contains the work; and
- synchronize and verify as the first action after connectivity returns.

## 10. Current TL-0008 migration sequence

The repository is currently at the point where the former hardware-lab procedure would have been executed. That procedure is superseded.

Use the exact transition process in `TL-0008_TRANSITION.md` and the copy-ready prompt in `CODEX_TL0008_TRANSITION_PROMPT.md`.

The historical references are:

- procedure: `TL-0008 draft 1`;
- source commit: `4fa3ea050fd5e9985fde9cc8218281698d371cc8`;
- digest: `ef150dbf14b5db208582b7b526c7e0c6d0a5b912736e9e6519b8918abcf0928b`.

Do not reset the repository to that commit merely because it is named. Verify whether it is an ancestor/current reference. Preserve newer work. Archive the old procedure with a superseded banner; do not execute `MHT-001`–`MHT-021` as TL-0008 completion evidence.

## 11. End-of-session report

Every report includes:

```text
Task:
Branch:
Commit:
Push status:
Working tree:

User outcome:
Changed files:

Testing:
- Quick: command | duration | result
- Targeted: command | duration | result/not run + reason
- Full: command | duration | result/not run + trigger decision
- Extended: scenario(s) | duration | result/not run + trigger decision

Reference-machine / clean-environment details:
Defects and focused regressions:
Security/privacy impact:
Accessibility/modest-hardware impact:
Data/migration impact:
Project-boundary/release-interface impact:
Outstanding evidence or blocker:
Next action:
```

A narrative cannot substitute for a pushed commit, durable evidence, or truthful test state.
