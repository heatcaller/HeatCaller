namespace HeatCaller.Tests;

/// <summary>
/// The throw into the stop set (StopSetThrow): the model's line is used only
/// when it names the show, the recap host and the announcer and says a break
/// is coming; otherwise a line written in the app goes instead, still naming
/// all three.
/// </summary>
public sealed class StopSetThrowTests
{
    private static readonly StopSetThrow.Names All = StopSetThrow.Names.Of("Friday Night Thunder", "Jess Cole", "Dave Marsh");

    [Fact]
    public void A_line_that_does_the_job_is_used()
    {
        const string Line = "That's Heat 2 in the books on Friday Night Thunder! Jess Cole has the race recap after the break - Dave Marsh here, stay with us.";

        Assert.Equal(Line, StopSetThrow.Checked(Line, All));
    }

    [Theory]
    [InlineData("Jess Cole has the race recap after the break. Dave Marsh here, stay with us.", "show")]
    [InlineData("Friday Night Thunder rolls on - the recap is after the break. Dave Marsh here, stay with us.", "host")]
    [InlineData("Friday Night Thunder rolls on - Jess Cole has the recap after the break. Stay with us.", "announcer")]
    [InlineData("Friday Night Thunder rolls on, Jess Cole has the recap, I'm Dave Marsh.", "break")]
    public void A_line_missing_what_it_must_say_is_not_used(string line, string missing)
    {
        var notes = new List<string>();

        Assert.Null(StopSetThrow.Checked(line, All, notes: notes));
        Assert.Contains(notes, n => missing switch
        {
            "show" => n.Contains("show's name"),
            "host" => n.Contains("recap host"),
            "announcer" => n.Contains("announcer"),
            _ => n.Contains("break"),
        });
    }

    [Fact]
    public void A_line_that_runs_on_is_not_used()
    {
        var line = "Friday Night Thunder, Jess Cole, Dave Marsh, after the break. " + string.Join(" ", Enumerable.Repeat("more", StopSetThrow.MaxWords));

        Assert.Null(StopSetThrow.Checked(line, All));
    }

    [Fact]
    public void A_repeat_of_a_recent_throw_is_not_used()
    {
        const string Line = "Friday Night Thunder continues - Jess Cole and the recap after the break. Dave Marsh here, stay with us.";

        Assert.Null(StopSetThrow.Checked(Line.ToUpperInvariant(), All, new[] { Line }));
    }

    [Fact]
    public void A_line_ending_on_his_name_gets_a_closing_phrase()
    {
        var said = StopSetThrow.Checked("Friday Night Thunder's recap with Jess Cole is after the break. This is Dave Marsh.", All);

        Assert.Equal("Friday Night Thunder's recap with Jess Cole is after the break. This is Dave Marsh - stay with us.", said);
    }

    [Fact]
    public void A_speaker_label_and_quotes_come_off()
    {
        var said = StopSetThrow.Checked("ANNOUNCER: \"Friday Night Thunder rolls on - Jess Cole has the recap after the break. Dave Marsh here, stay with us.\"", All);

        Assert.Equal("Friday Night Thunder rolls on - Jess Cole has the recap after the break. Dave Marsh here, stay with us.", said);
    }

    [Fact]
    public void Coming_up_with_a_colon_is_not_taken_for_a_label()
    {
        var names = StopSetThrow.Names.Of("", "", "");

        Assert.Equal("Coming up: the race recap, right after the break.", StopSetThrow.Checked("Coming up: the race recap, right after the break.", names));
    }

    [Fact]
    public void Names_that_are_not_set_are_not_required()
    {
        var names = StopSetThrow.Names.Of("", "", "");

        Assert.Equal("The recap is up after the break - stay with us.", StopSetThrow.Checked("The recap is up after the break - stay with us.", names));
    }

    [Fact]
    public void Every_fallback_names_all_three_and_the_break()
    {
        for (var pick = 0; pick < 4; pick++)
        {
            var line = StopSetThrow.Fallback(All, pick);

            Assert.Contains("Friday Night Thunder", line);
            Assert.Contains("Jess Cole", line);
            Assert.Contains("Dave Marsh", line);
            Assert.NotNull(StopSetThrow.Checked(line, All));
            Assert.False(line.TrimEnd('.', '!').EndsWith("Dave Marsh"));
        }
    }

    [Fact]
    public void A_fallback_with_no_names_set_makes_none_up()
    {
        var line = StopSetThrow.Fallback(StopSetThrow.Names.Of(null, null, null), 0);

        Assert.Equal("Don't go anywhere - the race recap is coming up after the break.", line);
    }

    [Fact]
    public void With_no_break_a_line_that_promises_one_is_not_used()
    {
        var notes = new List<string>();

        Assert.Null(StopSetThrow.Checked(
            "Friday Night Thunder rolls on - Jess Cole has the recap after the break. Dave Marsh here, stay with us.",
            All, notes: notes, breakFollows: false));
        Assert.Contains(notes, n => n.Contains("isn't one"));
    }

    [Fact]
    public void With_no_break_a_line_saying_the_recap_is_next_is_used()
    {
        const string Line = "Friday Night Thunder rolls on - Jess Cole has the recap coming up next. Dave Marsh here, stay with us.";

        Assert.Equal(Line, StopSetThrow.Checked(Line, All, breakFollows: false));
    }

    [Fact]
    public void Every_no_break_fallback_names_all_three_and_promises_no_break()
    {
        for (var pick = 0; pick < 4; pick++)
        {
            var line = StopSetThrow.Fallback(All, pick, breakFollows: false);

            Assert.Contains("Friday Night Thunder", line);
            Assert.Contains("Jess Cole", line);
            Assert.Contains("Dave Marsh", line);
            Assert.NotNull(StopSetThrow.Checked(line, All, breakFollows: false));
            Assert.False(line.TrimEnd('.', '!').EndsWith("Dave Marsh"));
        }
    }

    [Theory]
    [InlineData("Friday Night Thunder", "Jess Cole", "the Friday Night Thunder race recap with Jess Cole")]
    [InlineData("Friday Night Thunder", "", "the Friday Night Thunder race recap")]
    [InlineData("", "Jess Cole", "the race recap with Jess Cole")]
    [InlineData("", "", "the race recap")]
    public void The_recap_is_named_with_what_is_set(string show, string host, string expected)
    {
        Assert.Equal(expected, StopSetThrow.RecapPhrase(StopSetThrow.Names.Of(show, host, "")));
    }
}
