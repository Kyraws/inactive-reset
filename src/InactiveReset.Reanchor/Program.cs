namespace InactiveReset.Reanchor;

/// <summary>
/// Patch day, for the maintainer only.
///
/// This is not shipped. `build.ps1 ship` publishes InactiveReset.App and
/// InactiveReset.Cli and nothing else, so no user ever holds this binary. They
/// do not need it: profiles are published to the repository and the app fetches
/// the one matching its build hash.
///
/// WHAT THIS CAN AND CANNOT FIX. It re-derives ADDRESSES -- where a thing lives.
/// It cannot re-derive BEHAVIOUR -- what the engine does when it gets there. The
/// 2026-08-11 patch moved nothing and changed two tunable values, and every gate
/// stayed green while placement went a metre wrong. Behavioural tunables are now
/// read live from the running game instead (see <c>EngineTunables</c>), which is
/// why that class of patch no longer needs this tool at all.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Help();
            return args.Length == 0 ? 2 : 0;
        }

        var offsets = Environment.GetEnvironmentVariable("INACTIVE_RESET_OFFSETS")
                      ?? Path.Combine(AppContext.BaseDirectory, "offsets");
        var rest = args[1..];

        try
        {
            return args[0] switch
            {
                "dump" => ReanchorCommand.Dump(rest),
                "reanchor" => ReanchorCommand.Run(offsets, rest),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    private static int Unknown(string verb)
    {
        Console.Error.WriteLine($"ERROR: unknown verb '{verb}'");
        Help();
        return 2;
    }

    private static void Help() => Console.WriteLine(
        """
        inactive-reset-reanchor -- maintainer tool, not shipped to users

          inactive-reset-reanchor dump [file.bin]
              Capture the game's mapped module (file offset == RVA). Keep it:
              it is the "before" image for the NEXT patch, and you cannot make
              one retroactively.

          inactive-reset-reanchor reanchor --old-dump <before.bin>
                                           --base <offsets/OLD.json>
                                           [--out <file>]
              Captures the running game, re-derives every address against the
              old image, and writes a new profile. Anything it cannot resolve
              is marked confidence "U", so the tool refuses rather than reading
              the wrong place.

        Patch day:

          1. reanchor against your last "before" dump
          2. check the result: place at two checkpoints whose RANGE from the pit
             spot differs. A wrong constant scales with range; a wrong offset
             does not. One checkpoint cannot tell you which you have.
          3. commit the new offsets/<HASH8>.json and push

        Step 3 IS the release. The app fetches profiles by build hash from the
        repository, so a push is all a user needs. There is no binary to rebuild
        and nothing for them to install.

        What this does NOT cover: a patch that retunes placement without moving
        anything. Those values are read live from the running engine and need no
        profile change at all. If placement is wrong and reanchor reports every
        address resolved, you are looking at a behaviour change, not a move --
        read GetPitDestination, do not re-derive addresses harder.
        """);
}
