namespace HeatCaller;

/// <summary>Who a sidebar page is for.</summary>
public enum NavTier
{
    /// <summary>
    /// The pages a race day is actually run from, while one is open.
    ///
    /// First on purpose: the enum's order is the sidebar's order (see
    /// MainForm.OrderedNavNames, which sorts by this), and at seven o'clock on
    /// a Saturday these are the rows that matter. Nothing lives here
    /// permanently - a page is in this group only while a race day is open,
    /// and drops back into its own the moment the day is closed.
    /// </summary>
    Tonight,

    /// <summary>Anyone timing and running races - a home track, a garage, a single racer.</summary>
    Essentials,

    /// <summary>What a club running events for other people needs on top.</summary>
    Club,

    /// <summary>
    /// A page not yet tested at a real event, while the club has asked for
    /// those to be kept out of the way (AppSettings.HideUntestedPages).
    /// Last, so a search that turns one up shows it at the foot of the
    /// list under its own heading. See NavTiers.UntestedPages.
    /// </summary>
    Untested,
}

/// <summary>
/// Which sidebar pages are for everyone and which are club tools.
///
/// The sidebar shows the two as gently separated groups, Essentials first,
/// and keeps each page inside its own group whatever order the club drags
/// them into. This is also the line future subscription tiers are expected
/// to follow, so it lives in one place.
///
/// A page not listed as a club tool is Essentials - a new page shows up
/// for everyone until it is deliberately put here.
/// </summary>
public static class NavTiers
{
    private static readonly HashSet<string> ClubPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Race Day",
        "Championship",
        "Rentals",
        "Handicaps",
        "Venues",
        "Sponsors",
        "Local Screens",
        "Club Website",
    };

    /// <summary>
    /// The pages that come to the top while a race day is open, in the order
    /// the evening happens: the day is set up, then it is run, then the
    /// standings are looked at.
    ///
    /// Championship joined the two it started with: tonight's results are
    /// what move the standings, and between heats is when people come asking
    /// where they stand. It goes back down with Race Day when the day closes.
    ///
    /// Still short on purpose. The value of this is entirely in how short it
    /// is - a "tonight" group holding six pages is just the sidebar again with
    /// an extra heading in it.
    /// </summary>
    private static readonly string[] TonightPages = { "Race Day", "Race Control", "Championship" };

    /// <summary>
    /// THE BETA TIER (22 Oct 2026): the pages that have not yet been through
    /// a night on the floor, kept out of a beta club's sidebar when it asks
    /// for that (AppSettings.HideUntestedPages).
    ///
    /// Read from the rehearsal table in ROADMAP.md: four solo nights with
    /// the EasyLap and the I-Lap, real cars and the phone pages, plus the
    /// whole-night test (RaceNightTests) - a race day, check-in, qualifying,
    /// the mains, a bump-up, the announcer, the Big Screen, the club's
    /// website and the voice. These five were never part of any of them:
    /// a championship has been tested only in code, and rentals, handicaps,
    /// venues and the garage not at all on a night.
    ///
    /// The rest of what the roadmap names as untested - batteries, a
    /// multi-day event, the soundscape editor - lives inside pages that
    /// have been run (Driver Hub, Race Day, Sounds), so it cannot be hidden
    /// here without hiding the page a night needs.
    ///
    /// HOW A PAGE COMES OFF THIS LIST: it is used at a rehearsal or a club
    /// night, the night gets its row in the rehearsal table, and the page
    /// is taken out of this set in the same commit. Nothing else.
    /// </summary>
    private static readonly HashSet<string> UntestedPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Championship",
        "Rentals",
        "Handicaps",
        "Venues",
        "Garage",
    };

    /// <summary>The heading over a page not yet tested at a real event, when a search turns one up.</summary>
    public const string UntestedHeading = "NOT YET TESTED";

    /// <summary>
    /// Whether the sidebar is built for a club hiding untested pages - set
    /// by MainForm from AppSettings.HideUntestedPages before each rebuild,
    /// so SectionOf (which the sidebar list asks for each row's heading)
    /// agrees with the order the rebuild used. Tests call the overloads that
    /// take it as an argument and never set this.
    /// </summary>
    public static bool HidingUntested { get; set; }

    /// <summary>Whether a page has not yet been tested at a real event. See UntestedPages.</summary>
    public static bool IsUntested(string page) => UntestedPages.Contains(page);

    /// <summary>The pages not yet tested at a real event, for a dialog to name.</summary>
    public static IReadOnlyCollection<string> Untested => UntestedPages;

    public static NavTier TierOf(string page) => ClubPages.Contains(page) ? NavTier.Club : NavTier.Essentials;

    /// <summary>
    /// Which group a page is in, given whether a race day is open.
    ///
    /// Nothing is hidden by this and nothing is reordered permanently. The
    /// Tonight pages move to a group of their own at the top while a day is
    /// open, and the club's own order for everything else - and for those
    /// pages once the day closes - is exactly as it was.
    /// </summary>
    public static NavTier TierOf(string page, bool raceNight) => TierOf(page, raceNight, HidingUntested);

    /// <summary>
    /// The same, given whether the club hides untested pages. One of those
    /// is in the Untested group whatever else is true of it - including
    /// Championship on a race night - since it is only in the sidebar at all
    /// when a search turns it up.
    /// </summary>
    public static NavTier TierOf(string page, bool raceNight, bool hidingUntested)
    {
        if (hidingUntested && IsUntested(page))
        {
            return NavTier.Untested;
        }

        return raceNight && TonightPages.Contains(page, StringComparer.OrdinalIgnoreCase)
            ? NavTier.Tonight
            : TierOf(page);
    }

    /// <summary>
    /// The order within the Tonight group, and zero for everything else.
    ///
    /// Used as a secondary sort key over the whole list. LINQ's OrderBy and
    /// ThenBy are stable, so a key that is zero for every page in a group
    /// leaves that group in whatever order the club chose.
    /// </summary>
    public static int TonightRank(string page, bool raceNight)
    {
        if (!raceNight)
        {
            return 0;
        }

        var index = Array.FindIndex(TonightPages, p => string.Equals(p, page, StringComparison.OrdinalIgnoreCase));

        return index < 0 ? 0 : index;
    }

    /// <summary>The small heading drawn above each group in the sidebar.</summary>
    public static string HeadingFor(NavTier tier) => tier switch
    {
        NavTier.Tonight => "TONIGHT",
        NavTier.Club => "CLUB TOOLS",
        NavTier.Untested => UntestedHeading,
        _ => "ESSENTIALS",
    };

    /// <summary>The heading a page sits under - what the sidebar asks for each row.</summary>
    public static string SectionOf(string page) => HeadingFor(TierOf(page));

    /// <summary>The same, given whether a race day is open.</summary>
    public static string SectionOf(string page, bool raceNight) => HeadingFor(TierOf(page, raceNight));

    /// <summary>The same, given whether the club hides untested pages.</summary>
    public static string SectionOf(string page, bool raceNight, bool hidingUntested) =>
        HeadingFor(TierOf(page, raceNight, hidingUntested));
}
