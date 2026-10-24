namespace HeatCaller.Tests;

/// <summary>
/// The app's icon (24 Oct 2026): the Sunset Grid on the exe, every window
/// and the tray. See AppIcon and MainForm.Tray.cs.
/// </summary>
public class AppIconTests
{
    [Fact]
    public void The_icon_is_embedded_in_the_app()
    {
        Assert.NotNull(AppIcon.Window);
    }

    [Fact]
    public void The_tray_gets_the_small_size()
    {
        var tray = AppIcon.Tray;

        Assert.NotNull(tray);
        Assert.Equal(SystemInformation.SmallIconSize, tray.Size);
    }

    [Fact]
    public void The_icon_file_has_every_size_windows_asks_for()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(AppIcon.ResourceName);
        Assert.NotNull(stream);

        using var reader = new BinaryReader(stream);
        Assert.Equal(0, reader.ReadUInt16()); // reserved
        Assert.Equal(1, reader.ReadUInt16()); // an icon, not a cursor
        var count = reader.ReadUInt16();

        var sizes = new HashSet<int>();

        for (var i = 0; i < count; i++)
        {
            var width = reader.ReadByte();
            reader.ReadBytes(15);
            sizes.Add(width == 0 ? 256 : width);
        }

        foreach (var wanted in new[] { 16, 20, 24, 32, 40, 48, 256 })
        {
            Assert.Contains(wanted, sizes);
        }
    }

    [Fact]
    public void Every_window_starts_with_the_app_icon()
    {
        // Proves the WinForms field AppIcon replaces still exists on this
        // version of .NET. If this fails, windows still get the icon (the
        // fallback in AppIcon), a moment late.
        AppIcon.UseForEveryWindow();

        Assert.Null(AppIcon.Problem);

        using var form = new Form();
        Assert.Same(AppIcon.Window, form.Icon);
    }

    [Fact]
    public void The_rehearsal_tray_icon_says_so()
    {
        Assert.Equal("HeatCaller", MainForm.TrayText(rehearsal: false));
        Assert.Equal("HeatCaller Rehearsal", MainForm.TrayText(rehearsal: true));
    }
}
