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

    public static NavTier TierOf(string page) => ClubPages.Contains(page) ? NavTier.Club : NavTier.Essentials;

    /// <summary>
    /// Which group a page is in, given whether a race day is open.
    ///
    /// Nothing is hidden by this and nothing is reordered permanently. The
    /// Tonight pages move to a group of their own at the top while a day is
    /// open, and the club's own order for everything else - and for those
    /// pages once the day closes - is exactly as it was.
    /// </summary>
    public static NavTier TierOf(string page, bool raceNight) =>
        raceNight && TonightPages.Contains(page, StringComparer.OrdinalIgnoreCase)
            ? NavTier.Tonight
            : TierOf(page);

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
        _ => "ESSENTIALS",
    };

    /// <summary>The heading a page sits under - what the sidebar asks for each row.</summary>
    public static string SectionOf(string page) => HeadingFor(TierOf(page));

    /// <summary>The same, given whether a race day is open.</summary>
    public static string SectionOf(string page, bool raceNight) => HeadingFor(TierOf(page, raceNight));
}
