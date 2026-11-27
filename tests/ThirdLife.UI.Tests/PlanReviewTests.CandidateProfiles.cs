using System.IO;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Execution;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Core.Evidence;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Features.PlanReview;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

public sealed partial class PlanReviewTests
{
    private static readonly string[] CandidateCapabilities = ["document_editing", "pdf_reading", "video_calling", "web_browsing"];

    [Fact]
    [Trait("Category", "Extended")]
    public Task JobSeekerCandidateConvergesAndReopensWithoutLiveReadiness() =>
        RunCandidateProfileAsync("job-seeker", failVideo: false, Disposition.ReadyToPrepare,
            "All scheduled actions have saved independent verification.");

    [Fact]
    public Task OptionalCandidateFailurePreservesEssentialActions() =>
        RunCandidateProfileAsync("basic", failVideo: true, Disposition.ReadyToPrepare,
            "Review the recorded optional outcomes.");

    [Fact]
    public Task EssentialCandidateFailureBlocksPreparation() =>
        RunCandidateProfileAsync("job-seeker", failVideo: true, Disposition.HumanReviewRequired,
            "The broker reported a failure.");

    private static async Task RunCandidateProfileAsync(string profileId, bool failVideo, Disposition disposition,
        string runStatus)
    {
        using var disk = new AuditTestStore();
        var clock = new FixtureClock();
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "candidate-reports"));
        var verifier = new FixtureNoOpVerifier { CanConfirm = true };
        var dispatched = new List<Guid>();
        Guid video = Guid.Empty;
        var backend = new FixtureBrokerBackend(async (_, batch, authorize, _, token) =>
        {
            Assert.True(await authorize(token));
            var action = Assert.Single(batch.Actions);
            dispatched.Add(action.ActionId);
            var outcome = failVideo && action.ActionId == video ? NoOpBrokerActionStatus.Failed : NoOpBrokerActionStatus.Applied;
            return new NoOpBrokerBatchResult(NoOpBrokerBatchStatus.Finished,
                [new(batch.CorrelationId, action.ActionId, outcome)]);
        });

        JournalPlanKey key;
        JournalApproval approval;
        await using (var prepare = Open())
        {
            await PrepareAssessmentAsync(prepare);
            var option = Assert.Single(prepare.VisiblePlanProfiles, item => item.Profile.ProfileId == profileId);
            Assert.True(option.Profile.SyntheticData && option.Catalog.SyntheticData);
            prepare.SelectedPlanProfile = option;
            await prepare.BuildPlanReviewAsync();
            var review = Assert.IsType<PlanReviewSnapshot>(prepare.PlanReview);
            Assert.Null(review.Plan.BlockedReason);
            Assert.True(review.Plan.SyntheticData);
            Assert.Equal(option.Profile.ContentSha256, review.Plan.ProfileSha256);
            Assert.Equal(option.Catalog.ContentSha256, review.Plan.CatalogSha256);
            Assert.Equal(ReadyPolicy.Document.ContentSha256, review.Plan.PolicySha256);
            Assert.Equal(CandidateCapabilities, review.Plan.Actions.Select(action => action.CapabilityId).Order(StringComparer.Ordinal));
            Assert.Equal(4, review.Plan.Schedule.Length);
            Assert.Equal(4, review.Plan.RecipientChoices.Length);
            var videoAction = Assert.Single(review.Plan.Actions, action => action.CapabilityId == "video_calling");
            video = videoAction.Request.ActionId;
            Assert.Equal(profileId == "basic" ? CapabilityIntent.Optional : CapabilityIntent.Essential, videoAction.Intent);
            Assert.All(review.Plan.Actions, action => Assert.IsType<NoOpActionRequest>(action.Request));
            key = JournalPlanSnapshot.Create(review.Plan, review.Impact).Key;
            Assert.Empty(dispatched);
            Assert.False(prepare.CanInstallPackage);
            Assert.False(prepare.CanContinueMaintenance);
            prepare.BrokerOperatorCode = "fixture-operator";
            await prepare.ApprovePlanReviewAsync();
            Assert.Equal(key.PlanSha256, prepare.PlanReviewDecision?.PlanSha256);
            await using var saved = await disk.OpenSqliteAsync();
            var approved = Assert.IsType<JournalHistory>(await saved.LoadJournalAsync(key));
            approval = Assert.Single(approved.Approvals);
            Assert.Equal(key, approval.Plan);
            Assert.All(approved.Plan.Actions, action => Assert.Equal(JournalState.Approved, approved.StateOf(action.ActionId)));
            Assert.Empty(dispatched);
        }

        await using (var execute = Open())
        {
            await execute.OpenJobAsync(key.JobId.Value);
            execute.SelectedPlanProfile = Assert.Single(execute.VisiblePlanProfiles, item => item.Profile.ProfileId == profileId);
            await execute.BuildPlanReviewAsync();
            Assert.Equal(key.PlanSha256, execute.PlanReviewDecision?.PlanSha256);
            Assert.True(execute.CanRunBrokerExecution, execute.BrokerExecutionStatus);
            await execute.RunBrokerExecutionAsync();
            Assert.Contains(runStatus, execute.BrokerExecutionStatus, StringComparison.Ordinal);
        }

        await using var reopened = Open();
        await reopened.OpenJobAsync(key.JobId.Value);
        Assert.Equal(key.JobId.Value, reopened.JobId);
        Assert.False(reopened.CanRunProfileVerification);
        Assert.False(reopened.CanRunPostBootVerification);
        Assert.False(reopened.CanInstallPackage);
        await using var store = await disk.OpenSqliteAsync();
        var history = Assert.IsType<JournalHistory>(await store.LoadJournalAsync(key));
        Assert.Equal(approval, Assert.Single(history.Approvals));
        var schedule = Assert.IsType<ActionSchedule>(history.Plan.Schedule);
        Assert.Equal(schedule.Steps.Select(step => step.ActionId), dispatched);
        Assert.Equal(dispatched.Where(id => !failVideo || id != video), verifier.ObservedActions);
        foreach (var action in history.Plan.Actions)
        {
            var start = Assert.Single(history.Entries, entry => entry.ActionId == action.ActionId && entry.State == JournalState.Started);
            var failed = failVideo && action.ActionId == video;
            var outcome = Assert.Single(history.Entries, entry => entry.ActionId == action.ActionId &&
                entry.State == (failed ? JournalState.Failed : JournalState.Verified));
            Assert.Equal(start.CorrelationId, outcome.CorrelationId);
            Assert.Equal(approval.ApprovalId, outcome.ApprovalId);
            Assert.Equal(failed ? JournalResult.OperationFailed : JournalResult.VerificationPassed, outcome.Result);
            Assert.Equal(failed ? null : ActionVerification.NoMachineChange, outcome.Verification);
        }

        Assert.Equal(disposition, PreparationDisposition.Evaluate(history).Disposition);
        await reopened.GenerateReportAsync();
        await reopened.OpenReportAsync();
        Assert.Contains(reopened.PreparationDispositionStatus, reopened.ReportPreview, StringComparison.Ordinal);
        Assert.Contains($"Current plan (synthetic test). Plan SHA-256 {key.PlanSha256}", reopened.ReportPreview, StringComparison.Ordinal);
        Assert.Contains("Whole-device acceptance still requires applicable device checks, cold-boot verification and finalization.",
            reopened.ReportPreview, StringComparison.Ordinal);
        Assert.All(history.Plan.Actions, action => Assert.Contains(action.CapabilityId, reopened.ReportPreview, StringComparison.Ordinal));
        Assert.Equal(4, backend.Calls);

        AuditWorkspace Open() => new(async token => await disk.OpenSqliteAsync(token).ConfigureAwait(false),
            [new PlanFactsProvider()], clock, reports, fixturePlanPolicy: static _ => ReadyPolicy,
            fixtureBrokerCoordinator: journal => new(journal, backend, clock, verifier));
    }
}
