namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the Announcer page's director and show code.
///
/// #106: the show music test button had none of the checks the other show
/// buttons make, so it opened a second opener and bed over a race or a show
/// on air, and held nothing while it ran.
///
/// #107: a long read (tonight's recap, the season piece, a driver's night)
/// stopped by the announcer's Mute was still answered "Played, and saved".
///
/// #5: AI in-race lines went on being written and said under a red or a
/// yellow, and a red's frozen board turned two parked cars into a "fight".
///
/// The rules are pure statics and tested directly; the wiring is page code
/// no test can drive, so its shape is pinned from the source, in the style
/// of HandOffTests.
/// </summary>
public sealed class BugHunt24_AnnouncerControlTests
{
    // ------------------------------------------------ #106 show music test

    [Theory]
    [InlineData(RaceState.Staging)]
    [InlineData(RaceState.Countdown)]
    [InlineData(RaceState.Racing)]
    public void BugHunt24_The_show_music_test_refuses_while_a_race_is_on(RaceState state)
    {
        var refused = AnnouncerControl.ShowTestRefusal(state, floorBusy: false, heldBy: null, segmentInFlight: false, showOnAir: false);

        Assert.Equal("A race is on. Wait for the flag and try again.", refused);
    }

    [Theory]
    [InlineData(RaceState.Idle)]
    [InlineData(RaceState.Finished)]
    public void BugHunt24_The_show_music_test_plays_between_races_when_nothing_is_on_air(RaceState state)
    {
        Assert.Null(AnnouncerControl.ShowTestRefusal(state, floorBusy: false, heldBy: null, segmentInFlight: false, showOnAir: false));
    }

    [Fact]
    public void BugHunt24_The_show_music_test_refuses_while_something_holds_the_floor_and_names_it()
    {
        Assert.Equal(
            "tonight's recap is on air. Wait for it to finish and try again.",
            AnnouncerControl.ShowTestRefusal(RaceState.Finished, floorBusy: true, heldBy: "tonight's recap", segmentInFlight: false, showOnAir: false));

        Assert.Equal(
            "Something else is on air. Wait for it to finish and try again.",
            AnnouncerControl.ShowTestRefusal(RaceState.Idle, floorBusy: true, heldBy: null, segmentInFlight: false, showOnAir: false));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void BugHunt24_The_show_music_test_refuses_while_a_segment_or_show_is_on_air(bool segmentInFlight, bool showOnAir)
    {
        Assert.Equal(
            "A commentary segment is on air. Wait for it to finish and try again.",
            AnnouncerControl.ShowTestRefusal(RaceState.Idle, floorBusy: false, heldBy: null, segmentInFlight, showOnAir));
    }

    [Fact]
    public void BugHunt24_The_show_music_test_asks_the_gate_and_holds_the_director_off_while_it_plays()
    {
        var body = WithoutComments(MethodBody(Source(),
            "public async Task<string?> TestShowMusicAsync()", "internal static string? ShowTestRefusal("));

        var gate = body.IndexOf("ShowTestRefusal(_race.State, ShowFloor.Busy, ShowFloor.HeldBy, _segmentInFlight, ShowOnAir.Playing)", StringComparison.Ordinal);
        var held = body.IndexOf("_segmentInFlight = true;", StringComparison.Ordinal);
        var run = body.IndexOf("treatment.RunAsync(", StringComparison.Ordinal);
        var finallyAt = body.IndexOf("finally", StringComparison.Ordinal);
        var released = body.IndexOf("_segmentInFlight = false;", StringComparison.Ordinal);

        Assert.True(gate > 0 && run > gate, "The show music test plays without asking whether a race or a show is on.");
        Assert.True(held > gate && run > held, "The show music test does not hold the director's segments off while it plays.");
        Assert.True(finallyAt > run && released > finallyAt, "The show music test does not let the director go again when it ends.");
    }

    // ------------------------------------------------ #107 long read muted

    [Fact]
    public void BugHunt24_A_long_read_that_was_never_muted_is_answered_as_gone_out()
    {
        Assert.Null(AnnouncerControl.CutOffByMute(mutedNow: false, mutedAt: 3, mutedGenerationNow: 3, piece: "tonight's recap"));
    }

    [Fact]
    public void BugHunt24_A_long_read_with_the_announcer_still_muted_is_a_cut_off()
    {
        Assert.Equal(
            "The announcer was muted, so tonight's recap was cut off. Unmute it on the Mixer and play it again.",
            AnnouncerControl.CutOffByMute(mutedNow: true, mutedAt: 3, mutedGenerationNow: 4, piece: "tonight's recap"));
    }

    [Fact]
    public void BugHunt24_A_long_read_muted_and_unmuted_again_is_still_a_cut_off()
    {
        // The mute bins the queue and stops the feeding for good, so a mute
        // lifted two seconds later still leaves the piece unfinished.
        Assert.Equal(
            "The announcer was muted, so Sam's night was cut off. Unmute it on the Mixer and play it again.",
            AnnouncerControl.CutOffByMute(mutedNow: false, mutedAt: 3, mutedGenerationNow: 4, piece: "Sam's night"));
    }

    [Theory]
    [InlineData("public async Task<string?> PlayTonightsRecapAsync()", "private const string TheNightRecap")]
    [InlineData("public async Task<string?> PlayTheSeasonAsync(", "public async Task<string?> PlayADriversNightAsync(")]
    [InlineData("public async Task<string?> PlayADriversNightAsync(", "private static void NextRaceCalled(")]
    public void BugHunt24_A_long_read_never_answers_played_without_asking_whether_it_was_muted(string from, string until)
    {
        var body = WithoutComments(MethodBody(Source(), from, until));

        var counted = body.IndexOf("var mutedAt = _announcer.MutedGeneration;", StringComparison.Ordinal);
        var voiced = body.IndexOf("VoiceTheReadAhead(chunks, readToken)", StringComparison.Ordinal);

        Assert.True(counted > 0 && voiced > counted, "The mute count is not taken before the read starts.");

        // After the read starts, the only "it went out" answer is the one
        // that asks about the mute first.
        Assert.DoesNotContain("return null;", body[voiced..], StringComparison.Ordinal);

        var answers = 0;

        for (var at = body.IndexOf("return AfterTheRead(mutedAt, ", voiced, StringComparison.Ordinal);
             at >= 0;
             at = body.IndexOf("return AfterTheRead(mutedAt, ", at + 1, StringComparison.Ordinal))
        {
            answers++;
        }

        Assert.Equal(2, answers);
    }

    // ------------------------------------------------ #5 AI lines under a flag

    [Theory]
    [InlineData(FlagState.Yellow, true)]
    [InlineData(FlagState.Red, true)]
    [InlineData(FlagState.Green, false)]
    [InlineData(FlagState.None, false)]
    [InlineData(FlagState.Checkered, false)]
    public void BugHunt24_In_race_AI_lines_wait_for_a_yellow_or_a_red(FlagState flag, bool held)
    {
        Assert.Equal(held, AnnouncerControl.InRaceLinesHeldBy(flag));
    }

    [Fact]
    public void BugHunt24_The_director_neither_speaks_nor_feeds_the_frozen_board_under_a_flag()
    {
        var feed = WithoutComments(MethodBody(Source(),
            "private void FeedTheDirector()", "internal static bool InRaceLinesHeldBy("));

        var redCheck = feed.IndexOf("if (_race.Flag == FlagState.Red)", StringComparison.Ordinal);
        var ended = feed.IndexOf("director.Battles.Observe(Array.Empty<PairGap>());", StringComparison.Ordinal);
        var fed = feed.IndexOf("director.Standings(PairsFrom(ordered)", StringComparison.Ordinal);

        Assert.True(redCheck > 0 && ended > redCheck && fed > ended,
            "Under a red the frozen board is still fed to Battle watch, so two parked cars become a fight.");

        var held = feed.IndexOf("if (racing && InRaceLinesHeldBy(_race.Flag))", StringComparison.Ordinal);
        var spoken = feed.IndexOf("_ = SpeakNextSegmentAsync(director);", StringComparison.Ordinal);

        Assert.True(held > 0 && spoken > held, "In-race AI lines are still written and said under a flag.");

        var whyNot = WithoutComments(MethodBody(Source(), "private string? WhyNotNow(", "/// Continuity on its own"));

        Assert.Contains("InRaceLinesHeldBy(_race.Flag)", whyNot, StringComparison.Ordinal);
        Assert.Contains("\"a flag came out while it was being written\"", whyNot, StringComparison.Ordinal);
    }

    [Fact]
    public void BugHunt24_An_empty_tick_ends_every_fight_so_a_red_cannot_carry_one_over_the_stoppage()
    {
        // What the red's empty tick relies on: a fight that was being
        // followed when the red came out is over, so after the restart its
        // length cannot include the stoppage.
        var now = new DateTime(2026, 11, 23, 20, 0, 0, DateTimeKind.Utc);
        var watch = new BattleWatch(() => now);
        var pair = new PairGap("A", "B", 1, 0.40, 0, BehindLaps: 5);

        for (var tick = 0; tick < 40; tick++)
        {
            watch.Observe(new[] { pair });
            now = now.AddSeconds(0.5);
        }

        Assert.NotNull(watch.Pick());

        watch.Observe(Array.Empty<PairGap>());

        Assert.Equal(0, watch.Watching);
        Assert.Null(watch.Pick());
    }

    // ------------------------------------------------ helpers

    private static string Source()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", "AnnouncerControl.Director.cs")).Replace("\r\n", "\n");
    }

    private static string WithoutComments(string body) =>
        string.Join(
            "\n",
            body.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string MethodBody(string source, string from, string until)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);

        Assert.True(start >= 0, $"\"{from}\" is not in the file any more - this test needs its name updated.");

        var rest = source[start..];
        var end = rest.IndexOf(until, StringComparison.Ordinal);

        Assert.True(end >= 0, $"\"{until}\" no longer follows \"{from}\" - this test needs its bounds updated.");

        return rest[..end];
    }
}
