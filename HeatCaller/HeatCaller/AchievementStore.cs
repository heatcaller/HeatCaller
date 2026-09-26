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
            // reason a club night can't start.
            SafeFile.PreserveUnreadable(FilePath, ex);
        }

        return new AchievementStore(new List<DriverAchievements>());
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(_drivers, new JsonSerializerOptions { WriteIndented = true });
        SafeFile.WriteAllText(FilePath, json);
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
                .Where(u => u.SessionId is { } id && wanted.Contains(id))
                .ToList();

            foreach (var award in going)
            {
                driver.Unlocked.Remove(award);
                removed.Add(new RemovedAward(driver.Transponder, award));
            }
        }

        return removed;
    }

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
