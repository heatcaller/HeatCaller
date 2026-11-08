using System.Reflection;
using System.Text.RegularExpressions;

namespace HeatCaller.Tests;

/// <summary>
/// Every line the announcer says about how much of a race is left follows
/// what ENDS the race (1 Nov 2026, asked for by Asher).
///
/// He heard "X circuits to run" in a race that was only timed. The built-in
/// commentary's "What's still to decide" segment had been handed "About four
/// laps left, going by the clock" - the road was counted in laps whatever
/// ended the race - and turned it into circuits. Now a race the clock ends is
/// talked about in time, a race a lap count ends in laps, and a race with
/// both in the terms of whichever end comes first.
///
/// Each family of lines is run here against each kind of race: the
/// horizon's own sentence, the facts the commentary writes it from, the
/// instruction the commentary is given, the race-terms line every live
/// prompt carries, the scripted pools of every built-in soundscape, and
/// whether the time-left calls are made at all.
/// </summary>
public sealed class RaceTypeCallsTests
{
    /// <summary>A count of laps or circuits to go - what a race the clock ends must never hear.</summary>
    private static readonly Regex LapCount = new(
        @"\b(?:\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|several|many|a few)\s+(?:more\s+)?(?:laps?|circuits?)\b"
        + @"|\blaps?\s+(?:left|to go|remaining|to run)\b|\bcircuits?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Time on a clock - what a race a lap count ends must never hear.</summary>
    private static readonly Regex ClockTalk = new(
        @"\bclock\b|\btimed\b|\b(?:minutes?|seconds?)\s+(?:left|to go|remaining|on the clock)\b|\btime's up\b|\btime is up\b|\bexpired\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public enum RaceType
    {
        Laps,
        Timed,
        TimedFinalLap,
        TimedTimeUnknown,
        BothClockFirst,
        BothLapsFirst,
    }

    /// <summary>The race types the clock ends.</summary>
    private static bool ClockEnds(RaceType type) =>
        type is RaceType.Timed or RaceType.TimedFinalLap or RaceType.TimedTimeUnknown or RaceType.BothClockFirst;

    /// <summary>The road each race type has, as RaceHorizon.LapsLeftIn works it out for a real race of that type.</summary>
    private static RoadLeft RoadFor(RaceType type) => type switch
    {
        RaceType.Laps => RaceHorizon.LapsLeftIn(10, 6, null, TimeSpan.FromMinutes(1), 5.0)!.Value,
        RaceType.Timed => RaceHorizon.LapsLeftIn(null, 30, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(220), 5.0)!.Value,
        RaceType.TimedFinalLap => RaceHorizon.LapsLeftIn(null, 50, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5.1), 5.0)!.Value,
        RaceType.TimedTimeUnknown => new RoadLeft(4, true),
        RaceType.BothClockFirst => RaceHorizon.LapsLeftIn(40, 10, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(250), 6.0)!.Value,
        RaceType.BothLapsFirst => RaceHorizon.LapsLeftIn(20, 16, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(2), 6.0)!.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>A horizon of every kind for every race type, read off a real RaceHorizon fed that race's road.</summary>
    public static IEnumerable<object[]> EveryHorizon()
    {
        foreach (var type in Enum.GetValues<RaceType>())
        {
            foreach (var kind in new[] { HorizonKind.Closing, HorizonKind.WithinReach, HorizonKind.SlippingAway })
            {
                yield return new object[] { type, kind };
            }
        }
    }

    private static Horizon HorizonOf(RaceType type, HorizonKind kind)
    {
        var road = RoadFor(type);
        var watch = new RaceHorizon(() => new DateTime(2026, 11, 1, 20, 0, 0, DateTimeKind.Local));

        // Gaps that read as the kind wanted over the road there is: a big
        // rate for within reach, a small one for closing, a gap going out.
        var (first, second) = kind switch
        {
            HorizonKind.WithinReach => (0.90, 0.30),
            HorizonKind.Closing => (1.90, 1.85),
            _ => (0.40, 0.90),
        };

        // Three readings over two laps - the fewest a rate is taken over
        // since 8 Nov 2026 (RaceHorizon.LeastSamples).
        watch.Observe(new[] { new PairGap("carl", "dave", 1, first, 0) }, 4, road.Laps, road.Estimated, road.TimeLeft);
        watch.Observe(new[] { new PairGap("carl", "dave", 1, (first + second) / 2, 0) }, 5, road.Laps, road.Estimated, road.TimeLeft);
        watch.Observe(new[] { new PairGap("carl", "dave", 1, second, 0) }, 6, road.Laps, road.Estimated, road.TimeLeft);

        var horizon = watch.Pick();

        Assert.NotNull(horizon);
        Assert.Equal(kind, horizon!.Kind);
        Assert.Equal(ClockEnds(type), horizon.Estimated);

        return horizon;
    }

    private static void FollowsTheRace(RaceType type, string text)
    {
        if (ClockEnds(type))
        {
            Assert.DoesNotMatch(LapCount, text);
        }
        else
        {
            Assert.DoesNotMatch(ClockTalk, text);
        }
    }

    // ------------------------------------------------------------------
    //  The horizon's own sentence
    // ------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(EveryHorizon))]
    public void The_horizon_line_says_what_is_left_in_the_race_s_own_terms(RaceType type, HorizonKind kind)
    {
        var line = RaceHorizon.Say(HorizonOf(type, kind), "Carl", "Dave");

        Assert.False(string.IsNullOrWhiteSpace(line));
        FollowsTheRace(type, line);
    }

    [Theory]
    [InlineData(RaceType.Laps, "four laps")]
    [InlineData(RaceType.BothLapsFirst, "four laps")]
    [InlineData(RaceType.Timed, "about a minute and a half")]
    [InlineData(RaceType.BothClockFirst, "about fifty seconds")]
    [InlineData(RaceType.TimedFinalLap, "the final lap")]
    [InlineData(RaceType.TimedTimeUnknown, "what is left of the clock")]
    public void What_is_left_is_the_laps_or_the_clock_that_end_the_race(RaceType type, string said)
    {
        Assert.Equal(said, RaceHorizon.Remaining(HorizonOf(type, HorizonKind.WithinReach)));
    }

    [Fact]
    public void A_timed_race_closing_on_its_final_lap_says_so_and_no_laps()
    {
        var line = RaceHorizon.Say(HorizonOf(RaceType.TimedFinalLap, HorizonKind.Closing), "Carl", "Dave");

        Assert.Contains("on the final lap", line);
        Assert.DoesNotMatch(LapCount, line);
    }

    // ------------------------------------------------------------------
    //  The facts the commentary writes it from, and its instruction
    // ------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(EveryHorizon))]
    public void The_commentary_facts_give_what_is_left_in_the_race_s_own_terms(RaceType type, HorizonKind kind)
    {
        var fact = CommentaryFacts.Horizon(HorizonOf(type, kind), id => id == "carl" ? "Carl" : "Dave", "heat-1");

        FollowsTheRace(type, fact.Text);

        if (ClockEnds(type))
        {
            Assert.Contains("timed race", fact.Text);
            Assert.Contains("lap", fact.Text); // the rate per lap, which a timed race still has
        }
        else
        {
            Assert.Contains("run to a lap count", fact.Text);
            Assert.Contains("4 laps left", fact.Text);
        }
    }

    [Fact]
    public void The_horizon_instruction_asks_for_time_in_a_timed_race_and_laps_only_in_a_lap_race()
    {
        var instruction = (string)typeof(CommentaryService)
            .GetMethod("InstructionFor", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { SegmentKind.Horizon })!;

        Assert.DoesNotContain("how many laps are left", instruction);
        Assert.Contains("as time if the facts say the race is timed", instruction);
        Assert.Contains("as laps only if they say it is run to a lap count", instruction);
    }

    // ------------------------------------------------------------------
    //  The race-terms line every live prompt carries
    // ------------------------------------------------------------------

    [Fact]
    public void A_timed_race_tells_every_live_line_to_say_time_and_never_laps()
    {
        var line = CommentaryService.RaceTermsLine(null, TimeSpan.FromMinutes(5));

        Assert.Contains("timed race (5 minutes)", line);
        Assert.Contains("never as a number of laps or circuits to go", line);
    }

    [Fact]
    public void A_lap_race_tells_every_live_line_to_say_laps_and_never_time()
    {
        var line = CommentaryService.RaceTermsLine(20, null);

        Assert.Contains("run to a lap count (20 laps)", line);
        Assert.Contains("never as time on the clock", line);
    }

    [Fact]
    public void A_race_with_both_limits_says_whichever_comes_first()
    {
        var line = CommentaryService.RaceTermsLine(20, TimeSpan.FromMinutes(5));

        Assert.Contains("20 laps or 5 minutes, whichever comes first", line);
        Assert.Contains("in the same terms they use", line);
    }

    [Fact]
    public void A_race_with_neither_never_says_how_much_is_left()
    {
        Assert.Contains("never say how much of it is left", CommentaryService.RaceTermsLine(null, null));
        Assert.Contains("never say how much of it is left", CommentaryService.RaceTermsLine(0, TimeSpan.Zero));
    }

    [Fact]
    public void A_race_with_a_part_minute_clock_is_given_it_as_a_clock_reads()
    {
        Assert.Contains("timed race (2:30)", CommentaryService.RaceTermsLine(null, TimeSpan.FromSeconds(150)));
    }

    // ------------------------------------------------------------------
    //  Time left as it is said
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(3, "five seconds")]
    [InlineData(12, "ten seconds")]
    [InlineData(13, "fifteen seconds")]
    [InlineData(44, "forty-five seconds")]
    [InlineData(57, "fifty-five seconds")]
    [InlineData(58, "a minute")]
    [InlineData(80, "a minute and a half")]
    [InlineData(100, "a minute and a half")]
    [InlineData(110, "two minutes")]
    [InlineData(155, "two and a half minutes")]
    [InlineData(600, "ten minutes")]
    public void Time_left_is_said_the_way_an_announcer_says_it(int seconds, string said)
    {
        Assert.Equal(said, RaceNumbers.TimeLeftSpoken(TimeSpan.FromSeconds(seconds)));
    }

    // ------------------------------------------------------------------
    //  How much road there is
    // ------------------------------------------------------------------

    [Fact]
    public void A_road_the_clock_ends_carries_the_time_left_and_a_lap_road_none()
    {
        Assert.Equal(TimeSpan.FromSeconds(80), RoadFor(RaceType.Timed).TimeLeft);
        Assert.Equal(TimeSpan.Zero, RoadFor(RaceType.TimedFinalLap).TimeLeft);
        Assert.Equal(TimeSpan.FromSeconds(50), RoadFor(RaceType.BothClockFirst).TimeLeft);
        Assert.Null(RoadFor(RaceType.Laps).TimeLeft);
        Assert.Null(RoadFor(RaceType.BothLapsFirst).TimeLeft);
    }

    [Fact]
    public void A_race_with_both_limits_and_no_pace_yet_has_no_road_rather_than_the_lap_limit()
    {
        // "5 minutes, 99 laps" is a timed race with a ceiling. Before the
        // leader has a pace nobody can say which end comes first, and the
        // lap limit used to win: "ninety-seven laps left".
        Assert.Null(RaceHorizon.LapsLeftIn(99, 2, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(20), null));

        // A lap race with no clock still knows exactly, pace or none.
        Assert.Equal(new RoadLeft(97, false), RaceHorizon.LapsLeftIn(99, 2, null, TimeSpan.FromSeconds(20), null));
    }

    // ------------------------------------------------------------------
    //  Whether the time-left calls are made at all
    // ------------------------------------------------------------------

    [Fact]
    public void The_time_left_calls_are_made_in_a_race_the_clock_ends()
    {
        Assert.True(RaceHorizon.ClockEndsIt(null, 10, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(3), 6.0));
        Assert.True(RaceHorizon.ClockEndsIt(40, 10, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(250), 6.0));

        // Both limits and no pace yet: the clock, set and running, is given the benefit.
        Assert.True(RaceHorizon.ClockEndsIt(40, 1, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(10), null));
    }

    [Fact]
    public void The_time_left_calls_are_not_made_when_the_lap_limit_ends_it_first()
    {
        // Twenty laps, sixteen run, six-second laps: twenty-four seconds of
        // racing left, with three minutes on the clock. "Three minutes to
        // go" would be the announcer getting the race wrong.
        Assert.False(RaceHorizon.ClockEndsIt(20, 16, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(2), 6.0));

        // And a race with no clock never has one to call.
        Assert.False(RaceHorizon.ClockEndsIt(20, 16, null, TimeSpan.FromMinutes(2), 6.0));
    }

    // ------------------------------------------------------------------
    //  The scripted lines of every built-in soundscape
    // ------------------------------------------------------------------

    /// <summary>The default voice lines and every built-in soundscape's, by name.</summary>
    public static IEnumerable<object[]> EveryVoice()
    {
        yield return new object[] { "Default" };

        foreach (var theme in Theme.CreateBuiltInDefaults())
        {
            yield return new object[] { theme.Name };
        }
    }

    private static VoiceProfile VoiceNamed(string name) =>
        name == "Default"
            ? VoiceProfile.CreateDefault()
            : Theme.CreateBuiltInDefaults().Single(t => t.Name == name).Voice;

    private static IEnumerable<string> Lines(VoiceProfile voice, AnnouncementEventType type) =>
        voice.Events.TryGetValue(type.ToString(), out var config) && config is not null
            ? config.Templates.Append(config.Template ?? string.Empty).Where(t => t.Length > 0)
            : Enumerable.Empty<string>();

    [Theory]
    [MemberData(nameof(EveryVoice))]
    public void A_lap_race_s_last_lap_lines_never_talk_about_a_clock(string voiceName)
    {
        // Last lap fires only for a lap-limited race (RaceControlSession,
        // LastLapStarted) - "time's up" there is the wrong race.
        foreach (var line in Lines(VoiceNamed(voiceName), AnnouncementEventType.LastLap))
        {
            Assert.DoesNotMatch(ClockTalk, line);
        }
    }

    [Theory]
    [MemberData(nameof(EveryVoice))]
    public void A_timed_race_s_clock_lines_never_count_laps_to_go(string voiceName)
    {
        // Time's up, final minute, minutes left and the last-minute
        // countdown fire only for a timed race. "One lap to go" after the
        // clock runs out is right - the leader takes the flag next time by -
        // but a count of laps never is.
        var voice = VoiceNamed(voiceName);

        foreach (var type in new[]
                 {
                     AnnouncementEventType.RaceTimeOver,
                     AnnouncementEventType.FinalMinute,
                     AnnouncementEventType.MinutesLeft,
                     AnnouncementEventType.FinalSeconds,
                 })
        {
            foreach (var line in Lines(voice, type))
            {
                Assert.DoesNotMatch(
                    new Regex(@"\b(?:\d+|two|three|four|five|six|seven|eight|nine|ten|\{\w+\})\s+(?:more\s+)?(?:laps?|circuits?)\b|\bcircuits?\b", RegexOptions.IgnoreCase),
                    line);
            }
        }
    }

    // ------------------------------------------------------------------
    //  The wiring - read from the source, as the window cannot be built here
    // ------------------------------------------------------------------

    [Fact]
    public void Every_live_prompt_is_told_the_race_s_terms()
    {
        var service = Source("CommentaryService.cs");
        var main = Source("MainForm.cs");

        Assert.Contains("RaceTermsNowLine();", service);
        Assert.Contains("_commentary.RaceLimitsNow = () =>", main);
        Assert.Contains("(_race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing) && !_race.IsPracticeMode", main);
    }

    [Fact]
    public void The_horizon_is_handed_the_time_left_with_the_road()
    {
        Assert.Contains(
            "Horizons.Observe(pairs, leaderLaps, road?.Laps ?? 0, road?.Estimated ?? false, road?.TimeLeft);",
            Source("CommentaryDirector.cs"));
    }

    [Fact]
    public void Both_time_left_paths_ask_whether_the_clock_ends_the_race()
    {
        var main = Source("MainForm.cs");

        Assert.Contains("is { } secondsLeft\n                && ClockEndsTheRace())", main.Replace("\r\n", "\n"));
        Assert.Contains("if (ClockEndsTheRace()) _announcer.NotifyFinalMinute();", main);
    }

    private static string Source(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName));

    private static string RepoRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory);
        }
    }
}
