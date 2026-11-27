using System.Globalization;
using System.Text.Json;

namespace ThirdLife.Diagnostics.Redaction;

public enum SupportStorageKind { Hdd = 1, Ssd, Nvme, Emmc }

public enum SupportCheckId
{
    OperatingSystem = 1,
    Activation,
    WindowsUpdate,
    PendingRestart,
    SystemDiskHealth,
    SecurityBaseline,
    EssentialCapabilities,
    WorkshopFinalization,
}

public enum SupportCheckOutcome { Passed = 1, Failed, Unknown, NotAvailable }

public sealed record SupportCheck(SupportCheckId Check, SupportCheckOutcome Outcome);

/// <summary>Reconstructs support values from bounded facts instead of passing through provider text.</summary>
public sealed record SupportMachineFacts(int? WindowsBuild, bool ObservedNativeX64, long? InstalledMemoryBytes,
    SupportStorageKind? StorageKind)
{
    internal void Write(Utf8JsonWriter writer)
    {
        if (WindowsBuild is >= 22_000 and <= 99_999)
        {
            writer.WriteString("os_version", string.Create(CultureInfo.InvariantCulture, $"10.0.{WindowsBuild}"));
        }

        if (ObservedNativeX64)
        {
            writer.WriteString("hardware_architecture", "x64");
        }

        if (InstalledMemoryBytes is >= 1L << 30 and <= 1L << 40)
        {
            writer.WriteString("memory_bucket", InstalledMemoryBytes.Value switch
            {
                < 4L << 30 => "1-3 GiB",
                < 8L << 30 => "4-7 GiB",
                < 16L << 30 => "8-15 GiB",
                < 32L << 30 => "16-31 GiB",
                _ => "32 GiB or more",
            });
        }

        var storage = StorageKind switch
        {
            null => null,
            SupportStorageKind.Hdd => "hdd",
            SupportStorageKind.Ssd => "ssd",
            SupportStorageKind.Nvme => "nvme",
            SupportStorageKind.Emmc => "emmc",
            _ => throw new DiagnosticContractException("support_storage_value_unregistered", "The storage class is not registered."),
        };
        if (storage is not null)
        {
            writer.WriteString("storage_class", storage);
        }
    }
}
