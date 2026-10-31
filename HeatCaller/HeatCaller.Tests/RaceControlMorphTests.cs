namespace HeatCaller.Tests;

/// <summary>
/// Race Control's morphing bar, hold to act and Now Playing (25 Oct 2026).
///
/// The bar: which buttons are on the flag row at each moment of a race -
/// worked out by RaceBar from the race's own state, so it is tested here
/// without a window. The hold: HoldGesture's timing - done once, after a
/// second, and nothing when let go before. Now Playing: which of several
/// sounds going out at once is named, and that a sound stops being named
/// when it ends. And the row itself, built and measured the way a page is:
/// it fits at a wide and a narrow width, and it is no shorter with two
/// buttons on it than with five, so the page never jumps as the race moves on.
/// </summary>
public sealed class RaceControlMorphTests
{
    private static volatile string _now = "starting";

    // --- The bar ----------------------------------------------------------

    [Fact]
    public void The_row_keeps_room_for_the_most_buttons_any_moment_shows()
    {
        // FlagRow sizes itself for RaceBar.MostShown, so the row is one
        // height through the night. If a phase ever shows more, the row would
        // wrap and jump again - this says so before a race night does.
        var most = 0;

        foreach (var state in Enum.GetValues<RaceState>())
        foreach (var flag in Enum.GetValues<FlagState>())
        foreach (var practice in new[] { false, true })
        foreach (var warmingUp in new[] { false, true })
        foreach (var nextWaiting in new[] { false, true })
        foreach (var talkButtons in new[] { false, true })
        {
            var shown = RaceBar.ButtonsFor(state, flag, practice, warmingUp, nextWaiting, talkButtons);
            most = Math.Max(most, System.Numerics.BitOperations.PopCount((uint)shown));
        }

        Assert.Equal(RaceBar.MostShown, most);
    }

    [Fact]
    public void Between_races_the_row_is_for_starting_the_next_one()
    {
        // 26 Oct 2026: "from cold start the following buttons show, on air,
        // stage, countdown, green and reset" - On Air's button then went
        // again the same evening ("remove the on air flag like button").
        var shown = RaceBar.ButtonsFor(RaceState.Idle, FlagState.None, practice: false, warmingUp: false);

        Assert.Equal(
            RaceBarButtons.Stage | RaceBarButtons.Countdown | RaceBarButtons.Green | RaceBarButtons.Reset,
            shown);
    }

    [Fact]
    public void After_the_flag_the_row_is_skip_hold_talk_call_up_edit_result_and_reset()
    {
        // 31 Oct 2026: "you can remove the next heat button as the button
        // above it and to the right does that now", and Results with it -
        // Edit Result goes to the same race in Race History.
        var shown = RaceBar.ButtonsFor(RaceState.Finished, FlagState.Checkered, practice: false, warmingUp: false);

        Assert.Equal(
            RaceBarButtons.Skip | RaceBarButtons.HoldTalk | RaceBarButtons.CallUp | RaceBarButtons.EditResult | RaceBarButtons.Reset,
            shown);
        Assert.Equal(RaceBar.AfterTheFlag, shown);
        Assert.Equal(RacePhase.Finished, RaceBar.PhaseFor(RaceState.Finished, FlagState.Checkered, false, false));
    }

    [Fact]
    public void After_the_flag_the_talk_buttons_stay_whether_or_not_anything_is_next()
    {
        // Call Up greys itself when there is nothing next; the row does not move.
        Assert.Equal(RaceBar.AfterTheFlag, RaceBar.ButtonsFor(RaceState.Finished, FlagState.Checkered, false, false, nextWaiting: false));
    }

    [Fact]
    public void With_the_talk_buttons_switched_off_the_row_after_the_flag_is_edit_result_and_reset()
    {
        var shown = RaceBar.ButtonsFor(RaceState.Finished, FlagState.Checkered, false, false, nextWaiting: true, talkButtons: false);

        Assert.Equal(RaceBarButtons.EditResult | RaceBarButtons.Reset, shown);
    }

    [Fact]
    public void With_the_talk_buttons_off_and_nothing_next_Stage_is_on_the_row()
    {
        var shown = RaceBar.ButtonsFor(RaceState.Finished, FlagState.Checkered, false, false, nextWaiting: false, talkButtons: false);

        Assert.True(shown.HasFlag(RaceBarButtons.Stage));
        Assert.True(shown.HasFlag(RaceBarButtons.EditResult));
        Assert.False(shown.HasFlag(RaceBarButtons.Skip));
    }

    [Fact]
    public void The_after_the_flag_buttons_are_only_there_after_the_flag()
    {
        foreach (var state in new[] { RaceState.Idle, RaceState.Staging, RaceState.Countdown, RaceState.Racing })
        {
            var shown = RaceBar.ButtonsFor(state, FlagState.Green, false, false);

            Assert.False(shown.HasFlag(RaceBarButtons.Skip), $"Skip at {state}");
            Assert.False(shown.HasFlag(RaceBarButtons.HoldTalk), $"Hold Talk at {state}");
            Assert.False(shown.HasFlag(RaceBarButtons.CallUp), $"Call Up at {state}");
            Assert.False(shown.HasFlag(RaceBarButtons.EditResult), $"Edit Result at {state}");
        }

        // A warm-up between races is its own row, finished race or not.
        Assert.False(RaceBar.ButtonsFor(RaceState.Finished, FlagState.None, false, warmingUp: true).HasFlag(RaceBarButtons.Skip));
    }

    [Fact]
    public void Next_Heat_and_Results_have_left_the_row()
    {
        var layout = Source("RaceControlControl.Layout.cs");

        Assert.Contains("_btnStartNext.Click += (_, _) => StartNext();", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("_btnBarNext", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("_btnBarResults", layout, StringComparison.Ordinal);
        Assert.Contains("_btnBarSkip, _btnBarHoldTalk, _btnBarCallUp, _btnBarEditResult, _btnReset", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void The_resting_hint_after_the_flag_says_how_Skip_works()
    {
        var hint = RaceBar.RestingHint(RaceBar.AfterTheFlag, practice: false);

        Assert.Contains("Reset: press and hold for a second.", hint, StringComparison.Ordinal);
        Assert.Contains("Skip: click for the next part of the show, hold to end it.", hint, StringComparison.Ordinal);
    }

    [Fact]
    public void While_racing_the_row_is_the_flags_and_no_start_buttons()
    {
        // 26 Oct 2026: "once the race is green the buttons change to green,
        // yellow, red, checkered, reset" - the same five, caution or not.
        foreach (var flag in new[] { FlagState.Green, FlagState.Yellow, FlagState.Red, FlagState.None })
        {
            var shown = RaceBar.ButtonsFor(RaceState.Racing, flag, practice: false, warmingUp: false);

            Assert.Equal(
                RaceBarButtons.Green | RaceBarButtons.Yellow | RaceBarButtons.Red | RaceBarButtons.Checkered | RaceBarButtons.Reset,
                shown);
            Assert.False(shown.HasFlag(RaceBarButtons.Stage));
            Assert.False(shown.HasFlag(RaceBarButtons.Countdown));
        }
    }

    [Theory]
    [InlineData(FlagState.Yellow)]
    [InlineData(FlagState.Red)]
    public void Under_a_caution_Green_comes_back_to_restart_the_race(FlagState flag)
    {
        var shown = RaceBar.ButtonsFor(RaceState.Racing, flag, practice: false, warmingUp: false);

        Assert.True(shown.HasFlag(RaceBarButtons.Green));
        Assert.True(shown.HasFlag(RaceBarButtons.Yellow));
        Assert.True(shown.HasFlag(RaceBarButtons.Red));
        Assert.True(shown.HasFlag(RaceBarButtons.Checkered));
    }

    [Theory]
    [InlineData(RaceState.Idle, false)]
    [InlineData(RaceState.Idle, true)]
    [InlineData(RaceState.Staging, false)]
    [InlineData(RaceState.Staging, true)]
    [InlineData(RaceState.Countdown, false)]
    public void Until_the_green_the_row_does_not_move(RaceState state, bool warmingUp)
    {
        // 26 Oct 2026: "they all remain up until green gets pressed or is
        // moved on to naturally" - through On Air, a warm-up, staging and
        // the countdown the row is the cold start's five.
        Assert.Equal(RaceBar.BeforeTheGreen, RaceBar.ButtonsFor(state, FlagState.None, practice: false, warmingUp: warmingUp));
        Assert.Equal(
            RaceBarButtons.Stage | RaceBarButtons.Countdown | RaceBarButtons.Green | RaceBarButtons.Reset,
            RaceBar.BeforeTheGreen);
    }

    [Fact]
    public void The_countdown_stays_on_the_row_until_the_green()
    {
        // It used to leave the row once the countdown was running. Since 26
        // Oct 2026 it stays, with the rest of the set before the green: "they
        // all remain up until green gets pressed or is moved on to naturally".
        Assert.True(RaceBar.ButtonsFor(RaceState.Staging, FlagState.None, false, false).HasFlag(RaceBarButtons.Countdown));
        Assert.True(RaceBar.ButtonsFor(RaceState.Countdown, FlagState.None, false, false).HasFlag(RaceBarButtons.Countdown));
        Assert.False(RaceBar.ButtonsFor(RaceState.Racing, FlagState.Green, false, false).HasFlag(RaceBarButtons.Countdown));
    }

    [Fact]
    public void On_Air_has_no_button_and_shows_its_writing_and_voicing_on_the_strip()
    {
        // 25 Oct 2026: "use the press and hold animation to denote the writing
        // and voicing delay instead of the on air button which you can remove".
        var layout = Source("RaceControlControl.Layout.cs");
        var onAir = Source("RaceControlControl.OnAir.cs");

        // Its button came back for an hour on 26 Oct 2026 and went again:
        // "remove the on air flag like button".
        Assert.DoesNotContain("_btnBarOnAir", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("_btnOnAir", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("_btnOnAir", onAir, StringComparison.Ordinal);

        // The writing and voicing, and the show itself, on the strip under the row.
        Assert.Contains("_holdStrip.ShowWork(StepWords(_onAirStep), fill,", onAir, StringComparison.Ordinal);
        // The show itself since 26 Oct 2026: the bar stays up while anything
        // is going out and names the segment on air (UpdateOnAirBar).
        Assert.Contains("_onAirLive = live;", onAir, StringComparison.Ordinal);
        Assert.Contains("_holdStrip.ShowWork(words, null, _onAirLive ? OnAirSkipNote : string.Empty, OnAirLiveCyan);",
            Source("RaceControlControl.Morph.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_strip_shows_work_under_way_and_goes_back_to_rest_after()
    {
        var problems = StaThread.Run(() =>
        {
            var found = new List<string>();
            using var strip = new HoldStrip();

            strip.SetResting("Reset: press and hold for a second. Everything else is one click.");
            strip.ShowWork("On Air: voicing the opening…", 0.6f, "about 8s · press O to skip to Stage", Color.Red);

            if (strip.WorkText != "On Air: voicing the opening…")
            {
                found.Add($"while working it said {strip.WorkText ?? "nothing"}");
            }

            strip.EndWork();

            if (strip.WorkText is not null)
            {
                found.Add("the work stayed after it ended");
            }

            if (!strip.RestingText.StartsWith("Reset:", StringComparison.Ordinal))
            {
                found.Add("its resting line was lost");
            }

            return found;
        }, TimeSpan.FromSeconds(30), () => "the strip");

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Practice_has_no_flags_on_the_row()
    {
        Assert.Equal(RaceBarButtons.None,
            RaceBar.ButtonsFor(RaceState.Racing, FlagState.Green, practice: true, warmingUp: false));

        Assert.Equal(RacePhase.Practice, RaceBar.PhaseFor(RaceState.Racing, FlagState.Green, practice: true, warmingUp: false));
        Assert.Contains("Practice page", RaceBar.RestingHint(RaceBarButtons.None, practice: true), StringComparison.Ordinal);
    }

    [Fact]
    public void A_warm_up_offers_the_buttons_that_end_it()
    {
        var shown = RaceBar.ButtonsFor(RaceState.Idle, FlagState.None, practice: false, warmingUp: true);

        Assert.True(shown.HasFlag(RaceBarButtons.Stage));
        Assert.True(shown.HasFlag(RaceBarButtons.Green));
        Assert.False(shown.HasFlag(RaceBarButtons.Checkered));
        Assert.Equal(RacePhase.WarmUp, RaceBar.PhaseFor(RaceState.Idle, FlagState.None, false, warmingUp: true));
    }

    [Fact]
    public void Reset_is_on_the_row_at_every_point_but_practice()
    {
        foreach (var state in Enum.GetValues<RaceState>())
        {
            foreach (var flag in Enum.GetValues<FlagState>())
            {
                Assert.True(RaceBar.ButtonsFor(state, flag, false, false).HasFlag(RaceBarButtons.Reset),
                    $"Reset was off the row at {state} under {flag}.");
            }
        }
    }

    [Fact]
    public void Never_more_than_MostShown_buttons_are_on_the_row()
    {
        foreach (var state in Enum.GetValues<RaceState>())
        {
            foreach (var flag in Enum.GetValues<FlagState>())
            {
                foreach (var warm in new[] { false, true })
                {
                    foreach (var next in new[] { false, true })
                    {
                        var count = Count(RaceBar.ButtonsFor(state, flag, false, warm, next));
                        Assert.True(count <= RaceBar.MostShown, $"{count} buttons at {state} under {flag}.");
                        Assert.True(count >= 1, $"No buttons at {state} under {flag}.");
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(RaceState.Idle, FlagState.None, RacePhase.Ready)]
    [InlineData(RaceState.Staging, FlagState.None, RacePhase.Staging)]
    [InlineData(RaceState.Countdown, FlagState.None, RacePhase.Countdown)]
    [InlineData(RaceState.Racing, FlagState.Green, RacePhase.Racing)]
    [InlineData(RaceState.Racing, FlagState.Yellow, RacePhase.Caution)]
    [InlineData(RaceState.Racing, FlagState.Red, RacePhase.Stopped)]
    [InlineData(RaceState.Finished, FlagState.Checkered, RacePhase.Finished)]
    public void The_word_at_the_left_says_where_the_race_is(RaceState state, FlagState flag, RacePhase phase)
    {
        Assert.Equal(phase, RaceBar.PhaseFor(state, flag, practice: false, warmingUp: false));
        Assert.False(string.IsNullOrWhiteSpace(RaceBar.Word(phase)));
    }

    [Fact]
    public void The_strip_at_rest_names_only_the_held_buttons_that_are_on_the_row()
    {
        var racing = RaceBar.RestingHint(RaceBarButtons.Yellow | RaceBarButtons.Red | RaceBarButtons.Checkered | RaceBarButtons.Reset, false);

        Assert.StartsWith("Red, Checkered and Reset: press and hold", racing, StringComparison.Ordinal);

        var between = RaceBar.RestingHint(RaceBarButtons.Stage | RaceBarButtons.Reset, false);

        Assert.StartsWith("Reset: press and hold", between, StringComparison.Ordinal);
        Assert.Equal(string.Empty, RaceBar.RestingHint(RaceBarButtons.Stage | RaceBarButtons.Green, false));
    }

    // --- The hold ---------------------------------------------------------

    [Fact]
    public void A_hold_does_its_job_once_it_has_been_held_for_long_enough_and_only_once()
    {
        var hold = new HoldGesture(1000);

        hold.Press(5_000);

        Assert.False(hold.TryComplete(5_500));
        Assert.Equal(0.5, (double)hold.ProgressAt(5_500)!.Value, 3);
        Assert.False(hold.TryComplete(5_999));
        Assert.True(hold.TryComplete(6_000));
        Assert.False(hold.TryComplete(6_100));
        Assert.False(hold.Holding);
        Assert.Null(hold.ProgressAt(6_100));

        // The let-go after a hold that has done its job calls nothing off.
        Assert.False(hold.Release());
    }

    [Fact]
    public void Let_go_too_soon_and_nothing_happens()
    {
        var hold = new HoldGesture(1000);

        hold.Press(0);

        Assert.True(hold.Release());
        Assert.False(hold.TryComplete(5_000));
        Assert.Null(hold.ProgressAt(5_000));
    }

    [Fact]
    public void A_fresh_press_starts_the_second_again()
    {
        var hold = new HoldGesture(1000);

        hold.Press(0);
        hold.Release();
        hold.Press(10_000);

        Assert.False(hold.TryComplete(10_900));
        Assert.True(hold.TryComplete(11_000));

        // And after one that completed, the next press is a new hold too.
        hold.Release();
        hold.Press(20_000);

        Assert.Equal(0.0, (double)hold.ProgressAt(20_000)!.Value, 3);
        Assert.True(hold.TryComplete(21_000));
    }

    [Fact]
    public void Nothing_held_is_no_progress_and_nothing_to_call_off()
    {
        var hold = new HoldGesture(1000);

        Assert.Null(hold.ProgressAt(0));
        Assert.False(hold.TryComplete(10_000));
        Assert.False(hold.Release());
        Assert.Equal(1.0, HoldToAct.HoldFor.TotalSeconds, 3);
    }

    [Fact]
    public void Red_Checkered_and_Reset_are_held_and_not_clicked()
    {
        var layout = Source("RaceControlControl.Layout.cs");

        Assert.Contains("HoldToUse(_btnRed, ", layout, StringComparison.Ordinal);
        Assert.Contains("HoldToUse(_btnCheckered, ", layout, StringComparison.Ordinal);
        Assert.Contains("HoldToUse(_btnReset, ", layout, StringComparison.Ordinal);

        // A click on them would go straight through, hold or no hold.
        Assert.DoesNotContain("_btnRed.Click +=", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("_btnCheckered.Click +=", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("_btnReset.Click +=", layout, StringComparison.Ordinal);

        // Yellow is still one click, as asked.
        Assert.Contains("_btnYellow.Click += (_, _) => _actYellow();", layout, StringComparison.Ordinal);
    }

    // --- Now Playing ------------------------------------------------------

    [Fact]
    public void A_piece_is_named_over_a_sponsor_video_over_a_bed_over_the_music_over_the_crowd()
    {
        var crowd = new NowPlayingEntry(NowPlayingKind.Crowd, "Crowd ambience", 0);
        var music = new NowPlayingEntry(NowPlayingKind.Music, "Music · Track", 0);
        var bed = new NowPlayingEntry(NowPlayingKind.Bed, "Opening bed · Drive", 5);
        var video = new NowPlayingEntry(NowPlayingKind.SponsorVideo, "Sponsor video · Ad", 6);
        var piece = new NowPlayingEntry(NowPlayingKind.Piece, "Top of the race · ID", 2);

        Assert.Equal(piece, NowPlaying.Pick(new[] { crowd, music, bed, video, piece }));
        Assert.Equal(video, NowPlaying.Pick(new[] { crowd, music, bed, video }));
        Assert.Equal(bed, NowPlaying.Pick(new[] { crowd, bed, music }));
        Assert.Equal(music, NowPlaying.Pick(new[] { music, crowd }));
        Assert.Equal(crowd, NowPlaying.Pick(new[] { crowd }));
    }

    [Fact]
    public void Of_two_of_a_kind_the_newest_is_named_and_the_choice_never_wavers()
    {
        var older = new NowPlayingEntry(NowPlayingKind.Piece, "Sweeper", 3);
        var newer = new NowPlayingEntry(NowPlayingKind.Piece, "Bumper", 9);

        Assert.Equal(newer, NowPlaying.Pick(new[] { older, newer }));
        Assert.Equal(newer, NowPlaying.Pick(new[] { newer, older }));

        var a = new NowPlayingEntry(NowPlayingKind.Music, "A", 0);
        var b = new NowPlayingEntry(NowPlayingKind.Music, "B", 0);

        Assert.Equal(a, NowPlaying.Pick(new[] { a, b }));
        Assert.Equal(a, NowPlaying.Pick(new[] { b, a }));
    }

    [Fact]
    public void Nothing_playing_is_nothing_named()
    {
        Assert.Null(NowPlaying.Pick(Array.Empty<NowPlayingEntry>()));
        Assert.Null(NowPlaying.Pick(new[] { new NowPlayingEntry(NowPlayingKind.Piece, "  ", 1) }));
        Assert.Equal(string.Empty, NowPlaying.Describe(null));
    }

    [Fact]
    public void A_sound_is_named_while_it_plays_and_not_after()
    {
        var text = "Test piece " + Guid.NewGuid().ToString("N")[..8];
        var id = NowPlaying.Started(NowPlayingKind.Piece, text);

        try
        {
            Assert.Contains(NowPlaying.Everything(), entry => entry.Text == text && entry.Kind == NowPlayingKind.Piece);
        }
        finally
        {
            NowPlaying.Ended(id);
        }

        Assert.DoesNotContain(NowPlaying.Everything(), entry => entry.Text == text);

        // Twice, and nothing at all, are both harmless.
        NowPlaying.Ended(id);
        NowPlaying.Ended(0);
    }

    [Fact]
    public void A_watched_sound_is_asked_and_one_that_throws_is_left_out()
    {
        var name = "test-" + Guid.NewGuid().ToString("N")[..8];
        var broken = name + "-broken";
        var on = true;

        NowPlaying.Watch(name, () => on ? new NowPlayingEntry(NowPlayingKind.Music, name, 0) : null);
        NowPlaying.Watch(broken, () => throw new InvalidOperationException("torn down"));

        try
        {
            Assert.Contains(NowPlaying.Everything(), entry => entry.Text == name);

            on = false;
            Assert.DoesNotContain(NowPlaying.Everything(), entry => entry.Text == name);

            // Watched again under the same name: replaced, not added.
            NowPlaying.Watch(name, () => new NowPlayingEntry(NowPlayingKind.Music, name, 0));
            Assert.Single(NowPlaying.Everything(), entry => entry.Text == name);
        }
        finally
        {
            NowPlaying.Unwatch(name);
            NowPlaying.Unwatch(broken);
        }

        Assert.DoesNotContain(NowPlaying.Everything(), entry => entry.Text == name);
    }

    [Fact]
    public void A_name_is_one_line_never_empty_and_never_runs_on()
    {
        Assert.Equal("Imaging", NowPlaying.Tidy(null, NowPlayingKind.Piece));
        Assert.Equal("Sponsor video", NowPlaying.Tidy("   ", NowPlayingKind.SponsorVideo));
        Assert.Equal("Two lines", NowPlaying.Tidy("Two\nlines", NowPlayingKind.Piece).Replace("  ", " "));

        var tidy = NowPlaying.Tidy(new string('x', 200), NowPlayingKind.Piece);

        Assert.Equal(NowPlaying.LongestText, tidy.Length);
        Assert.EndsWith("…", tidy, StringComparison.Ordinal);

        Assert.Equal("Top of the race · Classic ID", NowPlaying.Joined("Top of the race", "Classic ID"));
        Assert.Equal("Classic ID", NowPlaying.Joined(null, "Classic ID"));
        Assert.Equal("Top of the race", NowPlaying.Joined("Top of the race", " "));
    }

    [Fact]
    public void Every_player_that_goes_out_to_the_room_says_so()
    {
        // The pieces and beds, the recap show, the replay and a sponsor video
        // with sound each tell NowPlaying when they start and when they end.
        var imaging = Source("BroadcastImaging.cs");

        Assert.Contains("NowPlaying.Started(NowPlayingKind.Piece", imaging, StringComparison.Ordinal);
        Assert.Contains("NowPlaying.Started(NowPlayingKind.Bed", imaging, StringComparison.Ordinal);
        Assert.Equal(2, Occurrences(imaging, "NowPlaying.Ended(_playingId);"));

        Assert.Contains("NowPlaying.Started(", Source("ShowTreatment.cs"), StringComparison.Ordinal);
        Assert.Contains("NowPlaying.Started(NowPlayingKind.Piece", Source("RaceControlControl.Broadcast.cs"), StringComparison.Ordinal);
        Assert.Contains("NowPlaying.Started(NowPlayingKind.SponsorVideo", Source("SponsorVideo.cs"), StringComparison.Ordinal);

        var audio = Source("MainForm.Audio.cs");

        Assert.Contains("NowPlaying.Watch(\"crowd\"", audio, StringComparison.Ordinal);
        Assert.Contains("NowPlaying.Watch(\"music\"", audio, StringComparison.Ordinal);
    }

    [Fact]
    public void Reset_stops_everything_the_room_hears()
    {
        // 26 Oct 2026: "when using reset the voice should stop. everything
        // should stop" - and "music and crowd and sponsor spot should be
        // stopped with a reset as well".
        Assert.Contains("StopTheBroadcast(\"Race reset\");", Source("RaceControlControl.Layout.cs"), StringComparison.Ordinal);

        var broadcast = Source("RaceControlControl.Broadcast.cs");

        Assert.Contains("_announcer.SilenceNow(why);", broadcast, StringComparison.Ordinal);
        Assert.Contains("BroadcastImaging.StopAll();", broadcast, StringComparison.Ordinal);
        Assert.Contains("screen.StopSponsorSpot();", broadcast, StringComparison.Ordinal);
        Assert.Contains("EverythingStopped?.Invoke();", broadcast, StringComparison.Ordinal);

        // The music and the crowd live in MainForm, which answers the event.
        Assert.Contains("raceControl.EverythingStopped += () =>", Source("MainForm.Pages.cs"), StringComparison.Ordinal);

        var audio = Source("MainForm.Audio.cs");

        Assert.Contains("private void StopMusicAndCrowd(string why)", audio, StringComparison.Ordinal);
        Assert.Contains("_music.Stop();", audio, StringComparison.Ordinal);
        Assert.Contains("_ambience.Stop();", audio, StringComparison.Ordinal);
    }

    [Fact]
    public void The_On_Air_bar_names_the_recap_show_by_the_name_on_the_AI_Commentary_page()
    {
        // 26 Oct 2026: "instead of saying recap show use the name in the AI
        // commentary if configured".
        Assert.Equal("Friday Night Grid", RaceControlControl.RecapShowLabel("  Friday Night Grid "));
        Assert.Equal("the recap show", RaceControlControl.RecapShowLabel(""));
        Assert.Equal("the recap show", RaceControlControl.RecapShowLabel(null));
        Assert.Contains("ShowOnAir.Caption is { Length: > 0 } show ? show : RecapShowNamed;", Source("RaceControlControl.Morph.cs"), StringComparison.Ordinal);
    }

    // --- The row on a page ------------------------------------------------

    [Fact]
    public void The_row_fits_at_a_wide_and_a_narrow_width_and_holds_its_height_as_the_buttons_change()
    {
        var problems = StaThread.Run(
            () => new[] { 1100, 560 }.SelectMany(CheckRow).ToList(),
            TimeSpan.FromMinutes(2),
            () => _now);

        Assert.True(problems.Count == 0, "The flag row does not fit:\n" + string.Join("\n", problems));
    }

    private static List<string> CheckRow(int width)
    {
        _now = $"the flag row at {width}px";

        var problems = new List<string>();

        void Threw(object? sender, ThreadExceptionEventArgs e) =>
            problems.Add($"{_now}: {e.Exception.GetType().Name}: {e.Exception.Message}");

        Application.ThreadException += Threw;

        try
        {
            using var window = new QuietWindow();
            var page = new Panel { Bounds = new Rectangle(0, 0, width, 400) };

            window.Controls.Add(page);

            var stack = new PageStack();
            page.Controls.Add(stack);

            var buttons = new Button[]
            {
                new FlagButton { Text = "Stage" },
                new FlagButton { Text = "Countdown" },
                new FlagButton { Text = "Green" },
                new FlagButton { Text = "Yellow" },
                new FlagButton { Text = "Red" },
                new FlagButton { Chequered = true, Text = "Checkered" },
                new Button { Text = "Skip" },
                new Button { Text = "Hold Talk" },
                new Button { Text = "Call Up" },
                new Button { Text = "Edit Result" },
                new Button { Text = "Reset" },
            };

            var bar = new ButtonBar(tight: true, sameWidth: true).With(buttons);
            var strip = new HoldStrip();
            var row = new FlagRow(bar, "Race Control", strip);

            // As Race Control builds it: the row draws the card's title, and
            // holds the On Air bar under the buttons.
            var flags = stack.Add(new Section());
            flags.Add(row, gapBelow: 0);

            row.SetPhase("Ready", AppTheme.TextMuted);
            row.SetNowPlaying("Top of the race · Classic station ID with a long name");
            strip.SetResting(RaceBar.RestingHint(RaceBarButtons.Reset, false));

            AppTheme.ApplyRecursively(page);

            window.Show();
            StaThread.Settle();

            void Show(RaceBarButtons on)
            {
                var bits = new[]
                {
                    RaceBarButtons.Stage, RaceBarButtons.Countdown, RaceBarButtons.Green,
                    RaceBarButtons.Yellow, RaceBarButtons.Red, RaceBarButtons.Checkered,
                    RaceBarButtons.Skip, RaceBarButtons.HoldTalk, RaceBarButtons.CallUp, RaceBarButtons.EditResult, RaceBarButtons.Reset,
                };

                for (var i = 0; i < buttons.Length; i++)
                {
                    buttons[i].Visible = on.HasFlag(bits[i]);
                }

                row.Rearrange();
                StaThread.Settle();
            }

            var heights = new List<(string When, int Row, int Strip)>();

            // Every phase in the height check, staging (the most buttons)
            // included: the row keeps room for RaceBar.MostShown whatever is
            // showing, so it is one height at any width.
            foreach (var (when, shown, sameHeight) in new[]
                     {
                         ("between races", RaceBar.ButtonsFor(RaceState.Idle, FlagState.None, false, false), true),
                         ("staging", RaceBar.ButtonsFor(RaceState.Staging, FlagState.None, false, false), true),
                         ("racing", RaceBar.ButtonsFor(RaceState.Racing, FlagState.Green, false, false), true),
                         ("under a caution", RaceBar.ButtonsFor(RaceState.Racing, FlagState.Yellow, false, false), true),
                         ("after the flag", RaceBar.ButtonsFor(RaceState.Finished, FlagState.Checkered, false, false), true),
                         ("practice", RaceBar.ButtonsFor(RaceState.Racing, FlagState.Green, true, false), true),
                     })
            {
                _now = $"the flag row at {width}px, {when}";

                Show(shown);

                foreach (var finding in LayoutAudit.Check(page, "Race Control row", StaThread.Settle))
                {
                    problems.Add($"{_now}: [{finding.Kind}] {finding.Detail}");
                }

                if (bar.Bottom > row.ClientSize.Height + 1 || bar.Right > row.ClientSize.Width + 1)
                {
                    problems.Add($"{_now}: the buttons at {bar.Bounds} run outside the row ({row.ClientSize})");
                }

                // The On Air bar ends short of the meters, and the buttons
                // showing are centred over it (26 Oct 2026).
                if (row.Meters.Visible && strip.Right > row.Meters.Left)
                {
                    problems.Add($"{_now}: the On Air bar at {strip.Bounds} runs into the meters at {row.Meters.Bounds}");
                }

                if (strip.Parent != row || strip.Top < bar.Bottom)
                {
                    problems.Add($"{_now}: the On Air bar at {strip.Bounds} is not under the buttons at {bar.Bounds}");
                }

                var showing = bar.Controls.OfType<Control>().Where(c => c.Visible).ToList();

                if (showing.Count > 0 && showing.All(c => c.Top == showing[0].Top))
                {
                    var left = bar.Left + showing.Min(c => c.Left);
                    var right = bar.Left + showing.Max(c => c.Right);
                    var off = Math.Abs((left - strip.Left) - (strip.Right - right));

                    if (off > 2)
                    {
                        problems.Add($"{_now}: the buttons ({left}-{right}) are not centred over the On Air bar ({strip.Left}-{strip.Right})");
                    }
                }

                if (sameHeight)
                {
                    heights.Add((when, row.Height, strip.Height));
                }
            }

            // The same height whatever is on it, wide or narrow - an empty
            // row in practice too - so the board never jumps at a phase.
            if (heights.Select(h => h.Row).Distinct().Count() > 1)
            {
                problems.Add($"the flag row at {width}px changes height as the buttons change: "
                             + string.Join(", ", heights.Select(h => $"{h.When} {h.Row}px")));
            }

            if (heights.Select(h => h.Strip).Distinct().Count() > 1)
            {
                problems.Add($"the hold strip at {width}px changes height: " + string.Join(", ", heights.Select(h => $"{h.When} {h.Strip}px")));
            }

            if (strip.Height <= 0)
            {
                problems.Add($"the hold strip at {width}px has no height");
            }

            // The card's title on the row, and the meters reaching from the
            // top of it (26 Oct 2026: "expand the height of the VU meters to
            // the top of the text of the card's title").
            if (row.CaptionText.Length == 0 || row.CaptionBox.Height <= 0)
            {
                problems.Add($"the flag row at {width}px draws no title");
            }

            // And down to the foot of the On Air bar ("stretch vu meters taller").
            // Their panel's edge (drawn one up from its foot) on the card's
            // edge (drawn HoldStrip.CardInsetBelow up from the strip's foot).
            if (row.Meters.Visible && (row.Meters.Top != row.CaptionBox.Top || row.Meters.Bottom > row.Height
                                       || row.Meters.Bottom - 1 != strip.Bottom - HoldStrip.CardInsetBelow))
            {
                problems.Add($"the meters at {width}px are at {row.Meters.Bounds}, not from the title's top ({row.CaptionBox}) to the On Air bar's foot ({strip.Bottom}) within the row ({row.Height}px)");
            }

            Assert.Equal("Ready", row.PhaseText);
            Assert.StartsWith("Top of the race", row.NowPlayingText, StringComparison.Ordinal);
        }
        finally
        {
            Application.ThreadException -= Threw;
        }

        return problems;
    }

    // --- Helpers ----------------------------------------------------------

    private static int Count(RaceBarButtons buttons)
    {
        var count = 0;

        foreach (var bit in Enum.GetValues<RaceBarButtons>())
        {
            if (bit != RaceBarButtons.None && buttons.HasFlag(bit))
            {
                count++;
            }
        }

        return count;
    }

    private static int Occurrences(string text, string what)
    {
        var count = 0;

        for (var at = text.IndexOf(what, StringComparison.Ordinal); at >= 0; at = text.IndexOf(what, at + what.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
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

    private sealed class QuietWindow : Form
    {
        public QuietWindow()
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-20000, -20000);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            Opacity = 0d;
            Size = new Size(200, 200);
            Font = AppTheme.BodyFont;
            BackColor = AppTheme.Background;
        }

        protected override bool ShowWithoutActivation => true;
    }
}
