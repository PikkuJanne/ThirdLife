# TL-0413 — TRACE native application

## Source and authorization

- The owner approved the TRACE v1.0 interface replacement for ThirdLife Setup Core. The build bundle ZIP SHA-256 was `208a66318530583f65c64d8e4e2e97e69273b96560b22ad3c16819dcdfb642b9`. All 30 payload hashes matched.
- The design source `d61c49e9bb4cc8f5b18c4ac3576a713060cb3f77` is an ancestor of fetched `main` at `4954fe7a9ef08e89357245d0fc07d62ec2129bc7`. Implementation started at the fetched M4-gated main in a separate worktree. Original untracked material and prior worktrees were preserved.
- The verified scope is the native presentation and existing service entry points. The production package route still depends on the exact action approval and applicable Windows consent. Device acceptance and recipient-facing features remain governed by their own implementation and release gates.

## Baseline

On 2026-11-26, `eng/verify.ps1 -Tier Quick` passed in 144.338 s on the active Windows 11 Pro 26H2 build 26300.9974, native x64, .NET SDK 10.0.400. Core 126, Inventory 927, UI 180. The run exceeded the 120 s target before TRACE changes. Its dominant test project was UI at 63 s. The run used structural bundle validation.

## Checked workflow

Jobs opens the existing local store lazily. Intake shows the saved record and lets the operator resolve an interrupted gate. Inspect shows a provider finding and its source, collection time and exact evidence reference, or a selected manual procedure and saved outcome. Decide shows the real candidate evaluation, its evidence and linked repairs. Prepare displays the reviewed profile, eight impact areas, exact approval and package operation. Verify reads the attempt journal, distinguishes installer results from independent verification, and exposes scoped retry, installed-state checks and restart review. Handover generates and opens the restricted saved workshop report.

The jobs list continues to use create and open by code. A recent-jobs query is deferred because the existing store has no bounded discovery route for approved metadata. The isolated no-op qualification remains available to engineering fixtures. Its historic journal can be read without presenting normal dispatch controls.

## Verification

Candidate results are recorded after committed-source verification.
