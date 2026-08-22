# ThirdLife Setup Core — Codex Start and Reusable Prompts

**Bundle version:** 0.3.1  
**Portfolio baseline:** ThirdLife Software Portfolio v2.1

## 1. Prompt selection

Use exactly one prompt per session:

- **Existing repository currently at TL-0008:** use `CODEX_TL0008_TRANSITION_PROMPT.md` first.
- **Fresh repository with no implementation:** use the first-session prompt below and start from the first dependency-ready task, normally `TL-0001`.
- **Normal later session:** use the reusable task prompt and select from live `STATUS.md`/`TASKS.yaml`.
- **Gate/release/security/accessibility/resource work:** use the specialized prompt for that purpose.

Do not paste the obsolete TL-0008 physical-device walkthrough into Codex. It is superseded.

## 2. First session in a fresh repository

```text
You are starting work in the ThirdLife Setup Core repository, Team B/B1.

Before implementation:
1. Read DEVELOPMENT_WORKFLOW.md.
2. Verify remote, branch, worktree, HEAD, upstream, and divergence using the documented Git sequence.
3. Read STATUS.md, DECISIONS.md, ROADMAP.md, PROJECT_BOUNDARY.md, SECURITY.md, ACCESSIBILITY.md, LOW_SPEC.md, TESTING.md, AGENTS.md, and TASKS.yaml.
4. Run python tools/validate_bundle.py.
5. Parse TASKS.yaml and select exactly one dependency-ready task. In a truly fresh repository this should normally be TL-0001; do not assume that if live status says otherwise.

Binding constraints:
- The active Codex machine is the only physical machine used for implementation, tests, benchmarks, clean environments, walkthroughs, and release evidence.
- Do not require or seek a lab, lower-performance computer, second PC, volunteer device pool, cloud runner matrix, or missing equipment class.
- GitHub is the continuity source of truth. Commit and push coherent checkpoints; update STATUS.md; do not report completion until the remote contains the reported commit.
- Follow the task’s expected_test_tier, full_test_triggers, and extended_test_triggers. Quick/targeted tests guide iteration; full/extended tests run only when triggered.
- Reduce every reproducible defect to the smallest practical deterministic regression. Slow scenarios must be independently invokable and checkpointed.
- Preserve the standalone project vacuum. Do not inspect or depend on sibling repositories or create B4 integration behavior.
- Preserve the unelevated UI, ephemeral allowlisted broker, declarative non-executable profiles, evidence/policy separation, independent verification, truthful journal states, local-first data ownership, privacy-safe outputs, accessibility, and modest-hardware design.
- Release wording may describe design intent and active-machine observations only; it may not imply cross-hardware certification.

Implement only the selected task with the smallest coherent diff. Update only status/evidence/blocked_reason unless the task explicitly authorizes a synchronized roadmap/schema change.

At completion, run the required test scope, update STATUS.md, commit and push, verify upstream and clean-tree state, and report using the AGENTS.md template. Stop after one task.
```

## 3. Reusable single-task prompt

```text
Work on exactly one ThirdLife Setup Core task.

Start by reading DEVELOPMENT_WORKFLOW.md and verifying remote/branch/status/HEAD/upstream/divergence. Read STATUS.md and the binding documents in AGENTS.md order. Parse the selected TASKS.yaml entry, including dependencies, decision_refs, deliverables, acceptance_criteria, verification, expected_test_tier, full_test_triggers, extended_test_triggers, prior evidence, and required human evidence.

If the task is not dependency-ready, stop and report the dependency chain. Do not implement around it.

Implement the smallest coherent diff that satisfies the complete task contract. Preserve the Team B/B1 project vacuum, local-first behavior, unelevated UI/ephemeral broker model, declarative non-executable profiles, evidence/policy separation, independent verification, truthful journal states, privacy-safe outputs, accessibility, and resource-conscious design.

All runtime verification runs on the active Codex machine. Clean clones/worktrees, VMs, Windows Sandbox, containers, virtual disks, and constraints are allowed only on that physical machine. Do not request a second device or remote runner.

Run the task’s expected tier. Run full/extended scope only when a named trigger applies. On failure, isolate the smallest deterministic regression, run it first, then the related targeted set. State every tier not run and why.

Update STATUS.md and only the selected task’s status/evidence/blocked_reason fields. Commit and push. Verify the remote contains the reported commit and the working tree is clean or explicitly documented. End with the AGENTS.md completion report and stop.
```

## 4. Focused defect prompt

```text
Investigate the named ThirdLife defect without starting unrelated roadmap work.

Verify Git/STATUS first. Reproduce the defect with the smallest deterministic fixture or scenario on the active Codex machine. Stop any broad suite once enough evidence exists to isolate the cause. Add or update a focused regression, run it first, then run the related targeted set. Run full or extended scope only if the task trigger or shared-cause evidence requires it.

Do not blind-rerun a flaky test and call it passed. If deterministic reproduction is not yet practical, record the technical reason, risk, owner, bounded follow-up task, and temporary containment.

Preserve security/privacy, data, accessibility, resource, and project-boundary invariants. Update evidence/STATUS, commit/push, verify upstream, and report exact commands, durations, environment/constraints, fixture hash, result, and broader tiers not run.
```

## 5. Milestone gate prompt

```text
Prepare the named milestone gate; do not approve a human gate.

Verify Git continuity and read STATUS.md, ROADMAP.md, DECISIONS.md, TASKS.yaml, TESTING.md, and AGENTS.md. Build an evidence index mapping every milestone exit criterion and every transitive predecessor acceptance criterion to a durable artifact.

Verify graph closure and prior-gate dependency. Run python tools/validate_bundle.py. Run the full tier on the active Codex machine and only those extended scenarios whose task/gate triggers apply. Include clean-clone evidence, branch/commit/push state, active reference-machine profile, constraint settings, workload hashes, durations, skipped scenarios, defect reruns, and claim limits.

Identify missing, stale, contradictory, environment-limited, or human-only evidence. A VM/constraint/fixture does not become hardware certification. Do not mark executor: human gates done or fabricate approvals.

Update the gate record and STATUS.md, commit/push the evidence package, and report pass/review/blocked with exact outstanding evidence.
```

## 6. Security and privacy review prompt

```text
Review the named ThirdLife task/release against SECURITY.md, the threat model, data boundary, privilege model, package/update provenance, logging/support-bundle rules, and project-vacuum constraints.

Use deterministic adversarial fixtures and the active Codex machine only. Cover applicable unknown actions, malformed/oversized input, path traversal/junctions, arbitrary-command attempts, IPC caller/replay/expiry, metadata/source substitution, hash/signature mismatch, secrets in logs/arguments/reports/support bundles, access denial, interruption, and uninstall/data preservation.

Run targeted checks first. Run full/extended security scope only when triggered. Reduce each defect to a focused regression. Do not weaken trust checks, permissions, redaction, or failure-closed behavior for test convenience.

Report evidence, residual risk, unsupported/unrun cases, claim limits, and release blockers. Update STATUS/task evidence, commit/push, and verify upstream.
```

## 7. Accessibility review prompt

```text
Review the named workflow against ACCESSIBILITY.md and TESTING.md on the active Codex machine.

Verify keyboard-only completion, visible focus, logical order, names/roles/states/relationships, status/progress/error announcements, Narrator and approved NVDA use, 200% scaling, reduced resolution, high contrast, color independence, plain language, cancellation, pause/resume, and error recovery.

Use automated/component checks at quick/targeted scope and perform human review only when the task requires it. Record exact build, machine profile, assistive technology/version, procedure, limitation, and result. Do not generalize one environment into universal assistive-technology or hardware support.

Update evidence/STATUS, commit/push, verify upstream, and leave hybrid tasks at review until the named human evidence exists.
```

## 8. Modest-hardware/resource review prompt

```text
Review the named ThirdLife change against LOW_SPEC.md and TESTING.md.

All measurements run on the active reference machine. Record source commit, profile revision, tier, exact command, clean environment, constraint profile, workload/fixture hash, elapsed/CPU time, peak memory, temporary/cache/database/output growth, cancellation/checkpoint/resume, cleanup, and result.

Use only risk-relevant same-machine profiles such as no-GPU, conservative concurrency, low-priority, low-free-space, offline, interrupted-network, provider-unavailable, slow-destination, or representative large workload. These are regression-finding tools, not simulations/certifications of a specific low-end device.

Reject unbounded growth, corruption, lost state, missing safe cancellation, mandatory GPU/high concurrency, or release wording that exceeds evidence. Run the smallest scenario first and broader scope only when triggered.

Update evidence/STATUS, commit/push, verify upstream, and state the claim boundary explicitly.
```

## 9. Recovery/interruption prompt

```text
Exercise the named interruption or recovery scenario on the active Codex machine using the safest deterministic injection or disposable hosted environment.

Record the precondition/checkpoint, injected event, journal/database state, user-visible message, resume/retry/recovery action, verification result, cleanup, and defect/limitation. Never disrupt the host network/storage/boot state without an explicit task, checkpoint, recovery plan, and required human approval.

A backend return or application restart is not success. Verify the intended result independently and prove partial work is not presented as final. Reduce failures to focused deterministic regressions and rerun only the affected scenario/targeted set unless a broader trigger applies.
```

## 10. Release-interface completion prompt

```text
Complete only fields in RELEASE_INTERFACE.md supported by the exact frozen build and evidence.

Include identity, artifact/hash/signature/source tag+commit, install/update/repair/remove, launch, data locations, inputs/outputs, offline/network behavior, privilege/security, support bundle, samples, known limitations, source continuity, and validation evidence.

For resource behavior, record the active reference-machine profile, actual observations, same-machine constraint methods, workload hashes, low-resource modes, skipped scenarios, and explicit absence of cross-hardware certification. Do not invent minimum specifications, silent options, rollback, APIs, or hardware support.

Mark unsupported/TBD with a reason. The interface is human-readable black-box documentation, not a shared runtime schema or permission to expose private state.
```

## 11. Core 1.0 stable-release prompt

```text
Prove ThirdLife Setup Core 1.0 is independently installable, runnable, updateable/repairable, exportable/recoverable, and removable without a sibling application, mandatory account, project-controlled server, or permanent privileged service.

Verify recipient-controlled accessibility/basic operating-system backup boundaries, data preservation, support sanitization, offline behavior, security/privacy/accessibility evidence, same-machine modest-hardware evidence and claim limits, known limitations, exact artifacts/hashes/SBOM/licences, clean-clone result, test manifest, and completed RELEASE_INTERFACE.md.

Run full scope and only the explicitly triggered extended scenarios on the active Codex machine. Ensure focused defect regressions and targeted reruns are present. Human approvals remain human; do not mark TL-0710 done.
```
