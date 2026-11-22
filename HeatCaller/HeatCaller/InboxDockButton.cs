namespace HeatCaller;

/// <summary>
/// DOCK INBOX ON RACE CONTROL (22 Nov 2026, asked for by Asher: "make a
/// button next to expand on race control to dock the inbox").
///
/// WHY: the inbox could only be docked from inside itself - open it from
/// the flag row (and only while something was waiting), then tick "Dock in
/// the main window" at its foot. The race director who wants it beside the
/// board all night had no one press for that from Race Control.
///
/// The button's rules, away from the window so they can be tested: what it
/// reads and what a press does both follow what is ON SCREEN - the inbox
/// open and in the dock - not the saved choice alone. A docked inbox that
/// was closed leaves the saved choice on, and a button that then read
/// "Undock Inbox" would be offering to undock nothing.
/// </summary>
internal static class InboxDockButton
{
    /// <summary>The button's tooltip: its name, then where the inbox goes.</summary>
    internal static string Tip(bool dockedOnScreen) => dockedOnScreen
        ? "Undock Inbox - the inbox goes back to a window of its own."
        : "Dock Inbox - the inbox opens down the right of this window, beside the board.";

    /// <summary>The button's name - its tooltip's first words, and what a screen reader says: what a press would do.</summary>
    internal static string Caption(bool dockedOnScreen) => dockedOnScreen ? "Undock Inbox" : "Dock Inbox";

    /// <summary>
    /// What a press asks for: docked (true) whenever it is not docked on
    /// screen now - closed, or in a window of its own - and back to a
    /// window of its own (false) when it is.
    /// </summary>
    internal static bool DockOnPress(bool dockedOnScreen) => !dockedOnScreen;

    /// <summary>Whether the inbox is docked on screen: open, and in the main window's dock rather than a window of its own.</summary>
    internal static bool DockedOnScreen(bool open, bool inItsOwnWindow) => open && !inItsOwnWindow;
}
