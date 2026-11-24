# TL-0115 workshop report verification

**Date:** 24 November 2026, Europe/Berlin  
**Base:** `ac35205d0295452d806fc494fffc88d39b974738`, PR #15 integrated on main  
**Branch:** `codex/tl-0115-workshop-report`  
**Environment:** Active Windows 11 Pro x64 build 26300, `REF-CODEX-001` revision `2026-08-29.1`, SDK 10.0.400

## Implemented scope

The [workshop report](../../../docs/ui/audit-workspace.md#workshop-report) is an explicit
restricted projection of the durable job. The renderer uses invariant formatting,
UTF-8/LF output through the store, an observation allowlist and separate sections for
external intake, assessment, observations, manual checks and next action. It retains
source class, provider or operator, capture time and limitations. Unknown, unavailable,
unrun, interrupted and historical results remain explicit. Manual free text and
unreviewed observation fields stay in their existing protected records rather than
entering the report. A fixture record identifies its scope. The report supplies no
readiness decision.

The UI rereads the durable job before saving. The destination uses an existing
restricted-file security implementation and a separate registered workshop folder.
The store holds the Windows destination identity, publishes without replacement and
issues a bounded capability. Opening validates that artifact's identity, bytes,
length and digest before displaying selectable read-only text in the app. Job change
clears the currently open capability and preview. Output names are opaque. Earlier
outputs remain available in the protected folder. No new external package version,
database migration, privilege, network use, shell handler or release claim is added.

## Verification

- The pre-edit Reports baseline passed its assembly test in 2.341s with no failures or skips.
- The focused renderer suite passed 7 tests in 6.245s. It checked the exact golden
  output, invariant culture, deterministic limits, provenance, wrong-job rejection,
  historical outcomes and omission of prohibited values. The 2,159-byte golden fixture
  SHA-256 is `4e1e260c9482a7ae1531dbb23e7ac7106a5f32ab15ec5cc50d8b4fbe173f8dc8`.
- The focused protected-store suite passed 9 tests in 1.792s. It covered bounded UTF-8,
  atomic collision preservation, unreadable destination, altered/missing artifact,
  capacity and cancellation, junctions and hard links.
- The focused loaded WPF suite passed 6 tests in 8.899s. It exercised generation from
  a fresh SQLite snapshot, stable bytes, keyboard focus and read-only preview, job
  switch, capacity and collision preserving earlier output, inaccessible destination,
  and opening failure. No actual device collection was involved.
- Two focused fixture-governance checks passed in 1.020s. The inventory accepts the
  exact M1 golden fixture required by TL-0115, rejects another output beside it and
  preserves the earlier pilot input digest. The bundle manifest binds the new file.
- Active-machine `eng/verify.ps1 -Tier Full -Restore` passed in 281.624s in an
  isolated worktree. New internal application dependency edges and native guarded
  publication justified Full. All 279 governance and 1,337 supported .NET tests
  passed with zero failures or skips. Reports passed 17, UI 61, Inventory 901 and
  Persistence 113. Locked restore, format and warnings-as-errors succeeded. This
  run includes the Quick checks and the affected projects. No Extended trigger applied.
- Candidate overlay SHA-256
  `9b5f280f8a0975dbda8cd5898bdb0bd090afab043d8457f7bdfcd5f7b32c5a16`.
  Standard-user apphost startup and navigation smoke passed in 1.614s and the
  owned window closed normally. Tested apphost SHA-256
  `101bde26c7d3d80a600d7b7113e135685bfce90965858471df429c17c7548e3d`.
  UI assembly SHA-256
  `bad8f61d47ace4e9cbda6b14fe651e296fd72a7bb7cefb10d2fcc3b361904dcf`.

The first native publication check exposed process-working-directory resolution of a
bare relative rename. Its output was confined to disposable synthetic test builds.
Binding a directory handle with a relative name was rejected by Windows. The successful
canary changed the native input to an absolute destination and guarded the full ancestor
chain while publishing and checking the resulting identity. The first WPF run exposed
focus moving to the root scroll viewer when Generate became busy. Anchoring focus to
the report status during a command fixed the issue. Focused regressions passed before
the final run. Any retained isolated build residues outside the checkout contain only
test output and synthetic inputs.

The M1 gate owns the integrated operator check and owner acceptance. The focused checks
do not establish those observations.
