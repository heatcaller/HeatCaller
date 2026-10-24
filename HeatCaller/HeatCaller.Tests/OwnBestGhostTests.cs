namespace HeatCaller.Tests;

/// <summary>
/// Chase your own ghost (22 Oct 2026): a ghost at the driver's own best lap
/// - tonight's on this layout, or their best ever on it before tonight's -
/// fixed for the run, brought up to a new best when asked, and a ghost like
/// any other everywhere ghosts are kept out. See OwnBestGhost.
/// </summary>
public sealed class OwnBestGhostTests
{
    private const string Driver = "101";

    [Fact]
    public void The_new_pace_source_is_appended_so_saved_ghosts_keep_theirs()
    {
        // Ghost sources are saved as numbers - inserting one would move every
        // saved ghost onto the wrong source.
        Assert.Equal(8, (int)GhostPaceSource.OwnBestTonight);
        Assert.Equal(GhostPaceSource.OwnBestTonight, Enum.GetValues<GhostPaceSource>().Last());
    }

    [Theory]
    [InlineData(9_400L, 9_600L, 9_000, 9_400)] // tonight's quicker of the two, never the old best
    [InlineData(null, 9_600L, 9_000, 9_600)]   // saved earlier tonight only
    [InlineData(9_700L, null, 9_000, 9_700)]   // this session only
    [InlineData(null, null, 9_000, 9_000)]     // nothing tonight yet: the best ever
    public void Tonights_best_comes_first_then_the_best_ever(long? runningNow, long? savedTonight, int ever, int expected)
    {
        Assert.Equal(expected, GhostService.OwnBestTonightMs(runningNow, savedTonight, ever));
    }

    [Fact]
    public void A_driver_with_no_lap_anywhere_has_nothing_to_chase()
    {
        Assert.Null(GhostService.OwnBestTonightMs(null, null, null));
        Assert.Null(GhostService.OwnBestTonightMs(0, 0, 0));
    }

    [Fact]
    public void Chasing_a_driver_adds_their_best_as_a_ghost_and_switches_ghosts_on()
    {
        var settings = new GhostSettings { Enabled = false };

        var (ghost, refreshed) = OwnBestGhost.ChaseFor(settings, Driver, "Sam", "MR-03");

        Assert.False(refreshed);
        Assert.True(settings.Enabled);
        Assert.Same(ghost, Assert.Single(settings.Ghosts));
        Assert.Equal("Sam's best", ghost.Name);
        Assert.Equal("MR-03", ghost.CarModel);
        Assert.Equal(GhostPaceSource.OwnBestTonight, ghost.PaceSource);
        Assert.Equal(Driver, ghost.SourceTransponder);
        Assert.Equal(100, ghost.PacePercent);
        Assert.True(ghost.Enabled);
        Assert.False(ghost.IsLivePace);
        Assert.True(GhostService.IsGhost(ghost.Transponder));
    }

    [Fact]
    public void Chasing_the_same_driver_again_brings_their_ghost_up_to_date_instead_of_adding_another()
    {
        var settings = new GhostSettings();
        var (first, _) = OwnBestGhost.ChaseFor(settings, Driver, "Sam", null);
        first.Enabled = false;

        var (again, refreshed) = OwnBestGhost.ChaseFor(settings, Driver, "Sam", null);

        Assert.True(refreshed);
        Assert.Same(first, again);
        Assert.Single(settings.Ghosts);
        Assert.Equal(1, again.PaceRevision);
        Assert.True(again.Enabled);
    }

    [Fact]
    public void A_driver_with_no_name_is_named_by_transponder()
    {
        Assert.Equal("101's best", OwnBestGhost.NameFor("  ", Driver));
        Assert.Equal("Sam's best", OwnBestGhost.NameFor(" Sam ", Driver));
    }

    [Fact]
    public void The_drivers_on_the_board_are_offered_first_and_ghosts_never()
    {
        var profiles = new[]
        {
            new DriverProfile { Transponder = "200", DriverName = "Bill" },
            new DriverProfile { Transponder = $"{Ghosts.Prefix}ABCDEF", DriverName = "Someone's ghost" },
            new DriverProfile { Transponder = Driver, DriverName = "Sam" },
        };

        var offered = OwnBestGhost.Candidates(profiles, new[] { Driver });

        Assert.Equal(new[] { Driver, "200" }, offered.Select(p => p.Transponder));
    }

    [Fact]
    public void A_ghost_of_tonights_best_runs_at_tonights_best_not_an_older_quicker_lap()
    {
        var said = StartWith(
            Session(DateTime.UtcNow.AddMinutes(-2), 9_500),
            Session(DateTime.UtcNow.AddYears(-1), 9_000));

        Assert.Contains(said, line => line.Contains("Sam's best on track at 9.500s", StringComparison.Ordinal));
    }

    [Fact]
    public void Before_a_lap_tonight_it_runs_at_the_best_ever()
    {
        var said = StartWith(Session(DateTime.UtcNow.AddYears(-1), 9_000));

        Assert.Contains(said, line => line.Contains("Sam's best on track at 9.000s", StringComparison.Ordinal));
    }

    [Fact]
    public void With_no_lap_at_all_it_says_why_it_did_not_start()
    {
        var said = StartWith();

        Assert.Contains(said, line => line.Contains("Sam's best not started", StringComparison.Ordinal));
    }

    [Fact]
    public void Nothing_is_re_paced_while_the_ghosts_are_not_running()
    {
        var ghosts = new GhostService(new RaceTiming(new RaceControlSession()), ProfileStore.Load(),
            SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>()));

        Assert.Equal(0, ghosts.RefreshChangedPaces());
    }

    [Fact]
    public void A_running_ghost_is_re_paced_from_the_settings_as_they_are_now()
    {
        // An Undo (GhostStore.Restore) or an edit hands the service NEW ghost
        // objects. The running ghost keeps the one it started with, so its
        // revision has to be read from the current settings, by transponder.
        var settings = new GhostSettings();
        var (started, _) = OwnBestGhost.ChaseFor(settings, Driver, "Sam", null);

        var ghosts = new GhostService(new RaceTiming(new RaceControlSession()), ProfileStore.Load(),
            SessionHistoryStore.FromSessions(new[] { Session(DateTime.UtcNow.AddMinutes(-2), 9_500) }))
        {
            Settings = settings,
        };

        try
        {
            ghosts.Start();
            Assert.True(ghosts.IsRunning);

            // Nothing asked for: nothing moves.
            Assert.Equal(0, ghosts.RefreshChangedPaces());

            // The settings replaced, as a restore does, with the revision moved
            // on the new copy only.
            var replaced = new GhostSettings
            {
                Enabled = true,
                Ghosts =
                {
                    new GhostCar
                    {
                        Id = started.Id,
                        Transponder = started.Transponder,
                        Name = started.Name,
                        PaceSource = GhostPaceSource.OwnBestTonight,
                        SourceTransponder = Driver,
                        PaceRevision = started.PaceRevision + 1,
                    },
                },
            };

            ghosts.Settings = replaced;

            Assert.Equal(1, ghosts.RefreshChangedPaces());
            Assert.Equal(0, ghosts.RefreshChangedPaces());
        }
        finally
        {
            ghosts.Stop();
        }
    }

    [Fact]
    public void Editing_an_own_best_ghost_keeps_its_revision()
    {
        // The edit window works on a copy; a copy that dropped the revision
        // would look like a request to re-read the running ghost's pace.
        var existing = new GhostCar
        {
            Name = "Sam's best",
            PaceSource = GhostPaceSource.OwnBestTonight,
            SourceTransponder = Driver,
            PaceRevision = 3,
        };

        var revision = StaThread.Run(() =>
        {
            using var form = new GhostEditForm(existing, ProfileStore.Load());
            return form.Result.PaceRevision;
        }, TimeSpan.FromMinutes(1), () => "own-best ghost, edit window");

        Assert.Equal(3, revision);
    }

    [Fact]
    public void Beating_your_own_best_ghost_still_counts_for_Ghost_Buster()
    {
        // The badge's rule names the own-best sources; the new one is one.
        var source = File.ReadAllText(SourcePath("AchievementEvaluator.Home.cs"));

        Assert.Contains("GhostPaceSource.OwnBestTonight", source, StringComparison.Ordinal);
    }

    private static List<string> StartWith(params SessionRecord[] sessions)
    {
        var settings = new GhostSettings();
        OwnBestGhost.ChaseFor(settings, Driver, "Sam", null);

        var ghosts = new GhostService(new RaceTiming(new RaceControlSession()), ProfileStore.Load(), SessionHistoryStore.FromSessions(sessions))
        {
            Settings = settings,
        };

        var said = new List<string>();
        ghosts.StatusMessage += said.Add;

        ghosts.Start();
        ghosts.Stop();

        return said;
    }

    private static SessionRecord Session(DateTime startedUtc, long fastLapMs) => new()
    {
        SessionType = RaceSessionType.Practice,
        StartedAtUtc = startedUtc,
        Results =
        {
            new SessionResultEntry
            {
                Transponder = Driver,
                Laps = 5,
                FastLapMs = fastLapMs,
                LapTimesMs = Enumerable.Repeat(fastLapMs, 5).ToList(),
                ElapsedMs = fastLapMs * 5,
            },
        },
    };

    private static string SourcePath(string fileName)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null)
        {
            if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
            {
                return Path.Combine(folder.FullName, "HeatCaller", "HeatCaller", fileName);
            }

            folder = folder.Parent;
        }

        throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory);
    }
}
