namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: the Breaks window opened a saved break it had no choice for
/// - after a round past the round count, or before the finals on a club
/// night - on "No break", and Save then dropped it without anyone having
/// touched it. Windows-only: builds the window.
/// </summary>
public sealed class BugHunt17_RaceDayEventControl_BreaksTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static List<PlannedBreak> SavedUntouched(IReadOnlyList<PlannedBreak> breaks, int rounds, bool clubNight) =>
        StaThread.Run(() =>
        {
            using var form = new BreaksForm(breaks, rounds, clubNight);
            return form.Breaks;
        }, TimeSpan.FromMinutes(1), () => "bug hunt 17, breaks window");

    [Fact]
    public void A_break_after_a_round_that_was_cut_is_still_there_after_Save()
    {
        // Planned with four qualifying rounds; qualifying is now three.
        var breaks = new[]
        {
            new PlannedBreak { AfterRound = 2, Minutes = 10, What = "water the track" },
            new PlannedBreak { AfterRound = 4, Minutes = 20, What = "dinner" },
        };

        var saved = SavedUntouched(breaks, rounds: 3, clubNight: false);

        Assert.Equal(NightBreaks.Summary(breaks, false), NightBreaks.Summary(saved, false));
        Assert.Contains(saved, b => b.AfterRound == 4 && b.Minutes == 20 && b.What == "dinner");
    }

    [Fact]
    public void A_break_before_the_finals_is_kept_on_a_club_night()
    {
        var breaks = new[] { new PlannedBreak { AfterRound = PlannedBreak.BeforeFinals, Minutes = 15, What = "dinner" } };

        var saved = SavedUntouched(breaks, rounds: 3, clubNight: true);

        var kept = Assert.Single(saved);
        Assert.Equal(PlannedBreak.BeforeFinals, kept.AfterRound);
        Assert.Equal(15, kept.Minutes);
    }

    [Fact]
    public void Breaks_the_night_offers_are_saved_as_they_were()
    {
        var breaks = new[]
        {
            new PlannedBreak { AfterRound = 1, Minutes = 5, What = string.Empty },
            new PlannedBreak { AfterRound = PlannedBreak.BeforeFinals, Minutes = 30, What = "dinner" },
        };

        var saved = SavedUntouched(breaks, rounds: 3, clubNight: false);

        Assert.Equal(new[] { 1, PlannedBreak.BeforeFinals }, saved.Select(b => b.AfterRound));
        Assert.Empty(SavedUntouched(Array.Empty<PlannedBreak>(), rounds: 3, clubNight: false));
    }
}
