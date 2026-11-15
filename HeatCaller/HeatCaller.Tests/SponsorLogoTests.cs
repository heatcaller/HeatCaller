using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeatCaller.Tests;

/// <summary>
/// A sponsor's own logo (14 Nov 2026, plans/obs-overlay-plan.md section 1,
/// Asher: "add in the ability to upload a sponsor logo via sponsor page").
/// See SponsorLogo.
/// </summary>
[Collection(SponsorLogoCollection.Name)]
public sealed class SponsorLogoTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hc-sponsor-logo-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _was = SponsorLogo.Folder;

    public SponsorLogoTests()
    {
        Directory.CreateDirectory(_folder);
        SponsorLogo.Folder = Path.Combine(_folder, "Sponsors");
        SponsorLogo.ForgetCache();
    }

    public void Dispose()
    {
        SponsorLogo.Folder = _was;
        SponsorLogo.ForgetCache();

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch
        {
            // A test machine's temp folder.
        }
    }

    /// <summary>A wide picture, transparent but for a red bar across the middle.</summary>
    private string WideLogo(int width = 1200, int height = 300)
    {
        var path = Path.Combine(_folder, "wide.png");

        using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.FillRectangle(Brushes.Red, 0, height / 3, width, height / 3);
            }

            bitmap.Save(path, ImageFormat.Png);
        }

        return path;
    }

    [Fact]
    public void Import_keeps_the_logo_wide_and_see_through()
    {
        var name = SponsorLogo.Import(WideLogo(), out var error);

        Assert.Null(error);
        Assert.True(SponsorLogo.IsLogoName(name));

        var bytes = SponsorLogo.PngBytes(name);
        Assert.NotNull(bytes);

        using var image = new Bitmap(new MemoryStream(bytes!));

        // 1200 x 300 fitted into 800 x 400: 800 x 200, the same shape.
        Assert.Equal(800, image.Width);
        Assert.Equal(200, image.Height);
        Assert.Equal(0, image.GetPixel(5, 5).A);
        Assert.Equal(255, image.GetPixel(400, 100).A);
    }

    [Fact]
    public void Import_never_makes_a_small_logo_bigger()
    {
        var name = SponsorLogo.Import(WideLogo(240, 60), out _);
        using var image = new Bitmap(new MemoryStream(SponsorLogo.PngBytes(name)!));

        Assert.Equal(240, image.Width);
        Assert.Equal(60, image.Height);
    }

    [Fact]
    public void Import_refuses_a_file_that_is_not_a_picture_with_a_reason()
    {
        var path = Path.Combine(_folder, "notes.png");
        File.WriteAllText(path, "this is not a picture");

        Assert.Null(SponsorLogo.Import(path, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("sponsor-logo-0123456789ab.png", true)]
    [InlineData("sponsor-logo-0123456789AB.png", false)]
    [InlineData("sponsor-logo-0123456789a.png", false)]
    [InlineData("club-logo-0123456789ab.png", false)]
    [InlineData("../sponsor-logo-0123456789ab.png", false)]
    [InlineData("sponsor-logo-0123456789ab.jpg", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_a_name_import_makes_is_a_logo_name(string? name, bool expected) =>
        Assert.Equal(expected, SponsorLogo.IsLogoName(name));

    [Fact]
    public void A_file_under_a_logo_name_that_is_not_a_real_png_is_never_served()
    {
        Directory.CreateDirectory(SponsorLogo.Folder);
        const string name = "sponsor-logo-aaaaaaaaaaaa.png";
        File.WriteAllText(Path.Combine(SponsorLogo.Folder, name), "planted");

        Assert.Null(SponsorLogo.PngBytes(name));
        Assert.Null(SponsorLogo.CardDataUri(name));
    }

    [Fact]
    public void The_card_copy_is_a_small_png_that_fits_the_card()
    {
        var name = SponsorLogo.Import(WideLogo(), out _);
        var uri = SponsorLogo.CardDataUri(name);

        Assert.StartsWith("data:image/png;base64,", uri);

        using var image = new Bitmap(new MemoryStream(Convert.FromBase64String(uri!["data:image/png;base64,".Length..])));
        Assert.True(image.Width <= SponsorLogo.CardBox.Width && image.Height <= SponsorLogo.CardBox.Height);
        Assert.Equal(0, image.GetPixel(1, 1).A);
    }

    [Fact]
    public void The_live_page_card_shows_the_logo_first_and_the_read_picture_otherwise()
    {
        var withLogo = new SponsorBooking { Id = "s1", Name = "Hobby Hut", LogoFileName = "sponsor-logo-0123456789ab.png", PictureFileName = "slide.png" };
        var pictureOnly = new SponsorBooking { Id = "s2", Name = "Volt", PictureFileName = "slide.png" };

        var doc = JsonNode.Parse(RelaySponsors.Document(
            new[] { withLogo, pictureOnly },
            _ => "data:image/jpeg;base64,SLIDE",
            _ => "data:image/png;base64,LOGO"))!;
        var list = doc["sponsors"]!.AsArray();

        Assert.Equal("data:image/png;base64,LOGO", list[0]!["picture"]!.GetValue<string>());
        Assert.Equal("data:image/jpeg;base64,SLIDE", list[1]!["picture"]!.GetValue<string>());
    }

    [Fact]
    public void A_logo_that_cannot_be_read_falls_back_to_the_read_picture()
    {
        var sponsor = new SponsorBooking { Id = "s1", Name = "Hobby Hut", LogoFileName = "sponsor-logo-0123456789ab.png", PictureFileName = "slide.png" };

        var doc = JsonNode.Parse(RelaySponsors.Document(new[] { sponsor }, _ => "data:image/jpeg;base64,SLIDE", _ => null))!;

        Assert.Equal("data:image/jpeg;base64,SLIDE", doc["sponsors"]![0]!["picture"]!.GetValue<string>());
    }

    [Fact]
    public void A_booking_saved_before_logos_loads_with_none()
    {
        var old = JsonSerializer.Deserialize<SponsorBooking>("{\"Id\":\"s1\",\"Name\":\"Hobby Hut\"}")!;

        Assert.Equal(string.Empty, old.LogoFileName);
    }

    [Fact]
    public void Only_a_logo_name_gets_an_address_on_the_wifi_server()
    {
        Assert.Equal("/sponsor-logo/sponsor-logo-0123456789ab.png", WebServerService.SponsorLogoUrl("sponsor-logo-0123456789ab.png"));
        Assert.Equal(string.Empty, WebServerService.SponsorLogoUrl("../../settings.json"));
        Assert.Equal(string.Empty, WebServerService.SponsorLogoUrl(null));
    }

    [Fact]
    public void Prune_clears_week_old_logos_nobody_names_and_keeps_the_rest()
    {
        Directory.CreateDirectory(SponsorLogo.Folder);
        var inUse = Path.Combine(SponsorLogo.Folder, "sponsor-logo-111111111111.png");
        var oldUnused = Path.Combine(SponsorLogo.Folder, "sponsor-logo-222222222222.png");
        var newUnused = Path.Combine(SponsorLogo.Folder, "sponsor-logo-333333333333.png");
        var slide = Path.Combine(SponsorLogo.Folder, "abc_slide.png");

        foreach (var path in new[] { inUse, oldUnused, newUnused, slide })
        {
            File.WriteAllText(path, "x");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-30));
        }

        File.SetLastWriteTimeUtc(newUnused, DateTime.UtcNow.AddDays(-1));

        var removed = SponsorLogo.Prune(new[] { "sponsor-logo-111111111111.png", null, "" }, DateTime.UtcNow);

        Assert.Equal(1, removed);
        Assert.True(File.Exists(inUse));
        Assert.False(File.Exists(oldUnused));
        Assert.True(File.Exists(newUnused), "kept for undo for a week");
        Assert.True(File.Exists(slide), "a slide is never a logo");
    }
}

/// <summary>SponsorLogo.Folder is one setting for the whole app: these tests take turns with it.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SponsorLogoCollection
{
    public const string Name = "SponsorLogo folder";
}
