# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-08-28  
**Snapshot preparation time:** 2026-08-28T21:19:40+02:00  
**Bundle baseline:** 0.3.1  
**Portfolio baseline:** ThirdLife Software Portfolio v2.1  
**Current milestone:** M1 — Audit-only vertical slice — active  
**Current task:** `TL-0105` — Define inventory provider contracts and evidence normalization — `done`  
**Action state:** implementation, independent review, Targeted verification, solution integration, failure-injection registration, task evidence, and the synchronized completion-tree Quick are complete; committing, pushing, fetching, and confirming local/upstream equality are the remaining session-close publication steps

## Executive project state

M0 is complete. M1 tasks `TL-0101` through `TL-0105` are now complete. ThirdLife Setup Core has the governed persistence foundation, dependency/licence review, evidence model, structured privacy-safe diagnostics, and the common inventory-provider boundary required before any Windows inventory adapter is introduced.

`TL-0105` establishes a closed, read-only, cancellable and timeout-aware contract for collecting evidence. Every provider must state its identity, required privilege, expected duration, declared timeout, network behavior, supported Windows versions, evidence keys, scalar types, units, source-reference limits, and per-key cardinality. The runner bounds the caller's wait after a provider returns asynchronous work; each concrete provider must separately prove that its own API call yields promptly and respects cancellation. Providers return typed bounded results and have no dedicated policy-verdict, raw-output, command-execution, unbounded-map, or mutation-instruction field. Because the typed text value can still contain bounded source text, each concrete provider must also prove allowlisted minimization and sanitization.

The normalization layer preserves `Observed`, `Inferred`, `Not available`, and `Not applicable` states with explicit provenance and limitations. Provider failure, timeout, cancellation, access denial, invalid data, and incomplete cleanup are not successes. Required missing facts become explicit unknown evidence with a sanitized stable error and recovery action. Valid unrelated partial observations may remain attributable, but an ordinary provider failure cannot retain every declared required fact as available and thereby resemble success.

This step does not collect a real machine fact. Device identity, processor, memory, architecture, OS lifecycle, activation, storage, battery, firmware/security, network, and cross-provider snapshot work remain in `TL-0106` through `TL-0112`. The next dependency-ready task is `TL-0106`.

## User outcome

- Future inventory providers share one fakeable contract that is independent of concrete Windows API types.
- Missing or failed provider evidence cannot silently become a pass.
- Policy remains separate from observation: normalized inventory contains facts and uncertainty, not readiness decisions.
- Provider behavior is reviewable before execution because privilege, duration, timeout, network use, OS support, evidence shape, and bounds are declared.
- The common contract exposes no setting-mutation, generic shell, arbitrary executable, registry-path, URL, or network surface.
- `FI-001` / `SMC-PROVIDER-UNAVAILABLE` now has an exact independently invokable provider-contract test; network/transfer and concrete-provider variants remain truthfully unrun.

## Implemented behavior

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

## Git state

| Field | Verified value |
|---|---|
| Remote | `origin` → GitHub repository `PikkuJanne/ThirdLife` |
| Branch | `codex/tl-0105-provider-contracts` |
| Starting commit | `b98b03698f9615340ae755796c6abbe2b0456f91` — published TL-0104 completion |
| Task-start checkpoint | `9d7989b` — task selected, baseline recorded, pushed |
| Implementation checkpoint | `8e284b9283f8ba12fc15b9436d69afc728d87ec1` — provider contracts, normalization, tests, review corrections, pushed |
| Evidence-hardening checkpoint | `e5e9c7ef26ddfb2674ce08d7ea35b0dd193b9a2c` — exact FI-001 recovery/limitation assertions, pushed |
| Completion synchronization | This status/task/risk evidence update; final commit and remote equality are reported at handoff |
| History handling | No reset, rebase, force push, or history rewrite |

The configured SSH remote rejects unattended authentication in this environment. Publication uses the governed process-scoped HTTPS `insteadOf` bridge without changing the configured remote or exposing credentials.

The unrelated untracked `ThirdLife_Two-Team_Software_Portfolio_Roadmap_v2.1.docx` predates this task and remains untouched and unstaged.

## Verification evidence

| Scope | Result | Duration / limitation |
|---|---|---|
| Pre-change Inventory baseline | Passed 1/1 | 362 ms reported / 7.724 s wall; prior assembly scaffold only |
| Provider-contract Targeted | Passed 22/22; 0 failed/skipped | Latest evidence-hardening rerun: 272 ms reported / 1.814 s wall; deterministic fakes on direct host |
| Exact `FI-001` provider-unavailable subset | Passed 1/1; 0 failed/skipped | Final recorded run: 177 ms reported / 1.787 s wall; exact recovery/limitation assertions; no real provider or host mutation |
| Strict formatting | Passed | `dotnet format ... --verify-no-changes`; unchanged |
| Inventory production static review | Passed | No write, mutation, network, or generic shell surface in current production source |
| Release solution build | Passed; 0 warnings/errors | 5.39 s direct host |
| Implementation-checkpoint governed Quick | Passed 187/187; bundle/repository validators passed | 108.504 s at `8e284b9` |
| Synchronized completion-tree governed Quick | Passed 187/187; bundle/repository validators passed | 107.775 s tests / 112.654 s complete; 2026-08-28T21:17:33–21:19:26+02:00 |
| Full | Not triggered | No milestone/release gate, dependency, migration, privilege, package/update, installer/lifecycle, or broad shared-boundary trigger |
| Extended | Not triggered | No named resource, interruption, hosted-environment, physical, or hardware scenario trigger |

The synchronized completion tree passed the governed Quick tier before publication. After recording that result, the resulting final manifested tree receives the same exact gate once more before commit/push handoff. This is an additional repository-continuity check; the task-required runtime proof is the Targeted provider-contract suite above.

## Defect handling and independent review

Three independent read-only reviews examined contract design, downstream provider fit, and security/failure behavior. Their findings were resolved across the implementation and evidence-hardening checkpoints:

1. Synthetic provenance no longer downgrades normalized evidence from Workshop Restricted to Public Reference.
2. Provider-supplied unavailable candidates cannot mint runner-only timeout, cancellation, contract, cleanup, or execution limitations.
3. Non-cleanup failure cannot retain every declared requirement as available; a focused regression enforces this.
4. The static safety guard is scoped to contract/normalization code so it does not block legitimate, task-reviewed fixed Windows adapters or verified cleanup in later tasks.
5. Pre-cancelled collection uses system-generated provenance because the provider was not invoked.
6. Faults that complete after a timeout race are observed; hostile exception accessors prove normalization does not read unsafe text.
7. Public mutation-surface checks cover setters/mutator methods and arbitrary command, argument, script, executable, registry-path, URL, and URI fields.
8. The exact `FI-001` case now checks error, limitation, recovery, partial-evidence, and fail-closed agreement instead of relying on a disconnected mutation flag.
9. Test naming now states the proved claim—no Windows types in the public contract—rather than implying a non-Windows runtime that the Windows-targeted projects did not execute.

No open TL-0105 defect remains. The cooperative-timeout limit is a documented assurance boundary for concrete provider tasks, not a hidden pass condition.

## Boundary and risk impact

- **Project vacuum / sibling:** no sibling source, data, runtime, adapter, dependency, test, or compatibility claim.
- **Data / migration:** no persistence schema, database, attachment, cache, temporary report, log, or migration change. Tests use in-memory deterministic fakes.
- **Release interface:** unchanged; no release behavior or compatibility promise is created.
- **Dependency / licence:** no package, SDK, toolchain, licence matrix, or redistribution-right change.
- **Security:** the public surface is read-only and closed; failures are bounded and fail closed; raw exception members, dedicated raw-output fields, and arbitrary execution fields are excluded. Bounded typed text still requires provider-specific allowlisting and sanitization. A compromised OS can still lie, so source truth and fresh re-observation remain residual risks.
- **Privacy:** every normalized observation is Workshop Restricted; no active-machine identifiers or sensitive capture were collected. Later providers still need field-specific minimization and approved persistence/report projections.
- **Accessibility:** no UI or human workflow changed. Typed stable outcome/error/recovery codes support later plain-language and assistive-technology mapping but do not constitute an accessibility walkthrough.
- **Modest hardware:** no GPU, service, resident worker, network activity, large fixture, or unbounded collection is introduced. Declared duration/timeout/count/text bounds support later constrained providers, but no resource benchmark, minimum specification, or cross-hardware claim is made.

## Outstanding

There is no remaining TL-0105 blocker or human evidence requirement. The task is complete at its defined contract boundary.

The following are explicit future-work limits, not incomplete TL-0105 acceptance criteria:

- No concrete Windows provider has been implemented or smoke-tested.
- No real device, CPU, memory, OS, storage, battery, firmware, security, or network fact was collected.
- No policy verdict, persistence snapshot, UI presentation, report, support export, release authorization, or hardware certification exists from this step.
- In-process timeouts do not forcibly terminate a provider that blocks before returning or ignores cancellation.
- `FI-001` network/transfer variants, concrete-provider `SMC-PROVIDER-UNAVAILABLE` variants, and the broad FI/SMC matrix remain unrun until their owning tasks reach their triggers.

## Next steps

1. Start only `TL-0106` after this completion commit is pushed and local/upstream equality is verified.
2. Implement the first concrete unelevated Windows provider for device identity, processor, architecture, and installed memory against the TL-0105 boundary.
3. Supply deterministic captured/synthetic inputs covering multiple represented manufacturers and processor/architecture/memory variants, including missing, denied, malformed, duplicate, and bounded multi-instance cases.
4. Run a bounded read-only active-machine smoke observation and record only the sanitized reference-profile facts authorized by the task; keep active observation separate from synthetic coverage.
5. Re-run the exact provider-unavailable case for the concrete adapter and add provider-specific fixed-operation, no-mutation, timeout/cancellation, source, unit, cardinality, and privacy tests.

## Upcoming decisions for `TL-0106`

### Windows evidence sources and fallback order

TL-0106 must choose exact structured, read-only Windows sources for manufacturer/model, processor, architecture, and installed memory and define a deterministic fallback/conflict order. The choice determines privilege requirements, localization exposure, cancellation behavior, unit conversion, and how a missing or contradictory source is reported. A fixed reviewed API or fixed helper invocation may be acceptable; generic shell strings, localized table parsing, and caller-supplied commands are not. This decision authorizes only those exact reads and does not grant a general WMI, PowerShell, process, or registry execution surface.

### Device identity and serial privacy

The task permits a full serial only in the approved Workshop Record while ordinary logs expose an approved suffix or no serial. Before implementation, TL-0106 must bind which layer may hold the full value, how deterministic fixtures avoid real serials, how active-machine smoke evidence is sanitized, and how error/source references avoid leaking device or host identity. Approval of the provider does not authorize serials in diagnostics, status files, test output, captures, support exports, or user-facing summaries.

### Normalized keys, units, and cardinality

The provider must define exact stable keys and invariant types/units for device type, manufacturer/model, processor identity/capability, architecture, and installed memory. It must also decide whether processor data is one aggregate observation or bounded per-instance evidence and how source references distinguish instances without embedding sensitive identifiers. These decisions affect later policy and reports, so keys should be minimal and durable rather than mirroring a provider's raw schema.

### Supported Windows range and unelevated behavior

TL-0106 acceptance requires successful unelevated operation on supported Windows 11. The current descriptor can state supported OS families (`Windows10` and/or `Windows11`); TL-0106 must choose the applicable family and define what happens on unsupported/unknown builds or denied data. If finer build-range metadata is necessary, that is an explicit governed contract extension rather than an implied existing capability. An unavailable source must remain unknown; the task may not add elevation, weaken permissions, or use a bypass simply to obtain a value.

### Cooperative cancellation and timeout proof

The concrete API path must return control promptly, honor cancellation where the underlying API permits it, and stay within its declared timeout and evidence bounds. If the API cannot be cancelled, the task must define a bounded wrapper and truthful late-completion/cleanup behavior or escalate an architecture decision. TL-0105 does not pre-authorize a permanent worker, privileged service, or forcible thread termination.

### Evidence scope and claim wording

Captured/synthetic samples prove only represented parser and normalization cases; the active-machine smoke run proves only that exact machine, build, provider, and observation time. TL-0106 must keep those evidence classes separate and explicitly avoid manufacturer coverage, minimum-specification, performance, reliability, compatibility, or cross-hardware certification claims.

## Historical TL-0008 transition

The superseded `TL-0008 draft 1` procedure remains preserved only as a historical record at source commit `4fa3ea050fd5e9985fde9cc8218281698d371cc8`, with procedure SHA-256 `ef150dbf14b5db208582b7b526c7e0c6d0a5b912736e9e6519b8918abcf0928b`. No physical hardware walkthrough was performed for that transition; its former device-pool procedure is not current evidence.

## Next dependency-ready task

`TL-0106` — Implement device identity, CPU, memory, and architecture inventory. It is not started in this session.
