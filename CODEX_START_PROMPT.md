# ThirdLife Setup Core — Codex Start and Reusable Prompts

**Bundle version:** 0.2.0  
**Current project:** Team B / B1 — ThirdLife Setup Core  
**Standalone release gate:** `TL-0710`  
**Future suite project:** Team B / B4 — ThirdLife Deployment and Suite Assembly

Use these prompts from the repository root. The prompts do not override `DECISIONS.md`, `ROADMAP.md`, `PROJECT_BOUNDARY.md`, or `AGENTS.md`.

## 1. First session — establish the repository

```text
You are starting development of ThirdLife Setup Core, the Team B/B1 standalone project in the ThirdLife portfolio.

Read, in order:
1. DECISIONS.md
2. ROADMAP.md
3. PROJECT_BOUNDARY.md
4. SECURITY.md
5. ACCESSIBILITY.md
6. LOW_SPEC.md
7. AGENTS.md
8. TASKS.yaml
9. README.md

Then run the roadmap-bundle validator. Do not inspect or depend on any sibling portfolio repository. This project is developed in a project vacuum; future portfolio adapters belong to Team B/B4 and may consume only frozen releases and public release documentation.

Select only TL-0001, verify its dependency state, set only that task to in_progress, and implement its complete contract. Keep the project namespaces ThirdLife.* and scaffold only the standalone Core architecture described by the roadmap. Do not create sibling-specific catalogue entries, shared SDKs/schemas, plugin frameworks, cross-repository dependencies, a permanent privileged service, arbitrary script execution, or speculative integration APIs.

Run every verification available for TL-0001. Update only its status/evidence/blocked_reason fields in TASKS.yaml. Mark done only if the full task contract is proven; otherwise use review or blocked with the exact missing evidence or unblock condition.

End with the AGENTS.md completion report: user outcome, files changed, verification, project-boundary/data/release-interface impact, security/privacy impact, accessibility/low-spec impact, outstanding evidence, and the next dependency-ready task. Stop after TL-0001.
```

## 2. Normal dependency-ready task session

```text
Read DECISIONS.md, ROADMAP.md, PROJECT_BOUNDARY.md, SECURITY.md, ACCESSIBILITY.md, LOW_SPEC.md, AGENTS.md, and TASKS.yaml.

Select exactly one dependency-ready task using AGENTS.md unless I name a task. Confirm its objective, dependencies, decision references, acceptance criteria, verification, executor, environment, and human evidence before editing. Do not inspect or rely on sibling repositories and do not turn a cross-project idea into B1 implementation; record such an idea only in FUTURE_ASSEMBLY_NOTES.md.

Implement the task with the smallest coherent diff. Preserve the standalone Core boundary, local-first behavior, unelevated UI/ephemeral broker model, declarative non-executable profiles, evidence/policy separation, independent verification, truthful journal states, privacy-safe outputs, accessibility, and low-spec behavior. Add negative, failure, interruption, recovery, and adversarial tests appropriate to the change.

Run the task verification and relevant repository checks. Update only the selected task’s status/evidence/blocked_reason. A hybrid or human task cannot be completed without its declared human evidence. End with the AGENTS.md completion report and stop.
```

## 3. Execute a specifically named task

```text
Work only on task <TL-XXXX>.

Read the binding files in AGENTS.md order, then verify every dependency is done. If any dependency is not done, do not implement around it; report the dependency chain and stop. Otherwise set only <TL-XXXX> to in_progress and implement all deliverables and acceptance criteria.

Keep work inside ThirdLife Setup Core and its project-local graph. No sibling repository, active branch, private database, app-specific adapter, shared SDK/schema, or cross-project acceptance criterion is allowed. Use FUTURE_ASSEMBLY_NOTES.md only for a concise deferred idea that does not affect completion.

Run the declared verification and relevant full checks. Update only <TL-XXXX> execution-state fields. Use done, review, or blocked truthfully and provide the AGENTS.md completion report. Stop after this task.
```

## 4. Review an implementation without broadening scope

```text
Review task <TL-XXXX> against its complete TASKS.yaml contract and cited decisions. Read the diff, affected tests, schemas, migrations, documentation, SECURITY.md, ACCESSIBILITY.md, LOW_SPEC.md, and PROJECT_BOUNDARY.md.

Check at minimum:
- every acceptance criterion and verification step;
- dependency direction and project-vacuum compliance;
- no sibling-specific behavior or private-data access;
- input/path/schema bounds and failure behavior;
- privilege, IPC, package/update trust, and arbitrary-execution surfaces;
- evidence versus inference versus human confirmation;
- persistence, migration, journal, resume, retry, and independent verification;
- log/report/support-bundle privacy;
- keyboard/screen-reader/scaling/plain-language behavior;
- low-memory/low-space/no-network/no-GPU and cancellation/recovery impact;
- whether RELEASE_INTERFACE.md contains verified facts rather than guesses.

Report findings by severity with file and line references. Do not make unrelated edits. If authorized to fix findings, keep them inside <TL-XXXX>, rerun verification, and update only its execution-state fields.
```

## 5. Milestone gate preparation

```text
Prepare gate task <TL-XXXX> without fabricating evidence.

Read the milestone in ROADMAP.md and all transitive predecessor tasks in TASKS.yaml. Build an evidence index mapping every exit criterion, task acceptance criterion, automated check, required Windows/physical/accessibility/security/privacy/licence/partner review, and unresolved limitation to a durable artifact.

Verify that the gate transitively covers every task in its milestone and depends on the prior gate. Run the bundle validator and all available release checks. Identify evidence that is missing, stale, contradictory, environment-limited, or human-only.

For a hybrid gate, leave review or blocked until required human evidence exists. For TL-0611 or TL-0710, Codex may prepare the package/checklist/index but may never authorize the pilot or stable release. Report the exact owner and unblock condition for every missing item.
```

## 6. Security and privilege-boundary review

```text
Perform a focused security review of <task/component> under SECURITY.md, DECISIONS.md, PROJECT_BOUNDARY.md, and the threat model.

Trace untrusted input, identities, paths, package metadata, profiles/policies, IPC messages, privileges, network destinations, persistence, logs, reports, support exports, update inputs, rollback, and cleanup. Attempt or design tests for unknown actions, replay/expiry, another-user IPC, oversized messages, path traversal/junctions, arbitrary executable/argument/URL/registry injection, source substitution, hash/signature mismatch, catalogue downgrade, stale approval, process termination, UAC denial, and secret-bearing fixtures.

Confirm the UI remains unelevated, the broker is ephemeral/allowlisted, profiles are non-executable, package trust cannot be bypassed, and future suite integration has not created sibling data access. Report exploitable findings, missing tests, residual risk, and safe remediation. Do not describe the device as secure or certified.
```

## 7. Accessibility and low-spec review

```text
Review <task/flow> against ACCESSIBILITY.md and LOW_SPEC.md.

Check keyboard-only completion, visible/logical focus, programmatic names/roles/states, status/progress announcements, 200% scaling, high contrast, reduced resolution, no color-only meaning, plain-language errors, cancellation, recovery, and recipient-choice boundaries. Identify which checks require Narrator, NVDA, or a human reviewer.

Measure or define evidence for startup, idle memory, inventory duration, peak memory, temporary storage, database growth, report generation, resume time, CPU-only behavior, conservative concurrency, low-space preflight, no-network/slow-network behavior, and interrupted operations. Do not invent minimum-hardware claims. Return defects, evidence gaps, and exact remediation tasks within the active project boundary.
```

## 8. Blocked-task recovery

```text
Investigate blocked task <TL-XXXX> only.

Read its dependencies, decisions, blocked_reason, code/diff, and evidence. Reproduce or validate the blocker where safe. Classify it as a project-local defect, missing input/environment, third-party limitation, decision contradiction, security/privacy/accessibility issue, release-interface uncertainty, or required human evidence.

Fix it only when the fix is inside the selected task and frozen constraints. Never bypass sanitization, supported Windows, ownership controls, package trust, broker validation, verification, finalization, project-vacuum rules, or human approval. Otherwise update blocked_reason with the exact cause, evidence, owner, safe options, and unblock condition. Do not start another task.
```

## 9. Record a future assembly idea without implementing it

```text
A possible cross-project or suite idea was discovered while working on <TL-XXXX>.

First determine whether the capability independently benefits ThirdLife Setup Core users. If yes, keep only the project-local behavior and document the independent rationale in the active task/decision process. If the value exists only when a sibling application is present, do not implement it and do not add a task dependency.

Add one concise entry to FUTURE_ASSEMBLY_NOTES.md containing: idea, target user journey, likely frozen product release, permitted shallow action, data/privilege considerations, manual fallback, and why it is deferred to Team B/B4. Mark uncertainty explicitly. Continue or complete <TL-XXXX> without making the note an acceptance criterion.
```

## 10. Complete the release interface from verified behavior

```text
Work on TL-0706 and RELEASE_INTERFACE.md only after its dependencies are done.

For each release-interface field, derive the value from frozen candidate artifacts, verified installer/application behavior, documentation, tests, and samples. Record exact identity, versions, hashes, install/update/repair/uninstall behavior, privilege/restart/rollback limits, data locations, inputs/outputs, launch behavior, offline/network behavior, resource evidence, security boundaries, support-bundle contents/exclusions, samples, limitations, and maintenance/reporting route.

Use “not supported” or “not yet verified” with a reason rather than inventing a capability. Do not add a speculative API, shared schema, sibling-specific command, or private-data access to make the sheet look complete. Run black-box tests using only documented behavior and preserve sample hashes/evidence.
```

## 11. Core 1.0 release and portfolio-boundary handoff

```text
Prepare TL-0709 or TL-0710 as assigned.

Prove that ThirdLife Setup Core 1.0 installs, runs, updates/repairs, exports/recovers, and uninstalls without a sibling application, mandatory account, project-controlled server, or permanent privileged service. Verify recipient-controlled accessibility/basic operating-system backup boundaries, data preservation, support sanitization, offline behavior, low-spec evidence, security/privacy/accessibility evidence, known limitations, exact release artifacts, hashes, SBOM/licences, and completed RELEASE_INTERFACE.md.

Search the source, package, tests, profiles, documentation, and task graph for sibling-specific entries, shared portfolio infrastructure, cross-project dependencies, active-branch assumptions, and private data access. Record any future idea only in FUTURE_ASSEMBLY_NOTES.md.

Codex may assemble the evidence index and identify gaps. It cannot mark TL-0710 done or announce the stable release. The human gate must confirm that Team B/B1 is complete, the next project is Scam Explainer, and no B4 integration work has been authorized.
```
