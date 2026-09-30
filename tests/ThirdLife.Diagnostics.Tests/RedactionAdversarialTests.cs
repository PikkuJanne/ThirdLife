using System.Text.Json;
using ThirdLife.Diagnostics.Redaction;

namespace ThirdLife.Diagnostics.Tests;

public sealed class RedactionAdversarialTests
{
    private static readonly RedactionFixtureSet Fixtures = RedactionFixtureSet.Load();

    [Theory]
    [InlineData("WiFi-SSID")]
    [InlineData(" WIFI.SSID ")]
    [InlineData("WiFi   SSID")]
    [InlineData("ｗｉｆｉ＿ｓｓｉｄ")]
    public void CaseSeparatorAndCompatibilityVariantsRemainSensitive(string fieldName)
    {
        var result = RedactionEngine.Transform(
            fieldName,
            DiagnosticContext.OrdinaryLog,
            "SYNTHETIC-NETWORK-NOT-REAL");

        Assert.Equal(RedactionAction.Redact, result.Action);
        Assert.Equal("[REDACTED:wifi-ssid]", result.RedactedForm!.GetString());
    }

    [Theory]
    [InlineData("wifi/ssid")]
    [InlineData("wifi\\ssid")]
    [InlineData("wіfi_ssid")]
    [InlineData("wifi_ssid\u202E")]
    [InlineData("wifi_ssid\0")]
    [InlineData("wifi_ssid\u0301")]
    [InlineData("wifi\tssid")]
    [InlineData("wifi\rssid")]
    [InlineData("wifi\nssid")]
    public void ConfusableControlAndUnsupportedSeparatorsFailClosed(string fieldName)
    {
        const string seed = "SYNTHETIC-NETWORK-NOT-REAL";
        var result = RedactionEngine.Transform(fieldName, DiagnosticContext.OrdinaryLog, seed);

        Assert.Equal(RedactionAction.Omit, result.Action);
        Assert.Equal("[OMITTED:unknown-field]", result.RedactedForm!.GetString());
        Assert.DoesNotContain(seed, result.RedactedForm.GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+SUM(A1:A2)")]
    [InlineData("-1")]
    [InlineData("@IMPORT")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("https://packages.example.test/app?token=secret")]
    [InlineData("C:\\Users\\SyntheticPerson\\secret.txt")]
    public void FormulaMarkupUrlAndPathValuesCannotBeRelabeledAsSupportScalars(string value)
    {
        foreach (var fieldName in Fixtures.SupportAllowlist)
        {
            var result = RedactionEngine.Transform(fieldName, DiagnosticContext.SupportExport, value);
            Assert.NotEqual(RedactionAction.PreserveAllowlisted, result.Action);
            Assert.Null(result.ExportedForm);
        }
    }

    [Fact]
    public void EveryProhibitedSeedFailsWhenRelabeledAsAnySupportField()
    {
        var prohibitedSeeds = Fixtures.Cases
            .Where(static fixture => fixture.Action is not RedactionAction.PreserveAllowlisted and
                not RedactionAction.SuppressTelemetry)
            .Select(static fixture => fixture.InputValue)
            .ToArray();

        foreach (var fieldName in Fixtures.SupportAllowlist)
        {
            foreach (var seed in prohibitedSeeds)
            {
                var result = RedactionEngine.Transform(fieldName, DiagnosticContext.SupportExport, seed);
                Assert.True(
                    result.Action != RedactionAction.PreserveAllowlisted,
                    $"Field {fieldName} preserved prohibited seed from fixture input {seed}.");
                Assert.Null(result.ExportedForm);
            }
        }
    }

    [Fact]
    public void NestedDuplicateUnknownAndOversizedCandidatesFailClosedWithoutStringifyingObjects()
    {
        var hostile = new HostileObject();
        var nested = JsonDocument.Parse("{\"nested\":[{\"value\":1}]}").RootElement.Clone();
        var duplicate = JsonDocument.Parse("{\"result_code\":\"one\",\"result_code\":\"two\"}").RootElement.Clone();

        foreach (var candidate in new object[] { hostile, nested, duplicate, new string('A', 65 * 1024) })
        {
            var result = RedactionEngine.Transform(
                "result_code",
                DiagnosticContext.SupportExport,
                candidate);
            Assert.Equal(RedactionAction.Omit, result.Action);
            Assert.Null(result.ExportedForm);
        }

        Assert.False(hostile.ToStringRead);
    }

    [Fact]
    public void SecretRawSiblingAndTelemetryInputsNeverBecomePublicWrappersOrOutput()
    {
        foreach (var field in new[]
        {
            RedactionField.Credential,
            RedactionField.Password,
            RedactionField.RecoveryKey,
            RedactionField.ClipboardSecret,
            RedactionField.RawCommandOutput,
            RedactionField.SiblingPrivateDatabaseRecord,
        })
        {
            Assert.Throws<ArgumentException>(() => SensitiveDiagnosticValue.Create(field, "SYNTHETIC-SECRET"));
        }

        var safeWrapper = SensitiveDiagnosticValue.Create(
            RedactionField.WifiSsid,
            "SYNTHETIC-NETWORK-NOT-REAL");
        Assert.Equal("[SENSITIVE:NOT-FOR-DIAGNOSTICS]", safeWrapper.ToString());
        Assert.Equal("[REDACTED:wifi-ssid]", safeWrapper.ToSafeOrdinaryLogRepresentation()!.ToString());

        var telemetry = RedactionEngine.Transform(
            "result_code",
            DiagnosticContext.Telemetry,
            "synthetic_success");
        Assert.Equal(RedactionAction.SuppressTelemetry, telemetry.Action);
        Assert.Equal(PersistenceDisposition.NoneForTelemetry, telemetry.Persistence);
        Assert.Null(telemetry.ExportedForm);
    }

    [Fact]
    public void SensitiveWrapperBoundIsExactForUtf16CodeUnitsIncludingSurrogatePairs()
    {
        var exactAscii = new string('A', SensitiveDiagnosticValue.MaximumTransientCodeUnits);
        var exactSurrogates = string.Concat(
            Enumerable.Repeat("\U0001F512", SensitiveDiagnosticValue.MaximumTransientCodeUnits / 2));

        Assert.NotNull(SensitiveDiagnosticValue.Create(RedactionField.PersonName, exactAscii));
        Assert.Equal(SensitiveDiagnosticValue.MaximumTransientCodeUnits, exactSurrogates.Length);
        Assert.NotNull(SensitiveDiagnosticValue.Create(RedactionField.PersonName, exactSurrogates));
        Assert.Throws<ArgumentOutOfRangeException>(() => SensitiveDiagnosticValue.Create(
            RedactionField.PersonName,
            string.Concat(exactAscii, "A")));
        Assert.Throws<ArgumentOutOfRangeException>(() => SensitiveDiagnosticValue.Create(
            RedactionField.PersonName,
            string.Concat(exactSurrogates, "\U0001F512")));
    }

    [Theory]
    [InlineData("SERIAL\r\n=HYPERLINK(1)")]
    [InlineData("=SYNTHETIC-SERIAL")]
    [InlineData("SERIAL/SYNTHETIC")]
    [InlineData("SERIAL\u202E0001")]
    public void WorkshopSerialExceptionRejectsControlFormulaAndConfusableValues(string value)
    {
        var result = RedactionEngine.Transform(
            RedactionField.FullSerialNumber,
            DiagnosticContext.WorkshopRecord,
            value);

        Assert.Equal(RedactionAction.Omit, result.Action);
        Assert.Equal(PersistenceDisposition.NoneInSupportExport, result.Persistence);
        Assert.Equal("[OMITTED:full-serial]", result.RedactedForm!.GetString());
    }

    [Theory]
    [InlineData("raw_command_output", DiagnosticContext.CommandIngest, "Virhe: C:\\Users\\SyntheticPerson\\secret.txt TOKEN-SYNTHETIC https://example.test/?key=secret\r\n")]
    [InlineData("raw_provider_output", DiagnosticContext.ProviderIngest, "Ошибка\0SSID-SYNTHETIC 192.0.2.44 C:\\Users\\SyntheticPerson")]
    [InlineData("raw_installer_output", DiagnosticContext.InstallerIngest, "Fehler: https://example.test/?token=secret C:\\Users\\SyntheticPerson")]
    public void LocalizedMalformedAndOverlappingRawOutputOmitsRawAndAllowsOnlyStructuredProjection(
        string fieldName,
        DiagnosticContext context,
        string value)
    {
        foreach (var candidate in new[] { value, string.Concat(value, new string('X', 65 * 1024)) })
        {
            var result = RedactionEngine.Transform(fieldName, context, candidate);

            Assert.Equal(RedactionAction.RejectRawAndExtractAllowlistedFields, result.Action);
            Assert.Equal(PersistenceDisposition.StructuredProjectionOnly, result.Persistence);
            Assert.Null(result.ExportedForm);
            Assert.DoesNotContain(candidate, result.RedactedForm?.GetString() ?? string.Empty, StringComparison.Ordinal);
        }
    }

    private sealed class HostileObject
    {
        public bool ToStringRead { get; private set; }

        public override string ToString()
        {
            ToStringRead = true;
            throw new InvalidOperationException("SYNTHETIC-SECRET");
        }
    }
}
