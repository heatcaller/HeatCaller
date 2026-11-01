using System.Globalization;

namespace HeatCaller;

/// <summary>
/// Lists all saved sessions and shows full results for the selected one.
///
/// On the layout pieces (LayoutPieces.cs): the sessions down the left, and
/// beside them a card for the one selected - what it was, its results
/// taking the height that is left, its notes, and what can be done with
/// it. It used to be placed by hand over an 820x520 floor, the results
/// grid at a fixed 268px and the eight buttons flowed under the notes on
/// every resize, in faces handed out once.
/// </summary>
public sealed class RaceHistoryControl : UserControl
{
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

    // The notes as the box was filled from the race - so SaveNotes writes
    // back only what was typed, not a stale copy (bug hunt 10).
    private string? _notesAsLoaded;

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

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize. No MinimumSize: the 820x520 floor was
        // honoured over the size the page was given. A short window scrolls
        // the page instead (FillPage).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshList();
    }

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The sessions' card at the normal text size - about a third of the page, between these. The least is the old list's width, which the date and time need.</summary>
    private const int SessionsLeastWidth = 380;
    private const int SessionsMostWidth = 520;

    /// <summary>Lines of the notes box kept in view.</summary>
    private const int NotesLines = 3;

    /// <summary>The page's own face changed - handed the body face at a new text size - so everything else it keeps in step follows. See TextSize.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>A new text size, reaching this page through its parent's font - the only way it does, since the page wears a face of its own. See TextSize.</summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>
    /// The form whose closing flushes the notes box - see
    /// <see cref="HookClosingForm"/>.
    /// </summary>
    private Form? _closingHooked;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        HookClosingForm();
    }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        HookClosingForm();
    }

    /// <summary>
    /// 17 Oct 2026, Bug Hunt 8, A30-4: closing the main window while
    /// typing a note lost it, because notes were saved only when focus left
    /// the box. Saved as the form closes instead - before any control is
    /// destroyed, while the history is still there to write to.
    /// </summary>
    private void HookClosingForm()
    {
        if (FindForm() is not { } form || ReferenceEquals(form, _closingHooked))
        {
            return;
        }

        if (_closingHooked is not null)
        {
            _closingHooked.FormClosing -= FlushNotesOnClosing;
        }

        _closingHooked = form;
        form.FormClosing += FlushNotesOnClosing;
    }

    private void FlushNotesOnClosing(object? sender, FormClosingEventArgs e)
    {
        try
        {
            SaveNotes();
        }
        catch
        {
            // Never the reason the app cannot close. SaveChanges reports
            // its own failures; anything else here is not worth a hang.
        }
    }

    private void BuildLayout()
    {
        // --- The sessions ---
        _sessionList.View = View.Details;
        _sessionList.FullRowSelect = true;
        // Ctrl-click for individual rows, Shift-click for a range.
        _sessionList.MultiSelect = true;
        _sessionList.GridLines = true;
        _sessionList.Columns.Add("Date");
        _sessionList.Columns.Add("Type");
        _sessionList.Columns.Add("Track");
        _sessionList.Columns.Add("Label");
        _sessionList.SelectedIndexChanged += (_, _) => ShowDetail();

        // The label takes what the others leave - see Pieces.FitListColumns -
        // and asks for little of its own: it is often "-", while the date
        // and time are what a session is picked by.
        Pieces.FitListColumns(_sessionList, 3, 130, 70, 100, 60);

        var sessions = new Section("Sessions");

        sessions.AddFill(_sessionList, () => _sessionList.Font.Height * 2 + 4 * (_sessionList.Font.Height + 6), gapBelow: 0);

        // --- The one selected ---
        //
        // What it was and when, over its results, in the bold face.
        _lblDetailTitle.AutoSize = true;
        _lblDetailTitle.Text = "Select a session to see full results.";
        _textSize.Wear(_lblDetailTitle, () => AppTheme.BoldFont);

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
            new DataGridViewTextBoxColumn { Name = "warn", HeaderText = "Warnings", FillWeight = 55 },

            // With a split loop: their best through each sector, and those
            // added up - shown only for a session that has sectors.
            new DataGridViewTextBoxColumn { Name = "sectors", HeaderText = "Best Sectors", FillWeight = 90 },
            new DataGridViewTextBoxColumn { Name = "ideal", HeaderText = "Ideal Lap", FillWeight = 60 }
        );

        // Every heading whole, at the text size in use - see Pieces.KeepHeadingsWhole.
        _textSize.Keep(() => Pieces.KeepHeadingsWhole(_detailGrid));

        _detailGrid.CellFormatting += FormatHandCountedLaps;

        // 22 Oct 2026: a driver's report card for the night of this race -
        // right-click their row. See CoachReport. Only once the host has
        // wired ReportCardFor in, so the item never opens onto nothing.
        var cardMenu = new ContextMenuStrip();
        var cardItem = new ToolStripMenuItem("Report Card...", null, (_, _) => ShowReportCard());
        cardMenu.Items.Add(cardItem);
        cardMenu.Opening += (_, e) =>
        {
            var row = _detailGrid.CurrentRow;
            cardItem.Text = row?.Cells[1].Value is string named && named.Length > 0 ? $"Report Card for {named}..." : "Report Card...";
            e.Cancel = ReportCardFor is null || row?.Tag is not string { Length: > 0 };
        };
        _detailGrid.ContextMenuStrip = cardMenu;
        _detailGrid.CellMouseDown += (_, e) =>
        {
            // A right-click picks the row it was on, so the card is for the
            // driver under the pointer rather than whoever was selected.
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && e.RowIndex < _detailGrid.Rows.Count)
            {
                _detailGrid.CurrentCell = _detailGrid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
            }
        };

        _lblNotes.Text = "Notes (weather, track surface, incidents, whatever's worth remembering):";
        _lblNotes.AutoSize = true;
        _textSize.Wear(_lblNotes, () => AppTheme.SmallFont);

        _txtNotes.Multiline = true;
        _txtNotes.AcceptsReturn = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        _txtNotes.Enabled = false;
        _txtNotes.Leave += (_, _) => SaveNotes();

        // A few lines of its type, at the text size in use.
        _textSize.Keep(() =>
        {
            var height = Pieces.LineHeight(_txtNotes.Font) * NotesLines + UiLayout.HintGap * 2;

            if (_txtNotes.Height != height)
            {
                _txtNotes.Height = height;
            }
        });

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

        var btnPrintResults = new Button { Text = "Print Results..." };
        btnPrintResults.Click += (_, _) => PrintSelectedResults();

        var btnEditResult = new Button { Text = "Edit Result..." };
        btnEditResult.Click += (_, _) => EditSelectedResult();

        // A result copied off another timing program's results page, put
        // in as an imported race that never scores (PasteResultForm).
        var btnPasteResult = new Button { Text = "Paste a Result..." };
        btnPasteResult.Click += (_, _) => PasteResult();

        // Ordered by how often a race director reaches for them, not by
        // the order they were written. Delete goes last because it is the
        // one that cannot be taken back from here - Edit Result sits just
        // ahead of it instead, since a correction is undoable. The bar
        // wraps onto more lines when the card is too narrow for them.
        var actions = new ButtonBar().With(
            btnReplay, btnPace, btnShareCard, btnEventReport,
            btnExportSession, btnExportAll, btnPrintResults, btnPasteResult, btnEditResult, btnDelete);

        var results = new Section("Results");

        results.Add(_lblDetailTitle);
        results.AddFill(_detailGrid, () => _detailGrid.ColumnHeadersHeight + 4 * _detailGrid.RowTemplate.Height);
        results.Add(_lblNotes, gapBelow: UiLayout.HintGap);
        results.Add(_txtNotes);
        results.Add(actions, gapBelow: 0);

        // --- The page ---
        //
        // The sessions down the left, picked from; the one picked beside
        // them, taking the rest of the width.
        Beside? band = null;

        band = new Beside(() => SessionsWidth(band?.Width ?? 0)) { SideOnLeft = true };

        band.AddFill(results, results.LeastHeight);
        band.AddSideFill(sessions, sessions.LeastHeight, gapBelow: 0);

        var page = new FillPage();

        page.AddFill(band, band.LeastHeight, gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>The sessions' card: about a third of the page, between 380px and 520px at the normal text size, at the size in use.</summary>
    private static int SessionsWidth(int page) =>
        Math.Clamp((int)(page * 0.35f), UiLayout.AtTextSize(SessionsLeastWidth), UiLayout.AtTextSize(SessionsMostWidth));

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
        session.IsImported()
            ? (full ? $"Imported from {session.ImportedFrom} (not scored)" : "Imported")
            : session.IsDemoSession()
                ? (full ? "Demo race (not scored)" : "Demo")
                : session.SessionType.ToString();

    private void RefreshList()
    {
        // 17 Oct 2026, Bug Hunt 8, A30-4: what is in the notes box is
        // saved before the list (and the box) is cleared. Notes were saved
        // only on Leave, and the Leave this clearing raises found nothing
        // to save it against. Safe for a just-deleted session: Get is null.
        SaveNotes();

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
        _notesAsLoaded = null;
        _txtNotes.Text = string.Empty;
        _txtNotes.Enabled = false;
    }

    /// <summary>
    /// Shows one race, selected in the list with its results beside it - and
    /// with Edit Result open on it when <paramref name="edit"/> is set. For
    /// Race Control's Results and Edit Result after the flag (25 Oct 2026);
    /// the host has put this page up first. Nothing when the race isn't in
    /// the history (yet).
    /// </summary>
    internal void ShowSession(string id, bool edit)
    {
        ListViewItem? Find() => _sessionList.Items.Cast<ListViewItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, id, StringComparison.Ordinal));

        // Not listed yet when the page has not been opened since it was saved.
        if (Find() is null)
        {
            RefreshList();
        }

        if (Find() is not { } found)
        {
            return;
        }

        _sessionList.SelectedItems.Clear();
        found.Selected = true;
        found.Focused = true;
        found.EnsureVisible();

        if (edit)
        {
            EditSelectedResult();
        }
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
        _notesAsLoaded = session.Notes;
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

        var hasSectors = session.Results.Any(r => Sectors.Bests(r) is not null);
        _detailGrid.Columns["sectors"]!.Visible = hasSectors;
        _detailGrid.Columns["ideal"]!.Visible = hasSectors;

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
                WarningsText(r),
                Sectors.Bests(r) is { } bests ? Sectors.Line(bests) : "-",
                Sectors.Bests(r) is { } forIdeal ? $"{Sectors.Format(forIdeal.Sum())}s" : "-");

            // The driver, for the Report Card on the right-click menu.
            _detailGrid.Rows[rowIndex].Tag = r.Transponder;

            // 22 Oct 2026: a count with laps counted by hand says so on
            // hover - see HandCountedLaps.
            // The number stays in the cell, so the column sorts as numbers;
            // the mark is added as it is drawn (FormatHandCountedLaps).
            if (HandCountedLaps.In(r) is > 0 and var byHand)
            {
                var lapsCell = _detailGrid.Rows[rowIndex].Cells["laps"];
                lapsCell.Tag = byHand;
                lapsCell.ToolTipText = HandCountedLaps.Note(byHand);
            }

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

    /// <summary>
    /// Prints the selected races' results for the pit board - one after
    /// another, oldest first, with a preview first (22 Oct 2026). Select
    /// several with Ctrl or Shift to print a whole evening on one run of
    /// paper. See ResultsSheetRenderer and PagePrinting.
    /// </summary>
    private void PrintSelectedResults()
    {
        var sessions = _sessionList.SelectedItems
            .Cast<ListViewItem>()
            .Select(i => _history.Get((string)i.Tag!))
            .Where(s => s is not null)
            .Select(s => s!)
            .OrderBy(s => s.StartedAtUtc)
            .ToList();

        if (sessions.Count == 0)
        {
            AppDialog.Show(this, "Select a race first - or several, with Ctrl or Shift, to print them together.",
                "Nothing to print", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var sections = sessions.Select(PrintSectionFor).ToList();
        var title = sessions.Count == 1
            ? sections[0].Title
            : $"Results - {AppTime.DateAndTime(sessions[0].StartedAtUtc.ToLocalTime())}";

        IReadOnlyList<Bitmap> pages;

        try
        {
            pages = ResultsSheetRenderer.RenderPages(title, sections);
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't create the results sheet: {ex.Message}", "Print",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            PagePrinting.PrintWithPreview(this, title, pages);
        }
        finally
        {
            foreach (var page in pages)
            {
                page.Dispose();
            }
        }
    }

    /// <summary>
    /// "12*" in the Laps column for a count with laps counted by hand - the
    /// cell holds the number and its Tag the hand count, so sorting still
    /// goes by the number (22 Oct 2026). See HandCountedLaps.
    /// </summary>
    private void FormatHandCountedLaps(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _detailGrid.Columns[e.ColumnIndex].Name != "laps")
        {
            return;
        }

        if (_detailGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Tag is int byHand && byHand > 0 && e.Value is int laps)
        {
            e.Value = HandCountedLaps.LapsText(laps, byHand);
            e.FormattingApplied = true;
        }
    }

    /// <summary>One race's block on the printed sheet: what it was, where and when, and its results in finishing order.</summary>
    private static ResultsSheetSection PrintSectionFor(SessionRecord session)
    {
        var label = string.IsNullOrWhiteSpace(session.Label) ? "" : $" - {session.Label}";
        var track = string.IsNullOrWhiteSpace(session.TrackLayoutName) ? "" : $"{session.TrackLayoutName}  -  ";

        return new ResultsSheetSection(
            $"{TypeText(session, full: true)}{label}",
            $"{track}{AppTime.DateAndTime(session.StartedAtUtc.ToLocalTime())}",
            session.Results.OrderBy(r => r.Position).ToList());
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
        // WHY (23 Oct 2026, bug hunt 10): a query noted into this race's
        // notes from elsewhere (DriverRequestDesk.Note) was wiped by this
        // box's stale copy on the next page visit or at close. Unedited, the
        // box is not written back.
        if (session is null || _txtNotes.Text == (_notesAsLoaded ?? string.Empty) || session.Notes == _txtNotes.Text)
        {
            return;
        }

        session.Notes = _txtNotes.Text;
        if (_history.SaveChanges())
        {
            _notesAsLoaded = _txtNotes.Text;
        }
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
            var unsaved = new List<SessionRecord>();

            foreach (var session in removed)
            {
                if (history.Get(session.Id) is not null)
                {
                    continue;
                }

                // A race deleted before it was ever filed goes back to
                // waiting for Race Control to file it - linking its round and
                // letting go of its crash journal - rather than being written
                // here without either, to be offered back at the next launch
                // and filed twice (see SessionHistoryStore._deletedUnfiled).
                if (history.RestoreUnfiled(session))
                {
                    continue;
                }

                if (!history.Add(session))
                {
                    unsaved.Add(session);
                }
            }

            // Only for a session that really is back - re-pointing a round
            // at one that is not would put back the exact fault the delete
            // path now avoids. "Back" means saved, not just listed: a
            // session whose Add failed is still in the store's memory (Get
            // finds it) but is held out of race-history.json, while
            // MarkRecorded writes the rounds file at once. Linking to it
            // would leave the round, after a restart, claiming a result
            // that is not there - shown as run, never offered again, and
            // counted by qualifying with nothing to score. Those rounds
            // stay unrun for now, which is the state the delete left them
            // in and one a heat can recover from by being run again.
            //
            // And only for a round that is still unlinked. The delete made
            // the heat unrun precisely so it could be run again; if it has
            // been since, the round now points at that newer race, and
            // filing a race takes no undo step of its own, so this undo
            // can easily be the next one pressed. Writing the old link
            // over the new one would quietly make qualifying score the
            // result that was thrown away and orphan the one that
            // replaced it. Bringing the old session back is all this undo
            // promised; the newer link is left alone and the director is
            // told.
            var rerun = new List<string>();

            // The same goes for a heat being run again right now: it has no
            // link yet, but filing it will link the new result, so linking
            // the old one here would only show the heat as run while it is
            // on track and then be written over without a word.
            var onTrack = RaceDayDeleter.RoundOnTrackId?.Invoke();

            foreach (var (roundId, sessionId) in relink)
            {
                if (history.Get(sessionId) is not { } back
                    || history.IsUnsaved(back)
                    || unsaved.Any(s => string.Equals(s.Id, sessionId, StringComparison.Ordinal)))
                {
                    continue;
                }

                if (rounds?.Get(roundId) is not { } round)
                {
                    continue;
                }

                if (string.Equals(roundId, onTrack, StringComparison.Ordinal)
                    || (!string.IsNullOrEmpty(round.LinkedSessionId)
                        && !string.Equals(round.LinkedSessionId, sessionId, StringComparison.Ordinal)))
                {
                    rerun.Add($"{round.ClassName} {round.RoundLabel}".Trim());
                    continue;
                }

                rounds.MarkRecorded(roundId, sessionId);
            }

            if (!IsDisposed)
            {
                RefreshList();
            }

            if (rerun.Count > 0)
            {
                // Before KeepRetryForUnsaved, which throws to report a failed
                // save and would otherwise stop this from being said.
                var which = rerun.Count == 1
                    ? $"'{rerun[0]}' has"
                    : $"{string.Join(", ", rerun.Select(n => $"'{n}'"))} have";

                AppDialog.Show(IsDisposed ? null : this,
                    $"The deleted session is back in Race History, but {which} been run again since it was deleted (or is on track now), "
                    + "so the newer result stays the one that counts. The restored session is kept in Race History without being linked to a round.",
                    "Session restored", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            // Last, so everything above is done either way. The rounds held
            // back above for a session that did not save are linked when a
            // retry does save it - otherwise the session would be back in
            // Race History while its heat still read as unrun. Again only
            // while the round is unlinked: a heat run again in the meantime
            // keeps its newer result, for the reason given above.
            history.KeepRetryForUnsaved(_undo, unsaved, saved =>
            {
                foreach (var (roundId, sessionId) in relink)
                {
                    if (!string.Equals(sessionId, saved.Id, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (rounds?.Get(roundId) is not { } round
                        || !string.IsNullOrEmpty(round.LinkedSessionId)
                        || string.Equals(roundId, RaceDayDeleter.RoundOnTrackId?.Invoke(), StringComparison.Ordinal))
                    {
                        continue;
                    }

                    rounds.MarkRecorded(roundId, sessionId);
                }

                if (!IsDisposed)
                {
                    RefreshList();
                }
            });
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

    /// <summary>
    /// A driver's report card for a night, a line at a time - set by the
    /// host (MainForm.CoachReportFor). Null hides the right-click item.
    /// </summary>
    public Func<string, DateTime, IReadOnlyList<string>>? ReportCardFor { get; set; }

    /// <summary>
    /// The report card for the driver on the current row, for the night the
    /// race shown was run on (22 Oct 2026). See CoachReport.
    /// </summary>
    private void ShowReportCard()
    {
        if (ReportCardFor is not { } cardFor
            || _detailGrid.CurrentRow?.Tag is not string { Length: > 0 } transponder
            || _currentSessionId is null
            || _history.All.FirstOrDefault(s => s.Id == _currentSessionId) is not { } session)
        {
            return;
        }

        var night = TonightAtTheTrack.NightOf(session.StartedAtUtc.ToLocalTime());
        var named = _detailGrid.CurrentRow.Cells[1].Value as string;
        var who = string.IsNullOrWhiteSpace(named) ? transponder : named;
        var when = night.ToString("ddd d MMM", System.Globalization.CultureInfo.CurrentCulture);

        IReadOnlyList<string> lines;

        try
        {
            lines = cardFor(transponder, night);
        }
        catch
        {
            lines = Array.Empty<string>();
        }

        // A window of its own (22 Oct 2026), with Print and Save Image - see ReportCardForm.
        // The race's own night goes in (22 Oct 2026) so a saved card is named for that date, not today's.
        using var form = new ReportCardForm(who, night, when, lines);
        form.ShowDialog(this);
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

        // Snapshot BEFORE applying - see ResultEditor.Apply. The undo that
        // puts it back is captured only once the correction has been saved,
        // below: captured first, a failed save left an undo step for a
        // correction that never happened, and the next Undo press was spent
        // on that instead of on whatever the director actually meant to undo.
        var snapshot = ResultEditor.Snapshot(session);
        var sessionId = session.Id;

        var history = _history;

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

            RefreshListAndSelect(sessionId);
            return;
        }

        _undo.Capture($"Correct the result of '{session.Label}'", () =>
        {
            // SaveEdited quietly ignores a session no longer in the store,
            // so the undo would have looked done while nothing was saved.
            if (!ReferenceEquals(history.Get(sessionId), session))
            {
                throw new InvalidOperationException($"'{session.Label}' has been deleted since. Nothing was changed.");
            }

            // The same rule the correction itself follows above: a write that
            // fails puts things back rather than leaving memory and disk
            // disagreeing. Restored but unsaved, qualifying, the mains builder
            // and the live standings would all read the uncorrected order
            // while the file - and the results page, archive index and
            // standings, which only hear about a SAVED edit - still held the
            // correction, and the next launch quietly brought it back. So
            // keep the corrected state as it is now, and if the undo cannot
            // be written, return to it and throw so UndoService reports the
            // failure instead of the undo looking done. (UndoService has
            // already taken the step off the stack by then, which is why the
            // message says to put the result back by hand.)
            var corrected = ResultEditor.Snapshot(session);
            ResultEditor.Restore(session, snapshot);

            if (!history.SaveEdited(session))
            {
                ResultEditor.Restore(session, corrected);

                if (!IsDisposed)
                {
                    RefreshListAndSelect(sessionId);
                }

                throw new InvalidOperationException(
                    $"Undoing the correction to \"{session.Label}\" could not be written to the race history - something "
                    + "else on this PC has the file open, or the disk is full. The corrected result is still in place; "
                    + "close anything that might be reading the HeatCaller folder and edit the result back by hand.");
            }

            if (!IsDisposed)
            {
                RefreshListAndSelect(sessionId);
            }
        });

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

        if (session.IsImported())
        {
            return ResultEditor.ImportedProblem;
        }

        if (session.SessionType != RaceSessionType.Race || session.IsDemoSession())
        {
            return "Only a real race's result can be corrected - not practice, and not a demo race.";
        }

        return null;
    }

    /// <summary>
    /// Paste a Result: the race result on the clipboard - copied off
    /// LiveRC's results page, or another program's - previewed in
    /// PasteResultForm and put into the history as an imported race. Only
    /// the clipboard is read; nothing is fetched. Undo takes it out again.
    /// </summary>
    private void PasteResult()
    {
        string? html = null;
        string? text = null;

        try
        {
            if (Clipboard.ContainsText(TextDataFormat.Html))
            {
                html = Clipboard.GetText(TextDataFormat.Html);
            }

            if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
            {
                text = Clipboard.GetText(TextDataFormat.UnicodeText);
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException)
        {
            // Another program holding the clipboard open as it is read.
            AppDialog.Show(this, $"Couldn't read the clipboard just now ({ex.Message}). Try Paste a Result again.",
                "Paste a result", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (PastedTable.Read(html, text) is not { } table || PastedResult.Read(table) is not { } race)
        {
            AppDialog.Show(this,
                "There's no race result on the clipboard.\n\n"
                + "On the results page, select the whole result - its headings (Pos, Driver, Laps) and every driver's row - "
                + "and copy it (Ctrl+C). Then press Paste a Result here.",
                "Nothing to paste", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var profiles = _profiles?.All ?? Array.Empty<DriverProfile>();
        var classNames = _classes?.All.Select(c => c.Name).ToList() ?? new List<string>();

        using var form = new PasteResultForm(
            race,
            PastedResult.SourceName(html),
            PastedResult.TitleFrom(text),
            classNames,
            name => PastedResult.MatchByName(profiles, name),
            DateTime.Now);

        if (form.ShowDialog(this) != DialogResult.OK || form.Session is not { } session)
        {
            return;
        }

        if (!_history.Add(session))
        {
            // Taken straight back out, rather than left listed and held for
            // a retry: pressing Paste a Result again makes a new session, and
            // the two would both be filed once the file could be written.
            _history.Delete(session.Id);

            AppDialog.Show(this,
                "The race couldn't be written to the race history file - something may have it open, or the disk may be full. "
                + "Nothing was added. Try Paste a Result again in a moment.",
                "Paste a result", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            RefreshList();
            return;
        }

        var history = _history;
        var id = session.Id;

        _undo.Capture($"Paste result '{session.Label}'", () =>
        {
            history.Delete(id);
            RefreshList();
        });

        RefreshListAndSelect(id);
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
            var lines = new List<string> { CsvHelper.BuildRow("Position", "Driver", "Car", "Laps", "Fastest Lap (s)", "Elapsed", "Status", "Laps Counted By Hand") };
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
                    r.Status,
                    // Last, so a sheet built against the old columns still
                    // finds every one where it was (22 Oct 2026).
                    HandCountedLaps.In(r)));
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
