# Windows Update broker v2

TL-0504 uses a dedicated bootstrap mode and a closed message version for one approved download or install. The unelevated caller first completes the version-pinned hello exchange. The broker accepts a single request over that authenticated logon-restricted pipe. It binds the kernel-observed peer PID, session, user and logon to the held client process, and checks the ticket hash against the complete canonical request. Admission and the final native call must start within the two-minute envelope. The session observes a separate thirty-minute operation deadline. The client may request cancellation once for the exact action, including after admission expires. [Broker trust boundary](../adr/0004-ephemeral-broker.md) · [v2 wire schema](../../schemas/windows-update-broker.schema.json)

The request binds the job, intake gate, scan, policy decision, effective servicing-policy digest, original operator approval, and both immutable phase IDs. The broker recomputes SHA-256 over the fixed-order versioned approval document produced by `BrokerWindowsUpdateRequest.ComputeApprovalSha256`. The document includes both compiled impact descriptors and the exact WUA identity, revision, security/critical class and maximum transfer. The selected phase must match its ID and descriptor. Operator attribution stays with the durable job. No paths, observer-provided machine facts, scripts, grant bytes or arbitrary actions are accepted in the request.

The broker selects the effective compiled review independently. At the authorization boundary the native backend supplies the freshly observed exact offer and machine facts from its owned WUA session. The broker checks the reviewed target, recovery, storage and power policy, current deadline, cancellation and real peer again. For supervised qualification it also consumes a separately provisioned one-shot grant. Each action has its own ticket, authorization and receipt. An `attempted` receipt reports the native phase result. `not_started` denotes a refusal before the Begin boundary. `unconfirmed` reports `Partial` with unknown restart and progress when dispatch may have occurred. A broken connection or missing receipt after send leaves the durable intent unresolved. Windows installed state must still be freshly verified by the unelevated application.

## Supervised qualification grant

The qualification authority retains the attributable evidence for the proposed operation and the exact operator approval independently of the UI. It checks the target machine, Windows logon, approved review, phase, recovery and resource envelope before provisioning. The digest fields bind an existing decision. Creating a matching record requires the underlying authority. A normal deployment requires the approved issuance and installation model from TL-0314.

On the admitted native x64 target, the supervised authority uses the system Program Files location and the fixed sibling directory `ThirdLife\ServicingGrants`. Download uses `download.grant` and install uses `install.grant`. The ThirdLife directory, grant directory, and grant file must be owned by built-in Administrators or SYSTEM, with a protected DACL. Only these principals may modify an existing object or replace its children. The grant directory and grant file must also restrict read access to these same principals. The broker checks local fixed boot volume, path, owner, DACL, reparse attributes and single file link on open handles. Provision a fresh slot with exclusive create semantics. Grant the broker sufficient read and delete access. Keep the originating authority evidence and exact grant receipt in the supervised qualification record. Reissuing a consumed grant requires a fresh authorization and action ID after reconciling prior native and durable state.

A grant contains seven ASCII lines in the order below, each terminated by one LF byte. Use no BOM, CR, trailing spaces or optional fields. The complete file is 305 bytes.

| Line | Value |
| --- | --- |
| 1 | Literal `TL-WUA-GRANT-1`. |
| 2 | 64 lowercase hexadecimal characters from the canonical approval SHA-256. |
| 3 | The selected approved action ID, lowercase UUID in hyphenated form. |
| 4 | SHA-256 of the retained attributable qualification authorization, 64 lowercase hexadecimal characters. |
| 5 | SHA-256 of the ASCII string form of the authorized target Windows logon SID, 64 lowercase hexadecimal characters. The broker obtains the comparison SID from the pinned client process token. |
| 6 | Grant issue time in the protocol UTC timestamp format. |
| 7 | Grant expiry in the same UTC format, at most 30 minutes after issue. |

The two deadlines are checked separately. The file remains available if content or final authorization fails. On success the broker marks the same exclusively held file for deletion and closes the handle before returning authorization to the native writer. A crash from this boundary onward can spend the grant without a conclusive servicing outcome. A missing slot fails closed. [Windows handle deletion](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-setfileinformationbyhandle)

No reviewed native offer is active in the checked-in servicing policy. Positive automated dispatch tests use bounded test doubles for machine policy, grant and native calls. File disposition tests use a temporary user-owned fixture. Live servicing requires an applicable reviewed offer, recovery and space evidence, effective privilege and organization authority, explicit operator approval, a matching protected grant for each phase and supervised execution on the existing ThirdLife-W11 guest.

## Protected refusal rehearsal

The opt-in `WindowsUpdateBrokerRefusal` Hyper-V case exercises the actual guarded launcher, elevated NativeAOT broker, pipe handshake and one correlated refusal. Its synthetic offer must have no compiled qualification review. The expected result is `NotStarted` / `ApprovalChanged`, with no progress or restart claim, followed by broker exit. It requires no grant.

From the reviewed clean checkout, `eng/run-thirdlife-hyperv.ps1 -Prepare -WindowsUpdateBrokerRefusal` packages the limited UI test, pinned verifier and guest driver. The binary must already have been independently admitted and installed in the protected guest. Run the scenario with `-Scenario WindowsUpdateBrokerRefusal`, the exact `-ExpectedVMId` from Inspect, the preparation's `-PayloadSha256`, and the qualification review's `-ReviewedWindowsUpdateBrokerSource` and `-ReviewedWindowsUpdateBrokerSha256`. Use the same clean source commit for the tested binary and payload. The runner verifies the installed binary, starts the limited interactive test and records the prompt observation, refusal and cleanup. The operator makes the Windows consent decision on the secure desktop. The existing managed read-only guest assessments run separately without UAC.
