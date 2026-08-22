# AGENTS.md — ThirdLife Setup Core Codex Operating Contract

**Bundle version:** 0.3.1  
**Project:** ThirdLife Setup Core — Team B / B1  
**Physical validation scope:** active Codex machine only

## 1. Mission and scope

This contract governs every Codex session in the ThirdLife Setup Core repository. It applies from the repository root downward unless a nested `AGENTS.md` adds stricter local rules. A nested file may not weaken this contract, a frozen decision, the binding roadmap, the project boundary, the test strategy, or the GitHub continuity rules.

ThirdLife Setup Core is safety-sensitive Windows refurbishment software. It must remain independently useful without a sibling portfolio application, mandatory account, project-controlled server, or permanent privileged service. Prefer small, typed, testable, explainable, cancellable, and recoverable work over broad automation.

The v0.1 controlled pilot at `TL-0611` is not the project exit. Team B/B1 completes only at `TL-0710`; Team B then proceeds to Scam Explainer. Sibling catalogue entries, profiles, adapters, compatibility cuts, and suite media remain deferred to Team B/B4.

## 2. Mandatory session start

### 2.1 Read and synchronize before implementation

1. Read `DEVELOPMENT_WORKFLOW.md`.
2. Run its Git remote/branch/status/fetch/divergence sequence.
3. Stop before editing when the remote, branch, worktree, upstream, or history is unsafe or ambiguous. Do not force-reset, force-push, or discard unexplained work.
4. Read `STATUS.md`.
5. Read, in authority order:
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
6. Read the selected task’s code, tests, ADRs, schemas, fixtures, prior evidence, and notes.
7. Run the narrowest relevant baseline test and record pre-existing failures.

`STATUS.md` is a factual handoff snapshot and cannot weaken a higher-authority document. `CODEX_START_PROMPT.md` and `CODEX_TL0008_TRANSITION_PROMPT.md` are operating prompts. `RELEASE_INTERFACE.md` is populated only from verified release behavior. `FUTURE_ASSEMBLY_NOTES.md` is non-binding deferred context.

When a material contradiction exists, stop before the conflicting implementation and report the exact files, decisions, tasks, and safe options.

## 3. Project-vacuum and late-integration rules

During B1, work only in this repository.

Codex must not:

- browse, clone, inspect, modify, import, or depend on a sibling portfolio repository unless the user explicitly assigns a future B4 task;
- depend on sibling source, active branches, development binaries, services, databases, schemas, background processes, private APIs, fixtures, or release schedules;
- create sibling-specific catalogue entries, profiles, commands, file associations, adapters, or acceptance tests;
- create a shared SDK, universal job/findings/handoff schema, plugin framework, portfolio background service, monorepo mandate, or shared content store;
- add a cross-project `depends_on` edge or delay Core for another team’s unfinished work; or
- expose an interface solely because a future sibling might use it.

Use generic public free essentials and synthetic packages during B1. Record a useful cross-project idea in `FUTURE_ASSEMBLY_NOTES.md` and continue the selected task. The note creates no code, dependency, acceptance criterion, or release blocker.

B4 may later consume only exact frozen releases, hashes, public documentation, `RELEASE_INTERFACE.md`, known limitations, and non-sensitive samples. B4 owns all sibling-specific adapters and compatibility work. An adapter remains optional, version-bounded, disableable, and backed by a manual fallback.

## 4. Single-machine validation rule

The active Codex machine is the only physical machine used for:

- implementation;
- unit, integration, end-to-end, accessibility, security, resource, and release tests;
- benchmarks;
- clean clones/worktrees;
- VMs, Windows Sandbox, containers, and virtual disks;
- human operator/recipient/proxy walkthroughs; and
- cold-boot evidence at explicit later tasks.

Do not require or seek:

- a lab machine;
- a lower-performance computer;
- a second physical PC;
- a volunteer/partner device pool;
- a cloud or GitHub Actions runtime matrix; or
- missing storage, battery, manufacturer, peripheral, or network classes.

Missing hardware variants are covered through deterministic fixtures, sanitized captured provider samples, safe same-machine constraints, bounded observation of capabilities actually present on the active machine, or explicit unverified limitations. None of these methods is cross-hardware certification.

The former `TL-0008 draft 1` device-pool and `MHT-001`–`MHT-021` walkthrough is superseded by `D-064`. Preserve it only as a clearly marked historical record. Do not execute it as current evidence.

## 5. Task selection

Unless the user names a task:

1. parse `TASKS.yaml`;
2. exclude `done` and `cancelled` tasks;
3. require every dependency to be `done`;
4. require `executor` to be `codex` or `hybrid`;
5. require the task to be verifiable on the active machine or through deterministic fixtures;
6. prefer `ready`, otherwise an eligible `backlog` task;
7. choose the lowest milestone, then P0 before lower priorities, then the lowest numeric task ID; and
8. work on exactly one task.

When the user names a task, verify the dependency chain first. Do not implement around an unfinished dependency or silently select another task.

### Executors

- `codex`: Codex may mark `done` only when the complete task contract is evidenced.
- `hybrid`: Codex implements the automatable part and leaves `review` or `blocked` until every `human_evidence_required` item exists.
- `human`: Codex may prepare evidence/checklists but never marks the task `done`.

Never fabricate a Windows run, host capability, cold boot, operator/recipient walkthrough, accessibility review, security/privacy approval, licence decision, partner rehearsal, or release sign-off.

## 6. Allowed task-state edits

Unless the selected task explicitly authorizes a synchronized roadmap/schema amendment, edit only these existing task fields:

- `status`
- `evidence`
- `blocked_reason`

Do not silently change task IDs, dependencies, milestone, priority, executor, environment, decision references, objective, deliverables, acceptance criteria, verification, expected test tier, or full/extended triggers to make work easier.

Use states truthfully:

- `backlog`: specified but not selected;
- `ready`: dependencies and required inputs are available;
- `in_progress`: implementation has begun;
- `blocked`: a concrete technical, environmental, decision, input, or human-evidence blocker exists;
- `review`: implementation is complete but required verification/human evidence remains;
- `done`: every deliverable, criterion, verification, and required evidence is satisfied;
- `cancelled`: only after an authorized human scope decision.

Evidence entries include task, command/review, tier, result, environment/constraint, date, duration, artifact/commit/reference, and limitation. Do not paste secrets, personal data, recovery material, or large raw logs into YAML.

When applying a synchronized bundle revision to an in-progress repository, preserve existing `status`, `evidence`, and `blocked_reason`. The one-time 0.3.0 TL-0008 transition uses `tools/merge_task_contracts.py`; a later governed amendment must merge only its approved contract changes and retain prior execution history.

## 7. Test-tier selection

`TESTING.md` is binding.

- **Quick:** after each small fix and before checkpoint commits; default target two minutes or less.
- **Targeted:** before pushing a feature/fix and after a changed subsystem or risk boundary; default target ten minutes or less unless justified.
- **Full:** at milestone/preview/pilot/stable gates, major refactors, migrations, dependency changes, protected release merges, or a named task trigger.
- **Extended:** only for the specific changed risk or explicit gate trigger; every scenario independently invokable and checkpointed.

Follow the selected task’s `expected_test_tier`, `full_test_triggers`, and `extended_test_triggers`.

When a test fails:

1. stop a broad run when enough evidence exists to isolate the defect;
2. reduce it to the smallest practical deterministic reproduction;
3. add/update a focused regression;
4. run that case first;
5. run the related targeted set; and
6. rerun a broader tier only when its trigger applies or a shared cause is suspected.

A blind rerun is not evidence. Flaky tests are defects; quarantine requires an owner, task, reason, risk, and removal condition.

Never disable analyzers, warnings-as-errors, security/provenance checks, accessibility checks, modest-hardware regression checks, or failing tests merely to obtain green status.

## 8. Session protocol

### Before editing

1. Identify the selected task and objective.
2. Read dependencies, decisions, deliverables, criteria, verification, test tier/triggers, environment, human evidence, and prior evidence.
3. Check `PROJECT_BOUNDARY.md` and current `STATUS.md`.
4. Inspect existing code/tests; reuse a suitable abstraction rather than creating a parallel one.
5. Run the narrowest baseline and record pre-existing failures.
6. Set only the selected task to `in_progress`.

### During implementation

1. Keep the diff scoped to the selected task.
2. Implement domain/failure semantics before or alongside adapters/UI.
3. Preserve dependency direction: Core/domain code does not depend on WPF, SQLite, WinGet, shell output, or siblings.
4. Add happy-path, negative, unavailable, adversarial, interruption, recovery, and resource tests appropriate to the changed risk.
5. Update schemas, fixtures, migrations, threat/privacy/accessibility/resource notes, `STATUS.md`, and release-interface placeholders when behavior changes.
6. Keep user-visible actions explicit: data touched, privilege, network, disk, restart, rollback, verification, failure, and recovery.
7. Avoid drive-by refactors. Record project-local follow-up work; record cross-project ideas only in the deferred notes file.
8. Commit/push coherent checkpoints according to `DEVELOPMENT_WORKFLOW.md`.
9. Stop before destructive action, frozen-decision/boundary change, unsupported bypass, arbitrary execution, sibling integration, or an unsafe host-level constraint.

### Before completion

1. Compare implementation against every acceptance criterion.
2. Run the task’s expected tier and any triggered broader tier.
3. Inspect the diff for secrets/PII, unsafe logging, broad permissions, arbitrary commands/paths/URLs, path traversal/junction issues, unbounded inputs/outputs, accidental telemetry/network use, sibling-data access, unsupported hardware claims, and broadened scope.
4. Confirm accessibility and modest-hardware impact, including background work, concurrency, memory/temp/cache/database growth, cancellation, and UI semantics.
5. Update task evidence/state and `STATUS.md`.
6. Use `review` when required human evidence remains; `blocked` for a named blocker; `done` only for complete proof.
7. Commit and push. Verify the remote contains the reported commit and the worktree is clean or explicitly documented.
8. Report the next dependency-ready task but do not start it.

## 9. Repository verification

`TL-0002` establishes the final commands. The intended local baseline is:

```powershell
python tools/validate_bundle.py
powershell -File eng/verify.ps1 -Tier Quick
```

Once implemented, targeted/full/extended invocations must be documented and independently selectable. A clean clone or worktree on the same physical machine runs the quick tier at defined gates.

Remote runtime CI is optional and non-authoritative. It cannot substitute for active-machine evidence or become a project/release dependency.

## 10. Architecture boundaries

### Domain purity

`ThirdLife.Core` and other domain layers must not depend on WPF, SQLite, WinGet, PowerShell, localized output, or siblings. Providers/adapters return typed normalized observations/results. Missing provider data remains unavailable/unknown rather than passed.

### Persistence

Use migrations and transactional repositories. Preserve source observations, policy version, action/verification history, and evidence provenance. Do not retroactively rewrite historical decisions. Keep attachments bounded and permission-restricted; never copy raw command output blindly into the database.

### Profiles, policy, and catalogue

Profiles/policies/catalogue entries are declarative validated data referencing only compiled action types and reviewed generic Core catalogue identities. They contain no scripts, command strings, arbitrary executables, registry paths, URLs, installer arguments, or sibling behavior.

### Privilege and IPC

The WPF UI remains unelevated. The broker is ephemeral, independently validates caller/request, accepts only typed allowlisted actions bound to approved digests, uses restricted authenticated IPC with nonce/expiry/correlation/message limits, returns structured results, and exits after the approved batch. No permanent LocalSystem service is introduced.

### Packages and updates

Prefer structured supported APIs. Never parse localized WinGet tables as the production contract. Resolve exact identity/source/publisher/version and enforce trust/provenance. No security-hash override is exposed. Backend success is not verification. Material metadata changes invalidate approval.

### Verification and journal

Persist truthful states: planned, approved, started, applied, verified, failed, skipped, rolled back, requires review. Execution and verification are separate. Essential failure prevents ready status. Resume/retry is state-aware and idempotent.

### Reports and diagnostics

Workshop record, recipient guide, and support bundle have separate allow-listed schemas. Support export is previewable. Exclude secrets, credentials, recovery keys, personal content, Wi-Fi names, IPs, serials, usernames, personal paths, and sibling data by default unless a reviewed contract explicitly permits a field.

## 11. Security and safety prohibitions

Never implement or suggest as a shortcut:

- donor-media erasure inside live Core;
- Windows/activation/processor/TPM/Secure Boot compatibility bypass;
- firmware-password, MDM, Autopilot, anti-theft, or ownership circumvention;
- registry cleaning, generic optimization/debloating, unknown-software deletion, malware cleanup, or broad existing-PC repair;
- arbitrary PowerShell/shell commands, executables, registry paths, URLs, or file operations through profile/UI/IPC/metadata;
- a permanent privileged service, retained token, disabled trust check, hash override, or “continue anyway” around a blocker;
- a numeric health/security score, certification, or unsupported guarantee;
- automatic recipient accounts, recovery-key custody, hidden cloud dependency, telemetry by default, or secret-bearing logs/reports/support bundles;
- direct sibling data access or B1 implementation of B4 adapters;
- unsafe host resource/network/storage manipulation merely to emulate weak hardware; or
- a release claim that VMs, constraints, fixtures, or one active machine certify all modest hardware.

When uncertain, fail closed, preserve state, explain known/unknown, and provide the safe manual/human-review path.

## 12. Accessibility and modest-hardware obligations

Every task considers keyboard journey, focus, names/roles/states, announcements, scaling, high contrast, color independence, plain-language errors, cancellation, and recovery. Prefer standard WPF controls; custom controls require automation peers and tests.

Do not require a GPU. Avoid unnecessary background work, permanent indexing, repeated large loading, unbounded cache, and excessive concurrency. Stream/chunk, preflight disk, preserve rollback headroom, keep concurrency conservative/configurable, and record active-machine resource metrics for hashed representative workloads.

A known accessibility/resource regression cannot be marked `done` unless the task explicitly includes and evidences an approved limitation. Lack of another physical device is not a reason to remove these design requirements or overstate evidence.

## 13. Data, migration, release interface, and provenance

Document persistent configuration, jobs, attachments, cache, temporary files, logs, reports, support data, and migration locations. State whether an operation references, copies, converts, exports, retains, or deletes data.

Migrations are versioned, transactional where practical, backed up before incompatible change, and tested for interrupted/corrupt/older/newer cases. Unsafe rollback is disclosed before update with export/recovery steps.

Do not fill `RELEASE_INTERFACE.md` with guesses. Record only implemented, verified behavior; mark unsupported/TBD truthfully. Include the active reference-machine profile, constraint methods, test tiers, skipped scenarios, and limits on hardware claims.

Pin dependencies/tools. Verify licences and redistribution rights. Maintain dependency inventory/SBOM. Do not silently fetch “latest” binaries. Record exact version, source, hash/signature, and policy.

## 14. Completion report template

```text
Task: TL-XXXX — <title>
Status: done | review | blocked
Branch: <branch>
Commit: <commit>
Push/upstream: <verified state>
Working tree: clean | <documented state>

User outcome:
- <what now works or is understood>

Changed:
- <path>: <reason>

Testing:
- Quick: <command> — <duration/result>
- Targeted: <command> — <duration/result or not run + reason>
- Full: <command> — <duration/result or not run + trigger decision>
- Extended: <scenario> — <duration/result or not run + trigger decision>

Reference machine / environment:
- <profile revision, clean environment, constraint, fixture/workload hash>

Defect handling:
- <focused regression and rerun evidence or none>

Boundary impact:
- Project-vacuum/sibling impact: none | <approved detail>
- Data/migration impact: none | <detail>
- Release-interface impact: none | <detail>

Risk impact:
- Security/privacy: <detail>
- Accessibility/modest-hardware: <detail and claim limit>

Evidence:
- <artifact/commit/report>

Outstanding:
- <human evidence, limitation, blocker, or none>

Next dependency-ready task:
- TL-XXXX — <title> | none
```

Do not use a completion narrative as a substitute for evidence, a pushed commit, or truthful test state.
