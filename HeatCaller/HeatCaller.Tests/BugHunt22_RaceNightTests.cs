namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 22 (19 Nov 2026), race night: calls held back while scoring by
/// hand, time-left calls counted from the mark, a half-run Pole Shootout,
/// and the run sheet's "+0 min".
/// </summary>
public sealed class BugHunt22_RaceNightTests
{
    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }

    private static string Between(string text, string from, string to)
    {
        var start = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{from}' not found");
        var end = text.IndexOf(to, start, StringComparison.Ordinal);
        Assert.True(end > start, $"'{to}' not found after '{from}'");
        return text[start..end];
    }

    // --- Scoring by hand ---------------------------------------------------------

    [Theory]
    [InlineData("_splitCrossedHandler = ", "_race.SplitCrossed += ")]
    [InlineData("_sectorsRecordedHandler = ", "_race.SectorsRecorded += ")]
    [InlineData("_personalBestHandler = ", "_race.PersonalBestLap += ")]
    public void A_crossing_scored_by_hand_has_none_of_its_calls_said(string handler, string wiredUp)
    {
        // LapProcessed already held its calls back; a personal best, a split
        // and a sector from the same untrusted loop were still called.
        var body = Between(Source("RaceControlControl.RaceEvents.cs"), handler, wiredUp);

        Assert.Contains("if (HandScoringOn) { _announcer.WhileHandScoring(", body);
    }

    // --- Lap calls timed from the crossing, inline -------------------------------

    [Fact]
    public void Inline_a_pass_handled_seconds_after_it_arrived_is_delivered_as_stale()
    {
        // The default mode. The decoder thread stamps the pass and posts it;
        // a busy window handles it late. Its calls were timed from then.
        var clock = 1_000_000L;
        var engine = new RaceControlSession { NowMsSource = () => clock };
        var race = new RaceTiming(engine);
        var seen = new List<(long? RaisedAt, bool Stale)>();
        race.LapProcessed += (_, _, _) => seen.Add((race.DeliveringEventRaisedAtMs, race.DeliveringIsStale));

        race.Stage();
        race.GreenFlag(10_000);
        race.ProcessLapEvent(Pass("101", 21_000, receivedMs: 0)); // stamped now

        clock += 12_000;
        race.ProcessLapEvent(Pass("101", 33_000, receivedMs: clock - 5_000)); // handled 5 s after it arrived

        Assert.NotEmpty(seen);
        var late = seen[^1];
        Assert.True(late.Stale);
        Assert.InRange(TimingClock.NowMs - late.RaisedAt!.Value, 5_000, 6_000);
        Assert.Null(race.DeliveringEventRaisedAtMs);
    }

    [Fact]
    public void Inline_a_pass_handled_as_it_arrives_is_fresh()
    {
        var clock = 1_000_000L;
        var engine = new RaceControlSession { NowMsSource = () => clock };
        var race = new RaceTiming(engine);
        var seen = new List<bool>();
        race.LapProcessed += (_, _, _) => seen.Add(race.DeliveringIsStale);

        race.Stage();
        race.GreenFlag(10_000);
        race.ProcessLapEvent(Pass("101", 21_000, receivedMs: clock));
        clock += 12_000;
        race.ProcessLapEvent(Pass("101", 33_000, receivedMs: clock));

        Assert.NotEmpty(seen);
        Assert.All(seen, Assert.False);
    }

    private static DirectLapEvent Pass(string car, uint deviceTimeMs, long receivedMs) => new()
    {
        TransponderId = car,
        DeviceTimeMs = deviceTimeMs,
        SignalHits = 1,
        SignalStrength = 100,
        Decoder = DecoderConnection.EasyLap,
        ReceivedMs = receivedMs,
    };

    // --- Time left ----------------------------------------------------------------

    [Fact]
    public void A_mark_offered_on_time_gets_its_full_window()
    {
        Assert.Equal(1_000 + AnnouncementService.FinalSecondsSayWithinMs,
            AnnouncementService.TimeLeftSayByMs(1_000, 30, TimeSpan.FromSeconds(30), AnnouncementService.FinalSecondsSayWithinMs));
    }

    [Fact]
    public void A_mark_offered_late_has_its_window_counted_from_the_mark()
    {
        // "Thirty seconds" offered with 25.1 s left: 4.9 s of its 6 s are gone.
        Assert.Equal(1_000 + AnnouncementService.FinalSecondsSayWithinMs - 4_900,
            AnnouncementService.TimeLeftSayByMs(1_000, 30, TimeSpan.FromMilliseconds(25_100), AnnouncementService.FinalSecondsSayWithinMs));
    }

    [Fact]
    public void A_mark_offered_past_its_window_is_not_said()
    {
        Assert.Null(AnnouncementService.TimeLeftSayByMs(1_000, 300, TimeSpan.FromSeconds(270), AnnouncementService.MinutesLeftSayWithinMs));
    }

    [Fact]
    public void The_window_always_tells_the_announcer_the_time_left()
    {
        Assert.Contains("_announcer.NotifyTimeLeft(secondsLeft, _race.TimeLimit - _race.ElapsedRaceTime);", Source("MainForm.cs"));
    }

    // --- Pole Shootout --------------------------------------------------------------

    [Fact]
    public void Two_runs_of_four_in_is_not_a_finished_shootout_even_when_only_those_two_are_known()
    {
        var runs = PoleShootout.BuildRuns("Stock", new[] { "q1", "q2", "q3", "q4" }, 1, null, "Club");
        runs[0].LinkedSessionId = "r1";
        runs[1].LinkedSessionId = "r2";
        var sessions = new Dictionary<string, SessionRecord>
        {
            ["r1"] = Run(runs[0].AssignedTransponders[0], 12_900),
            ["r2"] = Run(runs[1].AssignedTransponders[0], 12_300),
        };

        // The event report holds only the runs in its own sessions.
        var standing = PoleShootout.Standings(runs.Take(2).ToList(), id => sessions.GetValueOrDefault(id), 1);

        Assert.Equal(2, standing.RunsDone);
        Assert.Equal(4, standing.Runs);
        Assert.False(standing.Complete);
    }

    // --- Run sheet ----------------------------------------------------------------

    [Fact]
    public void Racing_done_a_few_seconds_late_is_never_a_red_plus_nought()
    {
        var late = RunSheet.EndOf(TimeSpan.FromSeconds(20), hasOutBy: true);

        Assert.Equal(RunSheet.Pill.Late, late.Pill);
        Assert.Equal("+1 min", late.Words);
    }

    // --- Overlay editor and listening ---------------------------------------------

    [Fact]
    public void An_arrow_key_nudge_is_saved_to_the_graphic_it_moved()
    {
        // The timer read `sel` when it fired: pick another graphic within
        // 350 ms and the nudge was saved to that one.
        var script = Source("WebServerService.OverlayEdit.cs");

        Assert.Contains("const k = sel;", script);
        Assert.Contains("setTimeout(() => finish(k, node, scaleOf(k)), 350)", script);
        Assert.DoesNotContain("finish(sel, node, scaleOf(sel)), 350", script);
    }

    [Fact]
    public void Only_a_yes_to_opening_listening_counts_as_the_relay_reached()
    {
        var open = Between(Source("RelayPublisher.Listen.cs"), "if ((int)response.StatusCode >= 500)", "IsGoneForAClose(response)");

        Assert.Contains("else if (response.IsSuccessStatusCode)", open);
    }

    private static SessionRecord Run(string transponder, long lapMs) => new()
    {
        SessionType = RaceSessionType.Race,
        Results = new List<SessionResultEntry>
        {
            new()
            {
                Position = 1,
                Transponder = transponder,
                DriverName = transponder,
                Laps = 2,
                LapTimesMs = new List<long> { 0, lapMs },
            },
        },
    };
}
