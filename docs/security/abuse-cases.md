# ThirdLife Setup Core — v0.1 Abuse Cases

**Status:** Approved initial model  
**Model revision:** TL-0004 approved 1  
**Draft date:** 2026-08-14  
**Authority:** Supporting analysis for [`threat-model.md`](threat-model.md) and [`data-flow.md`](data-flow.md)

**Post-approval maintenance:** 2026-08-21 — `TL-0005` traceability/status annotation only. The approved base remains commit `917b5ebd5f5e4cf273a087a05dd381da54324235`; threats, residual decisions, and security approval were not changed or re-approved.

These cases turn the threat register into reviewable misuse and failure stories. Attack paths are intentionally abstract: the repository does not need executable payloads, real secrets, private paths, or exploit scripts to test rejection and recovery semantics.

All mapped product controls are **planned** unless their owning task has separately reached done with the required evidence. A roadmap reference does not claim implementation. The named security owner recorded a treatment for every residual risk; this is not release authorization.

## Coverage index

| Required threat family | Abuse cases |
|---|---|
| Command and argument injection | `AC-001`, `AC-002` |
| Package substitution and material metadata change | `AC-005`, `AC-006` |
| Replay, expiry, downgrade, unknown action, and oversized IPC | `AC-002`, `AC-003` |
| Cross-user IPC and pipe impersonation | `AC-004` |
| Path traversal, reparse points, junctions, symlinks, and unsafe temporary/output files | `AC-007`, `AC-018` |
| Log, database, report, and support-export leakage | `AC-008`, `AC-018` |
| Stale, rollback, or malicious catalogue/profile data | `AC-001`, `AC-005` |
| UI/broker death, UAC decline, reboot, partial write, network loss, and blind retry | `AC-009`, `AC-012` |
| Malformed, unbounded, stale, or deceptive provider output | `AC-010`, `AC-014` |
| Journal tamper, corruption, history rewrite, and resume-token misuse | `AC-003`, `AC-011` |
| Windows Update unapproved class, stale scan, restart, and convergence failure | `AC-012` |
| Sanitization, ownership, activation, management, and finalization false-ready | `AC-013` |
| Accidental early cross-project coupling | `AC-015` |
| Future B4 adapter misuse without an adapter design | `AC-016` |
| Dependency/build/release substitution or incomplete provenance | `AC-019` |
| Misleading approval and social engineering | `AC-017` |

## Detailed abuse cases

### AC-001 — Catalogue or profile data injects executable behavior

**Threats:** `THR-001`  
**Actor:** `ACT-05` malicious metadata source or a mistaken catalogue/profile author  
**Preconditions:** The product imports declarative data before strict schema, version, provenance, and executable-content controls exist.  
**Attack path:** A record attempts to smuggle a command, script, arbitrary executable path, URL, registry path, installer argument, raw package ID, unknown action, duplicate/confusable ID, sibling identifier, or deeply nested/oversized value into planning.  
**Assets/impact:** `AST-04`, `AST-05`, `AST-06`; arbitrary or wrongly targeted privileged behavior could be made to look profile-approved.  
**Detection:** Strict schema/version checks, unknown-field rejection, stable/canonical IDs, size/depth/count bounds, downgrade/staleness checks, and static review that data cannot express execution.  
**Fail-closed response:** Reject the complete import/plan before download, approval, broker contact, or mutation; preserve the last approved snapshot and show a bounded reason.  
**Planned controls/tasks:** D-022, D-023, D-025; `TL-0007`, `TL-0201`, `TL-0301`, `TL-0302`, `TL-0303`, `TL-0304`, `TL-0508`  
**Control status:** Planned  
**Recovery/manual path:** Correct and re-review the declarative input; do not add an execution escape hatch.  
**Residual risk:** `RR-002`; schema correctness and reviewer mistakes still require adversarial tests and human review.

### AC-002 — Broker request contains injection, unknown action, downgrade, or oversized framing

**Threats:** `THR-001`, `THR-002`, `THR-012`  
**Actor:** `ACT-03`, compromised `P-01`, malformed client, or corrupted IPC data  
**Preconditions:** An elevated broker accepts input across `TB-BROKER`.  
**Attack path:** A client submits an arbitrary command/path/URL/registry value, unknown action/field, duplicate key, invalid encoding, unsupported protocol version, excessive nesting/collection/message size, correlation mismatch, or parameter outside the compiled action contract.  
**Assets/impact:** `AST-06`, `AST-07`; broker compromise, privileged arbitrary execution, resource exhaustion, or misleading journal state.  
**Detection:** Broker-owned protocol/schema and action registry, explicit version/unknown-field rules, strict byte/depth/count/rate bounds, canonical typed parameters, correlation and approved digest checks.  
**Fail-closed response:** Reject before action dispatch, return a bounded structured error, record no applied state, and exit after the failed/expired batch policy.  
**Planned controls/tasks:** D-023, D-029, D-030, D-031; `TL-0303`, `TL-0310`, `TL-0311`, `TL-0312`, `TL-0313`  
**Control status:** Planned  
**Recovery/manual path:** Recreate a plan through the reviewed UI; protocol errors never justify a generic process-launch fallback.  
**Residual risk:** `RR-001`; a compromised privileged process/OS remains outside application-level assurance.

### AC-003 — Replayed, stale, tampered, or cross-job approval authorizes work

**Threats:** `THR-002`, `THR-005`  
**Actor:** `ACT-03`, stale client, copied checkpoint, or compromised local process  
**Preconditions:** A prior handshake, request, approval, correlation, or resume token exists.  
**Attack path:** The actor reuses a nonce/request, changes job/action/content under an old approval, changes the clock, submits an expired request, copies a resume token to another job/device/user, or races the material-change check.  
**Assets/impact:** `AST-06`, `AST-07`, `AST-09`; unauthorized or duplicate mutation and false attribution.  
**Detection:** Random session nonce, replay state, bounded expiry with monotonic/lifecycle checks where applicable, correlation, exact job/action/user/session/content binding, material plan comparison, and actual-state re-observation.  
**Fail-closed response:** Reject and retain immutable history; require a fresh preview/approval or explicit operator recovery. Never auto-elevate on resume.  
**Planned controls/tasks:** D-025, D-030, D-032, D-033; `TL-0307`, `TL-0308`, `TL-0309`, `TL-0310`, `TL-0311`, `TL-0312`, `TL-0313`, `TL-0404`, `TL-0408`, `TL-0609`  
**Control status:** Planned  
**Recovery/manual path:** Reopen the bound job, re-observe state, regenerate the plan if material data changed, and approve anew.  
**Residual risk:** `RR-001`; wall-clock and local-store tampering by an administrator cannot be eliminated.

### AC-004 — Another local user connects to or pre-creates the broker pipe

**Threats:** `THR-002`  
**Actor:** `ACT-03` other local user/session or a process racing pipe creation  
**Preconditions:** Pipe naming, security descriptor, server identity, or caller/session validation is too broad.  
**Attack path:** The actor connects from another session, creates the expected pipe first, impersonates a server/client, or reuses an active session to submit or observe traffic.  
**Assets/impact:** `AST-06`, `AST-07`; cross-user privileged confused-deputy action, disclosure, or denial of service.  
**Detection:** First-instance/server identity policy, explicit restrictive pipe security descriptor scoped to the initiating logon/session, access-token/session validation, nonce/expiry, and manual ACL/process inspection.  
**Fail-closed response:** Refuse the connection or batch; do not execute after authentication/impersonation failure; exit and require a new operator-started session.  
**Planned controls/tasks:** D-029, D-030, D-031; `TL-0310`, `TL-0311`, `TL-0312`, `TL-0313`  
**Control status:** Planned  
**Recovery/manual path:** Close stale broker/pipe state, start a new approved batch, and investigate repeated cross-user attempts.  
**Residual risk:** `RR-001`; kernel/admin compromise can subvert local IPC controls.

### AC-005 — Package source, publisher, version, architecture, or catalogue changes after approval

**Threats:** `THR-003`, `THR-013`  
**Actor:** `ACT-05`, compromised mirror/cache/source, stale catalogue, or misleading operator-facing metadata  
**Preconditions:** Resolution and approval are not bound to exact material metadata, or a “latest” lookup occurs at execution.  
**Attack path:** Source substitution, redirect/cache confusion, publisher/version/architecture/scope mismatch, rollback/stale catalogue, same-version/different-content metadata, or material change is presented as the already approved package.  
**Assets/impact:** `AST-04`, `AST-05`, `AST-06`; privileged installation of unexpected software or unsupported configuration.  
**Detection:** Exact approved source/ID/publisher/version/architecture/scope and available trust data, catalogue version/provenance, material-field diff, execution-time revalidation, and downgrade/staleness rules.  
**Fail-closed response:** Invalidate approval; block download/install; show a plain-language diff and require fresh review. No hash or source override is offered.  
**Planned controls/tasks:** D-022, D-025, D-043; `TL-0006`, `TL-0301`, `TL-0307`, `TL-0402`, `TL-0403`, `TL-0404`, `TL-0508`  
**Control status:** Planned  
**Recovery/manual path:** Refresh through an approved source, re-review provenance/licence/privacy, resolve again, and approve the new exact plan.  
**Residual risk:** `RR-002`; an approved publisher/source may itself be compromised.

### AC-006 — Privileged installer is malicious, escapes its intended lifetime, or reports false success

**Threats:** `THR-003`  
**Actor:** `ACT-05` compromised trusted package or faulty installer/backend  
**Preconditions:** An exact package has passed available source/signature checks and is allowed to run elevated.  
**Attack path:** The installer changes more than declared, spawns persistent children, hides a restart, returns success without the expected package, installs a wrong version, or leaves an ambiguous partial state.  
**Assets/impact:** `AST-05`, `AST-06`, `AST-09`; privileged compromise, persistence, false verified state, or unrecoverable device changes.  
**Detection:** Reviewed minimal catalogue, declared behavior, process/lifetime/timeout observation where supported, structured stable result, post-install exact identity/version detection, bounded launch probe, restart and finalization checks.  
**Fail-closed response:** Backend success records at most applied; absent/wrong/crashing target fails verification; ambiguous state requires review and blocks readiness.  
**Planned controls/tasks:** D-025, D-032, D-033, D-043; `TL-0006`, `TL-0403`, `TL-0404`, `TL-0405`, `TL-0406`, `TL-0407`, `TL-0408`, `TL-0409`, `TL-0609`  
**Control status:** Planned  
**Recovery/manual path:** Stop the batch, reconcile installed state, use documented vendor/removal/recovery steps if reviewed, and disable the catalogue entry during incident review.  
**Residual risk:** `RR-002`, `RR-003`; signed or curated code and non-transactional installers retain inherent risk.

### AC-007 — Traversal, junction, symlink, reparse point, or unsafe temporary path redirects access

**Threats:** `THR-006`  
**Actor:** `ACT-03`, hostile destination owner, malicious local process, or malformed imported path  
**Preconditions:** Code trusts normalized strings without constraining roots, final object identity/type, or path topology at use time.  
**Attack path:** Traversal or namespace tricks escape an allowed root; a directory/file is replaced by a junction, symlink, hard link, mount point, or other reparse target; a predictable temp name is pre-created; overwrite/delete follows the target.  
**Assets/impact:** `AST-01`, `AST-02`, `AST-06`, `AST-08`, `AST-10`; arbitrary read/overwrite, privilege escalation, data loss, or leakage.  
**Detection:** Internal-ID roots, canonical/final-handle and allowed-root checks, expected object type, reparse/link policy on every component/final object, restrictive atomic temp creation, no profile-provided arbitrary path, and race-focused tests.  
**Fail-closed response:** Abort before read/write/delete/execute, preserve existing target, clean only an owned safe temporary object, and record a sanitized path error.  
**Planned controls/tasks:** D-030, D-035; `TL-0102`, `TL-0109`, `TL-0303`, `TL-0310`, `TL-0311`, `TL-0312`, `TL-0602`, `TL-0606`, `TL-0609`  
**Control status:** Planned  
**Recovery/manual path:** Select or create a reviewed local destination; use manual transfer only after inspecting object type and permissions.  
**Residual risk:** `RR-001`; hostile filesystem filters/admin changes can race application checks.

### AC-008 — Sensitive or hostile text leaks through logs, database, reports, rendering, or support output

**Threats:** `THR-007`, `THR-012`  
**Actor:** `ACT-05`, malformed provider/backend, operator-entered text, or faulty exception/crash path  
**Preconditions:** Raw output or identifiers reach persistence/rendering before allowlisting, redaction, escaping, and size control.  
**Attack path:** A value contains credentials, usernames, paths, serials, network identifiers, URLs, control characters, markup/formula content, extreme length/nesting, or crafted error text that is logged, stored, rendered, or exported.  
**Assets/impact:** `AST-01`, `AST-02`, `AST-08`, `AST-10`; privacy loss, report injection, UI/resource denial, or support artifact contamination.  
**Detection:** Sensitive domain wrappers, structured allowlists, redact-before-persist, stable error codes, Unicode/control/markup escaping, bounded fields/records/bytes, audience-schema separation, and adversarial fixtures.  
**Fail-closed response:** Drop/reject unsafe fields, preserve a bounded sanitized diagnostic, fail rendering/export without raw fallback, and keep unknown/limitations visible.  
**Planned controls/tasks:** D-014, D-036, D-037; `TL-0005`, `TL-0104`, `TL-0115`, `TL-0207`, `TL-0308`, `TL-0407`, `TL-0604`, `TL-0605`, `TL-0606`, `TL-0609`  
**Control status:** Planned; `TL-0005` now supplies a draft classification/logging contract and synthetic adversarial fixtures, but named privacy-owner approval and runtime enforcement remain pending  
**Recovery/manual path:** Correct the source/field contract, regenerate from normalized data, inspect the preview, and securely remove any exposed artifact under incident guidance.  
**Residual risk:** `RR-006`; exported data can be mishandled after release.

### AC-009 — UAC decline, network/power loss, reboot, full disk, or process death creates ambiguous state

**Threats:** `THR-005`, `THR-008`  
**Actor:** `ACT-06` interruption, operator cancellation, or killed UI/broker/backend  
**Preconditions:** A journaled action is near a mutation/commit boundary or a restart is pending.  
**Attack path:** Interruption occurs before/after backend mutation, result receipt, journal commit, attachment write, or verification; a retry blindly repeats work or a rollback/success is inferred.  
**Assets/impact:** `AST-07`, `AST-08`, `AST-09`; duplicate mutation, lost history, corrupt split state, false completion, or unusable device.  
**Detection:** Preflight space/power, transition checkpoints, process/operation correlation, exactly-once terminal recording, actual-state reconciliation, pending-restart evidence, retry limits, and failure injection at each boundary.  
**Fail-closed response:** UAC decline performs no authorized mutation; all other ambiguity becomes failed/requires review until actual state is re-observed. Applied is never verified by inference.  
**Planned controls/tasks:** D-032, D-033; `TL-0308`, `TL-0309`, `TL-0311`, `TL-0313`, `TL-0405`, `TL-0408`, `TL-0409`, `TL-0503`, `TL-0505`, `TL-0509`, `TL-0510`  
**Control status:** Planned  
**Recovery/manual path:** Reopen the job, re-observe package/update/filesystem/restart state, continue only an eligible idempotent action or require documented manual recovery.  
**Residual risk:** `RR-003`; some mutations have no reliable rollback or queryable intermediate state.

### AC-010 — Malformed, stale, localized, contradictory, or deceptive provider output becomes evidence

**Threats:** `THR-004`, `THR-012`  
**Actor:** `ACT-04`, faulty provider/API, hostile XML/structured source, or race with changing device state  
**Preconditions:** Inventory or verification consumes Windows/CIM/API/temp-report output.  
**Attack path:** Oversized/deep/entity-expanding input, invalid units/ranges, duplicate/conflicting records, localized text, stale pre-action result, timeout, access denied, or compromised OS output is normalized as a valid pass.  
**Assets/impact:** `AST-03`, `AST-08`, `AST-09`; wrong disposition, missed blocker, privacy leakage, resource exhaustion, or false verified state.  
**Detection:** Structured API preference, secure parser settings, fixed operations, type/range/count/depth/byte/time bounds, provenance/timestamp, explicit observed/inferred/not-available/human-confirmed classes, conflict policy, fresh verification source.  
**Fail-closed response:** Record not available/failed/contradictory evidence and sanitized error; never pass a required condition; allow safe provider rerun.  
**Planned controls/tasks:** D-015, D-018; `TL-0104`, `TL-0105`, `TL-0106`, `TL-0107`, `TL-0108`, `TL-0109`, `TL-0110`, `TL-0111`, `TL-0112`, `TL-0202`, `TL-0506`, `TL-0507`  
**Control status:** Planned  
**Recovery/manual path:** Retry bounded collection, use a reviewed independent source or attributable human test, and leave unsupported evidence unknown.  
**Residual risk:** `RR-001`; a compromised OS and incomplete hardware APIs can produce false negatives.

### AC-011 — Journal/store tampering, corruption, concurrency, or migration rewrites history

**Threats:** `THR-005`  
**Actor:** `ACT-03`, `ACT-04`, faulty migration, concurrent process, or full-disk interruption  
**Preconditions:** Local SQLite, attachments, snapshots, or checkpoints contain durable workflow authority/evidence.  
**Attack path:** Invalid direct edits, rollback to older snapshots, same-version/different-content data, partial migration, concurrent writers, DB/attachment split-brain, invalid state transition, deletion of prior attempts, or newer/corrupt schema is silently accepted.  
**Assets/impact:** `AST-07`, `AST-08`, `AST-09`; repudiation, false approval/verification, lost audit history, unsafe retry, or data loss.  
**Detection:** Restrictive ACL, schema/migration version and content identity, transaction/integrity checks, single-writer/concurrency policy, allowed state-transition table, append/history links, corruption/newer-schema detection, attachment reconciliation.  
**Fail-closed response:** Refuse unsafe open/migration/transition, preserve original files, create no new “passed” record, and surface export/recovery/manual-review guidance.  
**Planned controls/tasks:** D-028, D-032; `TL-0102`, `TL-0103`, `TL-0205`, `TL-0308`, `TL-0309`, `TL-0313`, `TL-0408`, `TL-0510`  
**Control status:** Planned  
**Recovery/manual path:** Work from a verified backup/export or reviewed recovery copy; never rewrite historical evidence to match current policy.  
**Residual risk:** `RR-001`; a local administrator can alter both primary and local backup records.

### AC-012 — Windows Update installs an unapproved class or loops/misreports across restarts

**Threats:** `THR-003`, `THR-008`, `THR-009`  
**Actor:** `ACT-05`, misconfigured update/WSUS service, changing update graph, faulty API/backend, or `ACT-06` interruption  
**Preconditions:** The product scans/downloads/installs through Windows Update and must converge across restart phases.  
**Attack path:** Stale scan or policy change includes an unapproved update class/identity; supersedence causes an unbounded loop; result/reboot is misclassified; offline/network/process/power failure leaves partial non-rollback state; driver/firmware/recovery implications are hidden.  
**Assets/impact:** `AST-05`, `AST-07`, `AST-09`, `AST-12`; unauthorized system change, unusable device, lost recovery access, false current/verified result, or endless maintenance.  
**Detection:** Structured WUA data, exact approved classes/identities, fresh scans, finite pass/restart policy, stable result codes, pending-reboot evidence, preflight, checkpoint/reconciliation, explicit driver policy, and an unconditional firmware-flashing exclusion.  
**Fail-closed response:** Do not install outside approval. Firmware flashing is blocked and routed to a documented manual process outside Core; authorizing it would require a formal boundary amendment, not another runtime class. Offline/service/error/partial state remains recoverable or requires review; no screen-scrape or infinite retry; handover remains blocked.  
**Planned controls/tasks:** D-025, D-032, D-033, D-034; `TL-0503`, `TL-0504`, `TL-0505`, `TL-0507`, `TL-0509`, `TL-0510`, `TL-0609`  
**Control status:** Planned  
**Recovery/manual path:** Resume from a journaled checkpoint after fresh scan/restart evidence, or use documented Windows/manual recovery under human review.  
**Residual risk:** `RR-002`, `RR-003`; update service trust and non-transactional mutations remain.

### AC-013 — False sanitization/ownership evidence or finalization omission produces false ready/handover

**Threats:** `THR-010`  
**Actor:** `ACT-01` mistake/dishonesty, `ACT-04` deceptive OS state, or incomplete external evidence  
**Preconditions:** Workflow intake/finalization relies partly on human/external evidence and bounded detection.  
**Attack path:** Unknown/failed sanitization is recorded as verified; management, activation, firmware-password, anti-theft, unexplained admin/remote-support, credential, Wi-Fi, test-media, or pending-restart evidence is missing or overridden; the UI advances anyway.  
**Assets/impact:** `AST-01`, `AST-02`, `AST-03`, `AST-09`; donor/workshop data exposure, ownership violation, unsupported device handover, or false safety claim.  
**Detection:** Attributable sanitization evidence and policy version, explicit unknown/failed classes, read-only ownership/management indicators, mandatory finalization rules, named human confirmations/exceptions, fresh verification, gate evaluator outside UI navigation.  
**Fail-closed response:** Block preparation, ready disposition, sign-off, or handover; route to repair/human review/do-not-deploy. Never offer erase, unlock, activation, firmware, MDM, Autopilot, anti-theft, or ownership bypass.  
**Planned controls/tasks:** D-007, D-009, D-035; `TL-0103`, `TL-0107`, `TL-0110`, `TL-0112`, `TL-0506`, `TL-0601`, `TL-0602`, `TL-0607`, `TL-0609`  
**Control status:** Planned  
**Recovery/manual path:** Obtain valid external evidence or authorized ownership resolution outside ThirdLife; otherwise stop deployment.  
**Residual risk:** `RR-005`; Core cannot independently prove the truth of external sanitization/legal ownership.

### AC-014 — Oversized input or work exhausts memory, disk, CPU, handles, or time

**Threats:** `THR-004`, `THR-012`  
**Actor:** `ACT-05`, faulty provider/backend, hostile archive/report data, or accidental large fixture  
**Preconditions:** Collection, parsing, IPC, persistence, rendering, archive, retry, or concurrency lacks strict bounds.  
**Attack path:** Excessive depth/count/bytes, decompression/expansion, endless progress/events, repeated retries, huge raw output, or too much concurrency blocks UI/cancellation, fills disk, or corrupts partial state.  
**Assets/impact:** `AST-07`, `AST-08`, `AST-09`, `AST-10`; denial of service, lost recovery, corrupt output, or disabled security/accessibility behavior.  
**Detection:** Versioned byte/depth/count/time/retry/concurrency/cache/temp limits, streaming/paging, disk preflight/reserve, cancellation, progress rate bounds, resource metrics, and small/normal/large/malformed/adversarial fixtures.  
**Fail-closed response:** Stop before mutation where preflight fails; otherwise persist a truthful failed/requires-review checkpoint and clean bounded temporary output without weakening safety/accessibility checks.  
**Planned controls/tasks:** D-030, D-037; `TL-0105`, `TL-0112`, `TL-0305`, `TL-0310`, `TL-0312`, `TL-0503`, `TL-0510`, `TL-0604`, `TL-0606`, `TL-0707`  
**Control status:** Planned  
**Recovery/manual path:** Reduce reviewed input/workload, free space safely, resume from checkpoint, or use the documented manual path.  
**Residual risk:** `RR-008`; workloads beyond measured/published bounds can still exhaust a supported device or leave recoverable partial state.

### AC-015 — B1 accidentally gains a sibling dependency or shared integration surface

**Threats:** `THR-011`  
**Actor:** `ACT-10` developer/maintainer convenience, speculative architecture, or misleading “integration readiness” request  
**Preconditions:** A task adds a sibling package/profile/test, repository/build/runtime/service/schema/data edge, shared SDK/plugin framework, or waits on another active project.  
**Attack path:** Private/unstable behavior becomes required for Core, cross-project data is indexed, uninstall can affect unrelated content, or release schedules become coupled before frozen releases exist.  
**Assets/impact:** `AST-01`, `AST-08`, `AST-11`; privacy/ownership violations, supply-chain expansion, unavailable standalone product, and hidden unowned infrastructure.  
**Detection:** The verified initial governance/naming/task-graph baseline plus planned dependency/reference/catalogue/profile scans, code/data-access review, generic synthetic B1 fixtures, and release boundary review.  
**Fail-closed response:** Reject/remove the edge; record only a non-binding future note with a manual fallback if genuinely useful; continue the project-local task.  
**Planned controls/tasks:** D-048, D-049, D-050, D-053, D-055; `TL-0003`, `TL-0009`, `TL-0301`, `TL-0302`, `TL-0303`, `TL-0304`, `TL-0508`, `TL-0703`, `TL-0709`  
**Control status:** `TL-0003` verified the initial governance documents, naming rules, and task graph; comprehensive product/dependency/data/release audits remain planned.  
**Recovery/manual path:** Restore standalone behavior and use generic public/synthetic inputs; no sibling checkout or temporary private-data bridge.  
**Residual risk:** `RR-007`; future external projects can disregard published boundaries, but Core must expose no enabling private interface.

### AC-016 — Future B4 adapter misuses private state, “latest” behavior, or mandatory coupling

**Threats:** `THR-011`  
**Actor:** `ACT-08` future integrator or an over-broad compatibility proposal  
**Preconditions:** A future project has a frozen Core release/interface but attempts deeper integration.  
**Attack path:** It reads/writes the private DB/log/job store, assumes active-branch or “latest” compatibility, requires a background service/shared account/schema, becomes mandatory, omits a manual fallback, or allows sibling uninstall/data access.  
**Assets/impact:** `AST-01`, `AST-08`, `AST-11`, `AST-12`; data leakage/corruption, lock-in, synchronized failure, and broken standalone lifecycle.  
**Detection:** Exact frozen version/hash/interface/sample compatibility cut, public-behavior-only review, data/dependency trace, optionality/disablement/manual-fallback test, and portfolio-owner review.  
**Fail-closed response:** Disable/reject the future integration and retain the normal standalone/manual path; open a formal portfolio decision rather than redesigning Core privately.  
**Planned controls/tasks:** D-049, D-050, D-051, D-052, D-053; `TL-0703`, `TL-0706`, `TL-0708`, `TL-0709`, `TL-0710`  
**Control status:** Planned; this case is a boundary assertion, not an adapter design or B1 implementation.  
**Recovery/manual path:** Install/launch/use Core independently and exchange only documented user-selected standard artifacts when later approved.  
**Residual risk:** `RR-007`; enforcement outside the repository depends on future portfolio governance.

### AC-017 — Operator is misled into approving unsafe or unintended work

**Threats:** `THR-013`  
**Actor:** `ACT-05` misleading metadata/installer identity, social engineer, or confusing product presentation  
**Preconditions:** Approval hides or ambiguously presents source, publisher, version, scope, privilege, network, disk, restart, rollback, verification, essentiality, or changed metadata.  
**Attack path:** The operator approves a batch believing it is smaller/different; a UAC prompt cannot be tied to the preview; optional/essential meaning is obscured; a stale approval silently survives change.  
**Assets/impact:** `AST-05`, `AST-06`, `AST-07`; authorized-but-unintended privileged mutation and weakened accountability.  
**Detection:** Complete accessible plain-language preview plus bounded expert metadata, explicit optional selection, exact plan hash, material diff/reapproval, broker correlation, and operator/pilot review.  
**Fail-closed response:** No hidden/default execution; ambiguous/missing impact or changed material blocks approval/execution; UAC decline preserves the job without mutation.  
**Planned controls/tasks:** D-020, D-025, D-029, D-039; `TL-0304`, `TL-0305`, `TL-0306`, `TL-0307`, `TL-0313`, `TL-0403`, `TL-0508`  
**Control status:** Planned  
**Recovery/manual path:** Decline, inspect the full plan/source, obtain human/security review, and approve only a corrected exact plan.  
**Residual risk:** `RR-004`; an authorized operator can still knowingly approve harmful trusted content.

### AC-018 — Export differs from preview or targets an unsafe object

**Threats:** `THR-006`, `THR-007`  
**Actor:** `ACT-03`, hostile destination owner, concurrent job mutation, or faulty exporter  
**Preconditions:** Preview and export are not digest-bound, or final destination checks occur only on the original path string.  
**Attack path:** Content changes after preview; a disallowed field/raw file is included; destination becomes a link/reparse target; existing file is overwritten; removable/share storage fills; partial archive remains; path/details leak to logs.  
**Assets/impact:** `AST-01`, `AST-02`, `AST-08`, `AST-10`; privacy disclosure, arbitrary overwrite, misleading consent, corrupt artifact, or machine-path leakage.  
**Detection:** Frozen preview manifest/content digest, audience field/file allowlist, revalidation immediately before final write, final-object/type/reparse/ACL/capacity/overwrite checks, archive byte/file/count bounds, atomic placement and partial cleanup.  
**Fail-closed response:** Abort export and leave existing target unchanged; remove or clearly identify an owned partial file; keep the exact preview for retry; persist only sanitized export metadata.  
**Planned controls/tasks:** D-036, D-037; `TL-0005`, `TL-0604`, `TL-0605`, `TL-0606`, `TL-0607`, `TL-0609`, `TL-0703`  
**Control status:** Planned; `TL-0005` now supplies the draft support allowlist and preview/redaction contract, but named privacy-owner approval and runtime export controls remain pending  
**Recovery/manual path:** Re-preview after any data change and select a safe reviewed destination or manual transfer process.  
**Residual risk:** `RR-006`; subsequent handling of a valid export is outside product control.

### AC-019 — Dependency, build input, or release artifact is substituted or lacks provenance

**Threats:** `THR-014`  
**Actor:** `ACT-09`, compromised upstream, malicious contributor, or faulty build/release process  
**Preconditions:** Repository verification, packaging, or release freeze consumes dependency/action/tool metadata, binaries, source revision, licence/SBOM records, signatures, or generated artifacts.  
**Attack path:** A dependency/source/action/version is confused or changed without lock review; a binary/model/asset lacks provenance/licence; a build uses an unpinned or different toolchain; an artifact is replaced after verification; SBOM/hash/signature/development labeling no longer describes the distributed bytes.  
**Assets/impact:** `AST-05`, `AST-11`; compromised product/build, undisclosed dependency risk, redistribution violation, irreproducible evidence, or false frozen-release identity.  
**Detection:** Exact SDK/tool/direct/transitive resolution, source mapping and locks, change review, owner/licence/redistribution record, SBOM and vulnerability review, clean-build provenance, exact source revision, artifact hashes/signature or explicit development labeling, and gate comparison of reviewed bytes.  
**Fail-closed response:** Reject restore/build/package/release or quarantine the candidate; never silently fetch “latest,” weaken a lock/trust check, substitute a source, or publish mismatched evidence.  
**Planned controls/tasks:** D-043; `TL-0002`, `TL-0006`, `TL-0610`, `TL-0704`, `TL-0706`, `TL-0708`, `TL-0709`, `TL-0710`  
**Control status:** `TL-0002` verified the pinned SDK, central direct-package versions, locked restore, and the then-current optional Windows workflow configuration. Under D-063, authoritative runtime evidence is produced on the active Codex machine; remote workflow results are non-authoritative convenience signals. Ownership/licence/SBOM, packaging provenance, lifecycle, and freeze checks remain planned.  
**Recovery/manual path:** Restore the reviewed source/lock/toolchain, investigate the changed input, regenerate every affected artifact/evidence item, and repeat independent verification before a new candidate.  
**Residual risk:** `RR-002`; an approved upstream or build environment may itself be compromised.

## Structured review checklist

The security owner should review the exact commit and confirm:

- actors, assets, every trust boundary, and flows `F-01` through `F-19`, including release supply, are complete enough for v0.1 design work;
- all High threats have a binding decision and at least one real roadmap task mapping;
- every broker, runtime-package, dependency/build, and release-supply abuse case maps to concrete future tests or human inspection;
- no future task mapping is represented as implemented or verified; any verified subset names a completed task and remains narrowly scoped;
- sanitization and ownership remain evidence/blocker boundaries, never runtime bypass behavior;
- early cross-project coupling and future B4 misuse are modeled without a B1 runtime edge or adapter design;
- proposed residuals `RR-001` through `RR-008` have an owner decision, conditions, review trigger, and blocking gate; and
- approval records name, role, date, result, exact commit/reference, and residual decisions.

**Review result:** Approved. Named security-owner approval is recorded for the exact model revision.
