# Approved amendment — Minimal, meaningful human testing

**Historical.** The [permanent testing policy reset](2026-11-24-permanent-testing-policy-reset.md)
supersedes this prospective policy and withdraws the M1 walkthrough. Completed work and
earlier observations retain their original provenance.

| Field | Approved value |
| --- | --- |
| Amendment | `AMD-2026-11-24-MINIMAL-HUMAN-TESTING` |
| Approving owner | Janne Vuorela, ThirdLife project owner |
| Approval date | 24 November 2026, Europe/Berlin |
| Approval source | The owner's instruction in the active Codex conversation directing implementation of `ThirdLife_Codex_Continuation_2026-11-24.zip` and the assigned M1 sequence |
| Governing decisions | [D-045](../../DECISIONS.md#d-045--authority-and-change-control), [D-073](../../DECISIONS.md#d-073) |
| Bundle revision | `0.4.0` → `0.5.0` |

The owner instructed, “This is my scoped authorization for AMD-2026-11-24-MINIMAL-HUMAN-TESTING and the routine implementation, verification, commit, push, PR update, and merge work described in this bundle, once each affected task's actual requirements are satisfied.” The same instruction states that it supplies no unperformed human test result, security review, or milestone sign-off. The attached documents specify the scope of work. Approval comes from the owner's instruction in the conversation.

## Scope and rationale

The prior TL-0113 procedure assigned build identification, environment recording and repeated journeys to the reviewer. TL-0114 also required a separate operator walkthrough before the M1 gate that already required the operator to exercise the integrated flow. Repeated identical verifier selections consumed time without producing additional coverage. The amendment moves technical preparation to Codex and puts human observations at the point where they can answer a concrete question about the running product.

This is an approved single-task diff exception for the current TL-0113 continuation on [PR #14](https://github.com/PikkuJanne/ThirdLife/pull/14). It permits the synchronized policy, contracts, prompts and narrow verifier change on that line. TL-0114 implementation starts after TL-0113 is complete and integrated into `main`. The recorded PR head `964863ebfc458a63ac559479a40450b652d2d787` and main baseline `d4ec836719746c659f12ca851f9890d62c76bfa7` describe the supplied snapshot. The fetched repository determines the live starting point. Valid newer work and existing evidence retain their original provenance.

The assigned execution sequence is TL-0113, TL-0114, TL-0115 and TL-0116, one active task and branch at a time. Each task integrates before the next task starts. The mandated conversational stop is superseded within this assigned sequence. Planned pauses are the prepared TL-0113 observation, the prepared M1 observation and gate decision, and completion of M1. An unresolved external condition can still require action. This approval covers routine merges only once each affected task's requirements are evidenced. Branch deletion, closing superseded PRs, history rewriting, a public product release and M2 are outside scope.

## Governing and task changes

| Scope | Change |
| --- | --- |
| D-073 | Make automated verification the default, reserve human testing for concrete unresolved questions, separate observations from accountable decisions, and continue through a bounded assignment without task-by-task conversational stops. |
| TL-0113 | Keep the hybrid executor and current review state. Use a single keyboard/Narrator journey and brief actual display checks for the applicable shell cases A11Y-001–007. Keep the conditional NVDA scope and record any unavailable configuration. Codex owns the build, setup, restoration, timing, and case mapping. |
| TL-0114 | Change the executor from hybrid to Codex, add TL-0112 as a direct dependency, and assign integration of job create/open/reopen, sanitization capture, read-only assessment and guided manual capture. Preserve deterministic state, negative-path, and accessibility coverage. Move the operator usability review to TL-0116. |
| TL-0115 | Keep the Codex executor and existing deterministic workshop-report requirements. Add operator UI generation and bounded opening of the produced report from durable job data. |
| TL-0116 | Keep the hybrid executor, existing dependencies, Full tier and triggers. Own the combined integrated operator, manual-capture and new or changed accessibility observations and separate owner decision. |

The former TL-0114 human requirement reads “A representative operator completes the guided workflow on the active Codex machine for available capabilities and reviews the simulated unavailable/failure paths; the record explicitly limits the evidence to that machine and workflow usability.” TL-0116 receives that ownership with a representative applicable manual observation, labelled unavailable/failure examples and pause/resume or close/reopen usability. Remaining capability variants and state transitions receive automated coverage. The operator acknowledges the tested active-machine scenario limits separately from the owner's gate approval. When a safe physical sample is unavailable, an explicitly labelled fixture may exercise workflow behavior and the record states the physical observation that was unavailable.

## Alternatives and impacts

- Keeping the earlier procedure would preserve its coverage but would require duplicate journeys and technical administration from the reviewer. Automation alone leaves actual assistive-technology speech, real display usability, representative operator interpretation and the accountable gate decision unevidenced. The approved option automates setup and objective checks and retains the focused observations.
- Security and privacy retain the existing sanitization, ownership, unelevated startup, bounded evidence and export boundaries. Synthetic sanitization never authorizes live collection against an unsanitized real host. Clearly labelled fixture observations stay separate from real-device evidence. Report opening uses a bounded generated-artifact path. Host Application Control remains in force, and remote CI remains supplementary.
- Accessibility preserves actual usable keyboard focus, Narrator and Windows display evidence for affected interactions. Use actual settings for scaling, reduced resolution and contrast, and restore the starting settings. Repeat only failed, changed or genuinely new interactions, while retaining distinct case results. Verifier deduplication saves identical work within an invocation and preserves independent scenarios and triggered tiers. Existing modest-hardware requirements and the active-machine scope remain binding.
- This amendment adds no runtime storage schema, migration, package, privilege or release interface. Later M1 implementation uses the existing durable job, sanitization, assessment, and evidence contracts. Support and recovery retain privacy, explicit incomplete outcomes and existing review paths. The standalone B1 boundary and future B4 ownership remain binding.

## Synchronization, evidence and recovery

Synchronize `DECISIONS.md`, `ROADMAP.md`, `TASKS.yaml`, `AGENTS.md`, `DEVELOPMENT_WORKFLOW.md`, `CODEX_START_PROMPT.md`, `TESTING.md`, `ACCESSIBILITY.md`, the accessibility and manual-test guidance, and the accessible shell documentation. Update `STATUS.md`, `README.md`, `CHANGELOG.md`, version markers and `BUNDLE_MANIFEST.sha256`. Adapt the existing verifier and the affected governance validation and tests. The task schema already represents the absence of a human requirement by omitting `human_evidence_required`, so the TL-0114 amendment uses that representation. The graph gains one direct edge to TL-0112 with no new task, circular dependency, or change to milestone ordering.

Codex owns implementation, build and scenario preparation, evidence administration, verifier regressions, and actual cleanup and restoration. The reviewer owns only the observations. The project owner owns the explicit M1 gate decision. Automation, human observations, and approvals are recorded separately. A shared session covers only cases the reviewer actually observed. Earlier tests and observations retain their tested source, artifact, environment, result and limits. Carry them forward only after checking which relevant inputs changed. Record unsuccessful diagnostic attempts with the changed input or expected new information. After two materially identical uninformative attempts, change method or record the limitation and continue independent work.

Apply the synchronized change as one versioned bundle on the valid continuation line. Validate the bundle and dependency graph, run focused verifier regressions and the applicable final governance and runtime checks, and publish committed evidence. Keep TL-0113 in review until its observations exist, rehearse the complete M1 flow before its combined observation, and preserve all named later gates. If the change introduces a regression, repair it in a forward commit and rerun the affected checks. A later revert must restore coherent task ownership, dependencies and validation while keeping historical evidence tied to the governing revision in effect when it was collected.
