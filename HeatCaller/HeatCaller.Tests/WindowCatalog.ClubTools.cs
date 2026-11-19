namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// Five windows a club opens beside the racing rather than as part of
    /// it, each always the same shape: the ? cheat sheet (ShortcutSheetForm),
    /// every shortcut in the app in two columns; the glossary from Next
    /// Steps' Glossary (RacingWordsForm), every word; the About box from
    /// Backup &amp; Restore's Getting help card (AboutForm), every
    /// third-party component with its licence; the license and privacy
    /// notice (TermsForm), both the startup gate with its two ticks and the
    /// read-again window; and the scan-to-join
    /// poster (JoinPosterForm, titled "&lt;heading&gt; - scan to join"),
    /// shown modeless from Who's Racing's Self Check-In Poster... and from
    /// Club Website's Show Poster - the second with the longest line under
    /// its code and a long address, which change only the picture drawn on
    /// it, not where anything goes. None of them reads a store, but for
    /// the terms window reading when its text was accepted.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> ClubToolWindows() => new (string, Func<Form>)[]
    {
        ("Keyboard shortcuts", () => new ShortcutSheetForm()),
        ("What these words mean", () => new RacingWordsForm()),
        ("About HeatCaller", () => new AboutForm()),
        ("HeatCaller license and privacy notice (asking)", () => new TermsForm(asking: true)),
        ("HeatCaller license and privacy notice (to read again)", () => new TermsForm(asking: false)),
        ("What HeatCaller sends", () => new UsagePreviewForm(SampleUsage(), on: true)),
        ("What HeatCaller sends (unticked)", () => new UsagePreviewForm(SampleUsage(), on: false)),
        ("Scan to join", () => new JoinPosterForm(
            "http://192.168.1.42:8080/checkin",
            "CHECK IN",
            "Scan, find your name, tap it. Same Wi-Fi as the timing laptop.")),
        ("Scan to join (club website)", () => new JoinPosterForm(
            "https://results.example-club.org/c/springfield-mini-z-racing-club-tuesday-nights",
            "FOLLOW THE RACING",
            "Scan with your phone camera. Live timing, schedule and results - and your own races under Me.")),
    };

    /// <summary>A few days' usage totals for See What's Sent, held in memory only (never saved).</summary>
    private static AppUsage SampleUsage()
    {
        var usage = new AppUsage(Path.Combine(Path.GetTempPath(), "hc-catalog-usage-never-saved.json"), () => [], "0.63.0");
        var context = new AppUsage.Context(RaceDay: true, DeskView: false);
        var at = new DateTime(2026, 11, 18, 18, 0, 0);

        foreach (var (page, tab) in new[] { ("Race Control", ""), ("Who's Racing", "Entries"), ("Setup", "Tonight"), ("Live Timing", "") })
        {
            usage.Enter(page, tab, at, context);
            at = at.AddSeconds(45);
            usage.Mark(at, inFront: true, active: true, context);
        }

        return usage;
    }
}
