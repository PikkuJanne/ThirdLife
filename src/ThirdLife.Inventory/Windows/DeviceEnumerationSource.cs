namespace ThirdLife.Inventory.Windows;

internal enum DeviceClass
{
    Wifi = 1, Ethernet, Bluetooth, Audio, Camera, Microphone, Display, Keyboard, Pointing, Touch, UsbController,
}

internal enum DeviceEnumerationStatus
{
    Available = 1, Unavailable, AccessDenied, InvalidData,
}

// Closed numeric/class data only. No device name, address or instance identity crosses this seam.
internal sealed record DeviceClassSnapshot(DeviceClass Class, DeviceEnumerationStatus Status,
    bool? Present = null, uint? ProblemCount = null, uint? NativeErrorCode = null, bool CleanupComplete = true);

internal interface IDeviceClassSource
{
    DeviceClassSnapshot Read(DeviceClass deviceClass, CancellationToken cancellationToken);
}

internal interface IDeviceEnumerationSource
{
    IReadOnlyList<DeviceClassSnapshot> Read(CancellationToken cancellationToken);
}

internal sealed class DeviceEnumerationCleanupException : Exception;

internal sealed class WindowsDeviceEnumerationSource : IDeviceEnumerationSource
{
    internal const int MaximumDevicesPerClass = 256;
    private readonly IDeviceClassSource _devices;
    private readonly IDeviceClassSource _network;

    public WindowsDeviceEnumerationSource() : this(new WindowsDeviceClassSource(), new WindowsNetworkDeviceSource()) { }

    internal WindowsDeviceEnumerationSource(IDeviceClassSource devices, IDeviceClassSource network)
    {
        _devices = devices;
        _network = network;
    }

    public IReadOnlyList<DeviceClassSnapshot> Read(CancellationToken cancellationToken)
    {
        var output = new List<DeviceClassSnapshot>();
        foreach (var deviceClass in Enum.GetValues<DeviceClass>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = deviceClass is DeviceClass.Wifi or DeviceClass.Ethernet ? _network : _devices;
            DeviceClassSnapshot result;
            try { result = source.Read(deviceClass, cancellationToken); }
            catch (DeviceEnumerationCleanupException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (UnauthorizedAccessException) { result = new(deviceClass, DeviceEnumerationStatus.AccessDenied, NativeErrorCode: 5); }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
            { result = new(deviceClass, DeviceEnumerationStatus.Unavailable); }
            catch (Exception) { result = new(deviceClass, DeviceEnumerationStatus.InvalidData); }
            if (!result.CleanupComplete) { throw new DeviceEnumerationCleanupException(); }
            output.Add(result);
        }

        return output.AsReadOnly();
    }
}
