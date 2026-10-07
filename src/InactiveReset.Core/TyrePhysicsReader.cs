using System.Globalization;
using System.Text.Json;

namespace InactiveReset.Core;

public sealed record TyrePhysicsRules(bool Invulnerable, int StoredInvulnerability,
    int StoredWearMultiplier, int StoredDamageMultiplier, bool InGarage = false);

/// <summary>Read-only, build-specific inspection of tyre-related rules.</summary>
public static class TyrePhysicsReader
{
    public static TyrePhysicsRules? Read(GameSession session, string offsetDirectory)
    {
        using var document = JsonDocument.Parse(AutomaticTyreOffsets.Load(session, offsetDirectory).ToJsonString());
        var root = document.RootElement;
        if (!string.Equals(root.GetProperty("executableSha256").GetString(),
            session.ExecutableSha256, StringComparison.OrdinalIgnoreCase)) return null;
        ulong Offset(string name) => ulong.Parse(root.GetProperty(name).GetString()![2..],
            NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var probe = Convert.FromHexString(root.GetProperty("probeBytes").GetString()!);
        if (!session.Memory.ReadBytes(session.ModuleBase + Offset("probeRva"), probe.Length)
            .AsSpan().SequenceEqual(probe)) return null;
        var slot = new LiveStateReader(session).ResolveSlotIndex();
        var vehicle = session.Offsets.Containers.ContainerAddress(session.ModuleBase, slot)
            + session.Offsets.Containers.VehicleDelta;
        var physics = session.Memory.ReadUInt64(vehicle + Offset("physicsPointerOffset"));
        if (physics == 0) return null;
        return new(session.Memory.ReadByte(physics + Offset("invulnerabilityOffset")) != 0,
            session.Memory.ReadInt32(session.ModuleBase + Offset("invulnerabilitySettingRva")),
            session.Memory.ReadInt32(session.ModuleBase + Offset("wearMultiplierSettingRva")),
            session.Memory.ReadInt32(session.ModuleBase + Offset("damageMultiplierSettingRva")),
            new LiveStateReader(session).ReadControlOwner(slot) == 1);
    }
}
