using ThirdLife.Diagnostics.Redaction;

namespace ThirdLife.Diagnostics.Logging;

/// <summary>Builds diagnostics from normalized package outcomes and compiled codes.</summary>
public static class PackageDiagnostic
{
    public static StructuredDiagnosticEvent Create(
        DiagnosticPhase phase,
        DiagnosticResultCode result,
        DiagnosticCorrelationId correlation)
    {
        var definition = Describe(phase, result) ??
            throw new ArgumentException("The package result is not registered for this phase.", nameof(result));

        return StructuredDiagnosticEvent.Create(
            DiagnosticEventCode.PackageOutcome,
            Component(phase),
            phase,
            DiagnosticSeverity.Warning,
            correlation,
            Fields(phase, result, definition));
    }

    /// <summary>Builds a bounded event projection in memory for a separate support preview.</summary>
    public static SupportProjection ProjectForSupport(StructuredDiagnosticEvent diagnosticEvent) =>
        SupportProjection.Create(SupportField.FromPackageDiagnostic(diagnosticEvent));

    internal static bool IsRegisteredEnvelope(
        DiagnosticComponent component,
        DiagnosticPhase phase,
        DiagnosticSeverity severity,
        DiagnosticResultCode result,
        IReadOnlyList<DiagnosticEventField> fields)
    {
        if (Describe(phase, result) is not { } definition ||
            component != Component(phase) || severity != DiagnosticSeverity.Warning)
        {
            return false;
        }

        var expected = Fields(phase, result, definition);
        return fields.Count == expected.Count && expected.All(expectedField =>
            fields.Any(field => field.Name == expectedField.Name && Equals(field.Value, expectedField.Value)));
    }

    private static DiagnosticComponent Component(DiagnosticPhase phase) => phase == DiagnosticPhase.Verify
        ? DiagnosticComponent.Verification : DiagnosticComponent.Packages;

    private static List<DiagnosticEventField> Fields(
        DiagnosticPhase phase, DiagnosticResultCode result, Definition definition)
    {
        var operation = phase switch
        {
            DiagnosticPhase.Plan => DiagnosticOperationType.ResolvePackage,
            DiagnosticPhase.Execute => DiagnosticOperationType.InstallPackage,
            DiagnosticPhase.Verify => DiagnosticOperationType.VerifyPackage,
            _ => throw new ArgumentOutOfRangeException(nameof(phase)),
        };

        var fields = new List<DiagnosticEventField>(5)
        {
            DiagnosticEventField.OperationType(operation),
            DiagnosticEventField.ResultCode(result),
            DiagnosticEventField.Retryable(definition.Retryable),
        };
        if (definition.Category is { } category)
        {
            fields.Add(DiagnosticEventField.SanitizedErrorCategory(category));
        }

        if (definition.Limitation is { } limitation)
        {
            fields.Add(DiagnosticEventField.LimitationCode(limitation));
        }

        return fields;
    }

    private static Definition? Describe(DiagnosticPhase phase, DiagnosticResultCode result) => (phase, result) switch
    {
        (DiagnosticPhase.Plan or DiagnosticPhase.Execute,
            DiagnosticResultCode.PackageResolutionConflict or DiagnosticResultCode.PackageResolutionUnavailable) =>
            new(DiagnosticErrorCategory.PackageResolution),
        (DiagnosticPhase.Plan or DiagnosticPhase.Execute, DiagnosticResultCode.PackageTrustMismatch) =>
            new(DiagnosticErrorCategory.PackageTrust),
        (DiagnosticPhase.Plan or DiagnosticPhase.Execute,
            DiagnosticResultCode.PackageDownloadUnavailable or DiagnosticResultCode.PackageDownloadTimedOut) =>
            new(DiagnosticErrorCategory.PackageDownload),
        (DiagnosticPhase.Plan or DiagnosticPhase.Execute, DiagnosticResultCode.PackageDownloadNetworkUnavailable) =>
            new(DiagnosticErrorCategory.PackageDownload, Retryable: true),
        (DiagnosticPhase.Plan or DiagnosticPhase.Execute, DiagnosticResultCode.PackageCancelled) =>
            new(DiagnosticErrorCategory.Cancellation),
        (DiagnosticPhase.Execute,
            DiagnosticResultCode.PackageElevationDeclined or DiagnosticResultCode.PackageElevationUnavailable) =>
            new(DiagnosticErrorCategory.PackageElevation),
        (DiagnosticPhase.Execute, DiagnosticResultCode.PackagePreconditionChanged) =>
            new(DiagnosticErrorCategory.PackageResolution),
        (DiagnosticPhase.Execute, DiagnosticResultCode.PackageInstallerFailed) =>
            new(DiagnosticErrorCategory.PackageInstaller),
        (DiagnosticPhase.Execute, DiagnosticResultCode.PackageInstallerTimedOut or DiagnosticResultCode.PackageInstallerInterrupted) =>
            new(DiagnosticErrorCategory.PackageInstaller, Limitation: DiagnosticLimitationCode.DurableStateAmbiguous),
        (DiagnosticPhase.Execute, DiagnosticResultCode.PackageRestartRequired) => new(null),
        (DiagnosticPhase.Verify, DiagnosticResultCode.PackageDetectionAbsent or DiagnosticResultCode.PackageDetectionMismatch or
            DiagnosticResultCode.PackageDetectionUnavailable) => new(DiagnosticErrorCategory.PackageDetection),
        (DiagnosticPhase.Verify, DiagnosticResultCode.PackageLaunchExecutableMissing or DiagnosticResultCode.PackageLaunchExecutableUntrusted or
            DiagnosticResultCode.PackageLaunchFailed or DiagnosticResultCode.PackageLaunchExitedEarly or
            DiagnosticResultCode.PackageLaunchTimedOut) =>
            new(DiagnosticErrorCategory.PackageLaunch),
        (DiagnosticPhase.Verify, DiagnosticResultCode.PackageVerificationCancelled) => new(DiagnosticErrorCategory.Cancellation),
        (DiagnosticPhase.Verify, DiagnosticResultCode.PackageLaunchCleanupUnconfirmed) =>
            new(DiagnosticErrorCategory.PackageLaunch, Limitation: DiagnosticLimitationCode.CleanupIncomplete),
        _ => null,
    };

    // A retryable download result permits fresh plan preparation. It does not authorize dispatch.
    private readonly record struct Definition(
        DiagnosticErrorCategory? Category,
        bool Retryable = false,
        DiagnosticLimitationCode? Limitation = null);
}
