namespace ThirdLife.Diagnostics.Logging;

public sealed class SanitizedFailure
{
    internal SanitizedFailure(
        DiagnosticResultCode resultCode,
        DiagnosticErrorCategory errorCategory,
        bool retryable,
        bool durableStateAmbiguous)
    {
        ResultCode = resultCode;
        ErrorCategory = errorCategory;
        Retryable = retryable;
        DurableStateAmbiguous = durableStateAmbiguous;
    }

    public DiagnosticResultCode ResultCode { get; }

    public DiagnosticErrorCategory ErrorCategory { get; }

    public bool Retryable { get; }

    public bool DurableStateAmbiguous { get; }

    public string DisplayMessage => ErrorCategory switch
    {
        DiagnosticErrorCategory.Cancellation => "The operation was cancelled. Review the current state before continuing.",
        DiagnosticErrorCategory.Timeout => "The operation did not finish in time. Review the current state before retrying.",
        DiagnosticErrorCategory.AccessDenied => "The operation was not permitted. No access restriction was bypassed.",
        DiagnosticErrorCategory.IoFailure => "The operation could not complete its local data step. Review the current state.",
        DiagnosticErrorCategory.InvalidInput => "The operation rejected invalid input. No untrusted detail was retained.",
        DiagnosticErrorCategory.InvalidState => "The operation could not continue from the current state.",
        DiagnosticErrorCategory.UnexpectedFailure => "An unexpected failure occurred. Review the current state before continuing.",
        _ => "A diagnostic failure occurred. Review the current state before continuing.",
    };
}

public static class ExceptionSanitizer
{
    public static SanitizedFailure Sanitize(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            OperationCanceledException => Failure(
                DiagnosticResultCode.OperationCancelled,
                DiagnosticErrorCategory.Cancellation,
                retryable: false),
            TimeoutException => Failure(
                DiagnosticResultCode.OperationTimedOut,
                DiagnosticErrorCategory.Timeout,
                retryable: true),
            UnauthorizedAccessException => Failure(
                DiagnosticResultCode.AccessDenied,
                DiagnosticErrorCategory.AccessDenied,
                retryable: false),
            IOException => Failure(
                DiagnosticResultCode.IoFailure,
                DiagnosticErrorCategory.IoFailure,
                retryable: true),
            ArgumentException => Failure(
                DiagnosticResultCode.InvalidInput,
                DiagnosticErrorCategory.InvalidInput,
                retryable: false),
            InvalidOperationException => Failure(
                DiagnosticResultCode.InvalidState,
                DiagnosticErrorCategory.InvalidState,
                retryable: false),
            _ => Failure(
                DiagnosticResultCode.UnexpectedFailure,
                DiagnosticErrorCategory.UnexpectedFailure,
                retryable: false),
        };
    }

    private static SanitizedFailure Failure(
        DiagnosticResultCode resultCode,
        DiagnosticErrorCategory category,
        bool retryable) =>
        new(resultCode, category, retryable, durableStateAmbiguous: true);
}
