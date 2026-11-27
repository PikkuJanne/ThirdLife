using ThirdLife.Actions.Registry;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.UI.Shell;

namespace ThirdLife.UI.Tests;

public sealed class WindowsUpdateStatusTests
{
    [Theory]
    [InlineData(WindowsUpdateStage.DownloadPreparing, WindowsUpdateStatus.Cancelled, "download", "cancelled")]
    [InlineData(WindowsUpdateStage.InstallPreparing, WindowsUpdateStatus.TimedOut, "install", "expired")]
    public void KnownPreDispatchStopNamesThePhaseAndOffersFreshReview(WindowsUpdateStage phase,
        WindowsUpdateStatus status, string action, string reason)
    {
        var prepared = Preparing(phase);
        var stopped = prepared with
        {
            Id = Guid.NewGuid(),
            PreviousId = prepared.Id,
            Stage = WindowsUpdateStage.DispatchNotSent,
            Operation = new(status, null)
        };
        var message = AuditWorkspace.DescribeSavedWindowsUpdate([prepared, stopped]);
        Assert.Contains($"before asking Windows to {action}", message);
        Assert.Contains(reason, message);
        Assert.Contains("fresh scan", message);
        var restart = stopped with { Operation = new(WindowsUpdateStatus.ApprovalChanged, true) };
        Assert.Contains("pending restart", AuditWorkspace.DescribeSavedWindowsUpdate([prepared, restart]));
    }

    [Fact]
    public void VerifyDistinguishesSavedIneligibleOffersFromRecoveryAndUnknownHistoricalReasons()
    {
        var prepared = Preparing(WindowsUpdateStage.InstallPreparing);
        var excluded = new WindowsUpdateCandidate(new(Guid.NewGuid(), 3), WindowsUpdateClass.Restricted, 2048);
        var scan = prepared with
        {
            Id = Guid.NewGuid(),
            PreviousId = null,
            Stage = WindowsUpdateStage.ScanCompleted,
            Approval = null,
            Scan = new(WindowsUpdateStatus.Succeeded, [excluded], false, [WindowsUpdateEligibilityReason.UnsupportedHandler])
        };
        var status = AuditWorkspace.DescribeSavedWindowsUpdate([scan]);
        Assert.Contains("offered one applicable revision", status);
        Assert.Contains("None met the servicing requirements", status);
        Assert.Contains("required component-based servicing handler", status);
        var historical = scan with { Scan = new(WindowsUpdateStatus.Succeeded, [excluded], false) };
        Assert.Contains("None met the servicing requirements", AuditWorkspace.DescribeSavedWindowsUpdate([historical]));
        Assert.DoesNotContain("handler", AuditWorkspace.DescribeSavedWindowsUpdate([historical]));
        var pendingRestart = scan with { Scan = new(WindowsUpdateStatus.Succeeded, [excluded], true) };
        Assert.Contains("required restart", AuditWorkspace.DescribeSavedWindowsUpdate([pendingRestart]));
        var sent = prepared with { Stage = WindowsUpdateStage.InstallStarted };
        Assert.Contains("needs workshop review", AuditWorkspace.DescribeSavedWindowsUpdate([sent, scan]));
        var receipt = prepared with
        {
            Id = Guid.NewGuid(),
            PreviousId = sent.Id,
            Stage = WindowsUpdateStage.InstallCompleted,
            Operation = new(WindowsUpdateStatus.Succeeded, false)
        };
        Assert.Contains("needs workshop review", AuditWorkspace.DescribeSavedWindowsUpdate([sent, receipt, scan]));
    }

    [Fact]
    public void LaterScanAndUncertainInstallRetainTheirOwnStatus()
    {
        var prepared = Preparing(WindowsUpdateStage.InstallPreparing);
        var stopped = prepared with
        {
            Id = Guid.NewGuid(),
            PreviousId = prepared.Id,
            Stage = WindowsUpdateStage.DispatchNotSent,
            Operation = new(WindowsUpdateStatus.Cancelled, null)
        };
        var rescan = new WindowsUpdateEntry(Guid.NewGuid(), stopped.Id, prepared.JobId, prepared.GateId,
            prepared.OccurredAtUtc, WindowsUpdateStage.ScanStarted, null, null, null);
        var fresh = rescan with
        {
            Id = Guid.NewGuid(),
            PreviousId = rescan.Id,
            Stage = WindowsUpdateStage.ScanCompleted,
            Scan = new(WindowsUpdateStatus.Succeeded, [], false)
        };
        Assert.Contains("offered no applicable", AuditWorkspace.DescribeSavedWindowsUpdate([prepared, stopped, rescan, fresh]));
        var started = prepared with { Id = prepared.Approval!.Actions!.Install.ActionId, PreviousId = prepared.Id, Stage = WindowsUpdateStage.InstallStarted };
        var uncertain = stopped with
        {
            PreviousId = started.Id,
            Stage = WindowsUpdateStage.InstallCompleted,
            Operation = new(WindowsUpdateStatus.Partial, null)
        };
        Assert.Contains("needs workshop review", AuditWorkspace.DescribeSavedWindowsUpdate([prepared, started, uncertain]));
        var unconfirmed = uncertain with { Stage = WindowsUpdateStage.DispatchUnconfirmed };
        var message = AuditWorkspace.DescribeSavedWindowsUpdate([prepared, started, unconfirmed]);
        Assert.Contains("handed off", message);
        Assert.Contains("needs workshop review", message);
        Assert.DoesNotContain("fresh scan", message);
        var laterScan = rescan with { PreviousId = unconfirmed.Id };
        Assert.Contains("needs workshop review", AuditWorkspace.DescribeSavedWindowsUpdate([prepared, started, unconfirmed, laterScan, fresh]));
    }

    private static WindowsUpdateEntry Preparing(WindowsUpdateStage phase)
    {
        var offer = new WindowsUpdateCandidate(new(Guid.NewGuid(), 1), WindowsUpdateClass.Security, 1024);
        var impacts = ActionRegistry.DescribeWindowsUpdateServicing(offer);
        var actions = new WindowsUpdateApprovedActions(Guid.NewGuid(), new(Guid.NewGuid(), impacts.Download), new(Guid.NewGuid(), impacts.Install));
        var approval = new WindowsUpdateApproval(Guid.NewGuid(), offer, new OperatorId("operator-synthetic"),
            "policy-decision-00000000000000000000000000000001", actions);
        return new(Guid.NewGuid(), Guid.NewGuid(), new JobId("job-status"), new SanitizationGateDecisionId("gate-status"),
            DateTimeOffset.UnixEpoch, phase, null, approval, null);
    }
}
