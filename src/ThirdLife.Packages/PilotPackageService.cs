using ThirdLife.Actions.Contracts;

namespace ThirdLife.Packages;

/// <summary>Resolves, applies and observes the one reviewed package without the ThirdLife broker.</summary>
public sealed class PilotPackageService
{
    private readonly IPilotPackageRuntime runtime;
    private readonly TimeSpan preparationTimeout;
    private readonly TimeSpan installTimeout;
    private int installInProgress;

    public PilotPackageService() : this(new WindowsInstallerPackageRuntime())
    {
    }

    internal PilotPackageService(IPilotPackageRuntime runtime, TimeSpan? preparationTimeout = null, TimeSpan? installTimeout = null)
    {
        this.runtime = runtime;
        this.preparationTimeout = preparationTimeout ?? TimeSpan.FromMinutes(2);
        this.installTimeout = installTimeout ?? TimeSpan.FromMinutes(10);
    }

    public static PilotPackageIdentity Identity { get; } = new(
        "notepad-plus-plus-releases",
        "Notepad++.Notepad++",
        "8.9.9.1",
        "Notepad Plus Plus",
        "e3b0607a092ce2604fbdac027db2c94915995e8e568ebd226f8c5493c2f61a80");

    public PackageObservation Inspect() => runtime.Inspect();

    public async Task<PackageVerificationStatus> VerifyInstalledAsync(InstallPackageActionRequest request,
        string approvedInstallerSha256, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!MatchesApproval(request, approvedInstallerSha256))
        {
            return PackageVerificationStatus.PackageMismatch;
        }

        try
        {
            var observation = await Task.Run(Inspect, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return observation.Status switch
            {
                PackageObservationStatus.Installed when observation.ExactVersion == request.ExactVersion =>
                    await runtime.ProbeAsync(cancellationToken).ConfigureAwait(false),
                PackageObservationStatus.Absent => PackageVerificationStatus.PackageAbsent,
                PackageObservationStatus.Unavailable => PackageVerificationStatus.PackageUnavailable,
                _ => PackageVerificationStatus.PackageMismatch,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PackageVerificationStatus.Cancelled;
        }
    }

    public async Task<PackageResolution> ResolveAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new(PackageResolutionStatus.Cancelled, new(PackageObservationStatus.Unavailable));
        }

        var observation = Inspect();
        if (observation.Status != PackageObservationStatus.Absent)
        {
            return new(observation.Status switch
            {
                PackageObservationStatus.Installed => PackageResolutionStatus.AlreadyInstalled,
                PackageObservationStatus.Conflict => PackageResolutionStatus.Conflict,
                _ => PackageResolutionStatus.Unavailable,
            }, observation);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(preparationTimeout);
        try
        {
            using var artifact = await runtime.AcquireAsync(deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            return new(PackageResolutionStatus.Ready, observation);
        }
        catch (PackageTrustException)
        {
            return new(PackageResolutionStatus.TrustMismatch, observation);
        }
        catch (Exception error) when (error is OperationCanceledException or TimeoutException)
        {
            return new(cancellationToken.IsCancellationRequested
                ? PackageResolutionStatus.Cancelled : PackageResolutionStatus.TimedOut, observation);
        }
        catch (Exception error) when (PackageFailure.IsNetwork(error) || PackageFailure.IsUnavailable(error))
        {
            var status = cancellationToken.IsCancellationRequested ? PackageResolutionStatus.Cancelled :
                deadline.IsCancellationRequested ? PackageResolutionStatus.TimedOut :
                PackageFailure.IsNetwork(error) ? PackageResolutionStatus.NetworkUnavailable : PackageResolutionStatus.DownloadUnavailable;
            return new(status, observation);
        }
    }

    public async Task<PackageInstallResult> InstallAsync(
        InstallPackageActionRequest request,
        string approvedInstallerSha256,
        Guid correlationId,
        Func<CancellationToken, Task<bool>> authorizeDispatch,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authorizeDispatch);
        ArgumentOutOfRangeException.ThrowIfEqual(correlationId, Guid.Empty);
        if (!MatchesApproval(request, approvedInstallerSha256))
        {
            return new(PackageInstallStatus.TrustMismatch);
        }

        if (Interlocked.CompareExchange(ref installInProgress, 1, 0) != 0)
        {
            return new(PackageInstallStatus.ReviewRequired);
        }

        IPackageArtifact? artifact = null;
        var artifactTransferred = false;
        var dispatch = new DispatchBoundary();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GetInstallObservationResult() is { } existing)
            {
                return existing;
            }

            progress?.Report(new(request.ActionId, correlationId, PackageInstallStage.Resolving));
            using var preparationDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            preparationDeadline.CancelAfter(preparationTimeout);
            try
            {
                artifact = await runtime.AcquireAsync(preparationDeadline.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is OperationCanceledException or TimeoutException)
            {
                return new(cancellationToken.IsCancellationRequested
                    ? PackageInstallStatus.CancelledBeforeDispatch : PackageInstallStatus.DownloadTimedOut);
            }
            catch (Exception error) when (PackageFailure.IsNetwork(error) || PackageFailure.IsUnavailable(error))
            {
                var status = cancellationToken.IsCancellationRequested ? PackageInstallStatus.CancelledBeforeDispatch :
                    preparationDeadline.IsCancellationRequested ? PackageInstallStatus.DownloadTimedOut :
                    PackageFailure.IsNetwork(error) ? PackageInstallStatus.NetworkUnavailable : PackageInstallStatus.DownloadUnavailable;
                return new(status);
            }

            if (preparationDeadline.IsCancellationRequested)
            {
                return new(cancellationToken.IsCancellationRequested
                    ? PackageInstallStatus.CancelledBeforeDispatch : PackageInstallStatus.DownloadTimedOut);
            }

            if (GetInstallObservationResult() is { } changed)
            {
                return changed;
            }

            if (preparationDeadline.IsCancellationRequested)
            {
                return new(cancellationToken.IsCancellationRequested
                    ? PackageInstallStatus.CancelledBeforeDispatch : PackageInstallStatus.DownloadTimedOut);
            }

            using var installDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            installDeadline.CancelAfter(installTimeout);
            var operation = DispatchAsync(artifact, authorizeDispatch, dispatch,
                new(request.ActionId, correlationId, PackageInstallStage.Installing), progress, installDeadline.Token, cancellationToken);
            artifactTransferred = true;
            return await operation.WaitAsync(installDeadline.Token).ConfigureAwait(false);
        }
        catch (PackageTrustException)
        {
            return new(PackageInstallStatus.TrustMismatch);
        }
        catch (OperationCanceledException)
        {
            var preventedDispatch = dispatch.PreventStart();
            if (!cancellationToken.IsCancellationRequested)
            {
                return new(PackageInstallStatus.TimedOut);
            }

            return new(preventedDispatch ? PackageInstallStatus.CancelledBeforeDispatch : PackageInstallStatus.ReviewRequired);
        }
        catch (TimeoutException)
        {
            dispatch.PreventStart();
            return new(PackageInstallStatus.TimedOut);
        }
        catch (Exception error) when (PackageFailure.IsUnavailable(error))
        {
            return new(artifactTransferred ? PackageInstallStatus.ReviewRequired : PackageInstallStatus.Unavailable);
        }
        finally
        {
            if (!artifactTransferred)
            {
                artifact?.Dispose();
                Interlocked.Exchange(ref installInProgress, 0);
            }
        }
    }

    private PackageInstallResult? GetInstallObservationResult() => Inspect().Status switch
    {
        PackageObservationStatus.Absent => null,
        PackageObservationStatus.Installed => new(PackageInstallStatus.AlreadyInstalled),
        PackageObservationStatus.Conflict => new(PackageInstallStatus.Conflict),
        _ => new(PackageInstallStatus.Unavailable),
    };

    private async Task<PackageInstallResult> DispatchAsync(IPackageArtifact artifact,
        Func<CancellationToken, Task<bool>> authorizeDispatch, DispatchBoundary dispatch, PackageInstallProgress installing,
        IProgress<PackageInstallProgress>? progress, CancellationToken dispatchToken, CancellationToken cancellationToken)
    {
        try
        {
            using (artifact)
            {
                // Starting Windows Installer can wait for secure-desktop consent.
                // Transfer ownership of the read-locked artifact to this operation.
                return await Task.Run(async () =>
                {
                    if (!await authorizeDispatch(dispatchToken).ConfigureAwait(false))
                    {
                        return new PackageInstallResult(PackageInstallStatus.PreconditionChanged);
                    }

                    dispatchToken.ThrowIfCancellationRequested();
                    progress?.Report(installing);
                    // A cancellation accepted before the stage changed can still be propagating to the linked token.
                    cancellationToken.ThrowIfCancellationRequested();
                    dispatchToken.ThrowIfCancellationRequested();
                    if (!dispatch.TryStart())
                    {
                        throw new OperationCanceledException(dispatchToken);
                    }

                    // Cancelling observation never sends a termination request to Windows Installer.
                    return await runtime.ApplyAsync(artifact, CancellationToken.None).ConfigureAwait(false);
                }, dispatchToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (dispatch.Started && !dispatchToken.IsCancellationRequested)
        {
            return new(PackageInstallStatus.Cancelled);
        }
        catch (Exception error) when (PackageFailure.IsUnavailable(error))
        {
            return new(PackageInstallStatus.ReviewRequired);
        }
        finally
        {
            Interlocked.Exchange(ref installInProgress, 0);
        }
    }

    private sealed class DispatchBoundary
    {
        private const int Applying = 1;
        private const int Prevented = 2;
        private int state;

        public bool Started => Volatile.Read(ref state) == Applying;

        public bool TryStart() => Interlocked.CompareExchange(ref state, Applying, 0) == 0;

        public bool PreventStart() => Interlocked.CompareExchange(ref state, Prevented, 0) != Applying;
    }

    private static bool MatchesApproval(InstallPackageActionRequest request, string approvedInstallerSha256) =>
        request.SourceId == Identity.SourceId &&
        request.PackageId == Identity.PackageId &&
        request.ExactVersion == Identity.ExactVersion &&
        request.Publisher == Identity.Publisher &&
        request.Scope == ActionScope.Machine &&
        request.Architecture == ActionArchitecture.X64 &&
        approvedInstallerSha256 == Identity.InstallerSha256;
}

public sealed record PilotPackageIdentity(string SourceId, string PackageId, string ExactVersion,
    string Publisher, string InstallerSha256);

public sealed record PackageObservation(PackageObservationStatus Status, string? ExactVersion = null);

public enum PackageObservationStatus
{
    Absent,
    Installed,
    Conflict,
    Unavailable,
}

public enum PackageVerificationStatus
{
    Passed = 1,
    PackageAbsent,
    PackageMismatch,
    PackageUnavailable,
    ExecutableMissing,
    ExecutableUntrusted,
    LaunchFailed,
    ExitedEarly,
    TimedOut,
    Cancelled,
    CleanupUnconfirmed,
}

public sealed record PackageResolution(PackageResolutionStatus Status, PackageObservation Observation);

public enum PackageResolutionStatus
{
    Ready,
    AlreadyInstalled,
    Conflict,
    Unavailable,
    TrustMismatch,
    Cancelled,
    TimedOut,
    NetworkUnavailable,
    DownloadUnavailable,
}

public sealed record PackageInstallResult(PackageInstallStatus Status);

public enum PackageInstallStatus
{
    Applied,
    RestartRequired,
    AlreadyInstalled,
    Conflict,
    Unavailable,
    TrustMismatch,
    AuthorizationDeclined,
    PreconditionChanged,
    Failed,
    Cancelled,
    TimedOut,
    ReviewRequired,
    CancelledBeforeDispatch,
    NetworkUnavailable,
    DownloadUnavailable,
    DownloadTimedOut,
    ElevationUnavailable,
}

public sealed record PackageInstallProgress(Guid ActionId, Guid CorrelationId, PackageInstallStage Stage);

public enum PackageInstallStage
{
    Resolving,
    Installing,
}

internal interface IPilotPackageRuntime
{
    PackageObservation Inspect();
    Task<IPackageArtifact> AcquireAsync(CancellationToken cancellationToken);
    Task<PackageInstallResult> ApplyAsync(IPackageArtifact artifact, CancellationToken cancellationToken);
    Task<PackageVerificationStatus> ProbeAsync(CancellationToken cancellationToken);
}

internal interface IPackageArtifact : IDisposable;

internal sealed class PackageTrustException(string message = "Package trust validation failed.") : Exception(message);

internal sealed class PackageNetworkException() : Exception("The package download connection was interrupted.");

internal static class PackageFailure
{
    public static bool IsNetwork(Exception error) => error is PackageNetworkException or HttpRequestException
    {
        HttpRequestError: HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError or HttpRequestError.ResponseEnded,
    };

    public static bool IsUnavailable(Exception error) =>
        error is IOException or UnauthorizedAccessException or System.Security.SecurityException or HttpRequestException
            or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException;
}
