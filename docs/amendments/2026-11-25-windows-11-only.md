# Windows 11–only platform amendment

| Field | Record |
| --- | --- |
| Amendment | `AMD-2026-11-25-WINDOWS-11-ONLY` |
| Owner and authority | Janne Vuorela, Principal Software Architect and Sole Project Owner, through the explicit implementation instruction in the active Codex conversation |
| Direction date | 25 November 2026, Europe/Berlin |
| Bundle revision | `0.6.0` → `0.7.0` |
| Implementation | `TL-0315` |
| Verified baseline | Integrated `main` at `8f1441be844536e8b4ef88193b3a3e4c1d9c453e` |
| Related checkpoint | Draft PR #36, `codex/tl-0311-authenticated-broker`, at `a41c8c7a2f2981f3da17612c6ec4b37a5c4f5aff` |

## Authority and scope

The owner directed Windows 11 client on native x64 as the sole application host and the
sole operating-system target for new live assessment, preparation, remediation, and
handover. The instruction authorizes the synchronized D-045 amendment, implementation,
technical verification, commit, push, pull-request maintenance and routine integration
when the actual merge requirements are met. Pilot exposure, public release, substantive
unrelated security-policy changes, and destructive machine actions retain their existing
authorization requirements. D-074 and the permanent testing policy reset still apply.

The previous Windows 10 audit entitlement and the alternative-operating-system recommendation
gave the supported workshop flow outcomes outside its Windows preparation scope. An audit
track or a separate platform port would keep that ambiguity and expand support obligations.
Preserving read access to existing records gives workshops a way to inspect prior work.

## Synchronized change

- **D-005.** Identify Windows 11 client using Windows version, workstation product type,
  exact reviewed Windows 11 build and native x64 OS architecture. Keep the UI standard-user.
  Build 22000 is the first Windows 11 base build. A higher number alone does not identify
  Windows 11. Established edition and servicing requirements still apply to readiness.
  Host recognition and lifecycle freshness are separate.
- **D-017.** Use Ready to prepare, Repair and retest, Human review required, and Do not deploy
  for current decisions. A demonstrated repair needs an existing Windows 11 workflow.
  Missing or contradictory evidence remains unknown. A verified scope or unrepairable
  eligibility failure blocks deployment. Retain the legacy
  `alternative_operating_system_candidate` token for historical reading and audit replay.
- **D-027.** Keep C#, .NET 10, WPF, structured Windows APIs, and testable adapters. Only the
  UI depends on WPF. Core and protocol remain independent of WPF, and the production entry
  points enforce Windows 11 client/native-x64 before live work.

The synchronized authority, roadmap, task graph, startup guidance, boundary, product
documents, policy schema, fixtures, validators and manifest adopt these rules together.
The completed TL-0107 acceptance and evidence describe its original contract. TL-0315
supersedes the Windows 10 audit criterion for all later runtime and evidence claims.
Later release work qualifies install, update, repair and removal for Windows 11 client
on native x64. The installer milestone and release authorization stay with their existing
tasks. No prospective task is devoted solely to the obsolete routes.

TL-0315 depends on integrated TL-0310. The next TL-0311 continuation first reconciles
its platform enforcement with TL-0315. The M3 gate and all later prerequisites stay in
place. The two commits unique to PR #36 are preserved. Its trusted elevated startup and
real privilege, recovery and cross-account evidence remain scoped to TL-0311.

## Compatibility, verification and rollback

Historical jobs, observations, disposition tokens, policy versions and digests, saved
reports, and source evidence retain their original meaning. Current policy is versioned.
Historical replay can explain a prior decision without granting current action authority.
The policy-owner direction applies to the platform-derived changes only. Prior TL-0007
approval remains tied to its original fixture digest. The archived policy and profile
files retain their exact bytes. The canonical current plan version is `thirdlife.plan.v2`.
Prior `thirdlife.plan.v1` plans remain readable with their journal history. Approvals bound to those plans
do not authorize a new dispatch or reboot resume. Generate a current plan and obtain a
current approval for future work. Prior unresolved execution remains available for
reconciliation. This contract needs no SQLite schema migration.

Use the active machine and an isolated synthetic job to verify the real application,
saved/reopened job, current workshop report, keyboard/focus, UI Automation and scaling.
Use deterministic cases for unsupported and unverifiable platforms, lifecycle freshness
and policy compatibility. Run Targeted, then the triggered clean-checkout Full gate.
Supplementary Windows Server checks cover build, governance and deterministic behavior.
Record source, artifacts, environment and material limits in [TL-0315 evidence](../../artifacts/evidence/TL-0315/report.md).

The active scope search covers governing documents, production providers, policy/profile
fixtures, UI/report mappings, tests and CI. Current logic retains the Windows 11 guard,
freshness checks and edition restrictions. Archived policy and record readers retain former
serialized values for compatibility. Synthetic Windows 10, Server and architecture cases
exercise rejection. Earlier TL-0107 and M1 evidence, amendments, and review records remain
historical. M1’s audit-only milestone describes a read-only internal application route.

Startup refusal precedes store access and elevation. The amendment keeps data collection
bounded and uses existing consent, privacy and privilege controls. Early refusal uses the
standard accessible dialog, and supported-host work retains the existing resource and
accessibility budgets. This is a standalone B1 change with no sibling dependency.

Correct any defect through a forward commit based on the amended contract. Preserve
operator and historical records, policy evidence, and original exported files. Restoring
an obsolete live route or disposition would require a later approved amendment. Neither
reverting policy version nor rewriting job history is a recovery procedure.
