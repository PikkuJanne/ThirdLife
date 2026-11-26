using System.Diagnostics;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Registry;

namespace ThirdLife.Packages.Tests;

public sealed class PilotPackageServiceTests
{
    private const string UntrustedBackendDetail = "fixture-user C:\\Users\\fixture-user\\Documents\\private.msi " +
        "FixtureWifi 192.0.2.14 serial FIXTURE-000123 https://packages.example.invalid/private?token=fixture-secret " +
        "\0\r\n\u202E\uD800";
    private static readonly PilotPackageIdentity Identity = PilotPackageService.Identity;
    private static readonly Guid Correlation = Guid.NewGuid();

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

        var result = await service.InstallAsync(request, hash ?? Identity.InstallerSha256, Correlation, Authorize, null,
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
        var result = await service.InstallAsync(Request(), Identity.InstallerSha256, Correlation, Authorize, null, CancellationToken.None);

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

        var result = await service.InstallAsync(Request(), Identity.InstallerSha256, Correlation, Authorize, null, CancellationToken.None);

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
        Assert.Equal(PackageInstallStatus.CancelledBeforeDispatch,
            (await service.InstallAsync(Request(), Identity.InstallerSha256, Correlation, Authorize, null, cancelled.Token)).Status);
        Assert.Equal(PackageInstallStatus.Unavailable,
            (await service.InstallAsync(Request(), Identity.InstallerSha256, Correlation, Authorize, null, CancellationToken.None)).Status);
        Assert.Equal(0, runtime.Acquisitions);
    }

    [Fact]
    public async Task AuthorizationRecheckedAfterAcquisitionCanRejectDispatch()
    {
        var runtime = new FakeRuntime();
        var service = new PilotPackageService(runtime);
        var progress = new RecordingProgress();

        var result = await service.InstallAsync(Request(), Identity.InstallerSha256, Correlation, _ =>
        {
            Assert.Equal(1, runtime.Acquisitions);
            Assert.False(runtime.Artifact.Disposed);
            return Task.FromResult(false);
        }, progress, CancellationToken.None);

        Assert.Equal(PackageInstallStatus.PreconditionChanged, result.Status);
        Assert.Equal(0, runtime.Dispatches);
        Assert.True(runtime.Artifact.Disposed);
        Assert.Equal(PackageInstallStage.Resolving, Assert.Single(progress.Updates).Stage);
    }

    [Fact]
    public async Task ProgressCarriesExactActionAndAttemptAndRequiresDispatchAuthorization()
    {
        var runtime = new FakeRuntime();
        var service = new PilotPackageService(runtime);
        var request = Request();
        var resolving = new PackageInstallProgress(request.ActionId, Correlation, PackageInstallStage.Resolving);
        var installing = resolving with { Stage = PackageInstallStage.Installing };
        var progress = new RecordingProgress(update =>
        {
            if (update.Stage == PackageInstallStage.Installing)
            {
                Assert.Equal(0, runtime.Dispatches);
            }
        });

        var result = await service.InstallAsync(request, Identity.InstallerSha256, Correlation, _ =>
        {
            Assert.Equal(resolving, Assert.Single(progress.Updates));
            return Task.FromResult(true);
        }, progress, CancellationToken.None);

        Assert.Equal(PackageInstallStatus.Applied, result.Status);
        Assert.Equal(new[] { resolving, installing }, progress.Updates);
        Assert.False(runtime.ApplyToken.CanBeCanceled);
    }

    [Fact]
    public async Task CancellationAtTheLaunchBoundaryPreventsDispatch()
    {
        using var cancellation = new CancellationTokenSource();
        var releaseCancellationCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration registration = default;
        Task? cancellationRequest = null;
        var runtime = new FakeRuntime();
        var service = new PilotPackageService(runtime);
        var progress = new RecordingProgress(update =>
        {
            if (update.Stage == PackageInstallStage.Installing)
            {
                // Keep the linked token's asynchronous cancellation propagation pending at the launch boundary.
                registration = cancellation.Token.Register(() => releaseCancellationCallback.Task.GetAwaiter().GetResult());
                cancellationRequest = cancellation.CancelAsync();
            }
        });

        try
        {
            var result = await service.InstallAsync(Request(), Identity.InstallerSha256, Correlation,
                Authorize, progress, cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(PackageInstallStatus.CancelledBeforeDispatch, result.Status);
            await runtime.Artifact.Disposal.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, runtime.Dispatches);
        }
        finally
        {
            releaseCancellationCallback.TrySetResult();
            if (cancellationRequest is not null)
            {
                await cancellationRequest.WaitAsync(TimeSpan.FromSeconds(5));
            }

            await registration.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, PackageResolutionStatus.NetworkUnavailable, PackageInstallStatus.NetworkUnavailable)]
    [InlineData(HttpRequestError.ConnectionError, PackageResolutionStatus.NetworkUnavailable, PackageInstallStatus.NetworkUnavailable)]
    [InlineData(HttpRequestError.ResponseEnded, PackageResolutionStatus.NetworkUnavailable, PackageInstallStatus.NetworkUnavailable)]
    [InlineData(HttpRequestError.InvalidResponse, PackageResolutionStatus.DownloadUnavailable, PackageInstallStatus.DownloadUnavailable)]
    public async Task AcquisitionClassifiesSourceFailuresWithoutBackendText(HttpRequestError error,
        PackageResolutionStatus expectedResolution, PackageInstallStatus expectedInstall)
    {
        var runtime = new FakeRuntime { AcquisitionError = new HttpRequestException(error, UntrustedBackendDetail) };
        var service = new PilotPackageService(runtime);
        var progress = new RecordingProgress();

        var resolution = await service.ResolveAsync(CancellationToken.None);
        var result = await service.InstallAsync(Request(), Identity.InstallerSha256, Correlation, Authorize, progress,
            CancellationToken.None);

        Assert.Equal(new PackageInstallResult(expectedInstall), result);
        Assert.Equal(new PackageResolution(expectedResolution, new(PackageObservationStatus.Absent)), resolution);
        Assert.DoesNotContain("fixture-user", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-user", resolution.ToString(), StringComparison.Ordinal);
        Assert.Equal(PackageInstallStage.Resolving, Assert.Single(progress.Updates).Stage);
        Assert.Equal(0, runtime.Dispatches);
    }

    [Fact]
    public async Task StagingUnavailableAndInstallerFailureStayInTheirOwnPhases()
    {
        var stagingUnavailable = new FakeRuntime { AcquisitionError = new IOException(UntrustedBackendDetail) };
        var service = new PilotPackageService(stagingUnavailable);
        var resolution = await service.ResolveAsync(CancellationToken.None);
        var staging = await service.InstallAsync(Request(), Identity.InstallerSha256, Correlation, Authorize, null, CancellationToken.None);

        Assert.Equal(new PackageResolution(PackageResolutionStatus.DownloadUnavailable, new(PackageObservationStatus.Absent)), resolution);
        Assert.Equal(new PackageInstallResult(PackageInstallStatus.DownloadUnavailable), staging);
        Assert.Equal(0, stagingUnavailable.Dispatches);

        var installerUnavailable = new FakeRuntime { Apply = () => Task.FromException<PackageInstallResult>(new IOException(UntrustedBackendDetail)) };
        service = new PilotPackageService(installerUnavailable);
        var apply = await service.InstallAsync(Request(), Identity.InstallerSha256, Correlation, Authorize, null, CancellationToken.None);

        Assert.Equal(new PackageInstallResult(PackageInstallStatus.ReviewRequired), apply);
        Assert.Equal(1, installerUnavailable.Dispatches);
        Assert.True(installerUnavailable.Artifact.Disposed);
    }

    [Fact]
    public async Task PreparationDeadlineAndExplicitCancellationPreventDispatch()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new FakeRuntime
        {
            Acquire = async token =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new FakeArtifact();
            },
        };
        var service = new PilotPackageService(runtime, preparationTimeout: TimeSpan.Zero);

        Assert.Equal(PackageResolutionStatus.TimedOut,
            (await service.ResolveAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5))).Status);
        Assert.Equal(PackageInstallStatus.DownloadTimedOut,
            (await service.InstallAsync(Request(), Identity.InstallerSha256, Correlation, Authorize, null,
                CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5))).Status);

        entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        service = new PilotPackageService(runtime);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pending = service.InstallAsync(Request(), Identity.InstallerSha256, Correlation, Authorize, null, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();

        Assert.Equal(PackageInstallStatus.CancelledBeforeDispatch, (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Status);
        Assert.Equal(0, runtime.Dispatches);
    }

    [Theory]
    [InlineData(PackageInstallStatus.Applied)]
    [InlineData(PackageInstallStatus.AuthorizationDeclined)]
    [InlineData(PackageInstallStatus.ElevationUnavailable)]
    [InlineData(PackageInstallStatus.RestartRequired)]
    [InlineData(PackageInstallStatus.Cancelled)]
    [InlineData(PackageInstallStatus.Failed)]
    public async Task BackendResultAndInstalledObservationAreIndependent(PackageInstallStatus status)
    {
        var runtime = new FakeRuntime { Apply = () => Task.FromResult(new PackageInstallResult(status)) };
        var service = new PilotPackageService(runtime);

        var result = await service.InstallAsync(Request(), Identity.InstallerSha256, Correlation, Authorize, null, CancellationToken.None);

        Assert.Equal(status, result.Status);
        Assert.Equal(PackageObservationStatus.Absent, service.Inspect().Status);
        Assert.True(runtime.Artifact.Disposed);
    }

    [Theory]
    [InlineData(PackageObservationStatus.Absent, null, PackageVerificationStatus.PackageAbsent)]
    [InlineData(PackageObservationStatus.Installed, "8.9.9", PackageVerificationStatus.PackageMismatch)]
    [InlineData(PackageObservationStatus.Conflict, null, PackageVerificationStatus.PackageMismatch)]
    [InlineData(PackageObservationStatus.Unavailable, null, PackageVerificationStatus.PackageUnavailable)]
    public async Task IndependentRegistrationFailureDoesNotLaunch(PackageObservationStatus observation, string? version,
        PackageVerificationStatus expected)
    {
        var runtime = new FakeRuntime { Observation = new(observation, version) };
        var service = new PilotPackageService(runtime);

        Assert.Equal(expected, await service.VerifyInstalledAsync(Request(), Identity.InstallerSha256, CancellationToken.None));
        Assert.Equal(0, runtime.Probes);
        Assert.Equal(0, runtime.Dispatches);
    }

    [Theory]
    [InlineData(PackageVerificationStatus.Passed)]
    [InlineData(PackageVerificationStatus.ExecutableMissing)]
    [InlineData(PackageVerificationStatus.ExecutableUntrusted)]
    [InlineData(PackageVerificationStatus.LaunchFailed)]
    [InlineData(PackageVerificationStatus.ExitedEarly)]
    [InlineData(PackageVerificationStatus.TimedOut)]
    [InlineData(PackageVerificationStatus.Cancelled)]
    [InlineData(PackageVerificationStatus.CleanupUnconfirmed)]
    public async Task MatchingRegistrationRequiresTheProbeResult(PackageVerificationStatus expected)
    {
        var runtime = new FakeRuntime
        {
            Observation = new(PackageObservationStatus.Installed, Identity.ExactVersion),
            ProbeResult = expected,
        };
        var service = new PilotPackageService(runtime);

        Assert.Equal(expected, await service.VerifyInstalledAsync(Request(), Identity.InstallerSha256, CancellationToken.None));
        Assert.Equal(1, runtime.Probes);
        Assert.Equal(0, runtime.Dispatches);
    }

    [Fact]
    public async Task ChangedApprovalAndCancelledVerificationDoNotLaunch()
    {
        var runtime = new FakeRuntime { Observation = new(PackageObservationStatus.Installed, Identity.ExactVersion) };
        var service = new PilotPackageService(runtime);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Assert.Equal(PackageVerificationStatus.PackageMismatch,
            await service.VerifyInstalledAsync(Request(), new string('1', 64), CancellationToken.None));
        Assert.Equal(PackageVerificationStatus.Cancelled,
            await service.VerifyInstalledAsync(Request(), Identity.InstallerSha256, cancelled.Token));
        Assert.Equal(0, runtime.Probes);
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task VerifyPreviouslyInstalledReviewedPackageWithIsolatedLaunch()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var service = new PilotPackageService();
        var stageRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var stagesBefore = Directory.EnumerateDirectories(stageRoot, "ThirdLife-package-*").Count();
        var instancesBefore = CountEditorInstances();

        Assert.Equal(new PackageObservation(PackageObservationStatus.Installed, Identity.ExactVersion), service.Inspect());
        Assert.Equal(PackageVerificationStatus.Passed,
            await service.VerifyInstalledAsync(Request(), Identity.InstallerSha256, deadline.Token));
        Assert.Equal(new PackageObservation(PackageObservationStatus.Installed, Identity.ExactVersion), service.Inspect());
        Assert.Equal(stagesBefore, Directory.EnumerateDirectories(stageRoot, "ThirdLife-package-*").Count());
        Assert.Equal(instancesBefore, CountEditorInstances());
    }

    private static int CountEditorInstances()
    {
        var editors = Process.GetProcessesByName("notepad++");
        foreach (var editor in editors)
        {
            editor.Dispose();
        }

        return editors.Length;
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
        var pending = service.InstallAsync(Request(), Identity.InstallerSha256, Correlation, Authorize, null, cancellation.Token);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cancellation.CancelAsync();
            Assert.Equal(PackageInstallStatus.ReviewRequired, (await pending).Status);
            Assert.Equal(PackageInstallStatus.ReviewRequired,
                (await service.InstallAsync(Request(), Identity.InstallerSha256, Correlation, Authorize, null, CancellationToken.None)).Status);
            Assert.False(runtime.Artifact.Disposed);
            Assert.False(runtime.ApplyToken.CanBeCanceled);
        }
        finally
        {
            finished.SetResult(new(PackageInstallStatus.Failed));
            await runtime.Artifact.Disposal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimeoutKeepsTheArtifactAndCannotLaunchAfterExpiredAuthorization(bool installerStarted)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var approved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource<PackageInstallResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new FakeRuntime
        {
            Apply = () =>
            {
                entered.TrySetResult();
                return finished.Task;
            },
        };
        var service = new PilotPackageService(runtime, installTimeout: TimeSpan.FromSeconds(2));
        var progress = new RecordingProgress();
        var pending = service.InstallAsync(Request(), Identity.InstallerSha256, Correlation, _ =>
        {
            if (!installerStarted)
            {
                entered.TrySetResult();
            }

            return installerStarted ? Task.FromResult(true) : approved.Task;
        }, progress, CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(PackageInstallStatus.TimedOut, (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Status);
            Assert.False(runtime.Artifact.Disposed);
            Assert.Equal(PackageInstallStatus.ReviewRequired,
                (await service.InstallAsync(Request(), Identity.InstallerSha256, Correlation, Authorize, null,
                    CancellationToken.None)).Status);
        }
        finally
        {
            approved.TrySetResult(true);
            finished.TrySetResult(new(PackageInstallStatus.Applied));
            await runtime.Artifact.Disposal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(installerStarted ? 1 : 0, runtime.Dispatches);
        Assert.Equal(installerStarted ? 2 : 1, progress.Updates.Count);
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
        public int Probes { get; private set; }
        public CancellationToken ApplyToken { get; private set; }
        public FakeArtifact Artifact { get; } = new();
        public Exception? AcquisitionError { get; init; }
        public Action<FakeRuntime>? OnAcquisition { get; init; }
        public Func<CancellationToken, Task<IPackageArtifact>>? Acquire { get; init; }
        public Func<Task<PackageInstallResult>> Apply { get; init; } = () =>
            Task.FromResult(new PackageInstallResult(PackageInstallStatus.Applied));
        public PackageObservation Observation { get; set; } = new(PackageObservationStatus.Absent);
        public PackageVerificationStatus ProbeResult { get; init; } = PackageVerificationStatus.Passed;

        public PackageObservation Inspect() => Observation;

        public Task<PackageVerificationStatus> ProbeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Probes++;
            return Task.FromResult(ProbeResult);
        }

        public Task<IPackageArtifact> AcquireAsync(CancellationToken cancellationToken)
        {
            Acquisitions++;
            OnAcquisition?.Invoke(this);
            return Acquire?.Invoke(cancellationToken) ?? (AcquisitionError is { } error
                ? Task.FromException<IPackageArtifact>(error) : Task.FromResult<IPackageArtifact>(Artifact));
        }

        public Task<PackageInstallResult> ApplyAsync(IPackageArtifact artifact, CancellationToken cancellationToken)
        {
            Dispatches++;
            ApplyToken = cancellationToken;
            return Apply();
        }
    }

    private sealed class RecordingProgress(Action<PackageInstallProgress>? onReport = null) : IProgress<PackageInstallProgress>
    {
        public List<PackageInstallProgress> Updates { get; } = [];

        public void Report(PackageInstallProgress value)
        {
            Updates.Add(value);
            onReport?.Invoke(value);
        }
    }

    private sealed class FakeArtifact : IPackageArtifact
    {
        public TaskCompletionSource Disposal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed => Disposal.Task.IsCompleted;
        public void Dispose() => Disposal.SetResult();
    }
}
