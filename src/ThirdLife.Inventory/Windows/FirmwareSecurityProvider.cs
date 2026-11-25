using System.Globalization;
using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;

namespace ThirdLife.Inventory.Windows;

/// <summary>Read-only firmware observations; these do not establish legal ownership or overall readiness.</summary>
public sealed class FirmwareSecurityProvider : IInventoryProvider
{
    internal const string ProviderIdentity = "windows-firmware-security";
    private static readonly (string Key, EvidenceValueKind Kind, string Source)[] Fields =
    [
        ("firmware.mode", EvidenceValueKind.Enum, "firmware.get_firmware_type"),
        ("firmware.type_code", EvidenceValueKind.WholeNumber, "firmware.get_firmware_type"),
        ("firmware.native_error_code", EvidenceValueKind.WholeNumber, "firmware.get_firmware_type"),
        ("secure_boot.capable", EvidenceValueKind.Boolean, "secure_boot.device_guard.available_properties"),
        ("secure_boot.enabled", EvidenceValueKind.Boolean, "secure_boot.registry.enabled"),
        ("secure_boot.registry_value", EvidenceValueKind.WholeNumber, "secure_boot.registry.enabled"),
        ("secure_boot.native_error_code", EvidenceValueKind.WholeNumber, "secure_boot.registry.enabled"),
        ("secure_boot.available_properties", EvidenceValueKind.Text, "secure_boot.device_guard.available_properties"),
        ("secure_boot.device_guard_error_code", EvidenceValueKind.WholeNumber, "secure_boot.device_guard.available_properties"),
        ("firmware.hardware_security_settings", EvidenceValueKind.WholeNumber, "firmware.smbios.type24"),
        ("firmware.password.power_on_state", EvidenceValueKind.Enum, "firmware.smbios.type24"),
        ("firmware.password.keyboard_state", EvidenceValueKind.Enum, "firmware.smbios.type24"),
        ("firmware.password.administrator_state", EvidenceValueKind.Enum, "firmware.smbios.type24"),
        ("firmware.password.front_panel_reset_state", EvidenceValueKind.Enum, "firmware.smbios.type24"),
        ("ownership.firmware_lock_indicator", EvidenceValueKind.Enum, "firmware.smbios.type24"),
        ("tpm.present", EvidenceValueKind.Boolean, "tpm.tbs.device_info"),
        ("tpm.version", EvidenceValueKind.Text, "tpm.tbs.device_info"),
        ("tpm.tbs.collection_status", EvidenceValueKind.Enum, "tpm.tbs.device_info"),
        ("tpm.tbs.return_code", EvidenceValueKind.WholeNumber, "tpm.tbs.device_info"),
        ("tpm.tbs.struct_version", EvidenceValueKind.WholeNumber, "tpm.tbs.device_info"),
        ("tpm.tbs.version_code", EvidenceValueKind.WholeNumber, "tpm.tbs.device_info"),
        ("tpm.tbs.interface_type", EvidenceValueKind.WholeNumber, "tpm.tbs.device_info"),
        ("tpm.tbs.implementation_revision", EvidenceValueKind.WholeNumber, "tpm.tbs.device_info"),
        ("tpm.wmi.collection_status", EvidenceValueKind.Enum, "tpm.wmi.properties"),
        ("tpm.wmi.instance_present", EvidenceValueKind.Boolean, "tpm.wmi.properties"),
        ("tpm.wmi.spec_version", EvidenceValueKind.Text, "tpm.wmi.properties"),
        ("tpm.wmi.manufacturer_version", EvidenceValueKind.Text, "tpm.wmi.properties"),
        ("tpm.wmi.manufacturer_id", EvidenceValueKind.WholeNumber, "tpm.wmi.properties"),
        ("tpm.wmi.enabled_initial", EvidenceValueKind.Boolean, "tpm.wmi.properties"),
        ("tpm.wmi.activated_initial", EvidenceValueKind.Boolean, "tpm.wmi.properties"),
        ("tpm.wmi.owned_initial", EvidenceValueKind.Boolean, "tpm.wmi.properties"),
        ("tpm.wmi.native_error_code", EvidenceValueKind.WholeNumber, "tpm.wmi.properties"),
        ("tpm.ready", EvidenceValueKind.Boolean, "tpm.wmi.is_ready_information"),
        ("tpm.readiness.collection_status", EvidenceValueKind.Enum, "tpm.wmi.is_ready_information"),
        ("tpm.readiness.native_error_code", EvidenceValueKind.WholeNumber, "tpm.wmi.is_ready_information"),
        ("tpm.readiness.return_value", EvidenceValueKind.WholeNumber, "tpm.wmi.is_ready_information"),
        ("tpm.readiness.information", EvidenceValueKind.WholeNumber, "tpm.wmi.is_ready_information"),
        ("tpm.readiness.is_ready", EvidenceValueKind.Boolean, "tpm.wmi.is_ready_information"),
    ];
    private readonly IFirmwareSecuritySource _source;
    private int _inProgress;
    private int _cleanupBlocked;

    public FirmwareSecurityProvider() : this(new WindowsFirmwareSecuritySource(), ProviderEvidenceOrigin.ActiveMachine) { }

    internal FirmwareSecurityProvider(IFirmwareSecuritySource source, ProviderEvidenceOrigin origin)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        Descriptor = new(new(ProviderIdentity), ProviderPrivilegeRequirement.StandardUser,
            TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(15), ProviderNetworkUse.None, origin,
            [ProviderOperatingSystem.Windows11],
            new(new("firmware_security.provider_status"), "firmware_security.provider.status"),
            Fields.Select(static field => new ProviderEvidenceDefinition(new(field.Key), field.Kind, null, field.Source)));
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

            // The runner bounds caller waiting. The worker retains admission until all
            // synchronous native calls return and their native resources are released.
            var snapshot = await Task.Run(() => _source.Read(cancellationToken), cancellationToken).ConfigureAwait(false);
            if (!snapshot.DeviceGuard.CleanupComplete || !snapshot.Tpm.Wmi.CleanupComplete ||
                snapshot.Tpm.Wmi.CollectionInfo.Status == FirmwareCollectionStatus.CleanupIncomplete ||
                snapshot.Tpm.Wmi.Readiness.CollectionInfo.Status == FirmwareCollectionStatus.CleanupIncomplete)
            {
                throw new FirmwareSecurityCleanupException();
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Normalize(snapshot);
        }
        catch (FirmwareSecurityCleanupException)
        {
            Volatile.Write(ref _cleanupBlocked, 1);
            return ProviderReadResult.CleanupIncomplete();
        }
        finally { Volatile.Write(ref _inProgress, 0); }
    }

    private static ProviderReadResult Normalize(FirmwareSecuritySourceSnapshot snapshot)
    {
        var output = new EvidenceBuilder();
        var platform = snapshot.Platform;
        output.Raw("firmware.type_code", platform.FirmwareType, static value => EvidenceValue.FromInteger(value));
        output.Number("firmware.native_error_code", platform.FirmwareTypeNativeErrorCode);
        uint? firmware = Value(platform.FirmwareType);
        if (firmware is 1 or 2) { output.Set("firmware.mode", EvidenceValue.FromEnum(firmware == 1 ? "bios" : "uefi")); }
        else { output.Unknown("firmware.mode", firmware.HasValue); }

        output.Raw("secure_boot.registry_value", platform.SecureBootRegistryValue, static value => EvidenceValue.FromInteger(value));
        output.Number("secure_boot.native_error_code", platform.SecureBootNativeErrorCode);
        uint? enabled = Value(platform.SecureBootRegistryValue);
        if (enabled is 0 or 1) { output.Set("secure_boot.enabled", EvidenceValue.FromBoolean(enabled == 1)); }
        else { output.Unknown("secure_boot.enabled", enabled.HasValue); }

        var guard = snapshot.DeviceGuard;
        output.Number("secure_boot.device_guard_error_code", guard.NativeErrorCode);
        output.SourceStatus(guard.AvailableSecurityProperties.Status);
        bool? capable = null;
        if (guard.AvailableSecurityProperties.Status == SystemInventorySourceStatus.Available)
        {
            var codes = guard.AvailableSecurityProperties.GetRequiredValue();
            if (codes.Count > 16) { output.Unknown("secure_boot.available_properties", true); output.Unknown("secure_boot.capable", true); }
            else if (codes.Count != 0)
            {
                output.Set("secure_boot.available_properties", EvidenceValue.FromString(string.Join(",", codes.Select(static code => code.ToString(CultureInfo.InvariantCulture)))));
                if (codes.Distinct().Count() != codes.Count || codes.Any(static code => code > 8) || (codes.Contains(0U) && codes.Count != 1))
                { output.Unknown("secure_boot.capable", true); }
                else
                {
                    capable = codes.Contains(2U) ? true : codes.Count == 1 && codes[0] == 0 ? false : null;
                    if (capable.HasValue) { output.Set("secure_boot.capable", EvidenceValue.FromBoolean(capable.Value)); }
                }
            }
        }

        // Explicit reconciliation retains both raw sources and never selects by read order.
        if (enabled == 1 && (firmware == 1 || capable == false))
        {
            output.Conflict("secure_boot.enabled");
            if (capable == false) { output.Conflict("secure_boot.capable"); }
        }

        output.Raw("firmware.hardware_security_settings", platform.HardwareSecuritySettings, static value => EvidenceValue.FromInteger(value));
        if (platform.HardwareSecuritySettings.Status == SystemInventorySourceStatus.Available)
        {
            var settings = platform.HardwareSecuritySettings.GetRequiredValue();
            string[] keys = ["power_on", "keyboard", "administrator", "front_panel_reset"];
            for (var index = 0; index < keys.Length; index++)
            {
                var state = (settings >> (6 - index * 2)) & 3;
                if (state != 3) { output.Set($"firmware.password.{keys[index]}_state", EvidenceValue.FromEnum(state == 0 ? "disabled" : state == 1 ? "enabled" : "not_implemented")); }
            }

            if (((settings >> 6) & 3) == 1 || ((settings >> 2) & 3) == 1)
            { output.Set("ownership.firmware_lock_indicator", EvidenceValue.FromEnum("present"), inferred: true); }
        }

        NormalizeTpm(snapshot.Tpm, output);
        return output.Result();
    }

    private static void NormalizeTpm(TpmSourceSnapshot snapshot, EvidenceBuilder output)
    {
        var device = snapshot.Device;
        var wmi = snapshot.Wmi;
        var readiness = wmi.Readiness;
        output.Collection("tpm.tbs.collection_status", device.CollectionInfo);
        output.Number("tpm.tbs.return_code", device.ReturnCode);
        bool? present = null;
        string? version = null;
        if (device.CollectionInfo.Status == FirmwareCollectionStatus.Collected && device.ReturnCode == 0 && device.Value is { } info)
        {
            output.Number("tpm.tbs.struct_version", info.StructVersion);
            output.Number("tpm.tbs.version_code", info.TpmVersion);
            output.Number("tpm.tbs.interface_type", info.InterfaceType);
            output.Number("tpm.tbs.implementation_revision", info.ImplementationRevision);
            if (info.StructVersion is 1 or 2)
            {
                version = info.TpmVersion switch { 1 => "1.2", 2 => "2.0", _ => null };
                present = version is null ? null : true;
            }

            if (!present.HasValue) { output.Unknown("tpm.present", true); output.Unknown("tpm.version", true); }
        }
        else if (device.CollectionInfo.Status == FirmwareCollectionStatus.NotFound && device.ReturnCode == 0x8028400F && device.Value is null)
        { present = false; }
        else if (device.CollectionInfo.Status is FirmwareCollectionStatus.Collected or FirmwareCollectionStatus.NotFound)
        { output.Unknown("tpm.present", true); }

        if (present.HasValue) { output.Set("tpm.present", EvidenceValue.FromBoolean(present.Value)); }
        if (version is not null) { output.Set("tpm.version", EvidenceValue.FromString(version)); }
        output.Collection("tpm.wmi.collection_status", wmi.CollectionInfo);
        output.Number("tpm.wmi.native_error_code", wmi.CollectionInfo.NativeErrorCode);
        if (wmi.InstancePresent.HasValue && wmi.CollectionInfo.Status is FirmwareCollectionStatus.Collected or FirmwareCollectionStatus.NotFound)
        { output.Set("tpm.wmi.instance_present", EvidenceValue.FromBoolean(wmi.InstancePresent.Value)); }
        if (wmi.CollectionInfo.Status == FirmwareCollectionStatus.Collected && wmi.InstancePresent == true)
        {
            output.Version("tpm.wmi.spec_version", wmi.SpecVersion);
            output.Version("tpm.wmi.manufacturer_version", wmi.ManufacturerVersion);
            output.Raw("tpm.wmi.manufacturer_id", wmi.ManufacturerId, static value => EvidenceValue.FromInteger(value));
            output.Raw("tpm.wmi.enabled_initial", wmi.EnabledInitial, EvidenceValue.FromBoolean);
            output.Raw("tpm.wmi.activated_initial", wmi.ActivatedInitial, EvidenceValue.FromBoolean);
            output.Raw("tpm.wmi.owned_initial", wmi.OwnedInitial, EvidenceValue.FromBoolean);
        }

        output.Collection("tpm.readiness.collection_status", readiness.CollectionInfo);
        output.Number("tpm.readiness.native_error_code", readiness.CollectionInfo.NativeErrorCode);
        output.Raw("tpm.readiness.return_value", readiness.ReturnValue, static value => EvidenceValue.FromInteger(value));
        output.Raw("tpm.readiness.information", readiness.Information, static value => EvidenceValue.FromInteger(value));
        output.Raw("tpm.readiness.is_ready", readiness.IsReady, EvidenceValue.FromBoolean);
        if (wmi.CollectionInfo.Status == FirmwareCollectionStatus.Collected && wmi.InstancePresent == true &&
            readiness.CollectionInfo.Status == FirmwareCollectionStatus.Collected && Value(readiness.ReturnValue) == 0 &&
            readiness.Information.Status == SystemInventorySourceStatus.Available)
        {
            output.Raw("tpm.ready", readiness.IsReady, EvidenceValue.FromBoolean);
            // The documented 0x40/0x80 bits describe ownership taken and an EK that
            // exists. Other bits describe provisioning needs/errors or are unknown.
            // A positive readiness claim with those bits requires review; retain the
            // complete raw boolean and mask without prescribing any TPM operation.
            if (readiness.IsReady.Status == SystemInventorySourceStatus.Available && readiness.IsReady.GetRequiredValue() &&
                (readiness.Information.GetRequiredValue() & ~0xC0U) != 0)
            { output.Conflict("tpm.ready"); }
        }
        else if (readiness.CollectionInfo.Status == FirmwareCollectionStatus.Collected)
        { output.Unknown("tpm.ready", true); }

        if (present == false && wmi.InstancePresent == true)
        {
            output.Conflict("tpm.present"); output.Conflict("tpm.version"); output.Conflict("tpm.ready");
        }
        if (version is not null && output.Text("tpm.wmi.spec_version") is { } spec)
        {
            var parts = spec.Split(',');
            var reported = parts[0].Trim();
            if (parts.Length != 3 || parts.Any(static part => part.Trim().Length == 0 ||
                part.Trim().Split('.').Any(static component => component.Length == 0 || !component.All(char.IsAsciiHexDigit))) ||
                reported is not "1.2" and not "2.0") { output.Unknown("tpm.version", true); }
            else if (reported != version) { output.Conflict("tpm.version"); }
        }
    }

    private static uint? Value(SystemInventorySourceValue<uint> source) =>
        source.Status == SystemInventorySourceStatus.Available ? source.GetRequiredValue() : null;

    private sealed class EvidenceBuilder
    {
        private readonly Dictionary<string, NormalizedEvidence> _values = Fields.ToDictionary(static field => field.Key,
            static field => NormalizedEvidence.NotAvailable(new(field.Key), ProviderLimitation.SourceValueMissing, field.Source));
        private bool _invalid;
        private bool _denied;
        private bool _failed;
        private bool _unavailable;

        public void Set(string key, EvidenceValue value, bool inferred = false)
        {
            var source = _values[key].SourceReference;
            _values[key] = inferred ? NormalizedEvidence.Inferred(new(key), value, sourceReference: source) :
                NormalizedEvidence.Observed(new(key), value, sourceReference: source);
        }

        public void Unknown(string key, bool malformed)
        {
            _invalid |= malformed;
            _values[key] = NormalizedEvidence.NotAvailable(new(key), malformed ? ProviderLimitation.SourceValueMalformed : ProviderLimitation.SourceValueMissing, _values[key].SourceReference);
        }

        public void Conflict(string key) => _values[key] = NormalizedEvidence.NotAvailable(new(key), ProviderLimitation.SourceValuesConflict, _values[key].SourceReference);
        public void Number(string key, uint? value) { if (value.HasValue) { Set(key, EvidenceValue.FromInteger(value.Value)); } }
        public string? Text(string key) => _values[key].Value?.StringValue;
        public void SourceStatus(SystemInventorySourceStatus status)
        {
            _invalid |= status == SystemInventorySourceStatus.InvalidData;
            _denied |= status == SystemInventorySourceStatus.AccessDenied;
            _unavailable |= status == SystemInventorySourceStatus.Unavailable;
        }

        public void Raw<T>(string key, SystemInventorySourceValue<T> value, Func<T, EvidenceValue> convert) where T : notnull
        {
            SourceStatus(value.Status);
            if (value.Status == SystemInventorySourceStatus.Available) { Set(key, convert(value.GetRequiredValue())); }
            else { Unknown(key, value.Status == SystemInventorySourceStatus.InvalidData); }
        }

        public void Version(string key, SystemInventorySourceValue<string> value)
        {
            if (value.Status != SystemInventorySourceStatus.Available) { Raw(key, value, EvidenceValue.FromString); return; }
            var text = value.GetRequiredValue();
            if (text.Length is 0 or > FirmwareWmiValueReader.MaximumVersionCharacters || !text.Any(char.IsAsciiDigit) ||
                !text.All(static character => char.IsAsciiHexDigit(character) || character is '.' or ',' or ' '))
            { Unknown(key, true); return; }
            Set(key, EvidenceValue.FromString(text));
        }

        public void Collection(string key, FirmwareCollectionInfo info)
        {
            if (!Enum.IsDefined(info.Status)) { Unknown(key, true); return; }
            Set(key, EvidenceValue.FromEnum(info.Status.ToString().ToLowerInvariant()));
            _invalid |= info.Status == FirmwareCollectionStatus.InvalidData;
            _denied |= info.Status == FirmwareCollectionStatus.AccessDenied;
            _failed |= info.Status == FirmwareCollectionStatus.Failed;
            _unavailable |= info.Status is FirmwareCollectionStatus.Unavailable or FirmwareCollectionStatus.TimedOut;
        }

        public ProviderReadResult Result() => _invalid ? ProviderReadResult.InvalidData(_values.Values) :
            _denied ? ProviderReadResult.AccessDenied(_values.Values) : _failed ? ProviderReadResult.Failed(_values.Values) :
            _unavailable ? ProviderReadResult.Unavailable(_values.Values) : ProviderReadResult.Collected(_values.Values);
    }
}
