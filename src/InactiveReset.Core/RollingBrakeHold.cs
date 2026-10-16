using System.Text.Json.Nodes;

namespace InactiveReset.Core;

/// <summary>Temporarily releases LMU brake hold without changing the user's final preference.</summary>
public sealed class RollingBrakeHold : IDisposable
{
    private readonly Func<bool> _read;
    private readonly Action<bool> _write;
    private readonly Func<bool> _sameContext;
    private readonly Action<string> _progress;
    private bool _pending;

    internal RollingBrakeHold(Func<bool> read, Action<bool> write, Func<bool> sameContext, Action<string> progress)
    { _read = read; _write = write; _sameContext = sameContext; _progress = progress; }

    public static RollingBrakeHold Prepare(LmuSessionClient menu, Func<bool> sameContext,
        Action<string> progress, CancellationToken cancellation, bool onTrack = false)
    {
        menu.RequirePracticeAsync(cancellation).GetAwaiter().GetResult();
        var state = menu.GameStateAsync(cancellation).GetAwaiter().GetResult();
        RequireState(state, onTrack);
        var hold = new RollingBrakeHold(
            () => menu.HoldBrakesAsync().GetAwaiter().GetResult(),
            enabled => menu.SetHoldBrakesAsync(enabled).GetAwaiter().GetResult(), sameContext, progress);
        cancellation.ThrowIfCancellationRequested();
        hold.Disable();
        return hold;
    }

    internal static void RequireState(JsonObject state, bool onTrack)
    {
        if (onTrack) { DirectReset.RequireDriver(state); return; }
        if (state["playerVehicleLoaded"]?.GetValue<bool>() != true ||
            state["inMonitor"]?.GetValue<bool>() != true || state["isReplayActive"]?.GetValue<bool>() != false)
            throw new GateException("Return to the garage before preparing rolling-start brake hold.");
    }

    internal void Disable()
    {
        RequireContext();
        if (!_read()) return;
        // Mark pending before the request: a failed response can still follow an applied change.
        _pending = true;
        try
        {
            RequireContext(); _write(false);
            if (_read()) throw new GateException("LMU did not release its Hold Brakes setting.");
            _progress("Hold Brakes temporarily released for this rolling start.");
        }
        catch { Restore(); throw; }
    }

    public void Restore()
    {
        if (!_pending) return;
        RequireContext();
        if (!_read())
        {
            RequireContext(); _write(true);
            if (!_read()) throw new GateException("Hold Brakes could not be restored; check LMU's setting.");
        }
        _pending = false;
        _progress("Original Hold Brakes setting restored.");
    }

    private void RequireContext()
    {
        if (!_sameContext()) throw new GateException("Game context changed; original Hold Brakes could not be restored. Check LMU's setting.");
    }

    public void Dispose() => Restore();
}
