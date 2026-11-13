using System.Drawing;
using System.Drawing.Imaging;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, the club's logo: a read that failed because another program
/// held the file for a moment is no longer remembered as "no logo" for the
/// rest of the session.
/// </summary>
public sealed class BugHunt17_ClubLogoTests : IDisposable
{
    // Declared first: everything below reads and writes where it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_logo_held_by_another_program_is_read_once_it_is_let_go()
    {
        var name = NewLogo();

        // Held the way a sync or backup tool holds it: nobody else may read.
        using (new FileStream(_data.PathOf(name), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Null(ClubLogo.PngBytes(name));
            Assert.Null(ClubLogo.DataUri(name));
            Assert.Null(ClubLogo.PlateFor(name, Color.Black));
        }

        Assert.NotNull(ClubLogo.PngBytes(name));
        Assert.StartsWith("data:image/png;base64,", ClubLogo.DataUri(name), StringComparison.Ordinal);

        // A black logo on a black screen needs a plate - which it would
        // not have been given had "no logo" been remembered.
        Assert.NotNull(ClubLogo.PlateFor(name, Color.Black));
    }

    [Fact]
    public void Only_a_file_that_is_there_but_could_not_be_opened_is_asked_for_again()
    {
        Assert.True(ClubLogo.MayReadLater(new IOException("being used by another process")));

        // Not there at all: remembered, as before - it is asked every two seconds.
        Assert.False(ClubLogo.MayReadLater(new FileNotFoundException()));
        Assert.False(ClubLogo.MayReadLater(new DirectoryNotFoundException()));

        // Not a picture: no amount of asking again will make it one.
        Assert.False(ClubLogo.MayReadLater(new ArgumentException("Parameter is not valid.")));
    }

    /// <summary>A solid black logo under a name Import could have made, in the test's data folder.</summary>
    private string NewLogo()
    {
        var name = "club-logo-" + Guid.NewGuid().ToString("N")[..12] + ".png";

        Directory.CreateDirectory(AppPaths.Root);

        using var bitmap = new Bitmap(32, 32);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Black);
        }

        bitmap.Save(_data.PathOf(name), ImageFormat.Png);
        return name;
    }
}
