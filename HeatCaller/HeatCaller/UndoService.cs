namespace HeatCaller;

/// <summary>
/// Holds the most recent undoable destructive actions, app-wide, newest
/// first, so the footer Undo button (and Ctrl+Z) can go back one step at a
/// time - or several at once from the button's right-click list.
///
/// Depth is capped (see MaxDepth) rather than unbounded: each entry holds a
/// captured snapshot of whatever it needs to restore, so keeping them
/// forever would pin that data in memory indefinitely. 25 is enough to walk
/// back a run of edits made in a hurry on race day, while an undo from much
/// further back than that stops being meaningful once other changes have
/// been made on top of it. Files an action would have deleted are held on
/// disk, not here - see UndoHoldingFolder.
/// </summary>
public sealed class UndoService
{
    /// <summary>How many steps back can be undone.</summary>
    public const int MaxDepth = 25;

    private sealed record UndoEntry(string Description, Action Action);

    // Index 0 is the most recent - undo works from the front.
    private readonly List<UndoEntry> _entries = new();

    /// <summary>Raised whenever the stack changes, so the UI can update its Undo affordance.</summary>
    public event Action? StateChanged;

    /// <summary>Raised when an undo action throws, so the failure is visible instead of silent.</summary>
    public event Action<string, Exception>? UndoFailed;

    /// <summary>What the next Undo would reverse, e.g. "Check Out Everyone". Empty when nothing is pending.</summary>
    public string Description => _entries.Count > 0 ? _entries[0].Description : string.Empty;

    public bool CanUndo => _entries.Count > 0;

    public int Count => _entries.Count;

    /// <summary>Descriptions of everything currently undoable, newest first - for showing the user what's available.</summary>
    public IReadOnlyList<string> PendingDescriptions => _entries.Select(e => e.Description).ToList();

    /// <summary>
    /// Pushes an action onto the undo stack. Call this immediately BEFORE
    /// performing the destructive change, capturing whatever state is
    /// needed to put things back.
    /// </summary>
    public void Capture(string description, Action undoAction)
    {
        _entries.Insert(0, new UndoEntry(description, undoAction));

        // Drop anything past the cap.
        while (_entries.Count > MaxDepth)
        {
            _entries.RemoveAt(_entries.Count - 1);
        }

        RaiseStateChanged();
    }

    /// <summary>Runs the most recent undo. Returns what was undone, or null if nothing was pending or it failed.</summary>
    public string? Undo()
    {
        if (_entries.Count == 0)
        {
            return null;
        }

        var entry = _entries[0];

        // Popped before running so a failure part-way through doesn't leave
        // a stale entry pointing at state that's already changed.
        _entries.RemoveAt(0);
        RaiseStateChanged();

        try
        {
            entry.Action();
        }
        catch (Exception ex)
        {
            // Report rather than swallow. A silently-failing undo is far
            // worse than a loud one: a captured action that referenced a
            // disposed dialog threw here for a while, and because the
            // failure was hidden the undo simply appeared to do nothing.
            RaiseUndoFailed(entry.Description, ex);
            return null;
        }

        return entry.Description;
    }

    /// <summary>
    /// Undoes the newest <paramref name="count"/> steps, newest first -
    /// the order they have to come off in, since a later change may sit on
    /// top of an earlier one. Stops at the first that fails (it is reported
    /// through UndoFailed like any other), because carrying on past a
    /// broken step would be undoing older changes onto state that step was
    /// meant to have put back. Returns what was undone, in order; empty
    /// when nothing was.
    /// </summary>
    public IReadOnlyList<string> UndoSteps(int count)
    {
        var undone = new List<string>();

        for (var i = 0; i < count && _entries.Count > 0; i++)
        {
            var description = Undo();

            if (description is null)
            {
                break;
            }

            undone.Add(description);
        }

        return undone;
    }

    public void Clear()
    {
        _entries.Clear();
        RaiseStateChanged();
    }

    /// <summary>
    /// Raises StateChanged one handler at a time, so a handler that throws
    /// (a tooltip on a control being disposed at shutdown, say) can't stop
    /// the others hearing about it - or escape into Capture or Undo, where
    /// it would abandon a destructive action half-done or an undo half-run
    /// after the stack had already changed.
    /// </summary>
    private void RaiseStateChanged()
    {
        if (StateChanged is not { } handlers)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch
            {
                // The stack is what matters; a stale Undo button is not.
            }
        }
    }

    /// <summary>The same protection for UndoFailed: reporting a failure must not become one.</summary>
    private void RaiseUndoFailed(string description, Exception failure)
    {
        if (UndoFailed is not { } handlers)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action<string, Exception>>())
        {
            try
            {
                handler(description, failure);
            }
            catch
            {
                // Nowhere left to report it.
            }
        }
    }
}
