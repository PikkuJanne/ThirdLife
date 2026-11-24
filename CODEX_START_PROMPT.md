# ThirdLife Setup Core — Codex Start and Reusable Prompts

**Bundle version:** 0.6.0  
**Portfolio baseline:** ThirdLife Software Portfolio v2.1

## 1. Prompt selection

- **Repository not yet consolidated through TL-0106:** continue the
  [approved stabilization](docs/amendments/2026-09-12-repository-stabilization.md)
  from live `STATUS.md` and the [review checklist](docs/reviews/2026-09-12-repository-review.md).
  Stop before merge and do not start `TL-0107` in that session.
- **Normal product session after stabilization:** use the task execution prompt below.
- **Defect, gate, security, accessibility, resource, recovery, or release work:** use the
  matching specialized prompt.

The TL-0008 transition prompt is historical. Do not select it for normal current work.

## 2. Normal task execution prompt

```text
Work on one ThirdLife Setup Core task at a time. Continue within an explicitly assigned
sequence from updated main after each completed task is integrated.

Start with DEVELOPMENT_WORKFLOW.md. Verify remote, branch, worktree, HEAD, upstream,
divergence, and default-branch integration state. Fetch/prune safely.

Do not select a new product task when completed prior work is not integrated into main. In
that case stop and perform only the named stabilization/integration work.

Read STATUS.md and the binding documents in AGENTS.md order. Parse the selected TASKS.yaml
entry, including dependencies, decisions, deliverables, criteria, verification, test tier,
triggers, prior evidence, and applicable accountable decisions.

If the task is not dependency-ready, stop. Do not implement around it.

Implement the smallest coherent diff. Preserve:
- standalone Team B/B1 scope and project vacuum;
- local-first behavior;
- unelevated UI and ephemeral allowlisted broker;
- declarative non-executable profiles;
- one semantic owner or explicit reconciliation per evidence fact;
- evidence/policy separation;
- independent verification;
- truthful journal states;
- privacy-safe outputs;
- accessibility;
- resource-conscious design;
- versioned and stale-safe external lifecycle knowledge.

All authoritative runtime verification occurs on the active Codex machine. Remote CI is
supplementary only.

Run Quick during iteration and before checkpoints. Run Targeted for the changed subsystem.
Run Full/Extended only when triggered. Reduce failures to focused deterministic regressions.

Keep STATUS.md concise. Put detailed evidence in committed artifacts and reference it from
TASKS.yaml.

Codex owns preparation, verification, diagnosis, repair and evidence under D-074.
Use real entry points. Carry forward valid unchanged evidence. Distinguish objective
results, actual observations, evidence limits, reported defects and scope-bound decisions.
Plan zero routine human QA, confirmation, retests or owner sign-off. Apply the D-074
exceptional-input criteria and continue safe preparation before a later decision.

Commit and push coherent checkpoints. Open/update the task pull request. Do not start the
next roadmap task until this task is integrated into main.

Pause when an action genuinely requires a decision, at a concrete external blocker or at the end of the
assigned work. Leave the AGENTS.md report and a resumable next action when paused.
For the authorized M1 continuation, complete and integrate the TL-0116 technical gate,
then end the assignment.
```

## 3. Focused defect prompt

```text
Investigate only the named defect. Verify Git/default-branch integration and current task
first. Reproduce with the smallest deterministic fixture/scenario. Stop broad runs once the
defect is isolated. Add the focused regression, run it first, then the related Targeted set.
Run Full/Extended only when triggered.

For each repeat record the changed input or expected new observation. After two materially
identical uninformative attempts, change the method or record the limitation and continue
independent work. Record durable evidence, compact task reference, branch, commit, push,
PR, and limitations.
```

## 4. Milestone or protected-merge prompt

```text
Prepare and verify the named gate. Preserve actual authorization and its scope.

Verify graph closure, branch/default integration, clean-clone continuity, and all predecessor
evidence. Run bundle/repository validation, active-machine Full, and only triggered Extended
scenarios. Remote CI is supplementary.

Create an evidence index mapping every criterion to a durable artifact. Record reference
machine, constraints, workload hashes, durations, skipped scenarios, defects, claim limits,
commit, tag/artifact state, and PR/merge state.

Complete technical gates from verified evidence. Leave genuine decision gates at review
or blocked when an applicable decision is still missing. Record the actual decision only.
```

## 5. Security/privacy prompt

```text
Review the named change against SECURITY.md, the threat model, privilege/package/update
provenance, data boundaries, diagnostics, support export, and project-vacuum rules.

Use deterministic adversarial fixtures on the active machine. Cover applicable malformed and
oversized input, path/link attacks, arbitrary-execution attempts, IPC replay/caller/expiry,
source substitution, signature/hash mismatch, secrets/PII leakage, denial, interruption, and
data preservation.

Run focused and Targeted first; broader scope only when triggered. Preserve trust checks and
fail-closed behavior.
```

## 6. Accessibility prompt

```text
Review the named workflow against ACCESSIBILITY.md on the active machine. Verify keyboard,
focus, names/roles/states/relationships, announcements, Narrator/approved NVDA, 200% scaling,
reduced resolution, high contrast, color independence, plain language, cancellation, and
recovery.

Automate objective checks and record each result with its exact build, settings,
assistive technology and limitations. Investigate reported failures through the relevant
route. Preserve actual prior observations with provenance. UI Automation and layout
checks do not establish perceived speech or usability. Apply D-074 to exceptional input.
```

## 7. Modest-hardware/resource prompt

```text
Review against LOW_SPEC.md. Record commit, profile revision, tier, command, constraint,
workload hash, elapsed/CPU time, peak memory, temporary/cache/database/output growth,
cancellation/checkpoint/resume, cleanup, and result.

Use only risk-relevant same-machine profiles. They find regressions; they do not certify a
different low-end device. Reject unbounded growth, corruption, missing cancellation, mandatory
GPU/high concurrency, or unsupported claims.
```

## 8. Recovery/interruption prompt

```text
Exercise only the named interruption/recovery scenario in the safest deterministic or
disposable active-machine environment. Record checkpoint, injection, journal/database state,
user message, recovery action, independent verification, cleanup, and limitation.

Do not disrupt the host without an explicit task, recovery plan, and approval. Partial work is
never presented as final.
```

## 9. Release-interface prompt

```text
Populate RELEASE_INTERFACE.md only from the exact frozen build and evidence. Include identity,
artifact/hash/signature/tag/commit, lifecycle commands, launch, data, inputs/outputs,
offline/network, resource behavior, privilege/security, support bundle, samples, source
continuity, validation, and limitations.

Record active-machine observations and same-machine constraints. Do not invent minimum specs,
silent options, rollback, APIs, or broad hardware support.
```

## 10. Stable-release prompt

```text
Prove Core 1.0 is independently installable, runnable, updateable/repairable, exportable,
recoverable, and removable without siblings, mandatory account, project server, or permanent
privileged service.

Verify data preservation, support sanitization, offline behavior, security/privacy,
accessibility, modest-hardware evidence, lifecycle, SBOM/licences, clean clone, test manifest,
known limitations, and release interface. Run active-machine Full and only triggered Extended
scenarios. Use the prepared evidence and limits for the actual release and portfolio
decisions at TL-0710.
```
