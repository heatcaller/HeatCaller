using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// THE CLUB'S PRIVACY RULES, FOR THE RELAY TO ENFORCE (15 Nov 2026,
/// children's privacy fixes 1.2, 2.6 and 4.3). One small private snapshot,
/// "privacy", sent like the other settings documents - every tick, and only
/// when it has changed (MainForm.Relay, SendToRelayIfChanged):
///
///   juniors   - every form a junior's name can go online in
///               (PublicPrivacy.JuniorPublicNames). The relay refuses a
///               follower alert sign-up for any of them, drops follows of
///               them it already holds, never alerts a follower about one,
///               and puts noindex on any public page that names one.
///   keepYears - "Unpublish results older than N years" (AppSettings
///               .RelayUnpublishAfterYears); 0 keeps them all.
///   fromYear  - the first calendar year still kept, worked out here so the
///               document changes, and is sent again, when the year turns -
///               which is what takes the oldest year down. 0 with keepYears 0.
///
/// Private on the relay (relay/src/types.ts CHANNELS.privacy): which names
/// are children's is nobody else's business. The full name is among the
/// forms because a club with two juniors whose names differ only at the end
/// can show it; the relay already holds full names privately (check-in).
/// </summary>
internal static class RelayPrivacyDocument
{
    /// <summary>The most years the setting takes; more is "keep them all" in all but name.</summary>
    public const int MostYears = 50;

    /// <summary>The first calendar year still published, or 0 to keep every year.</summary>
    public static int FirstYearKept(int keepYears, DateTime now)
    {
        var years = Math.Clamp(keepYears, 0, MostYears);
        return years == 0 ? 0 : now.Year - years + 1;
    }

    /// <summary>Whether a session raced (or a results-index year) at this time is older than the club keeps online.</summary>
    public static bool TooOld(DateTime raced, int keepYears, DateTime now)
    {
        var from = FirstYearKept(keepYears, now);

        // A session saved with no start carries DateTime.MinValue: not old, unknown.
        return from > 0 && raced > DateTime.MinValue && raced.Year < from;
    }

    /// <summary>Whether a results-index year ("2019") is older than the club keeps online.</summary>
    public static bool YearTooOld(string year, int keepYears, DateTime now)
    {
        var from = FirstYearKept(keepYears, now);
        return from > 0 && int.TryParse(year, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var y) && y < from;
    }

    /// <summary>
    /// Whether a session is older than the club keeps online, judged by the
    /// same year the results index files it under (RelayResultsIndex.YearOf:
    /// the local race night, 6am to 6am) against the local now.
    ///
    /// WHY (23 Nov 2026, bug hunt 24): the session check used TooOld on the
    /// session's UTC start against the UTC now, while the index year it is
    /// listed under is the LOCAL race night checked against the local now.
    /// Around New Year the two disagree: west of UTC a New Year's Eve race
    /// (UTC 1 Jan) was taken down with its index year but sent back on the
    /// next re-send; east of UTC a New Year's Day morning race (UTC 31 Dec)
    /// stopped being sent a year early while its index year was still up.
    /// One year now decides both. A session saved with no time at all is
    /// unknown, not old, as before.
    /// </summary>
    public static bool SessionTooOld(SessionRecord session, int keepYears, DateTime localNow)
    {
        if (session is null || RelayResultsIndex.EffectiveStart(session) == DateTime.MinValue)
            return false;

        return YearTooOld(RelayResultsIndex.YearOf(session), keepYears, localNow);
    }

    /// <summary>The document, as JSON.</summary>
    public static string Build(PublicPrivacy privacy, int keepYears, DateTime now)
    {
        var years = Math.Clamp(keepYears, 0, MostYears);

        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["juniors"] = (privacy ?? PublicPrivacy.Empty).JuniorPublicNames,
            ["keepYears"] = years,
            ["fromYear"] = FirstYearKept(years, now),
        });
    }
}
