using System.Text.Json;
using ThirdLife.Diagnostics.Logging;

namespace ThirdLife.Diagnostics.Tests;

public sealed class StructuredEventContractTests
{
    private static readonly DateTimeOffset Timestamp = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RegisteredEventSerializesOnlyItsTypedDeterministicEnvelope()
    {
        var diagnosticEvent = DiagnosticEventFactory.Completed(Timestamp);
        var first = diagnosticEvent.GetUtf8Json();
        var second = diagnosticEvent.GetUtf8Json();

        Assert.Equal(first, second);
        using var document = JsonDocument.Parse(first);
        var root = document.RootElement;
        Assert.Equal(14, root.EnumerateObject().Count());
        Assert.Equal(StructuredDiagnosticEvent.CurrentSchemaVersion, root.GetProperty("schema_version").GetString());
        Assert.Equal("operation_completed", root.GetProperty("event_code").GetString());
        Assert.Equal("diagnostics", root.GetProperty("component").GetString());
        Assert.Equal("persist", root.GetProperty("phase").GetString());
        Assert.Equal("information", root.GetProperty("severity").GetString());
        Assert.Equal("diagnostic.operation_completed", root.GetProperty("safe_message_key").GetString());
        Assert.Equal("persist_event", root.GetProperty("operation_type").GetString());
        Assert.Equal("succeeded", root.GetProperty("result_code").GetString());
        Assert.Equal(125, root.GetProperty("duration_ms").GetInt64());
        Assert.Equal(1, root.GetProperty("bounded_count").GetInt64());

        Assert.False(root.TryGetProperty("message", out _));
        Assert.False(root.TryGetProperty("details", out _));
        Assert.False(root.TryGetProperty("payload", out _));
        Assert.False(root.TryGetProperty("command", out _));
    }

    [Fact]
    public void EventContractsRejectMissingDuplicateUnknownAndContradictoryFields()
    {
        var correlation = DiagnosticCorrelationId.CreateForTesting("correlation00000000000000000000000000000001");

        Assert.Equal(
            "diagnostic_event_contract_invalid",
            Assert.Throws<DiagnosticContractException>(() =>
                StructuredDiagnosticEvent.Create(
                    DiagnosticEventCode.OperationCompleted,
                    DiagnosticComponent.Diagnostics,
                    DiagnosticPhase.Persist,
                    DiagnosticSeverity.Information,
                    correlation,
                    [DiagnosticEventField.ResultCode(DiagnosticResultCode.Succeeded)])).ResultCode);

        Assert.Equal(
            "diagnostic_field_duplicate",
            Assert.Throws<DiagnosticContractException>(() =>
                StructuredDiagnosticEvent.Create(
                    DiagnosticEventCode.OperationCompleted,
                    DiagnosticComponent.Diagnostics,
                    DiagnosticPhase.Persist,
                    DiagnosticSeverity.Information,
                    correlation,
                    [
                        DiagnosticEventField.OperationType(DiagnosticOperationType.PersistEvent),
                        DiagnosticEventField.ResultCode(DiagnosticResultCode.Succeeded),
                        DiagnosticEventField.ResultCode(DiagnosticResultCode.Succeeded),
                    ])).ResultCode);

        Assert.Equal(
            "diagnostic_event_envelope_invalid",
            Assert.Throws<DiagnosticContractException>(() =>
                StructuredDiagnosticEvent.Create(
                    DiagnosticEventCode.UnhandledException,
                    DiagnosticComponent.Diagnostics,
                    DiagnosticPhase.Persist,
                    DiagnosticSeverity.Information,
                    correlation,
                    [
                        DiagnosticEventField.ResultCode(DiagnosticResultCode.UnexpectedFailure),
                        DiagnosticEventField.SanitizedErrorCategory(DiagnosticErrorCategory.UnexpectedFailure),
                        DiagnosticEventField.Retryable(false),
                        DiagnosticEventField.LimitationCode(DiagnosticLimitationCode.DurableStateAmbiguous),
                    ])).ResultCode);

        Assert.Equal(
            "diagnostic_event_envelope_invalid",
            Assert.Throws<DiagnosticContractException>(() =>
                StructuredDiagnosticEvent.Create(
                    DiagnosticEventCode.RetentionCleanup,
                    DiagnosticComponent.Core,
                    DiagnosticPhase.Persist,
                    DiagnosticSeverity.Information,
                    correlation,
                    [
                        DiagnosticEventField.ResultCode(DiagnosticResultCode.CleanupCompleted),
                        DiagnosticEventField.BoundedCount(0),
                    ])).ResultCode);
    }

    [Fact]
    public void EventFieldEnumerationStopsAtTheEighthItem()
    {
        var enumerated = 0;

        IEnumerable<DiagnosticEventField> Unbounded()
        {
            while (true)
            {
                enumerated++;
                yield return DiagnosticEventField.BoundedCount(enumerated);
            }
        }

        var exception = Assert.Throws<DiagnosticContractException>(() =>
            StructuredDiagnosticEvent.Create(
                DiagnosticEventCode.OperationCompleted,
                DiagnosticComponent.Diagnostics,
                DiagnosticPhase.Persist,
                DiagnosticSeverity.Information,
                DiagnosticCorrelationId.CreateForTesting("correlation00000000000000000000000000000001"),
                Unbounded()));

        Assert.Equal("diagnostic_field_count_exceeded", exception.ResultCode);
        Assert.Equal(8, enumerated);
    }

    [Fact]
    public void EventSemanticsRejectContradictorySafeCodes()
    {
        var correlation = DiagnosticCorrelationId.CreateForTesting("correlation00000000000000000000000000000001");

        AssertInvalid(
            DiagnosticEventCode.OperationCompleted,
            DiagnosticSeverity.Information,
            [
                DiagnosticEventField.OperationType(DiagnosticOperationType.PersistEvent),
                DiagnosticEventField.ResultCode(DiagnosticResultCode.UnexpectedFailure),
            ]);
        AssertInvalidContract(
            DiagnosticEventCode.OperationCompleted,
            DiagnosticSeverity.Warning,
            [
                DiagnosticEventField.OperationType(DiagnosticOperationType.PersistEvent),
                DiagnosticEventField.ResultCode(DiagnosticResultCode.Succeeded),
                DiagnosticEventField.LimitationCode(DiagnosticLimitationCode.InputRejected),
            ]);
        AssertInvalid(
            DiagnosticEventCode.OperationFailed,
            DiagnosticSeverity.Error,
            [
                DiagnosticEventField.OperationType(DiagnosticOperationType.PersistEvent),
                DiagnosticEventField.ResultCode(DiagnosticResultCode.Succeeded),
                DiagnosticEventField.SanitizedErrorCategory(DiagnosticErrorCategory.UnexpectedFailure),
                DiagnosticEventField.Retryable(false),
            ]);
        AssertInvalid(
            DiagnosticEventCode.OperationFailed,
            DiagnosticSeverity.Error,
            [
                DiagnosticEventField.OperationType(DiagnosticOperationType.PersistEvent),
                DiagnosticEventField.ResultCode(DiagnosticResultCode.OperationTimedOut),
                DiagnosticEventField.SanitizedErrorCategory(DiagnosticErrorCategory.IoFailure),
                DiagnosticEventField.Retryable(false),
            ]);
        AssertInvalid(
            DiagnosticEventCode.DiagnosticRejected,
            DiagnosticSeverity.Warning,
            [
                DiagnosticEventField.OperationType(DiagnosticOperationType.NormalizeInput),
                DiagnosticEventField.ResultCode(DiagnosticResultCode.Succeeded),
                DiagnosticEventField.LimitationCode(DiagnosticLimitationCode.InputRejected),
            ]);
        AssertInvalid(
            DiagnosticEventCode.UnhandledException,
            DiagnosticSeverity.Error,
            [
                DiagnosticEventField.ResultCode(DiagnosticResultCode.Succeeded),
                DiagnosticEventField.SanitizedErrorCategory(DiagnosticErrorCategory.UnexpectedFailure),
                DiagnosticEventField.Retryable(false),
                DiagnosticEventField.LimitationCode(DiagnosticLimitationCode.DurableStateAmbiguous),
            ]);

        void AssertInvalid(
            DiagnosticEventCode eventCode,
            DiagnosticSeverity severity,
            DiagnosticEventField[] fields)
        {
            var exception = Assert.Throws<DiagnosticContractException>(() =>
                StructuredDiagnosticEvent.Create(
                    eventCode,
                    DiagnosticComponent.Diagnostics,
                    DiagnosticPhase.Persist,
                    severity,
                    correlation,
                    fields));
            Assert.Equal("diagnostic_event_envelope_invalid", exception.ResultCode);
        }

        void AssertInvalidContract(
            DiagnosticEventCode eventCode,
            DiagnosticSeverity severity,
            DiagnosticEventField[] fields)
        {
            var exception = Assert.Throws<DiagnosticContractException>(() =>
                StructuredDiagnosticEvent.Create(
                    eventCode,
                    DiagnosticComponent.Diagnostics,
                    DiagnosticPhase.Persist,
                    severity,
                    correlation,
                    fields));
            Assert.Equal("diagnostic_event_contract_invalid", exception.ResultCode);
        }
    }

    [Theory]
    [InlineData(DiagnosticOperationType.StartApplication, DiagnosticComponent.Ui, DiagnosticPhase.Execute)]
    [InlineData(DiagnosticOperationType.CollectEvidence, DiagnosticComponent.Inventory, DiagnosticPhase.Collect)]
    [InlineData(DiagnosticOperationType.NormalizeInput, DiagnosticComponent.Core, DiagnosticPhase.Normalize)]
    [InlineData(DiagnosticOperationType.PersistEvent, DiagnosticComponent.Diagnostics, DiagnosticPhase.Persist)]
    [InlineData(DiagnosticOperationType.VerifyState, DiagnosticComponent.Verification, DiagnosticPhase.Verify)]
    [InlineData(DiagnosticOperationType.BuildSupportProjection, DiagnosticComponent.Reports, DiagnosticPhase.Export)]
    [InlineData(DiagnosticOperationType.CleanRetention, DiagnosticComponent.Diagnostics, DiagnosticPhase.Cleanup)]
    [InlineData(DiagnosticOperationType.RecoverState, DiagnosticComponent.Persistence, DiagnosticPhase.Recover)]
    public void EveryRegisteredOperationHasAnExplicitComponentAndPhaseEnvelope(
        DiagnosticOperationType operation,
        DiagnosticComponent component,
        DiagnosticPhase phase)
    {
        var diagnosticEvent = StructuredDiagnosticEvent.Create(
            DiagnosticEventCode.OperationCompleted,
            component,
            phase,
            DiagnosticSeverity.Information,
            DiagnosticCorrelationId.CreateForTesting("correlation00000000000000000000000000000001"),
            [
                DiagnosticEventField.OperationType(operation),
                DiagnosticEventField.ResultCode(DiagnosticResultCode.Succeeded),
            ]);

        Assert.NotEmpty(diagnosticEvent.GetUtf8Json());
    }

    [Theory]
    [InlineData(DiagnosticOperationType.CollectEvidence, DiagnosticComponent.Inventory, DiagnosticPhase.Execute)]
    [InlineData(DiagnosticOperationType.PersistEvent, DiagnosticComponent.Ui, DiagnosticPhase.Persist)]
    [InlineData(DiagnosticOperationType.VerifyState, DiagnosticComponent.Verification, DiagnosticPhase.Collect)]
    [InlineData(DiagnosticOperationType.CleanRetention, DiagnosticComponent.Persistence, DiagnosticPhase.Cleanup)]
    public void ContradictoryOperationComponentAndPhaseEnvelopesFailClosed(
        DiagnosticOperationType operation,
        DiagnosticComponent component,
        DiagnosticPhase phase)
    {
        var exception = Assert.Throws<DiagnosticContractException>(() =>
            StructuredDiagnosticEvent.Create(
                DiagnosticEventCode.OperationCompleted,
                component,
                phase,
                DiagnosticSeverity.Information,
                DiagnosticCorrelationId.CreateForTesting("correlation00000000000000000000000000000001"),
                [
                    DiagnosticEventField.OperationType(operation),
                    DiagnosticEventField.ResultCode(DiagnosticResultCode.Succeeded),
                ]));

        Assert.Equal("diagnostic_event_envelope_invalid", exception.ResultCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(86_400_001)]
    public void NumericDiagnosticValuesAreBounded(long value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DiagnosticEventField.DurationMilliseconds(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiagnosticEventField.BoundedCount(value));
    }
}
