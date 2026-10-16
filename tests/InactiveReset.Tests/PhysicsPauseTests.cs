using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

public sealed class PhysicsPauseTests
{
    [Fact]
    public void OwnedPauseRestoresExactlyOnce()
    {
        byte flag = 0; var writes = new List<byte>();
        using var pause = new PhysicsPause(() => true, () => flag, v => { writes.Add(v); flag = v; });
        pause.Begin(); Assert.True(pause.IsPaused); Assert.True(pause.OwnsPause);
        pause.Dispose(); pause.Dispose();
        Assert.Equal(new byte[] { 1, 0 }, writes); Assert.False(pause.OwnsPause);
        Assert.Throws<GateException>(() => pause.Begin());
    }

    [Fact]
    public void ExistingDriverPauseIsNeverOwnedOrResumed()
    {
        var writes = new List<byte>();
        using var pause = new PhysicsPause(() => true, () => 1, writes.Add);
        Assert.Throws<GateException>(() => pause.Begin());
        pause.Dispose(); Assert.Empty(writes);
    }

    [Fact]
    public void ChangedSessionAndInvalidFlagsRefuseCleanupWrites()
    {
        byte flag = 0; var same = true; var writes = new List<byte>();
        var pause = new PhysicsPause(() => same, () => flag, v => { flag = v; writes.Add(v); });
        pause.Begin(); same = false;
        Assert.Throws<GateException>(() => pause.Dispose()); Assert.Equal(new byte[] { 1 }, writes);
        same = true; flag = 2;
        Assert.Throws<GateException>(() => pause.Dispose()); Assert.Equal(new byte[] { 1 }, writes);
        flag = 1; pause.Dispose(); Assert.Equal(new byte[] { 1, 0 }, writes);
    }

    [Fact]
    public void FailedReadbackAfterPauseWriteAttemptsOwnedCleanup()
    {
        byte flag = 0; var writes = new List<byte>();
        using var pause = new PhysicsPause(() => true, () => flag, v =>
        {
            flag = v; writes.Add(v);
            if (v == 1) throw new MemoryAccessException("simulated failed readback after write");
        });
        Assert.Throws<MemoryAccessException>(() => pause.Begin());
        Assert.Equal(new byte[] { 1, 0 }, writes); Assert.Equal(0, flag);
    }

    [Fact]
    public void PartialMotionCanRemainPausedForExplicitRecovery()
    {
        byte flag = 0; var writes = new List<byte>();
        using var pause = new PhysicsPause(() => true, () => flag, v => { flag = v; writes.Add(v); });
        pause.Begin(); pause.LeavePaused(); pause.Dispose();
        Assert.Equal(1, flag); Assert.Equal(new byte[] { 1 }, writes);
    }
}
