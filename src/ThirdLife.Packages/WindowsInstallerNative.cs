using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ThirdLife.Packages;

internal static partial class WindowsInstallerNative
{
    public const uint Success = 0;
    public const uint UnknownProduct = 1605;
    private const uint NoMoreItems = 259;
    private const uint MachineContext = 4;
    private const uint TrustUiNone = 2;
    private const uint TrustRevokeWholeChain = 1;
    private const uint TrustChoiceFile = 1;
    private const uint TrustStateVerify = 1;
    private const uint TrustStateClose = 2;
    private const uint TrustRevocationCheckChainExcludeRoot = 0x80;
    private const uint TrustDisableLegacyHash = 0x2000;
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public static unsafe (uint Status, string? Value) ReadMachineProperty(string product, string property)
    {
        const uint capacity = 128;
        var length = capacity;
        var buffer = stackalloc char[(int)capacity];
        var status = MsiGetProductInfoEx(product, null, MachineContext, property, buffer, &length);
        return status == Success && length is > 0 and < capacity
            ? (status, new string(buffer, 0, (int)length))
            : (status, null);
    }

    public static unsafe bool HasRelatedProduct(string upgradeCode)
    {
        var productCode = stackalloc char[39];
        return MsiEnumRelatedProducts(upgradeCode, 0, 0, productCode) switch
        {
            Success => true,
            NoMoreItems => false,
            _ => throw new IOException("Windows Installer could not enumerate related products."),
        };
    }

    public static unsafe int VerifySignature(string path)
    {
        var action = GenericVerifyV2;
        fixed (char* fileName = path)
        {
            var fileInfo = new TrustFile
            {
                Size = (uint)sizeof(TrustFile),
                Path = fileName,
            };
            var trust = new TrustData
            {
                Size = (uint)sizeof(TrustData),
                UiChoice = TrustUiNone,
                RevocationChecks = TrustRevokeWholeChain,
                Choice = TrustChoiceFile,
                FileInfo = &fileInfo,
                StateAction = TrustStateVerify,
                Flags = TrustRevocationCheckChainExcludeRoot | TrustDisableLegacyHash,
            };
            try
            {
                return WinVerifyTrust(-1, &action, &trust);
            }
            finally
            {
                trust.StateAction = TrustStateClose;
                _ = WinVerifyTrust(-1, &action, &trust);
            }
        }
    }

    public static unsafe SafeFileHandle GuardDirectory(string path)
    {
        const uint readAttributes = 0x80;
        const uint shareReadWrite = 3;
        const uint openExisting = 3;
        const uint openDirectoryWithoutReparse = 0x02200000;
        const int attributeTagInfo = 9;
        var handle = CreateFile(path, readAttributes, shareReadWrite, 0, openExisting, openDirectoryWithoutReparse, 0);
        var info = default(FileAttributeTagInfo);
        if (handle.IsInvalid ||
            GetFileInformationByHandleEx(handle, attributeTagInfo, &info, (uint)sizeof(FileAttributeTagInfo)) == 0 ||
            (info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
        {
            handle.Dispose();
            throw new IOException("The package staging directory could not be secured.");
        }

        return handle;
    }

    public static unsafe SafeFileHandle OpenProbeFile(string path)
    {
        const uint read = 0x80000000;
        const uint shareRead = 1;
        const uint openExisting = 3;
        const uint openWithoutReparse = 0x00200000;
        const int attributeTagInfo = 9;
        var handle = CreateFile(path, read, shareRead, 0, openExisting, openWithoutReparse, 0);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            if (error is 2 or 3)
            {
                throw new FileNotFoundException();
            }

            throw new IOException("The package probe file could not be opened.");
        }

        var info = default(FileAttributeTagInfo);
        if (GetFileInformationByHandleEx(handle, attributeTagInfo, &info, (uint)sizeof(FileAttributeTagInfo)) == 0 ||
            (info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            handle.Dispose();
            throw new PackageTrustException("The package probe file is redirected or unavailable.");
        }

        return handle;
    }

    [LibraryImport("msi.dll", EntryPoint = "MsiGetProductInfoExW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial uint MsiGetProductInfoEx(string product, string? userSid, uint context,
        string property, char* buffer, uint* size);

    [LibraryImport("msi.dll", EntryPoint = "MsiEnumRelatedProductsW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial uint MsiEnumRelatedProducts(string upgradeCode, uint reserved, uint index, char* productCode);

    [LibraryImport("wintrust.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int WinVerifyTrust(nint window, Guid* action, TrustData* trust);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint share, nint security,
        uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int GetFileInformationByHandleEx(SafeFileHandle file, int informationClass,
        FileAttributeTagInfo* information, uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo
    {
        public FileAttributes Attributes;
        public uint Tag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct TrustFile
    {
        public uint Size;
        public char* Path;
        public nint File;
        public Guid* KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct TrustData
    {
        public uint Size;
        public nint PolicyCallbackData;
        public nint SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint Choice;
        public TrustFile* FileInfo;
        public uint StateAction;
        public nint StateData;
        public char* UrlReference;
        public uint Flags;
        public uint UiContext;
        public nint SignatureSettings;
    }
}
