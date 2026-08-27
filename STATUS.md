# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-08-27  
**Snapshot preparation time:** 2026-08-27T19:04:40+02:00  
**Bundle baseline:** 0.3.1  
**Portfolio baseline:** ThirdLife Software Portfolio v2.1  
**Current milestone:** M1 — Audit-only vertical slice — active  
**Current task:** `TL-0101` — Implement core job and evidence domain models — `done`  
**Action state:** done; implementation, Targeted verification, contract review, governed Quick, commit, push, fetch, and divergence verification complete

## Current state

`TL-0101` implements immutable Core contracts for jobs, devices, typed evidence and provenance, observations, human tests, requirements, external sanitization evidence, the five dispositions, and the nine action-journal states. The implementation remains inside `ThirdLife.Core`; it has no WPF, SQLite, WinGet, shell, Windows API, or sibling dependency.

Evidence metadata keeps privacy classification, D-015 evidence classification, and value availability as separate axes. Real normalized evidence is workshop-restricted; wholly synthetic evidence may be public-reference or conservatively workshop-restricted. Recipient-guide, support-sanitized, and raw-untrusted classes cannot be persisted as normalized Core evidence. Unknown and not-applicable values remain distinct and cannot carry a value.

Human confirmation requires human-confirmation provenance and typed operator attribution. Human-test passes require attributable human-confirmed evidence, so provider presence cannot become a functional pass. `not_tested` remains distinct from `not_available`; the latter represents either an absent capability or unavailable/unsafe/equipment/provider conditions with a bounded limitation code.

External sanitization records model exactly `verified`, `replacement_storage`, `no_donor_storage`, `unknown`, and `failed`. Inferred evidence cannot establish a trusted sanitization result. Unknown records carry no event attribution or media identifier; no-donor-storage cannot identify absent media; verified, replacement, and failed media cases require the governed attribution and verification combinations.

Serialization uses explicit property and enum wire names, nonzero governed enums that reject omitted default states under ordinary `System.Text.Json`, strict options that reject missing non-optional fields, unknown properties, and duplicates, scalar strong-ID converters, bounded well-formed Unicode, and an explicit bounded scalar evidence-value union. Opaque IDs reject traversal separators, dot aliases, colons, whitespace, controls, malformed Unicode, and Windows device aliases.

## Git state

| Field | Verified value |
|---|---|
| Remote | `origin` → GitHub repository `PikkuJanne/ThirdLife` |
| Branch | `codex/tl-0101-core-domain-models` |
| Starting commit | `cfdc68d6e8d8f6c2f31b3123e3edd2aee3ed6670` — published TL-0010 completion handoff |
| Completion commit | `83579c513aea21a8e46aca7a85f2f9ecaa5c7d55` — TL-0101 implementation, tests, evidence, and completion state |
| History handling | Started from fetched local/upstream equality; no reset, rebase, force push, or history rewrite |
| Publication state | Completion commit published, fetched, and verified equal to upstream at 0 ahead and 0 behind |

The configured SSH remote rejects unattended public-key authentication on this machine. Publication will use the already governed process-scoped HTTPS `insteadOf` bridge without changing the configured remote or exposing credentials.

The unrelated untracked `ThirdLife_Two-Team_Software_Portfolio_Roadmap_v2.1.docx` predates TL-0101 and remains untouched and unstaged.

## Verification evidence

| Scope | Result | Duration / limitation |
|---|---|---|
| Pre-change Core baseline | Passed 1/1 | 12.355 s on the direct host at `cfdc68d6e8d8f6c2f31b3123e3edd2aee3ed6670` |
| Final formatter, build, and diff check | Passed; 0 warnings/errors | 23.818 s combined on the direct host |
| Direct-host test execution | Blocked by Application Control `0x800711C7` after successful compilation | Not accepted as pass evidence; no host-security control was weakened |
| Initial Sandbox defect | 41/43 passed; nullable scalar-ID converter rejected explicit null optional fields | Reduced to the five sanitization round trips, corrected, then 5/5 and 43/43 passed |
| Strict-serialization focus | Passed 4/4 | Omitted governed fields, explicit nulls, duplicate/unknown fields, and strict round trips |
| Attribution-rule fixture defect | 59/61 passed; two stale tests still built a provider-observed `pass` | Corrected test data, then the focused serialization set passed 4/4 |
| Exact final Targeted suite | Passed 61/61; 0 failed; 0 skipped | Windows Sandbox 4096 MiB guest, 30.652 s complete / 213 ms tests; bounded result SHA-256 `f96e6d80963b172312f93263f2e978bc9bcc4f8d3ef605226eec77d7a930e0ea` |
| Independent final acceptance audits | Passed; no remaining P0/P1 blocker | Canonical YAML mapping and a formal framework allowlist remain later-work limitations, not TL-0101 blockers |
| Governed Quick | Passed 178/178 validator regressions and every live governed control | 134.633 s reported test duration; complete command not separately timed |
| Full / Extended | Not triggered | TL-0101 declares no broader trigger and changes no persistence/migration, privilege, package/update, installer/lifecycle, backup, or release boundary |

Windows Sandbox ran on the active physical Codex machine with source, SDK, and NuGet cache mapped read-only and one bounded writable result directory. Networking was available only for pinned locked restore and mandatory NuGet audit. This is deterministic model/serialization evidence, not direct-host policy compatibility, real human confirmation, physical-hardware observation, accessibility or modest-hardware certification, or a cross-hardware claim.

## Defect handling

The implementation followed the required reduce-fix-focus-broaden sequence:

1. Two optional scalar-ID null round-trip failures were reduced to the five sanitization-state theory cases; the converter boundary was corrected; 5/5 focused and then 43/43 related tests passed.
2. The first hardened complete suite passed 60/61 because a test expected exactly `ArgumentException` after operator validation correctly produced `ArgumentNullException`; the exact case passed 1/1 after correcting the assertion, then 61/61 passed.
3. The final human-pass rule correctly invalidated two provider-observed `pass` test fixtures; the strict-serialization focus passed 4/4 after fixture correction, followed by the exact final 61/61 pass.

No blind rerun was accepted. Bounded failure summaries were retained only long enough to diagnose and record sanitized task evidence; raw guest output was not checked in.

## Historical TL-0008 transition

The superseded `TL-0008` draft-1 procedure remains preserved at source commit `4fa3ea050fd5e9985fde9cc8218281698d371cc8`, with procedure SHA-256 `ef150dbf14b5db208582b7b526c7e0c6d0a5b912736e9e6519b8918abcf0928b`. No physical hardware walkthrough was performed for that transition. Its former device-pool language remains historical only and is not current TL-0101 evidence.

## Boundary and risk impact

- **Project vacuum / sibling integration:** No sibling repository, source, runtime, data, service, profile, adapter, schema, SDK, framework, dependency, or acceptance test was introduced.
- **Data / migration:** No runtime persistence location, database, schema migration, retention, deletion, export, or telemetry behavior was implemented. TL-0102 owns persistence and path/junction integration tests.
- **Release interface:** No release behavior or compatibility promise was added to `RELEASE_INTERFACE.md`.
- **Security / privacy:** Domain construction and deserialization fail closed for omitted governed states, privacy projection misuse, inferred sanitization promotion, unattributed human confirmation, provider-observed functional pass, contradictory availability/value states, malformed Unicode, and unsafe opaque IDs. Full media identifiers remain workshop-restricted values and ordinary `ToString()` redacts them.
- **Accessibility / low-spec:** No UI, background process, concurrency, cache, database, device provider, or product runtime work was added. Tests are bounded in-memory models; no accessibility walkthrough, performance claim, or hardware certification is made.

## Changed paths

- `src/ThirdLife.Core/DomainValue.cs` and `DomainJson.cs`: bounded primitive validation and strict canonical JSON options.
- `src/ThirdLife.Core/Jobs`: strong IDs, device/job records, and action-state vocabulary.
- `src/ThirdLife.Core/Evidence`: classifications, provenance, typed values, observations, human tests, requirements, dispositions, and strong evidence IDs.
- `src/ThirdLife.Core/Sanitization`: external sanitization evidence and exact state invariants.
- `tests/ThirdLife.Core.Tests`: exhaustive state matrices, round trips, omissions/adversarial JSON, vocabulary, identifier, Unicode, and platform-purity coverage.
- `TASKS.yaml`: only TL-0101 execution fields (`status` and append-only `evidence`).
- `STATUS.md` and `BUNDLE_MANIFEST.sha256`: current handoff and governed hashes.

## Outstanding

None for `TL-0101`. The direct-host Application Control limitation and every Targeted claim boundary remain recorded; neither blocks the deterministic Core domain-model contract.

## Next dependency-ready task

After TL-0101 is complete: `TL-0102` — Implement the SQLite job store and migrations. Do not start it in this session.
