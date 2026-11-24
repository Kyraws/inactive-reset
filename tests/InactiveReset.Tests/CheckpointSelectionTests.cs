using InactiveReset.Core;
using InactiveReset.Ui;
using Xunit;

namespace InactiveReset.Tests;

public sealed class CheckpointSelectionTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("inactive-reset-selection-").FullName;

    private Checkpoint Save(string track, string car, string name)
    {
        var checkpoint = new Checkpoint
        {
            Name = name, TrackName = track, VehicleName = car,
            Pose = new RecordedPose(new Vec3(1, 2, 3), new Vec3(1, 0, 0),
                new Vec3(0, 1, 0), new Vec3(0, 0, 1))
        };
        return Checkpoint.Load(CaptureService.Save(checkpoint, _directory));
    }

    [Fact]
    public void SharedPlacementResolutionRequiresLiveIdentityForSavedAndRecordedDestinations()
    {
        var checkpoint = Save("Barcelona", "Ferrari", "End of Lap");
        Assert.Throws<GateException>(() => PlacementService.ResolveDestination(
            _directory, _directory, checkpoint.SelectionId(_directory)));
        Assert.Throws<GateException>(() => PlacementService.ResolveDestination(
            _directory, _directory, checkpoint.Name, checkpoint));
    }

    [Fact]
    public void CaseInsensitiveNameIsResolvedOnTheLoadedTrack()
    {
        Save("Barcelona", "Ferrari", "End of Lap");
        var daytona = Save("Daytona", "McLaren", "eND OF LAP");
        var selected = Checkpoint.Require(_directory, "End of Lap", new SessionIdentity("daytona", "BMW"));
        Assert.Equal(daytona.SourcePath, selected.SourcePath);
        Assert.Throws<CheckpointException>(() => Checkpoint.Require(_directory, "End of Lap"));
    }

    [Fact]
    public void SelectionIdKeepsSameNameCapturesDistinctAndAllowsAnotherCar()
    {
        var ferrari = Save("Daytona", "Ferrari", "End of Lap");
        var bmw = Save("Daytona", "BMW", "End of Lap");
        var identity = new SessionIdentity("Daytona", "BMW");
        Assert.NotEqual(ferrari.SelectionId(_directory), bmw.SelectionId(_directory));
        Assert.Equal(bmw.SourcePath, Checkpoint.Require(_directory, "End of Lap", identity).SourcePath);
        var selected = Checkpoint.Require(_directory, ferrari.SelectionId(_directory), identity);
        Assert.Equal(ferrari.SourcePath, selected.SourcePath);
        identity.RequireMatches(selected);
    }

    [Fact]
    public void RemainingAmbiguityIsRefusedWithIds()
    {
        var first = Save("Daytona", "Ferrari", "End of Lap");
        Save("Daytona", "BMW", "End of Lap");
        var ex = Assert.Throws<CheckpointException>(() => Checkpoint.Require(_directory,
            "End of Lap", new SessionIdentity("Daytona", "McLaren")));
        Assert.Contains(first.SelectionId(_directory), ex.Message);
    }

    [Fact]
    public void ExplicitIdFromAnotherTrackIsStillRejectedByTrackGate()
    {
        var barcelona = Save("Barcelona", "Ferrari", "End of Lap");
        var selected = Checkpoint.Require(_directory, barcelona.SelectionId(_directory));
        Assert.Throws<CheckpointException>(() => new SessionIdentity("Daytona", "Ferrari").RequireMatches(selected));
    }

    [Fact]
    public void PersistentQuickCapturesAndConfirmedClearPreserveOtherScopesAndNamedCopies()
    {
        var named = Save("Daytona", "Ferrari", "End of Lap");
        var quick = new Checkpoint
        {
            Name = CaptureService.QuickCaptureName, TrackName = named.TrackName,
            VehicleName = named.VehicleName, Pose = named.Pose, CapturedUtc = DateTimeOffset.UtcNow,
            Gear = 2, LocalVelocityMetresPerSecond = new(0, 0, -15),
        };
        string Append(Checkpoint capture) => Checkpoint.Load(CaptureService.Save(capture,
            _directory, quick: true, appendQuick: true)).SelectionId(_directory);
        var first = Append(quick);
        var second = Append(quick);
        var otherCar = Append(new Checkpoint
        {
            Name = quick.Name, TrackName = quick.TrackName, VehicleName = "BMW",
            Pose = quick.Pose, Gear = quick.Gear, LocalVelocityMetresPerSecond = quick.LocalVelocityMetresPerSecond,
        });
        Assert.NotEqual(first, second);
        var library = new CheckpointLibrary(_directory);
        var copy = library.RenameOrCopy(first, "Copy", copy: true);
        Assert.Equal(quick.Pose, Checkpoint.Require(_directory, copy).Pose);
        Assert.Equal(quick.CapturedUtc, Checkpoint.Require(_directory, copy).CapturedUtc);
        Assert.Throws<CheckpointException>(() => library.ClearQuick("Daytona", "Ferrari", new HashSet<string> { first }));
        Assert.Equal(2, library.ClearQuick("Daytona", "Ferrari", new HashSet<string> { first, second }));
        Assert.Equal(new[] { copy, named.SelectionId(_directory), otherCar }.Order(),
            Checkpoint.LoadAll(_directory).Select(c => c.SelectionId(_directory)).Order());
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
