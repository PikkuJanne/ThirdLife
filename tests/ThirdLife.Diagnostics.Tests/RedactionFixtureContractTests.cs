using ThirdLife.Diagnostics.Redaction;

namespace ThirdLife.Diagnostics.Tests;

public sealed class RedactionFixtureContractTests
{
    private static readonly RedactionFixtureSet Fixtures = RedactionFixtureSet.Load();

    [Fact]
    public void ApprovedFixtureIdentityAndCoverageAreExact()
    {
        Assert.Equal(RedactionFixtureSet.ExpectedSha256, Fixtures.Sha256);
        Assert.Equal(56, Fixtures.Cases.Count);
        Assert.Equal(
            Enumerable.Range(1, 56).Select(static number => $"RDX-{number:000}"),
            Fixtures.Cases.Select(static fixture => fixture.Id));
        Assert.Equal(
            SupportFieldCatalog.AllFields.Select(SupportFieldCatalog.GetWireName),
            Fixtures.SupportAllowlist);
    }

    [Fact]
    public void EveryApprovedRedactionFixtureExecutesExactly()
    {
        foreach (var fixture in Fixtures.Cases)
        {
            var first = RedactionEngine.Transform(fixture.InputField, fixture.Context, fixture.InputValue);
            var second = RedactionEngine.Transform(fixture.InputField, fixture.Context, fixture.InputValue);

            Assert.Equal(fixture.Action, first.Action);
            Assert.Equal(fixture.RedactedForm, first.RedactedForm?.ToObject());
            Assert.Equal(fixture.Persistence, first.Persistence);
            Assert.Equal(fixture.SupportOutcome, first.SupportOutcome);
            Assert.Equal(fixture.ExportedForm, first.ExportedForm?.ToObject());

            Assert.Equal(first.Action, second.Action);
            Assert.Equal(first.RedactedForm?.ToObject(), second.RedactedForm?.ToObject());
            Assert.Equal(first.Persistence, second.Persistence);
            Assert.Equal(first.SupportOutcome, second.SupportOutcome);
            Assert.Equal(first.ExportedForm?.ToObject(), second.ExportedForm?.ToObject());
        }
    }

    [Fact]
    public void RedactionIsIdempotentAndDoesNotExposeSensitiveSeeds()
    {
        foreach (var fixture in Fixtures.Cases)
        {
            var first = RedactionEngine.Transform(fixture.InputField, fixture.Context, fixture.InputValue);
            var transformedValue = first.RedactedForm?.ToObject();
            var second = RedactionEngine.Transform(fixture.InputField, fixture.Context, transformedValue);

            Assert.Equal(first.Action, second.Action);
            Assert.Equal(transformedValue, second.RedactedForm?.ToObject());

            if (fixture.Action is RedactionAction.PreserveAllowlisted or RedactionAction.PreserveWorkshopOnly)
            {
                continue;
            }

            var seed = fixture.InputValue.ToString();
            Assert.DoesNotContain(seed!, first.RedactedForm?.ToObject()?.ToString() ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(seed!, first.ExportedForm?.ToObject()?.ToString() ?? string.Empty, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FullSerialWorkshopExceptionNeverBecomesSupportOutput()
    {
        var fixture = Assert.Single(Fixtures.Cases, static item => item.Id == "RDX-008");
        var result = RedactionEngine.Transform(fixture.InputField, fixture.Context, fixture.InputValue);

        Assert.Equal(PersistenceDisposition.WorkshopRecordOnly, result.Persistence);
        Assert.Null(result.ExportedForm);
        Assert.DoesNotContain(RedactionField.FullSerialNumber, SupportFieldCatalog.AllFields.Select(ToRedactionField));
    }

    private static RedactionField ToRedactionField(SupportFieldName field)
    {
        var wireName = SupportFieldCatalog.GetWireName(field);
        return RedactionFieldCatalog.Parse(wireName);
    }
}
