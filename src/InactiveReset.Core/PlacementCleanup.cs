namespace InactiveReset.Core;

internal enum PitWaitEnd { Active, Cleared, Garage, SessionEnded }

internal static class PlacementCleanup
{
    // Cancellation after Drive requests cleanup; it must not re-enable a
    // speeding penalty while the player is still driving with pit state.
    internal static PitWaitEnd WaitForSafeRestore(Func<PitWaitEnd> read,
        Action delay, CancellationToken cancellation, Action cancellationPending)
    {
        var notified = false;
        while (true)
        {
            var state = read();
            if (state != PitWaitEnd.Active) return state;
            if (cancellation.IsCancellationRequested && !notified)
            {
                cancellationPending();
                notified = true;
            }
            delay();
        }
    }
}

internal sealed class PlacementRestoreScope(bool pending, Action restore,
    Func<bool> sameSession) : IDisposable
{
    public bool Pending { get; private set; } = pending;
    public bool SessionChanged { get; private set; }

    public void Restore()
    {
        if (!Pending) return;
        // A destroyed/replaced table means the saved rules belong to a session
        // that is gone. Do not apply that value to the replacement session.
        if (sameSession()) restore();
        else SessionChanged = true;
        Pending = false;
    }

    public void Dispose() => Restore();
}
