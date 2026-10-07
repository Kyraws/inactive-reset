using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

public sealed class TyreStateTests
{
    [Fact]
    public void DecodePreservesWheelOrderWearAndCompoundAndConvertsKelvin()
    {
        const int telem = 1000;
        var offsets = new TyreTelemetryOffsets(826, 620, 638, 18,
            848, 260, 128, 152, 204, 212, 240, 241, 198, 199);
        var doubles = new Dictionary<int, double>();
        var bytes = new Dictionary<int, byte> { [telem + 826] = 6 };
        for (var i = 0; i < 4; i++)
        {
            int wheel = telem + 848 + i * 260;
            for (var j = 0; j < 3; j++)
            {
                doubles[wheel + 128 + j * 8] = 273.15 + 70 + i * 10 + j;
                doubles[wheel + 212 + j * 8] = 273.15 + 60 + i * 10 + j;
            }
            doubles[wheel + 204] = 273.15 + 55 + i;
            doubles[wheel + 152] = 0.9 - i * 0.1;
            bytes[wheel + 240] = (byte)(i + 3);
            bytes[wheel + 241] = 1;
            bytes[wheel + 198] = (byte)(i == 1 ? 1 : 0);
            bytes[wheel + 199] = (byte)(i == 3 ? 1 : 0);
        }
        var state = SharedMemoryReader.DecodeTyres(telem, offsets,
            address => doubles[address], address => bytes[address],
            (address, length) =>
            {
                Assert.Equal(18, length);
                return address == telem + 620 ? "Medium" : "Wet";
            });
        Assert.Equal((byte)6, state.VehicleClass);
        Assert.Equal("Medium", state.FrontCompound);
        Assert.Equal("Wet", state.RearCompound);
        var wheels = new[] { state.FrontLeft, state.FrontRight, state.RearLeft, state.RearRight };
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(70 + i * 10, wheels[i].SurfaceCelsius.Left, 8);
            Assert.Equal(71 + i * 10, wheels[i].SurfaceCelsius.Centre, 8);
            Assert.Equal(72 + i * 10, wheels[i].SurfaceCelsius.Right, 8);
            Assert.Equal(55 + i, wheels[i].CarcassCelsius, 8);
            Assert.Equal(62 + i * 10, wheels[i].InnerLayerCelsius.Right, 8);
            Assert.Equal(0.9 - i * 0.1, wheels[i].WearRaw);
            Assert.Equal((byte)(i + 3), wheels[i].CompoundIndex);
            Assert.Equal((byte)1, wheels[i].CompoundType);
            Assert.Equal(i == 1, wheels[i].Flat);
            Assert.Equal(i == 3, wheels[i].Detached);
        }

        var directory = Path.Combine(Path.GetTempPath(), "ir-tyres-" + Guid.NewGuid().ToString("N"));
        try
        {
            var checkpoint = new Checkpoint
            {
                Name = "tyres", TrackName = "Test track", VehicleName = "Test car",
                Pose = new RecordedPose(new(1, 2, 3), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)),
                Tyres = state,
            };
            var path = CaptureService.Save(checkpoint, directory);
            Assert.Equal(state, Checkpoint.Load(path).Tyres);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(0, "Soft,Medium,Hard,Wet")]
    [InlineData(5, "Soft,Medium,Hard,Wet")]
    [InlineData(2, "Medium,Wet")]
    [InlineData(3, "Medium,Wet")]
    [InlineData(4, "Medium,Wet")]
    [InlineData(6, "Medium,Wet")]
    [InlineData(8, "")]
    [InlineData(255, "")]
    public void CompoundPolicyUsesSdkClass(byte vehicleClass, string expected) =>
        Assert.Equal(expected, string.Join(',', TyreState.AllowedCompounds(vehicleClass)));
}
