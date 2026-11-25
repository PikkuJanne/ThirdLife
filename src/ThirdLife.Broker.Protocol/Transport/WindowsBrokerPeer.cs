using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace ThirdLife.Broker.Protocol.Transport;

/// <summary>Operating-system identity of one broker pipe endpoint.</summary>
[SupportedOSPlatform("windows")]
public sealed class BrokerPeerIdentity
{
    internal BrokerPeerIdentity(SecurityIdentifier userSid, SecurityIdentifier logonSid, int sessionId, bool isElevated)
    {
        UserSid = userSid;
        LogonSid = logonSid;
        SessionId = sessionId;
        IsElevated = isElevated;
    }

    public SecurityIdentifier UserSid { get; }
    public SecurityIdentifier LogonSid { get; }
    public int SessionId { get; }
    public bool IsElevated { get; }
}

/// <summary>Pins a process and records the identity of its Windows primary token.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsBrokerPeer : IDisposable
{
    private const int ProcessQueryLimitedInformation = 0x1000;
    private const int Synchronize = 0x100000;
    private const int WaitTimeout = 258;
    private const int TokenQuery = 0x0008;
    private const int ErrorInsufficientBuffer = 122;
    private const int MaximumTokenInformationBytes = 65_536;
    private const uint LogonGroupAttributes = 0xc0000000;
    private const int SecurityIdentification = 1;
    private const int SidHeaderBytes = 8;
    private const int MaximumSidSubAuthorities = 15;
    private const string InvalidPeer = "The Windows broker peer could not be authenticated.";

    private readonly SafeProcessHandle _process;

    private WindowsBrokerPeer(SafeProcessHandle process, int processId, BrokerPeerIdentity identity)
    {
        _process = process;
        ProcessId = processId;
        Identity = identity;
    }

    public int ProcessId { get; }
    public BrokerPeerIdentity Identity { get; }
    public bool IsAlive => !_process.IsClosed && NativeMethods.WaitForSingleObject(_process, 0) == WaitTimeout;

    public static WindowsBrokerPeer Open(int processId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        var process = NativeMethods.OpenProcess(ProcessQueryLimitedInformation | Synchronize, false, processId);
        try
        {
            if (process.IsInvalid || NativeMethods.WaitForSingleObject(process, 0) != WaitTimeout ||
                !NativeMethods.OpenProcessToken(process, TokenQuery, out var token))
            {
                throw new UnauthorizedAccessException(InvalidPeer);
            }

            using (token)
            {
                var identity = ReadIdentity(token);
                if (NativeMethods.WaitForSingleObject(process, 0) != WaitTimeout)
                {
                    throw new UnauthorizedAccessException(InvalidPeer);
                }

                return new WindowsBrokerPeer(process, processId, identity);
            }
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public static BrokerPeerIdentity CurrentIdentity()
    {
        using var peer = Open(Environment.ProcessId);
        return peer.Identity;
    }

    public void Dispose() => _process.Dispose();

    internal static bool IsLogonSid(SecurityIdentifier sid) =>
        sid.BinaryLength == SidHeaderBytes + 3 * sizeof(int) && sid.Value.StartsWith("S-1-5-5-", StringComparison.Ordinal);

    internal static BrokerPeerIdentity ReadIdentificationToken()
    {
        if (!NativeMethods.OpenThreadToken(NativeMethods.GetCurrentThread(), TokenQuery, true, out var token))
        {
            throw new UnauthorizedAccessException(InvalidPeer);
        }

        using (token)
        {
            if (ReadTokenInteger(token, TokenInformationClass.ImpersonationLevel) != SecurityIdentification)
            {
                throw new UnauthorizedAccessException(InvalidPeer);
            }

            return ReadIdentity(token);
        }
    }

    private static BrokerPeerIdentity ReadIdentity(SafeAccessTokenHandle token)
    {
        var userSid = ReadTokenInformation(token, TokenInformationClass.User, static (buffer, length) =>
            length >= IntPtr.Size ? ReadSid(buffer, length, Marshal.ReadIntPtr(buffer)) : throw new UnauthorizedAccessException(InvalidPeer));
        var logonSid = ReadTokenInformation(token, TokenInformationClass.Groups, ReadLogonSid);
        var sessionId = ReadTokenInteger(token, TokenInformationClass.SessionId);
        var elevation = ReadTokenInteger(token, TokenInformationClass.Elevation);
        if (sessionId < 0 || elevation is not (0 or 1))
        {
            throw new UnauthorizedAccessException(InvalidPeer);
        }

        return new BrokerPeerIdentity(userSid, logonSid, sessionId, isElevated: elevation == 1);
    }

    private static SecurityIdentifier ReadLogonSid(nint buffer, int length)
    {
        var firstGroup = IntPtr.Size;
        var groupBytes = 2 * IntPtr.Size;
        if (length < firstGroup)
        {
            throw new UnauthorizedAccessException(InvalidPeer);
        }

        var groupCount = Marshal.ReadInt32(buffer);
        if (groupCount < 1 || groupCount > (length - firstGroup) / groupBytes)
        {
            throw new UnauthorizedAccessException(InvalidPeer);
        }

        SecurityIdentifier? logonSid = null;
        for (var index = 0; index < groupCount; index++)
        {
            var groupOffset = firstGroup + index * groupBytes;
            var attributes = unchecked((uint)Marshal.ReadInt32(buffer, groupOffset + IntPtr.Size));
            if ((attributes & LogonGroupAttributes) != LogonGroupAttributes)
            {
                continue;
            }

            var sid = ReadSid(buffer, length, Marshal.ReadIntPtr(buffer, groupOffset));
            if (logonSid is not null || !IsLogonSid(sid))
            {
                throw new UnauthorizedAccessException(InvalidPeer);
            }

            logonSid = sid;
        }

        return logonSid ?? throw new UnauthorizedAccessException(InvalidPeer);
    }

    private static SecurityIdentifier ReadSid(nint buffer, int length, nint sid)
    {
        var offset = sid.ToInt64() - buffer.ToInt64();
        if (offset < 0 || offset > length - SidHeaderBytes)
        {
            throw new UnauthorizedAccessException(InvalidPeer);
        }

        var subAuthorities = Marshal.ReadByte(sid, 1);
        if (subAuthorities > MaximumSidSubAuthorities || offset + SidHeaderBytes + subAuthorities * sizeof(int) > length)
        {
            throw new UnauthorizedAccessException(InvalidPeer);
        }

        return new SecurityIdentifier(sid);
    }

    private static int ReadTokenInteger(SafeAccessTokenHandle token, TokenInformationClass informationClass) =>
        NativeMethods.GetTokenInteger(token, informationClass, out var value, sizeof(int), out var length) && length == sizeof(int)
            ? value : throw new UnauthorizedAccessException(InvalidPeer);

    private static T ReadTokenInformation<T>(SafeAccessTokenHandle token, TokenInformationClass informationClass,
        Func<nint, int, T> read)
    {
        if (NativeMethods.GetTokenInformation(token, informationClass, 0, 0, out var length) ||
            Marshal.GetLastPInvokeError() != ErrorInsufficientBuffer || length is < 1 or > MaximumTokenInformationBytes)
        {
            throw new UnauthorizedAccessException(InvalidPeer);
        }

        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (!NativeMethods.GetTokenInformation(token, informationClass, buffer, length, out var bytesReturned) ||
                bytesReturned < 1 || bytesReturned > length)
            {
                throw new UnauthorizedAccessException(InvalidPeer);
            }

            return read(buffer, bytesReturned);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private enum TokenInformationClass
    {
        User = 1,
        Groups = 2,
        ImpersonationLevel = 9,
        SessionId = 12,
        Elevation = 20,
    }

    private static class NativeMethods
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern SafeProcessHandle OpenProcess(int desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern int WaitForSingleObject(SafeProcessHandle process, int milliseconds);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("advapi32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenProcessToken(SafeProcessHandle process, int desiredAccess, out SafeAccessTokenHandle token);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern nint GetCurrentThread();

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("advapi32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenThreadToken(nint thread, int desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool openAsSelf, out SafeAccessTokenHandle token);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetTokenInformation(SafeAccessTokenHandle token, TokenInformationClass informationClass,
            nint buffer, int bufferLength, out int bytesReturned);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("advapi32.dll", EntryPoint = "GetTokenInformation", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetTokenInteger(SafeAccessTokenHandle token, TokenInformationClass informationClass,
            out int value, int bufferLength, out int bytesReturned);
    }
}
