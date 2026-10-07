using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

public sealed class PlacementCleanupTests
{
    [Theory]
    [InlineData((int)PitWaitEnd.Cleared)]
    [InlineData((int)PitWaitEnd.Garage)]
    [InlineData((int)PitWaitEnd.SessionEnded)]
    public void CancellationDoesNotRestoreRulesWhileDriving(int endValue)
    {
        var end = (PitWaitEnd)endValue;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var polls = 0;
        var notifications = 0;
        var restored = false;
        using var lease = new PlacementRestoreScope(true, () => restored = true,
            () => end != PitWaitEnd.SessionEnded);
        var result = PlacementCleanup.WaitForSafeRestore(
            () => polls++ < 2501 ? PitWaitEnd.Active : end,
            () => Assert.False(restored), cancellation.Token, () => notifications++);
        // 2501 production polling intervals exceed the old 120-second limit.
        Assert.Equal(end, result);
        Assert.Equal(1, notifications);
        lease.Restore();
        Assert.Equal(end != PitWaitEnd.SessionEnded, restored);
        Assert.Equal(end == PitWaitEnd.SessionEnded, lease.SessionChanged);
    }

    [Fact]
    public void ScopeExitRestoresTheOriginalValueOnce()
    {
        var value = 0;
        var writes = 0;
        var lease = new PlacementRestoreScope(true, () => { value = 3; writes++; }, () => true);
        using (lease) { }
        lease.Restore();
        Assert.Equal(3, value);
        Assert.Equal(1, writes);
    }

    [Fact]
    public void FailedRestorationRemainsPendingForRetry()
    {
        var calls = 0;
        using var lease = new PlacementRestoreScope(true,
            () => { if (++calls == 1) throw new MemoryAccessException("restore failed"); }, () => true);
        Assert.Throws<MemoryAccessException>(lease.Restore);
        Assert.True(lease.Pending);
        lease.Restore();
        Assert.False(lease.Pending);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void DestroyedSessionDoesNotReceiveSavedSpotBytes()
    {
        var current = true;
        var writes = 0;
        using var transaction = new SpotWriteTransaction((_, _) => writes++, 0x1000,
            new byte[24], () => current);
        transaction.Write(new byte[24]);
        current = false;
        transaction.Restore();
        Assert.False(transaction.Active);
        Assert.Equal(1, writes);
        Assert.Throws<GateException>(() => transaction.Write(new byte[24]));
        Assert.Equal(1, writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailedSuppressionRestoresExactOriginalValue(bool failedWrite)
    {
        var value = 3;
        var writes = 0;
        Assert.Throws<MemoryAccessException>(() => RulesController.WriteWithRollback(3, 0,
            requested =>
            {
                value = requested;
                if (++writes == 1 && failedWrite) throw new MemoryAccessException("partial write");
            }, () => writes == 1 ? 1 : value));
        Assert.Equal(3, value);
        Assert.Equal(2, writes);
    }

    [Fact]
    public void FailedSuppressionAndRollbackReportBothFailures()
    {
        var failure = Assert.Throws<AggregateException>(() => RulesController.WriteWithRollback(3, 0,
            _ => throw new MemoryAccessException("write denied"), () => 3));
        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.Contains("original value could not be restored", failure.Message);
    }
}
