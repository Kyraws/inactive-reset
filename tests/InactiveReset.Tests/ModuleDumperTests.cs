using System.Buffers.Binary;
using InactiveReset.Reanchor;
using Xunit;

namespace InactiveReset.Tests;

public sealed class ModuleDumperTests
{
    [Fact]
    public void Disk_hash_is_not_used_for_a_different_mapped_image()
    {
        var mapped = new byte[0x5000];
        mapped[0] = (byte)'M'; mapped[1] = (byte)'Z';
        Put32(mapped, 0x3C, 0x80);
        Put32(mapped, 0x80, 0x4550);
        Put16(mapped, 0x86, 1);
        Put16(mapped, 0x94, 0xF0);
        Put16(mapped, 0x98, 0x20B);
        Put32(mapped, 0xD0, mapped.Length);
        var disk = mapped[..0x1000].ToArray();
        Assert.True(ModuleDumper.SamePeIdentity(disk, mapped));
        disk[0x88] ^= 1; // COFF timestamp
        Assert.False(ModuleDumper.SamePeIdentity(disk, mapped));
        disk[0x88] ^= 1;
        disk[0x188 + 12] ^= 1; // section RVA
        Assert.False(ModuleDumper.SamePeIdentity(disk, mapped));
    }

    private static void Put16(byte[] bytes, int at, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at, 2), value);
    private static void Put32(byte[] bytes, int at, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at, 4), value);
}
