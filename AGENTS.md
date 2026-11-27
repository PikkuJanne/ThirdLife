# AGENTS.md — ThirdLife Setup Core

**Bundle version:** 0.8.0  
**Project:** Team B / B1  
**Physical validation scope:** active Codex machine only

**Permanent guest limit:** the existing `ThirdLife-W11` is the only Windows guest for the complete project lifecycle, including pilot, release and maintenance. Do not request or create another Windows instance, a VM clone or a new Windows Sandbox instance, or change/reinstall Windows solely to manufacture a test condition. Do not reopen second-guest or additional-Windows-licensing questions. Ordinary supported servicing, repair and authorized checkpoint refresh of this same guest remain Codex-owned. Reuse its authorized checkpoint when restoration is needed, preserving evidence first; destructive resets require only the authority applicable to that operation. Earlier broader VM/Sandbox instructions are historical, not prospective authorization.

## Start and continue

1. Read [DEVELOPMENT_WORKFLOW.md](DEVELOPMENT_WORKFLOW.md). Verify remote, branch, worktree, HEAD, upstream and divergence, fetch from the intended repository, and compare with `main`. Preserve unexplained work and use an isolated branch/worktree when needed.
2. Read [STATUS.md](STATUS.md), the current [product contract](docs/product-contract.md) and applicable [boundary](PROJECT_BOUNDARY.md), [security](SECURITY.md), [privacy](docs/privacy/privacy-model.md), [accessibility](ACCESSIBILITY.md), [resource](LOW_SPEC.md) and [test requirements](TESTING.md). Use [DECISIONS.md](DECISIONS.md), ADRs and prior evidence for rationale and provenance.
3. Read [TASKS.yaml](TASKS.yaml), dependencies, execution and release prerequisites, implementation, tests and evidence for the outcome. Run the narrowest relevant baseline and record pre-existing failures.
4. Apply the standing project-wide engineering assignment through development and maintenance. Use implementation-ready tasks, prefer `ready` to `backlog`, and consider impact, priority and the current continuation. Use milestone order as a planning signal. Validate the actual implementation dependencies and keep the scope coherent.
5. After task completion, checkpoint integration or a scoped deferral, start the next authorized implementation-ready task in the same session. If a capability is blocked, identify the affected action and exact missing prerequisite. Diagnose and repair missing providers, adapters, runners and recovery routes where feasible. After two materially identical attempts without new information, change method or park that component and continue independent work. Record the changed input or hypothesis for a repeat.
6. A missing live test condition limits only its observation and dependent action. Complete implementation, deterministic checks and independent tasks. Repair whole-task dependencies that incorrectly propagate that limit. A successful scan with no eligible Windows Update offer does not prove that no updates exist, that Windows is fully patched, or that installation succeeded; record the scan and exact remaining qualification separately.

## Preserve scope and safeguards

- Keep Windows 11 client/native x64 admission, standalone B1 scope and the project vacuum. Do no sibling inspection, integration or B4 work. Preserve the controlled pilot at `TL-0611`, standalone release/Team B exit at `TL-0710`, and subsequent Scam Explainer queue.
- Keep the unelevated UI, durable plan/approval/journal and independent verification. Dispatch only a qualified and authorized backend for the exact approved action. Supported Windows/WinGet/vendor routes may run independently. ThirdLife-owned privileged code requires the ephemeral broker. Missing capability or authorization prevents the affected dispatch.
- Preserve sanitization, ownership and supported-Windows controls, exact package trust and scope, least privilege, data history, privacy, accessibility and bounded resources. Keep profiles non-executable, domain layers independent of infrastructure, and historical policy results intact. Never manufacture observations or consent.
- Codex can refine implementation, split tasks, revise genuine dependencies, choose a compliant route, repair tooling and integrate verified isolated checkpoints. Retain behavior and protection requirements when changing acceptance or tests. Use [change control](docs/change-control.md) for a material product/trust change, new commercial/licensing obligation or external exposure.
- Keep task state truthful. A checkpoint may be ready to integrate while an execution capability remains blocked. Record the exact blocker and a reopening condition. Use `done` only for evidenced completion at the task's declared scope. Record unmet release prerequisites and retain them for the corresponding exposure.

## Verify and integrate

- Codex owns functional, regression and technical accessibility verification, diagnosis, repair, cleanup and evidence. Use real entry points and focused negative or recovery cases as appropriate. Preserve reusable unchanged evidence and its provenance. Record unobserved behavior and limits separately.
- When automatic approval review rejects cleanup, stop the rejected deletion and preserve the residue. Record its verified location and the stated reason locally, exclude the residue from commits and deliverables, and continue in a separate output location when needed. Escalate only a demonstrated problem that permitted alternatives cannot resolve.
- Run Quick during iteration and before checkpoints, and Targeted for changed subsystems and risks. Targeted includes Quick. Run Full or independently invokable Extended scenarios when the applicable trigger requires them. Use a clean checkout on the active machine when required. GitHub CI is supplementary.
- Keep objective results distinct from operator action consent, Windows UAC, recipient choices, specialist decisions and pilot/public exposure consent. Plan zero routine owner QA, observations, retests or sign-offs. Prepare safe work first. Request only a consequential, unresolved input or authorization that genuinely applies, with the material limits and exact action.
- Check changed code and evidence for unintended privilege, trust/provenance changes, arbitrary execution, paths or data, secrets, unbounded resources, privacy leaks and unsupported claims. Preserve enforced repository access and merge controls.
- Commit/push verified coherent checkpoints and integrate via the repository process. Record base, head, tests, limitations and exact remaining work. Verify ancestry before branch retirement and obtain approval for destructive changes. Do not reset, rewrite or force-push to resolve divergence.
- Keep [STATUS.md](STATUS.md) short. Task evidence lists the command/review, tier, result, date/duration, source/artifact, environment and concise limitation. Store details in `artifacts/evidence/<task>/`, `artifacts/gates/`, ADRs or dated reviews.

## Handoff

Treat routine progress reports as updates and continue execution. Handoff when the authorized outcome is complete, the owner asks to stop, or no meaningful permitted work remains after checking the current graph and reasonable alternatives.

Report the observed application outcome, persisted continuity where relevant, task/capability state, branch/commit/PR/merge state, clean worktree or documented residue, evidence and tests with their limits, and the next executable action. If a capability remains blocked, identify its exact reopening condition and any actual authorization still required. Report completion only for the verified scope.
