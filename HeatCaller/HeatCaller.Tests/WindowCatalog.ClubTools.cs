namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// Three windows a club opens beside the racing rather than as part of
    /// it, each always the same shape: the ? cheat sheet (ShortcutSheetForm),
    /// every shortcut in the app in two columns; the glossary from Next
    /// Steps' Glossary (RacingWordsForm), every word; and the scan-to-join
    /// poster (JoinPosterForm, titled "&lt;heading&gt; - scan to join"),
    /// shown modeless from Who's Racing's Self Check-In Poster... and from
    /// Club Website's Show Poster - the second with the longest line under
    /// its code and a long address, which change only the picture drawn on
    /// it, not where anything goes. None of them reads a store.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> ClubToolWindows() => new (string, Func<Form>)[]
    {
        ("Keyboard shortcuts", () => new ShortcutSheetForm()),
        ("What these words mean", () => new RacingWordsForm()),
        ("Scan to join", () => new JoinPosterForm(
            "http://192.168.1.42:8080/checkin",
            "CHECK IN",
            "Scan, find your name, tap it. Same Wi-Fi as the timing laptop.")),
        ("Scan to join (club website)", () => new JoinPosterForm(
            "https://results.example-club.org/c/springfield-mini-z-racing-club-tuesday-nights",
            "FOLLOW THE RACING",
            "Scan with your phone camera. Live timing, schedule and results - from anywhere, on any network.")),
    };
}
