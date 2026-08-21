using InactiveReset.Core;
using Xunit;

namespace InactiveReset.Tests;

/// <summary>
/// The parts of locating an install that do not need the game, Steam, or the
/// registry.
///
/// Steam discovery itself is deliberately not tested here: it reads this
/// machine's registry and library folders, so a test of it would assert that
/// this machine has the game rather than that the code is right.
/// </summary>
public sealed class GameLaunchTests : IDisposable
{
    private readonly string _temp =
        Path.Combine(Path.GetTempPath(), "inactive-reset-tests-" + Guid.NewGuid().ToString("N"));

    private string FakeInstall()
    {
        var root = Path.Combine(_temp, "game");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, GameInstall.DirectExeName), "");
        return root;
    }

    private string DataDirectory()
    {
        var data = Path.Combine(_temp, "data");
        Directory.CreateDirectory(data);
        return data;
    }

    [Fact]
    public void An_explicit_directory_is_used_as_given()
    {
        var install = GameLauncher.Locate(DataDirectory(), FakeInstall());

        Assert.Equal(Path.GetFullPath(Path.Combine(_temp, "game")), install.Root);
        Assert.Equal("--game-dir", install.Source);
    }

    /// <summary>
    /// The exe names are what makes one folder an install and not another, so
    /// they are worth pinning: a typo in either is silent until launch day.
    /// </summary>
    [Fact]
    public void Both_entry_points_resolve_inside_the_install()
    {
        var install = GameLauncher.Locate(DataDirectory(), FakeInstall());

        Assert.Equal(Path.Combine(install.Root, "Le Mans Ultimate.exe"), install.DirectExe);
        Assert.Equal(Path.Combine(install.Root, "start_protected_game.exe"), install.ProtectedExe);
        Assert.Equal(install.DirectExe, install.ExecutableFor(LaunchMode.Direct));
        Assert.Equal(install.ProtectedExe, install.ExecutableFor(LaunchMode.Protected));
    }

    /// <summary>
    /// A folder without the game in it is refused rather than accepted and
    /// failed at launch, where the message would be about a missing exe instead
    /// of about the wrong folder.
    /// </summary>
    [Fact]
    public void A_folder_that_is_not_an_install_is_refused()
    {
        var empty = Path.Combine(_temp, "empty");
        Directory.CreateDirectory(empty);

        var ex = Assert.Throws<GameInstallException>(
            () => GameLauncher.Locate(DataDirectory(), empty));
        Assert.Contains("Le Mans Ultimate.exe", ex.Message);
    }

    [Fact]
    public void A_directory_that_does_not_exist_is_refused()
    {
        var ex = Assert.Throws<GameInstallException>(
            () => GameLauncher.Locate(DataDirectory(), Path.Combine(_temp, "nowhere")));
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void A_saved_directory_is_found_again()
    {
        var data = DataDirectory();
        var root = FakeInstall();

        GameLauncher.SetInstallDirectory(data, root);
        var install = GameLauncher.Locate(data);

        Assert.Equal(Path.GetFullPath(root), install.Root);
        Assert.Contains("game-install.json", install.Source);
    }

    /// <summary>
    /// A stored path that does not work would shadow the Steam lookup that
    /// would have succeeded, so it is refused at the point it is set -- while
    /// the person setting it is still there to correct it.
    /// </summary>
    [Fact]
    public void An_unusable_directory_is_never_saved()
    {
        var data = DataDirectory();

        Assert.Throws<GameInstallException>(
            () => GameLauncher.SetInstallDirectory(data, Path.Combine(_temp, "nowhere")));
        Assert.False(File.Exists(Path.Combine(data, "game-install.json")));
    }

    /// <summary>An explicit directory beats a saved one, and is not written over it.</summary>
    [Fact]
    public void An_explicit_directory_wins_over_a_saved_one()
    {
        var data = DataDirectory();
        GameLauncher.SetInstallDirectory(data, FakeInstall());

        var other = Path.Combine(_temp, "other");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, GameInstall.DirectExeName), "");

        Assert.Equal(Path.GetFullPath(other), GameLauncher.Locate(data, other).Root);
        Assert.Equal(Path.GetFullPath(Path.Combine(_temp, "game")), GameLauncher.Locate(data).Root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a test run over.
        }
    }
}
