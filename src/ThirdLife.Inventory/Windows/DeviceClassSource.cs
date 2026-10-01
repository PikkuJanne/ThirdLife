using System.Runtime.InteropServices;

namespace ThirdLife.Inventory.Windows;

internal sealed record DeviceClassNativeOpen(nint Handle, uint ErrorCode = 0);
internal sealed record DeviceClassNativeRow(uint ErrorCode, uint DeviceNode = 0);
internal sealed record DeviceClassNativeProblem(bool? HasProblem, uint ErrorCode = 0);
internal readonly record struct DeviceClassSelection(Guid ClassGuid, bool IsInterface);

// The seam accepts a closed class enum; it has no property, path, name or command input.
internal interface IDeviceClassNativeApi
{
    bool IsSupported { get; }
    DeviceClassNativeOpen Open(DeviceClass deviceClass);
    DeviceClassNativeRow Next(nint handle, uint index);
    DeviceClassNativeProblem ReadProblem(uint deviceNode);
    uint Close(nint handle);
    int ReadDigitizerFlags();
}

internal sealed class WindowsDeviceClassSource : IDeviceClassSource
{
    private readonly IDeviceClassNativeApi _native;

    public WindowsDeviceClassSource() : this(new WindowsDeviceClassNativeApi()) { }

    internal WindowsDeviceClassSource(IDeviceClassNativeApi native) =>
        _native = native ?? throw new ArgumentNullException(nameof(native));

    public DeviceClassSnapshot Read(DeviceClass deviceClass, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_native.IsSupported) { return new(deviceClass, DeviceEnumerationStatus.Unavailable); }
        nint handle = 0;
        var cleanupComplete = true;
        var cancelled = false;
        DeviceClassSnapshot result;
        try
        {
            if (deviceClass == DeviceClass.Touch)
            {
                result = NormalizeDigitizer(_native.ReadDigitizerFlags());
            }
            else
            {
                // Reject unowned classes before crossing the native boundary.
                _ = WindowsDeviceClassNativeApi.Selection(deviceClass);
                var opened = _native.Open(deviceClass);
                handle = opened.Handle;
                if (opened.ErrorCode != 0 || handle == 0 || handle == new nint(-1))
                {
                    result = Failure(deviceClass, opened.ErrorCode == 0 ? 13U : opened.ErrorCode);
                }
                else
                {
                    result = ReadRows(deviceClass, handle, cancellationToken);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            result = new(deviceClass, DeviceEnumerationStatus.Unavailable);
        }
        catch (UnauthorizedAccessException) { result = Failure(deviceClass, 5); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        { result = new(deviceClass, DeviceEnumerationStatus.Unavailable); }
        catch (Exception) { result = new(deviceClass, DeviceEnumerationStatus.InvalidData); }
        finally
        {
            if (handle != 0 && handle != new nint(-1))
            {
                try { cleanupComplete = _native.Close(handle) == 0; }
                catch (Exception) { cleanupComplete = false; }
            }
        }

        if (cancelled || cancellationToken.IsCancellationRequested)
        {
            if (!cleanupComplete) { throw new DeviceEnumerationCleanupException(); }
            throw new OperationCanceledException(cancellationToken);
        }

        return result with { CleanupComplete = cleanupComplete };
    }

    private DeviceClassSnapshot ReadRows(DeviceClass deviceClass, nint handle, CancellationToken cancellationToken)
    {
        uint problems = 0;
        // One extra read distinguishes a complete 256-device set from truncation.
        for (uint index = 0; index <= WindowsDeviceEnumerationSource.MaximumDevicesPerClass; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = _native.Next(handle, index);
            cancellationToken.ThrowIfCancellationRequested();
            if (row.ErrorCode == 259)
            { return new(deviceClass, DeviceEnumerationStatus.Available, index != 0, problems); }
            if (row.ErrorCode != 0) { return Failure(deviceClass, row.ErrorCode); }
            if (index == WindowsDeviceEnumerationSource.MaximumDevicesPerClass)
            { return new(deviceClass, DeviceEnumerationStatus.InvalidData); }

            var problem = _native.ReadProblem(row.DeviceNode);
            cancellationToken.ThrowIfCancellationRequested();
            if (problem.ErrorCode != 0) { return Failure(deviceClass, problem.ErrorCode); }
            if (!problem.HasProblem.HasValue) { return new(deviceClass, DeviceEnumerationStatus.InvalidData); }
            if (problem.HasProblem.Value) { problems++; }
        }

        return new(deviceClass, DeviceEnumerationStatus.InvalidData);
    }

    internal static DeviceClassSnapshot NormalizeDigitizer(int flags)
    {
        // SM_DIGITIZER is session/service dependent. Zero also means API failure,
        // so it cannot prove physical absence. The ready bit is not a function test.
        if (flags == 0) { return new(DeviceClass.Touch, DeviceEnumerationStatus.Unavailable); }
        if ((flags & ~0xCF) != 0) { return new(DeviceClass.Touch, DeviceEnumerationStatus.InvalidData); }
        if ((flags & 0x0F) == 0) { return new(DeviceClass.Touch, DeviceEnumerationStatus.Unavailable); }
        return new(DeviceClass.Touch, DeviceEnumerationStatus.Available, (flags & 3) != 0);
    }

    internal static DeviceClassSnapshot Failure(DeviceClass deviceClass, uint error) => new(deviceClass, error switch
    {
        5 or 1314 => DeviceEnumerationStatus.AccessDenied,
        1 or 2 or 3 or 6 or 21 or 31 or 50 or 120 or 1167 or 1168 => DeviceEnumerationStatus.Unavailable,
        _ => DeviceEnumerationStatus.InvalidData,
    }, NativeErrorCode: error);
}

internal sealed class WindowsDeviceClassNativeApi : IDeviceClassNativeApi
{
    public bool IsSupported => OperatingSystem.IsWindows();

    internal static DeviceClassSelection Selection(DeviceClass deviceClass) => deviceClass switch
    {
        DeviceClass.Bluetooth => new(new("0850302a-b344-4fda-9be9-90576b8d46f0"), true),
        DeviceClass.Audio => new(new("e6327cad-dcec-4949-ae8a-991e976a79d2"), true),
        DeviceClass.Camera => new(new("e5323777-f976-4f5b-9b55-b94699c46e44"), true),
        DeviceClass.Microphone => new(new("2eef81be-33fa-4800-9670-1cd474972c3f"), true),
        DeviceClass.Display => new(new("4d36e96e-e325-11ce-bfc1-08002be10318"), false),
        DeviceClass.Keyboard => new(new("4d36e96b-e325-11ce-bfc1-08002be10318"), false),
        DeviceClass.Pointing => new(new("4d36e96f-e325-11ce-bfc1-08002be10318"), false),
        DeviceClass.UsbController => new(new("3abf6f2d-71c4-462a-8a92-1e6861e6af27"), true),
        _ => throw new ArgumentOutOfRangeException(nameof(deviceClass)),
    };

    public DeviceClassNativeOpen Open(DeviceClass deviceClass)
    {
        var selection = Selection(deviceClass);
        var guid = selection.ClassGuid;
        var handle = NativeMethods.SetupDiGetClassDevsW(in guid, 0, 0, selection.IsInterface ? 0x12U : 2U);
        return new(handle, handle == new nint(-1) ? unchecked((uint)Marshal.GetLastPInvokeError()) : 0);
    }

    public DeviceClassNativeRow Next(nint handle, uint index)
    {
        var data = new DeviceInfoData { Size = (uint)Marshal.SizeOf<DeviceInfoData>() };
        if (NativeMethods.SetupDiEnumDeviceInfo(handle, index, ref data)) { return new(0, data.DeviceNode); }
        var error = unchecked((uint)Marshal.GetLastPInvokeError());
        return new(error == 0 ? 13U : error);
    }

    public DeviceClassNativeProblem ReadProblem(uint deviceNode)
    {
        var code = NativeMethods.CM_Get_DevNode_Status(out var status, out _, deviceNode, 0);
        // All outward error numbers use the Win32 domain, including Configuration Manager errors.
        return code == 0 ? new((status & 0x400U) != 0) :
            new(null, NativeMethods.CM_MapCrToWin32Err(code, 31));
    }

    public uint Close(nint handle)
    {
        if (NativeMethods.SetupDiDestroyDeviceInfoList(handle)) { return 0; }
        var error = unchecked((uint)Marshal.GetLastPInvokeError());
        return error == 0 ? 13U : error;
    }

    public int ReadDigitizerFlags() => NativeMethods.GetSystemMetrics(94);

    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceInfoData
    {
        internal uint Size;
        internal Guid ClassGuid;
        internal uint DeviceNode;
        internal nuint Reserved;
    }

    private static class NativeMethods
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("setupapi.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern nint SetupDiGetClassDevsW(in Guid classGuid, nint enumerator, nint parent, uint flags);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("setupapi.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiEnumDeviceInfo(nint handle, uint index, ref DeviceInfoData data);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("setupapi.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiDestroyDeviceInfoList(nint handle);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("cfgmgr32.dll", ExactSpelling = true)]
        internal static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint deviceNode, uint flags);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("cfgmgr32.dll", ExactSpelling = true)]
        internal static extern uint CM_MapCrToWin32Err(uint code, uint defaultError);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("user32.dll", ExactSpelling = true)]
        internal static extern int GetSystemMetrics(int index);
    }
}
