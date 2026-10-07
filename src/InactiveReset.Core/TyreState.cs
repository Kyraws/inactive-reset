using System.Text.Json;

namespace InactiveReset.Core;

// Values from the SDK, not a tyre physics preset. Wear retains the SDK's
// 0..1 scale; no assumption about remaining tread or grip percentage.
public sealed record TyreTemperatures(double Left, double Centre, double Right);
public sealed record WheelTyreState(TyreTemperatures SurfaceCelsius,
    double CarcassCelsius, TyreTemperatures InnerLayerCelsius,
    double WearRaw, byte CompoundIndex, byte CompoundType,
    bool Flat = false, bool Detached = false);
public sealed record TyreState(byte VehicleClass, string FrontCompound, string RearCompound,
    WheelTyreState FrontLeft, WheelTyreState FrontRight,
    WheelTyreState RearLeft, WheelTyreState RearRight)
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    // Requested class policy. These names are not engine compound indices;
    // applying a compound will also require the loaded car's actual options.
    public static IReadOnlyList<string> AllowedCompounds(byte vehicleClass) => vehicleClass switch
    {
        0 or 5 => ["Soft", "Medium", "Hard", "Wet"], // Hypercar, GTE
        2 or 3 or 4 or 6 => ["Medium", "Wet"], // LMP2 ELMS, LMP2, LMP3, GT3
        _ => [],
    };
}

internal sealed record TyreTelemetryOffsets(int VehicleClass, int FrontCompound,
    int RearCompound, int CompoundNameSize, int Wheels, int WheelStride,
    int Temperature, int Wear, int Carcass, int InnerLayer, int CompoundIndex, int CompoundType,
    int? Flat = null, int? Detached = null)
{
    internal static TyreTelemetryOffsets? Load(JsonElement root, JsonElement telem)
    {
        if (!root.TryGetProperty("wheel", out var wheel)) return null;
        return new(
            telem.GetProperty("mVehicleClass").GetInt32(),
            telem.GetProperty("mFrontTireCompoundName").GetInt32(),
            telem.GetProperty("mRearTireCompoundName").GetInt32(),
            telem.GetProperty("compoundNameSize").GetInt32(),
            telem.GetProperty("mWheel").GetInt32(),
            telem.GetProperty("wheelStride").GetInt32(),
            wheel.GetProperty("mTemperature").GetInt32(),
            wheel.GetProperty("mWear").GetInt32(),
            wheel.GetProperty("mTireCarcassTemperature").GetInt32(),
            wheel.GetProperty("mTireInnerLayerTemperature").GetInt32(),
            wheel.GetProperty("mCompoundIndex").GetInt32(),
            wheel.GetProperty("mCompoundType").GetInt32(),
            wheel.TryGetProperty("mFlat", out var flat) ? flat.GetInt32() : null,
            wheel.TryGetProperty("mDetached", out var detached) ? detached.GetInt32() : null);
    }
}
