using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Controls;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Catalog.Schema;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Packages;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Features.Disposition;
using ThirdLife.UI.Features.PlanReview;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

public sealed partial class PackageSetupTests
{
    [Fact]
    public async Task RebuildingTheSameVerifiedPackagePlanDoesNotReapplyIt()
    {
        using var disk = new AuditTestStore();
        var runtime = new FixtureInstaller(PackageInstallStatus.Applied, register: true);
        var service = new PilotPackageService(runtime);
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        JobId jobId;
        await using (var workspace = OpenWorkspace(disk, reports, service))
        {
            jobId = await ApproveInitialPackagePlanAsync(workspace);
            await workspace.InstallPackageAsync();
            await workspace.InstallPackageAsync();
            Assert.Equal(1, runtime.InstallCalls);
            Assert.Equal(1, runtime.ProbeCalls);
        }

        var saved = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(JournalState.Verified, saved.Entries[^1].State);
        await using var reopened = OpenWorkspace(disk, reports, service);
        await reopened.OpenJobAsync(jobId.Value);
        reopened.SelectedPlanProfile = Assert.Single(reopened.PlanProfiles, option => !option.Profile.SyntheticData);
        await reopened.BuildPlanReviewAsync();
        Assert.Equal(saved.Plan.Key.PlanSha256, reopened.PlanReview?.Plan.ContentSha256);
        reopened.PackageOperatorCode = "fixture-operator";
        Assert.False(reopened.CanApprovePlanReview);
        Assert.False(reopened.CanRetryPackage);
        await reopened.ApprovePlanReviewAsync();
        await reopened.InstallPackageAsync();
        await reopened.RetryPackageAsync();
        Assert.Equal(1, runtime.InstallCalls);
        var unchanged = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(saved.Entries.AsEnumerable(), unchanged.Entries.AsEnumerable());
        Assert.Equal(saved.Approvals.AsEnumerable(), unchanged.Approvals.AsEnumerable());
    }

    [Fact]
    public async Task MissingPreviouslyVerifiedPackageShowsFreshGuidanceWithoutReinstalling()
    {
        using var disk = new AuditTestStore();
        var runtime = new InterruptedPackageRuntime();
        var service = new PilotPackageService(runtime);
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        JobId jobId;
        await using (var workspace = OpenWorkspace(disk, reports, service))
        {
            jobId = await ApproveInitialPackagePlanAsync(workspace);
            await workspace.InstallPackageAsync();
        }

        var saved = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(JournalState.Verified, saved.Entries[^1].State);
        runtime.Installed = false;
        var acquisitions = runtime.Acquisitions;
        await using var reopened = OpenWorkspace(disk, reports, service);
        await reopened.OpenJobAsync(jobId.Value);
        var inspections = runtime.Inspections;
        reopened.SelectedPlanProfile = Assert.Single(reopened.PlanProfiles, option => !option.Profile.SyntheticData);
        InPackageRecoveryWindow(reopened, (window, panel) =>
        {
            Invoke(window, Named<Button>(panel, "BuildPlanReviewButton"));
            PumpUntil(window, () => !reopened.IsBusy && reopened.HasPlanReview);
            var planStatus = Named<TextBlock>(panel, "PlanStatusMessage");
            var sourceStatus = Named<TextBlock>(panel, "PackageSourceMessage");
            Assert.Contains("previously verified package is no longer installed",
                $"{planStatus.Text} {sourceStatus.Text}", StringComparison.OrdinalIgnoreCase);
            Assert.False(Named<Button>(panel, "ApprovePlanReviewButton").IsEnabled);
            Assert.False(Named<Button>(panel, "InstallPackageButton").IsEnabled);
            Assert.False(reopened.CanRetryPackage);
        });

        Assert.True(runtime.Inspections > inspections);
        Assert.Equal(acquisitions, runtime.Acquisitions);
        Assert.Equal(1, runtime.InstallCalls);
        Assert.Equal(1, runtime.ProbeCalls);
        var unchanged = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(saved.Entries.AsEnumerable(), unchanged.Entries.AsEnumerable());
        Assert.Equal(saved.Approvals.AsEnumerable(), unchanged.Approvals.AsEnumerable());
    }

    [Fact]
    public async Task ApprovedInstallVerifiesAnExactPackageThatAppearedBeforeDispatch()
    {
        using var disk = new AuditTestStore();
        var runtime = new InterruptedPackageRuntime();
        var service = new PilotPackageService(runtime);
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        JobId jobId;
        JournalHistory approved;
        await using (var workspace = OpenWorkspace(disk, reports, service))
        {
            jobId = await ApproveInitialPackagePlanAsync(workspace);
            approved = await ReadRecoveryHistoryAsync(disk, jobId);
            var acquisitions = runtime.Acquisitions;
            runtime.Installed = true;
            InPackageRecoveryWindow(workspace, (window, panel) =>
            {
                Invoke(window, Named<Button>(panel, "InstallPackageButton"));
                PumpUntil(window, () => !workspace.IsBusy && runtime.ProbeCalls == 1,
                    () => workspace.PlanReviewStatus);
                Assert.False(workspace.CanInstallPackage);
            });
            Assert.Equal(acquisitions, runtime.Acquisitions);
            Assert.Equal(0, runtime.InstallCalls);
        }

        var verified = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(approved.Entries.AsEnumerable(), verified.Entries.Take(approved.Entries.Length));
        Assert.Equal(approved.Approvals.AsEnumerable(), verified.Approvals.AsEnumerable());
        Assert.DoesNotContain(verified.Entries, entry => entry.State == JournalState.Applied);
        var result = verified.Entries[^1];
        Assert.Equal(JournalState.Verified, result.State);
        Assert.Equal(JournalCheckpoint.PackageExistingTargetVerification, result.Checkpoint);
        Assert.Equal(JournalActor.Verifier, result.Actor);
        Assert.Equal(Assert.Single(approved.Approvals).ApprovalId, result.ApprovalId);

        await using var reopened = OpenWorkspace(disk, reports, service);
        await reopened.OpenJobAsync(jobId.Value);
        await reopened.GenerateReportAsync();
        await reopened.OpenReportAsync();
        Assert.Contains("ThirdLife skipped Windows Installer for this attempt", reopened.ReportPreview, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("launch check", reopened.ReportPreview, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, runtime.ProbeCalls);
    }

    [Fact]
    public async Task PackageRetryBackoffAndAttemptLimitSurviveJobReopen()
    {
        using var disk = new AuditTestStore();
        var runtime = new InterruptedPackageRuntime();
        var service = new PilotPackageService(runtime);
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        var clock = new FixedClock();
        JobId jobId;
        await using (var workspace = OpenWorkspace(disk, reports, service, clock))
        {
            jobId = await ApproveInitialPackagePlanAsync(workspace);
            FailNextDownload();
            await workspace.InstallPackageAsync();
        }

        var initial = await ReadRecoveryHistoryAsync(disk, jobId);
        await using var reopened = OpenWorkspace(disk, reports, service, clock);
        clock.Current = Now.AddMilliseconds(4_999);
        await reopened.OpenJobAsync(jobId.Value);
        FailNextDownload();
        Assert.False(reopened.CanRetryPackage);
        await reopened.RetryPackageAsync();
        Assert.Equal(initial.Entries.AsEnumerable(), (await ReadRecoveryHistoryAsync(disk, jobId)).Entries.AsEnumerable());

        clock.Current = Now.AddSeconds(5);
        Assert.True(reopened.CanRetryPackage);
        await reopened.RetryPackageAsync();
        var firstRetry = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(initial.Entries[^1].EntryId, firstRetry.Entries[^2].PriorAttemptEntryId);
        Assert.Equal(JournalResult.NetworkUnavailable, firstRetry.Entries[^1].Result);
        FailNextDownload();

        clock.Current = Now.AddMilliseconds(19_999);
        Assert.False(reopened.CanRetryPackage);
        await reopened.RetryPackageAsync();
        Assert.Equal(firstRetry.Entries.AsEnumerable(), (await ReadRecoveryHistoryAsync(disk, jobId)).Entries.AsEnumerable());

        clock.Current = Now.AddSeconds(20);
        Assert.True(reopened.CanRetryPackage);
        await reopened.RetryPackageAsync();
        var exhausted = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(firstRetry.Entries[^1].EntryId, exhausted.Entries[^2].PriorAttemptEntryId);
        Assert.Equal(JournalResult.NetworkUnavailable, exhausted.Entries[^1].Result);
        Assert.Equal(3, exhausted.Entries.Count(entry => entry.State == JournalState.Started));
        Assert.Single(exhausted.Approvals);

        // Even reopening and building a fresh review after the old approval expires cannot reset the limit.
        clock.Current = Now.AddMinutes(11);
        await reopened.OpenJobAsync(jobId.Value);
        Assert.False(reopened.CanRetryPackage);
        await reopened.RetryPackageAsync();
        reopened.SelectedPlanProfile = Assert.Single(reopened.PlanProfiles, option => !option.Profile.SyntheticData);
        await reopened.BuildPlanReviewAsync();
        reopened.PackageOperatorCode = "fixture-operator";
        Assert.False(reopened.CanApprovePlanReview);
        await reopened.ApprovePlanReviewAsync();
        await reopened.InstallPackageAsync();
        Assert.Equal(0, runtime.InstallCalls);
        var unchanged = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(exhausted.Entries.AsEnumerable(), unchanged.Entries.AsEnumerable());
        Assert.Equal(exhausted.Approvals.AsEnumerable(), unchanged.Approvals.AsEnumerable());

        void FailNextDownload() =>
            runtime.NextAcquisition = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "Fixture connection failure.");
    }

    [Fact]
    public async Task UnavailableRegistrationBeforeDispatchAllowsLinkedRetryWhenObservationRecovers()
    {
        using var disk = new AuditTestStore();
        var runtime = new InterruptedPackageRuntime();
        var service = new PilotPackageService(runtime);
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        var clock = new FixedClock();
        JobId jobId;
        await using (var workspace = OpenWorkspace(disk, reports, service, clock))
        {
            jobId = await ApproveInitialPackagePlanAsync(workspace);
            var acquisitions = runtime.Acquisitions;
            runtime.NextObservation = new PackageObservation(PackageObservationStatus.Unavailable);
            await workspace.InstallPackageAsync();
            Assert.Equal(acquisitions, runtime.Acquisitions);
            Assert.Equal(0, runtime.InstallCalls);
        }

        var saved = await ReadRecoveryHistoryAsync(disk, jobId);
        AssertInterruptedAttempt(saved, JournalState.Failed, JournalResult.BackendUnavailable,
            JournalCheckpoint.PackagePreDispatchFailure);
        Assert.Equal(JournalActor.PackageBackend, saved.Entries[^1].Actor);
        clock.Current = Now.AddSeconds(5);
        await using var reopened = OpenWorkspace(disk, reports, service, clock);
        await reopened.OpenJobAsync(jobId.Value);
        InPackageRecoveryWindow(reopened, (window, panel) =>
        {
            var retry = Named<Button>(panel, "RetryPackageButton");
            Assert.True(retry.IsVisible && retry.IsEnabled);
            Invoke(window, retry);
            PumpUntil(window, () => !reopened.IsBusy &&
                reopened.PackageHistoryText.Contains("Independent verification confirmed", StringComparison.Ordinal));
        });

        var retried = await AssertPackageRetryAsync(disk, jobId, saved, Assert.Single(saved.Approvals).ApprovalId);
        Assert.Single(retried.Approvals);
        Assert.Equal(1, runtime.InstallCalls);
    }

    [Theory]
    [InlineData(false, JournalResult.OperationFailed, 1)]
    [InlineData(true, JournalResult.TrustMismatch, 0)]
    public async Task InstallerFailureAndTrustFailureDoNotOfferOrdinaryRetry(
        bool trustFailure, JournalResult expectedResult, int expectedApplies)
    {
        using var disk = new AuditTestStore();
        var runtime = new InterruptedPackageRuntime
        {
            Install = _ => Task.FromResult(new PackageInstallResult(PackageInstallStatus.Failed)),
        };
        var service = new PilotPackageService(runtime);
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        var clock = new FixedClock();
        JobId jobId;
        await using (var workspace = OpenWorkspace(disk, reports, service, clock))
        {
            jobId = await ApproveInitialPackagePlanAsync(workspace);
            if (trustFailure)
            {
                runtime.NextAcquisition = _ => throw new PackageTrustException();
            }
            await workspace.InstallPackageAsync();
        }

        var saved = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(expectedResult, saved.Entries[^1].Result);
        clock.Current = Now.AddMinutes(1);
        await using var reopened = OpenWorkspace(disk, reports, service, clock);
        await reopened.OpenJobAsync(jobId.Value);
        Assert.False(reopened.CanRetryPackage);
        await reopened.RetryPackageAsync();
        reopened.SelectedPlanProfile = Assert.Single(reopened.PlanProfiles, option => !option.Profile.SyntheticData);
        await reopened.BuildPlanReviewAsync();
        reopened.PackageOperatorCode = "fixture-operator";
        Assert.False(reopened.CanApprovePlanReview);
        await reopened.ApprovePlanReviewAsync();
        await reopened.InstallPackageAsync();
        Assert.Equal(expectedApplies, runtime.InstallCalls);
        Assert.Equal(saved.Entries.AsEnumerable(), (await ReadRecoveryHistoryAsync(disk, jobId)).Entries.AsEnumerable());
    }

    [Fact]
    public async Task CatalogRevisionRequiresExplicitApprovalBeforeLinkedRetry()
    {
        using var disk = new AuditTestStore();
        var runtime = new InterruptedPackageRuntime();
        var service = new PilotPackageService(runtime);
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        var clock = new FixedClock();
        JobId jobId;
        JournalPlanSnapshot previousPlan;
        JournalPlanSnapshot currentPlan;
        JournalEntry failed;
        await using (var workspace = OpenWorkspace(disk, reports, service, clock))
        {
            jobId = await PrepareInitialPackagePlanAsync(workspace);
            var current = Assert.IsType<PlanReviewSnapshot>(workspace.PlanReview);
            currentPlan = JournalPlanSnapshot.Create(current.Plan, current.Impact);
            previousPlan = ResolvePriorCatalogPlan(workspace);
            Assert.NotEqual(currentPlan.CatalogSha256, previousPlan.CatalogSha256);
            failed = await SeedPriorCatalogAttemptAsync(disk, clock, previousPlan);
        }

        clock.Current = Now.AddSeconds(5);
        await using var reopened = OpenWorkspace(disk, reports, service, clock);
        await reopened.OpenJobAsync(jobId.Value);
        Assert.True(reopened.CanRetryPackage);
        await reopened.RetryPackageAsync();
        Assert.Equal(0, runtime.InstallCalls);
        Assert.False(reopened.CanInstallPackage);
        Assert.Null(reopened.PlanReviewDecision);
        Assert.Equal(currentPlan.Key.PlanSha256, reopened.PlanReview?.Plan.ContentSha256);
        var prior = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(previousPlan.Key, prior.Plan.Key);
        Assert.Equal(failed, prior.Entries[^1]);
        Assert.Single(prior.Approvals);

        await reopened.BuildPlanReviewAsync();
        reopened.PackageOperatorCode = "fixture-operator";
        Assert.True(reopened.CanApprovePlanReview, reopened.PlanReviewStatus);
        await reopened.ApprovePlanReviewAsync();
        Assert.True(reopened.CanInstallPackage, reopened.PlanReviewStatus);
        await reopened.InstallPackageAsync();
        Assert.Equal(1, runtime.InstallCalls);
        await using var store = await disk.OpenSqliteAsync();
        var histories = await store.LoadJobJournalsAsync(jobId);
        Assert.Equal(2, histories.Length);
        var previous = Assert.Single(histories, item => item.Plan.Key == previousPlan.Key);
        Assert.Equal(prior.Entries.AsEnumerable(), previous.Entries.AsEnumerable());
        Assert.Equal(prior.Approvals.AsEnumerable(), previous.Approvals.AsEnumerable());
        var currentHistory = Assert.Single(histories, item => item.Plan.Key == currentPlan.Key);
        var started = Assert.Single(currentHistory.Entries, entry => entry.State == JournalState.Started);
        Assert.Equal(JournalCheckpoint.PackageRetryDispatchIntent, started.Checkpoint);
        Assert.Equal(failed.EntryId, started.PriorAttemptEntryId);
        Assert.Equal(Assert.Single(currentHistory.Approvals).ApprovalId, started.ApprovalId);
        Assert.Equal(JournalState.Verified, currentHistory.Entries[^1].State);
    }

    private static JournalPlanSnapshot ResolvePriorCatalogPlan(AuditWorkspace workspace)
    {
        const string resourcePrefix = "ThirdLife.UI.Features.PlanReview.PackageSetup.";
        var option = Assert.IsType<PlanProfileOption>(workspace.SelectedPlanProfile);
        var catalogBytes = WorkshopPlanCandidate.ReadPinned(
            resourcePrefix + "catalog.json", CatalogSchema.MaximumDocumentBytes, option.Catalog.ContentSha256);
        var profileBytes = WorkshopPlanCandidate.ReadPinned(
            resourcePrefix + "profile.json", ProfileSchema.MaximumDocumentBytes, option.Profile.ContentSha256);
        var catalogJson = JsonNode.Parse(catalogBytes)!;
        var profileJson = JsonNode.Parse(profileBytes)!;
        const string priorVersion = "0.1.0";
        catalogJson["version"] = priorVersion;
        profileJson["catalog"]!["version"] = priorVersion;
        var catalog = CatalogSchema.Parse(Encoding.UTF8.GetBytes(catalogJson.ToJsonString()), Now);
        var profile = ProfileSchema.Parse(Encoding.UTF8.GetBytes(profileJson.ToJsonString()), catalog);
        var job = workspace.Job!;
        var evidence = AuditPolicyEvidence.Create(job, workspace.Assessment!, Now);
        var earlier = PlanResolver.Resolve(job, option.PackagePolicy!.Document, profile, catalog, evidence, [], Now);
        Assert.Null(earlier.BlockedReason);
        return JournalPlanSnapshot.Create(earlier, ImpactEstimator.Estimate(earlier));
    }

    private static async Task<JournalEntry> SeedPriorCatalogAttemptAsync(
        AuditTestStore disk, FixedClock clock, JournalPlanSnapshot plan)
    {
        await using var store = await OpenPackageStoreAsync(disk, clock);
        var actor = new OperatorId("fixture-operator");
        var actionId = Assert.Single(plan.Actions).ActionId;
        var approval = new JournalApproval(Guid.NewGuid(), plan.Key, actor, Guid.NewGuid(), clock.Current);
        var intent = new JournalEntry(Guid.NewGuid(), plan.Key, actionId, approval.ApprovalId,
            JournalState.Approved, JournalState.Started, JournalActor.Coordinator, new OperatorId("fixture-coordinator"),
            Guid.NewGuid(), clock.Current, JournalResult.None, JournalCheckpoint.PackageDispatchIntent);
        var failure = new JournalEntry(Guid.NewGuid(), plan.Key, actionId, approval.ApprovalId,
            JournalState.Started, JournalState.Failed, JournalActor.PackageBackend, new OperatorId("fixture-backend"),
            intent.CorrelationId, clock.Current, JournalResult.NetworkUnavailable, JournalCheckpoint.PackagePreDispatchFailure);
        Assert.Equal(JournalWriteResult.Recorded, await store.SaveJournalPlanAsync(plan));
        Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalApprovalAsync(approval));
        Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(new JournalEntry(Guid.NewGuid(), plan.Key,
            actionId, approval.ApprovalId, JournalState.Planned, JournalState.Approved, JournalActor.Operator,
            actor, approval.CorrelationId, clock.Current, JournalResult.None, JournalCheckpoint.PackagePlanApproval)));
        Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(intent));
        Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(failure));
        return failure;
    }
}
