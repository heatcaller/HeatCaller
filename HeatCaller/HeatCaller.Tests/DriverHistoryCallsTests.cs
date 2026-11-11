namespace HeatCaller.Tests;

/// <summary>
/// The calls that know a driver's history (11 Nov 2026, voice speed plan,
/// phase 4 #4): quicker than last time out, back on form, milestones, and
/// rivals side by side.
/// </summary>
public sealed class DriverHistoryCallsTests
{
    private const string Track = "Club Oval";
    private const string Dee = "T-DEE";
    private static readonly DateOnly Tonight = new(2026, 11, 11);

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

    /// <summary>A race on <paramref name="night"/> (7 pm local) in which Dee's best lap was <paramref name="bestMs"/>.</summary>
    private static SessionRecord Race(DateOnly night, long bestMs, int laps = 20, string track = Track, bool demo = false, RaceSessionType type = RaceSessionType.Race) => new()
    {
        SessionType = type,
        TrackLayoutName = track,
        IsDemo = demo,
        StartedAtUtc = night.ToDateTime(new TimeOnly(19, 0), DateTimeKind.Local).ToUniversalTime(),
        Results = { new SessionResultEntry { Transponder = Dee, FastLapMs = bestMs, Laps = laps } },
    };

    // ----------------------------------------------------------- last time out

    [Fact]
    public void Last_time_out_is_the_best_of_their_last_night_here_not_tonight()
    {
        var history = new[]
        {
            Race(new DateOnly(2026, 10, 28), 6_900),
            Race(new DateOnly(2026, 11, 4), 7_100),
            Race(new DateOnly(2026, 11, 4), 7_050), // two heats that night: the better counts
            Race(Tonight, 6_800),                   // tonight's earlier heat is tonight, not last time
            Race(new DateOnly(2026, 11, 5), 6_000, track: "Other Layout"),
        };

        var nights = DriverHistory.NightsOn(history, Track, Dee, Tonight);

        Assert.Equal(7_050, DriverHistory.LastTimeOut(nights));
        Assert.Equal(2, nights.Count);
    }

    [Fact]
    public void Demos_and_imported_races_are_not_their_history()
    {
        var imported = Race(new DateOnly(2026, 11, 4), 6_500);
        imported.ImportedFrom = "LiveTime";

        var history = new[] { Race(new DateOnly(2026, 11, 3), 6_400, demo: true), imported };

        Assert.Null(DriverHistory.LastTimeOut(DriverHistory.NightsOn(history, Track, Dee, Tonight)));
        Assert.Equal(0, DriverHistory.RacesBefore(history, Dee));
    }

    [Fact]
    public void A_driver_new_to_the_layout_has_no_last_time()
    {
        Assert.Null(DriverHistory.LastTimeOut(DriverHistory.NightsOn(Array.Empty<SessionRecord>(), Track, Dee, Tonight)));
    }

    // ------------------------------------------------------------- back on form

    [Fact]
    public void Back_on_form_needs_three_slower_nights_after_a_better_one()
    {
        var history = new[]
        {
            Race(new DateOnly(2026, 10, 7), 6_800),  // their best
            Race(new DateOnly(2026, 10, 14), 7_200),
            Race(new DateOnly(2026, 10, 21), 7_100),
            Race(new DateOnly(2026, 10, 28), 7_300),
        };

        // The best of the slow run is the mark to beat.
        Assert.Equal(7_100, DriverHistory.SlumpBest(DriverHistory.NightsOn(history, Track, Dee, Tonight)));
    }

    [Fact]
    public void A_driver_still_getting_quicker_is_not_back_on_form()
    {
        var history = new[]
        {
            Race(new DateOnly(2026, 10, 7), 7_400),
            Race(new DateOnly(2026, 10, 14), 7_200),
            Race(new DateOnly(2026, 10, 21), 7_100),
            Race(new DateOnly(2026, 10, 28), 7_000),
        };

        Assert.Null(DriverHistory.SlumpBest(DriverHistory.NightsOn(history, Track, Dee, Tonight)));
    }

    [Fact]
    public void Three_nights_alone_are_not_enough_to_call_a_slump()
    {
        var history = new[]
        {
            Race(new DateOnly(2026, 10, 14), 7_200),
            Race(new DateOnly(2026, 10, 21), 7_100),
            Race(new DateOnly(2026, 10, 28), 7_300),
        };

        Assert.Null(DriverHistory.SlumpBest(DriverHistory.NightsOn(history, Track, Dee, Tonight)));
    }

    // --------------------------------------------------------------- milestones

    [Fact]
    public void Races_and_laps_are_counted_from_the_races_that_count()
    {
        var history = new[]
        {
            Race(new DateOnly(2026, 10, 7), 7_000, laps: 30),
            Race(new DateOnly(2026, 10, 8), 7_000, laps: 40, type: RaceSessionType.Practice),
            Race(new DateOnly(2026, 10, 9), 7_000, laps: 25, demo: true),
        };

        Assert.Equal(1, DriverHistory.RacesBefore(history, Dee));  // practice is no race
        Assert.Equal(70, DriverHistory.LapsBefore(history, Dee));  // but its laps are laps
    }

    [Theory]
    [InlineData(50, true)]
    [InlineData(100, true)]
    [InlineData(300, true)]
    [InlineData(150, false)]
    [InlineData(49, false)]
    [InlineData(1, false)]
    public void The_races_worth_saying(int race, bool milestone)
    {
        Assert.Equal(milestone, DriverHistory.IsRaceMilestone(race));
    }

    [Fact]
    public void The_thousandth_lap_is_called_on_the_lap_itself()
    {
        Assert.Equal(1000, DriverHistory.LapMilestoneReached(990, 10));
        Assert.Null(DriverHistory.LapMilestoneReached(990, 9));
        Assert.Null(DriverHistory.LapMilestoneReached(990, 11));
        Assert.Equal(2000, DriverHistory.LapMilestoneReached(1_995, 5));

        // Not before a lap tonight: the thousand came last race.
        Assert.Null(DriverHistory.LapMilestoneReached(1000, 0));
    }

    [Theory]
    [InlineData(50, "fiftieth")]
    [InlineData(100, "hundredth")]
    [InlineData(300, "three hundredth")]
    [InlineData(1000, "thousandth")]
    [InlineData(2000, "two thousandth")]
    public void A_milestone_is_said_as_an_ordinal(int n, string said)
    {
        Assert.Equal(said, AnnouncementService.MilestoneOrdinal(n));
    }

    // ---------------------------------------------------------------- wiring

    [Fact]
    public void The_lap_calls_stand_behind_fastest_ever_here_and_in_front_of_the_personal_best()
    {
        var events = Source("AnnouncementService.RaceEvents.cs");
        var bestEver = events.IndexOf("if (CallTheBestEverHere(transponderId, standing, lapMs))", StringComparison.Ordinal);
        var history = events.IndexOf("if (CallTheLapAgainstHistory(transponderId, standing, lapMs))", StringComparison.Ordinal);
        var personalBest = events.IndexOf("EnqueueVoice(AnnouncementEventType.PersonalBestLap, standing, null, lapMs);", StringComparison.Ordinal);

        Assert.True(bestEver > 0 && history > bestEver && personalBest > history);

        // Race Control answers, and leaves out who the records leave out.
        var control = Source("RaceControlControl.DriverHistory.cs");
        Assert.Contains("_race.IsDemoMode", control);
        Assert.Contains("{ ExcludeFromRecords: true } or { IsRental: true }", control);
        Assert.Contains("_toldTonight.Add($\"{tonight}|form|{transponder}\")", control);

        var wiring = Source("RaceControlControl.RaceEvents.cs");
        Assert.Contains("_announcer.LapAgainstHistory = LapAgainstHistory;", wiring);
        Assert.Contains("_announcer.CareerBefore = CareerBefore;", wiring);
    }

    [Fact]
    public void A_milestone_is_marked_said_only_once_it_is_heard()
    {
        var history = Source("AnnouncementService.DriverHistory.cs");
        Assert.Contains("heard: () =>", history);
        Assert.Contains("_milestonesHeard.Add(heard);", history);

        var queue = Source("AnnouncementService.VoiceQueue.cs");
        var spoken = queue.IndexOf("said = true;", StringComparison.Ordinal);
        var heardAt = queue.IndexOf("heard?.Invoke();", StringComparison.Ordinal);
        Assert.True(spoken > 0 && heardAt > spoken);

        Assert.Contains("ForgetMilestones();", Source("AnnouncementService.Density.cs"));
    }

    [Fact]
    public void Rivals_within_a_second_get_their_own_line_in_place_of_the_battle()
    {
        var laps = Source("AnnouncementService.LapCalls.cs");

        Assert.Contains("var rivals = rivalsOn && AreRivals(front.Transponder, chaser.Transponder);", laps);
        Assert.Contains("FirePairLine(AnnouncementEventType.RivalsSideBySide, chaser.Transponder, front.Transponder, offer, battleFor,", laps);
        Assert.Contains("if (Muted || (!battlesOn && !rivalsOn))", laps);
    }

    [Fact]
    public void The_new_calls_have_rows_default_lines_and_descriptions()
    {
        var defaults = VoiceProfile.CreateDefault();
        var rows = VoiceSettingsControl.AnnouncementTypes();

        foreach (var type in new[] { AnnouncementEventType.BeatLastTimeOut, AnnouncementEventType.BackOnForm, AnnouncementEventType.Milestone, AnnouncementEventType.RivalsSideBySide })
        {
            var config = defaults.Events[type.ToString()];
            Assert.True(config.Enabled, type.ToString());
            Assert.NotEmpty(config.Templates);
            Assert.Contains(type, rows);
            Assert.Equal("[excited]", AudioTagTiers.For(type));
        }

        Assert.Equal(rows.ToList().IndexOf(AnnouncementEventType.PersonalBestsTogether) + 1, rows.ToList().IndexOf(AnnouncementEventType.BackOnForm));
        Assert.Equal(rows.ToList().IndexOf(AnnouncementEventType.BackOnForm) + 1, rows.ToList().IndexOf(AnnouncementEventType.BeatLastTimeOut));
        Assert.Equal(rows.ToList().IndexOf(AnnouncementEventType.FinalLapsGap) + 1, rows.ToList().IndexOf(AnnouncementEventType.RivalsSideBySide));
    }
}
