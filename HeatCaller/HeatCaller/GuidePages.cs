namespace HeatCaller;

/// <summary>
/// WHICH PAGE OF THE USER GUIDE EXPLAINS WHICH PAGE OF THE APP.
///
/// WHY (25 Nov 2026, Asher: "put the guide online and link the app to it,
/// then find a better place in the app for people to find"): the guide went
/// up at heatcaller.com/docs/, and the only way to it from the app was a link
/// two clicks deep in the About box, to the home page. The User Guide button
/// at the foot of the sidebar opens the guide at the page you're looking at,
/// so this says which one that is.
///
/// Each address is a guide page's folder under <see cref="AppSupport.GuideUrl"/>,
/// as MkDocs builds it from website/docs - whole pages only, not "#" headings,
/// which move whenever a heading's wording changes. A test checks every one
/// is a page in website/mkdocs.yml's nav, so a renamed guide page can't leave
/// the button pointing at nothing.
/// </summary>
internal static class GuidePages
{
    /// <summary>Each sidebar page, by the name it has in the sidebar, and its guide page.</summary>
    internal static readonly IReadOnlyDictionary<string, string> ByPage = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Race Control"] = "race-control/running-a-race/",
        ["Race Day"] = "race-day/program-and-mains/",
        ["Championship"] = "race-control/results-and-championships/",
        ["Driver Hub"] = "race-day/drivers-and-classes/",
        ["Classes"] = "race-day/drivers-and-classes/",
        ["Rentals"] = "race-day/drivers-and-classes/",
        ["Handicaps"] = "race-control/practice-and-ghosts/",
        ["Track Layouts"] = "race-day/track-designer/",
        ["Race History"] = "race-control/results-and-championships/",
        ["Race Analytics"] = "race-control/results-and-championships/",
        ["Achievements"] = "race-control/results-and-championships/",
        ["Garage"] = "race-day/drivers-and-classes/",
        ["Practice"] = "race-control/practice-and-ghosts/",
        ["Ghost Cars"] = "race-control/practice-and-ghosts/",
        ["Demo Mode"] = "race-control/practice-and-ghosts/",
        ["Venues"] = "race-day/drivers-and-classes/",
        ["Themes"] = "screens/looks-and-logo/",
        ["Sponsors"] = "screens/big-screen/",
        ["Local Screens"] = "screens/phone-and-web/",
        ["Club Website"] = "online/setup/",
        ["Voice"] = "announcer/voice/",
        ["Sounds"] = "announcer/sounds-and-music/",
        ["Connection"] = "timing/decoders/",
        ["Backup & Restore"] = "admin/backups/",
    };

    /// <summary>The guide's address for <paramref name="page"/>, or the guide's home for a page it has no page of its own for.</summary>
    internal static string For(string? page) =>
        page is not null && ByPage.TryGetValue(page, out var path) ? AppSupport.GuideUrl + path : AppSupport.GuideUrl;
}
