using System.Runtime.InteropServices;
using ThirdLife.Actions.Contracts;
using ThirdLife.Broker.Protocol;
using ThirdLife.Broker.Protocol.Transport;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Inventory.Windows;
using ThirdLife.Packages.WindowsUpdates;

namespace ThirdLife.Broker.WindowsUpdates;

internal sealed class NativeWindowsUpdateBackend : IWindowsUpdateBrokerBackend
{
    private static readonly Guid SessionClass = new("4cb43d7f-7eee-4906-8698-60da1c38f2fe");
    private static readonly Guid SessionInterface = new("816858a4-260d-4260-933a-2585f1abc76b");
    private static readonly Guid SystemInfoClass = new("c01b9ba0-bea7-41ba-b604-d0a36f469133");
    private static readonly Guid SystemInfoInterface = new("ade87bf7-7b56-4275-8fab-b9b0e591844b");
    private static readonly Guid CollectionClass = new("13639463-00db-4646-803d-528026140d88");
    private static readonly Guid CollectionInterface = new("07f7438c-7709-4ca5-b518-91279288134e");
    private static readonly Guid Installer2 = new("3442d4fe-224d-4cee-98cf-30e0c4d229e6");
    private int _active;

    public async Task<WindowsUpdateOperation> ApplyAsync(WindowsUpdateActionRequest action,
        Func<WindowsUpdatePreparation, bool> authorizeBegin, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(authorizeBegin);
        if (token.IsCancellationRequested) { return new(WindowsUpdateStatus.Cancelled, null); }
        if (action.Kind is not (ActionKind.DownloadWindowsUpdate or ActionKind.InstallWindowsUpdate) ||
            action.Exact is not { } exact || !WindowsUpdatePolicy.IsSelectable(exact))
        {
            return new(WindowsUpdateStatus.ApprovalChanged, null);
        }
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            Interlocked.CompareExchange(ref _active, 1, 0) != 0)
        {
            return new(WindowsUpdateStatus.Unavailable, null);
        }

        // Keep a hung COM call off the protocol thread. The worker retains its one slot and
        // releases the apartment and callbacks itself, even if the broker stops waiting.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(BrokerProtocolLimits.MaximumLifetime); // bound lookup by the short admission window
        var operationToken = deadline.Token;
        var beginning = 0;
        var worker = Task.Run(Run);
        try { return await worker.WaitAsync(operationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return Interrupted(); }

        WindowsUpdateOperation Interrupted() => new(Volatile.Read(ref beginning) != 0 ? WindowsUpdateStatus.Partial :
            token.IsCancellationRequested ? WindowsUpdateStatus.Cancelled : WindowsUpdateStatus.TimedOut, null);

        WindowsUpdateOperation Run()
        {
            try
            {
                operationToken.ThrowIfCancellationRequested();
                if (WindowsPlatform.CheckCurrent() != WindowsPlatformEligibility.Supported) { return Unavailable(); }
                using var apartment = WuaApartment.Open();
                return Apply(exact, action.Kind == ActionKind.InstallWindowsUpdate, Authorize, operationToken);
            }
            catch (OperationCanceledException) { return Interrupted(); }
            catch (Exception ex) when (Failure(ex, Volatile.Read(ref beginning) != 0) is { } outcome)
            {
                return new(outcome, null);
            }
            finally { Volatile.Write(ref _active, 0); }
        }

        bool Authorize(WindowsUpdatePreparation observed)
        {
            operationToken.ThrowIfCancellationRequested();
            if (!WindowsBrokerPeer.CurrentIdentity().IsElevated || !authorizeBegin(observed)) { return false; }
            Volatile.Write(ref beginning, 1);
            deadline.CancelAfter(BrokerProtocolLimits.MaximumUpdateOperationLifetime);
            return true;
        }
    }

    private static WindowsUpdateOperation Apply(WindowsUpdateCandidate exact, bool install,
        Func<WindowsUpdatePreparation, bool> authorizeBegin, CancellationToken token)
    {
        using var session = WuaInterface.Activate(SessionClass, SessionInterface);
        session.SetClientId();
        if (!WuaServiceAuthority.IsBuiltInForSession(session.Pointer)) { return Unavailable(); }
        using var update = WuaExactOffer.Find(session, exact, token);
        if (!WuaServiceAuthority.IsBuiltInForSession(session.Pointer)) { return Unavailable(); }
        if (update is null || !WuaExactOffer.Matches(update, exact)) { return new(WindowsUpdateStatus.ApprovalChanged, null); }

        using var system = WuaInterface.Activate(SystemInfoClass, SystemInfoInterface);
        using var collection = WuaInterface.Activate(CollectionClass, CollectionInterface);
        collection.AddSingleUpdate(update);
        using var actor = session.Object(install ? 14 : 13); // CreateUpdateInstaller / CreateUpdateDownloader
        actor.SetObject(install ? 16 : 14, collection); // Updates
        if (install)
        {
            if (!update.Flag(23) || actor.Flag(23) || actor.Flag(27)) { return Unavailable(); } // downloaded, busy, reboot before install
        }
        ConfigureOperation(actor, install);
        var completed = new WuaCompleted();
        using var complete = WuaCallback.ForCompletion(install, completed);
        using var progress = WuaCallback.ForProgress(install, new WuaProgress());
        var now = DateTimeOffset.UtcNow;
        var resources = WindowsPreparationResources.ReadForWindowsUpdate(now, token);
        bool? power = resources.Power switch
        {
            WindowsPreparationPower.ExternalPower or WindowsPreparationPower.NoBattery => true,
            WindowsPreparationPower.OnBattery or WindowsPreparationPower.CriticallyLowBattery => false,
            _ => null,
        };
        var recommendationMb = ReadOptional(() => update.Number(36)); // RecommendedHardDiskSpace (zero means no WUA recommendation)
        var observation = new WindowsUpdatePreparation(exact, WindowsUpdateStatus.Succeeded, now,
            system.Flag(8), resources.ServicingSupported, power, resources.SystemAvailableBytes, resources.CacheAvailableBytes,
            resources.CacheSharesSystemVolume, recommendationMb > 0 ? recommendationMb * 1_048_576L : null,
            WindowsUpdateEligibilityReason.Eligible, update.Flag(28), resources.Target);
        token.ThrowIfCancellationRequested();
        if (observation.RebootRequired != false || !WuaServiceAuthority.IsBuiltInForSession(session.Pointer) ||
            !authorizeBegin(observation))
        {
            return Unavailable();
        }
        // No other update lookup, collection change, download or install path exists after authorization.
        token.ThrowIfCancellationRequested();
        using var job = actor.BeginOperation(install, progress.Pointer, complete.Pointer);
        try
        {
            completed.Wait(job, 12, token); // RequestAbort
            var percent = ReadOptional(() =>
            {
                using var finalProgress = job.Object(11);
                return finalProgress.Number(install ? 9 : 10);
            });
            using var result = actor.End(install ? 19 : 17, job);
            using var item = result.Item(install ? 10 : 9, 0); // GetUpdateResult
            int code = install ? 9 : 8; // ResultCode
            var outcome = Outcome(result.Number(code), result.Number(7), item.Number(code), item.Number(7));
            var reboot = system.Flag(8) || install && (result.Flag(8) || item.Flag(8));
            return new(outcome, reboot, percent is >= 0 and <= 100 ? percent : null);
        }
        finally { job.Call(10); } // CleanUp
    }

    internal static void ConfigureOperation(WuaInterface actor, bool install)
    {
        actor.SetFlag(10, false); // IsForced
        if (!install) { return; }
        using var installer = actor.Query(Installer2);
        installer.SetFlag(26, false); // AllowSourcePrompts
        installer.SetFlag(29, true); // ForceQuiet
    }

    internal static WindowsUpdateStatus Outcome(int overallCode, int overallHResult, int itemCode, int itemHResult)
    {
        if (overallCode == 2 && itemCode == 2 && overallHResult == 0 && itemHResult == 0) { return WindowsUpdateStatus.Succeeded; }
        if (overallCode == 4 && itemCode == 4) { return WindowsUpdateStatus.Failed; }
        if (overallCode == 5 && itemCode == 5) { return WindowsUpdateStatus.Cancelled; }
        return WindowsUpdateStatus.Partial;
    }

    internal static int? ReadOptional(Func<int> read)
    {
        try { return read(); }
        catch (Exception error) when (error is COMException or InvalidCastException or InvalidDataException or
            UnauthorizedAccessException or NotSupportedException or NotImplementedException)
        { return null; }
    }

    internal static WindowsUpdateStatus? Failure(Exception error, bool beginning)
    {
        WindowsUpdateStatus? status = error switch
        {
            UnauthorizedAccessException => WindowsUpdateStatus.AccessDenied,
            COMException com => WuaFailure.FromHResult(com.HResult),
            InvalidDataException or ExternalException or IOException or InvalidOperationException or ArgumentException or
                InvalidCastException or NotSupportedException or NotImplementedException or OverflowException => WindowsUpdateStatus.Unavailable,
            _ => null,
        };
        return beginning && status.HasValue ? WindowsUpdateStatus.Partial : status;
    }

    private static WindowsUpdateOperation Unavailable() => new(WindowsUpdateStatus.Unavailable, null);
}

internal sealed class WuaApartment : IDisposable
{
    private WuaApartment() { }
    internal static WuaApartment Open()
    {
        var status = CoInitializeEx(0, 0); // MTA, including an existing compatible apartment (S_FALSE)
        if (status < 0) { Marshal.ThrowExceptionForHR(status, new nint(-1)); }
        return new();
    }

    public void Dispose() => CoUninitialize();

    [DllImport("ole32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CoInitializeEx(nint reserved, uint flags);

    [DllImport("ole32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void CoUninitialize();
}
