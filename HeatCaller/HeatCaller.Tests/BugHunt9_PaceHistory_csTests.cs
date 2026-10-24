namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 9, 22 Oct 2026 - PaceHistory.PersonalBestsBefore: tonight's
/// saved heats count toward a driver's best before this race, so a main
/// cannot call a "personal best" for a lap slower than one run an hour ago.
/// </summary>
public sealed class BugHunt9_PaceHistory_csTests
{
    private static readonly DateTime Tonight = new(2026, 10, 22, 20, 0, 0, DateTimeKind.Local);

    private static SessionRecord Race(DateTime startedLocal, string layout, bool demo = false, params (string Transponder, long FastMs)[] results)
    {
        var record = new SessionRecord
        {
            TrackLayoutName = layout,
            IsDemo = demo,
            StartedAtUtc = startedLocal.ToUniversalTime(),
        };

        foreach (var (transponder, fast) in results)
        {
            record.Results.Add(new SessionResultEntry { Transponder = transponder, DriverName = transponder, FastLapMs = fast });
        }

        return record;
    }

    [Fact]
    public void Tonights_earlier_heat_counts_toward_the_best_before_this_race()
    {
        var races = new[]
        {
            Race(Tonight.AddDays(-7), "Infield", false, ("101", 8_000)),
            Race(Tonight.AddHours(-1), "Infield", false, ("101", 7_800)),
        };

        var bests = PaceHistory.PersonalBestsBefore(races, "Infield", TonightAtTheTrack.NightOf(Tonight));

        Assert.Equal(7.8, bests["101"], 3);
    }

    [Fact]
    public void Tonights_demos_and_ghosts_still_do_not_count()
    {
        var races = new[]
        {
            Race(Tonight.AddHours(-1), "Infield", false, ("101", 11_000)),
            Race(Tonight.AddHours(-1), "Infield", true, ("102", 10_000)),
            Race(Tonight.AddHours(-1), "Infield", false, (Ghosts.Prefix + "1", 9_000)),
        };

        var bests = PaceHistory.PersonalBestsBefore(races, "Infield", TonightAtTheTrack.NightOf(Tonight));

        Assert.Single(bests);
        Assert.Equal(11.0, bests["101"], 3);
    }

    [Fact]
    public void A_race_dated_after_tonight_is_still_left_out()
    {
        var races = new[] { Race(Tonight.AddDays(2), "Infield", false, ("101", 7_000)) };

        Assert.Empty(PaceHistory.PersonalBestsBefore(races, "Infield", TonightAtTheTrack.NightOf(Tonight)));
    }
}
