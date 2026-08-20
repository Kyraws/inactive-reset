using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace InactiveReset.Core;

public sealed class SharedMemoryException(string message) : Exception(message);

/// <summary>
/// Byte offsets into LMU's shared memory, emitted by the compiler from the
/// official SDK header. Never hand-edit — re-run tools/dump-sdk-offsets.
/// </summary>
public sealed class SharedMemoryOffsets
{
    public required string MapName { get; init; }
    public required string EventName { get; init; }
    public required int LayoutSize { get; init; }
    public required string SdkHeaderSha256 { get; init; }

    public required int PlayerVehicleIdx { get; init; }
    public required int PlayerHasVehicle { get; init; }
    public required int TelemInfo { get; init; }
    public required int TelemInfoStride { get; init; }

    public required int TelemPos { get; init; }
    public required int TelemOri { get; init; }
    public required int TelemGear { get; init; }
    public required int TelemElapsedTime { get; init; }
    public required int Vect3Stride { get; init; }

    /// <summary>
    /// 8 means the SDK stores positions and orientations as DOUBLES. Reading
    /// them as floats yields plausible nonsense rather than an error, so this is
    /// asserted rather than assumed.
    /// </summary>
    public required int Vect3ComponentSize { get; init; }

    public required int ScoringInfo { get; init; }
    public required int VehScoringInfo { get; init; }
    public required int VehScoringInfoStride { get; init; }
    public required int TrackName { get; init; }
    public required int TrackNameSize { get; init; }
    public required int NumVehicles { get; init; }
    public required int VehId { get; init; }
    public required int VehName { get; init; }
    public required int VehNameSize { get; init; }
    public required int VehIsPlayer { get; init; }
    public required int VehLapDist { get; init; }

    public static SharedMemoryOffsets Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        var root = document.RootElement;
        var telemetry = root.GetProperty("telemetry");
        var telem = root.GetProperty("telemInfo");
        var scoring = root.GetProperty("scoring");
        var scoringInfo = root.GetProperty("scoringInfo");
        var veh = root.GetProperty("vehScoringInfo");

        var offsets = new SharedMemoryOffsets
        {
            MapName = root.GetProperty("mapName").GetString()!,
            EventName = root.GetProperty("eventName").GetString()!,
            LayoutSize = root.GetProperty("layoutSize").GetInt32(),
            SdkHeaderSha256 = root.GetProperty("sdkHeaderSha256").GetString()!,

            PlayerVehicleIdx = telemetry.GetProperty("playerVehicleIdx").GetInt32(),
            PlayerHasVehicle = telemetry.GetProperty("playerHasVehicle").GetInt32(),
            TelemInfo = telemetry.GetProperty("telemInfo").GetInt32(),
            TelemInfoStride = telemetry.GetProperty("telemInfoStride").GetInt32(),

            TelemPos = telem.GetProperty("mPos").GetInt32(),
            TelemOri = telem.GetProperty("mOri").GetInt32(),
            TelemGear = telem.GetProperty("mGear").GetInt32(),
            TelemElapsedTime = telem.GetProperty("mElapsedTime").GetInt32(),
            Vect3Stride = telem.GetProperty("vect3Stride").GetInt32(),
            Vect3ComponentSize = telem.GetProperty("vect3ComponentSize").GetInt32(),

            ScoringInfo = scoring.GetProperty("scoringInfo").GetInt32(),
            VehScoringInfo = scoring.GetProperty("vehScoringInfo").GetInt32(),
            VehScoringInfoStride = scoring.GetProperty("vehScoringInfoStride").GetInt32(),
            TrackName = scoringInfo.GetProperty("mTrackName").GetInt32(),
            TrackNameSize = scoringInfo.GetProperty("mTrackNameSize").GetInt32(),
            NumVehicles = scoringInfo.GetProperty("mNumVehicles").GetInt32(),
            VehId = veh.GetProperty("mID").GetInt32(),
            VehName = veh.GetProperty("mVehicleName").GetInt32(),
            VehNameSize = veh.GetProperty("mVehicleNameSize").GetInt32(),
            VehIsPlayer = veh.GetProperty("mIsPlayer").GetInt32(),
            VehLapDist = veh.GetProperty("mLapDist").GetInt32(),
        };

        if (offsets.Vect3ComponentSize != 8)
        {
            throw new SharedMemoryException(
                $"vect3ComponentSize is {offsets.Vect3ComponentSize}, expected 8 (double). " +
                "The SDK layout changed; re-run tools/dump-sdk-offsets and review the reader.");
        }
        return offsets;
    }
}

/// <summary>One sample of the player's car from LMU's own telemetry.</summary>
public sealed record TelemetrySnapshot(
    string TrackName,
    string VehicleName,
    RecordedPose Pose,
    float LapDistance,
    int Gear,
    double ElapsedTime);

/// <summary>
/// Reads LMU's official shared memory, read-only.
///
/// This is the SUPPORTED interface — the same one plugins use. It is not a
/// memory hack, and it is where every checkpoint pose comes from. All layout
/// knowledge is data, produced by the compiler from the SDK header, so a new
/// SDK version means regenerating a JSON rather than editing this file.
/// </summary>
public sealed partial class SharedMemoryReader : IDisposable
{
    private const uint FILE_MAP_READ = 0x0004;

    private readonly SharedMemoryOffsets _offsets;
    private nint _mapping;
    private nint _view;

    public SharedMemoryReader(SharedMemoryOffsets offsets)
    {
        _offsets = offsets;

        _mapping = OpenFileMapping(FILE_MAP_READ, false, offsets.MapName);
        if (_mapping == 0)
        {
            throw new SharedMemoryException(
                $"cannot open the '{offsets.MapName}' mapping. Is Le Mans Ultimate running, " +
                "and has it loaded a session?");
        }

        _view = MapViewOfFile(_mapping, FILE_MAP_READ, 0, 0, (nuint)offsets.LayoutSize);
        if (_view == 0)
        {
            CloseHandle(_mapping);
            _mapping = 0;
            throw new SharedMemoryException("cannot map a read-only view of LMU's shared memory");
        }
    }

    /// <summary>
    /// Sample the player's car. Returns null when there is no player vehicle,
    /// which is the normal state in menus.
    /// </summary>
    public TelemetrySnapshot? Read()
    {
        if (ReadByte(_offsets.PlayerHasVehicle) == 0)
        {
            return null;
        }

        int playerIndex = ReadByte(_offsets.PlayerVehicleIdx);
        var telem = _offsets.TelemInfo + playerIndex * _offsets.TelemInfoStride;

        var pose = new RecordedPose(
            Position: ReadVec3(telem + _offsets.TelemPos),
            Row0: ReadVec3(telem + _offsets.TelemOri + 0 * _offsets.Vect3Stride),
            Row1: ReadVec3(telem + _offsets.TelemOri + 1 * _offsets.Vect3Stride),
            Row2: ReadVec3(telem + _offsets.TelemOri + 2 * _offsets.Vect3Stride));

        var trackName = ReadAscii(_offsets.ScoringInfo + _offsets.TrackName, _offsets.TrackNameSize);
        var (vehicleName, lapDistance) = FindPlayerScoring();

        return new TelemetrySnapshot(
            TrackName: trackName,
            VehicleName: vehicleName,
            Pose: pose,
            LapDistance: lapDistance,
            Gear: ReadInt32(telem + _offsets.TelemGear),
            ElapsedTime: ReadDouble(telem + _offsets.TelemElapsedTime));
    }

    /// <summary>
    /// The vehicle name lives in the SCORING array, which is ordered
    /// independently of telemetry — so it is found by the mIsPlayer flag rather
    /// than by reusing the telemetry index.
    /// </summary>
    private (string Name, float LapDistance) FindPlayerScoring()
    {
        var count = Math.Clamp(ReadInt32(_offsets.ScoringInfo + _offsets.NumVehicles), 0, 104);
        for (var i = 0; i < count; i++)
        {
            var entry = _offsets.VehScoringInfo + i * _offsets.VehScoringInfoStride;
            if (ReadByte(entry + _offsets.VehIsPlayer) != 0)
            {
                return (ReadAscii(entry + _offsets.VehName, _offsets.VehNameSize),
                        (float)ReadDouble(entry + _offsets.VehLapDist));
            }
        }
        return ("", 0f);
    }

    // ---- primitives --------------------------------------------------------

    private Vec3 ReadVec3(int offset) => new(
        (float)ReadDouble(offset),
        (float)ReadDouble(offset + 8),
        (float)ReadDouble(offset + 16));

    private byte ReadByte(int offset)
    {
        Bounds(offset, 1);
        return Marshal.ReadByte(_view, offset);
    }

    private int ReadInt32(int offset)
    {
        Bounds(offset, 4);
        return Marshal.ReadInt32(_view, offset);
    }

    private double ReadDouble(int offset)
    {
        Bounds(offset, 8);
        return BitConverter.Int64BitsToDouble(Marshal.ReadInt64(_view, offset));
    }

    private string ReadAscii(int offset, int maxLength)
    {
        Bounds(offset, maxLength);
        var bytes = new byte[maxLength];
        Marshal.Copy(_view + offset, bytes, 0, maxLength);
        var end = Array.IndexOf(bytes, (byte)0);
        return Encoding.ASCII.GetString(bytes, 0, end < 0 ? maxLength : end).Trim();
    }

    private void Bounds(int offset, int length)
    {
        if (offset < 0 || offset + length > _offsets.LayoutSize)
        {
            throw new SharedMemoryException(
                $"read of {length} bytes at {offset} is outside the {_offsets.LayoutSize}-byte " +
                "mapping - the offsets do not describe this SDK version");
        }
    }

    public void Dispose()
    {
        if (_view != 0)
        {
            UnmapViewOfFile(_view);
            _view = 0;
        }
        if (_mapping != 0)
        {
            CloseHandle(_mapping);
            _mapping = 0;
        }
    }

    // EntryPoint is explicit: unlike DllImport with CharSet, LibraryImport does
    // NOT append the A/W suffix, so "OpenFileMapping" resolves to nothing. LMU
    // creates the mapping with the ANSI variant and an ASCII name, and UTF-8
    // marshalling is byte-identical for ASCII.
    [LibraryImport("kernel32.dll", EntryPoint = "OpenFileMappingA",
                   SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint OpenFileMapping(uint access,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, string name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint MapViewOfFile(nint mapping, uint access,
        uint offsetHigh, uint offsetLow, nuint bytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnmapViewOfFile(nint address);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
