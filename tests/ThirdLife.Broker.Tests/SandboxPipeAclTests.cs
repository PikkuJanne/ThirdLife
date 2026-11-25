using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ThirdLife.Broker.Protocol.Transport;

namespace ThirdLife.Broker.Tests;

[SupportedOSPlatform("windows")]
public sealed class SandboxPipeAclTests
{
    private const string RunMarker = "THIRDLIFE_SANDBOX_BROKER_ACL_TEST";
    private const string RequestPath = @"C:\TL0102\Input\Request\run-request.json";
    private const uint UserNotFound = 2221;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);

    [Fact]
    [Trait("Category", "SandboxOnly")]
    public async Task WindowsSandboxDistinctStandardAccountIsDeniedByProductionPipeAcl()
    {
        RequireVerifiedSandbox();
        var pipeId = Guid.NewGuid();
        var initiatingIdentity = WindowsBrokerPeer.CurrentIdentity();
        await using var server = WindowsBrokerPipe.CreateServer(pipeId, initiatingIdentity.LogonSid);
        using var timeout = new CancellationTokenSource(Deadline);
        var accepting = server.WaitForConnectionAsync(timeout.Token);
        var account = "TLBroker" + Guid.NewGuid().ToString("N")[..10];
        using var password = CreatePassword();
        CreateGuestAccount(account, password);
        try
        {
            await CheckDistinctStandardAccountAsync(account, password, pipeId, timeout.Token);
            Assert.False(server.IsConnected, "The restricted pipe accepted a connection from the distinct account.");
            Assert.False(accepting.IsCompleted, "The restricted pipe listener should still be awaiting its initiating user.");
            await using var control = WindowsBrokerPipe.CreateClient(pipeId);
            await control.ConnectAsync(timeout.Token);
            await accepting;
            Assert.True(WindowsBrokerPipe.HasClientProcess(server, Environment.ProcessId, initiatingIdentity.SessionId));
        }
        finally
        {
            DeleteGuestAccount(account);
        }
    }

    private static void RequireVerifiedSandbox()
    {
        using var identity = WindowsIdentity.GetCurrent();
        Assert.True(identity.Name.Equals(Environment.MachineName + "\\WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase) &&
            new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator), "A verified administrative Sandbox guest token is required.");
        var runId = Environment.GetEnvironmentVariable(RunMarker);
        Assert.True(runId is { Length: 32 } && runId.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f'), "The admitted Sandbox case marker is required.");

        using (var file = new FileStream(RequestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.InRange(file.Length, 1, 8192);
            using var request = JsonDocument.Parse(file, new JsonDocumentOptions { AllowDuplicateProperties = false, MaxDepth = 4 });
            var root = request.RootElement;
            Assert.True(root.GetProperty("task_id").ValueEquals("TL-0311") &&
                root.GetProperty("phase").ValueEquals("Targeted") &&
                root.GetProperty("run_id").GetString() == runId &&
                root.GetProperty("networking_enabled").ValueKind == JsonValueKind.False,
                "The protected guest request must admit this offline broker case.");
        }

        // Verify the actual mounted request refuses write access before calling any account API.
        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            using var writable = new FileStream(RequestPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        });
    }

    private static SecureString CreatePassword()
    {
        const string alphabet = "0123456789abcdef";
        var result = new SecureString();
        foreach (var required in "aA7!") { result.AppendChar(required); }
        Span<byte> random = stackalloc byte[16];
        RandomNumberGenerator.Fill(random);
        foreach (var value in random)
        {
            result.AppendChar(alphabet[value >> 4]);
            result.AppendChar(alphabet[value & 0x0F]);
        }

        CryptographicOperations.ZeroMemory(random);
        result.MakeReadOnly();
        return result;
    }

    private static void CreateGuestAccount(string account, SecureString password)
    {
        var nativePassword = Marshal.SecureStringToGlobalAllocUnicode(password);
        try
        {
            var info = new NativeMethods.UserInfo
            {
                Name = account,
                Password = nativePassword,
                Privilege = 1, // USER_PRIV_USER
                Flags = 0x201, // UF_NORMAL_ACCOUNT | UF_SCRIPT
            };
            Assert.True(NativeMethods.NetUserAdd(null, 1, in info, out _) == 0,
                "The disposable guest could not create its standard test account.");
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(nativePassword);
        }
    }

    private static void DeleteGuestAccount(string account)
    {
        var deleted = NativeMethods.NetUserDel(null, account);
        var lookup = NativeMethods.NetUserGetInfo(null, account, 0, out var information);
        if (information != IntPtr.Zero) { _ = NativeMethods.NetApiBufferFree(information); }
        Assert.True(deleted == 0 && lookup == UserNotFound, "The owned guest test account was not fully removed.");
    }

    private static async Task CheckDistinctStandardAccountAsync(
        string account, SecureString password, Guid pipeId, CancellationToken cancellationToken)
    {
        // Only the generated account and owned pipe are substituted. The child observes its
        // Windows token, asks for the production pipe access mask, and sends no data.
        var fixture = $$"""
            $ErrorActionPreference = 'Stop'
            try {
                $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
                try {
                    $expected = $env:COMPUTERNAME + '\' + '{{account}}'
                    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
                    if ($identity.Name -ine $expected -or $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { exit 21 }
                } finally { $identity.Dispose() }
                $rights = [IO.Pipes.PipeAccessRights]::ReadData -bor [IO.Pipes.PipeAccessRights]::WriteData -bor
                    [IO.Pipes.PipeAccessRights]::ReadAttributes -bor [IO.Pipes.PipeAccessRights]::Synchronize
                $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'thirdlife-broker-v1-{{pipeId:N}}',
                    $rights, [IO.Pipes.PipeOptions]::Asynchronous, [Security.Principal.TokenImpersonationLevel]::Identification,
                    [IO.HandleInheritability]::None)
                try {
                    $pipe.Connect(2000)
                    exit 22
                } catch {
                    if ($_.Exception.GetBaseException() -is [UnauthorizedAccessException]) { exit 0 }
                    exit 23
                } finally { $pipe.Dispose() }
            } catch { exit 24 }
            """;
        var executable = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(executable)
        {
            Domain = Environment.MachineName,
            UserName = account,
            Password = password,
            UseShellExecute = false,
            CreateNoWindow = true,
            LoadUserProfile = false,
            WorkingDirectory = Environment.SystemDirectory,
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(fixture)));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The owned guest probe did not start.");
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(cleanup.Token);
            }
        }
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct UserInfo
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string Name;
            public nint Password;
            public uint PasswordAge;
            public uint Privilege;
            public nint HomeDirectory;
            public nint Comment;
            public uint Flags;
            public nint ScriptPath;
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("netapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern uint NetUserAdd(string? server, uint level, in UserInfo userInfo, out uint parameterError);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("netapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern uint NetUserDel(string? server, string userName);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("netapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern uint NetUserGetInfo(string? server, string userName, uint level, out nint information);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("netapi32.dll", ExactSpelling = true)]
        internal static extern uint NetApiBufferFree(nint buffer);
    }
}
