using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

public sealed class SpotWriteTransactionTests
{
    private const ulong Address = 0x1000;
    private static byte[] Original() => Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static byte[] Payload() => Enumerable.Repeat((byte)0xA5, 24).ToArray();

    [Fact]
    public void Write_and_restore_preserve_the_original_entry_and_padding()
    {
        var memory = Original();
        var before = memory.ToArray();
        var writes = 0;
        void Write(ulong address, ReadOnlySpan<byte> bytes)
        {
            Assert.Equal(Address, address);
            Assert.Equal(24, bytes.Length);
            bytes.CopyTo(memory);
            writes++;
        }

        using var transaction = new SpotWriteTransaction(Write, Address, before[..24]);
        transaction.Write(Payload());
        Assert.True(transaction.Active);
        Assert.Equal(Payload(), memory[..24]);
        Assert.Equal(before[24..], memory[24..]);

        transaction.Restore();
        Assert.False(transaction.Active);
        Assert.Equal(before, memory);
        transaction.Restore();
        transaction.Dispose();
        Assert.Equal(2, writes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    [InlineData(25)]
    [InlineData(32)]
    public void Invalid_payload_size_never_reaches_the_memory_writer(int size)
    {
        var writes = 0;
        using var transaction = new SpotWriteTransaction((_, _) => writes++, Address, Original()[..24]);
        Assert.Throws<MemoryAccessException>(() => transaction.Write(new byte[size]));
        Assert.False(transaction.Active);
        Assert.Equal(0, writes);
    }

    [Fact]
    public void A_partial_write_failure_rolls_back_before_returning_the_error()
    {
        var memory = Original();
        var before = memory.ToArray();
        var writes = 0;
        var failure = new MemoryAccessException("readback disagrees after partial write");
        void Write(ulong address, ReadOnlySpan<byte> bytes)
        {
            Assert.Equal(Address, address);
            writes++;
            if (writes == 1)
            {
                bytes[..7].CopyTo(memory);
                throw failure;
            }
            bytes.CopyTo(memory);
        }

        using var transaction = new SpotWriteTransaction(Write, Address, before[..24]);
        Assert.Same(failure, Assert.Throws<MemoryAccessException>(() => transaction.Write(Payload())));
        Assert.Equal(before, memory);
        Assert.False(transaction.Active);
        Assert.Equal(2, writes);
    }

    [Fact]
    public void Exceptional_scope_exit_restores_the_entry()
    {
        var memory = Original();
        var before = memory.ToArray();
        void ArmThenFail()
        {
            using var transaction = new SpotWriteTransaction((_, bytes) => bytes.CopyTo(memory), Address, before[..24]);
            transaction.Write(Payload());
            throw new InvalidOperationException("failure after arming");
        }
        Assert.Throws<InvalidOperationException>(ArmThenFail);
        Assert.Equal(before, memory);
    }

    [Fact]
    public void A_failed_restore_remains_active_and_can_be_retried()
    {
        var memory = Original();
        var before = memory.ToArray();
        var writes = 0;
        void Write(ulong _, ReadOnlySpan<byte> bytes)
        {
            writes++;
            if (writes == 2) throw new MemoryAccessException("restore unavailable");
            bytes.CopyTo(memory);
        }

        using var transaction = new SpotWriteTransaction(Write, Address, before[..24]);
        transaction.Write(Payload());
        Assert.Throws<MemoryAccessException>(() => transaction.Restore());
        Assert.True(transaction.Active);
        transaction.Restore();
        Assert.False(transaction.Active);
        Assert.Equal(before, memory);
        Assert.Equal(3, writes);
    }

    [Fact]
    public void A_failed_rollback_is_reported_and_disposal_retries_restoration()
    {
        var memory = Original();
        var before = memory.ToArray();
        var writes = 0;
        var rollbackFailure = new MemoryAccessException("rollback readback failed");
        void Write(ulong _, ReadOnlySpan<byte> bytes)
        {
            writes++;
            if (writes == 1)
            {
                bytes[..7].CopyTo(memory);
                throw new MemoryAccessException("payload write failed");
            }
            if (writes == 2) throw rollbackFailure;
            bytes.CopyTo(memory);
        }

        using var transaction = new SpotWriteTransaction(Write, Address, before[..24]);
        Assert.Same(rollbackFailure, Assert.Throws<MemoryAccessException>(() => transaction.Write(Payload())));
        Assert.True(transaction.Active);
        transaction.Dispose();
        Assert.False(transaction.Active);
        Assert.Equal(before, memory);
        Assert.Equal(3, writes);
    }

    [Fact]
    public void Disposal_without_a_payload_does_not_write_memory()
    {
        var writes = 0;
        using (new SpotWriteTransaction((_, _) => writes++, Address, Original()[..24])) { }
        Assert.Equal(0, writes);
    }
}
