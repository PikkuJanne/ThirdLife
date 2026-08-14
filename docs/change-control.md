# ThirdLife Setup Core — Change Control

**Status:** Repository change process derived from D-045 and `AGENTS.md`  
**Applies to:** Code, documentation, task state, schemas, fixtures, evidence, release inputs, and portfolio notes

This process keeps completed evidence, safety boundaries, and the Team B/B1 project vacuum intact. Silence, a convenient implementation, a passing test, or an ordinary pull-request approval is not permission to amend a frozen decision or the canonical project boundary.

## Authority order

When documents differ, apply them in this exact order:

1. `DECISIONS.md`
2. `ROADMAP.md`
3. `PROJECT_BOUNDARY.md`
4. `SECURITY.md`
5. `ACCESSIBILITY.md`
6. `LOW_SPEC.md`
7. `AGENTS.md`
8. `TASKS.yaml`
9. `CODEX_START_PROMPT.md`
10. `README.md`

`RELEASE_INTERFACE.md` contains verified black-box release facts; it does not outrank the list or authorize new behavior. `FUTURE_ASSEMBLY_NOTES.md` is a non-binding deferred backlog and creates no B1 requirement.

## Change classes

### Task execution change

For the one selected task, Codex may update only its `status`, `evidence`, and `blocked_reason` fields in `TASKS.yaml`. State must follow the declared transition graph. Evidence must identify the command or review, result, environment, date, and durable artifact, commit, run, or file reference.

Task execution authority does not permit changing IDs, dependencies, milestone, priority, size, executor, environment, decision references, objective, deliverables, acceptance criteria, verification, or human-evidence requirements.

### Contract-preserving implementation change

An eligible task may add or modify implementation, tests, documentation, schemas, fixtures, migrations, and evidence that its existing contract requires. The change must remain inside ThirdLife Setup Core, respect every higher-authority rule, and preserve completed history. It receives normal code/document review and the task's required automated, Windows, physical, accessibility, security/privacy, licence, or human evidence.

### Governed amendment

A proposal is a governed amendment when it changes or contradicts a frozen decision, the canonical **Owns / Does not own** boundary, portfolio posture, milestone scope, task graph or acceptance contract, authority order, supported safety boundary, release gate, or Team B queue.

Stop before implementing such a proposal. Explicit human approval must name the amendment, approving owner, and approval date; approval by implication is invalid. The amendment record must include:

- the affected decision IDs, authority files, tasks, schemas, releases, and completed evidence;
- the user problem and why the existing contract cannot safely satisfy it;
- security, privacy, accessibility, low-spec, data/migration, recovery, support, and project-vacuum impact;
- alternatives, including retaining the current contract or using a manual fallback;
- dependency-graph, compatibility, rollout, rollback/non-rollback, and historical-evidence treatment;
- the documents, validators, tests, owners, and human evidence that must change; and
- the new bundle version and changelog entry required by D-045.

An ADR may explain an implementation choice, but it cannot overrule a higher-authority decision. `TL-0009` owns the initial ADR set; an ordinary earlier task must not invent a parallel decision register.

### Deferred cross-project idea

If an idea has value only with a sibling product, do not open an active B1 task, add a dependency, inspect a sibling repository, or build an adapter. Add only a concise entry to `FUTURE_ASSEMBLY_NOTES.md` with the target journey, required frozen release/interface, shallow action, data and privilege impact, manual fallback, risk, maintenance boundary, and reason it belongs to B4.

## Amendment sequence after approval

1. Record the explicit approval and amendment scope before implementation.
2. Update the highest affected authority first. Preserve prior decisions and completed evidence through a clear supersession or migration record; do not silently rewrite history.
3. Synchronize every lower affected document, task record, schema, fixture, validator, test, prompt, and navigation entry.
4. Change task dependencies or acceptance contracts only within the named approved amendment. Validate the complete DAG and milestone-gate closure.
5. Increment the bundle version consistently and update `CHANGELOG.md` when D-045 applies.
6. Regenerate checked-in generated artifacts from their source, refresh `BUNDLE_MANIFEST.sha256`, and inspect all diffs.
7. Run the task-specific checks and the full Windows command `eng/verify.ps1`. Do not disable warnings, analyzers, security, accessibility, low-spec, provenance, signature/hash, or test gates.
8. Record environment-limited and human-only evidence honestly. A required approval or physical/manual result cannot be inferred from automated output.
9. Publish the reviewed change and its evidence without secrets, personal data, machine-specific paths, or sibling private information.

## Contradiction and stop rule

When a material contradiction exists, stop before the conflicting implementation. Report the exact files, decision IDs, task IDs, observed conflict, safety or delivery effect, and safe options. Leave the selected task `blocked` only when there is a concrete blocker and record the owner and unblock condition; otherwise keep it in the truthful workflow state.

Never bypass or weaken sanitization, supported-Windows, ownership, package trust, approval, privilege, verification, finalization, accessibility, low-spec, privacy, release, or human-review gates to resolve a contradiction.

## Evidence and claim discipline

- A code or document change is not evidence that its behavior works.
- Automated, Windows, physical-device, accessibility, security/privacy, licence, partner, and release evidence remain distinct.
- Unknown, unavailable, untested, or unsupported behavior stays explicit.
- Historical evidence remains tied to the source revision, dependency lock, policy/catalogue/profile version, environment, and artifact it actually tested.
- `RELEASE_INTERFACE.md` receives only implemented and verified facts. Use **TBD**, **not yet verified**, or **not supported** instead of speculative promises.
- No task is `done` until every deliverable, acceptance criterion, verification step, and declared human-evidence item is satisfied.

## Pull-request review checklist

- Exactly one dependency-ready task is selected and its execution fields are truthful.
- The diff is scoped to that task and follows the authority order.
- No frozen decision, boundary, graph, or gate was changed without a named approved amendment.
- No sibling dependency, private-data access, shared integration infrastructure, or early B4 work was introduced.
- Security/privacy, accessibility, low-spec, data/migration, and release-interface impacts are stated.
- Negative, failure, interruption, recovery, and adversarial checks appropriate to the change are present.
- The bundle validator, task checks, full repository verifier, manifest, and secret/machine-path scans pass.
- Evidence and release claims match what was actually run and reviewed.
