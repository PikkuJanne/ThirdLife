using System.Text.Json;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Recovery;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Actions.Tests;

public sealed class RebootCheckpointTests
{
    private static readonly DateTimeOffset Issued = new(2031, 2, 3, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Expires = Issued.AddHours(1);
    private static readonly Guid CheckpointId = new("22bd9aeb-08ec-41e8-92b9-08ce6d8229f2");
    private static readonly Guid ActionId = new("38324ca8-8d7c-4380-bc70-c31950e4fba5");
    private static readonly Guid EntryId = new("19609c3d-1f5d-4a99-a39b-defa431a2840");
    private static readonly Guid ApprovalId = new("d2577c70-92d4-4a86-b623-3757c45adc8e");
    private static readonly Guid CorrelationId = new("b728168a-331d-44cc-8ccf-ca994dce41ab");
    private static readonly Guid Other = new("052a9ac5-6cdb-4d58-86b6-0e69a5959c25");
    private static readonly JournalPlanKey Plan = new(new JobId("job-reboot-fixture"), new string('a', 64), new string('b', 64));

    [Theory]
    [InlineData("checkpoint")]
    [InlineData("job")]
    [InlineData("plan")]
    [InlineData("impact")]
    [InlineData("action")]
    [InlineData("entry")]
    [InlineData("approval")]
    [InlineData("correlation")]
    [InlineData("issue-time")]
    [InlineData("expiry")]
    public void AuthenticationDataBindsEachPartOfTheCheckpoint(string field)
    {
        var baseline = Checkpoint();
        var changedPlan = field switch
        {
            "job" => new JournalPlanKey(new JobId("job-other-fixture"), Plan.PlanSha256, Plan.ImpactSha256),
            "plan" => new JournalPlanKey(Plan.JobId, new string('c', 64), Plan.ImpactSha256),
            "impact" => new JournalPlanKey(Plan.JobId, Plan.PlanSha256, new string('d', 64)),
            _ => Plan,
        };
        var changed = new RebootCheckpoint(
            field == "checkpoint" ? Other : CheckpointId,
            changedPlan,
            field == "action" ? Other : ActionId,
            field == "entry" ? Other : EntryId,
            field == "approval" ? Other : ApprovalId,
            field == "correlation" ? Other : CorrelationId,
            field == "issue-time" ? Issued.AddMinutes(1) : Issued,
            field == "expiry" ? Expires.AddMinutes(1) : Expires);

        Assert.False(baseline.CreateAuthenticationData().AsSpan().SequenceEqual(changed.CreateAuthenticationData()));
    }

    [Fact]
    public void EquivalentTimestampsProduceTheSameAuthenticationBytes()
    {
        var offset = TimeSpan.FromHours(2);
        var checkpoint = new RebootCheckpoint(CheckpointId, Plan, ActionId, EntryId, ApprovalId, CorrelationId,
            Issued.ToOffset(offset), Expires.ToOffset(offset));

        Assert.Equal(Issued, checkpoint.IssuedAtUtc);
        Assert.Equal(TimeSpan.Zero, checkpoint.IssuedAtUtc.Offset);
        Assert.Equal(Checkpoint().CreateAuthenticationData(), checkpoint.CreateAuthenticationData());
    }

    [Fact]
    public void EncodesOnlyBoundedWorkshopCheckpointFields()
    {
        var bytes = Checkpoint().CreateAuthenticationData();
        Assert.InRange(bytes.Length, 1, RebootCheckpoint.MaximumAuthenticationDataBytes);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        Assert.Equal("thirdlife.reboot-checkpoint.v1", root.GetProperty("purpose").GetString());
        string[] expected =
        [
            "action_id", "approval_id", "checkpoint_id", "correlation_id", "expires_at_utc", "impact_sha256",
            "issued_at_utc", "job_id", "journal_entry_id", "plan_sha256", "purpose",
        ];
        Assert.Equal(expected, root.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ExpiryIsExclusiveAndClockRollbackBeforeIssueDoesNotGrantResume()
    {
        var checkpoint = Checkpoint();

        Assert.False(checkpoint.IsCurrent(Issued.AddTicks(-1)));
        Assert.True(checkpoint.IsCurrent(Issued));
        Assert.True(checkpoint.IsCurrent(Expires.AddTicks(-1)));
        Assert.False(checkpoint.IsCurrent(Expires));
        Assert.False(checkpoint.IsCurrent(Expires.AddTicks(1)));
    }

    [Fact]
    public void RejectsInvalidCheckpointIdsAndLifetimes()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new RebootCheckpoint(Guid.Empty, Plan, ActionId, EntryId, ApprovalId, CorrelationId, Issued, Expires));
        Assert.ThrowsAny<ArgumentException>(() =>
            new RebootCheckpoint(CheckpointId, Plan, ActionId, EntryId, ApprovalId, CorrelationId, Issued, Issued));
        Assert.ThrowsAny<ArgumentException>(() =>
            new RebootCheckpoint(CheckpointId, Plan, ActionId, EntryId, ApprovalId, CorrelationId,
                Issued, Issued.Add(RebootCheckpoint.MaximumLifetime).AddTicks(1)));
    }

    [Theory]
    [InlineData(JournalState.Started, JournalState.Applied, JournalActor.Broker, JournalResult.BackendApplied, JournalCheckpoint.RestartRequired, true)]
    [InlineData(JournalState.Approved, JournalState.Applied, JournalActor.Broker, JournalResult.BackendApplied, JournalCheckpoint.RestartRequired, false)]
    [InlineData(JournalState.Started, JournalState.Applied, JournalActor.Verifier, JournalResult.BackendApplied, JournalCheckpoint.RestartRequired, false)]
    [InlineData(JournalState.Started, JournalState.Applied, JournalActor.Broker, JournalResult.BackendApplied, JournalCheckpoint.None, false)]
    [InlineData(JournalState.Started, JournalState.Failed, JournalActor.Broker, JournalResult.OperationFailed, JournalCheckpoint.RestartRequired, false)]
    public void OnlyAnExactJournalRestartResultMatches(
        JournalState previous, JournalState state, JournalActor actor,
        JournalResult result, JournalCheckpoint checkpoint, bool expected)
    {
        var entry = new JournalEntry(EntryId, Plan, ActionId, ApprovalId, previous, state, actor,
            new OperatorId("actor-reboot-fixture"), CorrelationId, Issued.AddSeconds(-1), result, checkpoint);

        Assert.Equal(expected, Checkpoint().MatchesRestart(entry));
    }

    [Fact]
    public void RejectsARestartFromAnotherAttemptOrAFutureJournalEntry()
    {
        var actor = new OperatorId("actor-reboot-fixture");
        var wrongAttempt = new JournalEntry(EntryId, Plan, ActionId, ApprovalId, JournalState.Started, JournalState.Applied,
            JournalActor.Broker, actor, Other, Issued, JournalResult.BackendApplied, JournalCheckpoint.RestartRequired);
        var future = new JournalEntry(EntryId, Plan, ActionId, ApprovalId, JournalState.Started, JournalState.Applied,
            JournalActor.Broker, actor, CorrelationId, Issued.AddTicks(1), JournalResult.BackendApplied, JournalCheckpoint.RestartRequired);

        Assert.False(Checkpoint().MatchesRestart(wrongAttempt));
        Assert.False(Checkpoint().MatchesRestart(future));
        Assert.False(Checkpoint().MatchesRestart(null));
    }

    [Fact]
    public void TokenOwnsItsBoundedBytesAndDoesNotDisplayThem()
    {
        byte[] source = [65, 66, 67, 68, 69, 70];
        var token = new RebootResumeToken(source);
        var candidate = new RebootResumeCandidate(Checkpoint(), token);
        source[0] = 71;

        Assert.Equal((byte)65, token.AuthenticityBlob[0]);
        Assert.Same(token, candidate.Token);
        Assert.DoesNotContain("ABCDEF", token.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("QUJDREVG", token.ToString(), StringComparison.Ordinal);
        Assert.ThrowsAny<ArgumentException>(() => new RebootResumeToken(ReadOnlyMemory<byte>.Empty));
        Assert.ThrowsAny<ArgumentException>(() => new RebootResumeToken(new byte[RebootResumeToken.MaximumBytes + 1]));
    }

    private static RebootCheckpoint Checkpoint() =>
        new(CheckpointId, Plan, ActionId, EntryId, ApprovalId, CorrelationId, Issued, Expires);
}
