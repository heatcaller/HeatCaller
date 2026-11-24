namespace HeatCaller.Tests;

/// <summary>
/// Between races fixes from the 23 Nov 2026 bug hunt (24): countdown marks
/// already said were never said again after the race was pushed back; a
/// club notice that could not be filled in was booked as said; and after
/// Skip Next Call-up the first call-up the room heard was worded as a repeat.
/// All pure: no window, no voice.
/// </summary>
public sealed class BugHunt24_BetweenRacesTests
{
    private static readonly DateTime T0 = new(2026, 11, 23, 19, 0, 0, DateTimeKind.Utc);

    private static GapFacts Next(DateTime? due = null, string track = "the club track") => new()
    {
        NextId = "r3",
        Round = "Heat 3",
        ClassName = "Box Stock",
        Drivers = new[] { "Sam", "Bill", "Frank" },
        Track = track,
        DueUtc = due,
    };

    private static GapState OpenGap(GapFacts facts)
    {
        var gap = new GapState();
        gap.Open("race-2", T0);
        gap.NextRace(facts.NextId);
        gap.FloorFree(T0);
        return gap;
    }

    private static readonly GapRules Countdown = GapRules.Default with { AutoCountdown = true, CountdownMarks = new[] { 5, 1 } };

    // ------------------------------------------------- marks after a push-back

    [Fact]
    public void A_mark_already_called_is_called_again_after_the_race_is_pushed_back()
    {
        var facts = Next(due: T0.AddMinutes(6));
        var gap = OpenGap(facts);

        var five = T0.AddMinutes(1).AddSeconds(5);
        Assert.Equal(5, gap.MarkDue(Countdown, five, facts, false));
        gap.SaidMark(5, five);

        var one = T0.AddMinutes(5).AddSeconds(5);
        Assert.Equal(1, gap.MarkDue(Countdown, one, facts, false));
        gap.SaidMark(1, one);

        // A driver's "Need 5 minutes": the race is now due five minutes later.
        gap.PushBack(T0.AddMinutes(5).AddSeconds(20), 5);
        var later = facts with { DueUtc = T0.AddMinutes(11) };

        Assert.Null(gap.MarkDue(Countdown, T0.AddMinutes(5).AddSeconds(30), later, false));

        var fiveAgain = T0.AddMinutes(6).AddSeconds(5);
        Assert.Equal(5, gap.MarkDue(Countdown, fiveAgain, later, false));
        gap.SaidMark(5, fiveAgain);

        // Once only at its new time.
        Assert.Null(gap.MarkDue(Countdown, T0.AddMinutes(6).AddSeconds(10), later, false));

        Assert.Equal(1, gap.MarkDue(Countdown, T0.AddMinutes(10).AddSeconds(5), later, false));
    }

    [Fact]
    public void A_push_back_that_does_not_move_the_start_does_not_repeat_the_mark()
    {
        var facts = Next(due: T0.AddMinutes(6));
        var gap = OpenGap(facts);

        var one = T0.AddMinutes(5).AddSeconds(5);
        Assert.Equal(1, gap.MarkDue(Countdown, one, facts, false));
        gap.SaidMark(1, one, facts.DueUtc);

        // Auto start waiting on a promised time: the start stays put.
        gap.PushBack(T0.AddMinutes(5).AddSeconds(10));

        Assert.Null(gap.MarkDue(Countdown, T0.AddMinutes(5).AddSeconds(15), facts, false));
    }

    [Fact]
    public void A_mark_long_past_its_new_time_is_not_said_late_after_a_push_back()
    {
        var facts = Next(due: T0.AddMinutes(6));
        var gap = OpenGap(facts);

        var five = T0.AddMinutes(1).AddSeconds(5);
        Assert.Equal(5, gap.MarkDue(Countdown, five, facts, false));
        gap.SaidMark(5, five);

        gap.PushBack(T0.AddMinutes(4), 1);

        Assert.Null(gap.MarkDue(Countdown, T0.AddMinutes(4).AddSeconds(5), facts with { DueUtc = T0.AddMinutes(7) }, false));
    }

    [Fact]
    public void A_due_time_that_drifts_a_few_seconds_does_not_say_a_mark_twice()
    {
        var facts = Next(due: T0.AddMinutes(6));
        var gap = OpenGap(facts);

        var one = T0.AddMinutes(5).AddSeconds(2);
        Assert.Equal(1, gap.MarkDue(Countdown, one, facts, false));
        gap.SaidMark(1, one);

        // A held Auto start nudges the due on by twenty seconds.
        Assert.Null(gap.MarkDue(Countdown, T0.AddMinutes(5).AddSeconds(30), facts with { DueUtc = T0.AddMinutes(6).AddSeconds(20) }, false));
    }

    // ------------------------------------------------- the club's notice

    private const string MinutesNotice = "Racing resumes in about {minutes} minutes.";

    [Fact]
    public void A_notice_that_cannot_be_filled_is_not_booked_as_said()
    {
        var rules = GapRules.Default with { CallUps = false, Marshals = false, LongBreakNotice = false, CustomLines = new[] { MinutesNotice } };
        var facts = Next(due: null);
        var gap = OpenGap(facts);
        var now = T0.AddMinutes(1);

        Assert.NotNull(gap.WhyNot(GapLine.Notice, rules, now, facts, false, false));
        Assert.Null(gap.NextCall(rules, now, facts, false, false, out _));
        Assert.Equal(0, gap.Lines);

        // A time turns up later in the gap: the notice goes then.
        var timed = facts with { DueUtc = now.AddMinutes(8) };

        Assert.Null(gap.WhyNot(GapLine.Notice, rules, now, timed, false, false));
        Assert.Equal(GapLine.Notice, gap.NextCall(rules, now, timed, false, false, out _));
        Assert.Equal("Racing resumes in about 8 minutes.", GapWords.Notice(timed, rules, now));
    }

    [Fact]
    public void The_club_notice_that_fills_is_always_found_among_ones_that_do_not()
    {
        var rules = GapRules.Default with
        {
            CustomLines = new[] { MinutesNotice, "Pit lane is closed.", "Welcome to {track}." },
        };
        var facts = Next(due: null, track: string.Empty);

        for (var i = 0; i < 100; i++)
        {
            Assert.Equal("Pit lane is closed.", GapWords.Notice(facts, rules, T0));
        }

        Assert.True(GapWords.AnyNoticeFills(facts, rules, T0));
        Assert.False(GapWords.AnyNoticeFills(facts, rules with { CustomLines = new[] { MinutesNotice } }, T0));
    }

    [Fact]
    public void Hear_the_gap_with_an_unfillable_notice_runs_as_if_there_were_none()
    {
        var facts = Next(due: null);
        var plain = GapRules.Default;
        var withNotice = GapRules.Default with { CustomLines = new[] { MinutesNotice } };

        static IEnumerable<(TimeSpan, GapLine)> Order(IReadOnlyList<GapPreview.Step> steps) => steps.Select(s => (s.At, s.Kind)).ToList();

        var without = GapPreview.Run(plain, facts, T0, withTheDirector: true, commentaryOn: true, sponsorReads: true, onAirOpensTheNextRace: false);
        var unfillable = GapPreview.Run(withNotice, facts, T0, withTheDirector: true, commentaryOn: true, sponsorReads: true, onAirOpensTheNextRace: false);

        Assert.Equal(Order(without), Order(unfillable));
    }

    // ------------------------------------------------- the call-up after a skip

    private static bool IsRepeatWording(string text) =>
        text.StartsWith("Reminder", StringComparison.Ordinal)
        || text.StartsWith("Still waiting", StringComparison.Ordinal)
        || text.Contains("this is your call", StringComparison.Ordinal);

    [Fact]
    public void The_first_call_up_after_a_skip_is_not_worded_as_a_repeat()
    {
        var facts = Next();
        var gap = OpenGap(facts);

        gap.SkipCallUp(T0.AddSeconds(10));

        Assert.Equal(1, gap.CallUps);
        Assert.False(gap.CallUpSaid);

        for (var i = 0; i < 30; i++)
        {
            var text = GapPreview.TextFor(GapLine.CallUp, GapRules.Default, facts, T0.AddSeconds(200), gap);
            Assert.False(IsRepeatWording(text), text);
            Assert.Contains("Heat 3", text);
        }

        gap.Said(GapLine.CallUp, T0.AddSeconds(200));

        Assert.True(gap.CallUpSaid);

        for (var i = 0; i < 30; i++)
        {
            var text = GapPreview.TextFor(GapLine.CallUp, GapRules.Default, facts, T0.AddSeconds(400), gap);
            Assert.True(IsRepeatWording(text), text);
        }
    }

    [Fact]
    public void A_new_next_race_has_no_call_up_said()
    {
        var gap = OpenGap(Next());
        gap.Said(GapLine.CallUp, T0.AddSeconds(10));

        Assert.True(gap.CallUpSaid);

        gap.NextRace("r4");

        Assert.False(gap.CallUpSaid);
    }
}
