# TL-0312 broker security test plan

The verified scope is the reviewed Native AOT broker for one authenticated batch of
compiled no-op actions on Windows 11 client, native x64. Use the active Codex machine
and its existing isolated guest for runtime observations. The managed fixtures check
protocol and session behavior locally. [Security requirements](../../SECURITY.md#7-privileged-broker-requirements) ·
[Broker protocol](../../docs/security/broker-protocol.md) ·
[Launch trust boundary](../../docs/security/broker-launch.md)

## Coverage and evidence limits

| Abuse case | Focused check and expected result | Scope and remaining evidence |
| --- | --- | --- |
| AC-001 and AC-002 · Executable data, unknown actions and invalid frames | `BrokerRequestSecurityTests.RejectsExecutionFieldsWithoutDisclosingInput` rejects inputs for executables, commands, URLs and registry paths. `ProtocolTests` verifies the closed v1 envelope and action vocabulary. `IpcIntegrationTests` checks the frame length before reading a body. | The codec accepts only typed no-ops. Any future action that receives new inputs needs its own validation and evidence. |
| AC-003 · Stale or mismatched session authority | `IpcIntegrationTests` and `ProtocolTests` check bound content and expiry, reject a second request, and preserve a single authorized batch. The guest `Timeout` case observes expiration of an authenticated no-op session. | Planning tests cover the stored plan and journal contracts. TL-0313 owns their integration into application execution and recovery. See [FI-011](../../docs/testing/failure-injection.md#5-canonical-scenario-and-invocation-register). |
| AC-004 · Cross-account access and peer identity | `WindowsPipeHasAProtectedLogonAclAndKernelIdentifiesBothPeers` checks the native ACL and process/session identity. `SessionRechecksPeerAuthenticationBeforeDispatch` requires a fresh peer decision at dispatch and rejects the batch without progress or results if it fails. TL-0317 `DistinctAccount` observes real Windows ACL denial. | The guest exercised a different standard local logon token against the production pipe implementation, then verified the initiating account could still connect. The separate-process attempt did not establish a result. |
| AC-007 · Untrusted path and filesystem object | `InstalledBrokerTests` and `tools.tests.test_broker_deployment` cover fixed installation identity, protected ownership and permissions, and reviewed provisioning guards. The guest `Verify` case ran the launch guard and observed standard-user denial of writes to the protected binary and metadata. | Current actions accept no filesystem path and write no temporary action output. Qualify additional destinations, link handling and interruption semantics before any future file-mutating action. |
| AC-008 · Diagnostic disclosure | The focused request test and `ProtocolTests` require a fixed diagnostic for rejected inputs. Review `BrokerNonce` redaction, ticket contents, broker exit codes and the guest runner's bounded diagnostic projection against the logging contract. | Assert without echoing request bytes or nonce values. Keep raw local logs, traces, screenshots and credentials out of stored evidence. This scope covers broker diagnostics and the test result projection. |
| AC-009 · Decline, cancellation, interruption and process lifetime | `BufferedCancellationProducesOnlyItsScopedNoOpResult` verifies scoped cancellation. UI client fixtures verify consent mapping and result retention. Guest `Decline`, `Interrupt`, `Timeout`, `Disconnect` and `RetainObservation` provide protected-runtime evidence for return state, process exit and cleanup. | Prior failed guest runs retain their results and cleanup findings. TL-0313 owns durable job continuity, actual UI exit and reopen, and accessible recovery. Future mutation or power-loss scenarios require their own reconciliation evidence. See [FI-005 and FI-006](../../docs/testing/failure-injection.md#5-canonical-scenario-and-invocation-register). |
| AC-014 · Input and lifetime bounds | The named Extended selection below exercises bounded seeded inputs offline and requires the fixed decoder diagnostic. Transport tests check framing and request bounds. Guest `Timeout` observes bounded protected process lifetime. | The fixture measures the fixed parser workload. Workload-wide resource measurements and future mutating actions need their own evidence. |

Case IDs refer to the [broker-relevant abuse cases](../../docs/security/abuse-cases.md).

## Invocation

Run the deterministic suite from the repository root using the pinned SDK and locked
dependencies. Targeted includes Quick and excludes interactive and Sandbox-only cases.

```powershell
$projects = @(
    'tests/ThirdLife.Actions.Tests/ThirdLife.Actions.Tests.csproj'
    'tests/ThirdLife.Broker.SecurityTests/ThirdLife.Broker.SecurityTests.csproj'
    'tests/ThirdLife.Broker.Tests/ThirdLife.Broker.Tests.csproj'
    'tests/ThirdLife.UI.Tests/ThirdLife.UI.Tests.csproj'
)
.\eng\verify.ps1 -Tier Targeted -Restore -TestProject $projects
```

Run the bounded parser scenario independently for Extended evidence. It uses an
existing deterministic, in-memory fixture with fixed seeded data and no live endpoint.

```powershell
$project = 'tests/ThirdLife.Broker.Tests/ThirdLife.Broker.Tests.csproj'
$case = 'ThirdLife.Broker.Tests.ProtocolTests.BoundedSeededInvalidInputsAlwaysUseTheSanitizedDecoderError'
dotnet test $project -c Release -p:RestoreLockedMode=true --filter "FullyQualifiedName=$case" -- RunConfiguration.TreatNoTestsAsError=true
```

Follow the protected Windows guest procedure for any required repeat of a named live
case. It binds the reviewed artifact and admitted guest, checks the limited client
and elevated broker identities where applicable, and records UAC prompt observation,
the chosen Windows response, the resulting client state and owned cleanup separately.
Reuse unchanged evidence with its original provenance. TL-0317 recorded the no-op
accept, decline and interruption against its reviewed binary, the corrected result
retention case at `8ecfe22`, and the corrected distinct-account case at `d74ca7f`.
The final guest inspection found no remaining scenario processes, tasks, temporary
accounts or run directories. The protected candidate and staging root were retained
by design. [TL-0317 procedure and evidence](../evidence/TL-0317/report.md) ·
[TL-0311 host evidence](../evidence/TL-0311/report.md)

Run Full on a clean active-machine checkout when the protected integration or
security-boundary trigger applies. Retain the exact source and artifact, environment,
case selection, result, elapsed time, cleanup and any changed claim limits in
[the task evidence](../evidence/TL-0312/report.md). [Testing requirements](../../TESTING.md)

## Residual risk and decisions

RR-001 remains applicable to administrator or kernel compromise and filesystem races.
RR-008 retains the limits of the measured IPC workload and resource evidence.
RR-003 remains relevant when any future action can leave a partial mutation. RR-004
belongs to the application approval experience and accountable privilege decision.
The observed development UAC prompt identified an unknown publisher. TL-0314 owns the
decision on the candidate, authorized scope and residual risks. New live elevation
requires the applicable action authority and Windows consent. [Residual-risk register](../../docs/security/threat-model.md#residual-risk-register) ·
[Task prerequisites](../../TASKS.yaml)
