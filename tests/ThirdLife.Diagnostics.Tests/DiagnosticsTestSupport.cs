using ThirdLife.Diagnostics.Logging;

namespace ThirdLife.Diagnostics.Tests;

internal sealed class FixedTimeProvider : TimeProvider
{
    public FixedTimeProvider(DateTimeOffset utcNow)
    {
        UtcNow = utcNow.ToUniversalTime();
    }

    public DateTimeOffset UtcNow { get; set; }

    public override DateTimeOffset GetUtcNow() => UtcNow;
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"thirdlife-diagnostics-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        var fullPath = System.IO.Path.GetFullPath(Path);
        var tempRoot = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        if (!fullPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) ||
            !System.IO.Path.GetFileName(fullPath).StartsWith(
                "thirdlife-diagnostics-tests-",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The test directory is outside the verified temporary root.");
        }

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }
}

internal static class DiagnosticEventFactory
{
    public static StructuredDiagnosticEvent Completed(
        DateTimeOffset occurredAtUtc,
        string suffix = "00000000000000000000000000000001",
        string? buildVersion = null) =>
        StructuredDiagnosticEvent.CreateForTesting(
            DiagnosticEventCode.OperationCompleted,
            DiagnosticComponent.Diagnostics,
            DiagnosticPhase.Persist,
            DiagnosticSeverity.Information,
            DiagnosticCorrelationId.CreateForTesting($"correlation{suffix}"),
            [
                DiagnosticEventField.OperationType(DiagnosticOperationType.PersistEvent),
                DiagnosticEventField.ResultCode(DiagnosticResultCode.Succeeded),
                DiagnosticEventField.DurationMilliseconds(125),
                DiagnosticEventField.BoundedCount(1),
            ],
            $"event{suffix}",
            occurredAtUtc,
            buildVersion);
}
