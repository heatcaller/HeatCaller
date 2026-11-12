namespace HeatCaller.Tests;

/// <summary>
/// Ready to race and the drivers' meeting (12 Nov 2026, setup plan B3 and
/// D10). See ReadyChecklist and DriversMeeting.
/// </summary>
public sealed class ReadyToRaceTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 18, 0, 0, DateTimeKind.Utc);

    private static readonly HashSet<string> NoTicks = new();

    private static ReadyFacts AllGood() => new(
        DecoderConnected: true,
        DecoderHeardTonight: true,
        PhoneServerRunning: true,
        WebsiteOnline: true,
        VoiceProblem: null,
        HeatsBuilt: true,
        HeatsPosted: true,
        NotHeard: Array.Empty<string>(),
        PapersNotInOrder: Array.Empty<string>(),
        LastBackupUtc: Now.AddDays(-1));

    [Fact]
    public void A_night_that_is_ready_leaves_only_the_meeting_and_the_club_s_own_lines()
    {
        var items = ReadyChecklist.Build(AllGood(), Now, new[] { "Water the track" }, NoTicks);

        Assert.Equal(new[] { "meeting", "own:Water the track" }, items.Where(i => !i.Done).Select(i => i.Key));
    }

    [Fact]
    public void Each_line_says_what_is_left_in_plain_words()
    {
        var facts = AllGood() with
        {
            DecoderHeardTonight = false,
            VoiceProblem = "no voice is set up",
            HeatsPosted = false,
            NotHeard = new[] { "Al Gorithim", "Rhoda Rage" },
            PapersNotInOrder = new[] { "Hugh Turn" },
            LastBackupUtc = Now.AddDays(-30),
        };

        var left = ReadyChecklist.Build(facts, Now, null, NoTicks).Where(i => !i.Done).ToDictionary(i => i.Key, i => i.Words);

        Assert.StartsWith("the decoder hasn't heard a car yet tonight", left["decoder"]);
        Assert.Equal("the announcer can't speak: no voice is set up", left["voice"]);
        Assert.Equal("post tonight's heats", left["heats"]);
        Assert.Equal("2 cars not heard tonight (Al Gorithim, Rhoda Rage)", left["cars"]);
        Assert.Equal("1 driver's papers not in order (Hugh Turn)", left["papers"]);
        Assert.Equal("back up before the night starts", left["backup"]);
    }

    [Fact]
    public void A_thing_the_club_does_not_use_is_left_off_the_list()
    {
        var facts = AllGood() with { DecoderConnected = null, PhoneServerRunning = null, WebsiteOnline = null, HeatsPosted = null, PapersNotInOrder = null };

        var keys = ReadyChecklist.Build(facts, Now, null, NoTicks).Select(i => i.Key).ToList();

        Assert.DoesNotContain("decoder", keys);
        Assert.DoesNotContain("server", keys);
        Assert.DoesNotContain("website", keys);
        Assert.DoesNotContain("papers", keys);
        Assert.Contains("heats", keys);
    }

    [Fact]
    public void A_hand_tick_counts_but_never_over_something_seen_done()
    {
        var ticks = new HashSet<string> { "meeting", "voice" };

        var items = ReadyChecklist.Build(AllGood(), Now, null, ticks).ToDictionary(i => i.Key);

        Assert.True(items["meeting"].Done);
        Assert.True(items["voice"].SeenDone);
        Assert.False(items["voice"].Ticked);
    }

    [Fact]
    public void Before_the_heats_are_built_cars_are_not_asked_about()
    {
        var facts = AllGood() with { HeatsBuilt = false, HeatsPosted = false };

        var items = ReadyChecklist.Build(facts, Now, null, NoTicks);

        Assert.DoesNotContain(items, i => i.Key == "cars");
        Assert.Equal("build tonight's heats", items.Single(i => i.Key == "heats").Words);
    }

    [Fact]
    public void The_ready_lines_have_buttons_to_the_screens_that_sort_them()
    {
        Assert.Equal("Connection", CloseNightForm.GoCaption(CloseNightKind.Decoder));
        Assert.Equal("Meeting Sheet...", CloseNightForm.GoCaption(CloseNightKind.Meeting));
        Assert.Equal("Who's Racing", CloseNightForm.GoCaption(CloseNightKind.Papers));
    }

    // --- D10: the drivers' meeting ------------------------------------------------

    private static DriversMeeting.Facts Meeting(RaceEvent day, bool posts = true) =>
        new(day, new[] { "Juniors", "Stock", "Mod" }, 9, WaitsForMarshals: true, HasMarshalPosts: posts, ProtestMinutes: 5, PapersChecked: true);

    [Fact]
    public void The_meeting_says_tonight_s_order_times_breaks_marshals_and_notes()
    {
        var day = new RaceEvent
        {
            Name = "Club Night 12 Nov",
            QualifyingRounds = 3,
            FirstRaceLocalTime = TimeSpan.FromHours(19),
            OutByLocalTime = TimeSpan.FromHours(22),
            Breaks = { new PlannedBreak { AfterRound = 2, Minutes = 10, What = "water the track" } },
            Notes = "The back straight jump has been moved.",
        };

        var points = DriversMeeting.Points(Meeting(day));

        Assert.Equal("Welcome to Club Night 12 Nov.", points[0]);
        Assert.Contains("Classes tonight, in running order: Juniors, Stock and Mod.", points);
        Assert.Contains(points, p => p.StartsWith("3 qualifying rounds, 9 heats on the program"));
        Assert.Contains(points, p => p.StartsWith("Breaks: After Q2, 10 min (water the track)"));
        Assert.Contains(points, p => p.StartsWith("We have to be out of the venue by"));
        Assert.Contains(points, p => p.Contains("doesn't start until the marshals are in place"));
        Assert.Contains(points, p => p.StartsWith("Results are provisional for 5 minutes"));
        Assert.Contains(points, p => p.Contains("waiver or membership"));
        Assert.Contains("Tonight: The back straight jump has been moved.", points);
    }

    [Fact]
    public void A_night_with_no_marshal_posts_and_no_notes_leaves_those_points_out()
    {
        var day = new RaceEvent { Name = "Tonight", Shape = RaceDayShape.ClubNight };

        var points = DriversMeeting.Points(Meeting(day, posts: false));

        Assert.DoesNotContain(points, p => p.Contains("marshals"));
        Assert.DoesNotContain(points, p => p.StartsWith("Tonight:"));
        Assert.DoesNotContain(points, p => p.StartsWith("Results are provisional"));
    }

    [Fact]
    public void Read_out_the_meeting_drops_the_heading_and_the_dashes()
    {
        var text = DriversMeeting.Text(Meeting(new RaceEvent { Name = "Tonight" }));

        Assert.StartsWith("DRIVERS' MEETING - Tonight", text);

        var spoken = DriversMeeting.Spoken(text);
        Assert.StartsWith("Welcome to Tonight.", spoken);
        Assert.DoesNotContain("DRIVERS' MEETING", spoken);
        // One line, and no point still wearing its list dash (a dash inside a sentence stays).
        Assert.DoesNotContain("\n", spoken);
        Assert.DoesNotContain(". - ", spoken);
    }

    [Fact]
    public void A_long_point_wraps_at_a_word_on_the_printed_sheet()
    {
        var line = string.Join(' ', Enumerable.Repeat("word", 40));

        var pieces = TextSheetRenderer.Wrap(line, 40).ToList();

        Assert.True(pieces.Count > 1);
        Assert.All(pieces, p => Assert.True(p.Length <= 40));
        Assert.All(pieces.Skip(1), p => Assert.StartsWith("   ", p));
    }
}
