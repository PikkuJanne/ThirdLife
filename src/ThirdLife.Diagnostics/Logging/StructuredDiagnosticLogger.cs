namespace ThirdLife.Diagnostics.Logging;

public enum DiagnosticWriteStatus
{
    Written,
    WriteStateAmbiguous,
    Cancelled,
    Unavailable,
}

public sealed class UnhandledExceptionDiagnosticResult
{
    internal UnhandledExceptionDiagnosticResult(
        SanitizedFailure failure,
        DiagnosticWriteStatus writeStatus)
    {
        Failure = failure;
        WriteStatus = writeStatus;
    }

    public SanitizedFailure Failure { get; }

    public DiagnosticWriteStatus WriteStatus { get; }
}

public sealed class StructuredDiagnosticLogger
{
    private readonly SanitizedLogStore _store;

    public StructuredDiagnosticLogger(SanitizedLogStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<SanitizedLogAppendResult> AppendAsync(
        StructuredDiagnosticEvent diagnosticEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        return _store.AppendAsync(diagnosticEvent, cancellationToken);
    }

    public async Task<UnhandledExceptionDiagnosticResult> RecordUnhandledExceptionAsync(
        Exception exception,
        DiagnosticComponent component,
        DiagnosticPhase phase,
        CancellationToken cancellationToken = default)
    {
        return await RecordUnhandledExceptionCoreAsync(
            exception,
            component,
            phase,
            DiagnosticCorrelationId.CreateRandom(),
            cancellationToken).ConfigureAwait(false);
    }

    internal Task<UnhandledExceptionDiagnosticResult> RecordUnhandledExceptionForTestingAsync(
        Exception exception,
        DiagnosticComponent component,
        DiagnosticPhase phase,
        DiagnosticCorrelationId correlationId,
        CancellationToken cancellationToken = default) =>
        RecordUnhandledExceptionCoreAsync(
            exception,
            component,
            phase,
            correlationId,
            cancellationToken);

    private async Task<UnhandledExceptionDiagnosticResult> RecordUnhandledExceptionCoreAsync(
        Exception exception,
        DiagnosticComponent component,
        DiagnosticPhase phase,
        DiagnosticCorrelationId correlationId,
        CancellationToken cancellationToken)
    {
        var failure = ExceptionSanitizer.Sanitize(exception);

        try
        {
            var diagnosticEvent = StructuredDiagnosticEvent.Create(
                DiagnosticEventCode.UnhandledException,
                component,
                phase,
                DiagnosticSeverity.Error,
                correlationId,
                [
                    DiagnosticEventField.ResultCode(failure.ResultCode),
                    DiagnosticEventField.SanitizedErrorCategory(failure.ErrorCategory),
                    DiagnosticEventField.Retryable(failure.Retryable),
                    DiagnosticEventField.LimitationCode(DiagnosticLimitationCode.DurableStateAmbiguous),
                ]);
            _ = await _store.AppendAsync(diagnosticEvent, cancellationToken).ConfigureAwait(false);
            return new UnhandledExceptionDiagnosticResult(failure, DiagnosticWriteStatus.Written);
        }
        catch (OperationCanceledException)
        {
            return new UnhandledExceptionDiagnosticResult(failure, DiagnosticWriteStatus.Cancelled);
        }
        catch (DiagnosticContractException diagnosticException) when (diagnosticException.DurableStateAmbiguous)
        {
            return new UnhandledExceptionDiagnosticResult(failure, DiagnosticWriteStatus.WriteStateAmbiguous);
        }
        catch (DiagnosticContractException)
        {
            return new UnhandledExceptionDiagnosticResult(failure, DiagnosticWriteStatus.Unavailable);
        }
        catch (ArgumentException)
        {
            return new UnhandledExceptionDiagnosticResult(failure, DiagnosticWriteStatus.Unavailable);
        }
    }
}
