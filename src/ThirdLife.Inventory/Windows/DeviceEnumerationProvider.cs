using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;

namespace ThirdLife.Inventory.Windows;

/// <summary>Local device presence evidence. Enumeration never establishes device functionality.</summary>
public sealed class DeviceEnumerationProvider : IInventoryProvider
{
    internal const string ProviderIdentity = "windows-device-enumeration";
    private static readonly DeviceClass[] Classes = Enum.GetValues<DeviceClass>();
    private readonly IDeviceEnumerationSource _source;
    private int _inProgress;
    private int _cleanupBlocked;

    public DeviceEnumerationProvider() : this(new WindowsDeviceEnumerationSource(), ProviderEvidenceOrigin.ActiveMachine) { }

    internal DeviceEnumerationProvider(IDeviceEnumerationSource source, ProviderEvidenceOrigin origin)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        var definitions = Classes.SelectMany(static deviceClass => new[]
        {
            Definition(deviceClass, PresenceSuffix(deviceClass), EvidenceValueKind.Boolean),
            Definition(deviceClass, "collection_status", EvidenceValueKind.Enum),
            Definition(deviceClass, "problem_count", EvidenceValueKind.WholeNumber),
            Definition(deviceClass, "native_error_code", EvidenceValueKind.WholeNumber),
        }).Append(new(new("devices.functional_test"), EvidenceValueKind.Enum, null, "device_enumeration.scope.v1"));
        Descriptor = new(new(ProviderIdentity), ProviderPrivilegeRequirement.StandardUser,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(15), ProviderNetworkUse.None, origin,
            [ProviderOperatingSystem.Windows11],
            new(new("devices.provider_status"), "device_enumeration.provider.status"), definitions);
    }

    public InventoryProviderDescriptor Descriptor { get; }

    public async ValueTask<ProviderReadResult> ObserveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _cleanupBlocked) != 0) { return ProviderReadResult.CleanupIncomplete(); }
        if (Interlocked.CompareExchange(ref _inProgress, 1, 0) != 0) { return ProviderReadResult.Unavailable(); }
        try
        {
            if (Volatile.Read(ref _cleanupBlocked) != 0) { return ProviderReadResult.CleanupIncomplete(); }
            // Native calls cannot always be interrupted. Admission remains held through native cleanup,
            // even after the runner stops waiting. Late evidence is never published after cancellation.
            var snapshot = await Task.Run(() => _source.Read(cancellationToken), cancellationToken).ConfigureAwait(false);
            var values = snapshot.Take(Classes.Length + 1).ToArray();
            if (values.Any(static value => value is not null && !value.CleanupComplete))
            { throw new DeviceEnumerationCleanupException(); }
            cancellationToken.ThrowIfCancellationRequested();
            return Normalize(values);
        }
        catch (DeviceEnumerationCleanupException)
        {
            Volatile.Write(ref _cleanupBlocked, 1);
            return ProviderReadResult.CleanupIncomplete();
        }
        finally { Volatile.Write(ref _inProgress, 0); }
    }

    internal static string ClassKey(DeviceClass deviceClass) => deviceClass switch
    {
        DeviceClass.Wifi => "wifi",
        DeviceClass.Ethernet => "ethernet",
        DeviceClass.Bluetooth => "bluetooth",
        DeviceClass.Audio => "audio",
        DeviceClass.Camera => "camera",
        DeviceClass.Microphone => "microphone",
        DeviceClass.Display => "display",
        DeviceClass.Keyboard => "keyboard",
        DeviceClass.Pointing => "pointing",
        DeviceClass.Touch => "touch",
        DeviceClass.UsbController => "usb_controller",
        _ => throw new ArgumentOutOfRangeException(nameof(deviceClass)),
    };

    internal static string PresenceSuffix(DeviceClass deviceClass) => deviceClass switch
    {
        DeviceClass.Camera => "video_interface_present",
        DeviceClass.Bluetooth => "radio_interface_present",
        DeviceClass.UsbController => "host_interface_present",
        DeviceClass.Audio => "render_endpoint_present",
        DeviceClass.Microphone => "capture_endpoint_present",
        DeviceClass.Touch => "session_present",
        DeviceClass.Display => "monitor_present",
        _ => "present",
    };

    private static string Source(DeviceClass deviceClass) => deviceClass switch
    {
        DeviceClass.Wifi or DeviceClass.Ethernet => "device_enumeration.net_adapter.numeric.v1",
        DeviceClass.Touch => "device_enumeration.digitizer.session.v1",
        _ => $"device_enumeration.local.{ClassKey(deviceClass)}.v1",
    };

    private static ProviderEvidenceDefinition Definition(DeviceClass deviceClass, string suffix, EvidenceValueKind kind) =>
        new(new($"devices.{ClassKey(deviceClass)}.{suffix}"), kind, null, Source(deviceClass));

    private static ProviderReadResult Normalize(DeviceClassSnapshot[] values)
    {
        if (values.Length > Classes.Length || values.Any(static value => value is null || !Enum.IsDefined(value.Class)))
        { return ProviderReadResult.InvalidData(); }

        var output = new List<NormalizedEvidence>();
        var status = DeviceEnumerationStatus.Available;
        foreach (var deviceClass in Classes)
        {
            var matching = values.Where(value => value.Class == deviceClass).ToArray();
            var conflict = matching.Length > 1;
            var item = matching.Length == 1 ? matching[0] :
                new(deviceClass, conflict ? DeviceEnumerationStatus.InvalidData : DeviceEnumerationStatus.Unavailable);
            if (!Enum.IsDefined(item.Status) ||
                (item.Status == DeviceEnumerationStatus.Available && !item.Present.HasValue) ||
                (item.Status != DeviceEnumerationStatus.Available && (item.Present.HasValue || item.ProblemCount.HasValue)) ||
                item.ProblemCount > WindowsDeviceEnumerationSource.MaximumDevicesPerClass ||
                (item.Present == false && item.ProblemCount > 0))
            { item = new(deviceClass, DeviceEnumerationStatus.InvalidData); }

            // Severity is explicit; class input order never selects a winning fact.
            status = (DeviceEnumerationStatus)Math.Max((int)status, (int)item.Status);
            var prefix = $"devices.{ClassKey(deviceClass)}.";
            var source = Source(deviceClass);
            var limitation = conflict ? ProviderLimitation.SourceValuesConflict : item.Status == DeviceEnumerationStatus.InvalidData ?
                ProviderLimitation.SourceValueMalformed : ProviderLimitation.SourceValueMissing;
            output.Add(item.Present.HasValue
                ? NormalizedEvidence.Observed(new(prefix + PresenceSuffix(deviceClass)), EvidenceValue.FromBoolean(item.Present.Value), sourceReference: source)
                : NormalizedEvidence.NotAvailable(new(prefix + PresenceSuffix(deviceClass)), limitation, source));
            var statusText = item.Status switch
            {
                DeviceEnumerationStatus.Available => "collected",
                DeviceEnumerationStatus.AccessDenied => "access_denied",
                DeviceEnumerationStatus.InvalidData => "invalid_data",
                _ => "unavailable",
            };
            output.Add(NormalizedEvidence.Observed(new(prefix + "collection_status"), EvidenceValue.FromEnum(statusText), sourceReference: source));
            AddNumber("problem_count", item.ProblemCount);
            AddNumber("native_error_code", item.NativeErrorCode);

            void AddNumber(string suffix, uint? value) => output.Add(value.HasValue
                ? NormalizedEvidence.Observed(new(prefix + suffix), EvidenceValue.FromInteger(value.Value), sourceReference: source)
                : NormalizedEvidence.NotAvailable(new(prefix + suffix), limitation, source));
        }

        output.Add(NormalizedEvidence.Observed(new("devices.functional_test"), EvidenceValue.FromEnum("not_performed"),
            sourceReference: "device_enumeration.scope.v1"));
        return status switch
        {
            DeviceEnumerationStatus.InvalidData => ProviderReadResult.InvalidData(output),
            DeviceEnumerationStatus.AccessDenied => ProviderReadResult.AccessDenied(output),
            DeviceEnumerationStatus.Unavailable => ProviderReadResult.Unavailable(output),
            _ => ProviderReadResult.Collected(output),
        };
    }
}
