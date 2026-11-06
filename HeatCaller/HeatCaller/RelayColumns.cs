namespace HeatCaller;

/// <summary>
/// The live board's columns on the club's website (5 Nov 2026, Asher: "add
/// column picker for the live board on the relay in website settings").
///
/// The relay has always shown the Live Results page's columns (WebKpis), as
/// the same document went to both. It now has a choice of its own,
/// AppSettings.RelayKpis, picked on Club Website - and while that is empty it
/// still follows the Live Results page, so nothing changes for a club that
/// never picks.
///
/// The transponder column never goes online, whatever is picked: a number
/// that identifies a driver's car is the club's, not the public's. The picker
/// leaves it out (LeftOut) and Resolve drops it again, for a list saved by
/// hand or before this.
/// </summary>
internal static class RelayColumns
{
    /// <summary>Columns the club's website never shows, so its picker does not offer them.</summary>
    public static readonly IReadOnlyList<string> LeftOut = new[] { "transponder" };

    /// <summary>
    /// The columns for this build. The LAN page (forRelay false) is the Live
    /// Results page's, as always; the relay's are its own pick, or the Live
    /// Results page's while it has none.
    /// </summary>
    public static List<KpiDefinition> Resolve(IEnumerable<string> webKpis, IEnumerable<string> relayKpis, bool forRelay)
    {
        var web = KpiCatalog.Resolve(webKpis, KpiCatalog.DefaultWeb);

        if (!forRelay)
        {
            return web;
        }

        var picked = Online(KpiCatalog.Resolve(relayKpis, Array.Empty<string>()));

        // Nothing usable picked - never set, or only a column that cannot go
        // online - is the Live Results page's, as before the picker.
        return picked.Count > 0 ? picked : Online(web);
    }

    /// <summary>
    /// What the picker opens on: the club's own pick, or else what the relay
    /// shows now - the Live Results page's columns - so opening it and
    /// pressing OK changes nothing.
    /// </summary>
    public static List<string> Current(IEnumerable<string> webKpis, IEnumerable<string> relayKpis) =>
        Resolve(webKpis, relayKpis, forRelay: true).Select(k => k.Key).ToList();

    private static List<KpiDefinition> Online(IEnumerable<KpiDefinition> columns) =>
        columns.Where(k => !LeftOut.Contains(k.Key, StringComparer.OrdinalIgnoreCase)).ToList();
}
