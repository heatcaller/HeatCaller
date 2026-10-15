namespace HeatCaller.Tests;

/// <summary>
/// The count of what the announcer tried to say and what got out.
///
/// ASKED FOR IN THESE WORDS: "I didn't know how many are being dropped as
/// I don't have a visual." The lines were always in the console; nobody
/// reads the console on a race night. Most of this file is the two
/// properties a number on a screen has to have to be worth putting there -
/// that it adds up, and that it is silent when there is nothing to say.
/// </summary>
public sealed class AnnouncerLoadTests
{
    [Fact]
    public void A_new_race_counts_nothing()
    {
        var load = new AnnouncerLoad();

        Assert.Equal(0, load.Offered);
        Assert.Equal(0, load.Accepted);
        Assert.Equal(0, load.Held);
        Assert.Equal(0, load.Dropped);
        Assert.Equal(0, load.Unsaid);
        Assert.True(load.Balances);
    }

    [Fact]
    public void The_parts_add_up_to_the_whole()
    {
        // THE ONE PROPERTY THAT MAKES THIS WORTH SHOWING. A counter whose
        // parts do not sum to its whole is worse than no counter: the club
        // reads it, believes it, and tunes a dial against a number that is
        // lying.
        var load = new AnnouncerLoad();

        for (var i = 0; i < 7; i++)
        {
            load.NoteOffered();
        }

        load.NoteAccepted();
        load.NoteAccepted();
        load.NoteHeld();
        load.NoteHeld();
        load.NoteHeld();
        load.NoteDropped();
        load.NoteDropped();

        Assert.Equal(7, load.Offered);
        Assert.Equal(2, load.Accepted);
        Assert.Equal(3, load.Held);
        Assert.Equal(2, load.Dropped);
        Assert.Equal(5, load.Unsaid);
        Assert.True(load.Balances);
    }

    [Fact]
    public void A_race_ending_leaves_the_figures_for_the_next_one_to_clear()
    {
        var load = new AnnouncerLoad();

        load.NoteOffered();
        load.NoteDropped();

        load.Reset();

        Assert.Equal(0, load.Offered);
        Assert.Equal(0, load.Dropped);
        Assert.True(load.Balances);
    }

    // ------------------------------------------------------------------
    //  The badge on Race Control
    // ------------------------------------------------------------------

    [Fact]
    public void A_quiet_race_puts_nothing_on_the_strip_at_all()
    {
        // The rule the notes and votes counts beside it go by: "0 lost" on
        // a quiet practice session reads as a fault, and the busiest
        // screen in the app does not get to spend space saying nothing
        // happened.
        var load = new AnnouncerLoad();

        load.NoteOffered();
        load.NoteAccepted();

        Assert.Null(load.Badge);
        Assert.False(load.BadgeIsAWarning);
    }

    [Fact]
    public void Held_lines_are_reported_but_not_as_a_warning()
    {
        // Held is the club's own dial working. Lighting it like an alarm is
        // how a club ends up turning the fix back off.
        var load = new AnnouncerLoad();

        load.NoteOffered();
        load.NoteHeld();

        Assert.Equal("1 held", load.Badge);
        Assert.False(load.BadgeIsAWarning);
    }

    [Fact]
    public void Lost_lines_take_the_badge_and_light_it()
    {
        var load = new AnnouncerLoad();

        load.NoteOffered();
        load.NoteDropped();

        Assert.Equal("1 lost", load.Badge);
        Assert.True(load.BadgeIsAWarning);
    }

    [Fact]
    public void A_race_with_both_shows_the_lost_figure_and_not_the_reassuring_one()
    {
        // The one word on the strip is never the comfortable one: held is
        // the dial doing its job, lost is the announcer losing lines, and
        // the person holding the microphone needs the second number.
        var load = new AnnouncerLoad();

        for (var i = 0; i < 12; i++)
        {
            load.NoteOffered();
        }

        for (var i = 0; i < 9; i++)
        {
            load.NoteHeld();
        }

        for (var i = 0; i < 3; i++)
        {
            load.NoteDropped();
        }

        Assert.Equal("3 lost", load.Badge);
        Assert.True(load.BadgeIsAWarning);
    }

    // ------------------------------------------------------------------
    //  The sentence under the dial
    // ------------------------------------------------------------------

    [Fact]
    public void Nothing_offered_yet_says_so_rather_than_reading_as_a_fault()
    {
        var said = new AnnouncerLoad().Report();

        Assert.Contains("nothing to show", said, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_report_says_what_the_figures_mean_and_not_only_what_they_are()
    {
        // A club reading "74 held" has to be told that is the dial working,
        // or it reads as the app losing three quarters of the show.
        var load = new AnnouncerLoad();

        for (var i = 0; i < 10; i++)
        {
            load.NoteOffered();
        }

        load.NoteAccepted();
        load.NoteAccepted();

        for (var i = 0; i < 8; i++)
        {
            load.NoteHeld();
        }

        var said = load.Report();

        Assert.Contains("10 timing calls", said);
        Assert.Contains("on purpose", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("running out of room", said, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_report_with_lost_lines_names_the_fix()
    {
        // Lines being lost is the announcer running out of room rather than
        // the dial working, and the answer is a WIDER gap - which is the
        // opposite of what a club reaches for, so the sentence has to say
        // it.
        var load = new AnnouncerLoad();

        for (var i = 0; i < 4; i++)
        {
            load.NoteOffered();
        }

        load.NoteAccepted();
        load.NoteDropped();
        load.NoteDropped();
        load.NoteDropped();

        var said = load.Report();

        Assert.Contains("wider gap", said, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void One_of_anything_is_said_in_the_singular()
    {
        // "1 timing calls offered" and "1 were held back" are the forms
        // Plural exists to stop, and a count of one is the common case on
        // a quiet practice session.
        var load = new AnnouncerLoad();

        load.NoteOffered();
        load.NoteHeld();

        var said = load.Report();

        Assert.Contains("1 timing call offered", said);
        Assert.Contains("1 was held", said);
        Assert.DoesNotContain("1 timing calls", said);
        Assert.DoesNotContain("1 were held", said);
    }
}
