using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// PLAY AGAIN on Race Control (25 Nov 2026, asked for by Asher: "add a play
/// again option on race control", then "A bar, with the split button from B,
/// and a change the flags like in C" - board G of plans/play-again-plan.md).
/// </summary>
public sealed class PlayAgainTests
{
    private static readonly DateTime Evening = new(2026, 11, 25, 20, 41, 12);

    private static RaceFactsStamp Race(long race, int leaderLaps) => new(race, 0, 0, leaderLaps, 0);

    private static SaidLine Line(long n, SaidKind kind, string words, long race = 3, int leaderLaps = 14, DateTime? at = null, byte[]? audio = null, string? label = null, long? group = null) =>
        new(n, words, words, kind, "Finley", at ?? Evening.AddSeconds(n), Race(race, leaderLaps), null, audio ?? new byte[] { 1 }, null, label, group ?? n);

    private static IReadOnlyList<SaidLine> Log(params SaidLine[] newestFirst) => newestFirst;

    // --- What kind of line ---------------------------------------------------

    [Theory]
    [InlineData(null, null, "[voice] Winner", SaidKind.Winner)]
    [InlineData(null, null, "[voice] Class winners", SaidKind.Winner)]
    [InlineData(AnnouncementEventType.Reminder, "Call-up", "[voice] Reminder", SaidKind.CallUp)]
    [InlineData(AnnouncementEventType.GridCall, null, "[voice] Announcing", SaidKind.CallUp)]
    [InlineData(AnnouncementEventType.CountdownStarted, null, "[voice] Announcing", SaidKind.Countdown)]
    [InlineData(AnnouncementEventType.RedFlag, null, "[voice] RedFlag", SaidKind.Flag)]
    [InlineData(AnnouncementEventType.RaceCompleted, null, "[voice] Announcing", SaidKind.Finish)]
    [InlineData(AnnouncementEventType.LastLap, null, "[voice] Announcing", SaidKind.LastLap)]
    [InlineData(AnnouncementEventType.PositionReport, null, "[voice] Position report", SaidKind.RaceCall)]
    [InlineData(AnnouncementEventType.Reminder, "Race preview", "[voice] Reminder", SaidKind.Commentary)]
    [InlineData(AnnouncementEventType.Reminder, "Notice", "[voice] Reminder", SaidKind.Notice)]
    [InlineData(AnnouncementEventType.Reminder, null, "[voice] Reminder", SaidKind.Notice)]
    public void A_kept_line_knows_what_kind_of_line_it_was(AnnouncementEventType? call, string? label, string prefix, SaidKind kind)
    {
        Assert.Equal(kind, PlayAgain.KindOf(call, label, prefix));
    }

    // --- The log --------------------------------------------------------------

    [Fact]
    public void The_log_keeps_the_newest_twenty_lines_newest_first()
    {
        IReadOnlyList<SaidLine> log = Array.Empty<SaidLine>();

        for (var n = 1; n <= 25; n++)
        {
            log = PlayAgain.Added(log, Line(n, SaidKind.Notice, $"line {n}"));
        }

        Assert.Equal(PlayAgain.MostLines, log.Count);
        Assert.Equal(25, log[0].N);
        Assert.Equal(6, log[^1].N);
    }

    [Fact]
    public void A_new_race_night_at_6am_starts_the_log_again_but_1am_is_still_the_evening_before()
    {
        var oneAm = new DateTime(2026, 11, 26, 1, 0, 0);
        var sevenAm = new DateTime(2026, 11, 26, 7, 0, 0);

        var log = PlayAgain.Added(Log(Line(1, SaidKind.Winner, "evening", at: Evening)), Line(2, SaidKind.Notice, "late", at: oneAm));
        Assert.Equal(2, log.Count);

        log = PlayAgain.Added(log, Line(3, SaidKind.Notice, "morning", at: sevenAm));
        Assert.Single(log);
    }

    [Fact]
    public void The_oldest_lines_go_once_the_kept_audio_passes_its_limit()
    {
        var big = new byte[PlayAgain.MostAudioBytes / 3 + 1];
        IReadOnlyList<SaidLine> log = Array.Empty<SaidLine>();

        for (var n = 1; n <= 4; n++)
        {
            log = PlayAgain.Added(log, Line(n, SaidKind.Notice, $"line {n}", audio: big));
        }

        Assert.Equal(new long[] { 4, 3 }, log.Select(l => l.N));
    }

    // --- Out of date -------------------------------------------------------------

    [Fact]
    public void A_race_call_is_out_of_date_once_the_leader_has_done_another_lap_or_the_race_is_over()
    {
        var gap = Line(1, SaidKind.RaceCall, "Ortiz leads Kapoor by 1.8 seconds.", race: 3, leaderLaps: 12);

        Assert.Null(PlayAgain.WhyOutOfDate(gap, Race(3, 12), raceOverSince: false));
        Assert.Equal(PlayAgain.OutOfDateWords, PlayAgain.WhyOutOfDate(gap, Race(3, 13), raceOverSince: false));
        Assert.Equal(PlayAgain.OutOfDateWords, PlayAgain.WhyOutOfDate(gap, Race(3, 12), raceOverSince: true));
        Assert.Equal(PlayAgain.OutOfDateWords, PlayAgain.WhyOutOfDate(gap, Race(4, 0), raceOverSince: false));
    }

    [Theory]
    [InlineData(SaidKind.CallUp)]
    [InlineData(SaidKind.Winner)]
    [InlineData(SaidKind.Flag)]
    [InlineData(SaidKind.Finish)]
    [InlineData(SaidKind.LastLap)]
    [InlineData(SaidKind.Notice)]
    public void Call_ups_flags_the_finish_and_the_winner_stay_playable_all_night(SaidKind kind)
    {
        Assert.Null(PlayAgain.WhyOutOfDate(Line(1, kind, "words", race: 3, leaderLaps: 2), Race(9, 40), raceOverSince: true));
    }

    [Fact]
    public void An_ai_line_written_during_a_race_is_held_to_its_own_facts_but_not_its_deadline()
    {
        var facts = new RaceFactsStamp(3, 1, 0, 10, 0, SayByMs: 5);
        var line = Line(1, SaidKind.Commentary, "Kapoor's been quickest all race.") with { Facts = facts };

        Assert.Null(PlayAgain.WhyOutOfDate(line, new RaceFactsStamp(3, 1, 0, 10, 99_000), raceOverSince: false));
        Assert.NotNull(PlayAgain.WhyOutOfDate(line, new RaceFactsStamp(3, 2, 0, 10, 99_000), raceOverSince: false));
    }

    // --- Exchanges ------------------------------------------------------------

    [Fact]
    public void A_two_voice_exchange_is_one_group_and_plays_whole_in_the_order_said()
    {
        var first = Line(1, SaidKind.Commentary, "Finley's turn", label: "Race preview");
        var log = Log(first);
        var group = PlayAgain.GroupFor(log, SaidKind.Commentary, "Race preview", first.AtLocal.AddSeconds(4), 2);
        var second = Line(2, SaidKind.Commentary, "Alicia's turn", label: "Race preview", group: group);
        log = PlayAgain.Added(log, second);

        Assert.Equal(first.Group, group);
        Assert.Equal(new[] { "Finley's turn", "Alicia's turn" }, PlayAgain.WithItsExchange(log, second).Select(l => l.Shown));

        // A minute later, or under another name, is a new piece.
        Assert.Equal(3, PlayAgain.GroupFor(log, SaidKind.Commentary, "Race preview", first.AtLocal.AddMinutes(1), 3));
        Assert.Equal(3, PlayAgain.GroupFor(log, SaidKind.Commentary, "Recap", first.AtLocal.AddSeconds(8), 3));
    }

    // --- The main press follows the flags ---------------------------------------

    [Fact]
    public void After_the_flag_one_press_plays_the_winner_then_the_class_winners()
    {
        var log = Log(
            Line(4, SaidKind.Winner, "Class winners: Ana in 2WD."),
            Line(3, SaidKind.Winner, "And the winner of Heat 3 is Maya Ortiz!"),
            Line(2, SaidKind.Finish, "That's the finish!"),
            Line(1, SaidKind.RaceCall, "Ortiz leads by 1.8 seconds.", leaderLaps: 12));

        var press = PlayAgain.MainPress(log, PlayAgainPhase.AfterTheFlag, muted: false, Race(3, 14), raceOverSince: true);

        Assert.Equal("Play winner", press.Caption);
        Assert.True(press.Enabled);
        Assert.Equal(new long[] { 3, 4 }, press.Lines.Select(l => l.N));
    }

    [Fact]
    public void After_the_flag_a_call_up_said_since_the_winner_is_what_one_press_plays()
    {
        var log = Log(
            Line(5, SaidKind.CallUp, "Heat 4 to the drivers' stand."),
            Line(3, SaidKind.Winner, "And the winner is Maya Ortiz!"));

        var press = PlayAgain.MainPress(log, PlayAgainPhase.AfterTheFlag, false, Race(3, 14), true);

        Assert.Equal("Play call-up", press.Caption);
        Assert.Equal(5, Assert.Single(press.Lines).N);
    }

    [Fact]
    public void Between_races_one_press_plays_the_last_call_up()
    {
        var log = Log(
            Line(6, SaidKind.Notice, "Five minutes to Heat 4."),
            Line(5, SaidKind.CallUp, "Heat 4 to the drivers' stand."));

        var press = PlayAgain.MainPress(log, PlayAgainPhase.BetweenRaces, false, Race(3, 14), true);

        Assert.Equal("Play call-up", press.Caption);
        Assert.Equal(5, Assert.Single(press.Lines).N);
    }

    [Fact]
    public void During_a_race_one_press_plays_the_newest_line_from_this_race_still_true()
    {
        var log = Log(
            Line(9, SaidKind.RaceCall, "Kapoor is half a second back.", race: 4, leaderLaps: 5),
            Line(8, SaidKind.LastLap, "Last lap for Maya Ortiz.", race: 4, leaderLaps: 4),
            Line(3, SaidKind.Winner, "Last race's winner.", race: 3));

        // The gap went out of date a lap ago; the last-lap call is still true.
        var press = PlayAgain.MainPress(log, PlayAgainPhase.Racing, false, Race(4, 6), false);

        Assert.Equal("Play again", press.Caption);
        Assert.Equal(8, Assert.Single(press.Lines).N);

        // Nothing from this race yet: the last race's lines are history.
        Assert.False(PlayAgain.MainPress(Log(Line(3, SaidKind.Winner, "w", race: 3)), PlayAgainPhase.Racing, false, Race(4, 0), false).Enabled);
    }

    [Fact]
    public void Nothing_plays_over_the_start_or_while_muted_and_the_button_says_so()
    {
        var log = Log(Line(1, SaidKind.CallUp, "Heat 4 to the stand."));

        var starting = PlayAgain.MainPress(log, PlayAgainPhase.Starting, false, Race(3, 0), false);
        Assert.False(starting.Enabled);
        Assert.Equal("after the start", starting.Detail);

        var muted = PlayAgain.MainPress(log, PlayAgainPhase.BetweenRaces, true, Race(3, 0), false);
        Assert.False(muted.Enabled);
        Assert.Equal("Muted", muted.Caption);

        Assert.Equal("nothing said yet", PlayAgain.MainPress(Array.Empty<SaidLine>(), PlayAgainPhase.BetweenRaces, false, Race(3, 0), false).Detail);
    }

    [Theory]
    [InlineData(RaceState.Idle, false, PlayAgainPhase.BetweenRaces)]
    [InlineData(RaceState.Staging, false, PlayAgainPhase.Starting)]
    [InlineData(RaceState.Countdown, false, PlayAgainPhase.Starting)]
    [InlineData(RaceState.Racing, false, PlayAgainPhase.Racing)]
    [InlineData(RaceState.Finished, false, PlayAgainPhase.AfterTheFlag)]
    [InlineData(RaceState.Idle, true, PlayAgainPhase.Racing)]
    public void The_phase_follows_the_race_and_practice_counts_as_racing(RaceState state, bool practice, PlayAgainPhase phase)
    {
        Assert.Equal(phase, PlayAgain.PhaseOf(state, practice));
    }

    // --- Words -------------------------------------------------------------------

    [Fact]
    public void The_words_are_plain_and_the_speaker_goes_by_first_name()
    {
        Assert.Equal("Finley", PlayAgainWords.SpeakerName("Finley - announcer"));
        Assert.Equal("Alicia", PlayAgainWords.SpeakerName("Alicia (co-host)"));
        Assert.Equal(string.Empty, PlayAgainWords.SpeakerName(null));

        var line = Line(1, SaidKind.Winner, "And the winner is Maya Ortiz!");
        Assert.StartsWith("Last said · Winner · ", PlayAgainWords.Resting(Log(line))!.Value.Lead, StringComparison.Ordinal);
        Assert.Equal(PlayAgain.OutOfDateWords, PlayAgainWords.Row(line, PlayAgain.OutOfDateWords).Note);
        Assert.EndsWith("…", PlayAgainWords.Shortened(new string('x', 100)), StringComparison.Ordinal);
        Assert.Null(PlayAgainWords.Resting(Array.Empty<SaidLine>()));
    }

    // --- The announcer --------------------------------------------------------------

    [Fact]
    public void The_announcer_keeps_a_heard_line_and_wont_play_again_while_muted()
    {
        using var data = new TempAppData();
        var announcer = new AnnouncementService(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load());
        announcer.ForgetSaidForTests();
        var told = 0;
        announcer.SaidChanged += () => told++;

        announcer.NoteSaidForTests("[excited] And the winner is Maya Ortiz!", new byte[] { 1, 2 }, null, "[voice] Winner");

        var kept = Assert.Single(announcer.Said);
        Assert.Equal(SaidKind.Winner, kept.Kind);
        Assert.Equal("And the winner is Maya Ortiz!", kept.Shown);
        Assert.Equal(1, told);

        announcer.Muted = true;
        Assert.Equal("the announcer is muted", announcer.PlayAgainLines(announcer.Said));
        announcer.Muted = false;
        Assert.Equal("there's nothing to play again", announcer.PlayAgainLines(Array.Empty<SaidLine>()));
    }

    [Fact]
    public void Every_path_a_line_reaches_the_speaker_by_keeps_it()
    {
        Assert.Contains("NoteSaid(text, audio, currentProfile, announcingLogPrefix);", Source("AnnouncementService.VoiceQueue.cs"), StringComparison.Ordinal);
        Assert.Contains("NoteSaid(text, null, profile, announcingLogPrefix);", Source("AnnouncementService.Streaming.cs"), StringComparison.Ordinal);
        Assert.Contains("NoteSaid(text, made.Value.Audio, null, $\"[voice] {type}\", type);", Source("AnnouncementService.PreparedLines.cs"), StringComparison.Ordinal);
        Assert.Contains("NoteSaid(prepared.Text, audio, null, \"[voice] Reminder (prepared)\");", Source("AnnouncementService.cs"), StringComparison.Ordinal);
    }

    // --- Race Control -------------------------------------------------------------------

    [Fact]
    public void On_race_control_the_button_sits_at_the_on_air_bars_right_end_and_follows_what_was_said()
    {
        var seen = StaThread.Run(() =>
        {
            using var data = new TempAppData();

            new AppSettings
            {
                StartMaximized = false,
                HasCompletedFirstRun = true,
                HasSeenRaceDaySetup = true,
                AutoBackupEnabled = false,
                ConnectDecodersAtStartup = false,
            }.Save();

            var window = new MainForm { LayoutCheckMode = true, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };

            try
            {
                window.Show();
                StaThread.Settle();
                window.SizeContentForLayoutCheck(new Size(1440, 900));
                StaThread.Settle();

                var page = Field<RaceControlControl>(window, "_raceControlControl");
                var announcer = Field<AnnouncementService>(page, "_announcer");
                var strip = Field<HoldStrip>(page, "_holdStrip");
                var button = page.PlayAgainButton;
                announcer.ForgetSaidForTests();
                page.RefreshPlayAgain();

                var atRest = (button.Caption, button.Detail, button.Ready, strip.LastSaid is null);
                var placed = button.Parent == strip && button.Right <= strip.ClientSize.Width && button.Right >= strip.ClientSize.Width - UiLayout.AtDpi(8)
                             && strip.Height >= button.Height + UiLayout.AtDpi(8);

                var nothing = page.RunRemoteCommand("sayagain", false, null);

                announcer.NoteSaidForTests("Heat 4, 2WD Buggy, to the drivers' stand.", new byte[] { 1 }, AnnouncementEventType.GridCall, "[voice] Announcing");
                StaThread.Settle();

                var afterCallUp = (button.Caption, button.Ready, strip.LastSaid?.Kind);

                return (atRest, placed, nothing, afterCallUp);
            }
            finally
            {
                window.Close();
                window.Dispose();
                StaThread.Settle();
            }
        }, TimeSpan.FromMinutes(3), () => "Play Again on Race Control");

        Assert.Equal(("Play again", "nothing said yet", false, true), seen.atRest);
        Assert.True(seen.placed, "the button sits at the bar's right end, and the bar is tall enough for it");
        Assert.Equal(RemoteCommandOutcome.Refused("Nothing played: nothing said yet."), seen.nothing);
        Assert.Equal(("Play call-up", true, "Call-up"), seen.afterCallUp);
    }

    [Fact]
    public void S_and_the_phones_say_again_press_the_same_button()
    {
        Assert.Contains("case Keys.S when !e.Shift: rc.TriggerPlayAgain(); break;", Source("MainForm.Keyboard.cs"), StringComparison.Ordinal);
        Assert.Contains("go('sayagain')\">SAY AGAIN</button>", Source("WebServerService.cs"), StringComparison.Ordinal);
        Assert.Contains("\"sayagain\"", Source("WebServerService.cs"), StringComparison.Ordinal);
        Assert.Contains(ShortcutCatalog.Sections[0].Items, s => s.Keys == "S");
    }

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file)).Replace("\r\n", "\n");
    }
}
