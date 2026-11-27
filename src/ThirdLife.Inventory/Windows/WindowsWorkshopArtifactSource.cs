using System.IO.Enumeration;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32.SafeHandles;
using Kind = ThirdLife.Core.Finalization.WorkshopArtifactKind;
using Status = ThirdLife.Core.Finalization.WorkshopArtifactStatus;

namespace ThirdLife.Inventory.Windows;

internal static class WindowsWorkshopArtifactSource
{
    internal const int MaximumDirectoryEntries = 512;
    private const int MaximumPathComponents = 32;

    internal static Status Read(Kind kind, string? exactName, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            WindowsPlatform.CheckCurrent() != WindowsPlatformEligibility.Supported)
        {
            return Status.Unavailable;
        }

        try
        {
            return kind switch
            {
                Kind.BatteryReportResidue or Kind.WorkshopReportCopies or Kind.PackageStagingResidue =>
                    InspectRegisteredPath(BatteryReportFiles.LocalApplicationData(), kind, token),
                Kind.ConfiguredLocalAccounts or Kind.ConfiguredSmbShares => ReadExactLocalName(kind, exactName!),
                Kind.RemoteDesktopSetting or Kind.SolicitedRemoteAssistancePolicy or Kind.OfferRemoteAssistancePolicy =>
                    ReadRemoteSupportSetting(kind, token),
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }
        catch (BatteryReportNativeException failure) when (failure.NativeErrorCode == 5)
        {
            return Status.AccessDenied;
        }
        catch (Exception failure) when (failure is BatteryReportNativeException or BatteryReportWorkspaceException or
            DllNotFoundException or EntryPointNotFoundException)
        {
            return Status.Unavailable;
        }
    }

    internal static Status InspectRegisteredPath(string? localData, Kind kind, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var guards = new List<SafeFileHandle>();
        try
        {
            if (string.IsNullOrWhiteSpace(localData) || !WindowsStorageVolumeSource.IsLocalAbsolutePath(localData) || localData.Length <= 3 ||
                localData.AsSpan(2).Contains(':') || localData.Contains('/') || !BatteryReportFiles.IsFixedDrive(localData[..3]))
            {
                return Status.UnsafePathOrEntry;
            }

            var profile = Path.TrimEndingDirectorySeparator(localData);
            if (!string.Equals(Path.GetFullPath(profile), profile, StringComparison.OrdinalIgnoreCase))
            {
                return Status.UnsafePathOrEntry;
            }

            var root = kind switch
            {
                Kind.BatteryReportResidue => BatteryReportWorkspace.FromKnownFolderBase(profile),
                Kind.WorkshopReportCopies => Path.Combine(profile, "ThirdLife", "SetupCore", "WorkshopReports"),
                Kind.PackageStagingResidue => profile,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };

            var parents = new Stack<string>();
            for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
            {
                if (parents.Count >= MaximumPathComponents)
                {
                    return Status.UnsafePathOrEntry;
                }

                parents.Push(directory.FullName);
            }

            var profileGuarded = false;
            foreach (var directory in parents)
            {
                token.ThrowIfCancellationRequested();
                SafeFileHandle guard;
                try
                {
                    guard = BatteryReportFiles.OpenDirectoryGuard(directory);
                }
                catch (BatteryReportNativeException failure) when (failure.NativeErrorCode is 2 or 3)
                {
                    return profileGuarded ? Status.AbsentInCheckedScope : Status.Unavailable;
                }

                guards.Add(guard);
                if (!IsExpectedEntry(guard, directory, directory: true))
                {
                    return Status.UnsafePathOrEntry;
                }

                profileGuarded |= string.Equals(directory, profile, StringComparison.OrdinalIgnoreCase);
            }

            return SearchNames(root, kind, token);
        }
        catch (UnauthorizedAccessException)
        {
            return Status.AccessDenied;
        }
        catch (BatteryReportNativeException failure) when (failure.NativeErrorCode == 5)
        {
            return Status.AccessDenied;
        }
        catch (Exception failure) when (failure is IOException or SecurityException or NotSupportedException or
            BatteryReportNativeException)
        {
            return Status.Unavailable;
        }
        catch (Exception failure) when (failure is ArgumentException or BatteryReportWorkspaceException)
        {
            return Status.UnsafePathOrEntry;
        }
        finally
        {
            for (var index = guards.Count - 1; index >= 0; index--)
            {
                guards[index].Dispose();
            }
        }
    }

    private static Status SearchNames(string root, Kind kind, CancellationToken token)
    {
        var visited = 0;
        var entries = new FileSystemEnumerable<string?>(root,
            (ref FileSystemEntry entry) => visited <= MaximumDirectoryEntries ? entry.ToFullPath() : null,
            new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false })
        {
            // Count every entry, including names outside the allowlist. Never materialize a nonmatching name.
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
            {
                token.ThrowIfCancellationRequested();
                return ++visited > MaximumDirectoryEntries || Matches(kind, entry.FileName);
            },
        };

        foreach (var path in entries)
        {
            if (path is null)
            {
                return Status.ScanLimitReached;
            }

            using var guard = BatteryReportFiles.OpenDirectoryGuard(path);
            return IsExpectedEntry(guard, path, kind == Kind.PackageStagingResidue)
                ? Status.Present : Status.UnsafePathOrEntry;
        }

        return Status.AbsentInCheckedScope;
    }

    private static bool IsExpectedEntry(SafeFileHandle guard, string path, bool directory)
    {
        var information = BatteryReportFiles.Information(guard);
        const FileAttributes mask = FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.Device;
        return (information.Attributes & mask) == (directory ? FileAttributes.Directory : 0) &&
            (directory || information.Links == 1) &&
            string.Equals(BatteryReportFiles.FinalPath(guard), path, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Matches(Kind kind, ReadOnlySpan<char> name) => kind switch
    {
        Kind.BatteryReportResidue => name.Equals(BatteryReportWorkspace.OwnershipName, StringComparison.OrdinalIgnoreCase) ||
            MatchesGeneratedName(name, "report-", ".xml"),
        Kind.WorkshopReportCopies => MatchesGeneratedName(name, "workshop-", ".txt") ||
            MatchesGeneratedName(name, ".workshop-", ".tmp"),
        Kind.PackageStagingResidue => MatchesGeneratedName(name, "ThirdLife-package-", ""),
        _ => false,
    };

    private static bool MatchesGeneratedName(ReadOnlySpan<char> name, string prefix, string suffix) =>
        name.Length == prefix.Length + 32 + suffix.Length && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
        name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && Guid.TryParseExact(name.Slice(prefix.Length, 32), "N", out _);

    private static Status ReadExactLocalName(Kind kind, string name)
    {
        // Level zero returns only the matched name. Release the buffer without reading its contents.
        var account = kind == Kind.ConfiguredLocalAccounts;
        var result = account ? Native.NetUserGetInfo(0, name, 0, out var buffer) : Native.NetShareGetInfo(0, name, 0, out buffer);
        if (!WindowsSecurityBaselineSource.ReleaseNetApiBuffer(buffer))
        {
            return Status.Unavailable;
        }

        return result switch
        {
            0 when buffer != 0 => Status.Present,
            2221 when account => Status.AbsentInCheckedScope, // NERR_UserNotFound
            2310 when !account => Status.AbsentInCheckedScope, // NERR_NetNameNotFound
            5 => Status.AccessDenied,
            _ => Status.Unavailable,
        };
    }

    private static Status ReadRemoteSupportSetting(Kind kind, CancellationToken token)
    {
        const string assistancePolicy = @"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services";
        const string enabled = "allowed";
        const string disabled = "denied";
        var reading = kind switch
        {
            Kind.RemoteDesktopSetting => WindowsSecurityBaselineSource.Read(SecurityCheck.RemoteDesktopConnections, token),
            Kind.SolicitedRemoteAssistancePolicy => WindowsSecurityBaselineSource.ReadSwitch(assistancePolicy, "fAllowToGetHelp", disabled, enabled),
            Kind.OfferRemoteAssistancePolicy => WindowsSecurityBaselineSource.ReadSwitch(assistancePolicy, "fAllowUnsolicited", disabled, enabled),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        return reading.Status switch
        {
            SystemInventorySourceStatus.Available when reading.Value == enabled => Status.Present,
            SystemInventorySourceStatus.Available when reading.Value == disabled => Status.AbsentInCheckedScope,
            SystemInventorySourceStatus.AccessDenied => Status.AccessDenied,
            _ => Status.Unavailable,
        };
    }

    private static class Native
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("netapi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal static extern int NetUserGetInfo(nint server, string username, uint level, out nint buffer);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("netapi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal static extern int NetShareGetInfo(nint server, string share, uint level, out nint buffer);

    }
}
