using System.Runtime.InteropServices;
using ThirdLife.Actions.Registry;
using ThirdLife.Broker.WindowsUpdates;
using ThirdLife.Core.WindowsUpdates;
using Xunit.Abstractions;

namespace ThirdLife.Broker.Tests;

public sealed class NativeWindowsUpdateTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(2, 0, 2, 0, WindowsUpdateStatus.Succeeded)]
    [InlineData(2, 0, 2, 1, WindowsUpdateStatus.Partial)]
    [InlineData(2, -1, 2, 0, WindowsUpdateStatus.Partial)]
    [InlineData(3, 0, 2, 0, WindowsUpdateStatus.Partial)]
    [InlineData(2, 0, 4, -1, WindowsUpdateStatus.Partial)]
    [InlineData(4, -1, 4, -1, WindowsUpdateStatus.Failed)]
    [InlineData(5, -1, 5, -1, WindowsUpdateStatus.Cancelled)]
    [InlineData(0, 0, 0, 0, WindowsUpdateStatus.Partial)]
    public void ReceiptChecksOverallAndExactItemResults(int overall, int overallError, int item, int itemError,
        WindowsUpdateStatus expected) => Assert.Equal(expected, NativeWindowsUpdateBackend.Outcome(overall, overallError, item, itemError));

    [Fact]
    public void UnavailableOptionalNativeMeasurementRemainsUnknown()
    {
        Assert.Null(NativeWindowsUpdateBackend.ReadOptional(() => throw new NotSupportedException()));
        Assert.Equal(0, NativeWindowsUpdateBackend.ReadOptional(() => 0));
        Assert.Throws<OperationCanceledException>(() => NativeWindowsUpdateBackend.ReadOptional(() => throw new OperationCanceledException()));
    }

    [Fact]
    public async Task NativeWorkerRejectsScanAndCancelledRequestsBeforeAuthorization()
    {
        var worker = new NativeWindowsUpdateBackend();
        var scan = ActionRegistry.CreateScanWindowsUpdate(Guid.NewGuid());
        Assert.Equal(WindowsUpdateStatus.ApprovalChanged, (await worker.ApplyAsync(scan, _ => throw new InvalidOperationException(), CancellationToken.None)).Status);
        Assert.Equal(WindowsUpdateStatus.Cancelled, (await worker.ApplyAsync(scan, _ => throw new InvalidOperationException(), new CancellationToken(true))).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeCallbacksExposeExpectedVtablesToWua(bool install)
    {
        var completed = new WuaCompleted();
        using var complete = WuaCallback.ForCompletion(install, completed);
        using var progress = WuaCallback.ForProgress(install, new WuaProgress());
        InvokeTypedCallback(progress.Pointer, install ? typeof(IInstallationProgressChanged).GUID : typeof(IDownloadProgressChanged).GUID);
        InvokeTypedCallback(complete.Pointer, install ? typeof(IInstallationCompleted).GUID : typeof(IDownloadCompleted).GUID);
        using var search = new WuaCallback<ISearchCompleted>(completed);
        InvokeTypedCallback(search.Pointer, typeof(ISearchCompleted).GUID);
    }

    private static void InvokeTypedCallback(nint pointer, Guid iid)
    {
        Assert.Equal(0, Marshal.QueryInterface(pointer, in iid, out var typed));
        try
        {
            var invoke = Marshal.ReadIntPtr(Marshal.ReadIntPtr(typed), 3 * IntPtr.Size);
            Assert.Equal(0, Marshal.GetDelegateForFunctionPointer<CallbackMethod>(invoke)(typed, 0, 0));
        }
        finally { _ = Marshal.Release(typed); }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CallbackMethod(nint self, nint job, nint args);

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task NativeEmptyActorsExposeNonforcedAndQuietOptionsWithoutBeginningAnOperation()
    {
        await Task.Run(() =>
        {
            using var apartment = WuaApartment.Open();
            using var session = WuaInterface.Activate(new("4cb43d7f-7eee-4906-8698-60da1c38f2fe"), new("816858a4-260d-4260-933a-2585f1abc76b"));
            using var downloader = session.Object(13);
            NativeWindowsUpdateBackend.ConfigureOperation(downloader, false);
            Assert.False(downloader.Flag(9));
            using var installer = session.Object(14);
            NativeWindowsUpdateBackend.ConfigureOperation(installer, true);
            using var quietInstaller = installer.Query(new("3442d4fe-224d-4cee-98cf-30e0c4d229e6"));
            Assert.False(quietInstaller.Flag(9));
            Assert.False(quietInstaller.Flag(25));
            Assert.True(quietInstaller.Flag(28));
        }).WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task NativeExactLookupRejectsMissingRevisionAndChecksARealRestrictedOfferWhenAvailable()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var worker = new NativeWindowsUpdateBackend();
        var missing = new WindowsUpdateCandidate(new(Guid.NewGuid(), 1), WindowsUpdateClass.Security, 0);
        var authorizations = 0;
        bool Veto(WindowsUpdatePreparation _) { Interlocked.Increment(ref authorizations); return false; }
        var absent = await worker.ApplyAsync(ActionRegistry.CreateDownloadWindowsUpdate(Guid.NewGuid(), missing), Veto, timeout.Token);
        Assert.Equal(WindowsUpdateStatus.ApprovalChanged, absent.Status);
        Assert.Equal(0, authorizations);

        var restricted = await Task.Run(() => ReadRestrictedOffer(timeout.Token), timeout.Token);
        if (restricted is null)
        {
            output.WriteLine("Native broker lookup missing=Rejected, restricted=Unavailable, authorizations=0.");
            return;
        }
        var current = await worker.ApplyAsync(ActionRegistry.CreateDownloadWindowsUpdate(Guid.NewGuid(), restricted), Veto, timeout.Token);
        Assert.Equal(WindowsUpdateStatus.ApprovalChanged, current.Status);
        Assert.Null(current.ProgressPercent);
        Assert.Equal(0, authorizations);
        output.WriteLine("Native broker lookup missing=Rejected, restricted=Rejected, authorizations=0.");
    }

    // Read-only inventory locates a real restricted offer. Its actual identity, revision and maximum
    // bytes are preserved; the deliberately incorrect security classification probes the native check.
    private static WindowsUpdateCandidate? ReadRestrictedOffer(CancellationToken token)
    {
        using var apartment = WuaApartment.Open();
        using var session = WuaInterface.Activate(new("4cb43d7f-7eee-4906-8698-60da1c38f2fe"), new("816858a4-260d-4260-933a-2585f1abc76b"));
        using var searcher = session.Object(12);
        searcher.SetFlag(8, false);
        searcher.SetFlag(21, true);
        var completed = new WuaCompleted();
        using var callback = new WuaCallback<ISearchCompleted>(completed);
        using var job = searcher.BeginSearch("IsInstalled=0 and IsHidden=0 and DeploymentAction='Installation'", callback.Pointer);
        try
        {
            completed.Wait(job, 10, token);
            using var result = searcher.End(16, job);
            Assert.Equal(2, result.Number(7));
            using var warnings = result.Object(10);
            Assert.Equal(0, warnings.Number(9));
            using var updates = result.Object(9);
            int count = updates.Number(10);
            Assert.InRange(count, 0, WindowsUpdatePolicy.MaximumUpdates);
            for (var index = 0; index < count; index++)
            {
                using var update = updates.Item(7, index);
                if (update.Text(18, 128) == "http://schemas.microsoft.com/msus/2002/12/UpdateHandlers/Cbs" ||
                    update.DownloadBytes() is not { } bytes) { continue; }
                using var identity = update.Object(19);
                return new(new(Guid.ParseExact(identity.Text(8, 36), "D"), identity.Number(7)), WindowsUpdateClass.Security, bytes);
            }
            return null;
        }
        finally { job.Call(9); }
    }
}
