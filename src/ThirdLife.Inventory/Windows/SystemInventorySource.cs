using System.Runtime.InteropServices;

namespace ThirdLife.Inventory.Windows;

internal enum SystemInventorySourceStatus
{
    Available = 1,
    Unavailable,
    AccessDenied,
    InvalidData,
}

internal sealed class SystemInventorySourceValue<T>
    where T : notnull
{
    private SystemInventorySourceValue(SystemInventorySourceStatus status, T? value, bool hasValue)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "The source status is not defined.");
        }

        if ((status == SystemInventorySourceStatus.Available) != hasValue)
        {
            throw new ArgumentException("Only an available source value may carry data.", nameof(value));
        }

        Status = status;
        Value = value;
    }

    public SystemInventorySourceStatus Status { get; }

    public T? Value { get; }

    public static SystemInventorySourceValue<T> Available(T value) =>
        new(
            SystemInventorySourceStatus.Available,
            value ?? throw new ArgumentNullException(nameof(value)),
            hasValue: true);

    public static SystemInventorySourceValue<T> Unavailable() =>
        new(SystemInventorySourceStatus.Unavailable, value: default, hasValue: false);

    public static SystemInventorySourceValue<T> AccessDenied() =>
        new(SystemInventorySourceStatus.AccessDenied, value: default, hasValue: false);

    public static SystemInventorySourceValue<T> InvalidData() =>
        new(SystemInventorySourceStatus.InvalidData, value: default, hasValue: false);

    public T GetRequiredValue() =>
        Status == SystemInventorySourceStatus.Available
            ? Value!
            : throw new InvalidOperationException("The source value is not available.");
}

internal sealed class SystemInventorySourceSnapshot : IDisposable
{
    private byte[]? _ownedFirmwareTable;

    public SystemInventorySourceSnapshot(
        SystemInventorySourceValue<byte[]> firmwareTable,
        SystemInventorySourceValue<ushort> nativeArchitecture,
        SystemInventorySourceValue<ulong> installedMemoryKilobytes,
        SystemInventorySourceValue<uint> logicalProcessorCount)
    {
        FirmwareTable = firmwareTable ?? throw new ArgumentNullException(nameof(firmwareTable));
        NativeArchitecture = nativeArchitecture ?? throw new ArgumentNullException(nameof(nativeArchitecture));
        InstalledMemoryKilobytes = installedMemoryKilobytes
            ?? throw new ArgumentNullException(nameof(installedMemoryKilobytes));
        LogicalProcessorCount = logicalProcessorCount
            ?? throw new ArgumentNullException(nameof(logicalProcessorCount));

        if (FirmwareTable.Status == SystemInventorySourceStatus.Available)
        {
            _ownedFirmwareTable = FirmwareTable.GetRequiredValue();
        }
    }

    public SystemInventorySourceValue<byte[]> FirmwareTable { get; }

    public SystemInventorySourceValue<ushort> NativeArchitecture { get; }

    public SystemInventorySourceValue<ulong> InstalledMemoryKilobytes { get; }

    public SystemInventorySourceValue<uint> LogicalProcessorCount { get; }

    public void Dispose()
    {
        if (_ownedFirmwareTable is null)
        {
            return;
        }

        Array.Clear(_ownedFirmwareTable);
        _ownedFirmwareTable = null;
    }
}

internal interface ISystemInventorySource
{
    SystemInventorySourceSnapshot Read(CancellationToken cancellationToken);
}

internal sealed class WindowsSystemInventorySource : ISystemInventorySource
{
    internal const int MaximumFirmwareTableBytes = 1_048_576;

    private const uint RawSmbiosProviderSignature = 0x52534D42;
    private const ushort AllProcessorGroups = 0xffff;

    public SystemInventorySourceSnapshot Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return new SystemInventorySourceSnapshot(
                SystemInventorySourceValue<byte[]>.Unavailable(),
                SystemInventorySourceValue<ushort>.Unavailable(),
                SystemInventorySourceValue<ulong>.Unavailable(),
                SystemInventorySourceValue<uint>.Unavailable());
        }

        byte[]? ownedFirmwareTable = null;

        try
        {
            var firmwareTable = ReadFirmwareTable();
            if (firmwareTable.Status == SystemInventorySourceStatus.Available)
            {
                ownedFirmwareTable = firmwareTable.GetRequiredValue();
            }

            cancellationToken.ThrowIfCancellationRequested();
            var nativeArchitecture = MapOperatingSystemArchitecture(RuntimeInformation.OSArchitecture);

            cancellationToken.ThrowIfCancellationRequested();
            var installedMemory = ReadInstalledMemory();

            cancellationToken.ThrowIfCancellationRequested();
            var logicalProcessorCount = ReadLogicalProcessorCount();

            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = new SystemInventorySourceSnapshot(
                firmwareTable,
                nativeArchitecture,
                installedMemory,
                logicalProcessorCount);
            ownedFirmwareTable = null;
            return snapshot;
        }
        finally
        {
            if (ownedFirmwareTable is not null)
            {
                Array.Clear(ownedFirmwareTable);
            }
        }
    }

    private static SystemInventorySourceValue<byte[]> ReadFirmwareTable()
    {
        var requiredBytes = NativeMethods.GetSystemFirmwareTable(
            RawSmbiosProviderSignature,
            firmwareTableId: 0,
            firmwareTableBuffer: null,
            bufferSize: 0);
        if (requiredBytes == 0)
        {
            return CreateFailure<byte[]>(Marshal.GetLastPInvokeError());
        }

        if (requiredBytes < SmbiosInventoryParser.RawHeaderBytes ||
            requiredBytes > MaximumFirmwareTableBytes)
        {
            return SystemInventorySourceValue<byte[]>.InvalidData();
        }

        var buffer = new byte[checked((int)requiredBytes)];
        var writtenBytes = NativeMethods.GetSystemFirmwareTable(
            RawSmbiosProviderSignature,
            firmwareTableId: 0,
            buffer,
            requiredBytes);
        if (writtenBytes == requiredBytes)
        {
            return SystemInventorySourceValue<byte[]>.Available(buffer);
        }

        Array.Clear(buffer);
        return writtenBytes == 0
            ? CreateFailure<byte[]>(Marshal.GetLastPInvokeError())
            : SystemInventorySourceValue<byte[]>.InvalidData();
    }

    private static SystemInventorySourceValue<ulong> ReadInstalledMemory()
    {
        if (NativeMethods.GetPhysicallyInstalledSystemMemory(out var installedKilobytes))
        {
            return SystemInventorySourceValue<ulong>.Available(installedKilobytes);
        }

        return CreateFailure<ulong>(Marshal.GetLastPInvokeError());
    }

    private static SystemInventorySourceValue<uint> ReadLogicalProcessorCount()
    {
        var logicalProcessorCount = NativeMethods.GetActiveProcessorCount(AllProcessorGroups);
        return logicalProcessorCount == 0
            ? SystemInventorySourceValue<uint>.InvalidData()
            : SystemInventorySourceValue<uint>.Available(logicalProcessorCount);
    }

    internal static SystemInventorySourceValue<ushort> MapOperatingSystemArchitecture(
        Architecture operatingSystemArchitecture) =>
        operatingSystemArchitecture switch
        {
            Architecture.X86 => SystemInventorySourceValue<ushort>.Available(0),
            Architecture.Arm => SystemInventorySourceValue<ushort>.Available(5),
            Architecture.X64 => SystemInventorySourceValue<ushort>.Available(9),
            Architecture.Arm64 => SystemInventorySourceValue<ushort>.Available(12),
            _ => SystemInventorySourceValue<ushort>.InvalidData(),
        };

    private static SystemInventorySourceValue<T> CreateFailure<T>(int win32Error)
        where T : notnull =>
        win32Error switch
        {
            5 => SystemInventorySourceValue<T>.AccessDenied(),
            1 or 50 or 120 or 1168 => SystemInventorySourceValue<T>.Unavailable(),
            _ => SystemInventorySourceValue<T>.InvalidData(),
        };

    private static class NativeMethods
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        internal static extern uint GetSystemFirmwareTable(
            uint firmwareTableProviderSignature,
            uint firmwareTableId,
            [Out] byte[]? firmwareTableBuffer,
            uint bufferSize);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern uint GetActiveProcessorCount(ushort groupNumber);
    }
}
