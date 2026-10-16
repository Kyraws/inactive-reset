using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.Json.Nodes;
using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

public sealed class MotionCaptureTests
{
    [Fact]
    public void ReaderAndCapturePreserveVelocityDirectionAndConvertSpeedToKmh()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ir-motion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var layout = JsonNode.Parse(File.ReadAllText(ProfileFixture.PathFor("shared-memory")))!.AsObject();
            var name = "ir-motion-" + Guid.NewGuid().ToString("N");
            layout["mapName"] = name;
            var path = Path.Combine(directory, "layout.json");
            File.WriteAllText(path, layout.ToJsonString());
            var offsets = SharedMemoryOffsets.Load(path);
            Assert.NotNull(offsets.TelemLocalVelocity);
            using var mapping = MemoryMappedFile.CreateNew(name, offsets.LayoutSize);
            using var view = mapping.CreateViewAccessor();
            view.Write(offsets.PlayerHasVehicle, (byte)1);
            view.Write(offsets.PlayerVehicleIdx, (byte)0);
            view.Write(offsets.ScoringInfo + offsets.NumVehicles, 1);
            view.Write(offsets.VehScoringInfo + offsets.VehIsPlayer, (byte)1);
            void Text(int address, string text)
            {
                var bytes = Encoding.ASCII.GetBytes(text + "\0");
                view.WriteArray(address, bytes, 0, bytes.Length);
            }
            Text(offsets.ScoringInfo + offsets.TrackName, "Test track");
            Text(offsets.VehScoringInfo + offsets.VehName, "Test car");
            var telem = offsets.TelemInfo;
            view.Write(telem + offsets.TelemGear, 3);
            view.Write(telem + offsets.TelemElapsedTime, 42.5);
            for (var i = 0; i < 3; i++) view.Write(telem + offsets.TelemOri + i * offsets.Vect3Stride + i * 8, 1.0);
            var velocity = telem + offsets.TelemLocalVelocity.Value;
            view.Write(velocity, -3.0);
            view.Write(velocity + 8, 4.0);
            view.Write(velocity + 16, -12.0);
            using var reader = new SharedMemoryReader(offsets);
            var capture = new CaptureService(reader).Capture("moving");
            Assert.True(capture.Usable);
            Assert.Equal(new Vec3(-3, 4, -12), capture.Snapshot.LocalVelocityMetresPerSecond);
            Assert.Equal(46.8, capture.Snapshot.SpeedKmh!.Value, 6);
            var saved = CaptureService.Save(capture.Checkpoint, Path.Combine(directory, "checkpoints"));
            var loaded = Checkpoint.Load(saved);
            Assert.Equal(capture.Snapshot.LocalVelocityMetresPerSecond, loaded.LocalVelocityMetresPerSecond);
            Assert.Equal(46.8, loaded.SpeedKmh!.Value, 6);
            Assert.Equal(3, loaded.Gear);
            // Old generated layouts have no velocity channel, so speed must remain unknown, not zero.
            layout["telemInfo"]!.AsObject().Remove("mLocalVel");
            File.WriteAllText(path, layout.ToJsonString());
            using var legacy = new SharedMemoryReader(SharedMemoryOffsets.Load(path));
            Assert.Null(legacy.Read()!.LocalVelocityMetresPerSecond);
            view.Write(velocity, 0.0);
            view.Write(velocity + 8, 0.0);
            view.Write(velocity + 16, 0.0);
            Assert.Equal(0.0, reader.Read()!.SpeedKmh);
            view.Write(velocity, double.NaN);
            Assert.Throws<CheckpointException>(() => reader.Read());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("local_velocity_mps", 46.8)]
    [InlineData("local_velocity", 46.8)]
    [InlineData("local_angular_velocity", null)]
    public void ExistingMotionBlocksLoadWithEitherVelocityNameOrWithoutVelocity(string field, double? expectedSpeed)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ir-legacy-motion-" + Guid.NewGuid().ToString("N"));
        try
        {
            var checkpoint = new Checkpoint
            {
                Name = "legacy", TrackName = "Test track", VehicleName = "Test car",
                Pose = new(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)),
            };
            var path = CaptureService.Save(checkpoint, directory);
            var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            json["motion"] = new JsonObject
            {
                [field] = new JsonObject { ["x"] = -3, ["y"] = 4, ["z"] = -12 },
            };
            File.WriteAllText(path, json.ToJsonString());
            var loaded = Checkpoint.Load(path);
            if (expectedSpeed is { } speed) Assert.Equal(speed, loaded.SpeedKmh!.Value, 6);
            else Assert.Null(loaded.SpeedKmh);
            Assert.Single(Checkpoint.LoadAll(directory));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void NonFiniteVelocityIsRefused(float bad) =>
        Assert.Throws<CheckpointException>(() => MotionCapture.SpeedKmh(new(bad, 0, 0)));
}
