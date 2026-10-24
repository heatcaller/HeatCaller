namespace HeatCaller.Tests;

/// <summary>
/// Ghosts in practice and races (4 Oct bug hunt, fourth pass): a ghost
/// switched off mid-race left its frozen row on the board - called a stopped
/// car on the track map, holding the finish open, and "beaten" for badges -
/// ghosts switched on in a running practice never joined it, a recent-average
/// ghost counted the wait before a driver's first practice lap, practice speeds
/// used Race Control's layout, and two announcer calls still counted muted or
/// faster ghosts.
/// </summary>
public sealed class BugHunt4PracticeGhostTests : IDisposable
{
    private const string Ghost = "GHOST-AAAAAA";

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static void Cross(RaceControlSession race, string car, uint raceTimeMs) =>
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = car,
            DeviceTimeMs = raceTimeMs,
            SignalHits = 1,
            SignalStrength = 100,
            IsSynthetic = true,
        });

    private static RaceControlSession Racing(int? lapLimit = null)
    {
        var race = new RaceControlSession { LapLimit = lapLimit };
        race.Stage();
        race.GreenFlag(0);
        return race;
    }

    // --- practice-1 / session-3: a ghost switched off mid-race ---

    [Fact]
    public void A_ghost_switched_off_mid_race_leaves_the_board()
    {
        var race = Racing();
        var removed = new List<string>();
        race.StandingRemoved += standing => removed.Add(standing.TransponderId);

        Cross(race, Ghost, 5_000);
        Cross(race, "101", 6_000);
        Cross(race, Ghost, 12_000);
        Cross(race, "101", 13_000);
        Cross(race, Ghost, 19_000);

        Assert.True(race.RemoveGhostStanding(Ghost));

        Assert.Equal(new[] { "101" }, race.GetOrderedStandings().Select(s => s.TransponderId));
        Assert.Equal(new[] { Ghost }, removed);
    }

    [Fact]
    public void Only_a_ghost_can_be_taken_off_the_board()
    {
        var race = Racing();
        var removed = 0;
        race.StandingRemoved += _ => removed++;

        Cross(race, Ghost, 5_000);
        Cross(race, "101", 6_000);

        Assert.False(race.RemoveGhostStanding("101"));
        Assert.False(race.RemoveGhostStanding("GHOST-NOTHERE"));

        Assert.Equal(2, race.GetOrderedStandings().Count);
        Assert.Equal(0, removed);
    }

    [Fact]
    public void A_ghost_switched_off_after_the_checkered_no_longer_holds_the_finish_open()
    {
        var race = Racing(lapLimit: 2);

        Cross(race, Ghost, 3_000);
        Cross(race, "101", 4_000);
        Cross(race, "101", 10_000);

        // The real car has taken the flag; the race waits on the ghost.
        Assert.True(race.IsFinishing);
        Assert.Equal(RaceState.Racing, race.State);

        race.RemoveGhostStanding(Ghost);

        Assert.Equal(RaceState.Finished, race.State);
    }

    [Fact]
    public void A_ghost_switched_off_before_the_checkered_is_not_waited_for()
    {
        var race = Racing(lapLimit: 2);

        Cross(race, Ghost, 3_000);
        Cross(race, "101", 4_000);

        race.RemoveGhostStanding(Ghost);
        Assert.Equal(RaceState.Racing, race.State);

        Cross(race, "101", 10_000);

        Assert.Equal(RaceState.Finished, race.State);
    }

    /// <summary>A UI thread stand-in: holds what is posted to it until RunAll.</summary>
    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly List<(SendOrPostCallback Callback, object? State)> _posted = new();

        public int Count
        {
            get { lock (_posted) { return _posted.Count; } }
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_posted)
            {
                _posted.Add((d, state));
            }
        }

        public void RunAll()
        {
            List<(SendOrPostCallback Callback, object? State)> items;

            lock (_posted)
            {
                items = _posted.ToList();
                _posted.Clear();
            }

            foreach (var (callback, state) in items)
            {
                callback(state);
            }
        }
    }

    [Fact]
    public void A_crossing_already_on_its_way_when_a_ghost_is_switched_off_does_not_put_it_back()
    {
        var race = Racing();
        var context = new QueuedContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);

        try
        {
            var pace = new GhostCar { Name = "Pace", Transponder = Ghost, FixedLapMs = 1000 };
            var other = new GhostCar { Name = "Other", Transponder = "GHOST-BBBBBB", FixedLapMs = 60_000 };
            var ghosts = new GhostService(new RaceTiming(race), ProfileStore.Load(), SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>()))
            {
                Settings = new GhostSettings { Enabled = true, Ghosts = { pace, other } },
            };

            ghosts.Start();

            // Its first crossing, handed to the UI thread and not yet run.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (context.Count == 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(20);
            }

            Assert.True(context.Count > 0);

            // Switched off while the other ghost carries on: the restart and
            // the removal KeepRunningGhostsInStep makes, then the queued pass.
            pace.Enabled = false;
            ghosts.Start();
            race.RemoveGhostStanding(Ghost);
            context.RunAll();
            ghosts.Stop();

            Assert.DoesNotContain(race.GetOrderedStandings(), s => s.TransponderId == Ghost);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>A 10x10-tile square lap with the finish a third of the way along the top (as in TrackMapTests).</summary>
    private static RcpTrackDesign Square(bool withSplit)
    {
        var design = new RcpTrackDesign { RouteClosed = true };

        foreach (var (x, y) in new[] { (0f, 0f), (10f, 0f), (10f, 10f), (0f, 10f) })
        {
            design.Route.Add(new RcpRoutePoint { X = x, Y = y });
        }

        design.Decals.Add(new RcpDecal { Kind = RcpDecalKind.FinishLine, Col = 4, Row = 0 });

        if (withSplit)
        {
            design.Decals.Add(new RcpDecal { Kind = RcpDecalKind.SplitLoop1, Col = 9, Row = 4 });
        }

        return design;
    }

    [Fact]
    public void A_ghost_is_never_called_a_stopped_car_on_the_track_map()
    {
        var line = TrackLine.From(Square(withSplit: false))!;
        var positions = new TrackPositions();
        var said = new List<string>();
        positions.CarStopped += (standing, _) => said.Add(standing.TransponderId);

        var ghost = new RacerStanding { TransponderId = Ghost, Laps = 4, ElapsedMs = 26_000 };
        ghost.LapTimesMs.AddRange(new long[] { 5000, 7000, 7000, 7000 });
        positions.Crossed(Ghost, TimingPoint.Line, 1000);

        var placed = Assert.Single(positions.Estimate(new[] { ghost }, line, 60_000, racing: true));

        Assert.False(placed.Stopped);
        Assert.Empty(said);
    }

    [Fact]
    public void A_running_ghost_is_not_called_stopped_at_a_split_loop_it_never_crosses()
    {
        // Split 1 a quarter of the lap round and a 12s lap: the split is
        // "overdue" after 11s, and a ghost only ever crosses the line.
        var line = TrackLine.From(Square(withSplit: true))!;
        var positions = new TrackPositions();

        var ghost = new RacerStanding { TransponderId = Ghost, Laps = 4, ElapsedMs = 42_000 };
        ghost.LapTimesMs.AddRange(new long[] { 6000, 12_000, 12_000, 12_000 });
        positions.Crossed(Ghost, TimingPoint.Line, 1000);

        Assert.False(Assert.Single(positions.Estimate(new[] { ghost }, line, 12_500, racing: true)).Stopped);
    }

    [Fact]
    public void A_ghost_switched_on_part_way_through_a_race_is_not_counted_as_beaten()
    {
        var fromGreen = new GhostCar { Name = "Pace", Transponder = "GHOST-AAAAAA", PaceSource = GhostPaceSource.FixedLapTime };
        var late = new GhostCar { Name = "Late", Transponder = "GHOST-BBBBBB", PaceSource = GhostPaceSource.LiveOwnPace, SourceTransponder = "101" };

        // The late ghost joined on lap 6 and is a long way down on laps.
        var board = new List<(string, int, long)>
        {
            ("101", 10, 9000),
            ("GHOST-AAAAAA", 9, 9500),
            ("GHOST-BBBBBB", 4, 9000),
        };

        var ghosts = AchievementEvaluator.GhostsFromTheGreen(new[] { fromGreen, late }, new[] { "ghost-bbbbbb" });
        var beaten = AchievementEvaluator.GhostsBeatenBy(board, ghosts, byBestLap: false);

        Assert.Equal(new[] { "Pace" }, beaten["101"].Select(g => g.Name));
    }

    // --- practice-2: ghosts switched on during a running practice ---

    private static HashSet<string> Set(params string[] items) => new(items, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Ghosts_switched_on_during_a_running_practice_are_put_on_track()
    {
        Assert.Equal(MainForm.GhostSwitchChange.Restart,
            MainForm.GhostSwitchChangeFor(racing: true, practice: true, ghostsRunning: false, Set("GHOST-1"), Set()));
    }

    [Fact]
    public void A_race_still_leaves_ghosts_that_were_not_running_for_the_next_green()
    {
        Assert.Equal(MainForm.GhostSwitchChange.None,
            MainForm.GhostSwitchChangeFor(racing: true, practice: false, ghostsRunning: false, Set("GHOST-1"), Set()));
    }

    [Fact]
    public void Ghost_switches_change_nothing_unless_who_should_run_changes()
    {
        Assert.Equal(MainForm.GhostSwitchChange.None,
            MainForm.GhostSwitchChangeFor(racing: true, practice: true, ghostsRunning: true, Set("GHOST-1"), Set("ghost-1")));
        Assert.Equal(MainForm.GhostSwitchChange.None,
            MainForm.GhostSwitchChangeFor(racing: false, practice: true, ghostsRunning: false, Set("GHOST-1"), Set()));
        Assert.Equal(MainForm.GhostSwitchChange.StopAll,
            MainForm.GhostSwitchChangeFor(racing: true, practice: false, ghostsRunning: true, Set(), Set("GHOST-1")));
        Assert.Equal(MainForm.GhostSwitchChange.Restart,
            MainForm.GhostSwitchChangeFor(racing: true, practice: false, ghostsRunning: true, Set("GHOST-1", "GHOST-2"), Set("GHOST-1")));
    }

    // --- practice-3: a recent-average ghost's pace ---

    private static SessionResultEntry Entry(params long[] laps) => new()
    {
        Transponder = "101",
        Laps = laps.Length,
        ElapsedMs = laps.Sum(),
        LapTimesMs = laps.ToList(),
    };

    [Fact]
    public void Recent_average_leaves_out_the_wait_before_the_first_practice_lap()
    {
        // Green at 19:00, Sam's first crossing at 19:12, then 30 laps of 10s.
        var entry = Entry(new long[] { 720_000 }.Concat(Enumerable.Repeat(10_000L, 30)).ToArray());

        Assert.Equal(1_020_000, entry.ElapsedMs);
        Assert.Equal(10_000, GhostService.AveragePaceMs(entry));
    }

    [Fact]
    public void Recent_average_leaves_out_stops_and_dashes()
    {
        // A credited jump start (0), a dash to the line, a battery swap.
        var entry = Entry(new long[] { 0, 4_000 }
            .Concat(Enumerable.Repeat(10_000L, 20))
            .Concat(new long[] { 180_000, 10_000 })
            .ToArray());

        Assert.Equal(10_000, GhostService.AveragePaceMs(entry));
    }

    [Fact]
    public void A_session_with_only_its_start_lap_gives_no_average()
    {
        Assert.Null(GhostService.AveragePaceMs(Entry(720_000)));
    }

    [Fact]
    public void An_old_result_without_lap_times_still_averages_its_total()
    {
        var entry = new SessionResultEntry { Transponder = "101", Laps = 10, ElapsedMs = 100_000 };

        Assert.Equal(10_000, GhostService.AveragePaceMs(entry));
    }

    [Fact]
    public void A_recent_average_ghost_skips_a_session_that_holds_only_the_start_lap()
    {
        var older = new SessionRecord
        {
            SessionType = RaceSessionType.Practice,
            StartedAtUtc = new DateTime(2026, 10, 1, 19, 0, 0, DateTimeKind.Utc),
            Results = { Entry(new long[] { 300_000 }.Concat(Enumerable.Repeat(9_000L, 10)).ToArray()) },
        };
        var newer = new SessionRecord
        {
            SessionType = RaceSessionType.Practice,
            StartedAtUtc = new DateTime(2026, 10, 3, 19, 0, 0, DateTimeKind.Utc),
            Results = { Entry(720_000) },
        };

        var race = new RaceControlSession();
        var ghosts = new GhostService(new RaceTiming(race), ProfileStore.Load(), SessionHistoryStore.FromSessions(new[] { older, newer }))
        {
            Settings = new GhostSettings
            {
                Enabled = true,
                Ghosts =
                {
                    new GhostCar
                    {
                        Name = "Sam's ghost",
                        PaceSource = GhostPaceSource.YourRecentAverage,
                        SourceTransponder = "101",
                    },
                },
            },
        };

        var said = new List<string>();
        ghosts.StatusMessage += said.Add;

        ghosts.Start();
        ghosts.Stop();

        Assert.Contains(said, line => line.Contains("Sam's ghost on track at 9.000s", StringComparison.Ordinal));
    }

    // --- practice-4: whose track the speed columns use ---

    [Fact]
    public void Practice_speeds_and_ghosts_use_the_practice_pages_track()
    {
        Assert.Equal("Back Room Oval", MainForm.RunningTrackName(true, false, "Back Room Oval", "Demo Track", "Club Tri-oval"));
        Assert.Null(MainForm.RunningTrackName(true, false, null, "Demo Track", "Club Tri-oval"));
        Assert.Equal("Demo Track", MainForm.RunningTrackName(false, true, "Back Room Oval", "Demo Track", "Club Tri-oval"));
        Assert.Equal("Club Tri-oval", MainForm.RunningTrackName(false, false, "Back Room Oval", "Demo Track", "Club Tri-oval"));
    }

    // --- announcer-3 and announcer-4 ---

    /// <summary>An announcer with every line and sound off except <paramref name="on"/>, and Sam and Bill on file.</summary>
    private static (AnnouncementService Announcer, List<string> Said) Announcer(AnnouncementEventType on, string line, bool announceGhosts)
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "101", DriverName = "Sam" });
        profiles.Upsert(new DriverProfile { Transponder = "102", DriverName = "Bill" });
        profiles.Upsert(new DriverProfile { Transponder = Ghost, DriverName = "Pace Ghost", ExcludeFromRecords = true });

        var voice = VoiceSettingsStore.Load();
        foreach (var config in voice.Current.Events.Values)
        {
            config.Enabled = false;
            config.EnabledInPractice = false;
        }

        voice.Current.Events[on.ToString()] = new AnnouncementEventConfig { Enabled = true, Templates = { line } };

        var sounds = SoundEffectsStore.Load();
        foreach (var effect in sounds.Current.Effects.Values)
        {
            effect.Enabled = false;
        }

        var announcer = new AnnouncementService(profiles, voice, sounds)
        {
            AnnounceGhostsProvider = () => announceGhosts,
        };

        var said = new List<string>();
        announcer.AnnouncementTextReady += text =>
        {
            lock (said)
            {
                said.Add(text);
            }
        };

        return (announcer, said);
    }

    private static GatePassEvent Pass(string transponder, int streak) => new()
    {
        Transponder = transponder,
        Gate = "1",
        GateType = "finish",
        Valid = true,
        Streak = true,
        StreakCountOverride = streak,
        LapTimeMs = 10_000,
    };

    [Fact]
    public void A_muted_ghost_on_a_long_run_does_not_block_the_longest_streak_call()
    {
        var (announcer, said) = Announcer(AnnouncementEventType.StreakLead, "{driver} has the longest run going", announceGhosts: false);

        announcer.OnGatePassed(Pass(Ghost, 10));
        announcer.OnGatePassed(Pass("101", 5));

        // The per-lap batch waits 300ms before it decides.
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            lock (said)
            {
                if (said.Any(t => t.Contains("Sam has the longest run going", StringComparison.Ordinal)))
                {
                    break;
                }
            }

            Thread.Sleep(50);
        }

        lock (said)
        {
            Assert.Contains(said, t => t.Contains("Sam has the longest run going", StringComparison.Ordinal));
            Assert.DoesNotContain(said, t => t.Contains("Pace Ghost", StringComparison.Ordinal));
        }
    }

    private static RaceStanding Standing(string transponder, long fastLapMs) => new()
    {
        Transponder = transponder,
        Laps = 5,
        FastLapMs = fastLapMs,
        ElapsedMs = 60_000,
        Status = "active",
        Id = transponder,
    };

    [Fact]
    public void A_ghosts_quicker_lap_does_not_turn_the_races_fastest_lap_into_a_personal_best_call()
    {
        var (announcer, said) = Announcer(AnnouncementEventType.PersonalBestLap, "Personal best for {driver}", announceGhosts: true);

        announcer.OnStandingUpdated(Standing(Ghost, 9_000));
        announcer.OnStandingUpdated(Standing("101", 10_000));

        announcer.NotifyPersonalBestLap("101", 10_000);

        lock (said)
        {
            Assert.DoesNotContain(said, t => t.Contains("Personal best for Sam", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_personal_best_behind_a_quicker_real_driver_is_still_called()
    {
        var (announcer, said) = Announcer(AnnouncementEventType.PersonalBestLap, "Personal best for {driver}", announceGhosts: true);

        announcer.OnStandingUpdated(Standing(Ghost, 9_000));
        announcer.OnStandingUpdated(Standing("102", 9_500));
        announcer.OnStandingUpdated(Standing("101", 10_000));

        announcer.NotifyPersonalBestLap("101", 10_000);

        lock (said)
        {
            Assert.Contains(said, t => t.Contains("Personal best for Sam", StringComparison.Ordinal));
        }
    }
}
