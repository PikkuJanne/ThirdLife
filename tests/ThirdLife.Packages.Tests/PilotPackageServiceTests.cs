using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Registry;

namespace ThirdLife.Packages.Tests;

public sealed class PilotPackageServiceTests
{
    private static readonly PilotPackageIdentity Identity = PilotPackageService.Identity;

    [Theory]
    [InlineData("other-source", null, null, null, ActionScope.Machine, ActionArchitecture.X64, null)]
    [InlineData(null, "Other.Package", null, null, ActionScope.Machine, ActionArchitecture.X64, null)]
    [InlineData(null, null, "8.9.9", null, ActionScope.Machine, ActionArchitecture.X64, null)]
    [InlineData(null, null, null, "Some Other Publisher", ActionScope.Machine, ActionArchitecture.X64, null)]
    [InlineData(null, null, null, null, ActionScope.User, ActionArchitecture.X64, null)]
    [InlineData(null, null, null, null, ActionScope.Machine, ActionArchitecture.Arm64, null)]
    [InlineData(null, null, null, null, ActionScope.Machine, ActionArchitecture.X64, "other-hash")]
    public async Task MaterialChangesNeverReachAcquisitionOrDispatch(string? source, string? package, string? version,
        string? publisher, ActionScope scope, ActionArchitecture architecture, string? hash)
    {
        var runtime = new FakeRuntime();
        var service = new PilotPackageService(runtime);
        var request = ActionRegistry.CreateInstallPackage(Guid.NewGuid(), source ?? Identity.SourceId,
            package ?? Identity.PackageId, version ?? Identity.ExactVersion, publisher ?? Identity.Publisher, scope, architecture);

        var result = await service.InstallAsync(request, hash ?? Identity.InstallerSha256, Authorize, null,
            CancellationToken.None);

        Assert.Equal(PackageInstallStatus.TrustMismatch, result.Status);
        Assert.Equal(0, runtime.Acquisitions);
        Assert.Equal(0, runtime.Dispatches);
    }

    [Fact]
    public async Task FailureToVerifyInstallerNeverReachesDispatch()
    {
        var runtime = new FakeRuntime { AcquisitionError = new PackageTrustException() };
        var service = new PilotPackageService(runtime);

        var resolution = await service.ResolveAsync(CancellationToken.None);
        var result = await service.InstallAsync(Request(), Identity.InstallerSha256, Authorize, null, CancellationToken.None);

        Assert.Equal(PackageResolutionStatus.TrustMismatch, resolution.Status);
        Assert.Equal(PackageInstallStatus.TrustMismatch, result.Status);
        Assert.Equal(2, runtime.Acquisitions);
        Assert.Equal(0, runtime.Dispatches);
    }

    [Fact]
    public async Task InstalledStateChangeDuringAcquisitionBlocksDispatch()
    {
        var runtime = new FakeRuntime
        {
            OnAcquisition = state => state.Observation = new(PackageObservationStatus.Conflict),
        };
        var service = new PilotPackageService(runtime);

        var result = await service.InstallAsync(Request(), Identity.InstallerSha256, Authorize, null, CancellationToken.None);

        Assert.Equal(PackageInstallStatus.Conflict, result.Status);
        Assert.Equal(0, runtime.Dispatches);
        Assert.True(runtime.Artifact.Disposed);
    }

    [Fact]
    public async Task UnavailableInstalledStateAndPrecancelledOperationDoNotAcquire()
    {
        var runtime = new FakeRuntime { Observation = new(PackageObservationStatus.Unavailable) };
        var service = new PilotPackageService(runtime);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Assert.Equal(PackageResolutionStatus.Cancelled, (await service.ResolveAsync(cancelled.Token)).Status);
        Assert.Equal(PackageResolutionStatus.Unavailable, (await service.ResolveAsync(CancellationToken.None)).Status);
        Assert.Equal(PackageInstallStatus.Cancelled,
            (await service.InstallAsync(Request(), Identity.InstallerSha256, Authorize, null, cancelled.Token)).Status);
        Assert.Equal(PackageInstallStatus.Unavailable,
            (await service.InstallAsync(Request(), Identity.InstallerSha256, Authorize, null, CancellationToken.None)).Status);
        Assert.Equal(0, runtime.Acquisitions);
    }

    [Fact]
    public async Task AuthorizationRecheckedAfterAcquisitionCanRejectDispatch()
    {
        var runtime = new FakeRuntime();
        var service = new PilotPackageService(runtime);

        var result = await service.InstallAsync(Request(), Identity.InstallerSha256, _ =>
        {
            Assert.Equal(1, runtime.Acquisitions);
            Assert.False(runtime.Artifact.Disposed);
            return Task.FromResult(false);
        }, null, CancellationToken.None);

        Assert.Equal(PackageInstallStatus.PreconditionChanged, result.Status);
        Assert.Equal(0, runtime.Dispatches);
        Assert.True(runtime.Artifact.Disposed);
    }

    [Theory]
    [InlineData(PackageInstallStatus.Applied)]
    [InlineData(PackageInstallStatus.AuthorizationDeclined)]
    [InlineData(PackageInstallStatus.RestartRequired)]
    [InlineData(PackageInstallStatus.Cancelled)]
    [InlineData(PackageInstallStatus.Failed)]
    public async Task BackendResultAndInstalledObservationAreIndependent(PackageInstallStatus status)
    {
        var runtime = new FakeRuntime { Apply = () => Task.FromResult(new PackageInstallResult(status)) };
        var service = new PilotPackageService(runtime);

        var result = await service.InstallAsync(Request(), Identity.InstallerSha256, Authorize, null, CancellationToken.None);

        Assert.Equal(status, result.Status);
        Assert.Equal(PackageObservationStatus.Absent, service.Inspect().Status);
        Assert.True(runtime.Artifact.Disposed);
    }

    [Fact]
    public async Task CancellingAfterDispatchKeepsArtifactAndBlocksConcurrentRetry()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource<PackageInstallResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new FakeRuntime
        {
            Apply = () =>
            {
                started.SetResult();
                return finished.Task;
            },
        };
        var service = new PilotPackageService(runtime);
        using var cancellation = new CancellationTokenSource();
        var pending = service.InstallAsync(Request(), Identity.InstallerSha256, Authorize, null, cancellation.Token);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cancellation.CancelAsync();
            Assert.Equal(PackageInstallStatus.ReviewRequired, (await pending).Status);
            Assert.Equal(PackageInstallStatus.ReviewRequired,
                (await service.InstallAsync(Request(), Identity.InstallerSha256, Authorize, null, CancellationToken.None)).Status);
            Assert.False(runtime.Artifact.Disposed);
        }
        finally
        {
            finished.SetResult(new(PackageInstallStatus.Failed));
            await runtime.Artifact.Disposal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task ReviewPinnedVendorPackageWithoutApplyingIt()
    {
        using var inspectionTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var service = new PilotPackageService();

        Assert.Equal(PackageObservationStatus.Absent, service.Inspect().Status);
        var resolution = await service.ResolveAsync(inspectionTimeout.Token);

        Assert.Equal(PackageResolutionStatus.Ready, resolution.Status);
        Assert.Equal(PackageObservationStatus.Absent, resolution.Observation.Status);
        Assert.Equal(PackageObservationStatus.Absent, service.Inspect().Status);
    }

    private static InstallPackageActionRequest Request() => ActionRegistry.CreateInstallPackage(Guid.NewGuid(),
        Identity.SourceId, Identity.PackageId, Identity.ExactVersion, Identity.Publisher, ActionScope.Machine, ActionArchitecture.X64);

    private static Task<bool> Authorize(CancellationToken _) => Task.FromResult(true);

    private sealed class FakeRuntime : IPilotPackageRuntime
    {
        public int Acquisitions { get; private set; }
        public int Dispatches { get; private set; }
        public FakeArtifact Artifact { get; } = new();
        public Exception? AcquisitionError { get; init; }
        public Action<FakeRuntime>? OnAcquisition { get; init; }
        public Func<Task<PackageInstallResult>> Apply { get; init; } = () =>
            Task.FromResult(new PackageInstallResult(PackageInstallStatus.Applied));
        public PackageObservation Observation { get; set; } = new(PackageObservationStatus.Absent);

        public PackageObservation Inspect() => Observation;

        public Task<IPackageArtifact> AcquireAsync(CancellationToken cancellationToken)
        {
            Acquisitions++;
            OnAcquisition?.Invoke(this);
            return AcquisitionError is { } error ? Task.FromException<IPackageArtifact>(error) : Task.FromResult<IPackageArtifact>(Artifact);
        }

        public Task<PackageInstallResult> ApplyAsync(IPackageArtifact artifact, CancellationToken cancellationToken)
        {
            Dispatches++;
            return Apply();
        }
    }

    private sealed class FakeArtifact : IPackageArtifact
    {
        public TaskCompletionSource Disposal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed => Disposal.Task.IsCompleted;
        public void Dispose() => Disposal.SetResult();
    }
}
