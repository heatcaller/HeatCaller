namespace HeatCaller.Tests;

/// <summary>
/// How chatty as the one dial for the AI commentary (3 Nov 2026): it moves
/// every segment's limits and gaps, a demo gets a night of its own, and
/// Race Control can say what held the AI back in the race just run.
/// </summary>
public sealed class ChattyDialTests : IDisposable
{
    /// <summary>A folder of its own: the director's stores are loaded from it.</summary>
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // ------------------------------------------------------------------
    // The dial's own numbers
    // ------------------------------------------------------------------

    [Fact]
    public void Standard_leaves_every_limit_and_gap_as_the_table_has_it()
    {
        foreach (var spec in CommentaryBudget.AllSpecs)
        {
            Assert.Equal(spec.MaxPerMeeting, ShowChatter.ScaledLimit(spec.MaxPerMeeting, "standard"));
            Assert.Equal(spec.MaxPerClass, ShowChatter.ScaledLimit(spec.MaxPerClass, "standard"));
            Assert.Equal(spec.MinGap, ShowChatter.ScaledGap(spec.MinGap, "standard"));
        }

        Assert.Equal(CommentaryBudget.OpeningFloat, ShowChatter.OpeningFloatFor("standard"));
        Assert.Equal(CommentaryBudget.OpeningFloat, ShowChatter.OpeningFloatFor(string.Empty));
    }

    [Fact]
    public void The_limits_grow_and_the_gaps_shrink_left_to_right()
    {
        var battle = CommentaryBudget.Spec(SegmentKind.BattleWatch);

        for (var i = 1; i < ShowChatter.All.Count; i++)
        {
            var quieter = ShowChatter.All[i - 1].Key;
            var louder = ShowChatter.All[i].Key;

            Assert.True(ShowChatter.ScaledLimit(battle.MaxPerMeeting, louder) >= ShowChatter.ScaledLimit(battle.MaxPerMeeting, quieter));
            Assert.True(ShowChatter.ScaledGap(battle.MinGap, louder) <= ShowChatter.ScaledGap(battle.MinGap, quieter));
            Assert.True(ShowChatter.OpeningFloatFor(louder) >= ShowChatter.OpeningFloatFor(quieter));
        }

        Assert.Equal(int.MaxValue, ShowChatter.ScaledLimit(battle.MaxPerMeeting, "nonstop"));
        Assert.Equal(1.0, ShowChatter.OpeningFloatFor("nonstop"));
    }

    [Fact]
    public void A_quiet_stop_never_takes_a_ticked_segment_to_nought()
    {
        foreach (var spec in CommentaryBudget.AllSpecs)
        {
            Assert.True(ShowChatter.ScaledLimit(spec.MaxPerClass, "sparing") >= 1, spec.Title);
            Assert.True(ShowChatter.ScaledLimit(spec.MaxPerMeeting, "sparing") >= 1, spec.Title);
        }
    }

    [Fact]
    public void The_line_under_the_dial_says_what_non_stop_lifts()
    {
        var said = ShowChatter.Describe("nonstop");

        Assert.Contains("No limit on how often a segment comes back", said, StringComparison.Ordinal);

        // WHY (25 Nov 2026, Asher: "fix the app text too"): the per-segment
        // figures and the battle watch's gap are no longer spelled out.
        var standard = ShowChatter.Describe("standard");

        Assert.Contains("Each segment has a limit for the night and per class.", standard, StringComparison.Ordinal);
        Assert.DoesNotContain("waits 40s", standard, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // A demo's own night
    // ------------------------------------------------------------------

    private static CommentaryDirector NewDirector(AppSettings settings) =>
        new(new CommentaryService(ProfileStore.Load(), settings), settings);

    /// <summary>NewMeeting, without the "nothing new to say" question - these tests are about the counts.</summary>
    private static void Open(CommentaryDirector director, string day, int? demoRun = null)
    {
        director.NewMeeting(day, demoRun);
        director.Budget.NothingNewToSay = null;
    }

    private static bool Write(CommentaryBudget budget, SegmentKind kind)
    {
        var verdict = budget.Reserve(kind);

        if (!verdict.Allowed)
        {
            return false;
        }

        budget.Settle(verdict.Ticket);

        return true;
    }

    [Fact]
    public void A_demo_starts_a_fresh_night_and_the_real_one_comes_back_as_it_was()
    {
        var settings = new AppSettings { CommentaryEnabled = true };
        var director = NewDirector(settings);

        Open(director, "day-1");
        Assert.True(Write(director.Budget, SegmentKind.ColdOpen));
        director.Save();

        Assert.False(director.Budget.Peek(SegmentKind.ColdOpen).Allowed);

        // The demo: the cold open is there to be had again.
        Open(director, "day-1", demoRun: 1);
        Assert.True(director.OnADemoNight);
        Assert.True(director.Budget.Peek(SegmentKind.ColdOpen).Allowed);

        // Whatever the demo spends is not written down.
        var savedBefore = settings.CommentaryBudgetNight;
        Assert.True(Write(director.Budget, SegmentKind.ColdOpen));
        director.Save();
        Assert.Equal(savedBefore, settings.CommentaryBudgetNight);

        // Back to the real night: the cold open has still been said.
        Open(director, "day-1");
        Assert.False(director.OnADemoNight);
        Assert.False(director.Budget.Peek(SegmentKind.ColdOpen).Allowed);
    }

    [Fact]
    public void Each_demo_gets_a_night_of_its_own()
    {
        var director = NewDirector(new AppSettings { CommentaryEnabled = true });

        Open(director, "day-1", demoRun: 1);
        Assert.True(Write(director.Budget, SegmentKind.ColdOpen));
        Assert.False(director.Budget.Peek(SegmentKind.ColdOpen).Allowed);

        Open(director, "day-1", demoRun: 2);
        Assert.True(director.Budget.Peek(SegmentKind.ColdOpen).Allowed);
    }

    [Fact]
    public void Leaving_the_real_night_for_a_demo_saves_it_first()
    {
        var settings = new AppSettings { CommentaryEnabled = true };
        var director = NewDirector(settings);

        Open(director, "day-1");
        Assert.True(Write(director.Budget, SegmentKind.ColdOpen));

        // No Save after the line - the demo's NewMeeting does it.
        Open(director, "day-1", demoRun: 1);
        Open(director, "day-1");

        Assert.False(director.Budget.Peek(SegmentKind.ColdOpen).Allowed);
    }

    // ------------------------------------------------------------------
    // This race, in one line
    // ------------------------------------------------------------------

    [Fact]
    public void The_race_line_counts_this_race_only_and_names_the_dial()
    {
        var account = new NightAccount();
        account.NewMeeting("day-1");

        account.Weighed(SegmentKind.PaceNote);
        account.Refused(BudgetRefusal.TheRelayIsBusy);

        account.RaceStarted();

        Assert.Null(account.RaceReport());

        account.Weighed(SegmentKind.PaceNote);
        account.Refused(BudgetRefusal.TooSoonAfterTheLast);
        account.Weighed(SegmentKind.PaceNote);
        account.Refused(BudgetRefusal.TooSoonAfterTheLast);
        account.Weighed(SegmentKind.BattleWatch);
        account.Spoke(SegmentKind.BattleWatch, null);

        var line = account.RaceReport();

        Assert.NotNull(line);
        Assert.Contains("1 segment said", line!, StringComparison.Ordinal);
        Assert.Contains("too soon after the last line x2", line!, StringComparison.Ordinal);
        Assert.DoesNotContain("relay busy", line!, StringComparison.Ordinal);
        Assert.Contains("How chatty", line!, StringComparison.Ordinal);
    }
}
