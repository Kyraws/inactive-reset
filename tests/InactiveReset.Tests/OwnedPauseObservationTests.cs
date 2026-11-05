using InactiveReset.Core;
using InactiveReset.Reanchor;
using Xunit;

namespace InactiveReset.Tests;

public sealed class OwnedPauseObservationTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("read-failure")]
    [InlineData("cancel")]
    [InlineData("boundary-failure")]
    public void ReadOnlyObservationAlwaysReleasesOwnedPause(string scenario)
    {
        byte flag = 0; var writes = new List<byte>(); var events = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var pause = new PhysicsPause(() => true, () => flag, v => { flag = v; writes.Add(v); });
        int Observe()
        {
            Assert.Equal(1, flag);
            if (scenario == "read-failure") throw new IOException("read failed");
            if (scenario == "cancel") cancellation.Cancel();
            return 42;
        }
        var error = Record.Exception(() => OwnedPauseObservation.Run(pause, Observe, () =>
        {
            if (scenario == "boundary-failure") throw new GateException("boundary changed");
        }, events.Add, cancellation.Token));
        if (scenario == "success") Assert.Null(error); else Assert.NotNull(error);
        Assert.Equal(new byte[] { 1, 0 }, writes);
        Assert.False(pause.OwnsPause);
        Assert.Equal(0, flag);
        Assert.Contains(events, e => e.StartsWith("pause scope released"));
    }

    [Fact]
    public void PreCancelledAndExistingPausesAreNeverWritten()
    {
        foreach (var existingPause in new[] { false, true })
        {
            var writes = new List<byte>();
            using var cancellation = new CancellationTokenSource();
            if (!existingPause) cancellation.Cancel();
            var pause = new PhysicsPause(() => true, () => existingPause ? (byte)1 : (byte)0, writes.Add);
            Assert.ThrowsAny<Exception>(() => OwnedPauseObservation.Run<int>(pause,
                () => throw new InvalidOperationException("must not observe"), () => { }, _ => { }, cancellation.Token));
            Assert.Empty(writes);
        }
    }

    [Fact]
    public void ContextLossRefusesCleanupAndReportsRecovery()
    {
        byte flag = 0; var same = true; var writes = new List<byte>(); var events = new List<string>();
        var pause = new PhysicsPause(() => same, () => flag, v => { flag = v; writes.Add(v); });
        Assert.Throws<GateException>(() => OwnedPauseObservation.Run(pause, () => { same = false; return 42; },
            () => { }, events.Add, CancellationToken.None));
        Assert.Equal(new byte[] { 1 }, writes);
        Assert.Contains(events, e => e.Contains("cleanup failed; recovery required"));
        same = true; pause.Dispose();
    }

    [Fact]
    public void FailedResumeReadbackIsReportedAsRecoveryFailure()
    {
        byte flag = 0; var events = new List<string>();
        var pause = new PhysicsPause(() => true, () => flag, v =>
        {
            flag = v;
            if (v == 0) throw new MemoryAccessException("resume readback failed");
        });
        Assert.Throws<MemoryAccessException>(() => OwnedPauseObservation.Run(pause, () => 42,
            () => { }, events.Add, CancellationToken.None));
        Assert.Contains(events, e => e.Contains("cleanup failed; recovery required"));
        Assert.DoesNotContain(events, e => e.StartsWith("pause scope released"));
    }
}
