using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// What the club owns, as the settings file keeps it (Track Designer
/// overhaul, Phase 4, 25 Oct 2026): an older file has none, and what is
/// saved comes back.
/// </summary>
public sealed class RcpStockSettingsTests
{
    [Fact]
    public void An_older_settings_file_loads_with_nothing_owned()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}")!;

        Assert.NotNull(settings.TrackStock);
        Assert.True(settings.TrackStock.IsEmpty);
    }

    [Fact]
    public void What_the_club_owns_comes_back_as_it_was_saved()
    {
        var stock = new RcpStock();
        stock.Sets["50cm Wide-L / Oblong"] = 2;
        stock.LooseFor(500).OuterCurveTiles = 3;

        var back = JsonSerializer.Deserialize<RcpStock>(JsonSerializer.Serialize(stock))!.Tidied();

        Assert.Equal(2, back.Sets["50cm wide-l / oblong"]);
        Assert.Equal(2 * 12 + 3, back.OwnedFor(500).OuterCurveTiles);
    }
}
