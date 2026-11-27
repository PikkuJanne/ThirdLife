using System.Diagnostics;
using System.Runtime.InteropServices;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Packages.WindowsUpdates;
using Xunit.Abstractions;

namespace ThirdLife.Packages.Tests;

public sealed class WindowsUpdateAgentTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task ActiveMachineScansOnlyTheConfiguredWindowsUpdateService()
    {
        var timer = Stopwatch.StartNew();
        var result = await new WindowsUpdateAgent().ScanAsync(CancellationToken.None);
        output.WriteLine($"WUA online scan status={result.Status}, count={result.Updates.Count}, reboot={result.RebootRequired}, elapsed={timer.Elapsed}.");
        foreach (var group in result.Updates.GroupBy(update => update.Classification))
        {
            output.WriteLine($"Offered {group.Key}: {group.Count()}.");
        }
        Assert.Contains(result.Status, new[] { WindowsUpdateStatus.Succeeded, WindowsUpdateStatus.Offline,
            WindowsUpdateStatus.AccessDenied, WindowsUpdateStatus.ServiceUnavailable, WindowsUpdateStatus.TimedOut,
            WindowsUpdateStatus.Partial, WindowsUpdateStatus.LimitExceeded });
        Assert.InRange(result.Updates.Count, 0, WindowsUpdatePolicy.MaximumUpdates);
        if (result.Status == WindowsUpdateStatus.Succeeded) { Assert.NotNull(result.RebootRequired); }
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task ActiveMachineAssessesOneExactOfferedRevisionWithoutMutation()
    {
        var agent = new WindowsUpdateAgent();
        var scan = await agent.ScanAsync(CancellationToken.None);
        Assert.Equal(WindowsUpdateStatus.Succeeded, scan.Status);
        output.WriteLine($"Offered {scan.Updates.Count} revisions, selectable {scan.Updates.Count(WindowsUpdatePolicy.IsSelectable)}.");
        var exact = scan.Updates.FirstOrDefault(WindowsUpdatePolicy.IsSelectable)
            ?? scan.Updates.FirstOrDefault(update => update.Classification != WindowsUpdateClass.Driver)
            ?? (scan.Updates.Count > 0 ? scan.Updates[0] : null);
        if (exact is null) { output.WriteLine("The configured service offered no revision to assess."); return; }

        var timer = Stopwatch.StartNew();
        var observation = await agent.AssessPreparationAsync(exact, CancellationToken.None);
        Assert.Equal(exact, observation.Exact);
        output.WriteLine($"Exact {exact.Classification} lookup status={observation.Status}, reason={observation.EligibilityReason}, " +
            $"servicingSupported={observation.ServicingSupported}, powerReady={observation.PowerReady}, restart={observation.RebootRequired}, " +
            $"systemObserved={observation.SystemAvailableBytes.HasValue}, cacheObserved={observation.CacheAvailableBytes.HasValue}, " +
            $"sharedVolume={observation.CacheSharesSystemVolume}, recommendedBytes={observation.RecommendedInstallationBytes}, " +
            $"recommendedSpaceMet={observation.HasRecommendedSpace}, removalSupported={observation.RemovalSupported?.ToString() ?? "Unknown"}, elapsed={timer.Elapsed}.");
        Assert.Contains(observation.Status, new[] { WindowsUpdateStatus.Succeeded, WindowsUpdateStatus.ApprovalChanged,
            WindowsUpdateStatus.Unavailable, WindowsUpdateStatus.Offline, WindowsUpdateStatus.AccessDenied,
            WindowsUpdateStatus.ServiceUnavailable, WindowsUpdateStatus.TimedOut, WindowsUpdateStatus.Partial });
        if (observation.Status == WindowsUpdateStatus.Succeeded)
        {
            Assert.NotNull(observation.EligibilityReason);
            Assert.NotNull(observation.RebootRequired);
        }
        else
        {
            Assert.Null(observation.HasRecommendedSpace);
            Assert.Null(observation.RemovalSupported);
        }
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task ActiveMachineObservesExactInstalledRevisionThroughIndependentSearch()
    {
        var exact = ReadHistoricalCandidate();
        var historicalCandidate = exact is not null;
        exact ??= new WindowsUpdateIdentity(new Guid("bf830723-5bf7-489d-ae31-6bfcfc0927d3"), 1);
        var timer = Stopwatch.StartNew();
        var observation = await new WindowsUpdateAgent().ObserveInstalledAsync(exact, CancellationToken.None);
        output.WriteLine($"WUA installed-only lookup status={observation.Status}, historicalCandidate={historicalCandidate}, " +
            $"positive={observation.Installed == true}, elapsed={timer.Elapsed}.");
        Assert.Equal(exact, observation.Identity);
        Assert.NotEqual(WindowsUpdateStatus.Failed, observation.Status);
        if (observation.Installed.HasValue)
        {
            Assert.True(historicalCandidate);
            Assert.True(observation.Installed);
            Assert.Equal(WindowsUpdateStatus.Succeeded, observation.Status);
        }
    }

    // A history row selects the read-only test input. Only the independent
    // installed-only query above may report a positive observation.
    private static WindowsUpdateIdentity? ReadHistoricalCandidate()
    {
        using var scope = new ComScope();
        dynamic session = scope.Create(new Guid("4cb43d7f-7eee-4906-8698-60da1c38f2fe"));
        dynamic searcher = scope.Own(session.CreateUpdateSearcher());
        int count = Math.Min(8, (int)searcher.GetTotalHistoryCount());
        if (count <= 0) { return null; }
        dynamic rows = scope.Own(searcher.QueryHistory(0, count));
        for (var i = 0; i < count; i++)
        {
            dynamic row = scope.Own(rows[i]);
            if ((int)row.Operation != 1 || (int)row.ResultCode != 2) { continue; }
            dynamic identity = scope.Own(row.UpdateIdentity);
            if (Guid.TryParseExact((string)identity.UpdateID, "D", out Guid id) && id != Guid.Empty &&
                (int)identity.RevisionNumber > 0)
            {
                return new WindowsUpdateIdentity(id, (int)identity.RevisionNumber);
            }
        }
        return null;
    }

    [Fact]
    public async Task OrdinaryAdapterNeverInvokesAWriterAndCancelledScanMakesNoNativeCall()
    {
        var adapter = new WindowsUpdateAgent();
        var exact = new WindowsUpdateCandidate(new WindowsUpdateIdentity(Guid.NewGuid(), 1), WindowsUpdateClass.Security, 500);
        Assert.Equal(WindowsUpdateStatus.Unavailable, (await adapter.DownloadAsync(exact, CancellationToken.None)).Status);
        Assert.Equal(WindowsUpdateStatus.Unavailable, (await adapter.InstallAsync(exact, CancellationToken.None)).Status);
        Assert.Equal(WindowsUpdateStatus.Cancelled, (await adapter.ScanAsync(new CancellationToken(true))).Status);
        Assert.Equal(WindowsUpdateStatus.Cancelled, (await adapter.AssessPreparationAsync(exact, new CancellationToken(true))).Status);
    }

    [Theory]
    [InlineData("0FA1201D-4330-4FA8-8AE9-B877473B6441", WindowsUpdateClass.Security)]
    [InlineData("e6cf1350-c01b-414d-a61f-263d14d133b4", WindowsUpdateClass.Critical)]
    [InlineData("11111111-2222-3333-4444-555555555555", WindowsUpdateClass.Other)]
    [InlineData("Security Updates", WindowsUpdateClass.Restricted)]
    public void OnlyDocumentedClassificationGuidsAdmitSoftware(string category, WindowsUpdateClass expected)
    {
        using var scope = new ComScope();
        var update = new Update { Categories = [new Category { CategoryID = category }] };
        var candidate = WuaSession.ReadCandidate(scope, update, out var reason);
        Assert.Equal(expected, candidate.Classification);
        Assert.Equal(expected switch
        {
            WindowsUpdateClass.Restricted => WindowsUpdateEligibilityReason.AmbiguousClassification,
            WindowsUpdateClass.Other => WindowsUpdateEligibilityReason.OutsideApprovedClasses,
            _ => WindowsUpdateEligibilityReason.Eligible,
        }, reason);
        Assert.Equal(500L, candidate.MaximumDownloadBytes);
        Assert.Equal(2, candidate.Identity.Revision);
    }

    [Fact]
    public void DriverBundleLicenceInteractivePreviewAndAmbiguousMetadataStayOutOfSelection()
    {
        var software = new Update();
        using var scope = new ComScope();
        var restricted = new[]
        {
            (software with { BundledUpdates = new List<int> { 1 } }, WindowsUpdateEligibilityReason.Bundle),
            (software with { EulaAccepted = false }, WindowsUpdateEligibilityReason.LicenceAcceptance),
            (software with { HandlerID = "http://schemas.microsoft.com/msus/2002/12/UpdateHandlers/InfBasedInstallation" }, WindowsUpdateEligibilityReason.UnsupportedHandler),
            (software with { HandlerID = "http://schemas.microsoft.com/msus/2002/12/UpdateHandlers/CommandLineInstallation" }, WindowsUpdateEligibilityReason.UnsupportedHandler),
            (software with { BrowseOnly = true }, WindowsUpdateEligibilityReason.BrowseOnly),
            (software with { IsBeta = true }, WindowsUpdateEligibilityReason.Beta),
            (software with { CanRequireSource = true }, WindowsUpdateEligibilityReason.SourceMedia),
            (software with { IsMandatory = true }, WindowsUpdateEligibilityReason.Mandatory),
            (software with { IsInstalled = true }, WindowsUpdateEligibilityReason.Inapplicable),
            (software with { Type = 17 }, WindowsUpdateEligibilityReason.UnsupportedUpdateType),
            (software with { InstallationBehavior = new Behavior { CanRequestUserInput = true } }, WindowsUpdateEligibilityReason.Interactive),
            (software with { InstallationBehavior = null }, WindowsUpdateEligibilityReason.UnknownInstallationBehavior),
            (software with { Categories = [new Category(), new Category()] }, WindowsUpdateEligibilityReason.AmbiguousClassification),
            (software with { Categories = Enumerable.Repeat(new Category(), 33).ToList() }, WindowsUpdateEligibilityReason.AmbiguousClassification),
            (software with { MaxDownloadSize = -1 }, WindowsUpdateEligibilityReason.UnknownDownloadSize),
            (software with { MaxDownloadSize = WindowsUpdateCandidate.DownloadSizeLimit + 1 }, WindowsUpdateEligibilityReason.UnknownDownloadSize),
        };
        foreach (var (item, expected) in restricted)
        {
            Assert.Equal(WindowsUpdateClass.Restricted, WuaSession.ReadCandidate(scope, item, out var reason).Classification);
            Assert.Equal(expected, reason);
        }

        Assert.Equal(WindowsUpdateClass.Driver, WuaSession.ReadCandidate(scope, software with { Type = 2 }).Classification);
        Assert.Equal(WindowsUpdateClass.Other, WuaSession.ReadCandidate(scope, software with
        {
            Categories = [new Category { Type = "Product" }],
        }).Classification);
    }

    [Theory]
    [InlineData(2, 0, 2, 0, WindowsUpdateStatus.Succeeded)]
    [InlineData(2, 0, 4, 0, WindowsUpdateStatus.Failed)]
    [InlineData(3, 0, 2, 0, WindowsUpdateStatus.Partial)]
    [InlineData(4, 0, 2, 0, WindowsUpdateStatus.Failed)]
    [InlineData(2, 0, 5, 0, WindowsUpdateStatus.Cancelled)]
    [InlineData(2, 0, 2, 1, WindowsUpdateStatus.Partial)]
    [InlineData(4, 0, 4, unchecked((int)0x8024001f), WindowsUpdateStatus.Offline)]
    [InlineData(4, unchecked((int)0x80070422), 4, 0, WindowsUpdateStatus.ServiceUnavailable)]
    public void OverallAndPerIdentityResultsMustBothSucceed(int overall, int overallError, int item, int itemError,
        WindowsUpdateStatus expected) => Assert.Equal(expected, WuaSession.OperationStatus(overall, overallError, item, itemError));

    [Fact]
    public void NativeErrorsProduceOnlyBoundedStatusCodes()
    {
        var error = Marshal.GetExceptionForHR(unchecked((int)0x8024001f), -1)!;
        Assert.Equal(WindowsUpdateStatus.Offline, WuaSession.Failure(error));
    }

    public sealed record Update
    {
        public Identity Identity { get; } = new();
        public int Type { get; init; } = 1;
        public string HandlerID { get; init; } = WuaSession.CbsHandler;
        public bool IsDownloaded { get; init; } = true;
        public decimal MaxDownloadSize { get; init; } = 500;
        public Func<object?> ReadDiskRecommendation { get; init; } = () => 0;
        public object? RecommendedHardDiskSpace => ReadDiskRecommendation();
        public Func<object?> ReadRemovalSupport { get; init; } = () => null;
        public object? IsUninstallable => ReadRemovalSupport();
        public bool IsBeta { get; init; }
        public bool IsInstalled { get; init; }
        public bool IsMandatory { get; init; }
        public bool IsHidden { get; init; }
        public int DeploymentAction { get; init; } = 1;
        public bool BrowseOnly { get; init; }
        public bool CanRequireSource { get; init; }
        public bool EulaAccepted { get; init; } = true;
        public List<int>? BundledUpdates { get; init; }
        public Behavior? InstallationBehavior { get; init; } = new();
        public List<Category> Categories { get; init; } = [new Category()];
    }

    public sealed class Identity
    {
        public string UpdateID { get; init; } = "b9bd0425-cd83-49c9-8074-dba77be79ddf";
        public int RevisionNumber { get; init; } = 2;
    }

    public sealed class Category
    {
        public string Type { get; init; } = "UpdateClassification";
        public string CategoryID { get; init; } = "0fa1201d-4330-4fa8-8ae9-b877473b6441";
    }

    public sealed class Behavior
    {
        public bool CanRequestUserInput { get; init; }
    }
}
