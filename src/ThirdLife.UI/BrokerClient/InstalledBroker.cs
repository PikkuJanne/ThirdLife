using System.IO;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using static ThirdLife.Broker.Protocol.Transport.ProtectedBrokerFiles;

namespace ThirdLife.UI.BrokerClient;

/// <summary>Holds the installed image and its path stable through the Windows consent request.</summary>
internal sealed class InstalledBroker : IDisposable
{
    private const int DigestLength = 64;
    private const int MaximumImageBytes = 32 * 1024 * 1024;
    private readonly List<SafeFileHandle> _guards;

    private InstalledBroker(string executable, List<SafeFileHandle> guards)
    {
        Executable = executable;
        _guards = guards;
    }

    public string Executable { get; }

    public static InstalledBroker? TryOpen() => TryOpen(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.SystemDirectory);

    internal static InstalledBroker? TryOpen(string programFiles, string systemDirectory)
    {
        var guards = new List<SafeFileHandle>();
        InstalledBroker? installation = null;
        try
        {
            if (!OpenDirectory(programFiles, systemDirectory, "Broker", guards, out var directory)) { return null; }
            var metadata = Path.Combine(directory, "broker.sha256");
            var executable = Path.Combine(directory, "ThirdLife.Broker.exe");
            var entries = Directory.EnumerateFileSystemEntries(directory).Take(3).ToArray();
            if (entries.Length != 2 || !entries.Contains(metadata, StringComparer.OrdinalIgnoreCase) ||
                !entries.Contains(executable, StringComparer.OrdinalIgnoreCase) ||
                !OpenGuard(metadata, directory: false, ObjectKind.Installation, guards, out _) ||
                !OpenGuard(executable, directory: false, ObjectKind.Installation, guards, out var canonicalExecutable))
            {
                return null;
            }

            // The borrowing wrappers leave the original guards open through ShellExecuteEx.
            using var manifestStream = ReadGuardedFile(guards[^2]);
            using var executableStream = ReadGuardedFile(guards[^1]);
            if (MatchesArtifact(executableStream, manifestStream) && HasNativeStartupPolicy(executableStream))
            {
                installation = new InstalledBroker(canonicalExecutable, guards);
            }

            return installation;
        }
        finally
        {
            if (installation is null) { CloseGuards(guards); }
        }
    }

    public void Dispose() => CloseGuards(_guards);

    internal static bool MatchesArtifact(Stream executable, Stream metadata)
    {
        if (executable.Length is <= 0 or > MaximumImageBytes || metadata.Length != DigestLength)
        {
            return false;
        }

        Span<byte> expected = stackalloc byte[DigestLength];
        metadata.ReadExactly(expected);
        foreach (var value in expected)
        {
            if (value is not (>= (byte)'0' and <= (byte)'9' or >= (byte)'a' and <= (byte)'f'))
            {
                return false;
            }
        }

        executable.Position = 0;
        var digest = Convert.ToHexStringLower(SHA256.HashData(executable));
        return digest.Equals(System.Text.Encoding.ASCII.GetString(expected), StringComparison.Ordinal);
    }

    internal static bool HasNativeStartupPolicy(Stream executable)
    {
        // IMAGE_LOAD_CONFIG_DIRECTORY64.DependentLoadFlags is at byte offset 78.
        const int loadFlagsOffset = 78;
        const int minimumLoadConfigSize = loadFlagsOffset + sizeof(ushort);
        const ushort searchSystem32 = 0x0800;
        if (executable.Length is <= 0 or > MaximumImageBytes) { return false; }
        try
        {
            executable.Position = 0;
            using var image = new PEReader(executable, PEStreamOptions.LeaveOpen);
            var headers = image.PEHeaders;
            if (headers.CoffHeader.Machine != Machine.Amd64 ||
                (headers.CoffHeader.Characteristics & (Characteristics.ExecutableImage | Characteristics.Dll)) !=
                    Characteristics.ExecutableImage ||
                headers.PEHeader is not { Magic: PEMagic.PE32Plus } pe ||
                pe.CorHeaderTableDirectory.RelativeVirtualAddress != 0 || pe.CorHeaderTableDirectory.Size != 0 ||
                pe.LoadConfigTableDirectory is not { RelativeVirtualAddress: > 0, Size: >= minimumLoadConfigSize } configuration)
            {
                return false;
            }

            var section = image.GetSectionData(configuration.RelativeVirtualAddress);
            if (section.Length < configuration.Size) { return false; }
            var reader = section.GetReader(0, minimumLoadConfigSize);
            var configuredSize = reader.ReadUInt32();
            if (configuredSize < minimumLoadConfigSize || configuredSize > configuration.Size) { return false; }
            reader.Offset = loadFlagsOffset;
            return reader.ReadUInt16() == searchSystem32;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }
}
