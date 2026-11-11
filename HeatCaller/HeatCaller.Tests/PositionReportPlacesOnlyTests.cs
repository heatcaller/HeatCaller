namespace HeatCaller.Tests;

/// <summary>
/// A places-only running order - "Bill leads, Sam second, Ana third", the
/// default with the gaps box unticked - is still true when one of those
/// cars crosses the line again in the same place (10 Nov 2026, voice speed
/// plan, phase 2). The demo races dropped about one report in three for it.
/// </summary>
public sealed class PositionReportPlacesOnlyTests
{
    private static PositionReportOrder.Row Row(string transponder, int laps, long elapsedMs) =>
        new(new RaceStanding { Transponder = transponder, Laps = laps, ElapsedMs = elapsedMs }, new RaceGap(0, null), new RaceGap(0, null));

    private static readonly PositionReportOrder.Row[] Read =
    {
        Row("bill", 10, 80_000),
        Row("sam", 10, 81_200),
        Row("ana", 9, 79_500),
    };

    [Fact]
    public void A_car_over_the_line_in_the_same_place_moves_a_report_with_gaps()
    {
        // Its gap is now a lap old - the words are wrong.
        var now = new[] { Read[0], Row("sam", 11, 89_100), Read[2] };

        Assert.True(PositionReportOrder.Moved(Read, now));
        Assert.True(PositionReportOrder.Moved(Read, now, placesOnly: false));
    }

    [Fact]
    public void A_car_over_the_line_in_the_same_place_does_not_move_a_places_only_report()
    {
        var now = new[] { Read[0], Row("sam", 11, 89_100), Read[2] };

        Assert.False(PositionReportOrder.Moved(Read, now, placesOnly: true));
    }

    [Fact]
    public void A_change_of_order_moves_a_places_only_report()
    {
        var now = new[] { Read[1], Read[0], Read[2] };

        Assert.True(PositionReportOrder.Moved(Read, now, placesOnly: true));
    }

    [Fact]
    public void Another_car_named_moves_a_places_only_report()
    {
        var now = new[] { Read[0], Read[1], Row("dee", 9, 79_000) };

        Assert.True(PositionReportOrder.Moved(Read, now, placesOnly: true));
        Assert.True(PositionReportOrder.Moved(Read, Read[..2], placesOnly: true));
    }

    [Fact]
    public void Nothing_changed_moves_nothing()
    {
        Assert.False(PositionReportOrder.Moved(Read, Read));
        Assert.False(PositionReportOrder.Moved(Read, Read, placesOnly: true));
    }

    [Fact]
    public void Both_checks_on_a_report_know_whether_it_says_gaps()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", "AnnouncementService.LapCalls.cs"));

        Assert.Contains("PositionReportOrder.Moved(queuedRows, now, placesOnly)", source);
        Assert.Contains("PositionReportOrder.Moved(worded, after, placesOnly)", source);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }
}
