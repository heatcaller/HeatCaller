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

            // The track of whatever is actually running. Practice and a
            // demo each pick their own track on their own page, and the
            // practice is saved under the Practice page's one - so giving
            // their ghosts Race Control's track (often another layout, or
            // "(none)") paced a PB or track-record ghost from the wrong
            // track's laps, or skipped it as having no PB there. Practice
            // is checked first: it is the one most ghosts run in.
            var trackName = RunningTrackName(cmbTrackLayout.SelectedItem as string);

            _ghostsOnlyThese = onlyThese;
            _ghostsStartedFor = GhostsWanted(onlyThese);

            _ghosts.Start(trackName, onlyThese);
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

            // Forgotten with the race. A practice started with ghosts off
            // never sets them, so the last race's set was left standing,
            // and switching on the very ghosts it named looked like no
            // change and started nothing (see KeepRunningGhostsInStep).
            _ghostsStartedFor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _ghostsOnlyThese = null;
        }
    }

    /// <summary>
    /// The track of whatever is actually running: the Practice page's in
    /// practice, the Demo Mode page's in a demo, otherwise
    /// <paramref name="raceControlTrack"/>. One choice for the ghosts, the
    /// speed columns and the track map, so they can't drift apart - the
    /// speed columns and the map went on reading Race Control's layout in
    /// practice after the ghosts had moved to the Practice page's.
    /// </summary>
    private string? RunningTrackName(string? raceControlTrack)
    {
        var practice = _race.IsPracticeMode && _practiceControl is not null;
        var demo = _race.IsDemoMode && _demoControl is not null;

        return RunningTrackName(practice, demo,
            practice ? _practiceControl!.SelectedTrackName : null,
            demo ? _demoControl!.SelectedTrackName : null,
            raceControlTrack);
    }

    /// <summary>RunningTrackName's choice on its own, for testing. Practice first: it is the one most ghosts run in.</summary>
    internal static string? RunningTrackName(bool practice, bool demo, string? practiceTrack, string? demoTrack, string? raceControlTrack) =>
        practice ? practiceTrack : demo ? demoTrack : raceControlTrack;

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

        KeepRunningGhostsInStep();

        // An own-best ghost asked to chase a new best (the Practice page's
        // "Chase a Best Lap..." picked again for the same driver) takes it
        // from its next lap, without going back to the line (22 Oct 2026).
        //
        // The service is handed the settings as they are now first: an Undo
        // or an edit replaces them, and it finds each running ghost's
        // current revision there. Start reads Settings afresh every time,
        // so nothing else depends on it keeping the old object.
        if (_ghosts is not null)
        {
            _ghosts.Settings = _ghostStore.Current;
            _ghosts.RefreshChangedPaces();
        }

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

    /// <summary>The demo's ghost selection the running ghosts were started with; null for a real race.</summary>
    private IReadOnlyCollection<string>? _ghostsOnlyThese;

    /// <summary>Which ghosts were switched on when the running ones were started, by transponder.</summary>
    private HashSet<string> _ghostsStartedFor = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The ghosts the switches say should run: on, and (in a demo) ticked for it.</summary>
    private HashSet<string> GhostsWanted(IReadOnlyCollection<string>? onlyThese) =>
        _ghostStore.Current.Enabled
            ? _ghostStore.Current.Ghosts
                .Where(g => g.Enabled
                    && (onlyThese is null || onlyThese.Contains(g.Transponder, StringComparer.OrdinalIgnoreCase)))
                .Select(g => g.Transponder)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Ghosts switched off (or on) while they are racing are taken off (or
    /// put on) track there and then.
    ///
    /// WHY. The ghosts were only ever started and stopped with the race, and
    /// the service reads the switches only when it starts - so unticking
    /// "Enable ghost cars", a ghost's On/Off button or the Practice page's
    /// "Race ghost cars" mid-race changed the page to say the ghosts were
    /// off while they went on lapping, on every board and called by the PA,
    /// until the race ended. Now a save that changes which ghosts should be
    /// running stops them all, or restarts them - and a restart in a running
    /// race carries each remaining ghost on from its last crossing (see
    /// GhostService.Start), so they do not jump back to the line. A save
    /// that changes nothing about who runs (a ghost renamed, its pace
    /// nudged) leaves them alone. In a race, ghosts that were not running
    /// are left for the next green, as before.
    ///
    /// Practice has no next green - only Stop and Start, which ends and
    /// saves the session, or Clear Stats - so there ghosts switched on with
    /// none running are put on track straight away, as the Practice page
    /// says they will be. They used to wait for a green that never came.
    ///
    /// A ghost switched off leaves the board too (RemoveGhostStanding). Its
    /// frozen row used to stay there - see the session's summary of it.
    /// </summary>
    private void KeepRunningGhostsInStep()
    {
        if (_ghosts is null)
        {
            return;
        }

        var wanted = GhostsWanted(_ghostsOnlyThese);
        var change = GhostSwitchChangeFor(_race.State == RaceState.Racing, _race.IsPracticeMode, _ghosts.IsRunning, wanted, _ghostsStartedFor);

        if (change == GhostSwitchChange.None)
        {
            return;
        }

        var switchedOff = _ghostsStartedFor.Where(t => !wanted.Contains(t)).ToList();
        NoteGhostsSwitchedOn(wanted.Where(t => !_ghostsStartedFor.Contains(t)));

        if (change == GhostSwitchChange.StopAll)
        {
            _ghosts.Stop();
            _ghostWasAhead.Clear();
            _ghostsStartedFor = wanted;
            OnClientRawMessage("[ghost] Ghost cars switched off mid-race - taken off track.");
        }
        else
        {
            OnClientRawMessage(_ghosts.IsRunning
                ? "[ghost] Ghost cars changed mid-race - restarting the ones still switched on."
                : "[ghost] Ghost cars switched on mid-session - putting them on track.");
            HandleGhosts(RaceState.Racing);
        }

        // After the restart, not before: taking the last car the finish was
        // waiting on off the board ends the race, and the ghosts are stopped
        // with it - a restart after that would set them lapping in a race
        // that is over.
        foreach (var transponder in switchedOff)
        {
            _ghostWasAhead.Remove(transponder);
            _race.RemoveGhostStanding(transponder);
        }
    }

    /// <summary>What a save of the ghost switches does to the ghosts while a session runs - see KeepRunningGhostsInStep.</summary>
    internal enum GhostSwitchChange
    {
        /// <summary>Nothing about who should be running has changed, or nothing is to be done about it now.</summary>
        None,

        /// <summary>Every ghost is switched off: all taken off track.</summary>
        StopAll,

        /// <summary>Some were switched on or off: the ones still on are (re)started.</summary>
        Restart,
    }

    /// <summary>KeepRunningGhostsInStep's decision, on its own for testing.</summary>
    internal static GhostSwitchChange GhostSwitchChangeFor(bool racing, bool practice, bool ghostsRunning,
        IReadOnlySet<string> wanted, IReadOnlySet<string> startedFor)
    {
        // A race keeps "while ghosts are racing": with none running, the
        // ones switched on wait for the next green. Practice has none.
        if (!racing || (!ghostsRunning && !practice) || wanted.SetEquals(startedFor))
        {
            return GhostSwitchChange.None;
        }

        return wanted.Count == 0 ? GhostSwitchChange.StopAll : GhostSwitchChange.Restart;
    }

    /// <summary>
    /// Ghosts switched on after the green of the race now running, and that
    /// race's SessionGeneration - for the badges (GhostsSwitchedOnMidRace).
    /// </summary>
    private (int Generation, HashSet<string> Ghosts) _ghostsSwitchedOnMidRace = (-1, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private void NoteGhostsSwitchedOn(IEnumerable<string> transponders)
    {
        if (!_race.IsSameRace(_ghostsSwitchedOnMidRace.Generation))
        {
            _ghostsSwitchedOnMidRace = (_race.SessionGeneration, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        _ghostsSwitchedOnMidRace.Ghosts.UnionWith(transponders);
    }

    /// <summary>
    /// The ghosts that joined the race now on the board part-way through.
    /// One joins on no laps, so every driver was "ahead" of it at the flag
    /// and was credited with beating it (Ghost Buster, Shadow Boxer) without
    /// ever racing it. The badges leave these out.
    /// </summary>
    private IReadOnlyCollection<string> GhostsSwitchedOnMidRace() =>
        _race.IsSameRace(_ghostsSwitchedOnMidRace.Generation)
            ? _ghostsSwitchedOnMidRace.Ghosts
            : Array.Empty<string>();

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

    /// <summary>Who is ahead of whom, last time we looked: per ghost, the real car it was measured against and whether the ghost was ahead of it.</summary>
    private readonly Dictionary<string, (string Human, bool Ahead)> _ghostWasAhead = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// CheckGhostPositions' decision for one ghost, on its own for testing:
    /// null for no call, otherwise whether the human passed the ghost.
    /// Records the new state either way.
    /// </summary>
    internal static bool? GhostPassCall(IDictionary<string, (string Human, bool Ahead)> wasAhead,
        string ghost, string human, bool ghostAhead)
    {
        // WHY (2026-11-05, Bug hunt 11): the state used to be kept per ghost
        // only, against whichever real car led at the time. When the leading
        // real car changed between two of a ghost's crossings (A, G, B going
        // to G, B, A after A broke down), the old "behind A" was compared
        // with "ahead of B" and the PA called a pass on B that never
        // happened. Only a swap between the same two cars is a pass now; a
        // new reference car just starts afresh.
        bool? call = wasAhead.TryGetValue(ghost, out var last)
            && string.Equals(last.Human, human, StringComparison.OrdinalIgnoreCase)
            && last.Ahead != ghostAhead
                ? !ghostAhead
                : null;

        wasAhead[ghost] = (human, ghostAhead);
        return call;
    }

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

            if (GhostPassCall(_ghostWasAhead, ghost.TransponderId, human.TransponderId, ghostAhead) is { } humanPassedGhost)
            {
                _announcer.NotifyGhostPass(
                    human.TransponderId,
                    ghost.TransponderId,
                    humanPassedGhost: humanPassedGhost);
            }
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
        if (!SyncGhostProfiles(_ghostStore, _profiles))
        {
            return;
        }

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

    /// <summary>EnsureGhostProfiles' work on the profiles, on its own for testing. False when nothing was done.</summary>
    internal static bool SyncGhostProfiles(GhostStore ghostStore, ProfileStore profiles)
    {
        // WHY (2026-11-05, Bug hunt 11): ghosts.json held by a backup or sync
        // tool at launch loads as empty defaults (GhostStore.LoadedUnreadable),
        // and the startup call here then took every ghost's profile for an
        // orphan and deleted it from profiles.json - they came back next
        // launch as bare new profiles, FirstSeen and all else lost.
        // GhostStore.Save already refuses to bring the profiles into line
        // with those empty defaults; this is the same rule for the startup
        // call and HandleGhosts.
        if (ghostStore.LoadedUnreadable)
        {
            return false;
        }

        // Ghosts that have been deleted or renamed leave orphan profiles
        // behind, which would keep appearing in every picker.
        var liveTransponders = ghostStore.Current.Ghosts
            .Select(g => g.Transponder)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var orphans = profiles.AllIncludingGhosts
            .Where(p => GhostService.IsGhost(p.Transponder) && !liveTransponders.Contains(p.Transponder))
            .Select(p => p.Transponder)
            .ToList();

        foreach (var orphan in orphans)
        {
            profiles.Delete(orphan);
        }

        // Every ghost gets a profile, enabled or not - a disabled ghost
        // should still be selectable, it just won't run until switched on.
        foreach (var ghost in ghostStore.Current.Ghosts)
        {
            var existing = profiles.Get(ghost.Transponder);

            if (existing is not null
                && existing.DriverName == ghost.Name
                && existing.CarModel == ghost.CarModel)
            {
                continue;
            }

            profiles.Upsert(new DriverProfile
            {
                Transponder = ghost.Transponder,
                DriverName = ghost.Name,
                CarModel = ghost.CarModel,
                ExcludeFromRecords = true,
                Notes = "Virtual opponent (ghost car).",
            });
        }

        profiles.Save();
        return true;
    }
}
