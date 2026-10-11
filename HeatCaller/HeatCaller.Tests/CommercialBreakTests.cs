namespace HeatCaller.Tests;

/// <summary>
/// The commercial break after a race (10 Oct 2026): the club's sponsors
/// played as a real advert break between the teaser and the recap, for as
/// long as the model takes to write the recap.
///
/// The club owner's ask, in his words: "I want the sponsors to play as a
/// commercial break after the race and before the race show recap. This way
/// we can fill the time it takes to generate the content." And: "big screen
/// should align with that."
///
/// WHAT IS WORTH TESTING HERE, and in what way.
///
/// THE DECISION, driven directly. Whether a break is over is one pure
/// function of four facts (CommercialBreak.Over) for exactly this reason:
/// the sequence that drives it needs a model, a race and a sound device, and
/// takes most of a minute to say any of it out loud. The floor and the
/// ceiling are the two things in this feature a club would actually notice
/// being wrong, so every case of them is a line below rather than a
/// rehearsal.
///
/// THE GIVING BACK. A break left open keeps the standings off the crowd's
/// wall for the rest of the evening, and on a club with video sponsors the
/// rotation keeps starting another one and AdBreak keeps the club's music
/// down under each - which is the night AdBreak's own notes were written
/// about. So the count is held to the same promises AdBreak and ShowOnAir
/// are: counted once, ended once, never below zero, and whatever starts one
/// ends it from a finally.
///
/// THE SCREEN, driven for real. A BigScreenForm with a sponsor store is
/// built on an STA thread and a break is begun and ended from a thread of
/// its own, which is where they come from on a race night. That is also a
/// deadlock test, exactly as ShowOnScreenTests' live test is.
///
/// THE SEQUENCE, read as text. Where the break sits relative to the teaser
/// and the recap is an ORDER of lines in one method, which is what reading
/// the file can see and what a behavioural test would have to fake its way
/// around - the bargain RecapBroadcastTests and HonoursSegmentTests already
/// strike for the rest of this sequence.
///
/// Nothing here plays anything: AudioBus is silent for the whole suite
/// (TestSetup), and no break in here has a video in it.
/// </summary>
public sealed class CommercialBreakTests : IDisposable
{
    // Declared first: the on-air switch is kept in a file, and this is what
    // points the app at a folder of its own.
    private readonly TempAppData _data = new();

    public CommercialBreakTests()
    {
        // All four are the whole process's and outlive a test - see each
        // one's own ResetForTests.
        CommercialBreak.ResetForTests();
        AdBreak.ResetForTests();
        ShowOnAir.ResetForTests();
        OnAirTreatment.ResetForTests();
    }

    public void Dispose()
    {
        // A test that left a break open would hold the sponsors over the
        // crowd's screen in every test after it, and in a real app run
        // started by the layout tests in the same process.
        CommercialBreak.ResetForTests();
        AdBreak.ResetForTests();
        ShowOnAir.ResetForTests();
        OnAirTreatment.ResetForTests();
        _data.Dispose();
    }

    // ------------------------------------------------------------------
    //  The floor
    // ------------------------------------------------------------------

    [Fact]
    public void A_break_never_ends_inside_its_floor_however_fast_the_recap_came_back()
    {
        // The whole point of the floor. A recap already in hand - a big
        // model on a box in the corner, or a cached answer - would otherwise
        // make the break the crowd's screen flicking to a sponsor and back
        // inside a second, which reads as a fault and is worth nothing to
        // the sponsor who paid for it.
        Assert.Equal(
            BreakEnding.Running,
            CommercialBreak.Over(TimeSpan.Zero, recapReady: true, shownEverything: true, nextRaceCalled: false));

        Assert.Equal(
            BreakEnding.Running,
            CommercialBreak.Over(CommercialBreak.Shortest - TimeSpan.FromMilliseconds(1), true, true, false));

        // And at the floor exactly, it ends.
        Assert.Equal(
            BreakEnding.RecapReady,
            CommercialBreak.Over(CommercialBreak.Shortest, recapReady: true, shownEverything: false, nextRaceCalled: false));
    }

    [Fact]
    public void The_floor_is_long_enough_to_be_a_break_and_the_ceiling_long_enough_to_cover_the_model()
    {
        // One whole slide at SponsorSettings' own default, so the shortest
        // break is a slide held still rather than a flicker.
        Assert.Equal(TimeSpan.FromSeconds(new SponsorSettings().SlideSeconds), CommercialBreak.Shortest);

        // And longer than the shortest gap a paid read is worth starting
        // into at all, so the read a break earns has somewhere to go.
        Assert.True(CommercialBreak.Shortest >= SponsorCopy.ShortestUsefulGap);

        // Rehearsal 5 (10 Oct 2026) had the model take 41.6s to write and
        // render a recap. A ceiling anywhere near that would cut short
        // exactly the nights this feature was asked for.
        Assert.True(CommercialBreak.Longest >= TimeSpan.FromSeconds(83));

        Assert.True(CommercialBreak.Shortest < CommercialBreak.Longest);
    }

    // ------------------------------------------------------------------
    //  What ends a break
    // ------------------------------------------------------------------

    [Fact]
    public void The_break_ends_once_the_recap_is_written_and_rendered()
    {
        // Which is the whole job: the break is as long as the model needed
        // and not a second longer.
        Assert.Equal(
            BreakEnding.RecapReady,
            CommercialBreak.Over(TimeSpan.FromSeconds(20), recapReady: true, shownEverything: false, nextRaceCalled: false));

        // Not ready: the sponsors keep the screen.
        Assert.Equal(
            BreakEnding.Running,
            CommercialBreak.Over(TimeSpan.FromSeconds(20), recapReady: false, shownEverything: false, nextRaceCalled: false));
    }

    [Fact]
    public void The_ceiling_ends_a_break_the_model_never_answered()
    {
        // A model that has hung must not go on selling a sponsor
        // impressions nobody bought, and must not keep the standings off the
        // crowd's wall all evening.
        Assert.Equal(
            BreakEnding.Ceiling,
            CommercialBreak.Over(CommercialBreak.Longest, recapReady: false, shownEverything: false, nextRaceCalled: false));

        Assert.Equal(
            BreakEnding.Ceiling,
            CommercialBreak.Over(CommercialBreak.Longest + TimeSpan.FromMinutes(5), false, false, false));

        // A moment before it, still running - the ceiling is a backstop and
        // not the normal way a break ends.
        Assert.Equal(
            BreakEnding.Running,
            CommercialBreak.Over(CommercialBreak.Longest - TimeSpan.FromMilliseconds(1), false, false, false));
    }

    [Fact]
    public void A_break_that_has_been_round_every_sponsor_ends_rather_than_playing_them_again()
    {
        // Fifteen seconds of slides against a forty-second recap. The break
        // ENDS and the quiet before the recap's own opener holds the rest:
        // holding sounds like a station, and looping would sell a sponsor
        // impressions nobody bought.
        Assert.Equal(
            BreakEnding.EverySponsorShown,
            CommercialBreak.Over(TimeSpan.FromSeconds(15), recapReady: false, shownEverything: true, nextRaceCalled: false));

        // The recap still outranks it, so the console says the true reason
        // on the ordinary night where both are true at once.
        Assert.Equal(
            BreakEnding.RecapReady,
            CommercialBreak.Over(TimeSpan.FromSeconds(15), recapReady: true, shownEverything: true, nextRaceCalled: false));
    }

    [Fact]
    public void The_next_race_called_to_the_grid_abandons_the_break_at_once()
    {
        // Ahead of everything, the floor included. The heats can turn round
        // faster than the model answers, and adverts still running over the
        // next countdown are worse than no adverts at all.
        Assert.Equal(
            BreakEnding.NextRaceCalled,
            CommercialBreak.Over(TimeSpan.Zero, recapReady: false, shownEverything: false, nextRaceCalled: true));

        Assert.Equal(
            BreakEnding.NextRaceCalled,
            CommercialBreak.Over(TimeSpan.FromSeconds(1), recapReady: true, shownEverything: true, nextRaceCalled: true));

        Assert.Equal(
            BreakEnding.NextRaceCalled,
            CommercialBreak.Over(CommercialBreak.Longest, recapReady: true, shownEverything: true, nextRaceCalled: true));
    }

    // ------------------------------------------------------------------
    //  Giving the screen back
    // ------------------------------------------------------------------

    [Fact]
    public void A_break_abandoned_part_way_still_gives_the_screen_back()
    {
        CommercialBreak.Begin();
        CommercialBreak.EverySponsorShown();

        Assert.True(CommercialBreak.Running);
        Assert.True(CommercialBreak.ShownEverything);

        // The next race was called: the sequence returns and its finally
        // runs, which is the only thing this has to be true of.
        CommercialBreak.End();

        Assert.False(CommercialBreak.Running);
        Assert.False(CommercialBreak.ShownEverything);
    }

    [Fact]
    public void Ending_a_break_twice_counts_it_down_once()
    {
        // The sequence ends its break at the natural end AND from a finally
        // - the belt-and-braces shape EndShowAsync uses - so this has to be
        // safe twice. Counting below zero would leave the NEXT break
        // ending before it began.
        CommercialBreak.Begin();
        CommercialBreak.End();
        CommercialBreak.End();
        CommercialBreak.End();

        Assert.False(CommercialBreak.Running);

        CommercialBreak.Begin();

        Assert.True(CommercialBreak.Running);

        CommercialBreak.End();

        Assert.False(CommercialBreak.Running);
    }

    [Fact]
    public void Two_things_holding_a_break_is_still_one_break()
    {
        // A count rather than a flag, for the reason AdBreak's is: nothing
        // today runs two after-race sequences at once, but one beginning
        // while another was winding up must not have the first one's ending
        // put the standings back under it.
        CommercialBreak.Begin();
        CommercialBreak.Begin();
        CommercialBreak.End();

        Assert.True(CommercialBreak.Running);

        CommercialBreak.End();

        Assert.False(CommercialBreak.Running);
    }

    [Fact]
    public void One_breaks_spent_sponsors_are_not_the_next_breaks()
    {
        CommercialBreak.Begin();
        CommercialBreak.EverySponsorShown();
        CommercialBreak.End();

        // The next race's break has shown nothing yet, and must not end at
        // its floor on the strength of the last one's rotation.
        CommercialBreak.Begin();

        Assert.False(CommercialBreak.ShownEverything);

        CommercialBreak.End();
    }

    [Fact]
    public void Nothing_is_spent_while_no_break_is_on()
    {
        // Said with no break running, which is what a Big Screen on a
        // practice night does every eight seconds.
        CommercialBreak.EverySponsorShown();

        Assert.False(CommercialBreak.ShownEverything);
        Assert.False(CommercialBreak.Running);
    }

    // ------------------------------------------------------------------
    //  What the band says
    // ------------------------------------------------------------------

    [Fact]
    public void The_band_reads_the_break_in_the_crowds_words()
    {
        Assert.Equal("COMMERCIAL BREAK", OnAirTreatment.Caption(CommercialBreak.OnAirCaption));

        // Never the "a show with nothing to call itself" plate: the break
        // has a name, and the one moment the room is reading this band is
        // the moment it has to say something true.
        Assert.NotEqual(OnAirTreatment.Unnamed, OnAirTreatment.Caption(CommercialBreak.OnAirCaption));
    }

    [Fact]
    public void The_break_is_not_a_commentary_segment_and_is_not_in_the_segments_table()
    {
        // DELIBERATELY NOT a SegmentKind. That enum is "every kind of thing
        // the brain can be asked to write"; an advert is written by a
        // sponsor, costs no relay lines, sits on no tier and is never
        // offered the show's music. A member added to it would also need a
        // SegmentSpec - CommentaryBudgetTests holds AllSpecs to the length
        // of the enum - which would put a tick box for the adverts in the
        // middle of the Commentary page's list of things the model says.
        //
        // So ShowBlock.CaptionFor stays the ONE table of segment captions,
        // and the break's caption is simply not in it. Checked rather than
        // asserted in a comment, because the tempting fix the day somebody
        // wants this on the band again is to add a kind.
        foreach (var kind in Enum.GetValues<SegmentKind>())
        {
            Assert.NotEqual(CommercialBreak.OnAirCaption, ShowBlock.CaptionFor(kind));
        }
    }

    // ------------------------------------------------------------------
    //  No screen, no break
    // ------------------------------------------------------------------

    [Fact]
    public void A_club_that_has_never_touched_the_commentary_page_gets_no_break()
    {
        // Off by default, like every other part of the broadcast: a club
        // that never opens the page hears the recap it always has.
        Assert.False(new AppSettings().RecapAdBreak);
    }

    [Fact]
    public void A_break_is_not_worth_having_without_a_big_screen_showing_the_sponsors()
    {
        // The gate that keeps this additive. A break with nothing in it is
        // silence added in front of the recap, which is a regression and not
        // a feature.
        Assert.False(CommercialBreak.SponsorScreenUp);

        CommercialBreak.SponsorScreenReady();

        Assert.True(CommercialBreak.SponsorScreenUp);
    }

    [Fact]
    public void A_big_screen_that_has_stopped_saying_so_stops_counting()
    {
        // A heartbeat and not a count put up when the window opens and taken
        // down when it closes. A count can be got wrong in the direction
        // that costs something - a break held open against a screen that is
        // not there, delaying every recap of the night by the floor with
        // nothing on the wall to show for it - and a report that stops
        // arriving cannot. This is the half of that claim worth proving.
        CommercialBreak.SponsorScreenReady();

        Assert.True(CommercialBreak.SponsorScreenUp);

        // Past the window the report is good for. A real wait, because what
        // is being tested is a wall clock going by with nobody reporting.
        Thread.Sleep(3500);

        Assert.False(CommercialBreak.SponsorScreenUp);
    }

    // ------------------------------------------------------------------
    //  A real break past a real Big Screen
    // ------------------------------------------------------------------

    [Fact]
    public void The_sponsors_take_the_crowds_screen_for_the_break_and_give_it_back()
    {
        var seen = StaThread.Run(() =>
        {
            var sponsors = SponsorStore.Load();
            var added = sponsors.AddSlide(WritePng("banner.png"), out var slideError);

            var announcer = new AnnouncementService(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load());

            using var form = new BigScreenForm(new RaceControlSession(), announcer, ProfileStore.Load(), null, sponsors);

            // Switched OFF to begin with, which is most clubs: the window
            // must not offer to carry a break it has nothing to put in.
            sponsors.Current.Enabled = false;

            form.Show();
            Pump();

            var reportedWhileOff = CommercialBreak.SponsorScreenUp;

            sponsors.Current.Enabled = true;
            Pump();

            var reportedWhileOn = CommercialBreak.SponsorScreenUp;
            var boardBefore = form.BoardUpForTests;
            var bandBefore = form.OnAirUpForTests;

            // Begun and ended from a thread of its own, because that is
            // where they come from on a race night: the after-race sequence
            // is a background task. It is also what makes this a deadlock
            // test - the window asks CommercialBreak whether a break is on
            // twice a second, so anything in here that waited for the UI
            // thread from inside that lock would hang rather than fail.
            Task.Run(CommercialBreak.Begin).Wait();
            Pump();

            var hasScreen = form.BreakHasTheScreenForTests;
            var sponsorUp = form.SponsorUpForTests;
            var boardDuring = form.BoardUpForTests;
            var band = form.OnAirUpForTests;
            var says = form.OnAirSaysForTests;

            Task.Run(CommercialBreak.End).Wait();
            Pump();

            var hasScreenAfter = form.BreakHasTheScreenForTests;
            var sponsorAfter = form.SponsorUpForTests;
            var boardAfter = form.BoardUpForTests;
            var bandAfter = form.OnAirUpForTests;

            form.Close();

            return new Watched(slideError, added, reportedWhileOff, reportedWhileOn, boardBefore, bandBefore,
                hasScreen, sponsorUp, boardDuring, band, says,
                hasScreenAfter, sponsorAfter, boardAfter, bandAfter);
        }, TimeSpan.FromSeconds(90), () => "driving a commercial break past the Big Screen");

        Assert.Null(seen.SlideError);
        Assert.NotNull(seen.Added);

        // The slides switched off: nothing offered, so Race Control has no
        // break and the recap goes out exactly as it does today.
        Assert.False(seen.ReportedWhileOff);
        Assert.True(seen.ReportedWhileOn);

        // Before: the standings, and no band.
        Assert.True(seen.BoardBefore);
        Assert.False(seen.BandBefore);

        // During: the sponsors have the screen and the band says what is on.
        Assert.True(seen.HasScreen);
        Assert.True(seen.SponsorUp);
        Assert.False(seen.BoardDuring);
        Assert.True(seen.Band);
        Assert.Equal("COMMERCIAL BREAK", seen.Says);

        // After: the standings are back and the band is gone - which is the
        // one promise that costs a club their evening if it is broken.
        Assert.False(seen.HasScreenAfter);
        Assert.False(seen.SponsorAfter);
        Assert.True(seen.BoardAfter);
        Assert.False(seen.BandAfter);
    }

    /// <summary>
    /// What the live test above saw, in the order it saw it. A record rather
    /// than the tuple ShowOnScreenTests returns, because a break has three
    /// phases to look at rather than two and fifteen unnamed slots is not
    /// something a failure can be read out of.
    /// </summary>
    private sealed record Watched(
        string? SlideError,
        string? Added,
        bool ReportedWhileOff,
        bool ReportedWhileOn,
        bool BoardBefore,
        bool BandBefore,
        bool HasScreen,
        bool SponsorUp,
        bool BoardDuring,
        bool Band,
        string Says,
        bool HasScreenAfter,
        bool SponsorAfter,
        bool BoardAfter,
        bool BandAfter);

    /// <summary>
    /// Lets the window catch up.
    ///
    /// Everything the break does on screen is done by the window's own
    /// half-second tick (BigScreenForm.UpdateCommercialBreak), and a test
    /// thread has no message loop of its own - so the loop is run by hand
    /// (StaThread.Settle), kept going for most of a second so a tick gets
    /// its turn. The same helper ShowOnScreenTests keeps, and kept here
    /// rather than shared for the same reason: a change made for one of
    /// them must not break the other.
    /// </summary>
    private static void Pump()
    {
        for (var i = 0; i < 60; i++)
        {
            StaThread.Settle();
            Thread.Sleep(20);
        }
    }

    /// <summary>A one-pixel PNG for the rotation to have something real to put up.</summary>
    private string WritePng(string name)
    {
        Directory.CreateDirectory(_data.Folder);

        var path = Path.Combine(_data.Folder, name);

        using var bitmap = new Bitmap(1, 1);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);

        return path;
    }

    // ------------------------------------------------------------------
    //  The sequence, read from its source
    // ------------------------------------------------------------------
    //
    //  Read rather than driven, the way RecapBroadcastTests reads where the
    //  recap's show is opened and HonoursSegmentTests reads where the
    //  honours go: what is being held is an ORDER of lines in one method,
    //  which is exactly what reading the file can see and what a
    //  behavioural test of this would need a model, a race and a sound
    //  device to even start.

    [Fact]
    public void The_break_is_held_between_the_teaser_and_the_recap()
    {
        var source = Source("RaceControlControl.AfterRace.cs");

        var teaser = source.IndexOf("var teaserQueued = await QueueUnlessNextRaceCalledAsync(", StringComparison.Ordinal);
        var held = source.IndexOf("await HoldTheCommercialBreakAsync(", StringComparison.Ordinal);
        var recap = source.IndexOf("var recap = await recapTask!;", StringComparison.Ordinal);

        Assert.True(teaser > 0, "The teaser is no longer queued the way this test reads it.");
        Assert.True(held > 0, "The recap sequence no longer holds a commercial break the way this test reads it.");
        Assert.True(recap > 0, "The recap sequence no longer awaits the recap the way this test reads it.");

        Assert.True(
            held > teaser,
            "The commercial break is held before the teaser has gone out, so the room gets the adverts with no line into "
            + "them. The teaser is the lead-in; the break follows it.");

        Assert.True(
            recap > held,
            "The commercial break is held after the recap has been awaited, so it fills no wait at all - which is the "
            + "whole of what it is for.");
    }

    [Fact]
    public void The_teaser_plays_whether_or_not_there_is_a_break()
    {
        // A decision of the club owner's, and correct broadcast grammar on
        // top of it: "we'll have the full story in a moment" is the line
        // into an advert break. It is also the one part of this sequence
        // that needs no model, which is why it already sits outside the
        // commentary guard - so a club with no sponsors, no Big Screen and
        // no reachable model still gets told to stand by.
        var source = Source("RaceControlControl.AfterRace.cs");

        var teaser = source.IndexOf("// 3. The teaser, straight after the finish call.", StringComparison.Ordinal);
        var guard = source.IndexOf("if (!commentaryAvailable)", teaser, StringComparison.Ordinal);

        Assert.True(teaser > 0, "The teaser's step is no longer labelled the way this test reads it.");
        Assert.True(guard > teaser, "The commentary guard no longer follows the teaser the way this test reads it.");

        Assert.DoesNotContain("CommercialBreak", source[teaser..guard], StringComparison.Ordinal);
    }

    [Fact]
    public void Whatever_starts_a_break_ends_it_from_a_finally()
    {
        // THE ONE INVARIANT THAT COSTS A CLUB THEIR EVENING. A break left
        // open keeps the standings off the crowd's wall for the rest of the
        // night, leaves the band reading COMMERCIAL BREAK over every heat,
        // and on a club with video sponsors has the rotation start another
        // advert every pass - so AdBreak quiets the club's music under each
        // one and the night ends up exactly where AdBreak's own notes were
        // written. See the note on CommercialBreak.
        var source = Source("RaceControlControl.AfterRace.cs");

        Assert.Equal(1, Occurrences(source, "CommercialBreak.Begin();"));
        Assert.Equal(1, Occurrences(source, "CommercialBreak.End();"));

        var begun = source.IndexOf("CommercialBreak.Begin();", StringComparison.Ordinal);
        var tried = source.IndexOf("try", begun, StringComparison.Ordinal);
        var ended = source.IndexOf("CommercialBreak.End();", begun, StringComparison.Ordinal);
        var guarantee = source.IndexOf("finally", begun, StringComparison.Ordinal);

        Assert.True(begun > 0, "Nothing in the recap sequence begins a commercial break any more.");

        // Nothing between the Begin and the try that could throw, which is
        // what makes the finally below cover every path out. The shape
        // BigScreenForm.ShowSponsorVideo uses for AdBreak.
        Assert.Equal(string.Empty, source[(begun + "CommercialBreak.Begin();".Length)..tried].Trim());

        Assert.True(
            ended > guarantee && guarantee > begun,
            "The commercial break is not ended from a finally, so a path out of the sequence - a return, the next race, a "
            + "failure nobody expected - leaves the sponsors on the crowd's screen for the rest of the evening.");
    }

    [Fact]
    public void The_break_stops_on_the_same_token_the_rest_of_the_sequence_stops_on()
    {
        // nextRace, which the recap, the honours, the interview and the
        // sign-off all already stop on - so one press of Stage abandons the
        // whole of what follows a flag rather than most of it.
        var source = Source("RaceControlControl.AfterRace.cs");

        Assert.Contains("await HoldTheCommercialBreakAsync(recapTask!, nextRace.Token);", source, StringComparison.Ordinal);

        var body = MethodBody(
            source,
            "private async Task HoldTheCommercialBreakAsync(Task recapReady, CancellationToken nextRace)",
            "private static string WhyTheBreakEnded(BreakEnding ending)");

        // The wait itself is on the token, so a race called to the grid is
        // acted on within one look rather than at the end of the break.
        Assert.Contains("await Task.Delay(CommercialBreak.Look, nextRace);", body, StringComparison.Ordinal);

        // And the decision is asked about it on every look, so a token
        // cancelled while the wait was not running is caught too.
        Assert.Contains("nextRace.IsCancellationRequested", body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_club_with_no_sponsor_screen_is_turned_away_before_a_break_is_ever_begun()
    {
        // Both gates in front of the Begin, so the additive promise is not
        // a matter of what the loop happens to do first.
        var source = Source("RaceControlControl.AfterRace.cs");

        var switchedOn = source.IndexOf("if (!_settings.RecapAdBreak)", StringComparison.Ordinal);
        var screenUp = source.IndexOf("if (!CommercialBreak.SponsorScreenUp)", StringComparison.Ordinal);
        var begun = source.IndexOf("CommercialBreak.Begin();", StringComparison.Ordinal);

        Assert.True(switchedOn > 0, "The break no longer reads the club's switch the way this test reads it.");
        Assert.True(screenUp > 0, "The break no longer asks whether a Big Screen is showing the sponsors.");

        Assert.True(switchedOn < begun, "The club's switch is read after a break has already begun.");
        Assert.True(screenUp < begun, "Whether there is a screen to show the sponsors on is asked after the break has begun.");
    }

    // ------------------------------------------------------------------
    //  The Big Screen, read from its source
    // ------------------------------------------------------------------

    [Fact]
    public void Whether_the_break_has_the_screen_is_asked_again_on_every_tick()
    {
        // The sponsor video stayed on screen over the start of a heat
        // because StopSponsorVideo was the only route off it and the roll
        // call went round that route. Furniture with exactly one route off
        // stays on the moment that route is missed, so the break has none:
        // twice a second the window asks whether one is on and makes the
        // screen agree. A break that ends, is abandoned for the next race,
        // or whose Race Control page has gone away all come to the same
        // place within half a second.
        var tick = MethodBody(
            Source("BigScreenForm.cs"), "private void RefreshCountdownOnly()", "private void UpdateRollCall()");

        Assert.Contains("UpdateCommercialBreak();", tick, StringComparison.Ordinal);
    }

    [Fact]
    public void The_rotation_is_held_rather_than_looped_when_the_sponsors_run_out()
    {
        var slot = MethodBody(
            Source("BigScreenForm.cs"), "private void SponsorSlotEnded()", "private void UpdateCommercialBreak()");

        // Been round every one of them: the sequence is told, and the
        // rotation is NOT started again.
        Assert.Contains("CommercialBreak.EverySponsorShown();", slot, StringComparison.Ordinal);
        Assert.Contains("_breakSlidesShown >= _sponsors.SlideFileNames.Count", slot, StringComparison.Ordinal);

        // And with no break on, the standings come back exactly as they
        // always have - this method is the rotation's own ending too.
        Assert.Contains("ShowLeaderboardAgain();", slot, StringComparison.Ordinal);
    }

    [Fact]
    public void A_video_that_plays_itself_out_goes_through_the_same_ending_as_the_timer()
    {
        // Two copies of the rule about what takes this screen is precisely
        // how the ad's sound came to play under a roll call: the picture was
        // put away by one route and the video left running, so neither the
        // timer nor the video's own Finished could reach StopSponsorVideo -
        // the only caller of AdBreak.End in the app - and the club's music
        // stayed muted for a whole heat.
        var source = Source("BigScreenForm.cs");

        var finished = MethodBody(source, "surface.Finished += () => SafeInvoke(() =>", "return surface;");

        Assert.Contains("SponsorSlotEnded();", finished, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowLeaderboardAgain();", finished, StringComparison.Ordinal);

        var tick = MethodBody(source, "private void SponsorTimer_Tick(object? sender, EventArgs e)", "private void ShowNextSponsorSlide()");

        Assert.Contains("SponsorSlotEnded();", tick, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    //  Reading the source
    // ------------------------------------------------------------------
    //
    //  The same pair ShowOnScreenTests and BugHunt5RaceNightTests each
    //  keep, kept again here rather than shared: each test class that reads
    //  source carries its own, so none of them can be broken by a change
    //  made for another.

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

    private static int Occurrences(string source, string text)
    {
        var found = 0;

        for (var at = source.IndexOf(text, StringComparison.Ordinal); at >= 0;
             at = source.IndexOf(text, at + text.Length, StringComparison.Ordinal))
        {
            found++;
        }

        return found;
    }
}
