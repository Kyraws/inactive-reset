namespace InactiveReset.Core;

/// <summary>
/// The 24-byte spot-entry write and verified restoration attempt.
///
/// This transaction covers spot-entry bytes only. Placement also writes rule
/// and lap-state fields separately. The spot write is deliberately narrow:
///
///   * exactly 24 bytes, so the 8-byte padding tail at +0x18 cannot be touched;
///   * the target page must ALREADY be committed and writable — page protection
///     is never changed, because a page that needs changing is a page we have
///     no business writing;
///   * the live bytes are re-read and compared against what was planned, so a
///     reallocated table or a competing writer aborts instead of corrupting;
///   * the write is verified by read-back and rolled back if it disagrees;
///   * restore is attempted from <see cref="Dispose"/> on normal or exceptional
///     scope exit. Process termination cannot run this cleanup.
/// </summary>
public sealed class SpotWriteTransaction : IDisposable
{
    internal delegate void VerifiedWrite(ulong address, ReadOnlySpan<byte> payload);

    private readonly VerifiedWrite _writeVerified;
    private readonly ulong _address;
    private readonly byte[] _original;
    private readonly Func<bool> _sameSession;
    private bool _modified;

    public ulong Address => _address;
    public IReadOnlyList<byte> Original => _original;
    public bool Active => _modified;

    internal SpotWriteTransaction(VerifiedWrite writeVerified, ulong address, byte[] original,
        Func<bool>? sameSession = null)
    {
        _writeVerified = writeVerified;
        _address = address;
        _original = original;
        _sameSession = sameSession ?? (() => true);
    }

    /// <summary>
    /// Open a transaction against a spot entry.
    /// </summary>
    /// <param name="expectedOriginal">
    /// The bytes read when the placement was planned. If the live bytes no
    /// longer match, the table moved or something else is writing it, and we
    /// refuse rather than overwrite an entry we do not understand.
    /// </param>
    internal static SpotWriteTransaction Begin(
        GameSession session, ulong address, ReadOnlySpan<byte> expectedOriginal,
        Func<bool>? sameSession = null)
    {
        if (!session.Memory.CanWrite)
        {
            throw new MemoryAccessException("this session was attached read-only");
        }
        if (address == 0)
        {
            throw new MemoryAccessException("refusing a null write address");
        }
        if (address % 4 != 0)
        {
            throw new MemoryAccessException("spot entry address is not float-aligned");
        }

        var writeBytes = session.Offsets.SpotTable.WriteBytes;

        // Re-verify the running build independently of the attach-time gate.
        // Cheap, and it closes the window where the game was replaced underneath
        // a long-lived session.
        var imagePath = session.Process.MainModule?.FileName
            ?? throw new GateException("cannot re-verify the game's image path");
        var hash = GameSession.Sha256File(imagePath);
        if (!string.Equals(hash, session.Offsets.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new GateException("the running executable is not the build these offsets describe");
        }

        session.Memory.RequireWritableDataPage(address, writeBytes);

        var live = session.Memory.ReadBytes(address, writeBytes);
        if (!live.AsSpan().SequenceEqual(expectedOriginal[..writeBytes]))
        {
            throw new MemoryAccessException(
                "the live entry no longer matches the bytes read at plan time - the table was " +
                "reallocated or another writer is active; refusing");
        }

        return new SpotWriteTransaction(session.Memory.WriteVerified, address, live, sameSession);
    }

    /// <summary>Write the payload, verified by read-back. Rolls back on mismatch.</summary>
    public void Write(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != _original.Length)
        {
            throw new MemoryAccessException(
                $"payload must be exactly {_original.Length} bytes");
        }
        if (!_sameSession()) throw new GateException("the placement session changed before arming");

        try
        {
            _writeVerified(_address, payload);
            _modified = true;
        }
        catch
        {
            // WriteVerified only throws after a failed readback, so the entry may
            // be half-written. Treat it as modified and restore.
            _modified = true;
            Restore();
            throw;
        }
    }

    /// <summary>
    /// Put the original bytes back and confirm. Safe to call repeatedly.
    /// </summary>
    public void Restore()
    {
        if (!_modified)
        {
            return;
        }
        if (_sameSession()) _writeVerified(_address, _original);
        _modified = false;
    }

    /// <summary>
    /// Last line of defence. If anything unwinds between write and restore,
    /// attempt the restore and surface failure rather than hiding uncertain bytes.
    /// </summary>
    public void Dispose() => Restore();
}
