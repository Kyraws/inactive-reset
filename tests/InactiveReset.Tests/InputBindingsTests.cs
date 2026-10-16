using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

public sealed class InputBindingsTests
{
    [Fact]
    public void DefaultsAndCustomBindingsSurviveRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), "ir-bindings-" + Guid.NewGuid() + ".json");
        try
        {
            var defaults = InputBindings.Load(path);
            Assert.Equal(new KeyboardBinding(0x43, 3), defaults.Bindings["capture"].Keyboard);
            var custom = defaults.With("capture", new(new(117, 0), new("wheel-device", 65)));
            custom.Save(path);
            var loaded = InputBindings.Load(path);
            Assert.Equal(custom.Bindings["capture"], loaded.Bindings["capture"]);
            Assert.Equal(defaults.Bindings["teleport"], loaded.Bindings["teleport"]);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void DuplicateAssignmentsAreRefusedWithoutChangingOriginal()
    {
        var bindings = InputBindings.Defaults();
        Assert.Throws<ArgumentException>(() => bindings.With("next", bindings.Bindings["teleport"]));
        var wheel = bindings.With("capture", new(Wheel: new("DEVICE", 33)));
        Assert.Throws<ArgumentException>(() => wheel.With("next", new(Wheel: new("device", 33))));
        Assert.Equal(new KeyboardBinding(0x28, 3), bindings.Bindings["next"].Keyboard);
        Assert.Null(bindings.Bindings["capture"].Wheel);
        Assert.Throws<ArgumentException>(() => bindings.With("unknown", new()));
    }

    [Theory]
    [InlineData(65, 0)] [InlineData(17, 2)] [InlineData(27, 2)] [InlineData(91, 2)]
    [InlineData(65, 8)] [InlineData(256, 2)] [InlineData(160, 2)]
    public void InvalidKeyboardAssignmentsAreRefused(int key, int modifiers) =>
        Assert.Throws<ArgumentException>(() => InputBindings.Defaults().With("capture", new(new(key, modifiers))));

    [Fact]
    public void ClearingBindingsDoesNotRestoreDefaults()
    {
        var bindings = InputBindings.Defaults().With("capture", new());
        bindings.Validate();
        Assert.Null(bindings.Bindings["capture"].Keyboard);
        Assert.Null(bindings.Bindings["capture"].Wheel);
    }

    [Fact]
    public void NullAndUnknownActionMapsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new InputBindings { Bindings = null! }.Validate());
        Assert.Throws<ArgumentException>(() => new InputBindings { Bindings = new() { ["other"] = new() } }.Validate());
    }

    [Fact]
    public void HeldButtonsAndInitialReportsNeverRepeatActions()
    {
        var edges = new WheelButtonEdges();
        Assert.Empty(edges.Update("wheel", 0, [1])); // A held button on attach is not a press.
        Assert.Empty(edges.Update("wheel", 0, [1]));
        Assert.Empty(edges.Update("wheel", 0, []));
        Assert.Equal(new[] { 1, 65 }, edges.Update("WHEEL", 0, [1,65]));
        Assert.Empty(edges.Update("wheel", 0, [1,65]));
        Assert.Empty(edges.Update("wheel", 1, [])); // Another report cannot release report zero.
        Assert.Empty(edges.Update("wheel", 0, [1,65]));
        Assert.Empty(edges.Update("other wheel", 0, []));
        Assert.Equal(new[] { 1 }, edges.Update("other wheel", 0, [1]));
        edges.Remove("wheel");
        Assert.Empty(edges.Update("wheel", 0, [1,65])); // Reconnect while held is safe.
        Assert.Empty(edges.Update("wheel", 0, []));
        Assert.Equal(new[] { 65 }, edges.Update("wheel", 0, [65]));
    }
}
