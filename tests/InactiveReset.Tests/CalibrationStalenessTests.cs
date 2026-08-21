using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

/// <summary>
/// The staleness warning, and the two-directory calibration lookup.
///
/// Both exist because calibration is the one thing in this project that cannot
/// be re-derived automatically. The offset profile is gated on the executable
/// hash and re-downloaded when the game patches; a calibration is measured by a
/// human and just sits there, silently describing a build that no longer runs.
///
/// The response is deliberately a WARNING and not a gate. Being wrong about
/// offsets means writing bytes to the wrong addresses; being wrong about a
/// calibration means the car stops half a metre away. The severity of the check
/// matches the blast radius of the mistake.
/// </summary>
public sealed class CalibrationStalenessTests
{
    private const string BuildA = "0F6DCAC1524A3802C8F950B44DE4CA83ACB8C836DCD70253A875C36F3EFA144C";
    private const string BuildB = "1AC2F6059AA1FBC948373DF6883B83E91F82728330F1458D8C9D935C8CFB709D";

    private static PlacementOutcome Placed(float horizontalError) => new()
    {
        Completed = true,
        Message = "placed",
        HorizontalErrorMetres = horizontalError,
        VerticalErrorMetres = 0.0009f,
    };

    private static CalibrationProfile Calibration(
        float worstError = 0.0005f, string? build = BuildA) => new()
    {
        TrackName = "Circuit de Barcelona",
        VehicleName = "Richard Mille AF Corse 2025 #50:ELMS",
        ForwardDistance = 3.049423f,
        VerticalOffset = 0.373062f,
        LateralOffset = 0.503253f,
        Valid = true,
        ExecutableSha256 = build,
        WorstHorizontalErrorMetres = worstError,
    };

    private static OutcomeLine? Staleness(
        float error, CalibrationProfile? calibration, string? runningBuild) =>
        PlacementReport.StalenessLine(Placed(error), calibration, runningBuild);

    [Fact]
    public void AGoodPlacementSaysNothing()
    {
        // 0.5 mm, the verified figure. Silence is the correct output: a report
        // that warns when everything is fine trains people to ignore it.
        Assert.Null(Staleness(0.0005f, Calibration(), BuildA));
    }

    [Fact]
    public void APlacementWithinTheGoodThresholdSaysNothingEvenOnAnotherBuild()
    {
        // The build hash is NOT the trigger. A patch that does not move D and L
        // must produce no warning at all -- which is the whole reason this is a
        // measurement rather than a hash check.
        Assert.Null(Staleness(0.03f, Calibration(), BuildB));
    }

    [Fact]
    public void AMissMuchWorseThanRecordedWarnsAndBlamesTheBuild()
    {
        // 0.71 m: what the old constants produced on the new build. This is the
        // case the warning exists for.
        var line = Staleness(0.71f, Calibration(), BuildB);

        Assert.NotNull(line);
        Assert.Equal(OutcomeSeverity.Warning, line.Severity);
        Assert.Contains("0.710", line.Sentence);
        Assert.Contains("0.001", line.Sentence);      // the recorded 0.0005, formatted
        Assert.Contains("0F6DCAC1", line.Sentence);   // measured on
        Assert.Contains("1AC2F605", line.Sentence);   // running
        Assert.Contains("stale", line.Sentence);
    }

    [Fact]
    public void AMissOnTheSameBuildWarnsButDoesNotBlameTheBuild()
    {
        // The 0.185 m the Hypercar produced: a real miss with the correct build,
        // so the report must not offer a build change as the explanation.
        var line = Staleness(0.185f, Calibration(), BuildA);

        Assert.NotNull(line);
        Assert.Equal(OutcomeSeverity.Warning, line.Severity);
        Assert.DoesNotContain("stale", line.Sentence);
        Assert.Contains("something else", line.Sentence);
    }

    [Fact]
    public void ACalibrationWithNoRecordedErrorIsNotTreatedAsPerfect()
    {
        // worst_horizontal_error_m defaults to 0, which means NEVER MEASURED.
        // Reading it as "this calibration has never missed" would suppress the
        // warning exactly when there is least reason to trust the numbers.
        var line = Staleness(0.71f, Calibration(worstError: 0f), BuildA);

        Assert.NotNull(line);
        Assert.Contains("no measured error recorded", line.Sentence);
    }

    [Fact]
    public void NothingIsSaidWithoutACalibrationToCompareAgainst()
    {
        Assert.Null(Staleness(0.71f, calibration: null, BuildA));
    }

    [Fact]
    public void TheWarningNeverRefuses()
    {
        // Guards the design decision. If this ever becomes a gate, a game patch
        // bricks placement for everyone until they re-measure -- and there is no
        // calibrate command to re-measure with.
        var lines = PlacementReport.For(Placed(0.71f), Calibration(), BuildB);

        Assert.Contains(lines, l => l.Label == "result" && l.Value == "placed");
        Assert.Contains(lines, l => l.Label == "calibration");
    }

    // ---- the two directories ----------------------------------------------

    [Fact]
    public void AUserCalibrationShadowsAShippedOneForTheSamePair()
    {
        var data = Directory.CreateTempSubdirectory("inactive-reset-cal-").FullName;
        try
        {
            Write(data, CalibrationProfile.DefaultDirectoryName, "shipped.json", 3.0f);
            Write(data, CalibrationProfile.UserDirectoryName, "mine.json", 9.9f);

            var all = CalibrationProfile.LoadAllForData(data);

            // One entry, and it is theirs. Their measurement was made on their
            // machine and they chose to make it; a shipped default is a
            // convenience for combinations they have not reached yet.
            var only = Assert.Single(all);
            Assert.Equal(9.9f, only.ForwardDistance);
            Assert.False(only.IsShipped);
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }

    [Fact]
    public void AShippedCalibrationIsUsedWhenTheUserHasNone()
    {
        var data = Directory.CreateTempSubdirectory("inactive-reset-cal-").FullName;
        try
        {
            Write(data, CalibrationProfile.DefaultDirectoryName, "shipped.json", 3.0f);

            var only = Assert.Single(CalibrationProfile.LoadAllForData(data));
            Assert.Equal(3.0f, only.ForwardDistance);
            Assert.True(only.IsShipped);
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }

    private static void Write(string data, string folder, string file, float forwardDistance)
    {
        var directory = Path.Combine(data, folder);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, file), $$"""
            {
              "track_name": "Circuit de Barcelona",
              "vehicle_name": "Richard Mille AF Corse 2025 #50:ELMS",
              "executable_sha256": "{{BuildA}}",
              "calibration": {
                "forward_distance_D": {{forwardDistance}},
                "vertical_offset_H": 0.373062,
                "lateral_offset_L": 0.503253,
                "valid": true
              }
            }
            """);
    }
}
