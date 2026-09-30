# Repository stabilization: branch and pull-request disposition

**Review date:** 2026-09-30  
**Activity:** `AMD-2026-09-12-REPOSITORY-STABILIZATION`, bundle 0.4.0  
**Reviewed advanced commit:** `89303ab15ed749754290b7c17e5f6cdb27719a00`  
**Reviewed default branch:** `1bb776f710bfa76f485bbee8d4b182b7a5284cd9`  
**Result:** ancestry and content audit complete; owner disposition remains required for merge, PR closure, and branch deletion.

## Scope and decision boundary

This audit covers this repository's `main`, every existing `codex/tl-*` branch, the ADR numbering amendment branch, the threat-model branch, and open draft PRs #4-6. Counts and heads below describe the repository before publication of the new stabilization branch. GitHub's refs API independently confirmed all 17 remote heads; the session also completed its fetch through the HTTPS bridge recorded in the stabilization evidence.

The intended cumulative candidate descends from the reviewed advanced commit and retains its current approved TL-0005/TL-0006 implementations and subsequent work through TL-0106. The advanced commit contains all 42 commits after `main`. This is a source-continuity finding, not runtime verification or release approval.

The older privacy and supply-chain branches contain distinct commits and materially different contracts. Their current preservation is intentional. Neither topic overlap nor the existence of a later approved implementation proves that every older detail is included or that deletion is safe. The owner must decide whether to retain those alternatives as history, request separately governed follow-up work, or later authorize their retirement with an explicit record of excluded content.

The user authorized stabilization and a cumulative PR, and required a stop before merging. This audit authorizes no merge, PR closure, branch deletion, history rewrite, or TL-0107 implementation. All existing PRs and branch references remain intact during this session. An owner-approved merge and refreshed reachability/content checks must precede any separately authorized cleanup. TL-0107 starts only in a later session from updated `main`.

## Branch inventory

`Branch-only / advanced-only` is the symmetric difference from `git rev-list --left-right --count <branch>...89303ab15ed749754290b7c17e5f6cdb27719a00`. An ancestor has zero branch-only commits. Every local/remote pair matches except the threat-model pair described below.

| Remote branch | Exact remote head | Ancestor of advanced | Branch-only / advanced-only | Content and present disposition |
|---|---|---|---|---|
| `main` | `1bb776f710bfa76f485bbee8d4b182b7a5284cd9` | Yes | 0 / 42 | Base through merged TL-0004. Preserve; cumulative integration awaits owner review. |
| `agent/tl-0004-threat-model` | `e1880667619793e0a784020d1234f58c37ac2b5f` | No | 1 / 42 | Remote includes the legacy privacy implementation. Preserve distinct history pending owner disposition. |
| `codex/adr-0009-numbering-amendment` | `1c2aeff4b6517d676a3fc127fe1f912fb6b6c516` | Yes | 0 / 30 | Governed ADR numbering amendment is included. Preserve reference. |
| `codex/tl-0005-privacy-logging-model` | `b5c435c92e55f1d326963fc4aea9d4dd50525d37` | Yes | 0 / 37 | Current privacy implementation and approval are included. Preserve reference. |
| `codex/tl-0005-privacy-model` | `9fa41addc79504166627438022463fabc43757b6` | No | 2 / 42 | Earlier privacy implementation and approval differ from the retained contract. Preserve pending owner disposition. |
| `codex/tl-0006-dependency-license-sbom-controls` | `6843d354bd93c351ad5817a4136379dd1e9dccc6` | Yes | 0 / 34 | Current supply-chain implementation and original approval are included. Preserve reference. |
| `codex/tl-0006-supply-chain-controls` | `2b69ba4d9f5a9ae97c656c398d4528addce6f040` | No | 1 / 42 | Earlier SBOM implementation differs from the retained contract. Preserve pending owner disposition. |
| `codex/tl-0007-synthetic-pilot-fixtures` | `40a46c3e70bd495887e2e244c5d34bcbbb42adc6` | Yes | 0 / 32 | Synthetic fixtures and approvals are included. Preserve reference. |
| `codex/tl-0008-device-test-procedure` | `c77966ba90a31b99e790a2d1097b598c1f127961` | Yes | 0 / 39 | Historical device-pool procedure and its same-machine transition are included. Preserve reference; the old procedure stays superseded. |
| `codex/tl-0009-architecture-decisions-boundaries` | `4872f7c34143a3278b92bcb37c8fcd9a435b03a7` | Yes | 0 / 31 | Architecture baseline is included. Preserve reference. |
| `codex/tl-0010-m0-foundation-gate` | `cfdc68d6e8d8f6c2f31b3123e3edd2aee3ed6670` | Yes | 0 / 21 | M0 gate evidence and approval are included. Preserve reference. |
| `codex/tl-0101-core-domain-models` | `929f0e34dda02704c32658af9d7b8efc59c44028` | Yes | 0 / 19 | Domain models are included. Preserve reference. |
| `codex/tl-0102-sqlite-job-store` | `953df8cd483b714bbf495484fa789924c2af6308` | Yes | 0 / 13 | Store, migrations, renewed licence approval, and evidence are included. Preserve reference. |
| `codex/tl-0103-job-lifecycle` | `26c6b5004c7eab6e067897c8988c85deb3499db2` | Yes | 0 / 10 | Job lifecycle and governed evidence are included. Preserve reference. |
| `codex/tl-0104-structured-logging` | `b98b03698f9615340ae755796c6abbe2b0456f91` | Yes | 0 / 6 | Logging and recovery evidence are included. Preserve reference. |
| `codex/tl-0105-provider-contracts` | `6bfbbe8f51024065370c61cad9745a4f12db2e36` | Yes | 0 / 2 | Provider contracts and evidence are included. Preserve reference. |
| `codex/tl-0106-system-inventory` | `89303ab15ed749754290b7c17e5f6cdb27719a00` | Equal | 0 / 0 | Reviewed advanced base for stabilization. Preserve reference. |

The local `agent/tl-0004-threat-model` head is `4b5e22c1aa20249fc1dc4c3efd503ed7e783ae2b`, an ancestor of advanced with 0 / 43 commits. It is zero ahead and two behind its own upstream: the remote includes the `main` merge and then the legacy privacy commit. This is an explained stale local reference, not diverged local history. All other audited local branches exactly match their corresponding remote head.

## Open pull requests

At audit time, all three PRs were open drafts targeting `main`; GitHub reported `mergeStateStatus: CLEAN` for each. That value describes GitHub mergeability, not acceptance, runtime safety, or permission to merge.

| PR | Exact head | Unique commits versus advanced | Finding and required disposition |
|---|---|---|---|
| [#4 - TL-0005: define privacy and logging model](https://github.com/PikkuJanne/ThirdLife/pull/4) | `9fa41addc79504166627438022463fabc43757b6` | 2 | Earlier privacy implementation and its approval. Preserve open; owner must explicitly disposition its distinct content before any closure/deletion. |
| [#5 - TL-0006: add supply-chain and deterministic SBOM controls](https://github.com/PikkuJanne/ThirdLife/pull/5) | `2b69ba4d9f5a9ae97c656c398d4528addce6f040` | 1 | Earlier supply-chain implementation. Its PR still records human licence review as pending. Preserve open; owner must explicitly disposition its distinct content before any closure/deletion. |
| [#6 - TL-0008: define physical-device test procedure](https://github.com/PikkuJanne/ThirdLife/pull/6) | `c77966ba90a31b99e790a2d1097b598c1f127961` | 0 | Head and all commits are included in advanced. Title/body still request the superseded device pool and physical walkthrough. Preserve open; any later closure should cite D-064 and the cumulative integration. |

PR #6 includes historical procedure commit `4fa3ea050fd5e9985fde9cc8218281698d371cc8`, transition commit `0ded6a1778a6e747071a7f7dfea578c7f82b00ed`, and handoff commit `c77966ba90a31b99e790a2d1097b598c1f127961`. The stale PR description cannot revive the superseded physical-device gate.

## Substantive review of unique content

`git cherry -v` reports `+` for all three commits below. They are not patch-equivalent to commits in the advanced line.

### Earlier privacy implementation and approval

Distinct commits:

- `e1880667619793e0a784020d1234f58c37ac2b5f` - define privacy and logging model;
- `9fa41addc79504166627438022463fabc43757b6` - record privacy model approval.

The earlier line uses eight `PC-01` through `PC-08` classifications, fixture `schema_version: 1`, and 68 `PRV-*` cases. Its event envelope, field names, classification model, fixture shape, validators, and human approval binding differ from the current line. It is not an omitted patch that can safely be cherry-picked onto the current implementation.

The retained implementation is `118240955b01ea4a0b941b00d357ea165b035981`, followed by approval commit `b5c435c92e55f1d326963fc4aea9d4dd50525d37`. Its 56 `RDX-*` cases use `thirdlife.redaction-fixtures.v1`. The current [privacy model](../privacy/privacy-model.md#privacy-owner-approval-record) records the named privacy owner's 2026-08-21 exact-commit approval. TL-0104 subsequently implements the retained logging/redaction contract. The older approval applies only to its own reviewed revision and is not evidence for the retained replacement.

Present decision: retain the approved current implementation; preserve both older commits and their PR. No claim is made that all 68 older cases map one-for-one to current cases or that every older constraint is duplicated. If the owner identifies a useful missing constraint, its adoption requires separately scoped, governed work and the applicable privacy approval.

### Earlier supply-chain implementation

Distinct commit: `2b69ba4d9f5a9ae97c656c398d4528addce6f040` - add supply-chain and SBOM controls.

The earlier line adds `tools/generate_sbom.py`, emits CycloneDX 1.7, exposes an explicit `--release` gate, and uses an earlier 20-component matrix. Its wrapper, matrix columns/approval model, tests, and validator integration differ from the retained implementation. The current tree instead uses `tools/supply_chain.py`, CycloneDX 1.6, source-revision/input binding, and the later governed inventory. Restoring the old files would create competing contracts and tools.

The retained supply-chain lineage includes implementation `46de0c8597f462ad083284797e6f1fa09d52d47b`, review checkpoint `11711bcab68c5c23fea4705a49ca7ff2d021d485`, and original approval `6843d354bd93c351ad5817a4136379dd1e9dccc6`; later synthetic-fixture and SQLite changes renewed the matrix approval. The current [dependency and rights review](../supply-chain/dependencies.md#human-licence-and-rights-review) records the 2026-08-28 approval of candidate `d6807937f5eff712821c7927ce1953daaa5dfeb8` and exact 28-component matrix SHA-256 `e85f3002175dfadc860f0d2c92de0787f52364f491e50a030c936a5421395418`. Recorded withheld rights and release limitations remain binding.

Present decision: retain the approved current implementation and preserve the earlier commit/PR. The older CycloneDX version or release-mode interface is not implicitly adopted by cumulative integration. Any owner-selected reuse needs a separate contract and compatibility review; this audit neither certifies equivalence nor authorizes deletion.

## Reproducible evidence and limitations

The audit used these read-only commands, with branch arguments expanded over the inventory above:

```powershell
git for-each-ref --format='%(refname:short)|%(objectname)|%(upstream:short)|%(subject)' refs/heads refs/remotes
git rev-list --left-right --count <branch>...89303ab15ed749754290b7c17e5f6cdb27719a00
git log --format='%h %s' 89303ab15ed749754290b7c17e5f6cdb27719a00..<branch>
git cherry -v 89303ab15ed749754290b7c17e5f6cdb27719a00 origin/codex/tl-0005-privacy-model
git cherry -v 89303ab15ed749754290b7c17e5f6cdb27719a00 origin/codex/tl-0006-supply-chain-controls
git diff --name-status <advanced-or-replacement> <legacy-head> -- <reviewed-paths>
git show <commit>:<reviewed-path>
gh pr list --state open --json number,title,headRefName,headRefOid,baseRefName,url,isDraft,mergeStateStatus
gh pr view <4-or-5-or-6> --json number,title,state,headRefName,headRefOid,baseRefName,url,body,commits
gh api repos/PikkuJanne/ThirdLife/git/matching-refs/heads/ --jq '.[] | [.ref,.object.sha] | @tsv'
```

- **Tier:** repository ancestry/content review; runtime tiers are recorded separately in the stabilization evidence.
- **Environment:** active Codex machine; local object database and read-only GitHub metadata, with no sibling repository access.
- **Result:** all 17 remote heads matched the audited tracking refs; 42 advanced-only commits versus `main`; three distinct legacy commits; no unexplained local divergence.
- **Duration:** aggregate review duration was not measured; no runtime/performance claim is made.
- **Artifact:** this committed review, the immutable commit IDs above, and linked PR history.
- **Limitations:** remote refs and PR states can change after this observation. Refresh them before an owner-authorized merge or cleanup. The review identifies material differences but does not claim formal semantic equivalence, full behavior coverage, new human approval, or universal hardware support.
