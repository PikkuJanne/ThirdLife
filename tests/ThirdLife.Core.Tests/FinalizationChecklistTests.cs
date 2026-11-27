using ThirdLife.Core.Evidence;
using ThirdLife.Core.Finalization;

namespace ThirdLife.Core.Tests;

public class FinalizationChecklistTests
{
    private const string Revision = "device-state-1";
    private static readonly OperatorId Operator = new("workshop-21");
    private static readonly DateTimeOffset RecordedAt = new(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MissingChecksRemainBlockingAndEveryRuleIsRepresented()
    {
        var checklist = FinalizationChecklist.Evaluate(Revision, []);

        Assert.True(checklist.CanRecord);
        Assert.True(checklist.HasBlockingChecks);
        Assert.Equal(Enum.GetValues<FinalizationRuleId>(), checklist.Rows.Select(row => row.Rule.Id));
        Assert.All(checklist.Rows, row =>
        {
            Assert.Equal(FinalizationOutcome.Unknown, row.Outcome);
            Assert.Equal(FinalizationSource.None, row.Source);
            Assert.True(row.IsBlocking);
        });
    }

    [Fact]
    public void ValidConfirmationsAndIndependentRestartEvidenceClearOnlyTheChecklist()
    {
        var checkedRules = FinalizationChecklist.Rules.Where(rule => rule.AllowsOperatorConfirmation).ToArray();
        var confirmations = checkedRules.Select((rule, index) => rule.Id == FinalizationRuleId.WiFiProfiles
            ? Confirm(index + 1, rule.Id, FinalizationOutcome.NotApplicable, FinalizationReason.CapabilityAbsent)
            : Confirm(index + 1, rule.Id, FinalizationOutcome.Passed)).ToArray();

        var checklist = FinalizationChecklist.Evaluate(Revision, confirmations, FinalizationOutcome.Passed);

        Assert.False(checklist.HasBlockingChecks);
        var wifi = Row(checklist, FinalizationRuleId.WiFiProfiles);
        Assert.Equal(FinalizationOutcome.NotApplicable, wifi.Outcome);
        Assert.Equal(FinalizationSource.OperatorConfirmation, wifi.Source);
        Assert.Equal(Operator, wifi.Confirmation?.OperatorId);
        Assert.Equal(RecordedAt, wifi.Confirmation?.RecordedAtUtc);
        Assert.Equal(FinalizationSource.AutomatedObservation, Row(checklist, FinalizationRuleId.PendingRestart).Source);
        Assert.True(FinalizationChecklist.Evaluate(Revision, confirmations, FinalizationOutcome.Failed).HasBlockingChecks);
    }

    [Theory]
    [InlineData(FinalizationOutcome.Failed)]
    [InlineData(FinalizationOutcome.Unknown)]
    public void LaterUnresolvedConfirmationSupersedesPassAndRetainsHistory(FinalizationOutcome outcome)
    {
        var original = Confirm(1, FinalizationRuleId.TestMedia, FinalizationOutcome.Passed);
        var latest = Confirm(2, FinalizationRuleId.TestMedia, outcome);

        var checklist = FinalizationChecklist.Evaluate(Revision, [original, latest]);

        var row = Row(checklist, FinalizationRuleId.TestMedia);
        Assert.True(row.IsBlocking);
        Assert.Equal(outcome, row.Outcome);
        Assert.Equal(FinalizationSource.OperatorConfirmation, row.Source);
        Assert.Equal([original, latest], checklist.History);
    }

    [Fact]
    public void ChangedDeviceEvidenceKeepsHistoryButRequiresAnotherCheck()
    {
        var original = Confirm(1, FinalizationRuleId.TestMedia, FinalizationOutcome.Passed);
        var checklist = FinalizationChecklist.Evaluate("device-state-2", [original]);

        var row = Row(checklist, FinalizationRuleId.TestMedia);
        Assert.Equal(FinalizationOutcome.Unknown, row.Outcome);
        Assert.True(row.NeedsRecheck);
        Assert.True(row.IsBlocking);
        Assert.Same(original, row.Confirmation);
        Assert.Same(original, Assert.Single(checklist.History));

        var unavailable = FinalizationChecklist.Evaluate(null, [original], FinalizationOutcome.Passed);
        Assert.False(unavailable.CanRecord);
        Assert.True(Row(unavailable, FinalizationRuleId.PendingRestart).IsBlocking);
    }

    [Theory]
    [InlineData(FinalizationRuleId.PendingRestart, FinalizationOutcome.Passed, null)]
    [InlineData(FinalizationRuleId.TestMedia, FinalizationOutcome.NotApplicable, null)]
    [InlineData(FinalizationRuleId.TestMedia, FinalizationOutcome.NotApplicable, FinalizationReason.CapabilityAbsent)]
    [InlineData(FinalizationRuleId.Credentials, FinalizationOutcome.NotApplicable, FinalizationReason.NoWorkshopUse)]
    [InlineData(FinalizationRuleId.WiFiProfiles, FinalizationOutcome.Passed, FinalizationReason.CapabilityAbsent)]
    public void InvalidConfirmationsAreRejected(FinalizationRuleId rule, FinalizationOutcome outcome,
        FinalizationReason? reason) =>
        Assert.Throws<ArgumentException>(() => Confirm(1, rule, outcome, reason));

    [Fact]
    public void MissingOrOutOfOrderHistoryEntriesCannotBeProjected()
    {
        var original = Confirm(1, FinalizationRuleId.TestMedia, FinalizationOutcome.Passed);
        var later = Confirm(2, FinalizationRuleId.TestMedia, FinalizationOutcome.Unknown);

        Assert.Throws<ArgumentException>(() => FinalizationChecklist.Evaluate(Revision, [later]));
        Assert.Throws<ArgumentException>(() => FinalizationChecklist.Evaluate(Revision, [later, original]));
    }

    private static FinalizationRow Row(FinalizationChecklist checklist, FinalizationRuleId rule) =>
        Assert.Single(checklist.Rows, row => row.Rule.Id == rule);

    [Fact]
    public void ChangedNamedScopeCannotAutomaticallyDismissAPreviousIndication()
    {
        var rule = FinalizationRuleId.Shares;
        var prior = Scan(1, WorkshopArtifactStatus.Present);
        var current = Scan(2, WorkshopArtifactStatus.AbsentInCheckedScope);
        var previousReview = FinalizationChecklist.Evaluate(Revision, [], artifactScans: [prior]);
        var confirmation = new FinalizationRecord(1, rule, FinalizationOutcome.Passed, null,
            new OperatorId("operator-scope"), DateTimeOffset.UtcNow, previousReview.EvidenceVersionForConfirmation(rule)!);
        var review = FinalizationChecklist.Evaluate(Revision, [confirmation], artifactScans: [prior, current]);
        Assert.True(Row(review, rule).NeedsRecheck);
        Assert.True(Row(review, rule).IsBlocking);

        var rejectedOmission = new FinalizationRecord(2, rule, FinalizationOutcome.NotApplicable,
            FinalizationReason.NoWorkshopUse, new OperatorId("operator-scope"), DateTimeOffset.UtcNow,
            review.EvidenceVersionForConfirmation(rule)!);
        var unchanged = FinalizationChecklist.Evaluate(Revision, [confirmation, rejectedOmission], artifactScans: [prior, current]);
        Assert.True(Row(unchanged, rule).HasBlockingArtifact);
        Assert.True(Row(unchanged, rule).IsBlocking);
        Assert.Equal([prior, current], unchanged.ArtifactScans);

        static WorkshopArtifactScan Scan(long sequence, WorkshopArtifactStatus share) => new(sequence,
            DateTimeOffset.UtcNow, Revision, WorkshopArtifactScan.CurrentDetectorRevision, ProvenanceKind.SyntheticFixture,
            Enum.GetValues<WorkshopArtifactKind>().Select(kind => new WorkshopArtifactFinding(kind, kind == WorkshopArtifactKind.ConfiguredSmbShares
                ? share : WorkshopArtifactStatus.Unavailable)).ToArray());
    }

    private static FinalizationRecord Confirm(long index, FinalizationRuleId rule, FinalizationOutcome outcome,
        FinalizationReason? reason = null) => new(index, rule, outcome, reason, Operator, RecordedAt, Revision);
}
