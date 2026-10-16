namespace InactiveReset.Core;

/// <summary>Experimental build-bound physics pause; does not open the pause menu or pause audio.</summary>
public sealed class PhysicsPause : IDisposable
{
    // Historical experiment identifier; production support comes from the discovered profile.
    public const string SupportedBuild = "66942337813B301B87D12EDFA330F33354A2A4B8CD9B048885CBF77EB6F940CF";
    private readonly Func<bool> _sameContext;
    private readonly Func<byte> _read;
    private readonly Action<byte> _write;
    private bool _attempted, _owned;
    public bool IsPaused => ReadFlag() == 1;
    public bool OwnsPause => _owned;

    internal PhysicsPause(Func<bool> sameContext, Func<byte> read, Action<byte> write)
    { _sameContext = sameContext; _read = read; _write = write; }

    public static PhysicsPause Prepare(GameSession session, Func<bool> sameContext)
    {
        var layout = session.RequireMotion();
        var address = session.ModuleBase + layout.PauseFlagRva;
        session.Memory.RequireWritableDataPage(address, 1);
        var pause = new PhysicsPause(sameContext, () => session.Memory.ReadByte(address),
            value => session.Memory.WriteVerified(address, new[] { value }));
        pause.ReadFlag();
        return pause;
    }

    private byte ReadFlag()
    {
        if (!_sameContext()) throw new GateException("The physics-pause session changed.");
        var flag = _read();
        if (flag > 1) throw new GateException("Invalid physics-pause flag.");
        return flag;
    }

    public void Begin()
    {
        if (_attempted) throw new GateException("A physics-pause scope can only pause once.");
        _attempted = true;
        if (ReadFlag() != 0) throw new GateException("Game is already paused; refusing to take ownership of the driver's pause.");
        _owned = true;
        try { _write(1); }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>On a partial motion update, leave physics paused for explicit recovery.</summary>
    public void LeavePaused() => _owned = false;

    public void Dispose()
    {
        if (!_owned) return;
        if (ReadFlag() == 1) _write(0);
        _owned = false;
    }
}
