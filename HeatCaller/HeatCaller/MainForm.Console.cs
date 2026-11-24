using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// MainForm's console: logging lines to it, its filter and menu, and
/// redrawing it.
///
/// Moved here unchanged from MainForm.cs on 26 Sep 2026, when the file was
/// split into parts. Fields with initializers stayed in MainForm.cs.
/// </summary>
public partial class MainForm
{
    /// <summary>
    /// Every console line, unfiltered.
    ///
    /// Kept separately from what's displayed so a filter narrows the VIEW
    /// rather than throwing lines away - you can focus on one tag, then
    /// widen again with everything still there. Capped so a long race day
    /// doesn't grow it without limit.
    /// </summary>
    private readonly List<string> _consoleLines = new();

    private const int MaxConsoleLines = 5000;

    /// <summary>
    /// How far over the cap the console is allowed to run before it is
    /// trimmed back.
    ///
    /// This is the flicker.
    ///
    /// Trimming used to happen the moment the console went one line over
    /// 5000 - which, once it is full, is EVERY line. And trimming rebuilds
    /// the box: five thousand lines joined into a single ~400KB string and
    /// assigned to Text, which clears the control and refills it. During a
    /// race, with messages arriving several times a second, that is a
    /// multi-thousand-line text box being emptied and repopulated in front
    /// of you, continuously.
    ///
    /// Letting it run a block over the cap before trimming turns that into
    /// one rebuild per thousand lines instead of one per line.
    /// </summary>
    private const int ConsoleTrimBlock = 1000;

    /// <summary>
    /// Turns a control's painting off while its contents are replaced.
    ///
    /// A multiline TextBox is a native EDIT control. It does its own
    /// painting and ignores DoubleBuffered, so rewriting it shows as a
    /// clear followed by a refill however fast the two happen.
    /// WM_SETREDRAW is what stops that: painting is suppressed entirely
    /// while the text changes, and the refresh on the way out draws the
    /// finished result once.
    /// </summary>
    private static class NoRedraw
    {
        private const int WmSetRedraw = 0x000B;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public static void Begin(Control control)
        {
            // Nothing to suppress before the control has a window, and
            // asking for its Handle would force one into existence early.
            if (control.IsHandleCreated)
            {
                SendMessage(control.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
            }
        }

        public static void End(Control control)
        {
            if (!control.IsHandleCreated)
            {
                return;
            }

            SendMessage(control.Handle, WmSetRedraw, 1, IntPtr.Zero);

            // Refresh rather than Invalidate: the control has been blind to
            // every change just made to it, and a repaint queued for later
            // would leave it showing the old contents until something else
            // happened to be on screen.
            control.Refresh();
        }
    }

    /// <summary>
    /// Logs a line: to the session log file straight away, from whatever
    /// thread this is (see SessionLog), then to the console box.
    ///
    /// The file comes first and does not wait for the UI thread - when the
    /// window is frozen, the lines other threads go on logging still reach
    /// the file, and they are what shows what was happening around it.
    /// </summary>
    private void OnClientRawMessage(string line)
    {
        SessionLog.Write(line);
        ShowConsoleLine(line);
    }

    /// <summary>The console box half of OnClientRawMessage. Anything re-posting a line calls this, so the file gets it once.</summary>
    private void ShowConsoleLine(string line)
    {
        // Closing the window disposes the pages, and disposing Demo Mode
        // resets the race session - which logs, which lands here after the
        // console box is already gone. That threw ObjectDisposedException
        // out of Dispose and showed as a crash on exit.
        if (IsDisposed || Disposing || txtConsole.IsDisposed)
        {
            return;
        }

        // No handle yet - the constructor is still running, or the window
        // has not been shown. InvokeRequired answers false then even on a
        // background thread (there is no handle to ask), so the line used
        // to be written into the text box from whichever thread logged it.
        // Held instead, and shown when the handle arrives.
        if (!IsHandleCreated)
        {
            lock (_pendingConsoleLines)
            {
                _pendingConsoleLines.Add(line);
            }

            // The handle may have been created while that line was being
            // added, after OnHandleCreated had already drained the list.
            if (IsHandleCreated)
            {
                TryPost(DrainPendingConsoleLines);
            }

            return;
        }

        if (InvokeRequired)
        {
            TryPost(() => ShowConsoleLine(line));
            return;
        }

        _consoleLines.Add(line);

        if (_consoleLines.Count > MaxConsoleLines + ConsoleTrimBlock)
        {
            // Trimmed in a block rather than one line at a time - see
            // ConsoleTrimBlock. The list operation was already batched; it
            // was the REBUILD it triggers that was running per line.
            _consoleLines.RemoveRange(0, _consoleLines.Count - MaxConsoleLines);
            RenderConsole();
            return;
        }

        if (!ConsoleLineVisible(line))
        {
            return;
        }

        txtConsole.AppendText(line + Environment.NewLine);

        // AppendText already leaves the caret at the end and scrolls there,
        // so this is only needed when it did not - when the user has
        // clicked into the log and the selection was restored to where they
        // put it. Doing it unconditionally scrolled and repainted the box a
        // second time for every line.
        if (chkAutoScroll.Checked && txtConsole.SelectionStart != txtConsole.TextLength)
        {
            txtConsole.SelectionStart = txtConsole.TextLength;
            txtConsole.ScrollToCaret();
        }
    }

    /// <summary>Whether a line passes the current filter.</summary>
    private bool ConsoleLineVisible(string line)
    {
        var needle = CurrentConsoleFilter();

        if (string.IsNullOrWhiteSpace(needle))
        {
            return true;
        }

        // Several terms separated by spaces match ANY of them, so
        // "audio-wait audio-trace" shows both without regex.
        var terms = needle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matched = terms.Any(t => line.Contains(t, StringComparison.OrdinalIgnoreCase));

        return chkFilterExclude.Checked ? !matched : matched;
    }

    /// <summary>The active filter text - the preset unless something is typed.</summary>
    private string CurrentConsoleFilter()
    {
        var typed = txtConsoleFilter.Text.Trim();

        if (typed.Length > 0)
        {
            return typed;
        }

        // Parenthesised: "x as string switch" is ambiguous to the compiler,
        // which warns rather than guessing at the precedence.
        return (cmbConsolePreset.SelectedItem as string) switch
        {
            "Audio timing" => "[audio-wait] [audio-trace]",
            "Voice" => "[voice]",
            "Sound" => "[sound]",
            "Race" => "[race]",
            "Decoder" => "[decoded [raw HID]",
            "Storage" => "[storage]",
            "Hide raw HID" => "[raw HID]",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// The console's own right-click menu, drawn like the rest of the app.
    ///
    /// A read-only TextBox otherwise shows the stock Windows menu - grey,
    /// square-cornered, and offering Cut, Paste and Delete on a box that
    /// cannot be edited. Every other menu in the app is drawn by the theme,
    /// so this was the one place the illusion broke.
    ///
    /// The items are what a log actually needs: take a copy of what you can
    /// see, take a copy of everything, or clear it.
    /// </summary>
    private void BuildConsoleMenu(TextBox console)
    {
        var menu = new ContextMenuStrip();

        var copySelection = new ToolStripMenuItem("Copy");
        copySelection.Click += (_, _) => CopyToClipboard(console.SelectedText);

        var copyVisible = new ToolStripMenuItem("Copy everything shown");
        copyVisible.Click += (_, _) => CopyToClipboard(console.Text);

        var selectAll = new ToolStripMenuItem("Select all");
        selectAll.Click += (_, _) =>
        {
            console.Focus();
            console.SelectAll();
        };

        var clear = new ToolStripMenuItem("Clear the console");
        clear.Click += (_, _) =>
        {
            _consoleLines.Clear();
            console.Clear();
        };

        // The whole log, from the file rather than the box - which holds the
        // last few thousand lines, and has been cleared if somebody pressed
        // the item above - with names taken out. See SupportInfo.
        var saveSupport = new ToolStripMenuItem("Save support info...");
        saveSupport.Click += (_, _) => SupportInfo.Save(this, _settings, _voiceStore, _profiles, OnClientRawMessage);

        menu.Items.AddRange(new ToolStripItem[]
        {
            copySelection, copyVisible, new ToolStripSeparator(), selectAll, new ToolStripSeparator(), saveSupport, new ToolStripSeparator(), clear,
        });

        // Copy is pointless with nothing selected, so it says so rather
        // than sitting there doing nothing when clicked.
        menu.Opening += (_, _) =>
        {
            copySelection.Enabled = console.SelectionLength > 0;
            copyVisible.Enabled = console.TextLength > 0;
            clear.Enabled = _consoleLines.Count > 0;
        };

        AppTheme.StyleContextMenu(menu);

        console.ContextMenuStrip = menu;
    }

    /// <summary>
    /// Puts text on the clipboard without letting a locked clipboard become
    /// an error dialog - another process can hold it, and a failed copy is
    /// not worth interrupting anyone over.
    /// </summary>
    /// <summary>
    /// How many lines a piece of the console's text holds, for "Copied 10
    /// lines". The line break that ends the last line is not another line.
    /// </summary>
    /// <remarks>
    /// WHY (23 Nov 2026, bug hunt 24): the count was the text split on line
    /// breaks. The console always ends in one, so the empty piece after it
    /// was counted and ten lines read "Copied 11 lines".
    /// </remarks>
    internal static int ConsoleLineCount(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var breaks = text.Count(c => c == '\n');

        return text.EndsWith('\n') ? breaks : breaks + 1;
    }

    private void CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
            OnClientRawMessage($"{AppTime.LogStampNow()}  [console] Copied {Plural.Of(ConsoleLineCount(text), "line")}.");
        }
        catch
        {
            // ignored - see the summary.
        }
    }

    /// <summary>Redraws the console from the full buffer, applying the filter.</summary>
    private void RenderConsole()
    {
        var visible = _consoleLines.Where(ConsoleLineVisible).ToList();

        // Three separate changes to the box below - the text, the trailing
        // newline, and the scroll - each of which would repaint on its own.
        // Wrapped so the control paints once, at the end, already correct.
        NoRedraw.Begin(txtConsole);

        try
        {
            txtConsole.Text = string.Join(Environment.NewLine, visible);

            if (visible.Count > 0)
            {
                txtConsole.AppendText(Environment.NewLine);
            }

            if (chkAutoScroll.Checked)
            {
                txtConsole.SelectionStart = txtConsole.TextLength;
                txtConsole.ScrollToCaret();
            }
        }
        finally
        {
            // In a finally: a control left with its painting switched off
            // is a control that never draws again, which looks like the app
            // has frozen.
            NoRedraw.End(txtConsole);
        }
    }
}
