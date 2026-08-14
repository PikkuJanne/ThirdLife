# ThirdLife Setup Core — Non-Goals

**Status:** Scope guard derived from frozen decisions and `PROJECT_BOUNDARY.md`  
**Applies to:** Team B / B1 development, the controlled v0.1 pilot, and Core 1.0

These exclusions are safety and ownership boundaries, not a list of unfinished features. An ordinary implementation task cannot turn one into scope. Any genuine amendment follows `docs/change-control.md` and requires explicit human approval at the correct authority level.

## No existing personal-PC repair or donor-media erasure

ThirdLife Setup Core starts from donor storage that is sanitized, replaced, or absent and from a fresh or known Windows installation. It does not own:

- donor-media erasure inside the live Core application;
- preservation or migration of an unknown previous owner's data;
- malware cleanup, unknown-software removal, broad repair, or incident response on an existing personal PC;
- forensic proof that sanitization succeeded; or
- destructive stress or repair operations presented as routine assessment.

External sanitization is recorded as evidence. Unknown or failed sanitization blocks preparation.

## No cleaner, optimizer, debloater, or bypass positioning

ThirdLife Setup Core is not a PC cleaner, optimizer, debloater, registry cleaner, driver-download utility, or general IT toolbox. Those words may appear in governed text only to explain excluded positioning or prohibited behavior.

No unsupported Windows bypass is provided. The product does not:

- bypass Windows support, processor, TPM, Secure Boot, activation, firmware-password, MDM, Autopilot-style, anti-theft, or ownership controls;
- remove management or ownership controls automatically;
- offer generic registry edits, arbitrary scripts, arbitrary command execution, or unrestricted file operations;
- report a numeric health/security score, certification, blanket guarantee, or unsupported minimum-hardware claim; or
- weaken provenance, hash, signature, approval, verification, accessibility, or recovery checks to make a workflow pass.

Unsupported or unknown states remain explicit and may block preparation or handover.

## No recipient identity or secret custody

The workshop does not create personal cloud accounts, infer accessibility preferences, retain passwords or recovery keys, take custody of backup credentials, or make recipient-specific choices while the recipient is absent. Sealed handover records these choices as pending.

Core 1.0 may guide a present recipient through supported accessibility settings and basic operating-system backup onboarding. It does not build a backup engine, invent a repository format, or control another product's backup data.

## No sibling-domain ownership

This repository does not own or process the private domains of PaperWorkShell, CaptionKit, Scam Explainer, Job Application Studio, Charity Cyber Check, or Backup Circle. In particular, B1 does not read or write sibling workspaces, documents, recordings, transcripts, messages, job-search records, assessment evidence, repositories, schedules, credentials, recovery keys, or private databases.

Core catalogues, profiles, fixtures, commands, file associations, and acceptance tests use generic public free essentials or synthetic packages. They contain no sibling-specific behavior.

## No early shared integration infrastructure

B1 does not create:

- a shared SDK, universal job/findings/handoff schema, or plugin framework;
- a portfolio account, content library, user-content database, background service, or monorepo requirement;
- a sibling-specific catalogue entry, composite suite profile, command, file association, launch/open adapter, or compatibility record;
- a build, runtime, test, data, branch, service, or release dependency on another portfolio project; or
- a speculative interface whose only justification is possible future suite work.

An interface or command is considered during B1 only when it is independently useful to Core users, testing, automation, or support.

## No B4 work during B1

ThirdLife Deployment and Suite Assembly is a separate future Team B / B4 project. B4 owns sibling-specific installation records, profiles, adapters, compatibility cuts, offline suite media, and cross-product black-box tests. Passing the controlled pilot or Core 1.0 gate does not authorize B4.

A later B4 project may consume only exact frozen releases, cryptographic hashes, `RELEASE_INTERFACE.md`, known limitations, non-sensitive samples, and public documentation. It may not treat a sibling active branch or private implementation as an integration contract.

## Handling an out-of-scope proposal

1. Ask whether the capability has a complete, independently useful Core outcome.
2. If it does and is already inside the selected task's contract, implement it there. Otherwise route it as a separately authorized project-local follow-on without silently expanding the task or editing the task graph.
3. If its value depends on a sibling product, record only a concise non-binding entry in `FUTURE_ASSEMBLY_NOTES.md` with a manual fallback and why it belongs to B4.
4. Continue the selected B1 task without adding code, acceptance criteria, dependencies, or release promises from the note.
