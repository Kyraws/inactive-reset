using System.Text.Json;

namespace InactiveReset.Core;

/// <summary>SDK observations, keyed by SDK field name. Null means unavailable/nonfinite.</summary>
public sealed record TelemetryDetails(int? Session, double? ScoringTime, byte? GamePhase,
    bool? InRealtime, byte? OptionsLocation, sbyte? Control,
    IReadOnlyDictionary<string, double?> Values,
    IReadOnlyList<IReadOnlyDictionary<string, double?>> Wheels,
    IReadOnlyList<byte>? DentSeverity)
{
    public bool? IdentityMatches { get; init; }
    // Application options location can remain zero in live SDK output; realtime/control are the driving signals.
    public string? DrivingUnavailableReason => IdentityMatches != true ? "telemetry-scoring-identity-mismatch"
        : InRealtime != true ? "not-in-realtime"
        : Control == 3 ? "replay"
        : Control != 0 ? "not-player-controlled"
        : GamePhase is not (> 0 and < 7) ? "session-inactive-or-paused"
        : null;
    public bool Driving => DrivingUnavailableReason is null;

    internal static TelemetryDetails Decode(SharedMemoryOffsets offsets, int telem, byte[] bytes)
    {
        var root = offsets.Layout;
        var fields = root.GetProperty("telemInfo");
        int? Address(JsonElement section, string name, int start) =>
            section.TryGetProperty(name, out var value) ? checked(start + value.GetInt32()) : null;
        double? Number(int? address, bool single = false)
        {
            if (address is not { } a) return null;
            if (a < 0 || a > bytes.Length - (single ? 4 : 8)) throw new SharedMemoryException("SDK field outside layout.");
            double value = single ? BitConverter.ToSingle(bytes, a) : BitConverter.ToDouble(bytes, a);
            return double.IsFinite(value) ? value : null;
        }
        byte? Byte(int? a) => a is { } index && index >= 0 && index < bytes.Length ? bytes[index] : null;
        int? Integer(int? a) => a is { } index && index >= 0 && index <= bytes.Length - 4 ? BitConverter.ToInt32(bytes, index) : null;
        string? Text(JsonElement section, string name, int start, string sizeName)
        {
            if (Address(section, name, start) is not { } a || !section.TryGetProperty(sizeName, out var size)) return null;
            var length = size.GetInt32();
            if (a < 0 || length < 1 || a > bytes.Length - length) return null;
            var text = bytes.AsSpan(a, length);
            var end = text.IndexOf((byte)0);
            return System.Text.Encoding.ASCII.GetString(end < 0 ? text : text[..end]).Trim();
        }
        var values = new Dictionary<string, double?>();
        foreach (var name in new[] { "mDeltaTime", "mFuel", "mFuelCapacity", "mBatteryChargeFraction",
            "mUnfilteredThrottle", "mUnfilteredBrake", "mUnfilteredSteering", "mUnfilteredClutch",
            "mFilteredThrottle", "mFilteredBrake", "mFilteredSteering", "mFilteredClutch",
            "mFront3rdDeflection", "mRear3rdDeflection", "mFrontRideHeight", "mRearRideHeight",
            "mLastImpactET", "mLastImpactMagnitude" })
            values[name] = Number(Address(fields, name, telem));
        foreach (var name in new[] { "mSoC", "mVirtualEnergy", "mRegen" })
            values[name] = Number(Address(fields, name, telem), single: true);
        values["mElectricBoostMotorState"] = Byte(Address(fields, "mElectricBoostMotorState", telem));
        values["mDetached"] = Byte(Address(fields, "mDetached", telem));
        foreach (var name in new[] { "mLocalAccel", "mLocalRot", "mLocalRotAccel" })
        {
            var address = Address(fields, name, telem);
            for (var axis = 0; axis < 3; axis++) values[$"{name}.{ "xyz"[axis]}"] = Number(address + axis * 8);
        }
        // A zero battery reading on a car without an electric motor is unavailable, not empty charge.
        if (values["mElectricBoostMotorState"] is null or 0) values["mBatteryChargeFraction"] = null;
        var wheels = new List<IReadOnlyDictionary<string, double?>>();
        if (root.TryGetProperty("wheel", out var wheel) && Address(fields, "mWheel", telem) is { } first)
            for (var i = 0; i < 4; i++)
            {
                var item = new Dictionary<string, double?>();
                foreach (var name in new[] { "mSuspensionDeflection", "mRideHeight", "mSuspForce", "mRotation", "mTireLoad", "mWheelYLocation", "mVerticalTireDeflection" })
                    item[name] = Number(Address(wheel, name, first + i * fields.GetProperty("wheelStride").GetInt32()));
                wheels.Add(item);
            }
        var scoring = root.GetProperty("scoringInfo");
        sbyte? control = null;
        bool? identityMatches = null;
        for (var i = 0; i < Math.Clamp(Integer(offsets.ScoringInfo + offsets.NumVehicles) ?? 0, 0, 104); i++)
        {
            var entry = offsets.VehScoringInfo + i * offsets.VehScoringInfoStride;
            if (Byte(entry + offsets.VehIsPlayer) == 0) continue;
            if (Byte(Address(root.GetProperty("vehScoringInfo"), "mControl", entry)) is { } c) control = unchecked((sbyte)c);
            var telemetryTrack = Text(fields, "mTrackName", telem, "mTrackNameSize");
            var telemetryVehicle = Text(fields, "mVehicleName", telem, "mVehicleNameSize");
            identityMatches = telemetryTrack is not null && telemetryVehicle is not null &&
                Integer(Address(fields, "mID", telem)) == Integer(entry + offsets.VehId) &&
                string.Equals(telemetryTrack, Text(scoring, "mTrackName", offsets.ScoringInfo, "mTrackNameSize"), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(telemetryVehicle, Text(root.GetProperty("vehScoringInfo"), "mVehicleName", entry, "mVehicleNameSize"), StringComparison.OrdinalIgnoreCase);
            break;
        }
        var realtime = Byte(Address(scoring, "mInRealtime", offsets.ScoringInfo));
        byte[]? dents = null;
        if (Address(fields, "mDentSeverity", telem) is { } dent && dent >= 0 && dent <= bytes.Length - 8)
            dents = bytes.AsSpan(dent, 8).ToArray();
        return new(Integer(Address(scoring, "mSession", offsets.ScoringInfo)),
            Number(Address(scoring, "mCurrentET", offsets.ScoringInfo)),
            Byte(Address(scoring, "mGamePhase", offsets.ScoringInfo)),
            realtime is null ? null : realtime != 0,
            Byte(Address(root.GetProperty("generic"), "optionsLocation", 0)), control, values, wheels, dents)
            { IdentityMatches = identityMatches };
    }
}
