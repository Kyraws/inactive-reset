using InactiveReset.Core;

namespace InactiveReset.Reanchor;

/// <summary>Read-only work inside an owned pause; never retains a pause for motion recovery.</summary>
public static class OwnedPauseObservation
{
    public static T Run<T>(PhysicsPause pause, Func<T> observe, Action boundary,
        Action<string> record, CancellationToken cancellation)
    {
        try
        {
            cancellation.ThrowIfCancellationRequested();
            pause.Begin();
            record("pause acquired; verified flag=1");
            cancellation.ThrowIfCancellationRequested();
            if (!pause.OwnsPause || !pause.IsPaused) throw new GateException("Observation pause was lost.");
            boundary();
            var result = observe();
            cancellation.ThrowIfCancellationRequested();
            boundary();
            if (!pause.OwnsPause || !pause.IsPaused) throw new GateException("Observation pause was lost.");
            record("observation verified; flag=1");
            return result;
        }
        finally
        {
            try
            {
                pause.Dispose();
                record("pause scope released; owned flag restored when still set");
            }
            catch (Exception ex)
            {
                record("pause cleanup failed; recovery required: " + ex.Message);
                throw;
            }
        }
    }
}
