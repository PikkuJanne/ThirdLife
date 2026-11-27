using System.Collections.Frozen;
using ThirdLife.Core.Evidence;

namespace ThirdLife.Reports.WorkshopReport;

/// <summary>Closed workshop field projection. Other audiences own their own projections.</summary>
internal static class WorkshopObservationFields
{
    private static readonly FrozenDictionary<string, Field> Fields = BuildFields();

    internal static bool Allows(Observation observation)
    {
        var key = observation.EvidenceKey.Value;
        var metadata = observation.Metadata;
        if (Fields.TryGetValue(key, out var field) &&
            metadata.ProviderId.Value == field.Provider &&
            (observation.Value is null || observation.Value.Kind == field.Kind))
        {
            return true;
        }

        return metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture &&
            AllowsSyntheticObservation(key, metadata.ProviderId.Value, observation.Value?.Kind);
    }

    private static bool AllowsSyntheticObservation(string key, string provider, EvidenceValueKind? kind)
    {
        if (!provider.StartsWith("fixture-", StringComparison.Ordinal))
        {
            return false;
        }

        var name = provider["fixture-".Length..];
        if (name.Length is 0 or > 40 ||
            !name.All(static character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '-'))
        {
            return false;
        }

        return (key == $"test.{name}.value" && kind is null or EvidenceValueKind.Boolean) ||
            (key == $"test.{name}.failure" && kind is null);
    }

    private static FrozenDictionary<string, Field> BuildFields()
    {
        var fields = new Dictionary<string, Field>(StringComparer.Ordinal);

        Add("windows-system-inventory", EvidenceValueKind.Text,
            "device.manufacturer", "device.model", "device.serial_number", "processor.manufacturer", "processor.model");
        Add("windows-system-inventory", EvidenceValueKind.Enum,
            "device.type", "system.architecture", "system_inventory.provider_status");
        Add("windows-system-inventory", EvidenceValueKind.WholeNumber, "memory.installed_bytes", "processor.logical_count");

        Add("windows-operating-system", EvidenceValueKind.Text,
            "os.version", "os.lifecycle.source", "os.lifecycle.revision", "os.lifecycle.as_of", "os.lifecycle.fresh_through",
            "os.lifecycle.evaluated_on", "os.lifecycle.release");
        Add("windows-operating-system", EvidenceValueKind.Enum,
            "os.edition", "os.architecture", "os.support_target", "os.family", "os.lifecycle.reason", "os.provider_status");
        Add("windows-operating-system", EvidenceValueKind.WholeNumber, "os.build", "os.edition_code");
        Add("windows-security-baseline", EvidenceValueKind.Enum, "security.firewall_health", "security.antivirus_health",
            "security.uac_health", "security.uac_configured", "security.uac_state", "security.remote_desktop_configured",
            "security.ntp_client_configured", "ownership.domain_join", "ownership.mdm_registration", "ownership.cloud_join",
            "ownership.lock_state", "security.baseline_provider_status", "security.defender_health",
            "security.system_volume_protection");
        Add("windows-security-baseline", EvidenceValueKind.WholeNumber, "security.last_successful_sync_utc");
        Add("windows-activation", EvidenceValueKind.Enum, "activation.state", "activation.provider_status");
        Add("windows-pending-reboot", EvidenceValueKind.Enum, "reboot.pending_status", "reboot.provider_status");
        foreach (var source in new[] { "cbs", "windows_update", "file_rename" })
        {
            Add("windows-pending-reboot", EvidenceValueKind.Enum,
                $"reboot.{source}.indicator", $"reboot.{source}.confidence", $"reboot.{source}.collection_status",
                $"reboot.{source}.recovery");
        }

        Add("windows-system-disk-health", EvidenceValueKind.Enum, "storage.system.health_state", "storage.system.health_provider_status");
        Add("windows-storage", EvidenceValueKind.Enum,
            "storage.provider_status", "storage.physical_disk.media_type", "storage.physical_disk.bus_type",
            "storage.physical_disk.reported_health", "storage.physical_disk.collection_status",
            "storage.physical_disk.reliability_collection_status");
        Add("windows-storage", EvidenceValueKind.WholeNumber,
            "storage.physical_disk_count", "storage.physical_disk.capacity_bytes", "storage.physical_disk.wear",
            "storage.physical_disk.temperature", "storage.physical_disk.read_errors_total",
            "storage.physical_disk.read_errors_uncorrected", "storage.physical_disk.write_errors_total",
            "storage.physical_disk.write_errors_uncorrected", "storage.physical_disk.power_on_hours",
            "storage.physical_disk.native_error_code", "storage.physical_disk.reliability_native_error_code",
            "storage.system_volume.caller_capacity_bytes", "storage.system_volume.caller_free_bytes",
            "storage.system_volume.total_free_bytes", "storage.system_volume.native_error_code");
        Add("windows-storage", EvidenceValueKind.Text, "storage.physical_disk.operational_status_codes");

        Add("windows-battery-report", EvidenceValueKind.Boolean, "battery.present", "battery.report.cleanup_complete");
        Add("windows-battery-report", EvidenceValueKind.Enum, "battery.report.collection_status", "battery.provider_status");
        Add("windows-battery-report", EvidenceValueKind.WholeNumber,
            "battery.count", "battery.pack.design_capacity_mwh", "battery.pack.full_charge_capacity_mwh",
            "battery.pack.cycle_count", "battery.report.native_error_code", "battery.report.exit_code");
        Add("windows-battery-report", EvidenceValueKind.DecimalNumber, "battery.pack.full_charge_ratio_percent");

        Add("windows-device-enumeration", EvidenceValueKind.Enum, "devices.functional_test", "devices.provider_status");
        foreach (var (device, presence) in new[]
        {
            ("wifi", "present"), ("ethernet", "present"), ("bluetooth", "radio_interface_present"),
            ("audio", "render_endpoint_present"), ("camera", "video_interface_present"),
            ("microphone", "capture_endpoint_present"), ("display", "monitor_present"), ("keyboard", "present"),
            ("pointing", "present"), ("touch", "session_present"), ("usb_controller", "host_interface_present"),
        })
        {
            Add("windows-device-enumeration", EvidenceValueKind.Boolean, $"devices.{device}.{presence}");
            Add("windows-device-enumeration", EvidenceValueKind.Enum, $"devices.{device}.collection_status");
            Add("windows-device-enumeration", EvidenceValueKind.WholeNumber,
                $"devices.{device}.problem_count", $"devices.{device}.native_error_code");
        }

        Add("windows-firmware-security", EvidenceValueKind.Enum,
            "firmware.mode", "firmware.password.power_on_state", "firmware.password.keyboard_state",
            "firmware.password.administrator_state", "firmware.password.front_panel_reset_state",
            "ownership.firmware_lock_indicator", "tpm.tbs.collection_status", "tpm.wmi.collection_status",
            "tpm.readiness.collection_status", "firmware_security.provider_status");
        Add("windows-firmware-security", EvidenceValueKind.Boolean,
            "secure_boot.capable", "secure_boot.enabled", "tpm.present", "tpm.wmi.instance_present",
            "tpm.wmi.enabled_initial", "tpm.wmi.activated_initial", "tpm.wmi.owned_initial", "tpm.ready",
            "tpm.readiness.is_ready");
        Add("windows-firmware-security", EvidenceValueKind.Text,
            "secure_boot.available_properties", "tpm.version", "tpm.wmi.spec_version", "tpm.wmi.manufacturer_version");
        Add("windows-firmware-security", EvidenceValueKind.WholeNumber,
            "firmware.type_code", "firmware.native_error_code", "firmware.hardware_security_settings",
            "secure_boot.registry_value", "secure_boot.native_error_code", "secure_boot.device_guard_error_code",
            "tpm.tbs.return_code", "tpm.tbs.struct_version", "tpm.tbs.version_code", "tpm.tbs.interface_type",
            "tpm.tbs.implementation_revision", "tpm.wmi.manufacturer_id", "tpm.wmi.native_error_code",
            "tpm.readiness.native_error_code", "tpm.readiness.return_value", "tpm.readiness.information");

        return fields.ToFrozenDictionary(StringComparer.Ordinal);

        void Add(string provider, EvidenceValueKind kind, params string[] keys)
        {
            foreach (var key in keys)
            {
                fields.Add(key, new Field(provider, kind));
            }
        }
    }

    private readonly record struct Field(string Provider, EvidenceValueKind Kind);
}
