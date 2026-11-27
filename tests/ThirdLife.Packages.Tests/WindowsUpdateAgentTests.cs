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
    public async Task OrdinaryAdapterNeverInvokesAWriterAndCancelledScanMakesNoNativeCall()
    {
        var adapter = new WindowsUpdateAgent();
        var exact = new WindowsUpdateCandidate(new WindowsUpdateIdentity(Guid.NewGuid(), 1), WindowsUpdateClass.Security, 500);
        Assert.Equal(WindowsUpdateStatus.Unavailable, (await adapter.DownloadAsync(exact, CancellationToken.None)).Status);
        Assert.Equal(WindowsUpdateStatus.Unavailable, (await adapter.InstallAsync(exact, CancellationToken.None)).Status);
        Assert.Equal(WindowsUpdateStatus.Cancelled, (await adapter.ScanAsync(new CancellationToken(true))).Status);
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
        var candidate = WuaSession.ReadCandidate(scope, update);
        Assert.Equal(expected, candidate.Classification);
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
            software with { BundledUpdates = new List<int> { 1 } },
            software with { EulaAccepted = false },
            software with { HandlerID = "http://schemas.microsoft.com/msus/2002/12/UpdateHandlers/InfBasedInstallation" },
            software with { HandlerID = "http://schemas.microsoft.com/msus/2002/12/UpdateHandlers/CommandLineInstallation" },
            software with { BrowseOnly = true },
            software with { InstallationBehavior = new Behavior { CanRequestUserInput = true } },
            software with { InstallationBehavior = null },
            software with { Categories = [new Category(), new Category()] },
            software with { Categories = Enumerable.Repeat(new Category(), 33).ToList() },
            software with { MaxDownloadSize = -1 },
        };
        foreach (var item in restricted)
        {
            Assert.Equal(WindowsUpdateClass.Restricted, WuaSession.ReadCandidate(scope, item).Classification);
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
