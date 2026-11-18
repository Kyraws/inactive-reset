using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.Json.Nodes;
using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

public sealed class MotionCaptureTests
{
    [Fact]
    public async Task ReaderAndCapturePreserveVelocityDirectionAndConvertSpeedToKmh()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ir-motion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var layout = JsonNode.Parse(File.ReadAllText(ProfileFixture.PathFor("shared-memory")))!.AsObject();
            var name = "ir-motion-" + Guid.NewGuid().ToString("N");
            layout["mapName"] = name;
            layout["synchronization"] = new JsonObject
            {
                ["lockMap"] = name + "-lock", ["lockEvent"] = name + "-wake",
                ["holdEvent"] = name + "-hold", ["dataEvent"] = name + "-data",
            };
            using var sdkLock = MemoryMappedFile.CreateNew(name + "-lock", 8);
            using var lockView = sdkLock.CreateViewAccessor();
            using var wake = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-wake");
            using var hold = new EventWaitHandle(true, EventResetMode.ManualReset, name + "-hold");
            using var data = new EventWaitHandle(true, EventResetMode.ManualReset, name + "-data");
            var path = Path.Combine(directory, "layout.json");
            File.WriteAllText(path, layout.ToJsonString());
            var offsets = SharedMemoryOffsets.Load(path);
            Assert.NotNull(offsets.TelemLocalVelocity);
            using var mapping = MemoryMappedFile.CreateNew(name, offsets.LayoutSize);
            using var view = mapping.CreateViewAccessor();
            view.Write(offsets.PlayerHasVehicle, (byte)1);
            view.Write(offsets.PlayerVehicleIdx, (byte)0);
            view.Write(offsets.ActiveVehicles!.Value, (byte)1);
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
            var scoring = offsets.Layout.GetProperty("scoringInfo");
            var fields = offsets.Layout.GetProperty("telemInfo");
            view.Write(offsets.ScoringInfo + scoring.GetProperty("mInRealtime").GetInt32(), (byte)1);
            view.Write(offsets.ScoringInfo + scoring.GetProperty("mGamePhase").GetInt32(), (byte)5);
            Text(telem + fields.GetProperty("mTrackName").GetInt32(), "Test track");
            Text(telem + fields.GetProperty("mVehicleName").GetInt32(), "Test car");
            view.Write(telem + offsets.TelemGear, 3);
            view.Write(telem + offsets.TelemElapsedTime, 42.5);
            for (var i = 0; i < 3; i++) view.Write(telem + offsets.TelemOri + i * offsets.Vect3Stride + i * 8, 1.0);
            var velocity = telem + offsets.TelemLocalVelocity.Value;
            view.Write(velocity, -3.0);
            view.Write(velocity + 8, 4.0);
            view.Write(velocity + 16, -12.0);
            using var reader = new SharedMemoryReader(offsets);
            Assert.True(reader.WaitForFrame(0));
            lockView.Write(4, 1);
            Assert.Throws<SharedMemoryException>(() => reader.Read());
            lockView.Write(4, 0);
            var service = new CaptureService(reader);
            Assert.Contains("No fresh driving frame", Assert.Throws<CheckpointException>(() => service.Capture("moving")).Message);
            Text(telem + fields.GetProperty("mVehicleName").GetInt32(), "Other car");
            Assert.Contains("identity-mismatch", Assert.Throws<CheckpointException>(() => service.Capture("moving")).Message);
            Text(telem + fields.GetProperty("mVehicleName").GetInt32(), "Test car");
            lockView.Write(0, 1);
            var updated = Task.Factory.StartNew(() =>
            {
                if (!wake.WaitOne(1000)) return false;
                view.Write(telem + offsets.TelemElapsedTime, 42.52);
                return true;
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            var capture = service.Capture("moving");
            Assert.Equal(0, lockView.ReadInt32(4));
            Assert.True(await updated);
            lockView.Write(0, 0);
            view.Write(offsets.PlayerVehicleIdx, (byte)104);
            Assert.Throws<SharedMemoryException>(() => reader.Read());
            Assert.Equal(0, lockView.ReadInt32(4));
            view.Write(offsets.PlayerVehicleIdx, (byte)0);
            Assert.True(capture.Usable);
            Assert.Equal(new Vec3(-3, 4, -12), capture.Snapshot.LocalVelocityMetresPerSecond);
            Assert.Equal(46.8, capture.Snapshot.SpeedKmh!.Value, 6);
            var saved = CaptureService.Save(capture.Checkpoint, Path.Combine(directory, "checkpoints"));
            var loaded = Checkpoint.Load(saved);
            Assert.Equal(42.52, loaded.CapturedSimulationTime);
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
