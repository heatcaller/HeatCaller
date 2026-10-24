using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Who has earned what, persisted to
/// %AppData%\HeatCaller\achievements.json.
///
/// Keyed by transponder like every other per-driver store here, so a badge
/// follows the transponder rather than the name on it. That is the same
/// trade every other store makes: rename a driver and their badges follow;
/// hand the transponder to someone else and they inherit them.
/// </summary>
public sealed class AchievementStore
{
    private readonly List<DriverAchievements> _drivers;

    public IReadOnlyList<DriverAchievements> All => _drivers.ToList();

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "achievements.json");

    private AchievementStore(List<DriverAchievements> drivers)
    {
        _drivers = drivers;
    }

    public static AchievementStore Load()
    {
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var json = SafeFile.ReadAllTextWithRetry(FilePath);
                var loaded = JsonSerializer.Deserialize<List<DriverAchievements>>(json);

                if (loaded is not null)
                {
                    return new AchievementStore(loaded);
                }
            }
        }
        catch (Exception ex)
        {
            // Corrupt or unreadable: start fresh rather than refusing to
            // open the app. Badges are decoration - never let them be the
            // reason a club night can't start. But, when it could not be
            // read at all or no copy of it could be kept, never save over it
            // from this store (see LoadedUnreadable).
            unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        return new AchievementStore(new List<DriverAchievements>()) { LoadedUnreadable = unreadable };
    }

    /// <summary>
    /// True when the file was there but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. Badges are decoration, but the
    /// file also holds every driver's running race, lap, win and podium
    /// totals, and nothing rebuilds those. This store is loaded afresh
    /// after every race, so one locked read (a backup or sync tool holding
    /// the file as a main finishes) used to hand the evaluator an empty
    /// store, and its save then wrote just that race's drivers over
    /// everyone else's. Held per load rather than for the session: the
    /// next race's fresh load reads the real file again and saves as usual.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    /// <summary>
    /// Writes the store. False when nothing reached the disk - held after an
    /// unreadable load, or the write failed (SafeFile has already put the
    /// reason on the console). The answer used to be thrown away, so a
    /// night of failed writes announced every badge over the PA as if it
    /// were recorded, and the next race's fresh load, missing it, announced
    /// "First Timer" to the same drivers again - race after race.
    /// </summary>
    public bool Save()
    {
        // Not written: the real file is almost certainly fine, and saving
        // this near-empty store would replace it. The console has already
        // said why.
        if (LoadedUnreadable)
        {
            return false;
        }

        var json = JsonSerializer.Serialize(_drivers, new JsonSerializerOptions { WriteIndented = true });
        return SafeFile.WriteAllText(FilePath, json);
    }

    /// <summary>This driver's record, created empty if they've never raced.</summary>
    public DriverAchievements For(string transponder)
    {
        var existing = _drivers.FirstOrDefault(d =>
            string.Equals(d.Transponder, transponder, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            return existing;
        }

        var created = new DriverAchievements { Transponder = transponder };
        _drivers.Add(created);
        return created;
    }

    /// <summary>
    /// Awards a badge if it isn't already held, and says whether it was new.
    ///
    /// Returning the outcome rather than raising an event keeps the caller
    /// in charge of what an unlock means - announcing it, showing it, or
    /// neither - and lets a batch of them be collected before anything is
    /// said out loud.
    /// </summary>
    public bool Award(string transponder, string achievementId, string? sessionId = null, string detail = "")
    {
        if (string.IsNullOrWhiteSpace(transponder) || string.IsNullOrWhiteSpace(achievementId))
        {
            return false;
        }

        var driver = For(transponder);

        if (driver.Has(achievementId))
        {
            return false;
        }

        driver.Unlocked.Add(new UnlockedAchievement
        {
            Id = achievementId,
            UnlockedUtc = DateTime.UtcNow,
            SessionId = sessionId,
            Detail = detail,
        });

        return true;
    }

    /// <summary>One badge that was taken back, and whose it was - everything an undo needs to put it on again.</summary>
    public sealed record RemovedAward(string Transponder, UnlockedAchievement Award);

    /// <summary>
    /// Takes back every badge earned in one of these races, and says which
    /// ones went.
    ///
    /// For deleting a race day. A badge names the session that produced it,
    /// so when that session is being removed from history the badge has
    /// nothing left to point at - left behind it is a claim about a race
    /// nobody can look up.
    ///
    /// The running totals (races, laps, wins, podiums) are deliberately NOT
    /// wound back. They are a cache of milestones counted forward over the
    /// club's whole life, and correcting them from here is arithmetic this
    /// method cannot check: a driver sitting on 99 races would need their
    /// hundredth badge taking back as well, and the badge after that. A
    /// slightly generous race count is a better outcome than a wrong one,
    /// and deleting a day is rare.
    ///
    /// SO THE BADGES THOSE TOTALS GIVE ARE KEPT TOO (17 Oct 2026, Bug Hunt
    /// 8, A30-5) - see <see cref="KeptWithTheTotals"/>. Taking First Race
    /// or Ten Wins away while leaving the totals that earned them meant the
    /// very next race awarded them again, and a veteran heard "First Race"
    /// read out over the PA. They keep their SessionId, are never in the
    /// list returned, and so undo has nothing to do for them.
    ///
    /// Does not save. The caller saves once, after everything else it is
    /// removing, so a half-finished delete never reaches disk.
    /// </summary>
    public IReadOnlyList<RemovedAward> RemoveForSessions(IReadOnlyCollection<string> sessionIds)
    {
        var removed = new List<RemovedAward>();

        if (sessionIds.Count == 0)
        {
            return removed;
        }

        var wanted = new HashSet<string>(sessionIds, StringComparer.Ordinal);

        foreach (var driver in _drivers)
        {
            var going = driver.Unlocked
                .Where(u => u.SessionId is { } id && wanted.Contains(id) && !KeptWithTheTotals.Contains(u.Id))
                .ToList();

            foreach (var award in going)
            {
                driver.Unlocked.Remove(award);
                removed.Add(new RemovedAward(driver.Transponder, award));
            }
        }

        return removed;
    }

    /// <summary>
    /// The badges the evaluator re-derives from the running totals, which
    /// RemoveForSessions does not wind back - so neither are these. An
    /// explicit list rather than the whole milestone category, which also
    /// holds per-race badges (night-owl, calendar) that do go with their race.
    ///
    /// WHY (23 Oct 2026, bug hunt 10): Record Collector is worked out from
    /// DriverAchievements.TrackRecords, the list of layouts the driver has
    /// held a record on - another running total RemoveForSessions leaves
    /// alone. It used to go with its race while the list stayed, so the
    /// driver's next track record anywhere announced it again as new. It is
    /// kept now, like the race, lap, win and podium milestones. Track Record
    /// Holder is not: it is only awarded as a new record is set, so getting
    /// it back is earned.
    /// </summary>
    internal static readonly IReadOnlySet<string> KeptWithTheTotals = new HashSet<string>(StringComparer.Ordinal)
    {
        AchievementCatalogue.FirstRace,
        AchievementCatalogue.TenRaces,
        AchievementCatalogue.TwentyFiveRaces,
        AchievementCatalogue.FiftyRaces,
        AchievementCatalogue.HundredRaces,
        AchievementCatalogue.ThousandLaps,
        AchievementCatalogue.FiveThousandLaps,
        AchievementCatalogue.TenThousandLaps,
        AchievementCatalogue.FirstWin,
        AchievementCatalogue.TenWins,
        AchievementCatalogue.TwentyFiveWins,
        AchievementCatalogue.FiftyWins,
        AchievementCatalogue.FirstPodium,
        AchievementCatalogue.PodiumRegular,
        AchievementCatalogue.TenPodiums,
        AchievementCatalogue.TwentyFivePodiums,
        AchievementCatalogue.ThreeTrackRecords,
    };

    /// <summary>Puts back what RemoveForSessions took. Does not save; the caller does.</summary>
    public void RestoreAwards(IEnumerable<RemovedAward> awards)
    {
        foreach (var removed in awards)
        {
            var driver = For(removed.Transponder);

            // By id, so an undo pressed after the badge has been earned
            // again does not give the driver two of it.
            if (!driver.Has(removed.Award.Id))
            {
                driver.Unlocked.Add(removed.Award);
            }
        }
    }

    /// <summary>Badges this driver has yet to earn, for showing locked ones alongside the rest.</summary>
    public IReadOnlyList<AchievementDefinition> LockedFor(string transponder)
    {
        var driver = For(transponder);
        return AchievementCatalogue.All.Where(a => !driver.Has(a.Id)).ToList();
    }

    public void Delete(string transponder)
    {
        _drivers.RemoveAll(d => string.Equals(d.Transponder, transponder, StringComparison.OrdinalIgnoreCase));
        Save();
    }
}
