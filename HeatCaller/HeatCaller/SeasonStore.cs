using System.Text.Json;

namespace HeatCaller;

/// <summary>Loads/saves seasons to %AppData%\HeatCaller\seasons.json and computes championship standings.</summary>
public sealed class SeasonStore
{
    private readonly List<Season> _seasons;

    public IReadOnlyList<Season> All => _seasons.OrderBy(s => s.Name).ToList();

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "seasons.json");

    private SeasonStore(List<Season> seasons)
    {
        _seasons = seasons;
    }

    public static SeasonStore Load()
    {
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var json = SafeFile.ReadAllTextWithRetry(FilePath);
                var loaded = JsonSerializer.Deserialize<List<Season>>(json);
                if (loaded is not null)
                {
                    // A hand-edited or restored file can say null where a
                    // list belongs; every reader takes the list as it is.
                    loaded.RemoveAll(season => season is null);

                    foreach (var season in loaded)
                    {
                        season.CarriedPoints = (season.CarriedPoints ?? new List<CarriedPointsEntry>())
                            .Where(entry => entry is not null)
                            .ToList();
                    }

                    return new SeasonStore(loaded);
                }
            }
        }
        catch (Exception ex)
        {
            // Corrupt/unreadable file: start fresh rather than crashing on launch.
            unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        return new SeasonStore(new List<Season>()) { LoadedUnreadable = unreadable };
    }

    /// <summary>
    /// True when the file was there at startup but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. What this store holds is then a
    /// guess, and nothing it does this session is saved over the club's
    /// real file.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    public void Save()
    {
        if (LoadedUnreadable)
        {
            // See LoadedUnreadable: the club's real file stays as it is.
            return;
        }

        var json = JsonSerializer.Serialize(_seasons, new JsonSerializerOptions { WriteIndented = true });
        SafeFile.WriteAllText(FilePath, json);
    }

    public Season? Get(string id) => _seasons.FirstOrDefault(s => s.Id == id);

    public void Upsert(Season season)
    {
        var index = _seasons.FindIndex(s => s.Id == season.Id);
        if (index >= 0)
        {
            _seasons[index] = season;
        }
        else
        {
            _seasons.Add(season);
        }
        Save();
    }

    public void Delete(string id)
    {
        _seasons.RemoveAll(s => s.Id == id);
        Save();
    }

    /// <summary>
    /// Points every championship for one class at its new name, for a
    /// class rename. A championship names its class as text, and one whose
    /// class no longer exists stops auto-including that class's races -
    /// so without this a rename quietly froze the championship. Returns
    /// the ids of the championships that moved, for the undo.
    /// </summary>
    public IReadOnlyList<string> RenameClass(string oldName, string newName)
    {
        var moved = new List<string>();

        foreach (var season in _seasons)
        {
            if (season.RenameClass(oldName, newName))
            {
                moved.Add(season.Id);
            }
        }

        if (moved.Count > 0)
        {
            Save();
        }

        return moved;
    }

    /// <summary>
    /// The ids of the championships for this class now. Taken just before
    /// a rename, so its undo can leave alone one that was already for the
    /// new name.
    /// </summary>
    public IReadOnlyList<string> WithClassName(string className) =>
        _seasons
            .Where(s => !string.IsNullOrEmpty(s.ClassName)
                        && string.Equals(s.ClassName, className, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Id)
            .ToList();

    /// <summary>
    /// The undo half of RenameClass: every championship for
    /// <paramref name="newName"/> NOW goes back to <paramref name="oldName"/>,
    /// except those in <paramref name="leaveAlone"/>, which were already for
    /// the new name before the rename. Judged by what each one holds now,
    /// so a championship pointed at another class since is not dragged
    /// back, and one set up for the renamed class since goes back with it.
    /// </summary>
    public void UndoRenameClass(string newName, string oldName, IReadOnlyCollection<string> leaveAlone)
    {
        var keep = leaveAlone.ToHashSet(StringComparer.Ordinal);
        var changed = false;

        foreach (var season in _seasons.Where(s => !keep.Contains(s.Id)))
        {
            // The same rule the rename used, the other way round - an
            // "every class" championship names no class and is never touched.
            if (season.RenameClass(newName, oldName))
            {
                changed = true;
            }
        }

        if (changed)
        {
            Save();
        }
    }

    /// <summary>
    /// Computes the standings table for a season: total points, sessions
    /// counted, wins, podiums, and best finish per driver, across every
    /// included session that's still in history (a since-deleted session ID
    /// is simply skipped). Drivers are matched by the DriverName recorded
    /// in each session's results at the time it was saved - the same
    /// snapshot-based identity used everywhere else results are displayed.
    /// </summary>
    /// <remarks>
    /// Scores are gathered PER ROUND per driver rather than summed as they
    /// go, because "best 5 of 7" cannot be worked out from a running total
    /// - you have to still have the individual rounds to throw the worst
    /// of them away. When the season counts everything (CountBestRounds
    /// zero, which is the default and what this always did) the two are
    /// the same number and RawPoints equals TotalPoints.
    /// </remarks>
    /// <summary>
    /// Adds up a championship from the races it is handed.
    ///
    /// Takes the sessions rather than fetching them: which races count is
    /// EventSpine.SessionsFor's question, and this one only knows how to
    /// turn finishing positions into points. Keeping them apart is what
    /// stopped there being two answers to the first question.
    ///
    /// Two things are still refused here whatever the caller hands over,
    /// because they must never score however a session reached the list
    /// (a championship's rule, or a session ticked in by hand):
    ///
    /// A saved demo race - its lap times were invented by the simulator.
    ///
    /// A driver whose profile is marked "Exclude from records" - a guest,
    /// staff car or anyone running outside the competition. They are taken
    /// out and the places close up behind them, exactly as a ghost is, so
    /// the club driver who finished behind a guest scores the place they
    /// would have had if the guest had not been there. Matched on the
    /// transponder against the profile as it is NOW, the same live check the
    /// track record uses, so ticking or unticking the flag re-scores the
    /// table straight away. <paramref name="profiles"/> is optional only so
    /// a caller that cannot see the profiles still compiles; without it
    /// nobody is excluded.
    /// </summary>
    public static List<SeasonStandingEntry> ComputeStandings(
        Season season,
        IReadOnlyList<SessionRecord> sessions,
        ProfileStore? profiles = null)
    {
        var byDriver = new Dictionary<string, SeasonStandingEntry>(StringComparer.OrdinalIgnoreCase);
        var rounds = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in sessions)
        {
            if (session.IsDemoSession())
            {
                continue;
            }

            // Ghosts are a pace reference, not entrants - but races recorded
            // before they were kept out of saved results still carry them,
            // and a ghost sitting in third did not only score the points for
            // third. It pushed every real driver behind it down a place and
            // took a place's worth off each of them, for good.
            //
            // So they come out and the field closes up, which is the result
            // the race would have recorded had the ghost never been on the
            // board. Positions are taken from this order rather than from
            // the stored number for exactly that reason; a session with no
            // ghost in it comes out identical, because the stored positions
            // are already 1..N in this order.
            var scoring = session.Results
                .Where(r => !GhostService.IsGhost(r.Transponder))
                .Where(r => profiles is null
                            || string.IsNullOrEmpty(r.Transponder)
                            || profiles.Get(r.Transponder) is not { ExcludeFromRecords: true })
                .OrderBy(r => r.Position)
                .ToList();

            // One score per driver per session. Two rows under one name (a
            // spare transponder, a car swapped mid-meeting) used to score
            // twice, which inflated rounds counted and skewed best-N drops.
            // The first row is the best placed, because of the order above.
            var scoredHere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < scoring.Count; index++)
            {
                var result = scoring[index];

                var name = string.IsNullOrWhiteSpace(result.DriverName) ? result.Transponder : result.DriverName;

                if (string.IsNullOrWhiteSpace(name) || !scoredHere.Add(name))
                {
                    continue;
                }

                // Counted from the drivers actually scored so far, not the
                // row number: a skipped second row under the same name used
                // to keep its place, so everyone behind it scored one place
                // lower than they finished - the same wrong the ghost rule
                // above puts right.
                var position = scoredHere.Count;

                if (!byDriver.TryGetValue(name, out var entry))
                {
                    entry = new SeasonStandingEntry { DriverName = name };
                    byDriver[name] = entry;
                    rounds[name] = new List<int>();
                }

                // No laps is a DNF: on the entry list, never across the
                // line, so no points and no round counted. Zero-lap rows
                // always sort to the bottom, so nobody else's position
                // moves because of it.
                if (result.Laps <= 0)
                {
                    entry.Dnfs++;
                    continue;
                }

                var points = season.PointsForPosition(position);

                rounds[name].Add(points);

                entry.RawPoints += points;
                entry.SessionsCounted++;

                // A driver the race director marked DNF (or DQ) in Edit
                // Result can still have laps - ResultEditor keeps a DNF's
                // laps, and its place keeps that place's points on purpose
                // (see ResultEditor.Group). So the points above stand. But
                // the official word is "did not finish", so the DNF column
                // counts it and it is not a win or a podium: a DNF in P3
                // used to show 0 DNFs and one more podium. Same rule as
                // DriverStats.IsFinish.
                if (ResultEditor.NormaliseOutcome(result.Outcome) is ResultEditor.Dnf or ResultEditor.Dq)
                {
                    entry.Dnfs++;
                    continue;
                }

                // After the check, so a DNF is not a best finish either -
                // the driver card (DriverStats.IsFinish) leaves it out too.
                entry.BestFinish = Math.Min(entry.BestFinish, position);

                if (position == 1)
                {
                    entry.Wins++;
                }

                if (position is >= 1 and <= 3)
                {
                    entry.Podiums++;
                }
            }
        }

        var best = Math.Max(0, season.CountBestRounds);

        foreach (var (name, entry) in byDriver)
        {
            var scored = rounds[name];

            if (best <= 0 || scored.Count <= best)
            {
                // Nothing to drop. Somebody who raced four rounds of a
                // "best five" championship keeps all four - dropping their
                // worst as well would punish them for turning up less.
                entry.TotalPoints = entry.RawPoints;
                entry.DroppedRounds = 0;
                continue;
            }

            entry.TotalPoints = scored.OrderByDescending(p => p).Take(best).Sum();
            entry.DroppedRounds = scored.Count - best;
        }

        // Points carried in from before the championship ran here (27 Sep
        // 2026). Added after the best-rounds rule, which drops rounds - and
        // carried points are not a round. A driver with carried points and
        // no race yet is in the table on those alone - unless their profile
        // is kept out of records and points, as their races are above.
        var excluded = new HashSet<string>(
            (profiles?.All ?? Array.Empty<DriverProfile>())
                .Where(p => p.ExcludeFromRecords && !string.IsNullOrWhiteSpace(p.DriverName))
                .Select(p => p.DriverName.Trim()),
            StringComparer.OrdinalIgnoreCase);

        foreach (var carried in season.CarriedPoints ?? new List<CarriedPointsEntry>())
        {
            var name = (carried?.DriverName ?? string.Empty).Trim();

            if (carried is null || name.Length == 0 || carried.Points == 0 || excluded.Contains(name))
            {
                continue;
            }

            if (!byDriver.TryGetValue(name, out var entry))
            {
                entry = new SeasonStandingEntry { DriverName = name };
                byDriver[name] = entry;
            }

            entry.CarriedIn += carried.Points;
            entry.RawPoints += carried.Points;
            entry.TotalPoints += carried.Points;
        }

        return byDriver.Values
            .OrderByDescending(e => e.TotalPoints)
            .ThenByDescending(e => e.Wins)
            .ThenBy(e => e.BestFinish)
            .ToList();
    }

}
