using InactiveReset.Reanchor;
using Xunit;

namespace InactiveReset.Tests;

public sealed class IndexedSpotProbeTests
{
    [Fact]
    public void Shift_changes_only_the_selected_position_and_keeps_the_tail_outside_payload()
    {
        var original = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        BitConverter.GetBytes(119.5f).CopyTo(original, 0);
        var payload = IndexedSpotProbe.ShiftComponent(original, "x", 2f);
        Assert.Equal(24, payload.Length);
        Assert.Equal(121.5f, BitConverter.ToSingle(payload, 0));
        Assert.True(payload.AsSpan(4).SequenceEqual(original.AsSpan(4, 20)));
        Assert.True(IndexedSpotProbe.CanRestore(original, original, payload));
        var partial = original.ToArray();
        partial[0] = payload[0];
        Assert.True(IndexedSpotProbe.CanRestore(partial, original, payload));
        partial[24]++;
        Assert.True(IndexedSpotProbe.CanRestore(partial, original, payload));
        partial = original.ToArray();
        partial[7]++;
        Assert.False(IndexedSpotProbe.CanRestore(partial, original, payload));
        var zPayload = IndexedSpotProbe.ShiftComponent(original, "z", 2f);
        Assert.Equal(BitConverter.ToSingle(original, 8) + 2f, BitConverter.ToSingle(zPayload, 8));
        Assert.True(zPayload.AsSpan(0, 8).SequenceEqual(original.AsSpan(0, 8)));
        Assert.True(zPayload.AsSpan(12).SequenceEqual(original.AsSpan(12, 12)));
        var yawPayload = IndexedSpotProbe.ShiftComponent(original, "yaw", 0.1f);
        Assert.Equal(BitConverter.ToSingle(original, 16) + 0.1f, BitConverter.ToSingle(yawPayload, 16));
        Assert.True(yawPayload.AsSpan(0, 16).SequenceEqual(original.AsSpan(0, 16)));
        Assert.Throws<ArgumentException>(() => IndexedSpotProbe.ShiftComponent(original, "x", 6f));
        Assert.Throws<ArgumentException>(() => IndexedSpotProbe.ShiftComponent(original, "yaw", 0.3f));
    }
}
