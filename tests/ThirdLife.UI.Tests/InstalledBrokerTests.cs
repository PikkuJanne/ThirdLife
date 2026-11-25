using System.Buffers.Binary;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using ThirdLife.UI.BrokerClient;

namespace ThirdLife.UI.Tests;

public sealed class InstalledBrokerTests
{
    [Theory]
    [InlineData("O:BAD:P(A;;FA;;;BA)(A;;FA;;;SY)(A;;FRFX;;;BU)", true)]
    [InlineData("O:S-1-5-21-111-222-333-1000D:P(A;;FA;;;BA)(A;;FRFX;;;BU)", false)]
    [InlineData("O:BAD:(A;;FA;;;BA)(A;;FRFX;;;BU)", false)]
    [InlineData("O:BAD:NO_ACCESS_CONTROL", false)]
    [InlineData("O:BAD:P(A;;FA;;;BA)(A;;GW;;;BU)", false)]
    [InlineData("O:BAD:P(A;;FA;;;BA)(A;;0x40;;;AU)", false)]
    [InlineData("O:BAD:P(A;;FA;;;BA)(A;;WD;;;AU)", false)]
    [InlineData("O:BAD:P(A;;FA;;;BA)(A;;WO;;;AU)", false)]
    [InlineData("O:BAD:P(A;;FA;;;BA)(A;;SD;;;AU)", false)]
    public void InstalledObjectsRequireProtectedOwnerAndDacl(string sddl, bool expected) =>
        Assert.Equal(expected, InstalledBroker.HasTrustedAccess(
            new RawSecurityDescriptor(sddl), InstalledBroker.ObjectKind.Installation));

    [Fact]
    public void RootMayPermitCreationOfSiblingsButCannotPermitReplacement()
    {
        var createChildren = new RawSecurityDescriptor("O:SYD:P(A;;FA;;;SY)(A;;0x4;;;AU)(A;OICIIO;GW;;;AU)");
        var deleteChildren = new RawSecurityDescriptor("O:SYD:P(A;;FA;;;SY)(A;;0x40;;;AU)");

        Assert.True(InstalledBroker.HasTrustedAccess(createChildren, InstalledBroker.ObjectKind.VolumeRoot));
        Assert.False(InstalledBroker.HasTrustedAccess(createChildren, InstalledBroker.ObjectKind.SystemAncestor));
        Assert.False(InstalledBroker.HasTrustedAccess(deleteChildren, InstalledBroker.ObjectKind.VolumeRoot));
    }

    [Fact]
    public void ArtifactMetadataMustMatchTheEntireBinaryAndUseTheExactEncoding()
    {
        byte[] bytes = [1, 2, 3, 4];
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        using var executable = new MemoryStream(bytes);

        Assert.True(Matches(digest));
        Assert.False(Matches(digest.ToUpperInvariant()));
        Assert.False(Matches(digest + "\n"));
        Assert.False(Matches(new string('0', 64)));
        bytes[^1] = 5;
        Assert.False(Matches(digest));

        bool Matches(string metadata)
        {
            using var file = new MemoryStream(Encoding.ASCII.GetBytes(metadata));
            return InstalledBroker.MatchesArtifact(executable, file);
        }
    }

    [Theory]
    [InlineData(0x8664, 0x0800, false, true)]
    [InlineData(0x8664, 0x0000, false, false)]
    [InlineData(0x8664, 0x1000, false, false)]
    [InlineData(0xAA64, 0x0800, false, false)]
    [InlineData(0x8664, 0x0800, true, false)]
    public void ImageMustBeX64NativeWithSystem32DependentLoadFlags(
        ushort machine, ushort flags, bool hasClrHeader, bool expected)
    {
        using var image = new MemoryStream(CreateImage(machine, flags, hasClrHeader));
        Assert.Equal(expected, InstalledBroker.HasNativeStartupPolicy(image));
    }

    private static byte[] CreateImage(ushort machine, ushort flags, bool hasClrHeader)
    {
        // A bounded PE32+ image for inspection. This fixture is never executable.
        var image = new byte[1024];
        const int coff = 0x84;
        const int optional = coff + 20;
        const int section = optional + 240;
        const int dataDirectory = optional + 112;
        const int loadConfiguration = 512;
        WriteShort(0, 0x5A4D);
        WriteInt(0x3C, 0x80);
        WriteInt(0x80, 0x4550);
        WriteShort(coff, machine);
        WriteShort(coff + 2, 1);
        WriteShort(coff + 16, 240);
        WriteShort(coff + 18, 0x0022);
        WriteShort(optional, 0x020B);
        WriteInt(optional + 32, 4096);
        WriteInt(optional + 36, 512);
        WriteInt(optional + 56, 8192);
        WriteInt(optional + 60, 512);
        WriteInt(optional + 108, 16);
        WriteInt(dataDirectory + 80, 4096);
        WriteInt(dataDirectory + 84, 80);
        if (hasClrHeader)
        {
            WriteInt(dataDirectory + 112, 4192);
            WriteInt(dataDirectory + 116, 72);
        }
        ".rdata"u8.CopyTo(image.AsSpan(section));
        WriteInt(section + 8, 512);
        WriteInt(section + 12, 4096);
        WriteInt(section + 16, 512);
        WriteInt(section + 20, 512);
        WriteInt(loadConfiguration, 80);
        WriteShort(loadConfiguration + 78, flags);
        return image;

        void WriteShort(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(offset), value);
        void WriteInt(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(offset), value);
    }
}
