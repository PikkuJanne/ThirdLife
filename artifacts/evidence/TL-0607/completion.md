# TL-0607 sign-off transaction and reopening

On 27 November 2026 the active Windows 11 x64 host built the candidate
with the .NET 10.0.400 SDK. The composition is based on support export
source `7af7721aba6135574486acc3286150309463ab66` and the protected review
checkpoint `06146a1`. Migration 019 adds bounded per-job publication and
sign-off histories. The normal Handover stage can review blockers, list
exact output identifiers and digests, record a typed support omission, and
request a distinct operator sign-off. The completion transaction re-evaluates
the same policy, job evidence, authorization and output proofs before writing.

- The selected Core tests passed 16 of 16 in 0.309 s. They cover each missing
  input, wrong scope, wrong output, stale review, archived job and serialization
  of the exact sign-off proof.
- Six selected Persistence cases passed in 2 s. They exercised migration,
  superseded output scopes, interrupted/published support audit boundaries,
  and refusal to complete a blocked job.
- The positive Persistence case passed in 2 s through the same SQLite store,
  policy, controlled maintenance, profile and boot-verification entry points.
  It rejected a changed output before completion, recorded the exact actor and
  outputs, reopened current, then retained the original sign-off as historical
  after a modeled boot change. Its Windows and artifact results are fixtures.
- Five selected WPF tests passed in 9 s. The normal Handover surface announced
  missing requirements, saved a typed omission, and kept completion disabled.
  The guide, mode-selection and support-export controls passed in the same build.

No actual device handover or recipient consent was recorded. The positive
case is an isolated controlled proof of the completion path. Native release
qualification, actual eligible update installation, and pilot exposure retain
their separate prerequisites and evidence records.

The composed candidate passed `eng/verify.ps1 -Tier Targeted` on the active
Windows host in 232.114 s. All 1,428 Quick .NET tests and 57 selected Core,
Reports, Persistence and WPF tests passed with zero skips. Formatting,
repository controls and Release build passed. The clean Full migration gate
for the final composed source remains to run.
