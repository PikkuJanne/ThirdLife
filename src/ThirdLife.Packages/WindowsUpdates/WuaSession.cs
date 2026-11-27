using System.Runtime.InteropServices;
using ThirdLife.Core.WindowsUpdates;

namespace ThirdLife.Packages.WindowsUpdates;

// Fixed local Windows Update Agent automation interfaces from wuapi.idl. Free-text
// metadata, service URLs, history, credentials and installer arguments never leave WUA.
internal static class WuaSession
{
    private static readonly Guid SessionClass = new("4cb43d7f-7eee-4906-8698-60da1c38f2fe");
    private static readonly Guid SystemInfoClass = new("c01b9ba0-bea7-41ba-b604-d0a36f469133");
    private static readonly Guid CollectionClass = new("13639463-00db-4646-803d-528026140d88");
    private static readonly Guid WindowsUpdateService = new("9482f4b4-e343-43b6-b170-9a65bc822c77");
    internal const string CbsHandler = "http://schemas.microsoft.com/msus/2002/12/UpdateHandlers/Cbs";
    internal const string Applicable = "IsInstalled=0 and IsHidden=0 and DeploymentAction='Installation'";
    private const int MaxCategories = 32;

    internal static WindowsUpdateScan Scan(CancellationToken token)
    {
        using var scope = new ComScope();
        dynamic session = NewSession(scope);
        dynamic result = Search(scope, session, Applicable, token);
        CheckSearch(scope, result);
        dynamic updates = scope.Own(result.Updates);
        int count = updates.Count;
        if (count is < 0 or > WindowsUpdatePolicy.MaximumUpdates) { throw new UpdateFailure(WindowsUpdateStatus.LimitExceeded); }

        var inventory = new List<WindowsUpdateCandidate>(count);
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            using var itemScope = new ComScope();
            dynamic update = itemScope.Own(updates[i]);
            inventory.Add(ReadCandidate(itemScope, update));
        }

        dynamic system = scope.Create(SystemInfoClass);
        return new(WindowsUpdateStatus.Succeeded, inventory, (bool)system.RebootRequired);
    }

    internal static WindowsUpdateOperation Apply(WindowsUpdateCandidate exact, bool install, CancellationToken token,
        Func<Guid, object>? activate = null)
    {
        if (exact.Classification is not (WindowsUpdateClass.Security or WindowsUpdateClass.Critical) ||
            exact.MaximumDownloadBytes is null)
        {
            return new(WindowsUpdateStatus.ApprovalChanged, null);
        }

        using var scope = new ComScope(activate);
        dynamic session = NewSession(scope);
        if (!HasWindowsUpdateAuthority(scope, session)) { return new(WindowsUpdateStatus.Unavailable, null); }
        // Only strongly typed GUID and revision can reach this fixed criteria template.
        string criteria = $"{Applicable} and {IdentityCriteria(exact.Identity)}";
        dynamic result = Search(scope, session, criteria, token);
        CheckSearch(scope, result);
        dynamic updates = scope.Own(result.Updates);
        if ((int)updates.Count != 1) { return new(WindowsUpdateStatus.ApprovalChanged, null); }
        dynamic update = scope.Own(updates[0]);
        if (ReadCandidate(scope, update) != exact) { return new(WindowsUpdateStatus.ApprovalChanged, null); }
        dynamic system = scope.Create(SystemInfoClass);
        if ((bool)system.RebootRequired) { return new(WindowsUpdateStatus.Unavailable, true); }
        if (install && !(bool)update.IsDownloaded) { return new(WindowsUpdateStatus.Unavailable, false); }

        dynamic collection = scope.Create(CollectionClass);
        collection.Add(update);
        dynamic actor = scope.Own(install ? session.CreateUpdateInstaller() : session.CreateUpdateDownloader());
        actor.Updates = collection;
        actor.IsForced = false;
        if (install)
        {
            if ((bool)actor.IsBusy) { return new(WindowsUpdateStatus.Unavailable, null); }
            if ((bool)actor.RebootRequiredBeforeInstallation) { return new(WindowsUpdateStatus.Unavailable, true); }
            actor.AllowSourcePrompts = false;
            actor.ForceQuiet = true;
        }

        // Repeat the service check across the online lookup and the preparation window.
        if (!HasWindowsUpdateAuthority(scope, session)) { return new(WindowsUpdateStatus.Unavailable, null); }
        token.ThrowIfCancellationRequested();
        var completed = new WuaNotification();
        var progress = new WuaNotification();
        dynamic job = scope.Own(install ? actor.BeginInstall(progress, completed, null) : actor.BeginDownload(progress, completed, null));
        try
        {
            Wait(job, completed, token);
            dynamic finalProgress = scope.Own(job.GetProgress());
            int percent = finalProgress.PercentComplete;
            dynamic operation = scope.Own(install ? actor.EndInstall(job) : actor.EndDownload(job));
            dynamic perUpdate = scope.Own(operation.GetUpdateResult(0));
            WindowsUpdateStatus status = OperationStatus((int)operation.ResultCode, (int)operation.HResult,
                (int)perUpdate.ResultCode, (int)perUpdate.HResult);
            bool reboot = (bool)system.RebootRequired || install && ((bool)operation.RebootRequired || (bool)perUpdate.RebootRequired);
            return new(status, reboot, percent is >= 0 and <= 100 ? percent : null);
        }
        finally { job.CleanUp(); }
    }

    internal static WindowsUpdateInstalledObservation ObserveInstalled(WindowsUpdateIdentity exact, CancellationToken token,
        Func<Guid, object>? activate = null)
    {
        using var scope = new ComScope(activate);
        dynamic session = NewSession(scope);
        if (!HasWindowsUpdateAuthority(scope, session)) { return new(WindowsUpdateStatus.Unavailable, exact, null); }
        dynamic result = Search(scope, session, $"IsInstalled=1 and {IdentityCriteria(exact)}", token);
        CheckSearch(scope, result);
        dynamic updates = scope.Own(result.Updates);
        int count = updates.Count;
        if (!HasWindowsUpdateAuthority(scope, session)) { return new(WindowsUpdateStatus.Unavailable, exact, null); }
        // WUA may cease to expose an old revision. Its absence is never installation evidence.
        if (count == 0) { return new(WindowsUpdateStatus.Succeeded, exact, null); }
        if (count != 1) { return new(WindowsUpdateStatus.Partial, exact, null); }
        dynamic update = scope.Own(updates[0]);
        if (ReadIdentity(scope, update) != exact || !(bool)update.IsInstalled)
        {
            return new(WindowsUpdateStatus.Partial, exact, null);
        }
        return new(WindowsUpdateStatus.Succeeded, exact, true);
    }

    private static string IdentityCriteria(WindowsUpdateIdentity exact) =>
        FormattableString.Invariant($"UpdateID='{exact.UpdateId:D}' and RevisionNumber={exact.Revision}");

    private static dynamic NewSession(ComScope scope)
    {
        dynamic session = scope.Create(SessionClass);
        session.ClientApplicationID = "ThirdLife Setup Core";
        return session;
    }

    // The WUA default may resolve to WSUS or a different service, including mixed
    // per-category routing. Reject managed services and ambiguous AU registrations.
    // Never set ServerSelection/ServiceID to bypass the machine's configured policy.
    private static bool HasWindowsUpdateAuthority(ComScope scope, dynamic session)
    {
        dynamic manager = scope.Own(session.CreateUpdateServiceManager());
        dynamic services = scope.Own(manager.Services);
        int count = services.Count;
        if (count is < 1 or > 16) { return false; }
        var found = false;
        for (var i = 0; i < count; i++)
        {
            dynamic service = scope.Own(services[i]);
            if ((bool)service.IsManaged) { return false; }
            bool isDefault = service.IsDefaultAUService;
            bool registered = service.IsRegisteredWithAU;
            if (!isDefault && !registered) { continue; }
            if (found || !isDefault || !registered || (bool)service.IsScanPackageService ||
                !Guid.TryParseExact((string)service.ServiceID, "D", out Guid id) || id != WindowsUpdateService)
            {
                return false;
            }
            found = true;
        }
        return found;
    }

    private static dynamic Search(ComScope scope, dynamic session, string criteria, CancellationToken token)
    {
        dynamic searcher = scope.Own(session.CreateUpdateSearcher());
        searcher.CanAutomaticallyUpgradeService = false;
        searcher.Online = true;
        // Preserve the machine's configured update service, including managed policy.
        token.ThrowIfCancellationRequested();
        var completed = new WuaNotification();
        dynamic job = scope.Own(searcher.BeginSearch(criteria, completed, null));
        try
        {
            Wait(job, completed, token);
            return scope.Own(searcher.EndSearch(job));
        }
        finally { job.CleanUp(); }
    }

    private static void Wait(dynamic job, WuaNotification completed, CancellationToken token)
    {
        try { completed.Wait(token); }
        catch (OperationCanceledException)
        {
            job.RequestAbort();
            throw;
        }
    }

    private static void CheckSearch(ComScope scope, dynamic result)
    {
        WindowsUpdateStatus status = ResultCode((int)result.ResultCode);
        dynamic warnings = scope.Own(result.Warnings);
        int count = warnings.Count;
        if (count is < 0 or > 8) { throw new UpdateFailure(WindowsUpdateStatus.LimitExceeded); }
        for (var i = 0; i < count; i++)
        {
            dynamic warning = scope.Own(warnings[i]);
            var reason = HResultStatus((int)warning.HResult);
            if (reason != WindowsUpdateStatus.Failed) { throw new UpdateFailure(reason); }
        }
        if (status != WindowsUpdateStatus.Succeeded) { throw new UpdateFailure(status); }
        if (count != 0) { throw new UpdateFailure(WindowsUpdateStatus.Partial); }
    }

    private static WindowsUpdateIdentity ReadIdentity(ComScope scope, dynamic update)
    {
        dynamic identity = scope.Own(update.Identity);
        if (!Guid.TryParseExact((string)identity.UpdateID, "D", out Guid id)) { throw new InvalidDataException(); }
        return new WindowsUpdateIdentity(id, (int)identity.RevisionNumber);
    }

    internal static WindowsUpdateCandidate ReadCandidate(ComScope scope, dynamic update)
    {
        WindowsUpdateIdentity key = ReadIdentity(scope, update);
        decimal bytes = (decimal)update.MaxDownloadSize;
        long? maximum = bytes >= 0 && bytes <= long.MaxValue && bytes == decimal.Truncate(bytes) ? (long)bytes : null;
        int type = update.Type;
        if (type == 2) { return new(key, WindowsUpdateClass.Driver, maximum); }
        if (type != 1 || maximum is null || (string?)update.HandlerID != CbsHandler ||
            (bool)update.IsBeta || (bool)update.CanRequireSource ||
            !(bool)update.EulaAccepted || (bool)update.BrowseOnly || (bool)update.IsMandatory ||
            (bool)update.IsInstalled || (bool)update.IsHidden || (int)update.DeploymentAction != 1)
        {
            return new(key, WindowsUpdateClass.Restricted, maximum);
        }

        dynamic? bundle = scope.OwnOptional(update.BundledUpdates);
        dynamic? behavior = scope.OwnOptional(update.InstallationBehavior);
        if (bundle is not null && (int)bundle.Count != 0 || behavior is null || (bool)behavior.CanRequestUserInput)
        {
            return new(key, WindowsUpdateClass.Restricted, maximum);
        }

        dynamic categories = scope.Own(update.Categories);
        int count = categories.Count;
        if (count is < 0 or > MaxCategories) { return new(key, WindowsUpdateClass.Restricted, maximum); }
        var classification = WindowsUpdateClass.Other;
        var found = false;
        for (var i = 0; i < count; i++)
        {
            dynamic category = scope.Own(categories[i]);
            if ((string)category.Type != "UpdateClassification") { continue; }
            if (found) { return new(key, WindowsUpdateClass.Restricted, maximum); }
            found = true;
            if (!Guid.TryParseExact((string)category.CategoryID, "D", out Guid guid))
            {
                return new(key, WindowsUpdateClass.Restricted, maximum);
            }
            classification = guid.ToString("D") switch
            {
                "0fa1201d-4330-4fa8-8ae9-b877473b6441" => WindowsUpdateClass.Security,
                "e6cf1350-c01b-414d-a61f-263d14d133b4" => WindowsUpdateClass.Critical,
                _ => WindowsUpdateClass.Other,
            };
        }
        return new(key, classification, maximum);
    }

    internal static WindowsUpdateStatus OperationStatus(int overall, int overallHResult, int item, int itemHResult)
    {
        if (itemHResult < 0) { return HResultStatus(itemHResult); }
        if (overallHResult < 0) { return HResultStatus(overallHResult); }
        if (overallHResult > 0 || itemHResult > 0) { return WindowsUpdateStatus.Partial; }
        var status = ResultCode(overall);
        return status == WindowsUpdateStatus.Succeeded ? ResultCode(item) : status;
    }

    private static WindowsUpdateStatus ResultCode(int code) => code switch
    {
        2 => WindowsUpdateStatus.Succeeded,
        3 => WindowsUpdateStatus.Partial,
        5 => WindowsUpdateStatus.Cancelled,
        _ => WindowsUpdateStatus.Failed,
    };

    internal static WindowsUpdateStatus Failure(Exception error) => error switch
    {
        UpdateFailure failure => failure.Status,
        UnauthorizedAccessException => WindowsUpdateStatus.AccessDenied,
        COMException com => HResultStatus(com.HResult),
        _ => WindowsUpdateStatus.Failed,
    };

    private static WindowsUpdateStatus HResultStatus(int code) => unchecked((uint)code) switch
    {
        0x80070005 or 0x80240044 => WindowsUpdateStatus.AccessDenied,
        0x80070422 or 0x8024001e or 0x8024002e or 0x8024a000 => WindowsUpdateStatus.ServiceUnavailable,
        0x80072ee7 or 0x80072efd or 0x8024001f or 0x8024402c => WindowsUpdateStatus.Offline,
        0x80072ee2 => WindowsUpdateStatus.TimedOut,
        0x800704c7 or 0x8024000b => WindowsUpdateStatus.Cancelled,
        _ => WindowsUpdateStatus.Failed,
    };

    private sealed class UpdateFailure(WindowsUpdateStatus status) : Exception
    {
        internal WindowsUpdateStatus Status { get; } = status;
    }
}

internal sealed class ComScope(Func<Guid, object>? activate = null) : IDisposable
{
    private readonly List<object> owned = [];

    public dynamic Create(Guid classId) => Own(activate is null ?
        Activator.CreateInstance(Type.GetTypeFromCLSID(classId, throwOnError: true)!)! : activate(classId));

    public dynamic Own(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Marshal.IsComObject(value)) { owned.Add(value); }
        return value;
    }

    public dynamic? OwnOptional(object? value) => value is null ? null : Own(value);

    public void Dispose()
    {
        for (var i = owned.Count - 1; i >= 0; i--) { _ = Marshal.ReleaseComObject(owned[i]); }
        owned.Clear();
    }
}

// WUA accepts an Automation notification with DISPID 0 for all three job kinds.
[ComVisible(true), Guid("94ee2cc3-bbae-4f04-a8b3-797a4cea89ab"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
public interface IWuaNotification
{
    [DispId(0)]
    void Invoke(object job, object arguments);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None), ComDefaultInterface(typeof(IWuaNotification))]
public sealed class WuaNotification : IWuaNotification
{
    private readonly TaskCompletionSource signal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal WuaNotification() { }
    public void Invoke(object job, object arguments) => signal.TrySetResult();
    internal void Wait(CancellationToken token) => signal.Task.WaitAsync(token).GetAwaiter().GetResult();
}
