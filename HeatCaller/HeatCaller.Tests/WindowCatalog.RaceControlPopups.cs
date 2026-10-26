namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// The two small windows Race Control opens from its leaderboard: the
    /// seconds its Penalty... asks for (RaceControlControl.TimePenaltyForm),
    /// which shows the same whoever it is for; and the reason its warnings
    /// list's "Other reason..." asks for (RaceControlControl.WarningReasonForm),
    /// titled with the driver's name - the longest name there is, and a short
    /// one. Neither needs a race on the board to be built.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> RaceControlPopupWindows() => new (string, Func<Form>)[]
    {
        ("Time penalty", () => new RaceControlControl.TimePenaltyForm()),
        ("Warning for", () => new RaceControlControl.WarningReasonForm(Drivers(12)[11].DriverName)),
        ("Warning for (short name)", () => new RaceControlControl.WarningReasonForm(Drivers(1)[0].DriverName)),

        // Coach (name) > Target lap... (22 Oct 2026), titled with the
        // driver's name - the longest there is.
        ("Target lap for", () => new RaceControlControl.TargetLapForm(Drivers(12)[11].DriverName, 9_200)),

        // The Broadcast window (25 Oct 2026), from Race Control's Broadcast
        // button: the announcer on and held - Hold reading "HELD - Release",
        // its widest caption, with both counts showing and four lines lost,
        // so the readings under the row run to two lines (Replay Moment
        // stays hidden: the test has no race recording to replay); and the
        // announcer switched off, when the window says so and little else.
        ("Broadcast", () => BroadcastWindowFor(held: true)),
        ("Broadcast (the announcer off)", () => BroadcastWindowFor(held: false, off: true)),
    };

    /// <summary>
    /// A Broadcast window filled as Race Control's clock fills it. The
    /// director is a real one on a blank club, as the commentary tests make
    /// it; nothing is spoken or written. Held by setting the row's own flag,
    /// since its Hold button cannot be pressed in a window not on screen.
    /// </summary>
    private static BroadcastWindow BroadcastWindowFor(bool held, bool off = false)
    {
        var settings = new AppSettings();
        var director = new CommentaryDirector(new CommentaryService(ProfileStore.Load(), settings), settings);
        var load = new AnnouncerLoad();

        for (var i = 0; i < 12; i++)
        {
            load.NoteOffered();
        }

        for (var i = 0; i < 4; i++)
        {
            load.NoteDropped();
        }

        var strip = new AnnouncerStrip();
        var window = new BroadcastWindow(strip);

        if (held)
        {
            typeof(AnnouncerStrip)
                .GetField("_holding", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(strip, true);
            strip.Restyle();
        }

        strip.Show(off ? null : director, !off, speaking: false, onAir: null, muted: false, notesWaiting: 12, votesIn: 148, load: load);
        window.ShowState(off ? null : director, !off, speaking: false, muted: false, notesWaiting: 12, votesIn: 148, load: load,
            recapRunning: true, replaying: false, momentHeld: false);

        return window;
    }
}
