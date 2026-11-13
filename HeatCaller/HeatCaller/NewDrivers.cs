namespace HeatCaller;

/// <summary>
/// Drivers new to racing (12 Nov 2026, setup plan D12).
///
/// WHY: keeping new drivers is how clubs survive, and a first night is
/// where they're won or lost. Lapped six times with nobody saying a word,
/// they don't come back. It costs almost nothing to say who they are: the
/// desk sees "first night" or "novice, mentor Al" beside their name, the
/// announcer welcomes them once before their race so the field gives them
/// room, and Q1 seeding by pace already puts a driver with no laps in with
/// the slowest. Pure.
/// </summary>
public static class NewDrivers
{
    /// <summary>
    /// A driver first seen tonight - their profile made tonight - is on their
    /// first night. The night runs 6am to 6am (TonightAtTheTrack.NightOf), so
    /// a newcomer checked in at 19:30 is still on their first night at 00:05;
    /// by the calendar date the welcome and the badge went at midnight.
    /// </summary>
    public static bool FirstNight(DriverProfile profile, DateTime nowLocal) =>
        profile.FirstSeenUtc is { } first
        && TonightAtTheTrack.NightOf(first.ToLocalTime()) == TonightAtTheTrack.NightOf(nowLocal);

    /// <summary>New to racing: ticked as a novice, or on their first night.</summary>
    public static bool IsNew(DriverProfile profile, DateTime nowLocal) =>
        profile.IsNovice || FirstNight(profile, nowLocal);

    /// <summary>What Who's Racing puts after their name: " · first night", " · novice, mentor Al Gorithim", or nothing.</summary>
    public static string Badge(DriverProfile profile, DateTime nowLocal)
    {
        if (!IsNew(profile, nowLocal))
        {
            return string.Empty;
        }

        var what = FirstNight(profile, nowLocal) ? "first night" : "novice";
        var mentor = string.IsNullOrWhiteSpace(profile.Mentor) ? string.Empty : $", mentor {profile.Mentor.Trim()}";

        return $" · {what}{mentor}";
    }

    /// <summary>
    /// The announcer's welcome before a race with new drivers in it, or null
    /// with none: "A big welcome to Max Power, racing with us for the first
    /// time tonight - give them room out there." Names already welcomed
    /// tonight are left out.
    /// </summary>
    public static string? Welcome(IReadOnlyList<(string Name, bool FirstNight)> drivers)
    {
        if (drivers.Count == 0)
        {
            return null;
        }

        var names = Join(drivers.Select(d => d.Name).ToList());
        var allFirst = drivers.All(d => d.FirstNight);

        return allFirst
            ? $"A big welcome to {names}, racing with us for the first time tonight - give {(drivers.Count == 1 ? "them" : "them all")} room out there."
            : $"Look out for {names}, new to racing - give {(drivers.Count == 1 ? "them" : "them all")} room out there and a cheer at the end.";
    }

    private static string Join(IReadOnlyList<string> names) => names.Count switch
    {
        0 => string.Empty,
        1 => names[0],
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
    };
}
