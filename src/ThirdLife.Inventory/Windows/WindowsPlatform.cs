namespace ThirdLife.Inventory.Windows;

public enum WindowsPlatformEligibility
{
    UnverifiedIdentity = 0,
    Supported,
    UnsupportedOperatingSystem,
    UnsupportedArchitecture,
}

internal sealed record WindowsPlatformIdentity(
    WindowsPlatformEligibility Eligibility,
    string? Family,
    WindowsLifecycleLimitation Limitation = WindowsLifecycleLimitation.UnverifiedIdentity);

/// <summary>Checks the native execution platform independently of Windows servicing eligibility.</summary>
public static class WindowsPlatform
{
    internal const int MinimumWindows11Build = 22000;

    public static WindowsPlatformEligibility CheckCurrent() => OperatingSystem.IsWindows()
        ? Evaluate(new WindowsOperatingSystemSource().Read(CancellationToken.None)).Eligibility
        : WindowsPlatformEligibility.UnsupportedOperatingSystem;

    internal static WindowsPlatformIdentity Evaluate(OperatingSystemSnapshot snapshot)
    {
        if (snapshot.Version.Status != SystemInventorySourceStatus.Available ||
            snapshot.ProductType.Status != SystemInventorySourceStatus.Available)
        {
            return new(WindowsPlatformEligibility.UnverifiedIdentity, null);
        }

        var version = snapshot.Version.GetRequiredValue();
        var type = snapshot.ProductType.GetRequiredValue();
        uint? edition = snapshot.Edition.Status == SystemInventorySourceStatus.Available
            ? snapshot.Edition.GetRequiredValue() : null;
        if (!IsValidVersion(version))
        {
            return new(WindowsPlatformEligibility.UnverifiedIdentity, null);
        }

        if (type is WindowsProductType.Server or WindowsProductType.DomainController)
        {
            // A reviewed client SKU cannot establish a coherent identity alongside a server role.
            return edition.HasValue && WindowsLifecycleCatalog.GetEditionName(edition.Value) is not null
                ? new(WindowsPlatformEligibility.UnverifiedIdentity, null)
                : new(WindowsPlatformEligibility.UnsupportedOperatingSystem, "windows_server");
        }

        // The current Standard, Datacenter, evaluation, and Azure Server SKUs
        // conflict with a workstation role. Other SKUs do not grant client identity.
        if (type != WindowsProductType.Workstation || edition is 0x07 or 0x08 or 0x4F or 0x50 or 0x197)
        {
            return new(WindowsPlatformEligibility.UnverifiedIdentity, null);
        }

        if (version is { Major: 10, Minor: 0, Build: >= 10240 and < MinimumWindows11Build })
        {
            var family = version.Build is 10240 or 10586 or 14393 or 15063 or 16299 or 17134 or 17763 or
                18362 or 18363 or 19041 or 19042 or 19043 or 19044 or 19045 ? "windows_10" : "windows_other";
            return new(WindowsPlatformEligibility.UnsupportedOperatingSystem, family);
        }

        if (version is { Major: 5 or 6, Build: < 10240 })
        {
            return new(WindowsPlatformEligibility.UnsupportedOperatingSystem, "windows_other");
        }

        if (version is not { Major: 10, Minor: 0, Build: >= MinimumWindows11Build })
        {
            return new(WindowsPlatformEligibility.UnverifiedIdentity, null);
        }

        if (!WindowsLifecycleCatalog.Current.RecognizesWindows11Build((uint)version.Build))
        {
            return new(WindowsPlatformEligibility.UnverifiedIdentity, null, WindowsLifecycleLimitation.UnrecognizedBuild);
        }

        var eligibility = GetArchitecture(snapshot.Architecture) switch
        {
            "x64" => WindowsPlatformEligibility.Supported,
            null => WindowsPlatformEligibility.UnverifiedIdentity,
            _ => WindowsPlatformEligibility.UnsupportedArchitecture,
        };
        return new(eligibility, "windows_11");
    }

    internal static bool IsValidVersion(OperatingSystemVersion? version) =>
        version is { Major: >= 0 and <= 100, Minor: >= 0 and <= 100, Build: > 0 and <= 1_000_000 };

    internal static string? GetArchitecture(SystemInventorySourceValue<ushort> architecture) =>
        architecture.Status == SystemInventorySourceStatus.Available
            ? architecture.GetRequiredValue() switch
            {
                0 => "x86",
                5 => "arm32",
                6 => "ia64",
                9 => "x64",
                12 => "arm64",
                _ => null,
            }
            : null;
}
