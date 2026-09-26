using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// MainForm's ghost cars: running them beside a race, reacting to the ghost
/// store, and the profiles they race under.
///
/// Moved here unchanged from MainForm.cs on 26 Sep 2026, when the file was
/// split into parts. Fields with initializers stayed in MainForm.cs.
/// </summary>
public partial class MainForm
{
    /// <summary>
    /// Starts and stops virtual opponents with the race.
    ///
    /// Ghosts get real driver profiles created for them, so every existing
    /// path - the leaderboard, announcements, the big screen - names them
    /// properly without knowing they aren't real.
    /// </summary>
    private void HandleGhosts(RaceState state)
    {
        _ghosts ??= CreateGhostService();

        if (state == RaceState.Racing && _ghostStore.Current.Enabled)
        {
            EnsureGhostProfiles();
            _ghosts.Settings = _ghostStore.Current;

            // A demo runs only the ghosts ticked for it on the Demo Mode
            // page (none, unless some were); a real race runs them all.
            var onlyThese = _race.IsDemoMode
                ? _demoControl?.SelectedGhosts ?? Array.Empty<string>()
                : null;

            _ghosts.Start(cmbTrackLayout.SelectedItem as string, onlyThese);
        }
        else if (state is RaceState.Idle or RaceState.Finished or RaceState.Staging or RaceState.Countdown)
        {
            // Staging and Countdown too: a race staged straight over one
            // still running (or over a demo) never passes through Idle, so
            // its ghosts kept lapping through the new start. They start
            // afresh, with the new race's track and pace, at its green.
            _ghosts.Stop();

            // Or the next race would open by announcing a pass that
            // happened in the last one.
            _ghostWasAhead.Clear();
        }
    }

    /// <summary>
    /// Ghost profiles kept in step as ghosts are added, edited and deleted -
    /// so a new ghost is immediately pickable everywhere.
    ///
    /// The WHOLE of it on the UI thread. A save from another thread used to
    /// hop across for the profiles only, and skip the picker refreshes that
    /// were the reason for the handler - so a ghost saved that way stayed
    /// missing from every list until the page was left and re-entered.
    /// </summary>
    private void OnGhostStoreSaved()
    {
        if (InvokeRequired)
        {
            TryPost(OnGhostStoreSaved);
            return;
        }

        EnsureGhostProfiles();

        // Rebuild the pickers immediately. They cache their driver list
        // when built and only rebuild on navigation, so a ghost added
        // while another page was open wouldn't appear until that page
        // was left and re-entered.
        _practiceControl?.RefreshGhosts();

        foreach (var refresh in _navRefreshHooks.Values)
        {
            try
            {
                refresh();
            }
            catch
            {
                // One page failing to refresh must not stop the others.
            }
        }
    }

    private GhostService CreateGhostService()
    {
        // The track layouts feed the track-record ghost; the personal-best
        // resets keep a PB ghost chasing the same number the Personal Bests
        // page shows rather than a lap that page has retired.
        var service = new GhostService(_race, _profiles, _sessionHistory, _trackLayouts, _pbResets);
        service.StatusMessage += OnClientRawMessage;

        // Position changes against a ghost are the whole point of racing
        // one - being told you've been passed is what makes it an opponent
        // rather than a number on a screen.
        service.GhostLapCompleted += (_, _) => CheckGhostPositions();

        return service;
    }

    /// <summary>Who is ahead of whom, last time we looked.</summary>
    private readonly Dictionary<string, bool> _ghostWasAhead = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Announces when a real driver and a ghost swap places.
    ///
    /// Compares against the LAST known state rather than announcing on
    /// every lap, so a steady gap stays quiet and only an actual change
    /// gets called - the same rule a human commentator follows.
    /// </summary>
    private void CheckGhostPositions()
    {
        if (!_ghostStore.Current.AnnounceGhosts || _race.State != RaceState.Racing)
        {
            return;
        }

        var standings = _race.GetOrderedStandings();

        if (standings.Count < 2)
        {
            return;
        }

        // RacerStanding uses TransponderId, not Transponder - a different
        // type from RaceStanding, which is what the announcer takes.
        var human = standings.FirstOrDefault(s => !GhostService.IsGhost(s.TransponderId));

        if (human is null)
        {
            return;
        }

        var humanIndex = standings.ToList().IndexOf(human);

        foreach (var ghost in standings.Where(s => GhostService.IsGhost(s.TransponderId)))
        {
            var ghostAhead = standings.ToList().IndexOf(ghost) < humanIndex;

            if (_ghostWasAhead.TryGetValue(ghost.TransponderId, out var wasAhead) && wasAhead != ghostAhead)
            {
                _announcer.NotifyGhostPass(
                    human.TransponderId,
                    ghost.TransponderId,
                    humanPassedGhost: !ghostAhead);
            }

            _ghostWasAhead[ghost.TransponderId] = ghostAhead;
        }
    }

    /// <summary>
    /// Gives each ghost a driver profile, so it shows a name rather than a
    /// raw transponder everywhere a driver is displayed.
    ///
    /// Marked ExcludeFromRecords - a virtual car must never take a club's
    /// all-time record, which would be both wrong and very annoying.
    /// </summary>
    /// <summary>
    /// Creates or updates a driver profile for every ghost, and removes
    /// profiles for ghosts that no longer exist.
    ///
    /// Runs on every ghost save, not just at race start - a ghost has to
    /// have a profile before it can be picked in a roster or demo mode,
    /// and it was only getting one after a race had already run.
    /// </summary>
    private void EnsureGhostProfiles()
    {
        // Ghosts that have been deleted or renamed leave orphan profiles
        // behind, which would keep appearing in every picker.
        var liveTransponders = _ghostStore.Current.Ghosts
            .Select(g => g.Transponder)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var orphans = _profiles.AllIncludingGhosts
            .Where(p => GhostService.IsGhost(p.Transponder) && !liveTransponders.Contains(p.Transponder))
            .Select(p => p.Transponder)
            .ToList();

        foreach (var orphan in orphans)
        {
            _profiles.Delete(orphan);
        }

        // Every ghost gets a profile, enabled or not - a disabled ghost
        // should still be selectable, it just won't run until switched on.
        foreach (var ghost in _ghostStore.Current.Ghosts)
        {
            var existing = _profiles.Get(ghost.Transponder);

            if (existing is not null
                && existing.DriverName == ghost.Name
                && existing.CarModel == ghost.CarModel)
            {
                continue;
            }

            _profiles.Upsert(new DriverProfile
            {
                Transponder = ghost.Transponder,
                DriverName = ghost.Name,
                CarModel = ghost.CarModel,
                ExcludeFromRecords = true,
                Notes = "Virtual opponent (ghost car).",
            });
        }

        _profiles.Save();

        // Reports what actually happened. Three attempts at making ghosts
        // appear in the pickers have now failed, so this says plainly how
        // many ghosts exist, how many profiles they have, and how many the
        // pickers will therefore show.
        var ghostCount = _ghostStore.Current.Ghosts.Count;
        var ghostProfiles = _profiles.AllIncludingGhosts.Count(p => GhostService.IsGhost(p.Transponder));
        var raceable = _profiles.AllRaceable.Count;

        OnClientRawMessage(
            $"{AppTime.LogStampNow()}  [ghost] {Plural.Of(ghostCount, "ghost")} configured, "
            + $"{Plural.Of(ghostProfiles, "ghost profile")}, {Plural.Of(raceable, "selectable driver")} total.");
    }
}
