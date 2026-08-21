# Codex Prompt — Apply the TL-0008 Single-Machine Validation Transition

Use this prompt once in the live ThirdLife Setup Core repository.

```text
You are working in the ThirdLife Setup Core repository on task TL-0008.

The portfolio owner has approved a governance change before the former hardware-lab step is executed. The active Codex machine is now the only physical machine used for implementation, tests, benchmarks, clean environments, and release evidence. The project will not use lab machines, lower-performance test computers, a volunteer device pool, an external hardware matrix, or an authoritative remote runtime runner.

Historical procedure reference — preserve for audit, but DO NOT EXECUTE:
- Procedure: TL-0008 draft 1
- Source commit: 4fa3ea050fd5e9985fde9cc8218281698d371cc8
- Procedure digest: ef150dbf14b5db208582b7b526c7e0c6d0a5b912736e9e6519b8918abcf0928b
- Former paths: docs/testing/manual-hardware-tests.md and docs/testing/device-matrix.md
- Former action: build a physical device pool and run MHT-001–MHT-021

Do not reset to the named commit. Verify whether it is current, an ancestor, absent, or divergent, and preserve all newer work.

Follow this sequence exactly:

1. Read DEVELOPMENT_WORKFLOW.md from the supplied 0.3.0 bundle.
2. Verify the live repository state before editing:
   - git remote -v
   - git branch --show-current
   - git status --short --branch
   - git rev-parse HEAD
   - git log -1 --decorate --oneline
   - git fetch --all --prune
   - git rev-list --left-right --count HEAD...@{upstream}, when an upstream exists
3. Stop and report rather than force-reset, force-push, or overwrite unexplained local work if the branch, remote, worktree, or history is unsafe.
4. Read STATUS.md, DECISIONS.md, ROADMAP.md, PROJECT_BOUNDARY.md, SECURITY.md, ACCESSIBILITY.md, LOW_SPEC.md, TESTING.md, AGENTS.md, and the TL-0008 entry in TASKS.yaml from bundle version 0.3.0.
5. Work only on TL-0008. Do not start later implementation tasks.
6. Merge the 0.3.0 task contracts into the live TASKS.yaml while preserving every existing task’s status, evidence, and blocked_reason. Use tools/merge_task_contracts.py or an equivalent reviewed merge. Do not reset completed work to the bundle template.
7. Apply the synchronized 0.3.0 governance documents and schema/validator changes.
8. Preserve the old TL-0008 draft as docs/history/TL-0008-draft-1-superseded.md or an equivalent history file. Add a prominent:

   SUPERSEDED — DO NOT EXECUTE

   banner and retain the procedure revision, source commit, digest, supersession date, and reason.
9. Replace the physical device inventory with docs/testing/capability-risk-matrix.md. Map variants to deterministic fixtures, sanitized captured samples, safe same-machine constraints, bounded active-machine observation, or explicit unverified limitations.
10. Rewrite docs/testing/manual-hardware-tests.md as a product-workflow specification. Retain safe test IDs/result semantics where useful, but state that MHT-001–MHT-021 are not executed during revised TL-0008 and are not hardware certification.
11. Create/update:
   - docs/testing/reference-machine-profile.md
   - docs/testing/same-machine-constraints.md
   - docs/testing/failure-injection.md
   - docs/testing/accessibility-matrix.md
12. Record only sanitized reference-machine/toolchain facts. Do not record serial numbers, asset tags, usernames, email addresses, device names, SSIDs, IP addresses, credentials, recovery keys, personal paths, screenshots, photos, audio, video, or raw logs.
13. Define quick, targeted, full, and extended tiers. Specify cold boot, accessibility, failure injection, and resource matrices now, but do not run them unless TL-0008 explicitly triggers them. It does not.
14. Do not seek another computer, lower-performance machine, lab device, or missing equipment. Missing hardware capability is represented by deterministic Not available/failure state coverage and an explicit limitation, not a blocker.
15. Run only the TL-0008-appropriate checks:
   - python tools/validate_bundle.py
   - the repository quick documentation/schema/static tier
   - safe searches proving no active binding requirement for a hardware lab, second physical machine, lower-performance device, physical-device matrix, or authoritative remote runtime CI remains
16. Do not run:
   - MHT-001–MHT-021 physical walkthrough
   - broad failure-injection matrix
   - accessibility audit matrix
   - low-resource extended matrix
   - a real cold boot solely for TL-0008
   - package/update mutation
   - full or extended release suites
17. Update STATUS.md with the real branch, HEAD, upstream/push state, test commands/durations/results, skipped tiers and reasons, changed files, and next action.
18. Update TL-0008 evidence and status truthfully. Mark done only if every revised deliverable and acceptance criterion is met. No human device-pool confirmation is required.
19. Run the quick tier once more, inspect the diff for personal data, secrets, unsupported hardware claims, and accidental scope changes.
20. Commit a coherent checkpoint, push it, verify the remote contains the reported commit, verify the working tree is clean or explicitly documented, and stop after TL-0008.

Your final report must include:
- Task and status
- Branch, commit, upstream/push confirmation, and working-tree state
- User outcome
- Changed files
- Task-contract merge result and confirmation that existing status/evidence/blocked_reason values were preserved
- Quick/targeted/full/extended tests by command, duration, result, or not-run reason
- Superseded procedure history location and digest
- Reference-machine profile path and sanitization confirmation
- Search result for removed hardware-lab dependencies
- Security/privacy, accessibility, modest-hardware, data/migration, project-boundary, and release-interface impacts
- Remaining limitation: single-machine evidence is not cross-hardware certification
- Next dependency-ready task, but do not start it
```
