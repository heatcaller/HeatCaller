namespace HeatCaller;

/// <summary>
/// Firsts for a driver - for now, whether tonight is their first night at
/// the club, so their phone can show them a short guide to how a race night
/// runs before their first race (22 Oct 2026, driver experience, item 8).
/// </summary>
internal static class DriverFirsts
{
    /// <summary>
    /// True when <paramref name="transponder"/> has no result before tonight
    /// in <paramref name="history"/> - any session, practice included, that
    /// isn't a demo. A race or a practice already run TONIGHT doesn't count
    /// against it: the guide is for the whole of their first night.
    /// </summary>
    /// <param name="nightLocal">
    /// Any local time during tonight (DateTime.Now), which is put on its race
    /// night the way TonightAtTheTrack does (6am to 6am). A value with no time
    /// of day is taken as the night's date itself, so passing
    /// TonightAtTheTrack.NightOf(...) works too rather than meaning the night
    /// before.
    /// </param>
    public static bool IsFirstNight(IEnumerable<SessionRecord>? history, string? transponder, DateTime nightLocal)
    {
        var who = transponder?.Trim();

        if (string.IsNullOrEmpty(who))
        {
            return false;
        }

        var tonight = nightLocal.TimeOfDay == TimeSpan.Zero
            ? nightLocal.Date
            : TonightAtTheTrack.NightOf(nightLocal);

        foreach (var session in history ?? Enumerable.Empty<SessionRecord>())
        {
            if (session is null || session.IsDemoSession())
            {
                continue;
            }

            if (TonightAtTheTrack.NightOf(session.StartedAtUtc.ToLocalTime()) >= tonight)
            {
                continue;
            }

            if ((session.Results ?? new List<SessionResultEntry>())
                .Any(r => r is not null && string.Equals(r.Transponder?.Trim(), who, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return true;
    }
}
