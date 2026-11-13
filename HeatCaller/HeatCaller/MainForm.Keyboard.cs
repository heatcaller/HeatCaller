using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// MainForm's keyboard: the app-wide keys (ProcessCmdKey, MainForm_KeyDown),
/// the guided tour and the shortcut sheet.
///
/// Moved here unchanged from MainForm.cs on 26 Sep 2026, when the file was
/// split into parts. Fields with initializers stayed in MainForm.cs.
/// </summary>
public partial class MainForm
{
    /// <summary>
    /// Ctrl and + / - resize the whole app; Ctrl+0 puts it back to normal.
    ///
    /// Done in ProcessCmdKey rather than KeyDown because this has to win
    /// before the focused control sees the keystroke - a grid or a spinner
    /// would otherwise swallow it. Both the main and numeric-keypad keys are
    /// accepted, since which one arrives depends on the keyboard.
    ///
    /// F11 (and Esc, once in) toggles Race Mode from anywhere for the same
    /// reason - dialogs are separate forms, so their own Escape handling is
    /// untouched.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // The desk view lets typing through and nothing else (setup plan D8).
        if (_deskOnly && !DeskViewAllowsKey(keyData))
        {
            return true;
        }

        if (keyData == Keys.F11)
        {
            ToggleRaceMode();
            return true;
        }

        // F10, Hold Talk (31 Oct 2026). Here rather than in KeyDown: Windows
        // gives F10 to the menu bar, so it arrives as a system key that
        // KeyDown would see only after the menu had taken the focus. On Race
        // Control (or in Race Mode) only, as the flag keys are.
        if (keyData == Keys.F10 && (_raceMode || _shownPage == "Race Control"))
        {
            _raceControlControl.ToggleTalkHold();
            return true;
        }

        // Not while the focused control has its own use for Escape. This
        // runs before that control sees the key, so Escape meant to cancel
        // a grid cell's edit or close an open drop-down threw the whole
        // window out of Race Mode instead - and left the edit in place.
        // F11 still leaves from anywhere.
        if (keyData == Keys.Escape && _raceMode && !FocusedControlWantsEscape())
        {
            ToggleRaceMode();
            return true;
        }

        if ((keyData & Keys.Control) == Keys.Control)
        {
            var key = keyData & Keys.KeyCode;

            // Ctrl+Z is the app's Undo - unless a text box has the focus,
            // where it belongs to the box's own undo.
            // Not from the desk view either: there it is a text box's own
            // undo and nothing more, or a volunteer's Ctrl+Z on the grid
            // took back the race director's last change.
            if (keyData == (Keys.Control | Keys.Z) && !_deskOnly && !FocusedControlHasOwnUndo())
            {
                // Not btnUndo.PerformClick(): in Race Mode the footer is
                // hidden, and a hidden button ignores PerformClick.
                UndoSteps(1, " (Ctrl+Z)");
                return true;
            }

            // Ctrl+F: the page search at the top of the sidebar. With the
            // sidebar narrowed to its icons the box is hidden, so the sidebar
            // is opened first - the alternative is putting the focus in a box
            // nobody can see and typing into nothing.
            if (keyData == (Keys.Control | Keys.F) && !_raceMode)
            {
                if (_navIconsOnly)
                {
                    SetNavIconsOnly(false);
                }

                txtNavSearch.Focus();
                txtNavSearch.SelectAll();
                return true;
            }

            // Ctrl+B: the sidebar down to its icons, and back. Not in Race
            // Mode, where there is no sidebar to narrow - F11 took it away
            // altogether. Safe in a text box: Ctrl+B is not an editing key
            // in one, unlike Ctrl+Z, which is why that one asks first.
            if (keyData == (Keys.Control | Keys.B) && !_raceMode)
            {
                ToggleNavIconsOnly();
                return true;
            }

            // Ctrl+1 to Ctrl+9: that page in the sidebar, counting from the
            // top in the order the sidebar shows them (hidden pages skipped).
            // Not in Race Mode, which is one page on purpose. The rows on
            // screen, not the whole visible order: with a page search typed
            // the sidebar shows only the pages that match, and Ctrl+1 is the
            // first of those.
            if ((keyData & Keys.Modifiers) == Keys.Control && !_raceMode)
            {
                var index = key switch
                {
                    >= Keys.D1 and <= Keys.D9 => key - Keys.D1,
                    >= Keys.NumPad1 and <= Keys.NumPad9 => key - Keys.NumPad1,
                    _ => -1,
                };

                if (index >= 0)
                {
                    var pages = lstNav.Items;
                    if (index < pages.Count)
                    {
                        GoToPage(pages[index]);
                    }

                    return true;
                }
            }

            // Not with Alt held as well. AltGr arrives as Ctrl+Alt, so on
            // French, German and other European keyboards AltGr+0 ('@' or
            // '}') and AltGr on the '+' key ('~') matched here: the
            // character never reached the text box being typed in, and the
            // whole app's text size changed or snapped back to 100% instead.
            // Shift is still allowed, so Ctrl+Shift+'=' (which reads as
            // Ctrl++ on many keyboards) keeps growing the text as before.
            if ((keyData & Keys.Alt) == Keys.Alt)
            {
                return base.ProcessCmdKey(ref msg, keyData);
            }

            var step = key switch
            {
                Keys.Oemplus or Keys.Add => 0.1f,
                Keys.OemMinus or Keys.Subtract => -0.1f,
                _ => 0f,
            };

            if (step != 0f)
            {
                NudgeFontScale(TargetFontScale + step);
                return true;
            }

            if (key is Keys.D0 or Keys.NumPad0)
            {
                NudgeFontScale(1f);
                return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// F-key shortcuts for race control, active only when that page is
    /// showing (or Race Mode is on) - lets the person running the race trigger flags without
    /// having to click a specific small button, useful when they're also
    /// the one driving and only glancing at the screen. F-keys are safe to
    /// use globally like this since they never collide with normal text
    /// entry in any of the app's textboxes.
    /// </summary>
    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        // Never Ctrl or Alt. Alt+F4 is "close the window", and it used to
        // throw a yellow flag on its way out; the Ctrl shortcuts are handled
        // in ProcessCmdKey, before a focused grid or box can swallow them.
        // Shift is allowed through for the few keys that use it (Shift+P,
        // Shift+W, ?) - never with an F-key, which stays bare.
        if (e.Control || e.Alt)
        {
            return;
        }

        // The desk view's keys are typing and nothing else (setup plan D8).
        // A letter typed with the Who's Racing grid focused is not "typing
        // in a text field", so a bare M muted the PA mid-race and Shift+S
        // opened the setup wizard. Not marked handled: the key still goes
        // to whatever has the focus.
        if (_deskOnly)
        {
            return;
        }

        // A letter, unlike an F-key, IS text - so letters and symbols only
        // count when nothing that takes typing has the focus, or naming a
        // session "Wet track" would warn whoever was selected.
        var typing = IsTypingInTextField();

        // --- Anywhere in the app ---
        if (!typing)
        {
            if (e.KeyCode == Keys.OemQuestion && e.Shift)
            {
                ShowShortcutSheet();
                MarkHandled(e);
                return;
            }

            if (e.KeyCode == Keys.T && e.Shift)
            {
                StartTour();
                MarkHandled(e);
                return;
            }

            if (e.KeyCode == Keys.M && !e.Shift)
            {
                var mute = !_announcer.Muted;
                SetAllSoundMuted(mute);
                OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] {(mute ? "All sound muted" : "Sound back on")} (M).");
                MarkHandled(e);
                return;
            }

            // Shift+S, beside Shift+T for the tour: the two things that
            // walk you through something. Shifted, because the unshifted
            // letters belong to Race Control.
            if (e.KeyCode == Keys.S && e.Shift && _openRaceDaySetup is not null)
            {
                _openRaceDaySetup.Invoke(0);
                MarkHandled(e);
                return;
            }
        }

        // What is on screen, not what the sidebar has highlighted - the two
        // can differ (a button that opens Race Control from another page,
        // or Race Control hidden from the sidebar), and a flag key that
        // silently did nothing with the flags in plain view was the bug.
        // Race Mode counts whatever it shows: it exists to run the race.
        if (!_raceMode && _shownPage != "Race Control")
        {
            return;
        }

        var rc = _raceControlControl;

        // --- Function keys: bare only, and they work even mid-typing ---
        if (!e.Shift)
        {
            switch (e.KeyCode)
            {
                // THROUGH ON AIR (17 Oct 2026) when Race Control's "Start
                // races On Air" is ticked - the normal start - and straight
                // to Stage when it is not, or when On Air cannot open this
                // race. The Stage button itself still stages directly.
                //
                // NOT A SKIP ANY MORE (1 Nov 2026). Pressed again while On
                // Air ran, it skipped to Stage; skipping is now a hold of 0
                // (asked for by Asher: "you should have to press and hold to
                // skip"), so F1 only says so.
                case Keys.F1:
                    if (rc.OnAirRunning) rc.HintHoldToSkip(); else rc.TriggerStart();
                    e.Handled = true;
                    return;
                case Keys.F2: rc.TriggerCountdown(); e.Handled = true; return;
                case Keys.F3: rc.TriggerGreen(); e.Handled = true; return;
                case Keys.F4: rc.TriggerYellow(); e.Handled = true; return;
                case Keys.F5: rc.TriggerRed(); e.Handled = true; return;
                case Keys.F6: rc.TriggerCheckered(); e.Handled = true; return;
                case Keys.F7: rc.TriggerResetFromKeyboard(); e.Handled = true; return;
                case Keys.F8: rc.TriggerQuickRace(); e.Handled = true; return;

                // Between races (31 Oct 2026): Skip and Call Up, the flag
                // row's buttons after the flag. F10, Hold Talk, is taken in
                // ProcessCmdKey - see there.
                case Keys.F9: rc.SkipPart(); e.Handled = true; return;
                case Keys.F12: rc.CallUpNow(); e.Handled = true; return;
            }
        }

        if (typing)
        {
            return;
        }

        // --- Letters and symbols: Race Control and the selected driver ---
        var handled = true;

        switch (e.KeyCode)
        {
            case Keys.W:
                if (e.Shift) rc.TriggerTakeBackWarning(); else rc.TriggerWarning();
                break;

            case Keys.P:
                rc.TriggerPenalty(e.Shift ? 10000 : 5000);
                break;

            // "=" and "+" share a key; either, with or without Shift, and
            // the keypad plus.
            case Keys.Oemplus or Keys.Add:
                rc.TriggerLapAdjust(1);
                break;

            case Keys.OemMinus or Keys.Subtract when !e.Shift:
                rc.TriggerLapAdjust(-1);
                break;

            case Keys.C when !e.Shift: rc.TriggerClearPenalty(); break;
            case Keys.L when !e.Shift: rc.TriggerLapBreakdown(); break;
            case Keys.E when !e.Shift: rc.TriggerEditDriver(); break;
            case Keys.B when !e.Shift: rc.TriggerOpenBigScreen(); break;
            case Keys.R when !e.Shift: rc.TriggerReadinessCheck(); break;
            case Keys.A when !e.Shift: rc.TriggerAnnouncerScreen(); break;

            // Auto start's Hold / Resume and Start now (12 Nov 2026, setup
            // plan step 8, build plan 1.2). Letters, not F-keys: every bare
            // F-key is taken (F1-F10 and F12 on Race Control, F11 for Race
            // Mode), and the F-keys stay bare. H for Hold; N for Start now,
            // since S is half of Shift+S. Not while typing, like the other
            // letters - Start now starts a race, so a stray "n" in a notes
            // box must never do it.
            case Keys.H when !e.Shift: rc.TriggerAutoStartHold(); break;
            case Keys.N when !e.Shift: rc.TriggerAutoStartNow(); break;

            // On Air itself (17 Oct 2026), whatever "Start races On Air" is
            // set to - a key rather than Shift+F1, since the F-keys stay bare
            // (see the top of this method). 0 since 1 Nov 2026 (it was O,
            // which was confusing beside the Big Screen's own O): a tap
            // starts On Air, and while it runs 0 is HELD for a second to skip
            // to Stage - the KeyUp below lets go. Either 0, the top row's or
            // the keypad's. Shift+0 is ")" and does nothing here. The
            // warning menu's own 0 (W, then 0 for another reason) is the
            // menu's: while a menu is open it has the keyboard, not the form.
            case Keys.D0 or Keys.NumPad0 when !e.Shift: rc.OnAirKeyDown(); break;

            default:
                handled = false;
                break;
        }

        if (handled)
        {
            MarkHandled(e);
        }
    }

    /// <summary>
    /// A key let go (1 Nov 2026): 0, On Air's key, is held to skip On Air,
    /// so its let-go is passed on - early, it calls the skip off. Wherever
    /// the focus is and whatever page is showing: a let-go is always safe,
    /// and one missed would leave the next press taken for a repeat.
    /// </summary>
    private void MainForm_KeyUp(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is Keys.D0 or Keys.NumPad0)
        {
            _raceControlControl?.OnAirKeyUp();
        }
    }

    private static void MarkHandled(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private AppTour? _tour;

    /// <summary>
    /// Starts the app tour (Shift+T, and the end of first-run setup). Only
    /// one at a time: asking again while it is open brings it forward.
    /// </summary>
    private void StartTour()
    {
        if (_tour is { IsDisposed: false })
        {
            _tour.Activate();
            return;
        }

        if (_raceMode)
        {
            // Race Mode hides the sidebar the tour walks through.
            OnClientRawMessage($"{AppTime.LogStampNow()}  [tour] Leave Race Mode (F11) to take the tour.");
            return;
        }

        _tour = new AppTour(this, GoToPage);
        _tour.FormClosed += (_, _) => _tour = null;
        _tour.Show(this);
    }

    /// <summary>The ? cheat sheet - see ShortcutCatalog.</summary>
    private void ShowShortcutSheet()
    {
        using var sheet = new ShortcutSheetForm();
        sheet.ShowDialog(this);
    }

    /// <summary>
    /// True when the focused control takes typed keys - a text box, a
    /// number box, or any combo (a drop-down list jumps to the item a
    /// letter starts, so a letter there is still the user choosing, not a
    /// shortcut).
    /// </summary>
    private bool IsTypingInTextField()
    {
        Control? focused = ActiveControl;

        while (focused is ContainerControl { ActiveControl: { } inner })
        {
            focused = inner;
        }

        // A grid cell being edited types into an editing box the grid owns,
        // which ActiveControl does not reach - the grid itself says so.
        // TimePicker too: it takes typed digits and A/P, and without it here
        // a 0 typed into a race time on Race Control started On Air.
        return focused is TextBoxBase or UpDownBase or ComboBox or TimePicker
               || focused is DataGridView { IsCurrentCellInEditMode: true };
    }

    /// <summary>
    /// True when Ctrl+Z belongs to the focused control rather than to the
    /// app's Undo - see HasOwnUndo.
    /// </summary>
    private bool FocusedControlHasOwnUndo()
    {
        Control? focused = ActiveControl;

        while (focused is ContainerControl { ActiveControl: { } inner })
        {
            focused = inner;
        }

        return HasOwnUndo(focused);
    }

    /// <summary>
    /// True for a control with an undo of its own: a text box that can be
    /// typed in, a number box, a combo that can be typed in, or a grid cell
    /// being edited.
    ///
    /// Not IsTypingInTextField, which is about letter keys and so counts any
    /// combo and any text box. A drop-down list and a read-only box have no
    /// undo, so Ctrl+Z there went nowhere - and a drop-down is often exactly
    /// where the change to be undone was made (Race Day's event picker
    /// promises a switch made by accident is one Ctrl+Z back).
    /// </summary>
    internal static bool HasOwnUndo(Control? focused) =>
        focused is IDataGridViewEditingControl
        || focused is TextBoxBase { ReadOnly: false }
        || focused is UpDownBase
        || focused is ComboBox { DropDownStyle: not ComboBoxStyle.DropDownList }
        || focused is DataGridView { IsCurrentCellInEditMode: true };

    /// <summary>
    /// True when Escape means something to the focused control: a grid cell
    /// being edited (the key cancels the edit) or a drop-down that is open
    /// (the key closes it). A grid's editing box is a child of the grid, so
    /// it is what ActiveControl reaches, and it says so by what it is.
    /// </summary>
    private bool FocusedControlWantsEscape()
    {
        Control? focused = ActiveControl;

        while (focused is ContainerControl { ActiveControl: { } inner })
        {
            focused = inner;
        }

        return focused is IDataGridViewEditingControl
               || focused is DataGridView { IsCurrentCellInEditMode: true }
               || focused is ComboBox { DroppedDown: true };
    }
}
