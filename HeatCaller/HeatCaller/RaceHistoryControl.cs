using System.Globalization;

namespace HeatCaller;

/// <summary>Lists all saved sessions and shows full results for the selected one.</summary>
public sealed class RaceHistoryControl : UserControl
{
    /// <summary>The bottom action row, in the order it is laid out. See LayoutActionRow.</summary>
    private Button[] _actions = Array.Empty<Button>();

    private readonly SessionHistoryStore _history;
    private readonly UndoService _undo;
    private readonly AppSettings _settings;
    private readonly RoundDefinitionStore? _rounds;
    private readonly RaceClassStore? _classes;
    private readonly ProfileStore? _profiles;
    private readonly TrackLayoutStore? _trackLayouts;
    private readonly TechInspectionStore? _tech;
    private readonly RentalTransponderStore? _rentals;
    private readonly HandicapStore? _handicaps;
    private readonly RaceEventStore? _events;
    private readonly ListView _sessionList = new();
    private readonly DataGridView _detailGrid = new();
    private readonly Label _lblDetailTitle = new();
    private readonly Label _lblNotes = new();
    private readonly TextBox _txtNotes = new();
    private string? _currentSessionId;

    public RaceHistoryControl(SessionHistoryStore history, UndoService undo, AppSettings settings,
        RoundDefinitionStore? rounds = null, RaceClassStore? classes = null, ProfileStore? profiles = null,
        TrackLayoutStore? trackLayouts = null, TechInspectionStore? tech = null, RentalTransponderStore? rentals = null,
        HandicapStore? handicaps = null, RaceEventStore? events = null)
    {
        _history = history;
        _undo = undo;
        _settings = settings;
        _rounds = rounds;
        _classes = classes;
        _profiles = profiles;
        _trackLayouts = trackLayouts;
        _tech = tech;
        _rentals = rentals;
        _handicaps = handicaps;
        _events = events;

        Dock = DockStyle.Fill;
        MinimumSize = new Size(820, 520);
        Font = AppTheme.BodyFont;

        _sessionList.View = View.Details;
        _sessionList.FullRowSelect = true;
        // Ctrl-click for individual rows, Shift-click for a range.
        _sessionList.MultiSelect = true;
        _sessionList.GridLines = true;
        _sessionList.Location = new Point(12, 12);
        _sessionList.Size = new Size(380, 440);
        _sessionList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
        _sessionList.Columns.Add("Date", 110);
        _sessionList.Columns.Add("Type", 60);
        _sessionList.Columns.Add("Track", 100);
        _sessionList.Columns.Add("Label", 100);
        _sessionList.SelectedIndexChanged += (_, _) => ShowDetail();

        _lblDetailTitle.Location = new Point(404, 12);
        _lblDetailTitle.AutoSize = true;
        _lblDetailTitle.MaximumSize = new Size(404, 0);
        _lblDetailTitle.Font = new Font(Font, FontStyle.Bold);
        _lblDetailTitle.Text = "Select a session to see full results.";

        _detailGrid.Location = new Point(404, 44);
        _detailGrid.Size = new Size(404, 268);
        _detailGrid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _detailGrid.ReadOnly = true;
        _detailGrid.AllowUserToAddRows = false;
        _detailGrid.AllowUserToDeleteRows = false;
        _detailGrid.RowHeadersVisible = false;
        _detailGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _detailGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _detailGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "pos", HeaderText = "Pos", FillWeight = 30 },
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 100 },
            new DataGridViewTextBoxColumn { Name = "car", HeaderText = "Car", FillWeight = 90 },
            new DataGridViewTextBoxColumn { Name = "laps", HeaderText = "Laps", FillWeight = 40 },
            new DataGridViewTextBoxColumn { Name = "fast", HeaderText = "Fastest Lap", FillWeight = 70 },
            new DataGridViewTextBoxColumn { Name = "adj", HeaderText = "Handicap Pos", FillWeight = 70 },
            new DataGridViewTextBoxColumn { Name = "warn", HeaderText = "Warnings", FillWeight = 55 }
        );

        _lblNotes.Text = "Notes (weather, track surface, incidents, whatever's worth remembering):";
        _lblNotes.Location = new Point(404, 320);
        _lblNotes.AutoSize = true;
        _lblNotes.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _lblNotes.Font = AppTheme.SmallFont;

        _txtNotes.Multiline = true;
        _txtNotes.AcceptsReturn = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        _txtNotes.Location = new Point(404, 340);
        _txtNotes.Size = new Size(404, 68);
        _txtNotes.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _txtNotes.Enabled = false;
        _txtNotes.Leave += (_, _) => SaveNotes();

        // --- The action row ---
        //
        // Eight buttons, laid out in ONE measured pass - see
        // LayoutActionRow. They used to be three separate authorities:
        // Delete/Replay/Pace re-measured at y=460, Share/Event Report
        // remembered at y=454, and Export Session/Export Results
        // remembered at y=416. Six pixels between the first two rows is
        // near enough to look like a mistake rather than a decision, and
        // at a larger text size the 38px step between 416 and 454 was
        // smaller than the buttons themselves, so they overlapped and the
        // repair pass shoved them sideways.
        var btnDelete = new Button { Text = "Delete Session" };
        btnDelete.Click += (_, _) => DeleteSelected();

        var btnReplay = new Button { Text = "Replay..." };
        btnReplay.Click += (_, _) => ShowReplay();

        var btnPace = new Button { Text = "Pace..." };
        btnPace.Click += (_, _) => ShowPace();

        var btnShareCard = new Button { Text = "Share Results Card..." };
        btnShareCard.Click += (_, _) => ShareResultsCard();

        var btnEventReport = new Button { Text = "Event Report..." };
        btnEventReport.Click += (_, _) => ShowEventReport();

        var btnExportSession = new Button { Text = "Export Session..." };
        btnExportSession.Click += (_, _) => ExportSelectedSession();

        var btnExportAll = new Button { Text = "Export Results..." };
        btnExportAll.Click += (_, _) => ExportResults();

        var btnEditResult = new Button { Text = "Edit Result..." };
        btnEditResult.Click += (_, _) => EditSelectedResult();

        // Ordered by how often a race director reaches for them, not by
        // the order they were written. Delete goes last because it is the
        // one that cannot be taken back from here - Edit Result sits just
        // ahead of it instead, since a correction is undoable.
        _actions = new[]
        {
            btnReplay, btnPace, btnShareCard, btnEventReport,
            btnExportSession, btnExportAll, btnEditResult, btnDelete,
        };

        Controls.Add(_sessionList);
        Controls.Add(_lblDetailTitle);
        Controls.Add(_detailGrid);
        Controls.Add(_lblNotes);
        Controls.Add(_txtNotes);

        foreach (var button in _actions)
        {
            Controls.Add(button);
        }

        LayoutActionRow();

        // This page places every one of its children on every resize, so
        // the repair pass is told to keep out - two things moving the same
        // control is worse than neither doing it. See
        // LayoutDoctor.LeaveAlone.
        LayoutDoctor.LeaveAlone(this);

        RefreshList();
    }

    /// <summary>
    /// Flows the action row, wrapping when it runs out of width.
    ///
    /// Measured on every resize rather than placed once, because the
    /// captions are drawn in a font that follows the text-size setting and
    /// the page is as wide as the window. A row of seven that fits at the
    /// normal size stops fitting a couple of notches up, and the only
    /// honest answer to that is to wrap rather than to run off the edge.
    /// </summary>
    private void LayoutActionRow()
    {
        if (_actions.Length == 0)
        {
            return;
        }

        // Below the notes box, measured from it rather than from a number
        // that happened to be under it when the font was smaller.
        var top = UiLayout.Below(_txtNotes, UiLayout.RowGap);

        // And to the RIGHT of the session list, which is the bug this
        // replaces. The list runs the full height of the left column and
        // was added to Controls FIRST - which in WinForms puts it at the
        // front of the z-order - so a row starting at the page margin was
        // not merely beside the list, it was behind it, and the first
        // button in the row was sheared off mid-caption.
        var left = _detailGrid.Left;

        var right = Math.Max(left + 200, Width - UiLayout.PageRight);
        var x = left;
        var y = top;

        foreach (var button in _actions)
        {
            var text = TextRenderer.MeasureText(
                button.Text,
                AppTheme.BoldFont,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            button.Size = new Size(text.Width + 26, UiLayout.ButtonHeight);

            if (x > left && x + button.Width > right)
            {
                x = left;
                y += UiLayout.ButtonHeight + UiLayout.RowGap;
            }

            button.Location = new Point(x, y);
            x += button.Width + UiLayout.ButtonGap;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutActionRow();
    }

    /// <summary>Called when this page becomes visible - sessions are saved by Race Control and Practice while it's inactive.</summary>
    public void RefreshAll() => RefreshList();

    /// <summary>
    /// The session's type as shown in the list and the detail title.
    ///
    /// A saved demo race says so in its type as well as in its label: the
    /// label can be anything, and "Race" on its own reads as a result that
    /// counted - which a demo never does.
    /// </summary>
    /// <param name="full">The longer wording, for the detail title - the list's Type column is too narrow for it.</param>
    private static string TypeText(SessionRecord session, bool full = false) =>
        session.IsDemoSession()
            ? (full ? "Demo race (not scored)" : "Demo")
            : session.SessionType.ToString();

    private void RefreshList()
    {
        _sessionList.Items.Clear();
        foreach (var s in _history.All)
        {
            var item = new ListViewItem(AppTime.DateAndTime(s.StartedAtUtc.ToLocalTime())) { Tag = s.Id };
            item.SubItems.Add(TypeText(s));
            item.SubItems.Add(string.IsNullOrWhiteSpace(s.TrackLayoutName) ? "-" : s.TrackLayoutName);
            item.SubItems.Add(string.IsNullOrWhiteSpace(s.Label) ? "-" : s.Label);
            _sessionList.Items.Add(item);
        }

        _detailGrid.Rows.Clear();
        _lblDetailTitle.Text = "Select a session to see full results.";
        _currentSessionId = null;
        _txtNotes.Text = string.Empty;
        _txtNotes.Enabled = false;
    }

    private void ShowDetail()
    {
        _detailGrid.Rows.Clear();
        if (_sessionList.SelectedItems.Count == 0)
        {
            return;
        }

        var id = (string)_sessionList.SelectedItems[0].Tag!;
        var session = _history.All.FirstOrDefault(s => s.Id == id);
        if (session is null)
        {
            return;
        }

        _currentSessionId = session.Id;
        _txtNotes.Text = session.Notes;
        _txtNotes.Enabled = true;

        var duration = session.EndedAtUtc - session.StartedAtUtc;
        var trackPart = string.IsNullOrWhiteSpace(session.TrackLayoutName) ? "" : $" at {session.TrackLayoutName}";
        var labelPart = string.IsNullOrWhiteSpace(session.Label) ? "" : $" - {session.Label}";
        _lblDetailTitle.Text = $"{TypeText(session, full: true)}{trackPart}{labelPart}\n{AppTime.DateAndTime(session.StartedAtUtc.ToLocalTime())} ({duration:hh\\:mm\\:ss})";

        // Handicaps apply at presentation, never to the recorded result - so
        // the raw position stays in its own column and the adjusted one sits
        // beside it. Both visible, or the result can't be checked.
        var mode = HandicapModeFor(session);
        var adjusted = mode == HandicapMode.None || _handicaps is null
            ? null
            : HandicapCalculator.Apply(session.Results, _handicaps, mode)
                .ToDictionary(a => a.Result.Transponder, a => a, StringComparer.OrdinalIgnoreCase);

        _detailGrid.Columns["adj"]!.Visible = adjusted is not null;

        foreach (var r in session.Results.OrderBy(r => r.Position))
        {
            var adj = adjusted is not null && adjusted.TryGetValue(r.Transponder, out var a) ? a : null;

            var adjText = adj is null
                ? string.Empty
                : adj.CreditLaps != 0
                    ? $"P{adj.AdjustedPosition}  (+{adj.CreditLaps}L)"
                    : adj.CreditSeconds != 0
                        ? $"P{adj.AdjustedPosition}  (-{adj.CreditSeconds:0.0}s)"
                        : $"P{adj.AdjustedPosition}";

            var rowIndex = _detailGrid.Rows.Add(
                r.Position,
                r.DriverName,
                string.IsNullOrWhiteSpace(r.CarModel) ? "-" : r.CarModel,
                r.Laps,
                r.FastLapMs > 0 ? $"{r.FastLapMs / 1000.0:0.000}s" : "-",
                adjText,
                WarningsText(r));

            // Hover for what each warning was for.
            if (r.Warnings.Count > 0)
            {
                var warnCell = _detailGrid.Rows[rowIndex].Cells["warn"];
                warnCell.ToolTipText = string.Join(Environment.NewLine, r.Warnings.Select(w => w.Summary()));
                warnCell.Style.ForeColor = AppTheme.Danger;
                warnCell.Style.SelectionForeColor = AppTheme.Danger;
            }

            if (adj is { Moved: true })
            {
                _detailGrid.Rows[rowIndex].Cells[5].Style.ForeColor = AppTheme.Accent;
                _detailGrid.Rows[rowIndex].Cells[5].Style.Font = AppTheme.BoldFont;
            }
        }
    }

    /// <summary>"-", "\u26A0 2", or "\u2691 Black flag" for the history grid.</summary>
    private static string WarningsText(SessionResultEntry r) =>
        r.Warnings.Count == 0
            ? "-"
            : r.Warnings.Any(w => w.Action == WarningAction.BlackFlag)
                ? $"\u2691 Black flag ({r.Warnings.Count})"
                : $"\u26A0 {r.Warnings.Count}";

    /// <summary>The handicap mode for whichever class this session's round belongs to.</summary>
    private HandicapMode HandicapModeFor(SessionRecord session)
    {
        if (_handicaps is null || _rounds is null || session.RoundId is null)
        {
            return HandicapMode.None;
        }

        var round = _rounds.Get(session.RoundId);
        return round is null ? HandicapMode.None : _handicaps.ModeFor(round.ClassName);
    }

    private void SaveNotes()
    {
        if (_currentSessionId is null)
        {
            return;
        }

        var session = _history.Get(_currentSessionId);
        if (session is null || session.Notes == _txtNotes.Text)
        {
            return;
        }

        session.Notes = _txtNotes.Text;
        _history.SaveChanges();
    }

    private void ShareResultsCard()
    {
        if (_sessionList.SelectedItems.Count == 0)
        {
            AppDialog.Show(this, "Select a session first.", "No session selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var id = (string)_sessionList.SelectedItems[0].Tag!;
        var session = _history.All.FirstOrDefault(s => s.Id == id);
        if (session is null)
        {
            return;
        }

        if (session.Results.Count == 0)
        {
            AppDialog.Show(this, "This session has no results to share.", "Nothing to share", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = $"{session.StartedAtUtc.ToLocalTime():yyyy-MM-dd_HH-mm}_{SanitizeFileNamePart(session.Label)}_results.png",
        };

        // Ask how it should look first. Reuses the chart export dialog, so
        // the same print-friendly and monochrome styles are available here.
        using var optionsForm = new ChartExportForm(_settings, (g, bounds, palette) =>
        {
            using var preview = ResultsCardRenderer.Render(session, palette);
            g.DrawImage(preview, FitInto(preview.Size, bounds));
        });

        if (optionsForm.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            using var bitmap = ResultsCardRenderer.Render(session, ChartPalette.For(optionsForm.Options.StyleKey));
            bitmap.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);
            AppDialog.Show(this, $"Saved a shareable results image to:\n{dialog.FileName}\n\nReady to paste into a group chat or post.",
                "Results card saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't create the results card: {ex.Message}", "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }


    /// <summary>Centres an image inside a box without distorting it - the card is a fixed shape, the preview panel isn't.</summary>
    private static Rectangle FitInto(Size image, Rectangle bounds)
    {
        if (image.Width <= 0 || image.Height <= 0)
        {
            return bounds;
        }

        var scale = Math.Min(bounds.Width / (float)image.Width, bounds.Height / (float)image.Height);
        var w = Math.Max(1, (int)(image.Width * scale));
        var h = Math.Max(1, (int)(image.Height * scale));

        return new Rectangle(bounds.X + (bounds.Width - w) / 2, bounds.Y + (bounds.Height - h) / 2, w, h);
    }


    /// <summary>
    /// Opens the whole-day event report. Needs several stores beyond this
    /// page's own, so it degrades with a clear message rather than a crash
    /// if the host didn't supply them.
    /// </summary>

    /// <summary>
    /// Exports the selected sessions - or all of them - in a machine-readable
    /// format. Selection-aware because exporting a whole season when you
    /// wanted one heat is a quiet annoyance.
    /// </summary>
    private void ExportResults()
    {
        var selected = _sessionList.SelectedItems
            .Cast<ListViewItem>()
            .Select(i => _history.Get((string)i.Tag!))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();

        var all = _history.All.ToList();
        if (all.Count == 0)
        {
            AppDialog.Show(this, "There are no recorded sessions to export.", "Nothing to export",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new ResultsExportForm(selected.Count, all.Count);
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var sessions = form.SelectedOnly && selected.Count > 0 ? selected : all;

        using var dialog = new SaveFileDialog
        {
            Title = "Export results",
            Filter = form.Format == ResultsExporter.Format.Json
                ? "JSON (*.json)|*.json"
                : "CSV (*.csv)|*.csv",
            FileName = $"heatcaller-results_{DateTime.Now:yyyy-MM-dd}.{ResultsExporter.Extension(form.Format)}",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            // Loading a second ProfileStore here would read the same file into
            // a separate instance that then drifts from the shared one. The
            // host always supplies it; if it somehow didn't, say so.
            if (_profiles is null)
            {
                AppDialog.Show(this, "Export isn't available in this window.", "Not available",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var text = ResultsExporter.Build(form.Format, sessions, _rounds, _profiles);
            File.WriteAllText(dialog.FileName, text);

            AppDialog.Show(this,
                $"Exported {Plural.Of(sessions.Count, "session")}.\r\n\n{dialog.FileName}",
                "Export complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't write the file.\r\n\r\n{ex.Message}", "Export failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowEventReport()
    {
        if (_rounds is null || _classes is null || _profiles is null
            || _trackLayouts is null || _tech is null || _rentals is null)
        {
            AppDialog.Show(this, "The event report isn't available in this window.", "Not available",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new EventReportForm(_history, _rounds, _classes, _profiles, _trackLayouts, _tech, _rentals, _handicaps, _events);
        form.ShowDialog(this);
    }

    private void DeleteSelected()
    {
        var removed = _sessionList.SelectedItems
            .Cast<ListViewItem>()
            .Select(i => _history.Get((string)i.Tag!))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();

        if (removed.Count == 0)
        {
            return;
        }

        var what = removed.Count == 1
            ? "Delete this session from history?"
            : $"Delete these {removed.Count} sessions from history?";

        if (AppDialog.Show(this, what, "Delete session", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        // One undo for the batch rather than one per session.
        var history = _history;
        var rounds = _rounds;

        // Which round each of these sessions recorded, read BEFORE anything
        // is deleted: once the links are cleared there is nothing left to
        // work it out from, and the undo has to put them back or the rounds
        // come back unrun.
        var relink = rounds is null
            ? new List<(string RoundId, string SessionId)>()
            : removed
                .SelectMany(session => rounds.All
                    .Where(r => string.Equals(r.LinkedSessionId, session.Id, StringComparison.Ordinal))
                    .Select(r => (RoundId: r.Id, SessionId: session.Id)))
                .ToList();

        _undo.Capture(removed.Count == 1 ? $"Delete session '{removed[0].Label}'" : $"Delete {removed.Count} sessions", () =>
        {
            // By id, so a session already back (restored from a backup or
            // an export since) is not put in twice.
            foreach (var session in removed)
            {
                if (history.Get(session.Id) is null)
                {
                    history.Add(session);
                }
            }

            // Only for a session that really is back - re-pointing a round
            // at one that is not would put back the exact fault the delete
            // path now avoids.
            foreach (var (roundId, sessionId) in relink)
            {
                if (history.Get(sessionId) is not null)
                {
                    rounds?.MarkRecorded(roundId, sessionId);
                }
            }

            if (!IsDisposed)
            {
                RefreshList();
            }
        });

        foreach (var session in removed)
        {
            _history.Delete(session.Id);

            // The round it recorded is unrun again. Left linked, it points
            // at a session nobody can fetch: the heat cannot be re-run
            // because it is not offered anywhere, the programme shows it
            // done with no result behind it, and qualifying counts the
            // round while scoring nothing for it.
            _rounds?.ClearRecorded(session.Id);
        }

        RefreshList();
    }

    /// <summary>The session highlighted in the list, or null with a nudge shown.</summary>
    private SessionRecord? SelectedSession()
    {
        if (_sessionList.SelectedItems.Count == 0)
        {
            AppDialog.Show(this, "Select a session first.", "No session selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);

            return null;
        }

        var id = (string)_sessionList.SelectedItems[0].Tag!;

        return _history.All.FirstOrDefault(s => s.Id == id);
    }

    private void ShowReplay()
    {
        if (SelectedSession() is not { } session)
        {
            return;
        }

        using var form = new RaceReplayForm(session);
        form.ShowDialog(this);
    }

    private void ShowPace()
    {
        if (SelectedSession() is not { } session)
        {
            return;
        }

        using var form = new PaceAnalysisForm(session);
        form.ShowDialog(this);
    }

    /// <summary>
    /// Opens the Edit Result dialog and, on Save, applies the correction -
    /// the same snapshot/undo/apply/save sequence RaceDayOpsResults.ResultAdjust
    /// uses for the phone, so a laptop correction and a phone correction can
    /// never disagree about what a five-second penalty does to the order.
    /// </summary>
    private void EditSelectedResult()
    {
        if (SelectedSession() is not { } session)
        {
            return;
        }

        if (CanCorrectResult(session) is { } blocked)
        {
            AppDialog.Show(this, blocked, "Can't correct this result", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new ResultEditForm(session);
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // Snapshot and capture the undo BEFORE applying - see ResultEditor.Apply.
        var snapshot = ResultEditor.Snapshot(session);
        var sessionId = session.Id;

        var history = _history;

        _undo.Capture($"Correct the result of '{session.Label}'", () =>
        {
            // SaveEdited quietly ignores a session no longer in the store,
            // so the undo would have looked done while nothing was saved.
            if (!ReferenceEquals(history.Get(sessionId), session))
            {
                throw new InvalidOperationException($"'{session.Label}' has been deleted since. Nothing was changed.");
            }

            ResultEditor.Restore(session, snapshot);
            history.SaveEdited(session);

            if (!IsDisposed)
            {
                RefreshListAndSelect(sessionId);
            }
        });

        ResultEditor.Apply(session, form.Adjustments, form.Reason, "Laptop");

        // Put back rather than left half-applied: SaveEdited republishes the
        // corrected result to the results page, the archive index and the
        // championship standings, and it does none of that when the write
        // fails. In memory the correction would still look applied until the
        // next launch reloaded the old one.
        if (!_history.SaveEdited(session))
        {
            ResultEditor.Restore(session, snapshot);

            AppDialog.Show(this,
                $"The correction to \"{session.Label}\" could not be written to the race history - something else on "
                + "this PC has the file open, or the disk is full.\r\n\r\nNothing has been changed. Close anything "
                + "that might be reading the HeatCaller folder and try again.",
                "Correction not saved", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        RefreshListAndSelect(sessionId);
    }

    /// <summary>
    /// Why this session's result cannot be corrected from here, or null when
    /// it can. Kept in step with RaceDayOpsResults.CanCorrect, by hand - the
    /// laptop and the phone have to refuse on exactly the same grounds.
    /// </summary>
    private string? CanCorrectResult(SessionRecord session)
    {
        if (!_settings.AllowResultEditing)
        {
            return "Correcting saved results is switched off on the laptop. Turn on \"Allow corrections to saved results\" on the Local Screens page to use it.";
        }

        if (session.SessionType != RaceSessionType.Race || session.IsDemoSession())
        {
            return "Only a real race's result can be corrected - not practice, and not a demo race.";
        }

        return null;
    }

    /// <summary>Rebuilds the list and re-selects one session by id - so correcting a result doesn't leave the page looking blank.</summary>
    private void RefreshListAndSelect(string sessionId)
    {
        RefreshList();

        foreach (ListViewItem item in _sessionList.Items)
        {
            if (Equals(item.Tag, sessionId))
            {
                item.Selected = true;
                item.EnsureVisible();
                break;
            }
        }
    }

    private void ExportSelectedSession()
    {
        if (_sessionList.SelectedItems.Count == 0)
        {
            AppDialog.Show(this, "Select a session first.", "No session selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var id = (string)_sessionList.SelectedItems[0].Tag!;
        var session = _history.All.FirstOrDefault(s => s.Id == id);
        if (session is null)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"{session.StartedAtUtc.ToLocalTime():yyyy-MM-dd_HH-mm}_{SanitizeFileNamePart(session.Label)}.csv",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var lines = new List<string> { CsvHelper.BuildRow("Position", "Driver", "Car", "Laps", "Fastest Lap (s)", "Elapsed", "Status") };
            foreach (var r in session.Results.OrderBy(r => r.Position))
            {
                lines.Add(CsvHelper.BuildRow(
                    r.Position,
                    r.DriverName,
                    r.CarModel,
                    r.Laps,
                    // InvariantCulture because this is a FILE, not a
                    // label. On a machine set to a European locale the
                    // decimal separator is a comma, so a 12.345s lap was
                    // written into the CSV as "12,345" - an extra column,
                    // or a value nothing downstream parses as a number.
                    r.FastLapMs > 0
                        ? (r.FastLapMs / 1000.0).ToString("0.000", CultureInfo.InvariantCulture)
                        : string.Empty,
                    FormatElapsed(r.ElapsedMs),
                    r.Status));
            }

            File.WriteAllLines(dialog.FileName, lines, CsvHelper.FileEncoding);
            AppDialog.Show(this, $"Exported {Plural.Of(session.Results.Count, "result")} to:\n{dialog.FileName}", "Export complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Export failed: {ex.Message}", "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string FormatElapsed(long elapsedMs)
    {
        if (elapsedMs <= 0)
        {
            return string.Empty;
        }
        var ts = TimeSpan.FromMilliseconds(elapsedMs);
        return ts.ToString(ts.Hours > 0 ? @"h\:mm\:ss\.fff" : @"m\:ss\.fff");
    }

    private static string SanitizeFileNamePart(string value)
    {
        var cleaned = string.IsNullOrWhiteSpace(value) ? "session" : value;
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            cleaned = cleaned.Replace(invalid, '_');
        }
        return cleaned;
    }
}
