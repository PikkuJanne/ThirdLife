# Broker launch trust boundary

TL-0311 has a local broker transport and a development apphost. The
production client returns `Unavailable` before requesting elevation. The
UI build carries the protocol assembly. The development apphost is a separate
build output, remains `asInvoker`, and rejects an unelevated launch. Running
that apphost elevated is outside this candidate's verified scope.

## Startup must be trusted before managed code

The current apphost is framework-dependent. An elevated managed `Main` is
too late to check which executable, runtime or managed startup code Windows
has already loaded. The .NET host can select runtimes from environment-defined
locations, and .NET startup hooks run before `Main`. The documented Windows
shell elevation API does not accept an environment block. A fixed executable
name or hashing the IPC request cannot secure that earlier startup boundary.
The build output and its dependencies also sit in a developer-writable tree.

[.NET runtime discovery](https://github.com/dotnet/docs/blob/main/docs/core/runtime-discovery/troubleshoot-app-launch.md) ·
[.NET startup hook design](https://github.com/dotnet/runtime/blob/main/docs/design/features/host-startup-hook.md) ·
[Windows shell elevation](https://learn.microsoft.com/en-us/windows/win32/shell/launch) ·
[ShellExecuteEx parameters](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-shellexecuteinfoa)

## Required launch evidence

The next candidate needs a supported way to start only approved code before
the first instruction gains elevation. A possible route is a C# Native AOT
broker from a protected, integrity-checked deployment, with evidence for
runtime startup, Windows native loader behavior and deployment permissions.
The Windows native toolchain and exact AOT inputs need provenance and local
verification before choosing that route. The current machine has the pinned
.NET SDK and lacks the Windows C++ build tools and AOT packages needed by
that candidate. [Windows Native AOT requirements](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/?tabs=windows)

When that precondition is satisfied, preserve explicit Windows consent with
the exact fixed candidate and retest real accept, decline, token, peer
identity, and teardown under the active-machine rule. TL-0314 retains the
accountable privilege-boundary decision. An unavailable launch produces no
terminal action and leaves the approval and action journal available to the
unelevated workflow.
