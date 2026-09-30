# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-08-29  
**Snapshot preparation time:** 2026-08-29T08:53:19+02:00  
**Bundle baseline:** 0.3.1  
**Portfolio baseline:** ThirdLife Software Portfolio v2.1  
**Current milestone:** M1 — Audit-only vertical slice — active  
**Current task:** `TL-0106` — Implement device identity, CPU, memory, and architecture inventory — `done`  
**Action state:** implementation, independent review, deterministic/captured/active Targeted verification, governance synchronization, and publication are complete; `TL-0107` is next dependency-ready

## Executive project state

M0 is complete, and M1 tasks `TL-0101` through `TL-0106` are complete. ThirdLife Setup Core now has its governed persistence, dependency/licence, evidence, privacy-safe diagnostic, provider-normalization, and first concrete Windows inventory foundations. `TL-0107` is the next dependency-ready task; OS lifecycle, activation, storage, battery, firmware/security, network, and cross-provider snapshot work remain separately owned by `TL-0107` through `TL-0112`.

TL-0106 implements a fixed, local, read-only Windows system-inventory provider. A bounded `GetSystemFirmwareTable('RSMB')` source supplies SMBIOS device identity, chassis, and populated central-processor facts; fixed APIs supply physically installed memory and logical processor count; and `.NET RuntimeInformation.OSArchitecture` supplies the underlying operating-system architecture without confusing x64 process emulation on Arm64 with the host architecture. The provider adds no package, shell, WMI, registry, file, network, service, elevation, or mutation surface. The exact approved 28-component dependency/licence matrix therefore remains unchanged, and no Full dependency or milestone trigger arose.

Privacy handling is deliberately asymmetric. The protected normalized workshop observation may contain a full device serial, because that is the only approved audience for it. Ordinary logging and support schemas currently authorize no serial—not even a suffix—so implicit evidence stringification is now value-silent and no diagnostic integration was added. Raw firmware bytes are capped at 1 MiB, kept transient, parsed only for the allowlisted fields, and zeroed on disposal. The public fixture is wholly synthetic. Captured replay uses only the already-sanitized reference profile's published x64 architecture and logical-count facts; identity, raw SMBIOS, processor text, chassis, and exact memory input remain omitted or unavailable.

Verification is complete. The Targeted Inventory set passed 50/50 without the active category; the Core evidence set passed 79/79; the exact captured replay and `FI-001` concrete failure scenario each passed 1/1; and the separately filtered active-machine smoke passed 1/1 under a standard-user token without printing identity. A Release solution build completed with zero warnings/errors. The wholly synthetic fixture covers multiple fictional manufacturers, x86/x64/Arm64, 4/8/16 GiB, multiple processors, missing/placeholders, access denial, malformed/oversized input, conflicts, cancellation, cleanup, serialization, processor filtering, and architecture emulation. These results apply only to the represented inputs and this one active machine.

Two downstream policy decisions are intentionally unresolved rather than guessed. First, TL-0106 records CPU manufacturer/model but does not produce `processor.windows_11_eligibility`; real policy evaluation must remain unknown/blocking until a separately governed eligibility source or attributable human confirmation is assigned. Second, TL-0107 also owns architecture evidence; TL-0112 must define a conflict/freshness rule and may not silently select the first or last architecture observation. Windows 10 remains audit-only collection scope, not a normal-ready or release-support claim.

## User outcome

- A standard-user Windows 11 run can now collect bounded manufacturer, model, workshop-only full serial, inferred device type, populated central-processor manufacturer/model, logical processor count, native OS architecture, and physically installed memory evidence.
- Missing, placeholder, malformed, conflicting, denied, unavailable, timed-out, and cancelled facts remain explicit unknown/failure evidence; unrelated valid facts may remain attributable without turning the run into a pass.
- The active operation is read-only and local. It starts no shell, service, child process, network request, WMI query, registry access, file write, elevation, or mutation.
- Full serial remains usable by a future protected workshop-record projection while implicit rendering, ordinary diagnostics, support data, fixtures, and active test output remain serial-free.
- Deterministic synthetic and captured-profile replay tests are independently repeatable, and the active observation is clearly separate from them. None certifies a manufacturer, processor family, memory minimum, firmware implementation, or another machine.
- Policy remains separate from observation. In particular, the provider does not infer Windows 11 processor eligibility, legal ownership, authenticity, health, reliability, readiness, or support status.

## Implemented behavior

- `SystemInventoryProvider` declares nine closed evidence keys, standard-user privilege, no network, Windows 10/11 collection scope, 250 ms expected duration, and a two-second cooperative timeout.
- `WindowsSystemInventorySource` uses only fixed local read APIs: raw SMBIOS, installed memory, logical processor count, and emulation-aware .NET OS architecture. A public caller can construct only the reviewed default source; injected sources and origins remain internal test seams.
- The SMBIOS parser validates the raw header and declared length, structure headers/handles/terminators, duplicate Type-1/handles, end marker, cancellation, and total 1 MiB/1,024-structure/eight-populated-processor bounds. Field byte/character limits apply only to consumed fields, so unrelated valid long strings do not discard useful facts.
- Only Type-1 manufacturer/model/serial, Type-3 chassis category, and populated central Type-4 manufacturer/model fields are decoded. UUIDs, firmware handles, socket labels, processor IDs, asset tags, other serials, and unrelated strings are ignored.
- Placeholder, invalid UTF-8, control/injection-like, overbound, conflicting, unsupported-architecture, zero/overflow memory, and impossible logical-count values become typed unavailable evidence; no truncation or raw fallback is used.
- Owned firmware bytes are transient and cleared on success, parse failure, cancellation, or source-side abort. The native calls remain cooperatively cancellable between calls; no claim of forcible in-flight kernel-call termination is made.
- `EvidenceValue.ToString()` is value-silent. Protected JSON serialization still carries a normalized value for its approved workshop-record path, while accidental interpolation of an evidence value or containing observation yields only the fixed placeholder.
- The reference-profile replay preserves `imported_record` provenance and distinguishes the profile source timestamp from replay/import collection time. The public synthetic fixture remains wholly synthetic and independently classified.
- `IInventoryProvider` exposes an immutable descriptor and `ObserveAsync(CancellationToken)`.
- `InventoryProviderDescriptor` accepts only closed privilege, network, supported-OS, origin, failure-definition, and evidence-definition types. Inventory network use is fixed to `None` at this boundary.
- Evidence definitions fix each key, scalar kind, optional invariant unit, bounded source-reference length, and cardinality before collection begins.
- Provider candidates are constructed only as observed, inferred, not available, or not applicable; source-level limitations are distinct from runner-only timeout, cancellation, contract, cleanup, and execution limitations.
- `ProviderReadResult` has closed collected, unavailable, access-denied, invalid-data, cleanup-incomplete, and failed statuses and can retain bounded valid partial evidence.
- The runner validates the descriptor/result agreement, type, key, unit, count, source-reference uniqueness, total bounds, and canonical output ordering.
- Timeout and cancellation are first-class outcomes. Cancellation before provider invocation is system-generated evidence and never falsely claims provider observation.
- Exceptions are classified by type only. Normalization never reads exception message, `ToString()`, stack, source, `Data`, or inner-exception text; hostile exception accessors are covered by tests.
- Later provider-task faults are observed after a timeout race so they do not become unobserved background failures.
- All normalized observations are `WORKSHOP_RESTRICTED`, including synthetic-origin observations. Synthetic provenance remains visible but cannot downgrade privacy classification.
- Active-machine, captured-sample, synthetic-fixture, and system-generated provenance remain distinct. Captured-sample provenance does not itself sanitize data; the owning provider task must supply reviewed sanitization.
- An ordinary failed provider must leave at least one declared requirement unavailable. Cleanup-incomplete may retain all otherwise valid facts while the separate provider-status evidence remains unavailable.
- Public contracts have no setters, mutator methods, arbitrary command/argument/script/executable/registry-path/URL fields, or concrete Windows API types.

## Decisions and practical implications

### 1. Observation is not policy

The provider boundary records facts, uncertainty, source, and limitations; it does not issue `Pass`, `Fail`, readiness, ownership, compatibility, health, or security verdicts. This keeps evidence history stable when policy later changes and prevents a weak provider from granting approval. The practical consequence is that `TL-0202` and later policy tasks must explicitly map normalized evidence to versioned decisions. A completed provider call means only that collection and normalization completed—it never means the machine passed a requirement.

### 2. Provider behavior is declared before execution

Privilege, expected duration, timeout, network use, supported OS, evidence schema, and failure-status key are immutable descriptor data. This makes a provider's claimed operating envelope reviewable and lets the runner reject invalid metadata or undeclared result facts. It does not inspect whether adapter internals actually elevate, access the network, mutate state, block before returning, or ignore cancellation. Those behaviors require provider-specific architecture checks and runtime tests. The current descriptor can declare only `NetworkUse.None`; any adapter that nevertheless accesses a network is defective and outside the approved contract. Adding a network-dependent inventory source would be a new governed design decision, not an ordinary implementation detail.

### 3. Failure remains visible and cannot resemble success

Failure produces a separate unavailable provider-status observation plus unavailable values for affected required facts. Valid unrelated partial facts may survive with their original source attribution, which avoids throwing away useful evidence, but a non-cleanup failure cannot preserve every requirement as available. This gives downstream policy enough information to stop safely without treating the entire snapshot as empty. It also means each concrete provider must identify its declared requirements accurately; overly broad or overly narrow definitions would distort which evidence becomes unknown.

### 4. Cleanup failure is intentionally distinct

`CleanupIncomplete` is not treated like an acquisition failure. A provider such as the future battery-report adapter may obtain valid bounded facts and then fail to remove a verified temporary artifact. Those facts can remain available while the provider-status evidence records cleanup failure and requires review. The implication is that cleanup integrity remains visible without rewriting successfully acquired facts, but `TL-0109` must prove its temporary-file identity, bounds, deletion, cancellation, and recovery behavior before relying on this outcome.

### 5. Privacy classification cannot be downgraded by provenance

Every normalized observation is Workshop Restricted. Active, captured, synthetic, and system-generated origins answer where evidence came from; they do not answer who may see it. Synthetic evidence therefore remains marked synthetic but is not automatically public. Future logging, persistence, reports, and support exports must use their separately approved projections and may not expose normalized evidence merely because a fixture produced it.

### 6. Timeouts are cooperative, not forcible process termination

The runner bounds an asynchronously returned provider operation and signals cancellation, but an in-process provider can still block synchronously before returning its `ValueTask` or ignore cancellation after it starts. TL-0105 makes this limitation explicit instead of claiming hard preemption. Each Windows provider must yield promptly, honor the token, bound its underlying API call, and prove cleanup. Moving a provider out of process or adding a bounded cleanup/join grace would be a later architecture decision if a concrete API cannot satisfy cooperative behavior.

### 7. Evidence shape is closed and bounded

Keys, scalar types, invariant units, counts, total evidence, and source references are checked against the descriptor, and normalized output is canonically ordered. This prevents arbitrary payload growth and duplicate ambiguity and supplies no dedicated raw-output field. It does not semantically inspect or sanitize a bounded text or enum evidence value; concrete provider tasks must allowlist factual keys and minimize every emitted value so policy-like or raw source content cannot be smuggled through a generic scalar. Multi-instance domains such as disks and processors can still be represented through declared cardinality and distinct source references. Results also contain generated evidence IDs and collection timestamps. `TL-0112` must either inject controlled identity/time sources or define and test a canonical semantic comparison that explicitly excludes generated identity and controlled timestamps; whole result objects are not currently deterministic across reruns.

### 8. Public API neutrality does not prohibit reviewed Windows adapters

The public contract exposes no Windows API types, which makes it deterministic and fakeable. The safety guard is scoped to the contract/normalization surface rather than banning all future use of `System.Management`, fixed process invocation, or verified temporary-file cleanup. This is important because later tasks may need reviewed fixed Windows mechanisms. Each adapter must receive its own no-mutation, fixed-operation, bounded-output, sanitization, and cancellation checks; the common contract is not blanket approval for any Windows API.

### 9. Sanitized errors are deliberately non-diagnostic at the raw-exception level

Only stable error categories and predefined recovery actions cross the sanitized-error boundary. Ordinary exception messages, stacks, sources, data dictionaries, inner-exception text, and local paths from thrown exceptions are not retained. The contract has no dedicated raw-provider-output error channel, but TL-0105 does not prove semantic sanitization of arbitrary bounded text evidence; that remains a provider-specific obligation. If a future provider needs additional operator guidance, it must introduce a reviewed typed error category rather than forwarding source text.

### 10. Fixed native/BCL sources are preferable here to a new management dependency

TL-0106 uses the smallest reviewed local read surface that supplies its facts. This avoids a new package, localized command parsing, WMI query construction, provider service assumptions, and renewed licence-matrix approval. The implication is not that native APIs are universally superior: the project now owns a careful SMBIOS parser and must keep its bounds and regression corpus. If a later field cannot be obtained reliably through this surface, that task must justify its own structured source rather than widening TL-0106 or adding a generic query channel.

### 11. Operating-system architecture, not process architecture, is the evidence fact

Architecture policy concerns the Windows installation/host, not whichever binary architecture happens to execute under emulation. `GetNativeSystemInfo` can report emulated x86/x64 on Arm64, so the final source uses `.NET RuntimeInformation.OSArchitecture`, whose supported Windows behavior is designed to represent the underlying OS despite process emulation. Tests explicitly distinguish an x64 process example from an Arm64 OS. This prevents an emulated process from making an Arm64 system appear x64, but TL-0107 and TL-0112 still own reconciliation with their later architecture observation.

### 12. Full serial is retained only as protected evidence, never as a convenient diagnostic

The approved privacy contract permits a full serial in the protected workshop record because workshops may need to distinguish devices. That permission does not extend to ordinary logs, test failure formatting, support export, filenames, or public captured samples. The implementation therefore retains the normalized value for a future reviewed protected projection but makes generic evidence stringification value-silent. No suffix is emitted because the current log/support allowlists approve none. Adding a suffix later would be a privacy-schema decision with preview and projection tests, not a formatter tweak.

### 13. Parser strictness is field-local where compatibility requires it

Structural corruption—invalid lengths, duplicate handles, missing terminators/end marker, impossible record counts, or overlarge tables—invalidates SMBIOS acquisition because record boundaries cannot be trusted. A malformed consumed field, by contrast, becomes unavailable while independent facts survive. Long or numerous unreferenced strings in an otherwise valid SMBIOS structure are not globally rejected merely because TL-0106 would reject that size for a field it publishes. This distinction preserves fail-closed handling without turning a narrow output bound into an unsupported firmware-compatibility claim.

### 14. Processor facts are observations, not eligibility

Only populated Type-4 records whose Processor Type is `Central Processor` contribute processor evidence; math, DSP, video, other, unknown, and empty socket records are ignored. Evidence is ordered by internal handle but published with generated ordinals, so handles/socket identity do not become durable data. Manufacturer/model and logical count may help a later reviewed eligibility source, but TL-0106 deliberately emits no `processor.windows_11_eligibility`. Until an owning task supplies that source or human confirmation, a policy requiring the key must remain unknown and blocking.

### 15. Captured replay and active observation prove different things

The deterministic captured replay imports only x64 architecture and logical count from the exact sanitized `REF-CODEX-001` document. It does not relabel synthetic constants, replay identity, or invent exact memory input from a rounded display value. The active smoke separately exercises the real provider and checks its non-identity scalar facts and metadata without printing identity. Together they prove repeatable import semantics and one current host execution, not manufacturer coverage. Keeping them separate prevents a successful host run from masquerading as a portable fixture or hardware matrix.

## Git state

| Field | Verified value |
|---|---|
| Remote | `origin` → GitHub repository `PikkuJanne/ThirdLife` |
| Branch | `codex/tl-0106-system-inventory` |
| Starting commit | `6bfbbe8f51024065370c61cad9745a4f12db2e36` — published TL-0105 completion |
| Task-start checkpoint | `964fe76fcd492da441bc3bd1bd5307342cd32a86` — published task selection, design, baseline, and governed Quick |
| Implementation checkpoint | TL-0106 provider, fixtures, tests, privacy hardening, and Targeted evidence; exact published commit is recorded in final task evidence |
| Completion synchronization | Governed Quick, final commit/push/fetch, and local/upstream equality verified at handoff |
| History handling | No reset, rebase, force push, or history rewrite |

The configured SSH remote rejects unattended authentication in this environment. Publication uses the governed process-scoped HTTPS `insteadOf` bridge without changing the configured remote or exposing credentials.

The unrelated untracked `ThirdLife_Two-Team_Software_Portfolio_Roadmap_v2.1.docx` predates this task and remains untouched and unstaged.

## Verification evidence

| Scope | Result | Duration / limitation |
|---|---|---|
| Pre-change TL-0106 Inventory baseline | Passed 22/22; 0 failed/skipped | 268 ms reported / 1.945 s wall; direct active machine; no provider invocation or host mutation |
| TL-0106 task-start governed Quick | Passed 187/187; bundle/repository validators passed | 107.125 s tests; process-scoped execution-policy bypass used after the host rejected the first script launch; no system policy changed |
| TL-0106 deterministic Inventory Targeted | Passed 50/50; 0 failed/skipped | 2 s reported / 5.906 s wall; 2026-08-29T08:48:55.1718636–08:49:01.0780337+02:00; excludes separately filtered active category |
| Core evidence regression set | Passed 79/79; 0 failed/skipped | 124 ms reported / 1.706 s wall; value-silent implicit rendering included |
| Exact sanitized captured-profile replay | Passed 1/1 | 60 ms reported / 1.691 s wall; exact `REF-CODEX-001` profile bytes, x64 and logical count only; identity/raw SMBIOS/exact memory input omitted |
| Exact `FI-001` system-inventory subset | Passed 1/1 | 58 ms reported / 1.689 s wall; unavailable/access-denied/malformed injected paths; no real permission/provider mutation |
| Unelevated active-machine smoke | Passed 1/1 | 63 ms reported / 1.750 s wall; 2026-08-29T08:50:26.3411602–08:50:28.0907197+02:00; standard-user token, identity-silent output, one-machine claim only |
| Strict scoped formatting and diff check | Passed | `dotnet format ... --verify-no-changes` on every changed C# file plus `git diff --check` |
| Inventory production static review | Passed | Exactly three fixed Kernel32 imports plus `.NET` OS architecture; no write, file, mutation, WMI, registry, network, process, or generic shell surface |
| Release solution build | Passed; 0 warnings/errors | 5.07 s build / 5.387 s wall; direct host |
| Completion-candidate governed Quick | Passed 187/187; bundle/repository validators passed | 107.128 s tests / 111.994 s complete; 2026-08-29T08:51:26.7526576–08:53:18.7464849+02:00 |
| Final synchronized governed Quick | Passed | Same exact gate rerun after recording the candidate result and refreshing the manifest; final timing reported at handoff |
| Full | Not triggered | No milestone/release gate, dependency, migration, privilege, package/update, installer/lifecycle, or broad shared-boundary trigger |
| Extended | Not triggered | No named resource, interruption, hosted-environment, physical, or hardware scenario trigger |

The task-required runtime proof is the Targeted provider and Core set plus the separately filtered active smoke. The governed Quick remains a repository/governance continuity check. Full and Extended are not substituted by these runs and were not triggered.

## Defect handling and independent review

Three independent read-only reviews examined C#/interop behavior, adversarial SMBIOS parsing/privacy, and exact TL-0106 contract fit. Every reported blocker was corrected and re-reviewed:

1. Unavailable numeric source values initially used a generic default value that looked present and became invalid data. Source/field wrappers now carry an explicit presence bit; the focused case and full Targeted set passed.
2. Generic record stringification could expose a full serial. `EvidenceValue` now renders only `[evidence_value]`, with Core and provider-specific interpolation regressions while protected JSON serialization remains intact.
3. A sanitized active capture was incorrectly mixed into a `PUBLIC_REFERENCE` container labelled wholly synthetic. It was removed; the JSON fixture is now entirely synthetic, while captured replay reads only the separately governed sanitized reference profile.
4. A global 256-byte/64-string SMBIOS rule rejected valid unrelated strings. Structural parsing remains bounded by the 1 MiB table, while published-field byte/character limits are applied only when that field is consumed.
5. Serial placeholder separators were inconsistent. Space, dash, underscore, dot, slash, and hash now participate consistently in empty/all-zero/all-`F` detection.
6. Type-4 records were accepted without proving `Central Processor`, and synthetic builders omitted the required type. The parser now filters processor type and populated-socket status; empty sockets do not consume the eight-observation bound.
7. `GetNativeSystemInfo` can report x86/x64 under Arm64 emulation. The provider now uses emulation-aware OS architecture and has a deterministic x64-process/Arm64-OS regression.
8. Cancellation cleanup proof could race the background provider task. The test now waits a bounded interval for explicit buffer zeroing before asserting cleanup.
9. Invalid UTF-8, controls, long serials, duplicate structures/handles, conflicting chassis, malformed lengths/terminators, excess populated processors, overflow memory/count, and raw-buffer clearing all have focused negative coverage.

Final independent verdicts report no remaining P0/P1/P2 code issue and no contract blocker. Cooperative native-call cancellation, compromised-OS truth, one-machine coverage, absent CPU-eligibility evidence, and future cross-provider architecture conflict are documented limitations/decisions rather than hidden passes.

## Boundary and risk impact

- **Project vacuum / sibling:** no sibling source, data, runtime, adapter, dependency, test, or compatibility claim.
- **Data / migration:** no persistence schema, database, attachment, cache, temporary report, log, or migration change. Raw firmware is transient and zeroed; normalized evidence is returned in memory for the already-governed later store boundary.
- **Release interface:** unchanged; no release behavior or compatibility promise is created.
- **Dependency / licence:** no package, SDK, toolchain, licence matrix, or redistribution-right change.
- **Security:** the surface is read-only and closed; firmware size/structure/field/count/time are bounded; failures are fail-closed; buffers clear; only central populated processors are admitted; process emulation cannot silently change the host-architecture fact. A compromised OS/firmware or local administrator can still lie, and in-process native calls cannot be forcibly terminated mid-call.
- **Privacy:** normalized observations, including full serial, remain Workshop Restricted. Full serial is absent from ordinary diagnostics/support and implicit rendering. The active test printed no identity; the repository capture is an explicit sanitized profile projection, and the public JSON remains wholly synthetic.
- **Accessibility:** no UI or human workflow changed. Typed stable outcome/error/recovery codes support later plain-language and assistive-technology mapping but do not constitute an accessibility walkthrough.
- **Modest hardware:** no GPU, service, resident worker, network activity, file scan, large fixture, or unbounded collection is introduced. One bounded off-thread operation and at most 1 MiB transient firmware bytes are used. Active timing is recorded, but no resource benchmark, minimum specification, or cross-hardware claim is made.

## Outstanding

None for TL-0106. It has no human-evidence requirement, approval blocker, known test failure, or unpublished tracked change.

The binding limitations remain: native calls are synchronous/cooperatively cancelled; Windows 10 is audit-only; firmware/OS facts are self-reported; active proof is one machine; manual/accessibility/resource/Full/Extended scenarios were not triggered; no eligibility/readiness/ownership/authenticity/reliability/cross-hardware claim is made; and no release authorization or redistribution right is created.

## Next steps

1. Select `TL-0107` and repeat the governed repository/task-start protocol before editing.
2. Implement read-only Windows edition/build/lifecycle, architecture, activation, and pending-reboot evidence without attempting key installation, bypass, or state mutation.
3. Define the exact cross-provider architecture conflict/freshness rule before TL-0112 aggregates TL-0106 and TL-0107 observations.
4. Keep unsupported/unknown lifecycle or activation evidence blocking, and preserve Windows 10 as audit-only rather than normal-ready.

## Upcoming decisions and implications

### Assign a governed source for Windows 11 processor eligibility

The approved pilot policy expects `processor.windows_11_eligibility`, but TL-0106 correctly records only manufacturer/model/count facts and no later task currently names ownership of the eligibility conclusion. Treating a model string as eligibility inside this provider would blend observation with policy, become stale as Microsoft support lists change, and allow a weak parser to grant readiness. Before real policy evaluation depends on the key, the project must assign an owning task and choose a versioned structured eligibility source or an attributable human-confirmation path. Until then, the key is absent/unknown and any requirement depending on it must block; this is safer than assuming either eligible or ineligible.

### Reconcile duplicate architecture evidence before aggregation

TL-0106 now emits `system.architecture` from the emulation-aware .NET OS source, while TL-0107 also owns architecture within OS lifecycle evidence. Both can be useful independent sources, but TL-0112 must not use collection order, first-wins, or last-wins. The upcoming decision must define source priority, freshness window, equality semantics, and the exact conflict result. A disagreement should remain visible and block dependent readiness until re-observation/review; silently collapsing it would erase evidence of a compromised, stale, or misunderstood source.

### Decide whether exact memory belongs in later public/support projections

Exact installed bytes are appropriate in Workshop Restricted normalized evidence and were retained only in the sanitized reference profile under its explicit project-evidence purpose. Current ordinary diagnostics/support allowlists do not gain the value automatically. Later report/support work must decide whether it needs exact bytes, a reviewed bucket, or omission for each audience. The choice affects privacy/linkability and usefulness; TL-0106 grants no general logging/export permission.

### Preserve the cooperative native-call boundary unless evidence requires isolation

The selected calls are fixed, fast on the active host, and run off the caller/UI thread, but Windows does not provide a safe way to abort them while in flight. Cancellation and timeout therefore bound the caller's wait and signal the source; cleanup completes when the native call returns. If later evidence shows a call can hang unacceptably, the project must decide whether to isolate collection in a bounded helper process with authenticated typed output and cleanup. It must not add thread termination, a permanent service, elevation, or an unsafe continue-anyway path.

### Keep Windows 10 collection separate from support readiness

The descriptor permits read-only Windows 10 collection because an audit may need truthful evidence from an unsupported target. This does not mean Windows 10 is supported for normal-ready disposition, pilot release, or remediation. TL-0107 policy/lifecycle evidence must preserve the distinction visibly. Conflating “provider can read this OS” with “product approves this OS” would weaken D-005 and could turn an audit capability into a false support promise.

## Historical TL-0008 transition

The superseded `TL-0008 draft 1` procedure remains preserved only as a historical record at source commit `4fa3ea050fd5e9985fde9cc8218281698d371cc8`, with procedure SHA-256 `ef150dbf14b5db208582b7b526c7e0c6d0a5b912736e9e6519b8918abcf0928b`. No physical hardware walkthrough was performed for that transition; its former device-pool procedure is not current evidence.

## Next dependency-ready task

`TL-0107` — Implement OS lifecycle and activation inventory. It is dependency-ready from TL-0105 and should be selected only in the next governed task-start session; no TL-0107 implementation was started here.
