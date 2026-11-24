namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, findings 17, 164 and 165: Forget this driver.
///
/// 17: it renamed every result ever run on one of their transponders,
/// whoever drove - so after a transponder changed hands, the old owner's
/// results became "Removed driver" for good - and missed the driver's own
/// results on a transponder they had since given up.
///
/// 164: a name taken out of a race's notes or a correction's reason was not
/// written to disk unless a result row was renamed too.
///
/// 165: it removed any ghost car whose name merely contained their name.
///
/// Real stores in a throwaway data folder; no window is shown.
/// </summary>
public sealed class BugHunt24_DriverForgetTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string Mia = "1234567";
    private const string MiaSpare = "7654321";
    private const string Ben = "2222222";
    private const string GivenUp = "8888888";
    private const string Sam = "5555555";
    private const string Samantha = "6666666";

    private static readonly string[] MiaIds = { Mia, MiaSpare };
    private static readonly string[] MiaNames = { "Mia Kent", "Mia" };

    private static SessionRecord Race(string notes, params SessionResultEntry[] rows) => new()
    {
        Label = "Junior A Main",
        StartedAtUtc = new DateTime(2026, 11, 1, 19, 0, 0, DateTimeKind.Utc),
        EndedAtUtc = new DateTime(2026, 11, 1, 19, 6, 0, DateTimeKind.Utc),
        Notes = notes,
        Results = rows.ToList(),
    };

    // --- Finding 17: whose result it is ----------------------------------

    [Fact]
    public void A_result_on_their_transponder_under_another_drivers_name_is_not_theirs()
    {
        var others = new[] { "Ben Kent", "Sam Roe" };

        Assert.False(DriverForget.IsTheirResult(MiaSpare, "Ben Kent", MiaIds, "Mia Kent", MiaNames, others));
        Assert.False(DriverForget.IsTheirResult(" " + MiaSpare + " ", "ben  kent", MiaIds, "Mia Kent", MiaNames, others));
    }

    [Fact]
    public void A_result_on_their_transponder_is_theirs_under_their_name_an_old_name_or_none()
    {
        var others = new[] { "Ben Kent", "Sam Roe" };

        Assert.True(DriverForget.IsTheirResult(MiaSpare, "Mia Kent", MiaIds, "Mia Kent", MiaNames, others));
        Assert.True(DriverForget.IsTheirResult(MiaSpare, "Mia", MiaIds, "Mia Kent", MiaNames, others));
        Assert.True(DriverForget.IsTheirResult(Mia, "", MiaIds, "Mia Kent", MiaNames, others));

        // The profile was renamed after the race: the old name is nobody
        // else's, so it still goes.
        Assert.True(DriverForget.IsTheirResult(Mia, "Mia Smith", MiaIds, "Mia Kent", MiaNames, others));

        // A namesake on file does not save a result filed under their own name.
        Assert.True(DriverForget.IsTheirResult(Mia, "Mia Kent", MiaIds, "Mia Kent", MiaNames, new[] { "Mia Kent" }));

        // Nothing known about anybody else: the transponder alone decides, as before.
        Assert.True(DriverForget.IsTheirResult(MiaSpare, "Ben Kent", MiaIds, "Mia Kent", MiaNames, null));
    }

    [Fact]
    public void A_result_under_their_name_on_a_transponder_they_gave_up_is_theirs_unless_a_namesake_is_on_file()
    {
        Assert.True(DriverForget.IsTheirResult(GivenUp, "Mia Kent", MiaIds, "Mia Kent", MiaNames, new[] { "Ben Kent" }));

        // Only the full driver name, never the short announcer name.
        Assert.False(DriverForget.IsTheirResult(GivenUp, "Mia", MiaIds, "Mia Kent", MiaNames, new[] { "Ben Kent" }));

        // Another Mia Kent is on file: it may be hers.
        Assert.False(DriverForget.IsTheirResult(GivenUp, "Mia Kent", MiaIds, "Mia Kent", MiaNames, new[] { "Mia Kent" }));

        // Nothing known about anybody else: left, as before.
        Assert.False(DriverForget.IsTheirResult(GivenUp, "Mia Kent", MiaIds, "Mia Kent", MiaNames, null));

        // Somebody else's result on somebody else's transponder.
        Assert.False(DriverForget.IsTheirResult(Sam, "Sam Roe", MiaIds, "Mia Kent", MiaNames, new[] { "Sam Roe" }));
    }

    [Fact]
    public void A_result_with_no_transponder_goes_by_their_driver_name_as_before()
    {
        Assert.True(DriverForget.IsTheirResult("", "mia kent", MiaIds, "Mia Kent", MiaNames, null));
        Assert.False(DriverForget.IsTheirResult(null, "Mia", MiaIds, "Mia Kent", MiaNames, null));
        Assert.False(DriverForget.IsTheirResult("", "Ben Kent", MiaIds, "Mia Kent", MiaNames, null));
    }

    [Fact]
    public void Forget_leaves_results_another_driver_ran_on_a_transponder_that_is_now_theirs()
    {
        var profiles = ProfileStore.Load();
        var history = SessionHistoryStore.Load();

        // The spare was Ben's last season and was transferred to Mia.
        profiles.Upsert(new DriverProfile
        {
            Transponder = Mia,
            DriverName = "Mia Kent",
            Transponders = new List<OwnedTransponder> { new() { Id = MiaSpare } },
        });
        profiles.Upsert(new DriverProfile { Transponder = Ben, DriverName = "Ben Kent" });

        history.Add(Race(
            string.Empty,
            new SessionResultEntry { Position = 1, Transponder = MiaSpare, DriverName = "Ben Kent", Laps = 20 },
            new SessionResultEntry { Position = 2, Transponder = MiaSpare, DriverName = "Mia Kent", Laps = 19 },
            new SessionResultEntry { Position = 3, Transponder = GivenUp, DriverName = "Mia Kent", Laps = 18 },
            new SessionResultEntry { Position = 4, Transponder = Ben, DriverName = "Ben Kent", Laps = 17 }));

        var race = history.All.Single();
        race.Edits.Add(new ResultEdit { Transponder = MiaSpare, DriverName = "Ben Kent", Change = "laps 19 -> 20", Reason = "missed lap" });
        race.Edits.Add(new ResultEdit { Transponder = MiaSpare, DriverName = "Mia Kent", Change = "laps 18 -> 19", Reason = "missed lap" });
        history.SaveChanges();

        var outcome = DriverForget.Forget(profiles.Get(Mia)!, new DriverDataSources { Profiles = profiles, History = history });

        Assert.Equal(
            new[] { "Ben Kent", "Removed driver", "Removed driver", "Ben Kent" },
            history.All.Single().Results.Select(r => r.DriverName));
        Assert.Equal(new[] { "Ben Kent", "Removed driver" }, history.All.Single().Edits.Select(e => e.DriverName));

        // Said, and counted truthfully.
        Assert.Contains(outcome.Lines, l => l.StartsWith("2 results renamed", StringComparison.Ordinal));
        Assert.Contains(outcome.Lines, l => l.Contains("another driver's name", StringComparison.Ordinal));

        // The transponder she no longer holds is handed back for the republish.
        Assert.Equal(new[] { GivenUp }, outcome.AlsoRepublish);

        // On disk too.
        var saved = File.ReadAllText(_data.PathOf("race-history.json"));
        Assert.DoesNotContain("Mia Kent", saved);
        Assert.Contains("Ben Kent", saved);
    }

    // --- Finding 164: notes and reasons are saved -------------------------

    [Fact]
    public void Forget_saves_a_name_taken_out_of_notes_and_reasons_when_the_driver_has_no_results()
    {
        var profiles = ProfileStore.Load();
        var history = SessionHistoryStore.Load();

        profiles.Upsert(new DriverProfile { Transponder = Mia, DriverName = "Mia Kent" });
        profiles.Upsert(new DriverProfile { Transponder = Sam, DriverName = "Sam Roe" });

        // Mia only marshalled: no result row is hers.
        history.Add(Race(
            "Mia Kent marshalled turn 3",
            new SessionResultEntry { Position = 1, Transponder = Sam, DriverName = "Sam Roe", Laps = 20 }));
        history.All.Single().Edits.Add(new ResultEdit { Transponder = Sam, DriverName = "Sam Roe", Change = "laps 19 -> 20", Reason = "Mia Kent saw the missed lap" });
        history.SaveChanges();
        Assert.Contains("Mia Kent", File.ReadAllText(_data.PathOf("race-history.json")));

        var outcome = DriverForget.Forget(profiles.Get(Mia)!, new DriverDataSources { Profiles = profiles, History = history });

        var saved = File.ReadAllText(_data.PathOf("race-history.json"));
        Assert.DoesNotContain("Mia Kent", saved);
        Assert.Contains("Removed driver marshalled turn 3", saved);
        Assert.Contains("Removed driver saw the missed lap", saved);

        // And a fresh load - the next time the app opens - agrees.
        var reloaded = SessionHistoryStore.Load().All.Single();
        Assert.Equal("Removed driver marshalled turn 3", reloaded.Notes);
        Assert.Equal("Sam Roe", reloaded.Results.Single().DriverName);

        // The write worked, so nothing warns.
        Assert.DoesNotContain(outcome.Lines, l => l.Contains("NOT saved", StringComparison.Ordinal));
        Assert.Empty(outcome.AlsoRepublish);
    }

    // --- Finding 165: ghost cars -------------------------------------------

    [Fact]
    public void A_ghost_is_theirs_by_the_transponder_it_chases_or_by_their_name_as_a_whole_word_when_it_chases_nobody()
    {
        var ids = new[] { Sam };
        var names = new[] { "Sam Roe", "Sam" };

        Assert.True(DriverForget.IsTheirGhost(Sam, "Chasing him", ids, names));
        Assert.True(DriverForget.IsTheirGhost("", "Sam's pace", ids, names));
        Assert.True(DriverForget.IsTheirGhost(null, "sam roe at his best", ids, names));

        // Another driver's ghost, whatever it is called.
        Assert.False(DriverForget.IsTheirGhost(Samantha, "Samantha Lee's ghost", ids, names));
        Assert.False(DriverForget.IsTheirGhost(Samantha, "Sam's rival", ids, names));

        // Their name inside a longer word is not their name.
        Assert.False(DriverForget.IsTheirGhost("", "Samantha's pace", ids, names));
        Assert.False(DriverForget.IsTheirGhost("", "Pace car", ids, names));
    }

    [Fact]
    public void Forget_leaves_another_drivers_ghost_whose_name_contains_theirs()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = Sam, DriverName = "Sam Roe", AnnouncerName = "Sam" });
        profiles.Upsert(new DriverProfile { Transponder = Samantha, DriverName = "Samantha Lee" });

        var ghosts = GhostStore.Load();
        ghosts.Current.Ghosts.Clear();
        ghosts.Current.Ghosts.Add(new GhostCar { Name = "Samantha Lee's ghost", PaceSource = GhostPaceSource.DriverPersonalBest, SourceTransponder = Samantha });
        ghosts.Current.Ghosts.Add(new GhostCar { Name = "Sam's best", PaceSource = GhostPaceSource.DriverPersonalBest, SourceTransponder = Sam });
        ghosts.Current.Ghosts.Add(new GhostCar { Name = "Sam's pace" });
        ghosts.Current.Ghosts.Add(new GhostCar { Name = "Samantha's pace" });
        ghosts.Save();

        var outcome = DriverForget.Forget(profiles.Get(Sam)!, new DriverDataSources { Profiles = profiles, Ghosts = ghosts });

        Assert.Equal(new[] { "Samantha Lee's ghost", "Samantha's pace" }, GhostStore.Load().Current.Ghosts.Select(g => g.Name));
        Assert.Contains(outcome.Lines, l => l.StartsWith("2 ghost cars", StringComparison.Ordinal));
    }
}
