using System.IO;
using System.Security;
using ThirdLife.Diagnostics;
using ThirdLife.Diagnostics.Logging;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private static readonly SanitizedLogStoreOptions PackageLogOptions = new(maximumBytes: 1024 * 1024);
    private readonly Func<StructuredDiagnosticEvent, CancellationToken, Task>? _appendPackageDiagnostic;
    private Task<bool>? _pendingPackageDiagnostic;
    private bool _packageDiagnosticUnavailable;

    private async Task RecordPackageDiagnosticAsync(DiagnosticPhase phase, DiagnosticResultCode? result,
        DiagnosticCorrelationId correlation)
    {
        if (_appendPackageDiagnostic is not { } append || result is not { } code)
        {
            return;
        }

        if (_pendingPackageDiagnostic is { IsCompleted: false })
        {
            _packageDiagnosticUnavailable = true;
            return;
        }

        // Keep the saved outcome available while the log store completes any outstanding I/O.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            var diagnostic = PackageDiagnostic.Create(phase, code, correlation);
            var token = deadline.Token;
            _pendingPackageDiagnostic = Task.Run(() => TryAppendPackageDiagnosticAsync(append, diagnostic, token));
            _packageDiagnosticUnavailable = !await _pendingPackageDiagnostic.WaitAsync(token);
        }
        catch (Exception error) when (error is OperationCanceledException or ArgumentException)
        {
            _packageDiagnosticUnavailable = true;
        }
    }

    private static async Task<bool> TryAppendPackageDiagnosticAsync(
        Func<StructuredDiagnosticEvent, CancellationToken, Task> append, StructuredDiagnosticEvent diagnostic, CancellationToken token)
    {
        try
        {
            await append(diagnostic, token).ConfigureAwait(false);
            return true;
        }
        catch (Exception error) when (error is DiagnosticContractException or OperationCanceledException or
            IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            return false;
        }
    }

    private static async Task AppendLocalPackageDiagnosticAsync(StructuredDiagnosticEvent diagnostic, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var store = SanitizedLogStore.OpenDefault(PackageLogOptions);
        var logger = new StructuredDiagnosticLogger(store);
        await logger.AppendAsync(diagnostic, cancellationToken).ConfigureAwait(false);
    }
}
