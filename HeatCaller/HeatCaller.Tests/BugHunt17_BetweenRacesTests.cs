namespace HeatCaller.Tests;

/// <summary>
/// 13 Nov 2026, Bug Hunt 17. Two in the gap between races.
///
/// The Race day preset put every answer back to its default, the automatic
/// countdown among them - so a race day set to "Race day" called no countdown
/// marks all night while the Timetable's "Count down by itself" showed ticked.
///
/// And with the call-up's box unticked, the marshal call and the
/// not-checked-in call waited all gap for a call-up that was never going.
/// </summary>
public sealed class BugHunt17_BetweenRacesTests
{
    private static readonly DateTime T0 = new(2026, 11, 13, 19, 0, 0, DateTimeKind.Utc);

    private static GapFacts Next(int notIn = 0) => new()
    {
        NextId = "r3",
        Round = "Heat 3",
        ClassName = "Box Stock",
        Drivers = new[] { "Sam", "Bill", "Frank" },
        Marshals = new[] { ("Marshal 1", "Turn 1"), ("Marshal 2", "Turn 2") },
        NotCheckedIn = Enumerable.Range(1, notIn).Select(i => $"Late {i}").ToList(),
        Track = "the club track",
    };

    /// <summary>A gap opened at T0 with the floor free from T0 and the next race set.</summary>
    private static GapState OpenGap(GapFacts facts)
    {
        var gap = new GapState();
        gap.Open("race-1", T0);
        gap.NextRace(facts.NextId);
        gap.FloorFree(T0);
        return gap;
    }

    // -------------------------------------------------------------- presets

    [Fact]
    public void A_race_day_set_to_Race_day_keeps_the_Timetables_automatic_countdown()
    {
        var settings = new AppSettings
        {
            BetweenRacesAutoCountdown = true,
            BetweenRacesCountdownMarks = "10,2",
            BetweenRacesCountdownTimesUp = true,
        };

        var rules = GapRules.From(settings, GapPreset.RaceDay.Key);

        Assert.True(rules.AutoCountdown);
        Assert.Equal(new[] { 10, 2 }, rules.CountdownMarks);
        Assert.True(rules.CountdownTimesUp);
    }

    [Fact]
    public void No_preset_touches_the_automatic_countdown()
    {
        var own = GapRules.Default with { AutoCountdown = true, CountdownMarks = new[] { 10, 2 }, CountdownTimesUp = true };

        foreach (var preset in GapPreset.All)
        {
            var over = preset.Over(own);

            Assert.True(over.AutoCountdown, preset.Name);
            Assert.Equal(own.CountdownMarks, over.CountdownMarks);
            Assert.True(over.CountdownTimesUp, preset.Name);
        }
    }

    [Fact]
    public void Picking_Race_day_on_the_card_saves_the_automatic_countdown_as_it_was()
    {
        // What the card's preset pick does: the preset over the rules, saved.
        var settings = new AppSettings { BetweenRacesAutoCountdown = true, BetweenRacesLevel = "quiet" };

        GapPreset.RaceDay.Over(GapRules.From(settings)).SaveTo(settings);

        Assert.True(settings.BetweenRacesAutoCountdown);
        Assert.Equal(GapLevel.Normal, GapRules.From(settings).Level);
    }

    [Fact]
    public void Race_day_still_puts_the_cards_other_answers_back()
    {
        var changed = GapRules.Default with { Level = GapLevel.Busy, SpaceSeconds = 55, LongBreakMinutes = 30, CallUpsWithOnAir = false };

        var over = GapPreset.RaceDay.Over(changed);

        Assert.Equal(GapRules.Default.Level, over.Level);
        Assert.Equal(GapRules.Default.SpaceSeconds, over.SpaceSeconds);
        Assert.Equal(GapRules.Default.LongBreakMinutes, over.LongBreakMinutes);
        Assert.True(over.CallUpsWithOnAir);
    }

    // ------------------------------------------------------------------ gap

    [Fact]
    public void With_the_call_up_unticked_the_marshals_are_still_called()
    {
        var facts = Next();
        var gap = OpenGap(facts);
        var rules = GapRules.Default with { CallUps = false };
        var now = T0.AddSeconds(15);

        Assert.Null(gap.WhyNot(GapLine.Marshals, rules, now, facts, false, false));
        Assert.Equal(GapLine.Marshals, gap.NextCall(rules, now, facts, false, false, out _));
    }

    [Fact]
    public void With_the_call_up_unticked_the_not_checked_in_call_is_still_said()
    {
        var facts = Next(notIn: 2);
        var gap = OpenGap(facts);
        var rules = GapRules.Default with { CallUps = false, Marshals = false, NotCheckedIn = true };
        var now = T0.AddSeconds(15);

        Assert.Null(gap.WhyNot(GapLine.NotCheckedIn, rules, now, facts, false, false));
        Assert.Equal(GapLine.NotCheckedIn, gap.NextCall(rules, now, facts, false, false, out _));
    }

    [Fact]
    public void A_Call_Up_pressed_after_the_automatic_marshals_does_not_say_them_again()
    {
        var facts = Next();
        var gap = OpenGap(facts);
        var rules = GapRules.Default with { CallUps = false };
        var now = T0.AddSeconds(15);

        // The marshals go by themselves, the call-up's box being unticked.
        Assert.Equal(GapLine.Marshals, gap.NextCall(rules, now, facts, false, false, out _));
        Assert.True(gap.MarshalsFollowACallUp(rules, facts));
        gap.Said(GapLine.Marshals, now);

        // Then the race director presses Call Up: the call-up, and no marshals after it.
        var pressed = now.AddSeconds(40);
        gap.NextRace(facts.NextId);
        gap.Said(GapLine.CallUp, pressed);

        Assert.False(gap.MarshalsFollowACallUp(rules, facts));
        Assert.Equal("the marshals have been called", gap.WhyNot(GapLine.Marshals, rules, pressed.AddMinutes(2), facts, false, false));
    }

    [Fact]
    public void A_Call_Up_pressed_first_is_still_followed_by_the_marshals_once()
    {
        var facts = Next();
        var gap = OpenGap(facts);
        var rules = GapRules.Default with { CallUps = false };

        gap.Said(GapLine.CallUp, T0.AddSeconds(5));
        Assert.True(gap.MarshalsFollowACallUp(rules, facts));
        gap.Said(GapLine.Marshals, T0.AddSeconds(5));

        Assert.False(gap.MarshalsFollowACallUp(rules, facts));

        // Unticked, or nobody posted: never.
        var fresh = OpenGap(facts);
        Assert.False(fresh.MarshalsFollowACallUp(rules with { Marshals = false }, facts));
        Assert.False(fresh.MarshalsFollowACallUp(rules, facts with { Marshals = Array.Empty<(string, string)>() }));

        // A new next race starts again.
        gap.NextRace("r4");
        Assert.True(gap.MarshalsFollowACallUp(rules, facts));
    }

    [Fact]
    public void The_Call_Up_button_asks_the_gap_before_saying_the_marshals()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "check.cmd")))
        {
            folder = folder.Parent;
        }

        Assert.NotNull(folder);

        var source = File.ReadAllText(Path.Combine(folder!.FullName, "HeatCaller", "HeatCaller", "RaceControlControl.BetweenRaces.cs"));
        var start = source.IndexOf("internal string CallUpNow(", StringComparison.Ordinal);
        var end = source.IndexOf("private void PushNextRaceBack()", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);
        Assert.Contains("if (Gap.MarshalsFollowACallUp(rules, facts))", source[start..end], StringComparison.Ordinal);
    }

    [Fact]
    public void With_the_call_up_ticked_both_still_wait_for_it()
    {
        var facts = Next(notIn: 2);
        var gap = OpenGap(facts);
        var rules = GapRules.Default with { NotCheckedIn = true };
        var now = T0.AddSeconds(15);

        Assert.Equal("the call-up goes first", gap.WhyNot(GapLine.Marshals, rules, now, facts, false, false));
        Assert.Equal("the call-up goes first", gap.WhyNot(GapLine.NotCheckedIn, rules, now, facts, false, false));
    }
}
