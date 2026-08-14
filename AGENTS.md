# AGENTS.md — ThirdLife Setup Core Codex Operating Contract

## 1. Scope and mission

This contract governs every Codex session in the ThirdLife Setup Core repository. It applies from the repository root downward unless a nested `AGENTS.md` adds stricter local rules. A nested file may not weaken this contract, a frozen decision, the binding roadmap, or the project boundary.

The current project is **ThirdLife Setup Core**, Team B / B1. It is safety-sensitive Windows refurbishment software and must be independently useful without any sibling portfolio application. Prefer small, typed, testable, explainable, recoverable work over broad automation.

The v0.1 controlled pilot is not the project exit. Team B/B1 completes only at the standalone Core 1.0 gate `TL-0710`. After that, Team B proceeds to Scam Explainer. Portfolio-specific integration remains deferred to ThirdLife Deployment and Suite Assembly, Team B/B4.

## 2. Mandatory read order and authority

Before changing code, read in this order:

1. `DECISIONS.md`
2. `ROADMAP.md`
3. `PROJECT_BOUNDARY.md`
4. `SECURITY.md`
5. `ACCESSIBILITY.md`
6. `LOW_SPEC.md`
7. `AGENTS.md`
8. `TASKS.yaml`
9. the selected task’s referenced code, tests, ADRs, schemas, fixtures, documentation, and existing evidence

`CODEX_START_PROMPT.md` supplies operating prompts; `README.md` supplies navigation. `RELEASE_INTERFACE.md` is completed from verified release behavior, and `FUTURE_ASSEMBLY_NOTES.md` is non-binding deferred context.

A lower-authority file cannot weaken a higher-authority file. Do not edit `DECISIONS.md` or change the canonical **Owns / Does not own** boundary unless the user explicitly approves a named amendment. When a material contradiction exists, stop before the conflicting implementation and report the exact files, decisions, tasks, and safe options.

## 3. Project-vacuum and late-integration rules

During B1, this repository is developed in a project vacuum.

Codex must not:

- browse, clone, inspect, modify, import, or depend on a sibling portfolio repository unless the user explicitly assigns a future B4 task;
- add a dependency on a sibling source tree, development branch, binary, service, database, schema, background process, private API, test fixture, or release schedule;
- create a PaperWorkShell-, CaptionKit-, Scam Explainer-, Job Application Studio-, Charity Cyber Check-, or Backup Circle-specific catalogue entry, profile, command, file association, adapter, or acceptance test;
- create a shared SDK, universal job/findings/handoff schema, plugin framework, portfolio background service, shared content store, or monorepo requirement;
- add a cross-project `depends_on` edge or block a Core release on another team’s unfinished work;
- expose a speculative interface solely because a future sibling might use it.

Use generic public free essentials and synthetic test packages during Core development. When a potentially useful cross-project idea appears, add a concise non-binding note to `FUTURE_ASSEMBLY_NOTES.md` and continue the selected project-local task. Do not create implementation, dependency, acceptance, or release work from the note.

B4 may later consume only exact frozen releases, hashes, public documentation, `RELEASE_INTERFACE.md`, known limitations, and non-sensitive samples. B4 owns all sibling-specific adapters and compatibility work. A future adapter must remain optional and have a manual fallback; it never justifies reading private product data or coupling active branches.

## 4. Task selection

### Default selection

Unless the user names a task:

1. Parse `TASKS.yaml`.
2. Exclude tasks with `status: done` or `cancelled`.
3. Require every `depends_on` task to be `done`.
4. Require `executor` to be `codex` or `hybrid`.
5. Prefer `status: ready`; otherwise select an eligible `backlog` task and set it to `ready`.
6. Choose the lowest milestone, then `P0` before lower priority, then the lowest numeric task ID.
7. Work on exactly one task.

At bundle creation, the intended first task is `TL-0001`.

Do not start a dependent task because its dependency appears nearly complete. Do not bypass a gate or combine unrelated tasks to accelerate the roadmap.

### Named task

When the user names a task, verify every dependency first. If a dependency is not `done`, do not implement around it. Report the missing dependency chain. Select no other task unless the user also authorizes it.

### Executors and human evidence

- `executor: codex`: Codex may implement and mark `done` only when every deliverable, acceptance criterion, verification step, and evidence requirement is satisfied.
- `executor: hybrid`: Codex implements the automatable part, then leaves `review` or `blocked` until every `human_evidence_required` item exists.
- `executor: human`: Codex may prepare a checklist, commands, fixtures, or evidence index, but never marks the task `done`.

Never fabricate a Windows run, physical-device observation, workshop-operator result, recipient/proxy walkthrough, NVDA/Narrator review, security/privacy approval, package/licence decision, release sign-off, partner rehearsal, cold-boot result, or go/no-go decision.

## 5. Allowed `TASKS.yaml` state edits

Unless the selected task explicitly changes the roadmap schema or task graph, edit only these fields in existing task entries:

- `status`
- `evidence`
- `blocked_reason`

Do not silently change task IDs, dependencies, milestone, priority, size, executor, environment, decision references, objective, deliverables, acceptance criteria, verification, or human evidence to make work easier.

Use states truthfully:

- `backlog`: specified but not selected;
- `ready`: dependencies and required inputs are available;
- `in_progress`: the current session has begun implementation;
- `blocked`: a concrete technical, environmental, decision, input, or human-evidence blocker exists;
- `review`: implementation is complete but required verification or human evidence remains;
- `done`: the complete task contract is evidenced;
- `cancelled`: only after an authorized human scope decision.

Evidence entries must be concise and durable: command/review, result, environment, date, and artifact/commit/reference. Do not paste secrets, personal content, recovery material, or large raw logs into YAML.

## 6. Session protocol

### Before editing

1. Identify the selected task and restate its objective in the session log or final report.
2. Read its dependencies, `decision_refs`, deliverables, acceptance criteria, verification, environment, human evidence, and prior evidence.
3. Check `PROJECT_BOUNDARY.md` for ownership and non-goals.
4. Inspect existing code and tests; reuse a suitable abstraction rather than creating a parallel one.
5. Run the narrowest relevant baseline tests. Record pre-existing failures separately.
6. Set only the selected task to `in_progress`.

### During implementation

1. Keep the diff scoped to the selected task.
2. Implement domain contracts and failure-state semantics before or alongside adapters/UI.
3. Preserve dependency direction: domain code does not depend on WPF, SQLite, WinGet, shell output, or sibling products.
4. Add happy-path, negative, adversarial, interruption, and recovery tests appropriate to the task.
5. Update documentation, schemas, fixtures, migrations, threat/privacy notes, accessibility notes, low-spec notes, and release-interface placeholders when behavior changes.
6. Keep user-visible actions explicit: data touched, privilege, network, disk, restart, rollback, verification, failure, and recovery.
7. Avoid drive-by refactors. Record a project-local follow-on task/issue; record cross-project ideas only in `FUTURE_ASSEMBLY_NOTES.md`.
8. Stop before an irreversible/destructive action, frozen-decision change, boundary change, unsupported Windows/ownership bypass, arbitrary execution surface, or sibling-specific integration.

### Before completion

1. Compare implementation against every acceptance criterion, not only the stated objective.
2. Run the task’s verification and relevant repository checks.
3. Inspect the diff for secrets/PII, unsafe logging, broad permissions, arbitrary commands/paths/URLs, path traversal/junction issues, unbounded input/output, accidental telemetry/network use, sibling-data access, and broadened scope.
4. Confirm accessibility and low-spec impact, including whether the task added blocking work, background activity, memory/storage growth, or inaccessible custom UI.
5. Update only the selected task’s evidence/state fields.
6. Use `review` when Windows/manual/human evidence remains; use `blocked` for a named blocker; use `done` only for complete proof.
7. Report files changed, tests run, user outcome, boundary impact, data/migration impact, security/privacy impact, accessibility/low-spec impact, release-interface impact, residual risks, and the next dependency-ready task.

Never claim a test passed when it was not run. State exactly why a Windows, privileged, physical, accessibility, partner, licence, or release check could not run.

## 7. Repository verification

`TL-0002` establishes the final clean-checkout verification command. On Windows, after installing the exact SDK and Python tooling documented in `README.md`, run:

```powershell
.\eng\verify.ps1
```

Git Bash on Windows may use `./eng/verify.sh`. Both entry points run the governed bundle and repository validators, locked restore, formatting verification, Release build with warnings as errors, and Release tests. The authoritative CI environment is Windows; an unsupported host must fail clearly rather than skip Windows projects.

Use the checked-in SDK/tool manifest and locked dependencies once created. Run focused tests during implementation and the documented full suite before a gate/release.

Do not disable analyzers, warnings-as-errors, signature/hash/provenance checks, security tests, accessibility checks, low-spec regression checks, or failing tests to obtain green status. Tag Windows integration, privileged, package, update, reboot, physical-device, accessibility, failure-injection, and destructive tests so their required environment is explicit.

Portable unit tests must not require system mutation. Windows-mutating tests use disposable VMs or approved lab devices and leave a recovery/cleanup record.

## 8. Architecture boundaries

Maintain the project structure and dependency rules in `ROADMAP.md` and relevant ADRs.

### Domain purity

`ThirdLife.Core` and other domain layers must not depend on WPF, SQLite, WinGet, PowerShell, localized command output, or sibling applications. Providers and adapters return typed normalized observations/results. Missing provider data remains unavailable/unknown rather than passed.

### Persistence

Use migrations and transactional repositories. Preserve source observations, policy version, action history, and verification history. Do not retroactively rewrite historical decisions. Keep raw attachments bounded and permission-restricted; never copy command output blindly into the database.

### Profiles, policy, and catalogue

Profiles/policies/catalogue entries are declarative validated data. They may reference only compiled action types and reviewed generic Core catalogue identities. They may not contain scripts, command strings, arbitrary executable paths, registry paths, URLs, installer arguments, or sibling-specific integration behavior.

### Privilege and IPC

The WPF UI remains unelevated. The broker is ephemeral, validates caller/request independently, accepts only typed allowlisted actions, binds execution to approved content digests, uses restricted authenticated IPC with nonce/expiry/correlation/message limits, returns structured results, and exits after the approved batch. There is no permanent LocalSystem service in the Core release.

### Packages and updates

Prefer structured supported APIs. Never parse localized WinGet tables as the production contract. Resolve exact identity/source/publisher/version and enforce trust/provenance. No hash/security override is exposed. Backend success is not verification. Material metadata changes invalidate approval.

### Verification and journal

Persist truthful states such as planned, approved, started, applied, verified, failed, skipped, rolled back, and requires review. Execution and verification are separate. Essential failure prevents a ready disposition. Resume/retry is state-aware and idempotent; it must not blindly repeat mutations.

### Reports and diagnostics

Workshop record, recipient guide, and sanitized support bundle have separate allow-listed schemas. Support export is previewable. Exclude secrets, credentials, recovery keys, personal content, donor/recipient information, Wi-Fi names, IPs, serials, usernames, personal paths, and sibling data by default unless a reviewed contract explicitly permits a field.

## 9. Security and safety prohibitions

Never implement or suggest as a shortcut:

- donor-media erasure inside the live Core application;
- Windows/activation/processor/TPM/Secure Boot compatibility bypass;
- firmware-password, MDM, Autopilot, anti-theft, or ownership-control circumvention;
- registry cleaning, generic optimization/debloating, unknown-software deletion, malware cleanup, or broad existing-PC repair;
- arbitrary PowerShell, shell commands, executables, registry paths, URLs, or file operations through a profile, UI, IPC request, or package metadata;
- a permanent privileged service, unnecessarily retained token, disabled trust check, hash override, or “continue anyway” around a blocker;
- a numeric health/security score, certification, or guarantee unsupported by evidence;
- automatic recipient accounts, recovery-key custody, hidden cloud dependence, telemetry by default, or secret-bearing logs/reports/support bundles;
- direct sibling product data access or B1 implementation of B4 adapters.

When uncertain, fail closed, preserve state, explain what is known/unknown, and provide the safe manual or human-review path.

## 10. Accessibility and low-spec obligations

Every task must consider its impact on the complete keyboard journey, focus, names/roles/states, screen-reader announcements, scaling, high contrast, color independence, plain-language errors, cancellation, and recovery. Standard WPF controls are preferred; custom controls require explicit automation peers and tests.

Do not require a GPU. Avoid unnecessary background work, permanent indexing, repeated large loading, unbounded cache, and excessive concurrency. Stream/chunk large data, preflight disk space, preserve rollback headroom, keep concurrency conservative/configurable, and record resource metrics for representative fixtures.

A task that introduces a known accessibility or resource regression cannot be marked `done` unless the task contract explicitly includes and evidences an approved limitation.

## 11. Data, migration, and release-interface discipline

Document every persistent configuration, job, attachment, cache, temporary, log, report, support, and migration location. State whether an operation references, copies, converts, exports, retains, or deletes data.

Migration is versioned, transactional where practical, backed up before incompatible change, and tested for interrupted/corrupt/older/newer cases. Unsafe rollback is disclosed before update with export/recovery steps.

Do not fill `RELEASE_INTERFACE.md` with guesses. Update fields only from implemented and verified behavior. Mark unsupported items truthfully. The file documents the standalone black-box release and is not permission to add a shared API or expose private state.

## 12. Generated files, dependencies, and provenance

Do not hand-edit generated files when a checked-in generator exists. Update the source and regenerate deterministically. Keep fixtures non-sensitive and attributable.

Pin dependencies and tools. Verify licences and redistribution rights before bundling an engine, installer, model, template, font, asset, or data source. Installation rights and offline redistribution rights are separate. Maintain the dependency inventory and SBOM required by the active task.

Do not silently fetch “latest” runtime binaries. Record exact version, source, hash/signature, and policy. A missing licence/provenance decision is a blocker, not a reason to guess.

## 13. Completion report template

Use this structure at the end of a task session:

```text
Task: TL-XXXX — <title>
Status: done | review | blocked

User outcome:
- <what the project can now do or understand>

Changed:
- <file/path>: <reason>

Verification:
- <command or review> — <pass/fail/not run and environment>

Boundary impact:
- Project-vacuum / sibling-integration impact: none | <approved detail>
- Data/migration impact: none | <detail>
- Release-interface impact: none | <field/placeholder affected>

Risk impact:
- Security/privacy: <detail>
- Accessibility/low-spec: <detail>

Evidence:
- <artifact/commit/report reference>

Outstanding:
- <required human evidence, limitation, blocker, or none>

Next dependency-ready task:
- TL-XXXX — <title> | none
```

Do not use a completion narrative to substitute for evidence.
