namespace HeatCaller;

/// <summary>Where the inbox is: closed, a window of its own, or in the dock down the right.</summary>
public enum InboxPlace
{
    Closed,
    Window,
    Docked,
}

/// <summary>
/// The parts of the view Focus changes: the sidebar narrowed to its icons,
/// the column under Race Control's clock folded, and where the inbox is.
/// </summary>
internal readonly record struct FocusViewState(bool NavNarrowed, bool SideFolded, InboxPlace Inbox);

/// <summary>What was asked for from the Focus button or its menu.</summary>
internal enum FocusAsk
{
    /// <summary>The button itself: Focus, or Restore while focused.</summary>
    Focus,

    /// <summary>The menu's "Wide board only": the sidebar and the clock column, the inbox left as it is.</summary>
    WideOnly,

    /// <summary>The menu's "Inbox only": the inbox docked or undocked, the board left as it is.</summary>
    InboxOnly,
}

/// <summary>
/// FOCUS, ONE PRESS (24 Nov 2026, asked for by Asher: "I want to have one
/// button press that expands the leaderboard and loads in the docked inbox
/// with a click"; option B of six, plans/race-control-foot-plan.md). It took
/// two presses in the right order - Expand, then the dock square - with a
/// jump at each.
///
/// The rules, kept away from the window so they are tested without one:
/// Focus narrows the sidebar, folds the clock column and docks the inbox
/// (opening it if it is closed), and remembers what the view was. Restore
/// puts back exactly that: an inbox that was a window of its own goes back
/// to being one, a closed one closes, a sidebar that was already narrow
/// stays narrow. A change made by hand while focused - the inbox undocked,
/// the sidebar opened - drops what was remembered, so Restore never undoes
/// something the director did themselves.
/// </summary>
internal static class FocusView
{
    /// <summary>The view Focus gives.</summary>
    public static FocusViewState Focused => new(NavNarrowed: true, SideFolded: true, InboxPlace.Docked);

    /// <summary>
    /// What Restore gives with nothing remembered (the app was closed while
    /// focused, or the view was changed by hand): everything open, and the
    /// inbox in a window of its own - Restore takes it out of the dock.
    /// </summary>
    public static FocusViewState Plain => new(NavNarrowed: false, SideFolded: false, InboxPlace.Window);

    /// <summary>The view is the one Focus gives.</summary>
    public static bool IsFocused(FocusViewState now) => now == Focused;

    /// <summary>The Focus button's caption.</summary>
    public static string Caption(FocusViewState now) => IsFocused(now) ? "Restore" : "Focus";

    /// <summary>
    /// The view <paramref name="ask"/> leads to from <paramref name="now"/>,
    /// and what is remembered afterwards.
    /// </summary>
    public static (FocusViewState Target, FocusViewState? Before) After(FocusAsk ask, FocusViewState now, FocusViewState? before)
    {
        switch (ask)
        {
            case FocusAsk.Focus when IsFocused(now):
                return (before ?? Plain, null);

            case FocusAsk.Focus:
                return (Focused, now);

            case FocusAsk.WideOnly:
            {
                // As Expand always did: both folded opens both, else both fold.
                var wide = !(now.NavNarrowed && now.SideFolded);
                var target = now with { NavNarrowed = wide, SideFolded = wide };
                return (target, Kept(before, target));
            }

            default:
            {
                var target = now with { Inbox = now.Inbox == InboxPlace.Docked ? InboxPlace.Window : InboxPlace.Docked };
                return (target, Kept(before, target));
            }
        }
    }

    /// <summary>
    /// What is still remembered once the view is <paramref name="now"/>:
    /// the view before Focus while it is still focused, nothing once it is
    /// not. Run after every change to the view, by hand or not.
    /// </summary>
    public static FocusViewState? Kept(FocusViewState? before, FocusViewState now) => IsFocused(now) ? before : null;

    /// <summary>The widest the docked inbox is while focused, at 100% - the mockup's.</summary>
    public const int FocusedInboxWidest = 400;

    /// <summary>
    /// How wide the docked inbox is while focused: the club's own width (the
    /// splitter's, AppSettings.InboxDockWidth), but never wider than
    /// <paramref name="widest"/> - so Focus always leaves the board wider than
    /// it was. With the default 520, the board came out NARROWER than before
    /// the press at 1440 wide: the flag buttons wrapped and the Next Up line
    /// was cut short. Never narrower than the inbox's own least.
    /// </summary>
    public static int FocusedInboxWidth(int saved, int widest, int least) => Math.Max(least, Math.Min(saved, widest));

    /// <summary>
    /// Whether the saved "keep the inbox docked" choice has to be put back
    /// after a Restore: Focus docks the inbox through the same saved choice
    /// as the tick at the inbox's foot, so one that was closed, or in a
    /// window, is opened next time where it was before Focus.
    /// </summary>
    public static bool DockSavedAfterRestore(InboxPlace restoredTo, bool savedBeforeFocus) =>
        restoredTo switch
        {
            InboxPlace.Docked => true,
            InboxPlace.Window => false,
            _ => savedBeforeFocus,
        };
}
