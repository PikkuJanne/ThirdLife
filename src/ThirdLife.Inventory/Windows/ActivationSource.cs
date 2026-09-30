using System.Runtime.InteropServices;

namespace ThirdLife.Inventory.Windows;

internal sealed record ActivationSourceSnapshot(SystemInventorySourceValue<int[]> Statuses, bool CleanupComplete = true);

internal interface IActivationSource
{
    ActivationSourceSnapshot Read(CancellationToken cancellationToken);
}

internal interface IActivationNativeApi
{
    int Open(out nint context);

    int ReadStatuses(nint context, in Guid applicationId, out uint count, out nint statuses);

    nint Free(nint statuses);

    int Close(nint context);
}

internal sealed class WindowsActivationSource : IActivationSource
{
    internal const int MaximumStatusCount = 128;
    // SL_LICENSING_STATUS has a 16-byte SKU GUID, four 32-bit fields, then UINT64.
    // Only eStatus is copied. SKU IDs, reason codes, grace metadata and expiry are ignored.
    internal const int NativeStatusBytes = 40;
    internal const int NativeStatusOffset = 16;

    private static readonly Guid WindowsApplicationId = new("55c92734-d682-4d71-983e-d6ec3f16059f");
    private readonly IActivationNativeApi _native;

    public WindowsActivationSource()
        : this(new WindowsActivationNativeApi())
    {
    }

    internal WindowsActivationSource(IActivationNativeApi native)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    public ActivationSourceSnapshot Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return new ActivationSourceSnapshot(SystemInventorySourceValue<int[]>.Unavailable());
        }

        nint context = 0;
        nint buffer = 0;
        var cleanupComplete = true;
        SystemInventorySourceValue<int[]> statuses;
        try
        {
            statuses = ReadCore(ref context, ref buffer, cancellationToken);
        }
        catch (DllNotFoundException)
        {
            statuses = SystemInventorySourceValue<int[]>.Unavailable();
        }
        catch (EntryPointNotFoundException)
        {
            statuses = SystemInventorySourceValue<int[]>.Unavailable();
        }
        finally
        {
            try
            {
                if (buffer != 0)
                {
                    cleanupComplete = _native.Free(buffer) == 0;
                }
            }
            finally
            {
                if (context != 0)
                {
                    cleanupComplete &= _native.Close(context) == 0;
                }
            }
        }

        return new ActivationSourceSnapshot(statuses, cleanupComplete);
    }

    private SystemInventorySourceValue<int[]> ReadCore(
        ref nint context,
        ref nint buffer,
        CancellationToken cancellationToken)
    {
        var result = _native.Open(out context);
        if (result != 0)
        {
            return Failure(result);
        }

        if (context == 0)
        {
            return SystemInventorySourceValue<int[]>.InvalidData();
        }

        cancellationToken.ThrowIfCancellationRequested();
        result = _native.ReadStatuses(context, in WindowsApplicationId, out var count, out buffer);
        if (result != 0)
        {
            return Failure(result);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (count > MaximumStatusCount || (count > 0 && buffer == 0))
        {
            return SystemInventorySourceValue<int[]>.InvalidData();
        }

        var statuses = new int[checked((int)count)];
        for (var index = 0; index < statuses.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            statuses[index] = Marshal.ReadInt32(buffer, checked((index * NativeStatusBytes) + NativeStatusOffset));
        }

        return SystemInventorySourceValue<int[]>.Available(statuses);
    }

    private static SystemInventorySourceValue<int[]> Failure(int result) =>
        result is unchecked((int)0x80070005) or unchecked((int)0xc004f025)
            ? SystemInventorySourceValue<int[]>.AccessDenied()
            : SystemInventorySourceValue<int[]>.Unavailable();
}

internal sealed class WindowsActivationNativeApi : IActivationNativeApi
{
    public int Open(out nint context) => NativeMethods.SLOpen(out context);

    public int ReadStatuses(nint context, in Guid applicationId, out uint count, out nint statuses) =>
        NativeMethods.SLGetLicensingStatusInformation(context, in applicationId, 0, 0, out count, out statuses);

    public nint Free(nint statuses) => NativeMethods.LocalFree(statuses);

    public int Close(nint context) => NativeMethods.SLClose(context);

    private static class NativeMethods
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("slc.dll", ExactSpelling = true)]
        internal static extern int SLOpen(out nint context);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("slc.dll", ExactSpelling = true)]
        internal static extern int SLGetLicensingStatusInformation(
            nint context,
            in Guid applicationId,
            nint productSkuId,
            nint rightName,
            out uint statusCount,
            out nint licensingStatus);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("slc.dll", ExactSpelling = true)]
        internal static extern int SLClose(nint context);

        // The SDK defines SLFreeMemory as LocalFree.
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern nint LocalFree(nint memory);
    }
}
