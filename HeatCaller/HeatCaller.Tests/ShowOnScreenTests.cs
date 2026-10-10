namespace HeatCaller.Tests;

/// <summary>
/// The show on the crowd's screen: the Big Screen's on-air treatment
/// (OnAirTreatment, OnAirBar) and the way it listens to ShowOnAir.
///
/// Three things are worth testing here and none of them is the drawing.
/// First, the state machine: whether the band is up is one question asked of
/// the show itself, so a show that ends, fails halfway or ends while this
/// window was not listening all come to the same place. Second, the
/// marshalling: ShowOnAir raises its event inside its own lock, so the
/// handler may post to the window but must never wait for it - the live
/// test below begins and ends a show from a thread of its own and would
/// hang rather than fail if that were ever got wrong. Third, that the band
/// takes nothing from the race: the board, the roll call, the slides and the
/// clock are all somebody else's, and a treatment that reached for them is
/// how the sponsor video came to play over the start of a heat.
/// </summary>
public sealed class ShowOnScreenTests : IDisposable
{
    // Declared first: the switch below is kept in a file, and this is what
    // points the app at a folder of its own.
    private readonly TempAppData _data = new();

    public ShowOnScreenTests()
    {
        // Both are the whole process's and outlive a test - see each one's
        // own ResetForTests.
        ShowOnAir.ResetForTests();
        OnAirTreatment.ResetForTests();
    }

    public void Dispose()
    {
        ShowOnAir.ResetForTests();
        OnAirTreatment.ResetForTests();
        _data.Dispose();
    }

    // ------------------------------------------------------------------
    //  When the band is up
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void The_band_is_up_while_a_show_plays_and_the_club_wants_it(bool playing, bool wanted, bool expected) =>
        Assert.Equal(expected, OnAirTreatment.Showing(playing, wanted));

    // ------------------------------------------------------------------
    //  What it says
    // ------------------------------------------------------------------

    [Fact]
    public void What_is_on_air_is_read_in_capitals_on_one_line()
    {
        Assert.Equal("RACE RECAP", OnAirTreatment.Caption("Race Recap"));

        // A caption with a line break in it would be drawn as a box by the
        // band's single-line draw, across the wall, at the one moment the
        // room is looking at it.
        Assert.Equal("COLD OPEN", OnAirTreatment.Caption("  Cold\r\n  Open\t"));
    }

    [Fact]
    public void A_show_with_no_name_of_its_own_still_says_something()
    {
        // ShowOnAir's caption is optional on purpose, and an ON AIR tally
        // beside an empty plate reads as a half-drawn screen.
        Assert.Equal(OnAirTreatment.Unnamed, OnAirTreatment.Caption(null));
        Assert.Equal(OnAirTreatment.Unnamed, OnAirTreatment.Caption(string.Empty));
        Assert.Equal(OnAirTreatment.Unnamed, OnAirTreatment.Caption("   \r\n  "));
    }

    [Fact]
    public void Every_kind_of_show_has_words_a_crowd_can_read()
    {
        Assert.Equal("Cold Open", OnAirTreatment.Words(SegmentKind.ColdOpen));
        Assert.Equal("Round Intro", OnAirTreatment.Words(SegmentKind.RaceIntro));
        Assert.Equal("Race Recap", OnAirTreatment.Words(SegmentKind.RaceRecap));
        Assert.Equal("Sign-Off", OnAirTreatment.Words(SegmentKind.SignOffNumbers));

        // Every kind the club can put the music under has its own words, so
        // no enum name can ever end up on the wall.
        Assert.All(ShowSegments.Candidates, kind => Assert.NotEqual(OnAirTreatment.Unnamed, OnAirTreatment.Words(kind)));

        // A mid-race remark never gets the show treatment, so it never gets
        // here - and if one ever did it falls back rather than printing
        // "BattleWatch" across the screen.
        Assert.Equal(OnAirTreatment.Unnamed, OnAirTreatment.Words(SegmentKind.BattleWatch));
    }

    // ------------------------------------------------------------------
    //  The club's switch
    // ------------------------------------------------------------------

    [Fact]
    public void It_is_on_for_a_club_that_has_never_touched_it()
    {
        Assert.True(OnAirTreatment.Wanted);
        Assert.False(File.Exists(_data.PathOf("big-screen.json")));
    }

    [Fact]
    public void A_club_that_switches_it_off_still_has_it_off_next_week()
    {
        OnAirTreatment.Wanted = false;

        Assert.True(File.Exists(_data.PathOf("big-screen.json")));

        // Forgotten in memory, the way a restart forgets it: the answer has
        // to come back off the disk, or a volunteer switches it off again
        // every race night.
        OnAirTreatment.ResetForTests();

        Assert.False(OnAirTreatment.Wanted);
        Assert.False(OnAirTreatment.Showing(playing: true, wanted: OnAirTreatment.Wanted));

        OnAirTreatment.Wanted = true;
        OnAirTreatment.ResetForTests();

        Assert.True(OnAirTreatment.Wanted);
    }

    // ------------------------------------------------------------------
    //  A real show past a real Big Screen
    // ------------------------------------------------------------------

    [Fact]
    public void The_band_comes_up_when_a_show_starts_and_is_gone_when_it_ends()
    {
        var seen = StaThread.Run(() =>
        {
            // The Big Screen as Practice opens it: no schedule, no sponsors
            // and no settings, which is also the "no club logo and no club
            // colours" case the band has to look right in.
            var announcer = new AnnouncementService(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load());

            using var form = new BigScreenForm(new RaceControlSession(), announcer, ProfileStore.Load());

            form.Show();
            Pump();

            var before = form.OnAirUpForTests;

            // Begun and ended from a thread of its own, because that is
            // where they come from on a race night - ShowTreatment's ending
            // awaits with ConfigureAwait(false), so End() runs on a
            // thread-pool thread.
            //
            // It is also what makes this a deadlock test. ShowOnAir raises
            // its event INSIDE its own lock, and this thread asks that same
            // lock whether a show is on twice a second; a handler that
            // waited for this thread rather than posting to it would hang
            // here for ever, and the test would fail on StaThread's limit.
            Task.Run(() => ShowOnAir.Begin("Race Recap")).Wait();
            Pump();

            var band = form.OnAirUpForTests;
            var says = form.OnAirSaysForTests;
            var board = form.BoardUpForTests;

            Task.Run(ShowOnAir.End).Wait();
            Pump();

            var after = form.OnAirUpForTests;
            var boardAfter = form.BoardUpForTests;

            form.Close();

            return (before, band, says, board, after, boardAfter);
        }, TimeSpan.FromSeconds(60), () => "driving a show past the Big Screen");

        Assert.False(seen.before);
        Assert.True(seen.band);
        Assert.Equal("RACE RECAP", seen.says);

        // The whole constraint, in two assertions: the leaderboard is up
        // before, during and after. The band is a strip at the foot of the
        // window, not something over the board.
        Assert.True(seen.board);
        Assert.True(seen.boardAfter);

        Assert.False(seen.after);
    }

    /// <summary>
    /// Lets the window catch up.
    ///
    /// The band is put up by a message POSTED to the window
    /// (BigScreenForm.NudgeOnAir) and, as its backstop, by the window's own
    /// half-second tick - and a test thread has no message loop of its own,
    /// so both are run by hand (StaThread.Settle). Kept going for most of a
    /// second rather than settling once, so a tick gets its turn too: this
    /// test is of the whole arrangement, not only of the post.
    /// </summary>
    private static void Pump()
    {
        for (var i = 0; i < 40; i++)
        {
            StaThread.Settle();
            Thread.Sleep(20);
        }
    }

    // ------------------------------------------------------------------
    //  What the window does, read from its source
    // ------------------------------------------------------------------
    //
    //  Read rather than driven, the way BugHunt5RaceNightTests reads the
    //  roll call's end of a sponsor video and InstallerTests reads the order
    //  of things in Main: a worse test than a real one, and much better than
    //  none, because each of these failures is silent on a race night and
    //  none of them can be seen from outside the window.

    [Fact]
    public void The_show_event_is_posted_to_the_window_and_never_waited_on()
    {
        var nudge = MethodBody(Source("BigScreenForm.cs"), "private void NudgeOnAir()", "private void UpdateOnAir()");

        Assert.Contains("BeginInvoke(", nudge, StringComparison.Ordinal);

        // Invoke would wait for the UI thread from inside ShowOnAir's lock,
        // while the UI thread waits for that lock - a race night stopped
        // dead with the club's music silenced.
        Assert.DoesNotContain("Invoke(", nudge.Replace("BeginInvoke", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);

        // And nothing is read off ShowOnAir in there: that would re-enter
        // the lock the handler is already inside, for an answer UpdateOnAir
        // asks for itself once it is safely on the UI thread.
        Assert.DoesNotContain("ShowOnAir.", nudge, StringComparison.Ordinal);
    }

    [Fact]
    public void Whether_the_band_is_up_is_asked_again_on_every_tick()
    {
        // The sponsor video stayed on screen because StopSponsorVideo was
        // the only route off it and the roll call went round that route.
        // This has no route: the half-second tick asks the show itself and
        // makes the band agree, so a show that ends, fails, or ends while
        // this window was not listening all end up in the same place.
        var tick = MethodBody(Source("BigScreenForm.cs"), "private void RefreshCountdownOnly()", "private void UpdateRollCall()");

        Assert.Contains("UpdateOnAir();", tick, StringComparison.Ordinal);
    }

    [Fact]
    public void Putting_the_band_up_or_down_touches_nothing_else_on_the_screen()
    {
        var update = MethodBody(Source("BigScreenForm.cs"), "private void UpdateOnAir()", "internal bool OnAirUpForTests");

        // "_race" among them on purpose: a cold open and a sign-off happen
        // with nothing on track, so the band must not be gated on a race -
        // and because it covers nothing, there is no race state it has to
        // stand down for either. The decision is two booleans
        // (OnAirTreatment.Showing) and neither of them is about the race.
        foreach (var theirs in new[] { "_race", "_grid", "_rollCall", "_picSponsor", "_attractBoard", "_video", "_pnlOnDeck", "_lblWarning" })
        {
            Assert.DoesNotContain(theirs, update, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_board_the_roll_call_and_the_slides_know_nothing_about_the_band()
    {
        var source = Source("BigScreenForm.cs");

        // The other half of the same promise: nothing that takes the
        // leaderboard's area can hide the band, leave it behind, or be
        // delayed by it. The two never ask for the same room.
        var theirs = new[]
        {
            ("private void UpdateRollCall()", "private void HideRollCall()"),
            ("private void HideRollCall()", "private void UpdateTrackMap()"),
            ("private void ShowNextSponsorSlide()", "private void ShowSponsorVideo(string path)"),
            ("private void ShowSponsorVideo(string path)", "private SponsorVideoSurface MakeVideoSurface()"),
            ("private void StopSponsorVideo()", "private void ShowLeaderboardAgain()"),
            ("private void ShowLeaderboardAgain()", "private static int SponsorIntervalMs(int seconds)"),
            ("private void UpdateAttract()", "private void UpdateTicker()"),
        };

        foreach (var (from, until) in theirs)
        {
            Assert.DoesNotContain("_onAir", MethodBody(source, from, until), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_band_claims_the_very_bottom_of_the_window()
    {
        var source = Source("BigScreenForm.cs");

        var onDeck = source.IndexOf("Controls.Add(_pnlOnDeck);", StringComparison.Ordinal);
        var band = source.IndexOf("Controls.Add(_onAir);", StringComparison.Ordinal);

        Assert.True(onDeck >= 0, "The On Deck bar is no longer added where this test expects it.");

        // Docking is laid out from the last control added to the first, so
        // added after On Deck the band takes the foot of the window and the
        // bar sits above it. Added before, it would take the strip above the
        // bar - which is the leaderboard's, not the band's.
        Assert.True(band > onDeck,
            "The on-air band is added before the On Deck bar, so docking gives it the wrong strip.");

        // And it is a band of the WINDOW, not of the leaderboard's area -
        // which is what keeps it out of the way of the roll call, the slides
        // and the cards.
        Assert.DoesNotContain("_pnlLeaderboardArea.Controls.Add(_onAir", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_window_comes_off_the_show_event_when_it_closes()
    {
        var source = Source("BigScreenForm.cs");

        // ShowOnAir is static and outlives every Big Screen ever opened, so
        // a dead form left on it is a leak - and a handler marshalling into
        // a disposed window is worse. Taken off in BOTH places, exactly as
        // the look's handler is: FormClosed for a window that is closed, and
        // Dispose for one that never was.
        Assert.Contains("ShowOnAir.PlayingChanged += _onAirChangedHandler;", source, StringComparison.Ordinal);
        Assert.Contains("ShowOnAir.PlayingChanged -= _onAirChangedHandler;", source, StringComparison.Ordinal);

        var closed = MethodBody(source, "FormClosed += (_, _) =>", "RefreshAll();");
        Assert.Contains("UnsubscribeOnAir();", closed, StringComparison.Ordinal);

        var disposed = MethodBody(source, "protected override void Dispose(bool disposing)", "base.Dispose(disposing);");
        Assert.Contains("UnsubscribeOnAir();", disposed, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    //  Reading the source
    // ------------------------------------------------------------------
    //
    //  The same pair BugHunt5RaceNightTests keeps, kept again here rather
    //  than shared: each test class that reads source carries its own, so
    //  none of them can be broken by a change made for another.

    /// <summary>The folder the repo is checked out in, found by the file that is only ever at its root.</summary>
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

    private static string Source(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName));

    /// <summary>
    /// The source from <paramref name="from"/> up to <paramref name="until"/>
    /// - enough to say whether a call is inside it.
    /// </summary>
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
