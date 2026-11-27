# TL-0607 handover eligibility evaluator

The isolated domain evaluator checks whether the current job can request an operator sign-off. Each required projection binds to the same job, reviewed state and evidence origin. The evaluator requires a ready policy disposition, a current checklist without blocking rows, and current evidence for platform, sanitization, ownership, exceptions, essential actions, independent profile verification, cold boot, restart, handover mode, workshop record, recipient guide and support bundle disposition. The optional support bundle can use an exact saved omission choice. Every missing, conflicting or mismatched projection has a typed blocker.

On 2026-11-27, `dotnet test tests/ThirdLife.Core.Tests/ThirdLife.Core.Tests.csproj --configuration Release -p:RestoreLockedMode=true --filter FullyQualifiedName~HandoverEligibilityTests` passed 12 focused tests in 0.129 s. The active Windows host used .NET 10.0.400. Source tree `6404413a065695b04442229ef2d2decc5d0e4d59` is based on `be6069969f66b0971fd30454cef74cc9199e912a`. Controlled cases exercised each missing projection, archive, stale job context, different job and origin, stale verification, incomplete finalization, missing exception authority and the scope-bound support omission.

The expanded source tree `044fd103a8c0e6778819bd8acfd153085d930abe` adds output references with kind, version, SHA-256 and exact review scope. A support omission requires an operator code, UTC time, saved version and reason. The eligibility comparison detects changed bytes even when the output version stays the same and also detects a changed proof version. The same focused test selection passed 14 tests in 0.132 s with `--no-restore` after the locked restore.

The next integration work projects authoritative persisted evidence and binds the operator's sign-off to the exact reviewed state. The store must derive the review scope from the current job, policy, approvals, independent verification, cold boot, finalization confirmations and artifact scans, and recipient handover. The generated output receipts need the same review scope and exact verified bytes. A guide filename alone cannot establish those bytes. The completion transaction must re-evaluate current evidence and retain a superseded sign-off when relevant state changes. This checkpoint covers the pure domain eligibility decision. The final sign-off, lifecycle transition and report publication each require evidence from their applicable entry points.

## Integrated protected review checkpoint

On the active Windows 11 host, the review, attribution model and exact
workshop/guide receipt reader passed Targeted in 262.702 s against reviewed
main `cefe11e39539ca2a7d691723a14b12f82b17e3f6`. All 1,425 Quick .NET tests
and 31 selected Core, Reports and Persistence tests passed with zero skips.
A stale or missing profile, persisted checklist edits, recipient-mode changes
and a boot change prevent use of the previous review. Reopened protected
files must match the original digest and bounded size. The filename guard
rejects traversal and a receipt from the other output kind.

This checkpoint provides the review and exact-file primitives. The next
composition adds migration, console-free operator controls, receipt
registration, completion transaction and reopening projection.
