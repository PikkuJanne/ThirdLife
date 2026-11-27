using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using ThirdLife.Actions.Recovery;

namespace ThirdLife.Persistence;

[SupportedOSPlatform("windows")]
internal static class WindowsRebootAuthenticator
{
    private const uint UiForbidden = 0x1;

    internal static RebootResumeToken Seal(RebootCheckpoint checkpoint, GuardedStoreFile databaseGuard) =>
        TrySeal(checkpoint, databaseGuard) ?? throw new JobStoreUnavailableException();

    internal static RebootResumeToken? TrySeal(RebootCheckpoint checkpoint, GuardedStoreFile databaseGuard)
    {
        var sealedDigest = Seal(checkpoint.CreateAuthenticationData(), databaseGuard, "ThirdLife.RebootCheckpoint.v1"u8);
        return sealedDigest is { Length: > 0 and <= RebootResumeToken.MaximumBytes }
            ? new RebootResumeToken(sealedDigest)
            : null;
    }

    internal static bool Matches(RebootCheckpoint checkpoint, RebootResumeToken token, GuardedStoreFile databaseGuard)
    {
        return Matches(checkpoint.CreateAuthenticationData(), token.AuthenticityBlob.ToArray(), databaseGuard,
            "ThirdLife.RebootCheckpoint.v1"u8);
    }

    internal static byte[]? Seal(byte[] data, GuardedStoreFile databaseGuard, ReadOnlySpan<byte> purpose) =>
        Transform(SHA256.HashData(data), Purpose(databaseGuard, purpose), protect: true);

    internal static bool Matches(byte[] data, byte[] authenticator, GuardedStoreFile databaseGuard, ReadOnlySpan<byte> purpose)
    {
        var openedDigest = Transform(authenticator, Purpose(databaseGuard, purpose), protect: false);
        return openedDigest is not null && CryptographicOperations.FixedTimeEquals(openedDigest, SHA256.HashData(data));
    }

    private static byte[] Purpose(GuardedStoreFile databaseGuard, ReadOnlySpan<byte> label)
    {
        var entropy = new byte[label.Length + sizeof(uint) + sizeof(ulong)];
        label.CopyTo(entropy);
        BinaryPrimitives.WriteUInt32LittleEndian(entropy.AsSpan(label.Length), databaseGuard.Identity.VolumeSerialNumber);
        BinaryPrimitives.WriteUInt64LittleEndian(entropy.AsSpan(label.Length + sizeof(uint)), databaseGuard.Identity.FileIndex);
        return entropy;
    }

    private static byte[]? Transform(byte[] input, byte[] entropy, bool protect)
    {
        var inputHandle = default(GCHandle);
        var entropyHandle = default(GCHandle);
        var output = default(DataBlob);
        try
        {
            inputHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
            entropyHandle = GCHandle.Alloc(entropy, GCHandleType.Pinned);
            var data = new DataBlob(input.Length, inputHandle.AddrOfPinnedObject());
            var purpose = new DataBlob(entropy.Length, entropyHandle.AddrOfPinnedObject());
            var success = protect
                ? CryptProtectData(in data, IntPtr.Zero, in purpose, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output)
                : CryptUnprotectData(in data, IntPtr.Zero, in purpose, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
            var maximumLength = protect ? RebootResumeToken.MaximumBytes : SHA256.HashSizeInBytes;
            if (!success || output.Pointer == IntPtr.Zero || output.Length < 1 || output.Length > maximumLength)
            {
                return null;
            }

            var bytes = new byte[output.Length];
            Marshal.Copy(output.Pointer, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            if (output.Pointer != IntPtr.Zero) { _ = LocalFree(output.Pointer); }
            if (entropyHandle.IsAllocated) { entropyHandle.Free(); }
            if (inputHandle.IsAllocated) { inputHandle.Free(); }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DataBlob(int length, IntPtr pointer)
    {
        internal readonly int Length = length;
        internal readonly IntPtr Pointer = pointer;
    }

    [DllImport("crypt32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(in DataBlob data, IntPtr description, in DataBlob entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("crypt32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(in DataBlob data, IntPtr description, in DataBlob entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
