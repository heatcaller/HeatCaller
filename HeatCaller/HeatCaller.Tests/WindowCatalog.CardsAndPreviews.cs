namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// Two windows the sweep had never built (18 Nov 2026, dialog jazz-up):
    /// a driver's report card (ReportCardForm), from a right-click in Driver
    /// Hub or Race History - with a full card, the longest line long enough
    /// to wrap, and with nothing to report; and the sponsor video preview
    /// (SponsorVideoPreviewForm), from the Sponsors page, which is built
    /// but never shown here, so the video player is never started. Neither
    /// is on LayoutAudit.ConvertedWindows, so their findings are in the
    /// report without failing the test.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> CardAndPreviewWindows() => new (string, Func<Form>)[]
    {
        ("Report card", () => new ReportCardForm(
            "Alexandria Montgomery-Fitzwilliam",
            new DateTime(2026, 10, 22),
            "Wed 22 Oct",
            new[]
            {
                "Your best lap was 14.212s, 0.31s off the class's fastest - closer than last week.",
                "Your laps were steady: 80% within half a second of your average.",
                "Sector 2 cost you most: 0.4s a lap against your own best sector times there, the hairpin before the long straight.",
                "You ran clean - no laps more than three seconds slower than your average.",
            })),
        ("Report card (nothing to report)", () => new ReportCardForm(
            "Sam", new DateTime(2026, 10, 22), "Wed 22 Oct", Array.Empty<string>())),
        ("Sponsor video preview", () => new SponsorVideoPreviewForm(
            @"C:\Sponsors\Springfield Hobby Shop - spring sale 30s.mp4")),
    };
}
