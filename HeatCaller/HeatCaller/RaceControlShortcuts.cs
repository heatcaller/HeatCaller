namespace HeatCaller;

/// <summary>
/// Keyboard entry points for Race Control, beyond the flag keys - see
/// ShortcutCatalog for the list and MainForm_KeyDown for the keys. Each one
/// does exactly what its button or menu item does.
///
/// The driver actions never put up the "select a racer first" box that the
/// buttons do: a dialog appearing from a stray key press mid-race is worse
/// than the key doing nothing, so they say it in the log instead.
/// </summary>
public sealed partial class RaceControlControl
{
    public void TriggerLapAdjust(int delta)
    {
        if (HasKeyboardSelection(delta > 0 ? "add a lap" : "remove a lap"))
        {
            AdjustSelected(delta, 0);
        }
    }

    public void TriggerPenalty(long penaltyMs)
    {
        if (HasKeyboardSelection($"give a {penaltyMs / 1000}s penalty"))
        {
            AdjustSelected(0, penaltyMs);
        }
    }

    public void TriggerClearPenalty()
    {
        if (HasKeyboardSelection("clear penalties"))
        {
            ClearSelectedPenalty();
        }
    }

    public void TriggerTakeBackWarning()
    {
        if (HasKeyboardSelection("take back a warning") && SelectedTransponder() is { } transponder)
        {
            TakeBackLastWarning(transponder);
        }
    }

    public void TriggerLapBreakdown()
    {
        if (HasKeyboardSelection("open a lap breakdown") && SelectedTransponder() is { Length: > 0 } transponder)
        {
            ShowLapPopup(transponder, _dgvLeaderboard.CurrentRow?.Index ?? -1);
        }
    }

    public void TriggerEditDriver()
    {
        if (HasKeyboardSelection("edit a driver"))
        {
            EditSelectedDriver();
        }
    }

    /// <summary>
    /// Exactly the Quick Race button, including its refusal while a session
    /// is under way. Through the button's action rather than PerformClick,
    /// which does nothing while this page is hidden - see _actQuickRace.
    /// </summary>
    public void TriggerQuickRace() => _actQuickRace?.Invoke();

    public void TriggerReadinessCheck() => ShowReadinessCheck();

    public void TriggerAnnouncerScreen() => AnnouncerRequested?.Invoke();

    /// <summary>
    /// F7. Reset is one key along from the checkered flag, so a race that
    /// is actually under way is asked about first. An idle or finished one
    /// resets straight away, as the button does - and Undo still has it.
    /// </summary>
    public void TriggerResetFromKeyboard()
    {
        if (_race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
        {
            // What the question was about. The box is modal but still runs
            // everything posted to this thread - a phone's Stage, a demo
            // ending - and a yes about one race must not reset another.
            // Stage, a fresh green and Reset all move the generation on, so
            // it says whether the race is still the one asked about; a start
            // that merely moved from staging to its countdown still is.
            var generationAsked = _race.SessionGeneration;

            var answer = AppDialog.Show(this,
                "A race is under way. Reset it? (Undo can still bring it back.)",
                "Reset race", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (answer != DialogResult.Yes)
            {
                return;
            }

            if (_race.SessionGeneration != generationAsked)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Did not reset - the session changed while the question was open "
                    + $"(it is now {_race.State}). Nothing was done; press F7 again if it is still wanted.");
                return;
            }
        }

        TriggerReset();
    }

    /// <summary>True when a driver is selected; otherwise says why the key did nothing.</summary>
    private bool HasKeyboardSelection(string what)
    {
        if (SelectedTransponder() is not null)
        {
            return true;
        }

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Can't {what} - no driver selected. Click one on the board first.");
        return false;
    }
}
