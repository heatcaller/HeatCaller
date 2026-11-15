namespace HeatCaller.Tests;

/// <summary>
/// Most improved tonight (15 Nov 2026, Asher: "Make most improved") - see
/// MostImprovedTonight.
/// </summary>
public sealed class MostImprovedTonightTests
{
    private static readonly DateTime Seven = new(2026, 11, 14, 19, 0, 0, DateTimeKind.Utc);

    private static SessionRecord Race(string label, int minutesIn, params (string Tx, long BestMs)[] drivers) => new()
    {
        SessionType = RaceSessionType.Race,
        TrackLayoutName = "Club Oval",
        Label = label,
        StartedAtUtc = Seven.AddMinutes(minutesIn),
        Results = drivers.Select((d, i) => new SessionResultEntry { Position = i + 1, Transponder = d.Tx, FastLapMs = d.BestMs }).ToList(),
    };

    [Fact]
    public void It_is_the_biggest_gain_by_percentage_so_a_slower_class_has_the_same_chance()
    {
        var pick = MostImprovedTonight.From(new[]
        {
            Race("Junior - Heat 1", 0, ("mia", 10_000), ("sam", 20_000)),
            Race("Junior - Heat 2", 30, ("mia", 9_600), ("sam", 19_400)),
        });

        // Mia 4% (0.40 s), Sam 3% (0.60 s): Mia, though Sam gained more seconds.
        Assert.NotNull(pick);
        Assert.Equal("mia", pick!.Transponder);
        Assert.Equal("0.40 s quicker than in Junior - Heat 1", pick.Line);
    }

    [Fact]
    public void It_counts_their_best_race_since_the_first_not_only_the_last()
    {
        var pick = MostImprovedTonight.From(new[]
        {
            Race("Heat 1", 0, ("mia", 10_000)),
            Race("Heat 2", 30, ("mia", 9_500)),
            Race("Final", 60, ("mia", 9_900)),
        });

        Assert.Equal(500, pick!.GainMs);
    }

    [Fact]
    public void Nobody_is_named_on_a_night_when_nobody_really_got_quicker()
    {
        Assert.Null(MostImprovedTonight.From(new[]
        {
            Race("Heat 1", 0, ("mia", 10_000), ("sam", 10_100)),
            Race("Heat 2", 30, ("mia", 9_950), ("sam", 10_300)),
        }));

        Assert.Null(MostImprovedTonight.From(new[] { Race("Heat 1", 0, ("mia", 10_000)) }));
        Assert.Null(MostImprovedTonight.From(Array.Empty<SessionRecord>()));
    }

    [Fact]
    public void Practice_demos_imports_and_another_layout_never_count()
    {
        var practice = Race("Practice", 0, ("mia", 12_000));
        practice.SessionType = RaceSessionType.Practice;
        var imported = Race("Heat 1", 5, ("sam", 12_000));
        imported.ImportedFrom = "LiveTime";
        var otherTrack = Race("Heat 2", 30, ("mia", 9_000), ("sam", 9_000));
        otherTrack.TrackLayoutName = "Long Layout";

        Assert.Null(MostImprovedTonight.From(new[] { practice, imported, Race("Heat 1", 10, ("mia", 10_000)), otherTrack }));
    }

    [Fact]
    public void The_stream_page_puts_it_on_a_card_between_races()
    {
        var server = new WebServerService(new RaceTiming(new RaceControlSession()), ProfileStore.Load(),
            settings: new AppSettings { DisplayLook = Looks.MidnightKey });
        var page = server.BuildBroadcastPage(OverlayLayouts.BuiltIn("between"));

        Assert.Contains("['Most improved tonight', sn(imp.name)", page);
    }
}
