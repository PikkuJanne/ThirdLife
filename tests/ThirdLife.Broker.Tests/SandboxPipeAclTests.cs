using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using ThirdLife.Broker.Protocol.Transport;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.Broker.Tests;

[SupportedOSPlatform("windows")]
public sealed class SandboxPipeAclTests
{
    private const string RunMarker = "THIRDLIFE_SANDBOX_BROKER_ACL_TEST";
    private const string RequestPath = @"C:\TL0102\Input\Request\run-request.json";
    private const string HyperVRunMarker = "THIRDLIFE_HYPERV_BROKER_ACL_RUN_ID";
    private const uint UserNotFound = 2221;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);

    [Fact]
    [Trait("Category", "SandboxOnly")]
    public async Task WindowsSandboxDistinctStandardAccountIsDeniedByProductionPipeAcl()
    {
        RequireVerifiedSandbox();
        await VerifyProductionPipeDeniesDistinctAccountAsync("TLBroker" + Guid.NewGuid().ToString("N")[..10], null);
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task HyperVDistinctStandardAccountIsDeniedByProductionPipeAcl()
    {
        var runId = RequireVerifiedHyperV();
        await VerifyProductionPipeDeniesDistinctAccountAsync("TLBroker" + runId[..12], "ThirdLife TL-0317 " + runId);
    }

    private static async Task VerifyProductionPipeDeniesDistinctAccountAsync(string account, string? comment)
    {
        var pipeId = Guid.NewGuid();
        var initiatingIdentity = WindowsBrokerPeer.CurrentIdentity();
        await using var server = WindowsBrokerPipe.CreateServer(pipeId, initiatingIdentity.LogonSid);
        using var timeout = new CancellationTokenSource(Deadline);
        var accepting = server.WaitForConnectionAsync(timeout.Token);
        using var password = CreatePassword();
        CreateGuestAccount(account, password, comment);
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

    private static string RequireVerifiedHyperV()
    {
        using var identity = WindowsIdentity.GetCurrent();
        Assert.True(Environment.MachineName.Equals("ThirdLife-W11", StringComparison.OrdinalIgnoreCase) &&
            identity.Name.Equals("ThirdLife-W11\\ThirdLifeAdmin", StringComparison.OrdinalIgnoreCase) &&
            new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),
            "The administrative ThirdLife Hyper-V guest token is required.");
        Assert.Equal(WindowsPlatformEligibility.Supported, WindowsPlatform.CheckCurrent());
        using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        Assert.True(bios?.GetValue("SystemManufacturer") is "Microsoft Corporation" &&
            bios.GetValue("SystemProductName") is "Virtual Machine", "The Hyper-V guest firmware identity is required.");

        var runId = Environment.GetEnvironmentVariable(HyperVRunMarker);
        Assert.NotNull(runId);
        Assert.Matches(@"\A[0-9a-f]{32}\z", runId);
        var staging = new DirectoryInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "ThirdLifeVerification"));
        var run = new DirectoryInfo(Path.Combine(staging.FullName, runId));
        var request = new FileInfo(Path.Combine(run.FullName, "run-request.json"));
        const AccessControlSections sections = AccessControlSections.Owner | AccessControlSections.Access;
        Assert.False(staging.Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.False(run.Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.False(request.Attributes.HasFlag(FileAttributes.ReparsePoint));
        RequireProtectedHyperVInput(staging.GetAccessControl(sections));
        RequireProtectedHyperVInput(run.GetAccessControl(sections));
        RequireProtectedHyperVInput(request.GetAccessControl(sections));
        Assert.True(request.IsReadOnly);

        using (var file = new FileStream(request.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.InRange(file.Length, 1, 8192);
            using var document = JsonDocument.Parse(file, new JsonDocumentOptions { AllowDuplicateProperties = false, MaxDepth = 4 });
            var root = document.RootElement;
            Assert.True(root.EnumerateObject().Count() == 3 &&
                root.GetProperty("task_id").ValueEquals("TL-0317") &&
                root.GetProperty("scenario").ValueEquals("DistinctAccount") &&
                root.GetProperty("run_id").ValueEquals(runId),
                "The protected Hyper-V request must admit this distinct-account scenario.");
        }

        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            using var writable = new FileStream(request.FullName, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        });
        return runId;
    }

    private static void RequireProtectedHyperVInput(FileSystemSecurity security)
    {
        static bool IsTrustedOwner(SecurityIdentifier sid) => sid.IsWellKnown(WellKnownSidType.LocalSystemSid) ||
            sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid);

        Assert.True(security.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner && IsTrustedOwner(owner),
            "The Hyper-V scenario inputs must be owned by Administrators or SYSTEM.");
        const FileSystemRights readOnly = FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize;
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
        Assert.NotEmpty(rules);
        foreach (FileSystemAccessRule rule in rules)
        {
            var sid = (SecurityIdentifier)rule.IdentityReference;
            Assert.True(rule.AccessControlType != AccessControlType.Allow || IsTrustedOwner(sid) ||
                (rule.FileSystemRights & ~readOnly) == 0,
                "The Hyper-V scenario input ACL grants write access outside Administrators and SYSTEM.");
        }
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

    private static void CreateGuestAccount(string account, SecureString password, string? comment)
    {
        var nativePassword = Marshal.SecureStringToGlobalAllocUnicode(password);
        try
        {
            var info = new NativeMethods.UserInfo
            {
                Name = account,
                Password = nativePassword,
                Privilege = 1, // USER_PRIV_USER
                Comment = comment,
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
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? Comment;
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
