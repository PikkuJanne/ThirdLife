using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using ThirdLife.Actions.Contracts;
using ThirdLife.Broker.Protocol;
using ThirdLife.Broker.Protocol.Transport;

namespace ThirdLife.Broker.WindowsUpdates;

internal static class WindowsUpdateQualificationGrant
{
    private const string Header = "TL-WUA-GRANT-1";
    private const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
    private const int RecordLength = 14 + 64 + 36 + 64 + 64 + 28 + 28 + 7;
    private static readonly TimeSpan MaximumValidity = TimeSpan.FromMinutes(30);

    // Only the supervised qualification authority provisions these two protected slots.
    // The broker never creates a grant or takes an authority path or principal from the pipe.
    internal static bool TryConsume(BrokerWindowsUpdateRequest request, SecurityIdentifier logonSid, Func<DateTimeOffset, bool> authorize)
    {
        var guards = new List<SafeFileHandle>();
        try
        {
            if (!ProtectedBrokerFiles.OpenDirectory(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.SystemDirectory, "ServicingGrants", guards, out var directory))
            {
                return false;
            }
            var name = request.Action.Kind switch
            {
                ActionKind.DownloadWindowsUpdate => "download.grant",
                ActionKind.InstallWindowsUpdate => "install.grant",
                _ => throw new InvalidOperationException(),
            };
            if (!ProtectedBrokerFiles.OpenGuard(Path.Combine(directory, name), false,
                ProtectedBrokerFiles.ObjectKind.QualificationGrant, guards, out _, consume: true))
            {
                return false;
            }

            return ConsumeGuardedFile(guards[^1], request, logonSid, authorize);
        }
        finally
        {
            ProtectedBrokerFiles.CloseGuards(guards);
        }
    }

    internal static bool ConsumeGuardedFile(SafeFileHandle handle, BrokerWindowsUpdateRequest request,
        SecurityIdentifier logonSid, Func<DateTimeOffset, bool> authorize)
    {
        using var file = ProtectedBrokerFiles.ReadGuardedFile(handle);
        if (file.Length != RecordLength) { return false; }
        var record = new byte[RecordLength];
        file.ReadExactly(record);
        var now = TimeProvider.System.GetUtcNow();
        if (!Matches(record, request, logonSid, now) || !authorize(now)) { return false; }
        var finalTime = TimeProvider.System.GetUtcNow();
        if (finalTime < now || !BrokerProtocolLimits.IsWithinWindow(request.Envelope.ExpiresAtUtc, finalTime) ||
            !Matches(record, request, logonSid, finalTime)) { return false; }

        // One exclusive handle pins data, identity, ACL and deletion. No second broker
        // can claim the slot, including after a process dies between this point and Begin.
        byte delete = 1;
        return NativeMethods.SetFileInformationByHandle(handle, 4, ref delete, sizeof(byte));
    }

    internal static bool Matches(ReadOnlySpan<byte> record, BrokerWindowsUpdateRequest request, SecurityIdentifier logonSid, DateTimeOffset now)
    {
        if (record.Length != RecordLength) { return false; }
        var fields = Encoding.ASCII.GetString(record).Split('\n');
        var subject = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(logonSid.Value)));
        return fields.Length == 8 && fields[0] == Header && fields[1] == request.ApprovalSha256 &&
            fields[2] == request.Action.ActionId.ToString("D") && fields[3].Length == 64 && fields[3].All(char.IsAsciiHexDigitLower) &&
            fields[3].Any(c => c != '0') && fields[4] == subject && TryReadUtc(fields[5], out var issued) && TryReadUtc(fields[6], out var expires) &&
            fields[7].Length == 0 && issued <= now && expires > now && expires - issued <= MaximumValidity;
    }

    private static bool TryReadUtc(string text, out DateTimeOffset time) =>
        DateTimeOffset.TryParseExact(text, UtcFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out time) &&
        text == time.ToString(UtcFormat, CultureInfo.InvariantCulture);

    private static class NativeMethods
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref byte value, uint length);
    }
}
