using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace ThirdLife.Broker.WindowsUpdates;

// The native callback interfaces all have the same one-method IUnknown ABI, with distinct IIDs.
// Supply a source-generated CCW and wait for a completion notification without dispatching messages.
[Guid("88aee058-d4b0-4725-a2f1-814a67ae964c"), GeneratedComInterface]
internal partial interface ISearchCompleted
{
    [PreserveSig] int Invoke(nint job, nint args);
}

[Guid("77254866-9f5b-4c8e-b9e2-c77a8530d64b"), GeneratedComInterface]
internal partial interface IDownloadCompleted
{
    [PreserveSig] int Invoke(nint job, nint args);
}

[Guid("45f4f6f3-d602-4f98-9a8a-3efa152ad2d3"), GeneratedComInterface]
internal partial interface IInstallationCompleted
{
    [PreserveSig] int Invoke(nint job, nint args);
}

[Guid("8c3f1cdd-6173-4591-aebd-a56a53ca77c1"), GeneratedComInterface]
internal partial interface IDownloadProgressChanged
{
    [PreserveSig] int Invoke(nint job, nint args);
}

[Guid("e01402d5-f8da-43ba-a012-38894bd048f1"), GeneratedComInterface]
internal partial interface IInstallationProgressChanged
{
    [PreserveSig] int Invoke(nint job, nint args);
}

[GeneratedComClass]
internal sealed partial class WuaCompleted : ISearchCompleted, IDownloadCompleted, IInstallationCompleted
{
    private readonly TaskCompletionSource _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Invoke(nint job, nint args) { _signal.TrySetResult(); return 0; }

    internal void Wait(WuaInterface job, int abortSlot, CancellationToken token)
    {
        try { _signal.Task.WaitAsync(token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException)
        {
            job.Call(abortSlot);
            throw;
        }
    }
}

[GeneratedComClass]
internal sealed partial class WuaProgress : IDownloadProgressChanged, IInstallationProgressChanged
{
    // Do not retain high-frequency native callbacks; the per-job completion supplies final progress.
    public int Invoke(nint job, nint args) => 0;
}

internal abstract class WuaCallback : IDisposable
{
    internal abstract nint Pointer { get; }
    public abstract void Dispose();
    internal static WuaCallback ForCompletion(bool install, WuaCompleted completed) => install
        ? new WuaCallback<IInstallationCompleted>(completed) : new WuaCallback<IDownloadCompleted>(completed);
    internal static WuaCallback ForProgress(bool install, WuaProgress progress) => install
        ? new WuaCallback<IInstallationProgressChanged>(progress) : new WuaCallback<IDownloadProgressChanged>(progress);
}

internal sealed unsafe class WuaCallback<T>(T callback) : WuaCallback where T : class
{
    private void* _value = ComInterfaceMarshaller<T>.ConvertToUnmanaged(callback);
    internal override nint Pointer => (nint)_value;
    public override void Dispose()
    {
        if (_value == null) { return; }
        ComInterfaceMarshaller<T>.Free(_value);
        _value = null;
    }
}
