using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace ThirdLife.Broker.Protocol.Transport;

/// <summary>A single local asynchronous broker pipe with a logon-session ACL.</summary>
[SupportedOSPlatform("windows")]
public static class WindowsBrokerPipe
{
    private const int PipeAccessDuplex = 0x00000003;
    private const int FirstPipeInstance = 0x00080000;
    private const int Overlapped = 0x40000000;
    private const int RejectRemoteClients = 0x00000008;
    private const PipeAccessRights ClientAccess = PipeAccessRights.ReadData | PipeAccessRights.WriteData |
        PipeAccessRights.ReadAttributes | PipeAccessRights.Synchronize;
    private const string PipePrefix = "thirdlife-broker-v1-";

    public static NamedPipeServerStream CreateServer(Guid pipeId, SecurityIdentifier logonSid)
    {
        ArgumentNullException.ThrowIfNull(logonSid);
        if (!WindowsBrokerPeer.IsLogonSid(logonSid))
        {
            throw new ArgumentException("A Windows logon SID is required for the broker pipe.", nameof(logonSid));
        }

        var pipePath = @"\\.\pipe\" + PipeName(pipeId);
        var descriptor = BuildSecurityDescriptor(logonSid);
        var descriptorHandle = GCHandle.Alloc(descriptor, GCHandleType.Pinned);
        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = descriptorHandle.AddrOfPinnedObject(),
                InheritHandle = 0,
            };
            // Byte mode, overlapped handles, a single instance and no remote transport.
            var handle = NativeMethods.CreateNamedPipeW(pipePath, PipeAccessDuplex | FirstPipeInstance | Overlapped,
                RejectRemoteClients, 1, BrokerProtocolLimits.MaximumMessageBytes, BrokerProtocolLimits.MaximumMessageBytes,
                0, in attributes);
            try
            {
                if (handle.IsInvalid)
                {
                    throw new IOException("The restricted local broker pipe could not be created.");
                }

                return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, handle);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }
        finally
        {
            descriptorHandle.Free();
        }
    }

    public static NamedPipeClientStream CreateClient(Guid pipeId) => new(".", PipeName(pipeId), ClientAccess,
        PipeOptions.Asynchronous, TokenImpersonationLevel.Identification, HandleInheritability.None);

    public static bool HasClientProcess(NamedPipeServerStream pipe, int processId, int sessionId)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        return processId > 0 && sessionId >= 0 && pipe.IsConnected &&
            NativeMethods.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var actualProcessId) && actualProcessId == processId &&
            NativeMethods.GetNamedPipeClientSessionId(pipe.SafePipeHandle, out var actualSessionId) && actualSessionId == sessionId;
    }

    public static bool HasServerProcess(NamedPipeClientStream pipe, int processId, int sessionId)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        return processId > 0 && sessionId >= 0 && pipe.IsConnected &&
            NativeMethods.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var actualProcessId) && actualProcessId == processId &&
            NativeMethods.GetNamedPipeServerSessionId(pipe.SafePipeHandle, out var actualSessionId) && actualSessionId == sessionId;
    }

    /// <summary>Reads the identification token associated with the last client data read.</summary>
    public static BrokerPeerIdentity ReadImpersonatedClient(NamedPipeServerStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        BrokerPeerIdentity? identity = null;
        pipe.RunAsClient(() => identity = WindowsBrokerPeer.ReadIdentificationToken());
        return identity ?? throw new UnauthorizedAccessException("The Windows broker peer could not be authenticated.");
    }

    private static string PipeName(Guid pipeId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(pipeId, Guid.Empty);
        return PipePrefix + pipeId.ToString("N");
    }

    private static byte[] BuildSecurityDescriptor(SecurityIdentifier logonSid)
    {
        var ownerRights = new SecurityIdentifier(WellKnownSidType.WinCreatorOwnerRightsSid, null);
        var acl = new RawAcl(GenericAcl.AclRevision, 2);
        acl.InsertAce(0, new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, (int)ClientAccess, logonSid, false, null));
        // An OWNER RIGHTS ACE replaces the implicit owner WRITE_DAC grant.
        acl.InsertAce(1, new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, (int)PipeAccessRights.ReadPermissions,
            ownerRights, false, null));
        var descriptor = new RawSecurityDescriptor(
            ControlFlags.DiscretionaryAclPresent | ControlFlags.DiscretionaryAclProtected | ControlFlags.SelfRelative,
            null, null, null, acl);
        var bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        return bytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public nint SecurityDescriptor;
        public int InheritHandle;
    }

    private static class NativeMethods
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern SafePipeHandle CreateNamedPipeW(string name, int openMode, int pipeMode, int maximumInstances,
            int outputBufferSize, int inputBufferSize, int defaultTimeout, in SecurityAttributes securityAttributes);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out int processId);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetNamedPipeClientSessionId(SafePipeHandle pipe, out int sessionId);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out int processId);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetNamedPipeServerSessionId(SafePipeHandle pipe, out int sessionId);
    }
}
