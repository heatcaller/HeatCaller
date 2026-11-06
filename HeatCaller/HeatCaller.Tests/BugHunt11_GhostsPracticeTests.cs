namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11, ghosts and practice (5 Nov 2026).
///
/// WHY. ghosts.json held by a backup tool at launch loaded empty, and the
/// startup EnsureGhostProfiles then deleted every ghost's driver profile as
/// an orphan. Undoing a Chase a Best Lap restored the whole garage, putting
/// back ghosts unticked since. And a ghost's pass call was measured against
/// whichever real car led, so a change of leader was called as a pass.
/// </summary>
public sealed class BugHunt11_GhostsPracticeTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- ghosts.json locked at startup ------------------------------------

    [Fact]
    public void A_ghost_store_locked_at_startup_leaves_the_ghost_profiles_alone()
    {
        var club = GhostStore.Load();
        var ghost = new GhostCar { Name = "Rival", CarModel = "Buggy" };
        club.Current.Ghosts.Add(ghost);
        club.Save();

        var profiles = ProfileStore.Load();
        Assert.True(MainForm.SyncGhostProfiles(club, profiles));
        Assert.NotNull(profiles.Get(ghost.Transponder));

        GhostStore tonight;
        using (new FileStream(_data.PathOf("ghosts.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            tonight = GhostStore.Load();
        }

        Assert.True(tonight.LoadedUnreadable);
        Assert.Empty(tonight.Current.Ghosts);

        var startup = ProfileStore.Load();
        Assert.False(MainForm.SyncGhostProfiles(tonight, startup));

        Assert.NotNull(startup.Get(ghost.Transponder));
        Assert.NotNull(ProfileStore.Load().Get(ghost.Transponder));
    }

    [Fact]
    public void A_readable_ghost_store_still_prunes_a_deleted_ghosts_profile()
    {
        var club = GhostStore.Load();
        var ghost = new GhostCar { Name = "Rival" };
        club.Current.Ghosts.Add(ghost);
        club.Save();

        var profiles = ProfileStore.Load();
        MainForm.SyncGhostProfiles(club, profiles);

        club.Current.Ghosts.Clear();
        Assert.True(MainForm.SyncGhostProfiles(club, profiles));

        Assert.Null(ProfileStore.Load().Get(ghost.Transponder));
    }

    // --- Chase a Best Lap's Undo ------------------------------------------

    [Fact]
    public void Undoing_a_chase_keeps_a_ghost_unticked_after_it()
    {
        var settings = new GhostSettings { Enabled = true };
        var rival = new GhostCar { Name = "Rival", Enabled = true };
        settings.Ghosts.Add(rival);

        var (chased, refreshed, undo) = OwnBestGhost.ChaseWithUndo(settings, "1234", "Sam", "Buggy");
        Assert.False(refreshed);

        // Unticked on the Practice page - no undo of its own.
        rival.Enabled = false;

        undo(settings);

        Assert.DoesNotContain(settings.Ghosts, g => g.Id == chased.Id);
        Assert.False(settings.Ghosts.Single(g => g.Id == rival.Id).Enabled);
        Assert.True(settings.Enabled);
    }

    [Fact]
    public void Undoing_a_chase_turns_ghost_racing_back_off_only_if_the_chase_turned_it_on()
    {
        var settings = new GhostSettings { Enabled = false };

        var (_, _, undo) = OwnBestGhost.ChaseWithUndo(settings, "1234", "Sam", null);
        Assert.True(settings.Enabled);

        undo(settings);

        Assert.False(settings.Enabled);
        Assert.Empty(settings.Ghosts);
    }

    [Fact]
    public void Undoing_a_refresh_puts_back_only_that_ghosts_pace_and_switch()
    {
        var settings = new GhostSettings { Enabled = true };
        var (sams, _, _) = OwnBestGhost.ChaseWithUndo(settings, "1234", "Sam", null);
        sams.Enabled = false;
        var rival = new GhostCar { Name = "Rival", Enabled = true };
        settings.Ghosts.Add(rival);
        var revision = sams.PaceRevision;

        var (again, refreshed, undo) = OwnBestGhost.ChaseWithUndo(settings, "1234", "Sam", null);
        Assert.True(refreshed);
        Assert.Same(sams, again);
        Assert.True(sams.Enabled);

        rival.Enabled = false;

        // A later save or Undo can replace the settings object: the undo
        // works on whatever is current, by Id.
        var now = System.Text.Json.JsonSerializer.Deserialize<GhostSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        undo(now);

        var samsNow = now.Ghosts.Single(g => g.Id == sams.Id);
        Assert.Equal(revision, samsNow.PaceRevision);
        Assert.False(samsNow.Enabled);
        Assert.False(now.Ghosts.Single(g => g.Id == rival.Id).Enabled);
        Assert.True(now.Enabled);
    }

    // --- Ghost pass calls -------------------------------------------------

    [Fact]
    public void A_change_of_leading_real_car_is_not_called_as_a_pass()
    {
        var state = new Dictionary<string, (string Human, bool Ahead)>(StringComparer.OrdinalIgnoreCase);

        // A, G, B: the ghost is behind A.
        Assert.Null(MainForm.GhostPassCall(state, "GHOST-G", "A", ghostAhead: false));

        // A breaks down: G, B, A. The leading real car is now B, and the
        // ghost is ahead of it - but B never swapped places with it.
        Assert.Null(MainForm.GhostPassCall(state, "GHOST-G", "B", ghostAhead: true));
    }

    [Fact]
    public void A_real_swap_with_the_same_car_is_still_called()
    {
        var state = new Dictionary<string, (string Human, bool Ahead)>(StringComparer.OrdinalIgnoreCase);

        Assert.Null(MainForm.GhostPassCall(state, "GHOST-G", "A", ghostAhead: false));
        Assert.Null(MainForm.GhostPassCall(state, "GHOST-G", "A", ghostAhead: false));
        Assert.False(MainForm.GhostPassCall(state, "GHOST-G", "A", ghostAhead: true));
        Assert.True(MainForm.GhostPassCall(state, "GHOST-G", "A", ghostAhead: false));
    }
}
