using System.Text;
using System.Text.Json;
using ThirdLife.Diagnostics.Logging;

namespace ThirdLife.Diagnostics.Tests;

public sealed class PackageDiagnosticTests
{
    [Theory]
    [InlineData(DiagnosticPhase.Plan, DiagnosticResultCode.PackageResolutionConflict, "package_resolution_conflict", "package_resolution", false)]
    [InlineData(DiagnosticPhase.Plan, DiagnosticResultCode.PackageResolutionUnavailable, "package_resolution_unavailable", "package_resolution", false)]
    [InlineData(DiagnosticPhase.Plan, DiagnosticResultCode.PackageTrustMismatch, "package_trust_mismatch", "package_trust", false)]
    [InlineData(DiagnosticPhase.Plan, DiagnosticResultCode.PackageDownloadTimedOut, "package_download_timed_out", "package_download", false)]
    [InlineData(DiagnosticPhase.Plan, DiagnosticResultCode.PackageCancelled, "package_cancelled", "cancellation", false)]
    [InlineData(DiagnosticPhase.Execute, DiagnosticResultCode.PackageElevationDeclined, "package_elevation_declined", "package_elevation", false)]
    [InlineData(DiagnosticPhase.Execute, DiagnosticResultCode.PackageElevationUnavailable, "package_elevation_unavailable", "package_elevation", false)]
    [InlineData(DiagnosticPhase.Execute, DiagnosticResultCode.PackageDownloadNetworkUnavailable, "package_download_network_unavailable", "package_download", true)]
    [InlineData(DiagnosticPhase.Execute, DiagnosticResultCode.PackageDownloadUnavailable, "package_download_unavailable", "package_download", false)]
    [InlineData(DiagnosticPhase.Execute, DiagnosticResultCode.PackagePreconditionChanged, "package_precondition_changed", "package_resolution", false)]
    [InlineData(DiagnosticPhase.Execute, DiagnosticResultCode.PackageInstallerFailed, "package_installer_failed", "package_installer", false)]
    [InlineData(DiagnosticPhase.Execute, DiagnosticResultCode.PackageInstallerTimedOut, "package_installer_timed_out", "package_installer", false, "durable_state_ambiguous")]
    [InlineData(DiagnosticPhase.Execute, DiagnosticResultCode.PackageInstallerInterrupted, "package_installer_interrupted", "package_installer", false, "durable_state_ambiguous")]
    [InlineData(DiagnosticPhase.Execute, DiagnosticResultCode.PackageRestartRequired, "package_restart_required", null, false)]
    [InlineData(DiagnosticPhase.Verify, DiagnosticResultCode.PackageDetectionAbsent, "package_detection_absent", "package_detection", false)]
    [InlineData(DiagnosticPhase.Verify, DiagnosticResultCode.PackageDetectionMismatch, "package_detection_mismatch", "package_detection", false)]
    [InlineData(DiagnosticPhase.Verify, DiagnosticResultCode.PackageDetectionUnavailable, "package_detection_unavailable", "package_detection", false)]
    [InlineData(DiagnosticPhase.Verify, DiagnosticResultCode.PackageLaunchExecutableMissing, "package_launch_executable_missing", "package_launch", false)]
    [InlineData(DiagnosticPhase.Verify, DiagnosticResultCode.PackageLaunchExecutableUntrusted, "package_launch_executable_untrusted", "package_launch", false)]
    [InlineData(DiagnosticPhase.Verify, DiagnosticResultCode.PackageLaunchFailed, "package_launch_failed", "package_launch", false)]
    [InlineData(DiagnosticPhase.Verify, DiagnosticResultCode.PackageLaunchExitedEarly, "package_launch_exited_early", "package_launch", false)]
    [InlineData(DiagnosticPhase.Verify, DiagnosticResultCode.PackageLaunchTimedOut, "package_launch_timed_out", "package_launch", false)]
    [InlineData(DiagnosticPhase.Verify, DiagnosticResultCode.PackageVerificationCancelled, "package_verification_cancelled", "cancellation", false)]
    [InlineData(DiagnosticPhase.Verify, DiagnosticResultCode.PackageLaunchCleanupUnconfirmed, "package_launch_cleanup_unconfirmed", "package_launch", false, "cleanup_incomplete")]
    public void PackageOutcomesRoundTripWithExactCodesAndSupportFields(
        DiagnosticPhase phase,
        DiagnosticResultCode result,
        string resultCode,
        string? category,
        bool retryable,
        string? limitation = null)
    {
        var operationId = Guid.NewGuid();
        var diagnostic = PackageDiagnostic.Create(phase, result, DiagnosticCorrelationId.FromOperation(operationId));
        var persisted = diagnostic.GetUtf8Json();

        Assert.True(PersistedDiagnosticRecordValidator.IsCanonical(persisted, diagnostic.OccurredAtUtc));
        using var local = JsonDocument.Parse(persisted);
        Assert.Equal("package_outcome", local.RootElement.GetProperty("event_code").GetString());
        Assert.Equal(operationId.ToString("N"), local.RootElement.GetProperty("correlation_id").GetString());
        Assert.Equal(retryable, local.RootElement.GetProperty("retryable").GetBoolean());

        var component = phase == DiagnosticPhase.Verify ? "verification" : "packages";
        var operation = phase switch
        {
            DiagnosticPhase.Plan => "resolve_package",
            DiagnosticPhase.Execute => "install_package",
            DiagnosticPhase.Verify => "verify_package",
            _ => throw new ArgumentOutOfRangeException(nameof(phase)),
        };
        var expected = $"{{\"result_code\":\"{resultCode}\",\"component_id\":\"{component}\",\"operation_type\":\"{operation}\"" +
            (category is null ? string.Empty : $",\"sanitized_error_category\":\"{category}\"") +
            (retryable ? ",\"retryable\":true" : ",\"retryable\":false") +
            (limitation is null ? string.Empty : $",\"limitation_code\":\"{limitation}\"") + "}";
        var projection = PackageDiagnostic.ProjectForSupport(diagnostic);
        Assert.Equal(expected, Encoding.UTF8.GetString(projection.GetUtf8Json()));
    }

    [Theory]
    [InlineData(DiagnosticPhase.Verify, DiagnosticResultCode.PackageTrustMismatch)]
    [InlineData(DiagnosticPhase.Plan, DiagnosticResultCode.PackageInstallerTimedOut)]
    [InlineData(DiagnosticPhase.Execute, DiagnosticResultCode.PackageLaunchFailed)]
    [InlineData(DiagnosticPhase.Verify, DiagnosticResultCode.Succeeded)]
    [InlineData((DiagnosticPhase)int.MaxValue, DiagnosticResultCode.PackageLaunchFailed)]
    [InlineData(DiagnosticPhase.Execute, (DiagnosticResultCode)int.MaxValue)]
    public void RejectsUnregisteredPhaseAndResultCombinations(DiagnosticPhase phase, DiagnosticResultCode result)
    {
        Assert.Throws<ArgumentException>(() => PackageDiagnostic.Create(phase, result, DiagnosticCorrelationId.CreateRandom()));
    }

    [Fact]
    public async Task ProtectedLogRejectsAdditionalFieldsAndSupportProjectionUsesOnlyNormalizedData()
    {
        using var temporary = new TemporaryDirectory();
        var diagnostic = PackageDiagnostic.Create(DiagnosticPhase.Execute, DiagnosticResultCode.PackageInstallerFailed,
            DiagnosticCorrelationId.CreateRandom());
        var logDirectory = Path.Combine(temporary.Path, "logs");
        var time = new FixedTimeProvider(diagnostic.OccurredAtUtc);
        await using var store = SanitizedLogStore.OpenForTesting(logDirectory, new SanitizedLogStoreOptions(64 * 1024), time);

        await new StructuredDiagnosticLogger(store).AppendAsync(diagnostic);
        var persisted = Assert.Single(await store.ReadOwnedRecordsForTestingAsync());
        Assert.Equal(diagnostic.GetUtf8Json(), persisted);
        var text = Encoding.UTF8.GetString(persisted);

        const string hostileBackend = "SYNTH-USER C:\\Users\\SYNTH-USER\\private SYNTH-WIFI 192.0.2.4 SYNTH-SERIAL " +
            "https://download.invalid/package?token=synthetic \u0000\u202e =HYPERLINK(<fixture>)";
        var injectedLog = Encoding.UTF8.GetBytes(text[..^1] + ",\"message\":" + JsonSerializer.Serialize(hostileBackend) + "}");
        Assert.False(PersistedDiagnosticRecordValidator.IsCanonical(injectedLog, diagnostic.OccurredAtUtc));
        Assert.False(PersistedDiagnosticRecordValidator.IsCanonical(
            Encoding.UTF8.GetBytes(text.Replace("\"retryable\":false", "\"retryable\":true", StringComparison.Ordinal)), diagnostic.OccurredAtUtc));

        var support = PackageDiagnostic.ProjectForSupport(diagnostic);
        Assert.Equal("""
            {"result_code":"package_installer_failed","component_id":"packages","operation_type":"install_package","sanitized_error_category":"package_installer","retryable":false}
            """, Encoding.UTF8.GetString(support.GetUtf8Json()));
        Assert.Throws<ArgumentException>(() => PackageDiagnostic.ProjectForSupport(DiagnosticEventFactory.Completed(time.UtcNow)));
    }
}
