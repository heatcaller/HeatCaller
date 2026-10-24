namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10, 23 Oct 2026 - PaceHistory.PersonalBestsBefore honors the
/// club's personal-best resets, so the announcer measures a reset driver
/// against laps set after the reset, as the Personal Bests page does.
/// </summary>
public sealed class BugHunt10_PaceHistoryTests
{
    private static readonly DateTime Tonight = new(2026, 10, 23, 20, 0, 0, DateTimeKind.Local);

    private static SessionRecord Race(DateTime startedLocal, string layout, params (string Transponder, string Name, long FastMs)[] results)
    {
        var record = new SessionRecord
        {
            TrackLayoutName = layout,
            StartedAtUtc = startedLocal.ToUniversalTime(),
        };

        foreach (var (transponder, name, fast) in results)
        {
            record.Results.Add(new SessionResultEntry { Transponder = transponder, DriverName = name, FastLapMs = fast });
        }

        return record;
    }

    [Fact]
    public void A_lap_before_the_reset_is_not_the_best_to_beat()
    {
        var cutoff = Tonight.AddDays(-1).ToUniversalTime();
        var races = new[]
        {
            Race(Tonight.AddDays(-30), "Club Oval", ("101", "Sam", 7_800), ("102", "Dave", 7_900)),
            Race(Tonight.AddHours(-1), "Club Oval", ("101", "Sam", 8_100), ("102", "Dave", 8_300)),
        };

        var bests = PaceHistory.PersonalBestsBefore(
            races,
            "Club Oval",
            TonightAtTheTrack.NightOf(Tonight),
            (driver, track) => driver == "Sam" && track == "Club Oval" ? (DateTime?)cutoff : null);

        Assert.Equal(8.1, bests["101"], 3);
        // Dave was not reset, so his older lap still stands.
        Assert.Equal(7.9, bests["102"], 3);
    }

    [Fact]
    public void A_race_exactly_at_the_cutoff_is_left_out()
    {
        var started = Tonight.AddDays(-7);
        var races = new[] { Race(started, "Club Oval", ("101", "Sam", 7_800)) };

        var bests = PaceHistory.PersonalBestsBefore(
            races,
            "Club Oval",
            TonightAtTheTrack.NightOf(Tonight),
            (_, _) => started.ToUniversalTime());

        Assert.Empty(bests);
    }

    [Fact]
    public void No_cutoff_lookup_keeps_every_saved_race()
    {
        var races = new[] { Race(Tonight.AddDays(-30), "Club Oval", ("101", "Sam", 7_800)) };

        var bests = PaceHistory.PersonalBestsBefore(races, "Club Oval", TonightAtTheTrack.NightOf(Tonight));

        Assert.Equal(7.8, bests["101"], 3);
    }
}
