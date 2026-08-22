# ThirdLife Setup Core — Portfolio Boundary and Validation Contract

**Status:** Binding project boundary  
**Bundle version:** 0.3.1  
**Portfolio source:** *ThirdLife Software Portfolio — Two-Team Development Roadmap and Boundary Architecture*, version 2.1, 15 August 2026  
**Current queue position:** Team B / B1  
**Future suite project:** Team B / B4 — ThirdLife Deployment and Suite Assembly

## 1. Identity and purpose

**ThirdLife** is the product family and code identity. **ThirdLife Setup Core** is the active standalone project and the release being built by this repository. Production namespaces remain `ThirdLife.*` because the later B4 suite-assembly project may extend the same host product; this does not authorize integration code during B1.

ThirdLife Setup Core helps a refurbisher, volunteer, support worker, or recipient assess and prepare a supported Windows PC through safe, explicit, understandable actions. It must remain useful, installable, removable, recoverable, and supportable without any sibling portfolio application, project-controlled account, external test laboratory, or permanent privileged service.

## 2. Owns

ThirdLife Setup Core owns:

- local refurbishment jobs and recorded external sanitization evidence;
- read-only device, operating-system, firmware, storage, battery, update, security, activation, management, and key-device observations;
- a guided manual functional-test workflow whose evidence states **Observed**, **Human confirmed**, **Not available**, **Failed**, or another approved bounded state without implying hardware certification;
- organization policy evaluation and explainable disposition;
- a small reviewed catalogue of generic free essentials and synthetic test packages;
- outcome-based machine and recipient profile data;
- complete change-plan preview and per-action approval;
- supported package operations and structured Windows Update orchestration;
- an unelevated UI and an ephemeral, typed, allowlisted elevated broker;
- action journaling, restart recovery, independent verification, and cold-boot acceptance on the active Codex machine at the later task that explicitly requires it;
- recipient-controlled accessibility setup for the Core 1.0 release;
- basic operating-system backup onboarding and restore guidance/verification for the Core 1.0 release;
- finalization, handover, workshop records, recipient guides, and sanitized diagnostics;
- its own installer, update, repair, uninstall, data migration, support, threat model, accessibility evidence, and same-machine modest-hardware evidence;
- its own GitHub continuity, task status, test tiers, deterministic fixtures, clean-clone procedure, and release evidence.

## 3. Does not own

ThirdLife Setup Core does not own:

- PaperWorkShell document preparation or its workspaces;
- CaptionKit media, models, transcripts, captions, or correction workflow;
- Scam Explainer message evidence or legitimacy decisions;
- Job Application Studio records, authoring, deadlines, or application ranking;
- Charity Cyber Check authorization, assessment evidence, beneficiary data, or certification;
- Backup Circle repositories, schedules, credentials, recovery keys, or backup-engine behavior;
- a central user identity, portfolio content library, shared workspace, or behavioural record;
- another application's private database, internal source, development branch, background process, or release schedule;
- shared SDKs, universal job engines, universal findings schemas, plugin frameworks, shared user-content databases, or portfolio-wide services;
- registry cleaning, generic optimization, aggressive debloating, unsupported Windows bypasses, ownership-lock removal, or security certification;
- the B4 portfolio catalogue, sibling-app adapters, suite compatibility matrix, or offline suite deployment media;
- a device lab, second physical test computer, lower-performance reference computer, volunteer hardware pool, external hardware matrix, or authoritative remote runtime test farm;
- broad manufacturer, device-class, or modest-hardware certification based on one physical development machine.

Any change to **Owns** or **Does not own** requires an explicit portfolio decision, not an ordinary implementation task.

## 4. Primary data boundary

ThirdLife Setup Core stores only the data required for its own device-support workflow:

- local profile and policy selections;
- job and device identity needed by the workshop record;
- normalized observations and human confirmations;
- decisions, exceptions, plans, action journals, verification, and finalization state;
- application/package metadata used by the selected plan;
- handover and sanitized support metadata;
- local test and benchmark evidence stripped of prohibited identity, secret, and user-content fields.

It must not index, ingest, or take ownership of sibling-application content. It must never store Backup Circle keys or repository credentials, Charity Cyber Check evidence, message bodies from Scam Explainer, recordings/transcripts from CaptionKit, job-search records, or PaperWorkShell document content merely because those applications may be installed later.

Opening or launching a user-selected file in a future adapter must state whether a file is referenced, copied, or converted. Uninstalling ThirdLife must not delete sibling-application workspaces.

## 5. Current development posture: project vacuum

B1 is developed in its own repository and its own project vacuum.

Codex must not:

- inspect or modify a sibling repository unless the user explicitly assigns a later B4 task;
- add a source, binary, service, schema, database, branch, or test dependency on another portfolio project;
- create a PaperWorkShell-specific package entry, profile, file association, command, adapter, or acceptance test;
- create speculative extension points solely because a future sibling might use them;
- block a ThirdLife Setup Core task on an unfinished sibling feature;
- create cross-team release synchronization or assume another project is installed.

When a cross-project idea appears, record it in `FUTURE_ASSEMBLY_NOTES.md` and continue the current project-local task.

## 6. Validation and hardware boundary

The active Codex machine is the only physical machine used for implementation, tests, benchmarks, clean environments, manual walkthroughs, cold-boot evidence, and release evidence.

Allowed same-machine isolation and constraint tools include:

- clean clones and separate worktrees;
- local virtual machines, Windows Sandbox, containers, and virtual disks when supported;
- deterministic synthetic and captured provider fixtures;
- configurable concurrency, low-priority execution, no-GPU paths, bounded low-space volumes, offline/interrupted-network stubs, and slow-destination fixtures;
- capability-absent, permission-denied, provider-unavailable, and failure states exercised deterministically.

These are engineering techniques, not proof that every modest computer or peripheral class has been tested. Missing touch, battery, Bluetooth, dock, card-reader, degraded-storage, manufacturer, or other hardware classes do not block development. Product behavior for absent or unavailable capability must still be deterministic, accessible, privacy-safe, and honestly reported.

The former `TL-0008 draft 1` physical device-pool and `MHT-001`–`MHT-021` walkthrough is superseded by `D-064`. Revised TL-0008 defines the test system and specifications; it does not execute the former broad physical, accessibility, failure-injection, low-resource, or cold-boot matrices.

## 7. Modest-hardware engineering obligation

Lack of a low-performance test computer does not remove the low-resource requirement. ThirdLife must be designed for modest supported hardware through:

- streaming and chunking rather than unbounded whole-input loading;
- bounded collections, logs, caches, retries, temporary files, and queue depth;
- conservative configurable concurrency and a tested CPU fallback;
- preflighted disk requirements and rollback reserve;
- cancellation, checkpointing, resume, and idempotent recovery;
- avoidance of unnecessary startup work, permanent indexing, and repeated model or provider loading;
- measured startup, elapsed time, peak memory, CPU time, temporary storage, cache growth, and output size for versioned workloads;
- graceful degradation that never disables safety, privacy, accessibility, or verification controls.

Release wording may state design intent and active-machine observations. It may not claim broad hardware certification or untested minimum specifications.

## 8. Integration preparation allowed during B1

Integration readiness during B1 means publishing the ordinary information a responsible standalone desktop product should already expose:

1. stable product identity, release tag, source commit, and version;
2. normal install, update, repair, and uninstall behavior;
3. privilege, restart, rollback, and remaining-data behavior;
4. documented configuration, job/workspace, cache, temporary, log, support, and backup locations;
5. ordinary inputs and outputs;
6. normal interactive launch behavior and only independently useful command-line options;
7. a sanitized, previewable support bundle;
8. non-sensitive sample artifacts and expected results;
9. known limitations, offline behavior, reference-machine observations, test tiers, same-machine constraints, and explicit limits on hardware claims;
10. clean-clone and GitHub source-continuity instructions.

These fields are completed in `RELEASE_INTERFACE.md` only when preview/stable behavior is known. They are human-readable release documentation, not a shared runtime API.

## 9. Integration prohibited during B1

The following are explicitly deferred:

- sibling-application catalogue entries and curated portfolio profiles;
- app-specific launch/open adapters;
- suite-wide file associations or composite journeys;
- compatibility cuts and adapter version ranges;
- offline package caches containing sibling releases;
- shared schemas or machine-readable handoff protocols created only for the portfolio;
- reading or writing sibling private data;
- testing against another team's active branch or development build.

Use generic public free essentials and synthetic packages to prove package, profile, journal, verification, and recovery behavior.

## 10. Future B4 treatment

ThirdLife Deployment and Suite Assembly is the only project expected to understand several portfolio applications. B4 consumes exact frozen releases, hashes, release-interface sheets, sample artifacts, and public documentation.

Its default integration order is:

1. install/update/remove the frozen application;
2. launch it interactively;
3. open a user-selected standard file through supported operating-system behavior;
4. open a documented workspace/export when independently supported;
5. show human guidance;
6. add a narrow ThirdLife-owned adapter only when repeated user burden justifies it.

An adapter must be optional, version-bounded, independently disableable, and have a manual fallback. Adapter failure must not make the standalone product unusable.

## 11. Standalone release and team transition

The M6 gate authorizes only a controlled v0.1 pilot. It is not the Team B project-exit gate.

B1 completes only at `TL-0710`, after ThirdLife Setup Core 1.0 is independently releasable and the following are frozen:

- installer/package, immutable GitHub tag/source commit, and cryptographic hashes;
- dependency lock, SBOM, licence evidence, and third-party notices;
- clean-clone, quick, targeted, full, and risk-relevant extended evidence from the active Codex machine;
- reference-machine profile, workload hashes, same-machine constraint records, and explicit hardware-claim limits;
- security, privacy, accessibility, offline, update, repair, uninstall, and data-preservation evidence;
- known limitations and sanitized support sample;
- `RELEASE_INTERFACE.md` and non-sensitive samples.

After that gate, Team B proceeds to **Scam Explainer**. It does not begin portfolio integration until B4 becomes the formal Team B project.

## 12. Change rule

A normal Codex task may improve ThirdLife Setup Core within this boundary. Any proposal to add a sibling domain, shared portfolio infrastructure, live cross-project dependency, second-machine requirement, unsupported hardware claim, or B4 adapter work must stop at a documented decision point and obtain portfolio-owner approval.
