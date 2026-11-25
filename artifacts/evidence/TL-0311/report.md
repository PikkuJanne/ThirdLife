# TL-0311 authenticated broker transport checkpoint

**Date** 2026-11-25  
**Base** `8f1441be844536e8b4ef88193b3a3e4c1d9c453e`, PR #35  
**Environment** REF-CODEX-001 rev 2026-08-29.1, unelevated interactive Windows 11 x64, .NET SDK 10.0.400 and Python 3.14.7

## Status

The authenticated local transport is implemented. The development broker
compiles and the ordinary apphost rejects an unelevated launch. The UI
returns `Unavailable` before requesting elevation because the current
framework-dependent apphost cannot establish trusted startup. This checkpoint
does not complete TL-0311 or authorize its integration into `main`.

Windows can load the managed host, runtime and startup code before broker
`Main` can validate the caller or request. The direct shell-launch candidate
was removed after review identified that gap and the writable development
deployment. No host consent request was made. The launch decision, missing
toolchain, and remaining proof are documented in the
[broker launch trust boundary](../../../docs/security/broker-launch.md).

## Completed technical checks

| Scope | Observed result |
| --- | --- |
| Local pipe | Native first-instance, overlapped byte pipe with remote clients rejected and a protected logon-SID data ACL. Kernel process and session queries agree with both endpoints. An unlisted logon fixture is denied. The peer token is read at Identification level. |
| Exact request | A fixed bounded ticket pins the known client process, correlation, UTC expiry, nonce proof and canonical request digest. A single ordered batch is checked independently against the compiled no-op registry. The nonce itself is not in the bootstrap arguments. |
| Framing and result | The 4,096-byte cap is checked before payload allocation. A truncated frame differs from normal EOF. Only an exact ordered result for the active action is accepted once. Typed results carry no raw output and do not establish verification. |
| Recovery | Deadline, disconnect, cancellation, partial results and a second request are tested. The broker closes after its batch or a bound, and the client retains partial terminal observations when the connection becomes ambiguous. |
| Launch | The Windows error-code mapping and pending-consent cancellation are simulated. Production UI requests no elevation with this development host. Real UAC, elevated peer identity and failure of the actual elevated process remain unverified. |

## Verification

- Before edits, the existing broker project passed 48 tests in 5.237 s.
- Locked first-party references reuse the broker protocol and compiled action
  contracts. The UI build carries the shared protocol and leaves the development
  apphost with the broker project and its tests. No new package or machine
  configuration was required. Release builds passed with warnings as errors.
- The first focused IPC run found a Windows fixed-width `TokenElevation`
  query failing on a null-buffer size probe. Reading those integer fields
  into a four-byte native buffer corrected the problem. The focused native
  regression passed before running the related suite.
- The focused broker selection passed 30 cases in 5.092 s, including the independently
  invokable `ActiveMachine` unelevated apphost rejection. Native pipe tests
  observed ordered final frames after server disposal followed by clean EOF.
- The focused UI `BrokerBatchClientTests` passed eight cases in 6.490 s,
  including ordered results, reconstructed partial state after stream loss,
  pending consent cancellation and two Windows consent-path fixtures. Fixture
  results do not establish an actual elevation prompt or operator decision.
- `./eng/verify.ps1 -Tier Targeted` with the Broker and UI projects passed
  1,204 .NET tests in 76.187 s, including Quick. The exact checked source tree
  `fa3fe60e9b9bc3f479b5145d5f9af3e04363cc1b` is pushed as checkpoint
  `382e4f4767d0efadaf217536a1e2c138504fa7e7`.
- Clean `./eng/verify.ps1 -Tier Full -Restore` on the same tree passed in
  343.515 s. It ran 289 governance and 1,926 .NET tests, with locked restore,
  format verification and the warnings-as-errors Release build.
- After recording the launch blocker, Quick passed in 69.979 s. The final
  guest limitation also passed the bundle and repository validators.
- The network-disabled Windows Sandbox cross-account profile stopped after
  342.323 s without a guest result. Its window reported that Windows Sandbox
  failed to initialize, with `0x80070780`. A minimal offline startup probe
  without mapped folders or a logon command reproduced that exact error. This
  identifies a Sandbox startup blocker independent of the governed mounts.
  The distinct-account test remains unobserved and no host policy was changed.

## Windows 11 platform reconciliation

The draft incorporates integrated `main` at `af51598904b60f6f4fadf0bc09efe6857a51a95b`.
Both retained broker commits are ancestors of this checkpoint. The client checks the shared
platform contract before identity lookup or launch. The broker independently checks it before
ticket processing, token checks or IPC. The default trusted launcher remains unavailable.
Synthetic refusal cases covered unsupported OS, unsupported native architecture and
unverified identity at each boundary.

- The pre-reconciliation IPC selection passed 29 cases in 8.074 s. Targeted
  with `-Restore` and the Broker and Broker.SecurityTests projects passed 1,248 .NET
  tests, including Quick, in 78.901 s on tree `30b2427c113b6c30b82a128380ce5ec0cdc2e44b`.
- The `test_sandbox_harness.py` unittest selection passed 41 static and function cases
  in 55.167 s. The distinct-account guest was outside this reconciliation run.
- On the active unelevated Windows 11 Pro 26H2 native-x64 host, build 26300.9974,
  `UnelevatedApphostRejectsAValidTicketWithoutStartingAPipe` passed in 1.881 s.
  Two simulated consent-path cases passed in 1.736 s.
- The exercised Broker apphost SHA-256 was
  `e339183b291080f53b3103ba0ce7c7a0b6817fe8bff15acd279f94f6c23590a8`.
  The Broker assembly was `74e6370d888e537c0b997f9cf50acb01bb5cc1be83a651dabfc23391e365ceca`
  and UI assembly was `7a5b3ea94eb920aff32bf6d0e91e4b99de1caf313fade3df18c5a4e2041d3052`.

The reconciliation covers platform admission. Trusted elevated startup, real UAC and
recovery, and the distinct-account guest remain TL-0311 blockers.

## Remaining evidence

- Trusted, fixed elevated startup before .NET hosting or any broker work.
- Actual explicit UAC accept and decline, matching elevated broker token,
  process exit and recovery for interruption of the elevated apphost.
- Guest execution of the distinct standard-account connection case.
- A capability and exact artifact for the supported Windows native build
  toolchain if the project uses the C# Native AOT route.
