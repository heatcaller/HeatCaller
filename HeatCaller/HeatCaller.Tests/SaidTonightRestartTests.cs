namespace HeatCaller.Tests;

/// <summary>
/// WHAT THE ANNOUNCER HAS ALREADY SAID, ACROSS A RESTART (13 Oct 2026) -
/// see <see cref="SaidTonight.Save"/> and <see cref="SaidTonight.Restore"/>.
///
/// WHY THIS FILE EXISTS AT ALL
///
/// Both methods were written a month ago, commented, correct - and called
/// by nobody. <see cref="CommentaryBudget"/>'s twins beside them were
/// wired up the same day, so the SPEND survived a restart and the FACTS
/// did not. A club that closed HeatCaller between the heats and the mains
/// came back and heard every lap record, every storyline and every
/// championship line over again, with the budget correctly reporting it
/// had already paid for them once.
///
/// Nothing was broken. Nothing threw. The one thing that would have found
/// it - a test that asked whether anything calls these - did not exist,
/// and the director's own Save said in words that it held "tonight's
/// spend and tonight's facts" while holding one. That comment is the
/// reason this went a month: anybody checking would have read it and
/// stopped.
///
/// So the test worth reading first is
/// <see cref="A_fact_said_before_the_restart_is_not_said_again_after_it"/>,
/// and the one worth reading second is
/// <see cref="Last_weeks_spend_cannot_silence_this_week"/> - because the
/// failure in THAT direction is the worse of the two. A night that
/// repeats itself is irritating; a night that has nothing to say because
/// it thinks it said it all last Saturday is a feature a club switches
/// off.
/// </summary>
public sealed class SaidTonightRestartTests
{
    private const string Tonight = "2026-10-13";
    private const string LastWeek = "2026-10-06";

    private static SaidTonight Started(string day = Tonight)
    {
        var said = new SaidTonight();
        said.NewMeeting(day);
        return said;
    }

    // ------------------------------------------------------------------
    //  The round trip
    // ------------------------------------------------------------------

    [Fact]
    public void A_fact_said_before_the_restart_is_not_said_again_after_it()
    {
        var before = Started();
        before.Mark("record:stock:dave");

        var saved = before.Save();

        // The laptop closes and opens. A brand-new director, the same day.
        var after = new SaidTonight();
        after.Restore(saved, Tonight);

        Assert.True(after.Spent("record:stock:dave"));
    }

    [Fact]
    public void Everything_said_comes_back_and_nothing_else_does()
    {
        var before = Started();
        before.Mark(new[] { "a", "b", "c" });

        var after = new SaidTonight();
        after.Restore(before.Save(), Tonight);

        Assert.Equal(3, after.Count);
        Assert.False(after.Spent("d"));
    }

    [Fact]
    public void A_night_that_said_nothing_restores_to_a_night_that_said_nothing()
    {
        var after = new SaidTonight();
        after.Restore(Started().Save(), Tonight);

        Assert.Equal(0, after.Count);
    }

    // ------------------------------------------------------------------
    //  The day is what decides
    // ------------------------------------------------------------------

    [Fact]
    public void Last_weeks_spend_cannot_silence_this_week()
    {
        // THE WORSE FAILURE OF THE TWO, and the reason Restore takes the
        // day rather than just the string. A night that repeats itself is
        // irritating. A night with nothing to say because it believes it
        // said everything last Saturday is a feature a club switches off,
        // and nothing in the log would explain it.
        var lastWeek = Started(LastWeek);
        lastWeek.Mark("record:stock:dave");

        var tonight = new SaidTonight();
        tonight.Restore(lastWeek.Save(), Tonight);

        Assert.False(tonight.Spent("record:stock:dave"));
        Assert.Equal(0, tonight.Count);
    }

    [Fact]
    public void Restoring_twice_for_the_same_day_is_harmless()
    {
        // The director calls this whenever a race day is opened, which
        // happens more than once an evening. It has to be idempotent or
        // the second call would clear what the first picked up.
        var before = Started();
        before.Mark("a");

        var saved = before.Save();
        var after = new SaidTonight();

        after.Restore(saved, Tonight);
        after.Restore(saved, Tonight);

        Assert.Equal(1, after.Count);
        Assert.True(after.Spent("a"));
    }

    [Fact]
    public void A_restore_does_not_lose_what_tonight_has_already_said()
    {
        // The order the director uses: a day is opened, facts are said,
        // the day is opened again. The second restore must not throw away
        // the segments spoken in between.
        var after = Started();
        after.Mark("said-after-opening");

        after.Restore(Started().Save(), Tonight);

        Assert.True(after.Spent("said-after-opening"));
    }

    // ------------------------------------------------------------------
    //  Nothing here may cost a club its announcer
    // ------------------------------------------------------------------

    [Fact]
    public void A_corrupt_setting_is_a_fresh_night_rather_than_a_crash()
    {
        // settings.json is a file a club can open, and somebody will. A
        // throw here happens while a race day is being opened, which is
        // the announcer never starting at all.
        foreach (var rubbish in new[] { "{", "[]", "not json", "{\"DayId\":", "\0" })
        {
            var said = new SaidTonight();

            said.Restore(rubbish, Tonight);

            Assert.Equal(0, said.Count);
        }
    }

    [Fact]
    public void A_first_ever_night_has_nothing_saved_and_that_is_not_an_error()
    {
        foreach (var nothing in new string?[] { null, string.Empty, "   " })
        {
            var said = new SaidTonight();

            said.Restore(nothing, Tonight);

            Assert.Equal(0, said.Count);
        }
    }

    [Fact]
    public void A_saved_night_with_no_facts_in_it_restores_cleanly()
    {
        var said = new SaidTonight();

        said.Restore("{\"DayId\":\"" + Tonight + "\",\"Keys\":null}", Tonight);

        Assert.Equal(0, said.Count);
    }

    [Fact]
    public void A_blank_key_in_a_hand_edited_file_is_passed_over()
    {
        var said = new SaidTonight();

        said.Restore("{\"DayId\":\"" + Tonight + "\",\"Keys\":[\"a\",\"\",\"  \"]}", Tonight);

        Assert.Equal(1, said.Count);
        Assert.True(said.Spent("a"));
    }

    // ------------------------------------------------------------------
    //  What is deliberately NOT carried over
    // ------------------------------------------------------------------

    [Fact]
    public void The_theme_cooldown_does_not_survive_a_restart_and_should_not()
    {
        // Restore marks the keys and nothing else, on purpose. The
        // cooldown is about what the room heard in the last minute and a
        // half, and a laptop that has been closed and reopened has had a
        // longer gap than that by definition - so a theme held back for
        // variety would be held back for a room that has heard nothing.
        var before = Started();

        before.Mark(FactPack.Build(new[] { new FactBlock("k", "text", "dave") }));

        Assert.True(before.ThemeCooled("dave"));

        var after = new SaidTonight();
        after.Restore(before.Save(), Tonight);

        Assert.False(after.ThemeCooled("dave"));

        // The fact itself still is spent, which is the half that matters.
        Assert.True(after.Spent("k"));
    }

    [Fact]
    public void A_new_day_after_a_restore_starts_the_night_over()
    {
        var said = new SaidTonight();
        said.Restore(Started().Save(), Tonight);
        said.Mark("a");

        said.NewMeeting("2026-10-20");

        Assert.Equal(0, said.Count);
        Assert.False(said.Spent("a"));
    }

    // ------------------------------------------------------------------
    //  The shape on disk
    // ------------------------------------------------------------------

    [Fact]
    public void What_is_saved_is_small_enough_to_live_in_a_settings_file()
    {
        // Written after every spoken segment, so a night of two hundred
        // facts must not turn settings.json into something a club notices.
        var said = Started();

        for (var i = 0; i < 200; i++)
        {
            said.Mark($"record:stock:driver-{i}:lap");
        }

        var saved = said.Save();

        Assert.True(saved.Length < 32 * 1024, $"a night came to {saved.Length} characters");
    }

    [Fact]
    public void What_is_saved_names_the_day_it_belongs_to()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(Started().Save());

        // Without this the day check in Restore has nothing to check
        // against, and last week's spend silences this week.
        Assert.Equal(Tonight, doc.RootElement.GetProperty("DayId").GetString());
    }
}
