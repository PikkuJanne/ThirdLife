using ThirdLife.Core.WindowsUpdates;

namespace ThirdLife.Broker.WindowsUpdates;

internal static class WuaExactOffer
{
    private static readonly Guid Update3 = new("112eda6b-95b3-476f-9d90-aee82c6b8181");
    private static readonly Guid Security = new("0fa1201d-4330-4fa8-8ae9-b877473b6441");
    private static readonly Guid Critical = new("e6cf1350-c01b-414d-a61f-263d14d133b4");
    private const string Cbs = "http://schemas.microsoft.com/msus/2002/12/UpdateHandlers/Cbs";

    internal static WuaInterface? Find(WuaInterface session, WindowsUpdateCandidate exact, CancellationToken token)
    {
        // The machine keeps its configured server selection. The only input to this criterion is typed.
        using var searcher = session.Object(12); // IUpdateSession::CreateUpdateSearcher
        searcher.SetFlag(8, false); // CanAutomaticallyUpgradeService
        searcher.SetFlag(21, true); // Online
        var criteria = FormattableString.Invariant($"IsInstalled=0 and IsHidden=0 and DeploymentAction='Installation' and UpdateID='{exact.Identity.UpdateId:D}' and RevisionNumber={exact.Identity.Revision}");
        var completed = new WuaCompleted();
        using var callback = new WuaCallback<ISearchCompleted>(completed);
        token.ThrowIfCancellationRequested();
        using var job = searcher.BeginSearch(criteria, callback.Pointer);
        WuaInterface? selected = null;
        try
        {
            try
            {
                completed.Wait(job, 10, token); // ISearchJob::RequestAbort
                using var result = searcher.End(16, job);
                using var warnings = result.Object(10);
                if (result.Number(7) != 2 || warnings.Number(9) != 0) { throw new InvalidDataException(); }
                using var updates = result.Object(9);
                if (updates.Number(10) == 1)
                {
                    using var update = updates.Item(7, 0);
                    // IUpdate3 adds BrowseOnly to the IUpdate interface used by the collection.
                    selected = update.Query(Update3);
                }
            }
            finally { job.Call(9); } // ISearchJob::CleanUp
            return selected;
        }
        catch { selected?.Dispose(); throw; }
    }

    internal static bool Matches(WuaInterface update, WindowsUpdateCandidate exact)
    {
        using var key = update.Object(19); // Identity
        if (!Guid.TryParseExact(key.Text(8, 36), "D", out var id) || exact.Identity.UpdateId != id ||
            exact.Identity.Revision != key.Number(7) || exact.MaximumDownloadBytes != update.DownloadBytes() ||
            update.Number(42) != 1 || update.Text(18, 128) != Cbs || // software, HandlerID
            update.Flag(22) || update.Flag(10) || !update.Flag(16) || // beta, source, EULA
            update.Flag(56) || update.Flag(27) || update.Flag(26) || update.Flag(24) || // browse, mandatory, installed, hidden
            update.Number(48) != 1) // DeploymentAction = installation
        {
            return false;
        }

        using var bundle = update.OptionalObject(9);
        using var behavior = update.OptionalObject(21);
        if (bundle is not null && bundle.Number(10) != 0 || behavior is null || behavior.Flag(7)) { return false; }
        using var categories = update.Object(11);
        var count = categories.Number(9);
        if (count is < 1 or > 32) { return false; }
        var expected = exact.Classification == WindowsUpdateClass.Security ? Security : Critical;
        var found = false;
        for (var index = 0; index < count; index++)
        {
            using var category = categories.Item(7, index);
            if (category.Text(14, 64) != "UpdateClassification") { continue; }
            if (found || !Guid.TryParseExact(category.Text(8, 36), "D", out var actual) || actual != expected) { return false; }
            found = true;
        }
        return found;
    }
}
