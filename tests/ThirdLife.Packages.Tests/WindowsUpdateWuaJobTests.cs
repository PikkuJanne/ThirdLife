using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Packages.WindowsUpdates;

namespace ThirdLife.Packages.Tests;

// Managed automation-object fixtures exercise the fixed WUA calls without contacting
// COM, a service, the network or an installer. Native scan evidence runs separately.
public sealed class WindowsUpdateWuaJobTests
{
    private static readonly WindowsUpdateCandidate Exact = new(
        new WindowsUpdateIdentity(new Guid("b9bd0425-cd83-49c9-8074-dba77be79ddf"), 2), WindowsUpdateClass.Security, 500);

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(17)]
    public void RejectsInvalidServiceCountBeforeReadingRegistrations(int count) =>
        Assert.False(WuaServiceAuthority.IsBuiltIn(count, _ => throw new InvalidOperationException()));

    [Fact]
    public void PreparationLookupChecksExactOfferAndSourceBeforeReturningWuaRecommendation()
    {
        var cases = new (Session Fixture, WindowsUpdateStatus Expected, long? Bytes)[]
        {
            (new Session { Offer = new() { RecommendedHardDiskSpace = 512 }, RebootRequired = true }, WindowsUpdateStatus.Succeeded, 512 * 1_048_576L),
            (new Session(), WindowsUpdateStatus.Succeeded, null),
            (new Session { Offer = new() { RecommendedHardDiskSpace = -1 } }, WindowsUpdateStatus.Succeeded, null),
            (new Session { SearchRows = [] }, WindowsUpdateStatus.ApprovalChanged, null),
            (new Session { SearchRows = [new(), new()] }, WindowsUpdateStatus.ApprovalChanged, null),
            (new Session { Offer = new() { MaxDownloadSize = 501 } }, WindowsUpdateStatus.ApprovalChanged, null),
            (new Session { Services = [] }, WindowsUpdateStatus.Unavailable, null),
            (new Session { ServicesAfterLookup = [] }, WindowsUpdateStatus.Unavailable, null),
        };
        foreach (var (fixture, status, bytes) in cases)
        {
            var result = WuaSession.AssessOffer(Exact, CancellationToken.None, fixture.Create);
            Assert.Equal(Exact, result.Exact);
            Assert.Equal(status, result.Status);
            Assert.Equal(bytes, result.RecommendedInstallationBytes);
            Assert.Null(fixture.Dispatched);
            if (status == WindowsUpdateStatus.Succeeded)
            {
                Assert.NotNull(result.EligibilityReason);
                Assert.Equal(fixture.RebootRequired, result.RebootRequired);
                Assert.Contains($"UpdateID='{Exact.Identity.UpdateId:D}' and RevisionNumber=2", fixture.Criteria, StringComparison.Ordinal);
            }
            else { Assert.Null(result.EligibilityReason); }
        }
    }

    [Fact]
    public void IndependentInstalledQueryRequiresMatchingRevisionAndPositiveInstalledProperty()
    {
        var installed = new WindowsUpdateAgentTests.Update { IsInstalled = true };
        var cases = new (Session Fixture, WindowsUpdateIdentity Query, WindowsUpdateStatus Status, bool? Installed)[]
        {
            (new Session { Offer = installed }, Exact.Identity, WindowsUpdateStatus.Succeeded, true),
            (new Session { Offer = installed }, new(Exact.Identity.UpdateId, 3), WindowsUpdateStatus.Partial, null),
            (new Session { Offer = installed }, new(Guid.NewGuid(), 2), WindowsUpdateStatus.Partial, null),
            (new Session(), Exact.Identity, WindowsUpdateStatus.Partial, null),
            (new Session { SearchRows = [] }, Exact.Identity, WindowsUpdateStatus.Succeeded, null),
            (new Session { SearchRows = [installed, installed] }, Exact.Identity, WindowsUpdateStatus.Partial, null),
            (new Session { ServicesAfterLookup = [] }, Exact.Identity, WindowsUpdateStatus.Unavailable, null),
        };
        foreach (var (fixture, query, status, fact) in cases)
        {
            var observation = WuaSession.ObserveInstalled(query, CancellationToken.None, fixture.Create);
            Assert.Equal(query, observation.Identity);
            Assert.Equal(status, observation.Status);
            Assert.Equal(fact, observation.Installed);
            Assert.StartsWith("IsInstalled=1 and UpdateID=", fixture.Criteria, StringComparison.Ordinal);
            Assert.True(fixture.Online);
            Assert.Null(fixture.Dispatched);
        }
        Assert.Throws<ArgumentException>(() => new WindowsUpdateInstalledObservation(WindowsUpdateStatus.Partial, Exact.Identity, true));
    }

    [Theory]
    [InlineData(false, false, 2, WindowsUpdateStatus.Succeeded)]
    [InlineData(true, true, 2, WindowsUpdateStatus.Succeeded)]
    [InlineData(false, false, 4, WindowsUpdateStatus.Failed)]
    [InlineData(true, true, 4, WindowsUpdateStatus.Failed)]
    public void UsesFreshExactCandidateAndReadsBothJobAndItemResults(bool install, bool reboot,
        int resultCode, WindowsUpdateStatus expected)
    {
        var fixture = new Session { Result = new Result { ResultCode = resultCode, RebootRequired = reboot } };
        var result = WuaSession.Apply(Exact, install, CancellationToken.None, fixture.Create);
        Assert.Equal(expected, result.Status);
        Assert.Equal(reboot, result.RebootRequired);
        Assert.Equal(73, result.ProgressPercent);
        Assert.Equal(install ? "install" : "download", fixture.Dispatched);
        Assert.Equal(Exact.Identity.UpdateId.ToString("D"), Assert.Single(fixture.Updates).Identity.UpdateID);
        Assert.Contains($"UpdateID='{Exact.Identity.UpdateId:D}' and RevisionNumber=2", fixture.Criteria, StringComparison.Ordinal);
        Assert.True(fixture.Online);
        Assert.False(fixture.CanAutomaticallyUpgradeService);
        Assert.False(fixture.IsForced);
        Assert.Equal(2, fixture.Cleanups);
        if (install)
        {
            Assert.True(fixture.ForceQuiet);
            Assert.False(fixture.AllowSourcePrompts);
        }
    }

    [Fact]
    public void ServiceAmbiguityReconfigurationChangedOfferAndPendingRestartPreventBegin()
    {
        var valid = new Service();
        var cases = new[]
        {
            new Session { Services = [valid with { IsManaged = true }] },
            new Session { Services = [valid with { IsRegisteredWithAU = false }] },
            new Session { Services = [valid with { ServiceID = "7971f918-a847-4430-9279-4a52d1efe18d" }] },
            new Session { Services = [valid with { ServiceID = "invalid" }] },
            new Session { Services = [valid, valid with { IsDefaultAUService = false }] },
            new Session { Services = [] },
            new Session { ServicesAfterLookup = [valid with { IsScanPackageService = true }] },
            new Session { Offer = new WindowsUpdateAgentTests.Update { MaxDownloadSize = 501 } },
            new Session { Offer = new WindowsUpdateAgentTests.Update { HandlerID = "unknown" } },
            new Session { Offer = new WindowsUpdateAgentTests.Update { IsDownloaded = false } },
            new Session { RebootRequired = true },
        };
        foreach (var fixture in cases)
        {
            var result = WuaSession.Apply(Exact, true, CancellationToken.None, fixture.Create);
            Assert.Contains(result.Status, new[] { WindowsUpdateStatus.Unavailable, WindowsUpdateStatus.ApprovalChanged });
            Assert.Null(fixture.Dispatched);
        }
    }

    [Theory]
    [InlineData(2, 1, WindowsUpdateStatus.Partial)]
    [InlineData(4, unchecked((int)0x8024001f), WindowsUpdateStatus.Offline)]
    [InlineData(3, unchecked((int)0x80070422), WindowsUpdateStatus.ServiceUnavailable)]
    public void SearchWarningPreventsDispatchAndSurfacesOnlyTheBoundedReason(int code, int warning, WindowsUpdateStatus reason)
    {
        var fixture = new Session { ResultCode = code, Warnings = [new Result { HResult = warning }] };
        var failure = Record.Exception(() => WuaSession.Apply(Exact, false, CancellationToken.None, fixture.Create));
        Assert.NotNull(failure);
        Assert.Equal(reason, WuaSession.Failure(failure));
        Assert.Null(fixture.Dispatched);
        Assert.Equal(1, fixture.Cleanups);
    }

    [Fact]
    public void CancellationWhileWaitingRequestsAbortAndCleansUpBeforeAnyWriter()
    {
        using var cancel = new CancellationTokenSource();
        var fixture = new Session { OnSearch = cancel.Cancel };
        Assert.ThrowsAny<OperationCanceledException>(() => WuaSession.Apply(Exact, false, cancel.Token, fixture.Create));
        Assert.Equal(1, fixture.Aborts);
        Assert.Equal(1, fixture.Cleanups);
        Assert.Null(fixture.Dispatched);
    }

    public sealed record Service
    {
        public string ServiceID { get; init; } = "9482f4b4-e343-43b6-b170-9a65bc822c77";
        public bool IsDefaultAUService { get; init; } = true;
        public bool IsRegisteredWithAU { get; init; } = true;
        public bool IsManaged { get; init; }
        public bool IsScanPackageService { get; init; }
    }

    public sealed class Result
    {
        public int ResultCode { get; init; } = 2;
        public int HResult { get; init; }
        public bool RebootRequired { get; init; }
        public Result GetUpdateResult(int index) => index == 0 ? this : throw new ArgumentOutOfRangeException(nameof(index));
    }

    public sealed class Session : IWuaRegistrationSource
    {
        private static readonly Guid CollectionClass = new("13639463-00db-4646-803d-528026140d88");
        public object Create(Guid id) => id == CollectionClass ? new List<WindowsUpdateAgentTests.Update>() : this;
        public List<Service> Services { get; set; } = [new Service()];
        public List<Service>? ServicesAfterLookup { get; init; }
        public WindowsUpdateAgentTests.Update Offer { get; init; } = new();
        public Result Result { get; init; } = new();
        public int ResultCode { get; init; } = 2;
        public List<Result> Warnings { get; init; } = [];
        public Action? OnSearch { get; init; }
        public bool Online { get; set; }
        public bool CanAutomaticallyUpgradeService { get; set; } = true;
        public bool IsForced { get; set; } = true;
        public bool RebootRequired { get; init; }
        public bool IsBusy { get; init; }
        public bool RebootRequiredBeforeInstallation { get; init; }
        public bool AllowSourcePrompts { get; set; } = true;
        public bool ForceQuiet { get; set; }
        public string? ClientApplicationID { get; set; }
        public List<WindowsUpdateAgentTests.Update> Updates { get; set; } = [];
        public List<WindowsUpdateAgentTests.Update>? SearchRows { get; init; }
        public string? Criteria { get; private set; }
        public string? Dispatched { get; private set; }
        public int Cleanups { get; private set; }
        public int Aborts { get; private set; }
        public int PercentComplete { get; init; } = 73;
        int IWuaRegistrationSource.Count => Services.Count;
        WuaServiceRegistration IWuaRegistrationSource.ReadRegistration(int index)
        {
            var item = Services[index];
            return new(item.IsManaged, item.IsDefaultAUService, item.IsRegisteredWithAU,
                item.IsScanPackageService, Guid.TryParseExact(item.ServiceID, "D", out var id) ? id : null);
        }
        public Session CreateUpdateSearcher() => this;
        public Session CreateUpdateInstaller() => this;
        public Session CreateUpdateDownloader() => this;
        public Session BeginSearch(string criteria, WuaNotification completed, object? state)
        {
            Criteria = criteria;
            if (OnSearch is null) { completed.Invoke(this, this); }
            else { OnSearch(); }
            return this;
        }
        public Session EndSearch(Session job)
        {
            Updates = SearchRows ?? [Offer];
            if (ServicesAfterLookup is not null) { Services = ServicesAfterLookup; }
            return this;
        }
        public Session BeginInstall(WuaNotification progress, WuaNotification completed, object? state) => Begin("install", completed);
        public Session BeginDownload(WuaNotification progress, WuaNotification completed, object? state) => Begin("download", completed);
        private Session Begin(string operation, WuaNotification completed)
        {
            Dispatched = operation;
            completed.Invoke(this, this);
            return this;
        }
        public Session GetProgress() => this;
        public Result EndDownload(Session job) => Result;
        public Result EndInstall(Session job) => Result;
        public void CleanUp() => Cleanups++;
        public void RequestAbort() => Aborts++;
    }
}
