using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

public sealed class RollingBrakeHoldTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreservesOriginalPreferenceAndRestoresOnlyOnce(bool original)
    {
        var value = original; var writes = new List<bool>();
        var hold = new RollingBrakeHold(() => value, v => { value = v; writes.Add(v); }, () => true, _ => { });
        hold.Disable(); Assert.False(value);
        hold.Restore(); hold.Dispose();
        Assert.Equal(original, value);
        Assert.Equal(original ? new[] { false, true } : Array.Empty<bool>(), writes);
    }

    [Fact]
    public void FailedDisableResponseRestoresAChangeThatWasActuallyApplied()
    {
        var value = true;
        var hold = new RollingBrakeHold(() => value, v =>
        {
            value = v;
            if (!v) throw new GateException("Lost response after write");
        }, () => true, _ => { });
        Assert.Throws<GateException>(hold.Disable);
        Assert.True(value);
    }

    [Fact]
    public void IgnoredDisableIsRejectedWithoutOverwritingOriginal()
    {
        var writes = new List<bool>();
        var hold = new RollingBrakeHold(() => true, writes.Add, () => true, _ => { });
        Assert.Throws<GateException>(hold.Disable);
        Assert.Equal(new[] { false }, writes);
    }

    [Fact]
    public void CancellationOrOtherFailureOnScopeExitRestoresThePreference()
    {
        var value = true;
        Assert.Throws<OperationCanceledException>((Action)(() =>
        {
            using var hold = new RollingBrakeHold(() => value, v => value = v, () => true, _ => { });
            hold.Disable();
            throw new OperationCanceledException();
        }));
        Assert.True(value);
    }

    [Fact]
    public void FailedRestorationIsReportedAndRemainsPendingForCleanup()
    {
        var value = true; var refuseRestore = true;
        var hold = new RollingBrakeHold(() => value, v => { if (!v || !refuseRestore) value = v; }, () => true, _ => { });
        hold.Disable();
        Assert.Throws<GateException>(hold.Restore);
        Assert.False(value);
        refuseRestore = false; hold.Dispose(); Assert.True(value);
    }

    [Fact]
    public void ContextChangeBlocksWritingToAReplacementGame()
    {
        var value = true; var same = true; var writes = 0;
        var hold = new RollingBrakeHold(() => value, v => { value = v; writes++; }, () => same, _ => { });
        hold.Disable(); same = false;
        Assert.Throws<GateException>(hold.Restore);
        Assert.Equal(1, writes);
    }

    [Fact]
    public void AlreadyRestoredPreferenceDoesNotGetAnotherWrite()
    {
        var value = true; var writes = 0;
        var hold = new RollingBrakeHold(() => value, v => { value = v; writes++; }, () => true, _ => { });
        hold.Disable(); value = true; hold.Restore();
        Assert.Equal(1, writes);
    }
}
