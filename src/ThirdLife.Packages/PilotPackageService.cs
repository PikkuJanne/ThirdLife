using ThirdLife.Actions.Contracts;

namespace ThirdLife.Packages;

/// <summary>Resolves, applies and observes the one reviewed package without the ThirdLife broker.</summary>
public sealed class PilotPackageService
{
    private static readonly TimeSpan PreparationTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(10);
    private readonly IPilotPackageRuntime runtime;
    private int installInProgress;

    public PilotPackageService() : this(new WindowsInstallerPackageRuntime())
    {
    }

    internal PilotPackageService(IPilotPackageRuntime runtime)
    {
        this.runtime = runtime;
    }

    public static PilotPackageIdentity Identity { get; } = new(
        "notepad-plus-plus-releases",
        "Notepad++.Notepad++",
        "8.9.9.1",
        "Notepad Plus Plus",
        "e3b0607a092ce2604fbdac027db2c94915995e8e568ebd226f8c5493c2f61a80");

    public PackageObservation Inspect() => runtime.Inspect();

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
        deadline.CancelAfter(PreparationTimeout);
        try
        {
            using var artifact = await runtime.AcquireAsync(deadline.Token).ConfigureAwait(false);
            return new(PackageResolutionStatus.Ready, observation);
        }
        catch (PackageTrustException)
        {
            return new(PackageResolutionStatus.TrustMismatch, observation);
        }
        catch (OperationCanceledException)
        {
            return new(cancellationToken.IsCancellationRequested
                ? PackageResolutionStatus.Cancelled : PackageResolutionStatus.TimedOut, observation);
        }
        catch (Exception error) when (PackageFailure.IsUnavailable(error))
        {
            return new(PackageResolutionStatus.Unavailable, observation);
        }
    }

    public async Task<PackageInstallResult> InstallAsync(
        InstallPackageActionRequest request,
        string approvedInstallerSha256,
        Func<CancellationToken, Task<bool>> authorizeDispatch,
        IProgress<PackageInstallStage>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authorizeDispatch);
        if (!MatchesApproval(request, approvedInstallerSha256))
        {
            return new(PackageInstallStatus.TrustMismatch);
        }

        if (Interlocked.CompareExchange(ref installInProgress, 1, 0) != 0)
        {
            return new(PackageInstallStatus.ReviewRequired);
        }

        IPackageArtifact? artifact = null;
        var dispatched = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GetInstallObservationResult() is { } existing)
            {
                return existing;
            }

            progress?.Report(PackageInstallStage.Resolving);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(PreparationTimeout);
            artifact = await runtime.AcquireAsync(deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();

            if (GetInstallObservationResult() is { } changed)
            {
                return changed;
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(PackageInstallStage.Installing);
            var operation = DispatchAsync(artifact, authorizeDispatch, cancellationToken);
            dispatched = true;
            return await operation.WaitAsync(InstallTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (PackageTrustException)
        {
            return new(PackageInstallStatus.TrustMismatch);
        }
        catch (OperationCanceledException)
        {
            return new(dispatched ? PackageInstallStatus.ReviewRequired : cancellationToken.IsCancellationRequested
                ? PackageInstallStatus.Cancelled : PackageInstallStatus.TimedOut);
        }
        catch (TimeoutException)
        {
            return new(dispatched ? PackageInstallStatus.ReviewRequired : PackageInstallStatus.TimedOut);
        }
        catch (Exception error) when (PackageFailure.IsUnavailable(error))
        {
            return new(dispatched ? PackageInstallStatus.ReviewRequired : PackageInstallStatus.Unavailable);
        }
        finally
        {
            if (!dispatched)
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
        Func<CancellationToken, Task<bool>> authorizeDispatch, CancellationToken cancellationToken)
    {
        try
        {
            using (artifact)
            {
                // Starting Windows Installer can wait for secure-desktop consent.
                // Transfer ownership of the read-locked artifact to this operation.
                return await Task.Run(async () =>
                {
                    if (!await authorizeDispatch(cancellationToken).ConfigureAwait(false))
                    {
                        return new PackageInstallResult(PackageInstallStatus.PreconditionChanged);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    return await runtime.ApplyAsync(artifact, cancellationToken).ConfigureAwait(false);
                }, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
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
}

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
}

internal interface IPackageArtifact : IDisposable;

internal sealed class PackageTrustException(string message = "Package trust validation failed.") : Exception(message);

internal static class PackageFailure
{
    public static bool IsUnavailable(Exception error) =>
        error is IOException or UnauthorizedAccessException or System.Security.SecurityException or HttpRequestException
            or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException;
}
