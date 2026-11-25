# TL-0310 versioned broker protocol

**Date** 2026-11-25  
**Base** `2564c99d7adf46a5627bd716235310533ea6e851`, PR #34  
**Environment** REF-CODEX-001 rev 2026-08-29.1, Windows 11 x64, .NET SDK 10.0.400 and Python 3.14.7

| Requirement | Evidence |
| --- | --- |
| Bounded versioned messages | Seven closed v1 shapes carry an exact correlation ID, 32-byte nonce and UTC deadline. The codec bounds each payload to 4,096 bytes and four JSON levels. Progress and result sequence values range from 1 through 32. |
| Typed allowlist | A request binds one to four distinct compiled no-op actions with empty parameters to a job, approval, plan digest and impact digest. A canonical digest covers the ordered actions and the full session context. |
| Strict decode | The parser rejects invalid UTF-8, duplicate or unknown keys, bad integer tokens, unknown versions, bare or invalid identifiers, and unsupported actions. The schema describes the closed decoded shapes. |
| Structured errors | A finite error enum and a fixed decoder diagnostic keep untrusted payloads out of errors. Progress, results and cancellation use typed values. Applied results still require independent verification. |
| Narrow interface | The portable protocol exposes no process, path, file, command or registry operation. Runtime IPC and elevation are unchanged by this task. |

## Verification

- Both pre-existing broker assembly contract tests passed in 3.138 s before edits.
- First-party references from Broker.Protocol to Actions and Core reuse the
  compiled action contract and domain identifiers. Force-evaluate and locked
  restore changed only three project-reference lock closures. The Release
  build passed with zero warnings after correcting the version property to use
  an initialized getter, which resolved the first build's `CA1822` diagnostic.
- The focused `ProtocolTests` command passed 46 cases in 4.242 s. It checks
  every kind, the exact request digest bindings, size and depth, strict values,
  malformed UTF-8, reserved job IDs, duplicate and unknown fields, planning-only
  actions and sanitized errors. The deterministic corpus adds 128 bounded
  arbitrary-invalid byte inputs. An ambiguous empty-array test call was made
  explicit before the focused pass.
- PowerShell 7.6.9 `Test-Json -SchemaFile` accepted seven valid message kinds
  and rejected 25 invalid vectors. The schema SHA-256 is
  `bc74d907f44c3a1bde80e747bf002c9dde955d460df78d3be753efd908ac6254`.
  The repository bundle passed its standard structural validator. The isolated
  runtime parser tests separately cover exact UTF-8, lexical and byte bounds.
- Broker and Actions Targeted passed 1,317 .NET tests in 76.395 s, including
  Quick. The source tree `a36302f32940281c32e1b0cadfefb045ec932fdc`
  matches pushed checkpoint `94c211bab046beebff3c5cfb13419c557a4ff8c8`.
- Clean `./eng/verify.ps1 -Tier Full -Restore` on the same tree passed in
  338.374 s. It ran 287 governance and 1,891 .NET tests. Locked restore,
  formatting and the warnings-as-errors Release build passed. The task has
  no named Extended trigger.

## Limits

This evidence covers the codec and schema. The broker executable still has its
existing stub entry point. The authenticated named pipe, caller and server
identity, replay state, monotonic deadlines and phase enforcement, UAC, and
process teardown belong to TL-0311 and TL-0312. No executable or installer
ran through this protocol. No job data migration, account, external package,
telemetry, or release interface was added.
