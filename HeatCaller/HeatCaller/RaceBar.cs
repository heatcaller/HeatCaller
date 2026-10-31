namespace HeatCaller;

/// <summary>The buttons on Race Control's flag row, one bit each, in F-key order.</summary>
[Flags]
internal enum RaceBarButtons
{
    None = 0,

    // 1 << 0 was On Air, which left the row on 25 Oct 2026, came back for a
    // moment on 26 Oct and went again the same evening (Asher: "remove the on
    // air flag like button") - see RaceControlControl.OnAir. Not reused.
    Stage = 1 << 1,
    Countdown = 1 << 2,
    Green = 1 << 3,
    Yellow = 1 << 4,
    Red = 1 << 5,
    Checkered = 1 << 6,
    Reset = 1 << 7,

    // After the flag (25 Oct 2026) - the mockup's After row. Not flags.
    //
    // 1 << 8 was Next Heat and 1 << 9 Results, which left the row on 31 Oct
    // 2026 (Asher: "you can remove the next heat button as the button above
    // it and to the right does that now", and Results went to Race History
    // as Edit Result does). Not reused.

    /// <summary>The race just run, in Race History, with Edit Result open.</summary>
    EditResult = 1 << 10,

    // Between races (31 Oct 2026) - the announcer's after-race talk.

    /// <summary>Skip: press for the next part of the show, hold for the end of it. F9.</summary>
    Skip = 1 << 11,

    /// <summary>Hold Talk: no talk between races until it is pressed again. F10.</summary>
    HoldTalk = 1 << 12,

    /// <summary>Call Up: the next race's drivers and marshals called now. F12.</summary>
    CallUp = 1 << 13,
}

/// <summary>Where the race is, as the flag row names it at its left.</summary>
public enum RacePhase
{
    Ready,
    WarmUp,
    Staging,
    Countdown,
    Racing,
    Caution,
    Stopped,
    Finished,
    Practice,
}

/// <summary>
/// THE MORPHING BAR (25 Oct 2026, as chosen on the Race Control layouts
/// canvas; the sets made steady on 26 Oct 2026 - Asher: "which version the
/// race control morphs into is all over the place"). The flag row shows only the buttons that mean something at this
/// moment of the race, worked out here from the race's own state - so it
/// can be tested without a window, and so the buttons follow the race
/// whichever way it moved (a button, a key, the phone, the start sequence).
///
/// Hiding a button takes nothing away from the keys or the phone: F1-F7
/// and the remote call the same actions as before, whatever is on screen.
///
///   Before the green   Stage, Countdown, Green, Reset - from a cold
///                      start, and the same through On Air, a warm-up,
///                      staging and the countdown, until the race goes green
///                      ("they all remain up until green gets pressed or is
///                      moved on to naturally"). Green because a race can be
///                      started straight from here. A yellow or a red before
///                      the green is still F4 or F5, and the phone.
///   Racing             Green, Yellow, Red, Checkered, Reset - the same five
///                      from the green to the flag, caution or not ("once
///                      the race is green the buttons change to green,
///                      yellow, red, checkered, reset"); Green restarts the
///                      race after a yellow or a red.
///   Finished           Skip, Hold Talk, Call Up, Edit Result, Reset -
///                      the after-race talk and the race just run (31 Oct
///                      2026: Next Heat left the row, Start Next on the Next
///                      Up bar doing the same, and Results with it, Edit
///                      Result going to the same place). With the talk
///                      buttons switched off on the Announcer page, Edit
///                      Result and Reset, with Stage when the program has
///                      nothing next (a Quick Race night) so the next race
///                      can still be started from the row.
///                      Reset goes back to the set before the green.
///   Practice           nothing: practice is run from the Practice page,
///                      and every flag is refused while it runs
///
/// On Air has no button on the row (25 Oct 2026, and again on 26 Oct after
/// it came back for an hour): its writing, voicing and show are on the strip
/// under the row, and O starts and skips it.
/// </summary>
internal static class RaceBar
{
    /// <summary>
    /// The most buttons any moment shows: racing. The flag row
    /// keeps room for this many whatever is showing, so it is the same height
    /// - and Now Playing the same width - from one phase to the next.
    /// </summary>
    internal const int MostShown = 5;

    /// <summary>The buttons to show.</summary>
    /// <param name="nextWaiting">Whether the program has something next for Start Next to start - see RaceControlControl.UpdateNextUpBar.</param>
    /// <param name="talkButtons">Skip, Hold Talk and Call Up after the flag - the Announcer page's Between races tick.</param>
    internal static RaceBarButtons ButtonsFor(RaceState state, FlagState flag, bool practice, bool warmingUp,
        bool nextWaiting = true, bool talkButtons = true)
    {
        if (practice)
        {
            return RaceBarButtons.None;
        }

        // A warm-up runs between races, before the Stage it ends with: the
        // race has not gone green, so the set before the green.
        if (warmingUp && state is RaceState.Idle or RaceState.Finished or RaceState.Staging)
        {
            return BeforeTheGreen;
        }

        return state switch
        {
            // Through staging and the countdown the row does not move - the
            // same five as a cold start, until the green.
            RaceState.Staging or RaceState.Countdown => BeforeTheGreen,

            RaceState.Racing => RaceBarButtons.Green | RaceBarButtons.Yellow | RaceBarButtons.Red
                                | RaceBarButtons.Checkered | RaceBarButtons.Reset,

            // After the flag: what is done with the race just run, and the
            // way on to the next one.
            RaceState.Finished => talkButtons
                ? AfterTheFlag
                : (nextWaiting ? RaceBarButtons.None : RaceBarButtons.Stage) | RaceBarButtons.EditResult | RaceBarButtons.Reset,

            // Idle - a cold start, or after a Reset: the next race's buttons.
            _ => BeforeTheGreen,
        };
    }

    /// <summary>The row after the flag: Skip, Hold Talk, Call Up, Edit Result, Reset.</summary>
    internal const RaceBarButtons AfterTheFlag =
        RaceBarButtons.Skip | RaceBarButtons.HoldTalk | RaceBarButtons.CallUp | RaceBarButtons.EditResult | RaceBarButtons.Reset;

    /// <summary>The row from a cold start until the race goes green: Stage, Countdown, Green, Reset.</summary>
    internal const RaceBarButtons BeforeTheGreen =
        RaceBarButtons.Stage | RaceBarButtons.Countdown | RaceBarButtons.Green | RaceBarButtons.Reset;

    /// <summary>Where the race is.</summary>
    internal static RacePhase PhaseFor(RaceState state, FlagState flag, bool practice, bool warmingUp)
    {
        if (practice)
        {
            return RacePhase.Practice;
        }

        if (warmingUp && state is RaceState.Idle or RaceState.Finished or RaceState.Staging)
        {
            return RacePhase.WarmUp;
        }

        return state switch
        {
            RaceState.Staging => RacePhase.Staging,
            RaceState.Countdown => RacePhase.Countdown,
            RaceState.Racing => flag switch
            {
                FlagState.Yellow => RacePhase.Caution,
                FlagState.Red => RacePhase.Stopped,
                _ => RacePhase.Racing,
            },
            RaceState.Finished => RacePhase.Finished,
            _ => RacePhase.Ready,
        };
    }

    /// <summary>The phase in a word, as the row shows it (in capitals, spaced).</summary>
    internal static string Word(RacePhase phase) => phase switch
    {
        RacePhase.WarmUp => "Warm-up",
        RacePhase.Staging => "Staging",
        RacePhase.Countdown => "Countdown",
        RacePhase.Racing => "Racing",
        RacePhase.Caution => "Caution",
        RacePhase.Stopped => "Stopped",
        RacePhase.Finished => "Finished",
        RacePhase.Practice => "Practice",
        _ => "Ready",
    };

    /// <summary>Every word the row can show, so its column is measured for the widest and never moves.</summary>
    internal static IEnumerable<string> AllWords => Enum.GetValues<RacePhase>().Select(Word);

    /// <summary>
    /// What the hold strip says at rest - which of the buttons on the row now
    /// want holding, or nothing when none of them is there. During practice,
    /// where the row is empty, it says where practice is run from instead.
    /// </summary>
    internal static string RestingHint(RaceBarButtons shown, bool practice)
    {
        if (practice)
        {
            return "Practice is running - stop it, or clear it, on the Practice page.";
        }

        var held = new List<string>();

        if (shown.HasFlag(RaceBarButtons.Red))
        {
            held.Add("Red");
        }

        if (shown.HasFlag(RaceBarButtons.Checkered))
        {
            held.Add("Checkered");
        }

        if (shown.HasFlag(RaceBarButtons.Reset))
        {
            held.Add("Reset");
        }

        var hint = held.Count switch
        {
            0 => string.Empty,
            1 => $"{held[0]}: press and hold for a second. Everything else is one click.",
            _ => $"{string.Join(", ", held.Take(held.Count - 1))} and {held[^1]}: press and hold for a second. Everything else is one click.",
        };

        // Skip is both: a click for the next part of the show, a hold for
        // the end of it.
        if (shown.HasFlag(RaceBarButtons.Skip))
        {
            hint = (hint.Length > 0 ? hint.Replace(" Everything else is one click.", string.Empty, StringComparison.Ordinal) + " " : string.Empty)
                   + "Skip: click for the next part of the show, hold to end it.";
        }

        return hint;
    }
}
