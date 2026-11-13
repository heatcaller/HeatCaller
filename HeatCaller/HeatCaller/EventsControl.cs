namespace HeatCaller;

/// <summary>
/// Manages events - the grouping that keeps one race weekend's qualifying
/// separate from the next.
///
/// The page answers two questions: which event are new rounds joining, and
/// what has each event actually covered. The first matters during an event,
/// the second afterwards.
/// </summary>
public sealed class EventsControl : UserControl
{
    private readonly RaceEventStore _events;
    private readonly RoundDefinitionStore _rounds;
    private readonly SessionHistoryStore _history;
    private readonly UndoService _undo;

    /// <summary>Both only for the shared delete - see RaceDayDeleter.</summary>
    private readonly SeasonStore? _seasons;

    private readonly TrackLayoutStore? _layouts;

    /// <summary>
    /// Raised when a new race day is wanted. The host routes it to The
    /// Day's own command, so there is ONE way a race day is started and one
    /// dialog that asks what kind of night it is - see
    /// MainForm._startRaceDay. This page used to make one with a name and a
    /// notes box and nothing else: no shape, no classes, and the check-in
    /// rule left at its bare default. A day made here could not build a
    /// programme until somebody went to The Day and finished it.
    /// </summary>
    public event Action? StartRaceDayRequested;

    private readonly DataGridView _grid = new();
    private readonly Label _lblActive = new();
    private readonly Label _lblStatus = new();

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    public EventsControl(RaceEventStore events, RoundDefinitionStore rounds,
        SessionHistoryStore history, UndoService undo,
        SeasonStore? seasons = null, TrackLayoutStore? layouts = null)
    {
        _events = events;
        _rounds = rounds;
        _history = history;
        _undo = undo;
        _seasons = seasons;
        _layouts = layouts;

        Dock = DockStyle.Fill;
        ForeColor = AppTheme.TextDark;

        // The body face, worn rather than set once: this page sits in a tab
        // whose row is drawn in the bold face, which it would otherwise
        // take, and set once it stayed at the size it was built at when the
        // text size changed. See TextSize.
        //
        // No design size and no MinimumSize: the 900x540 floor was honoured
        // over the size the page was given, and a small window lost the
        // buttons and the status line off the bottom. A short window
        // scrolls the page instead (FillPage).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshAll();
    }

    /// <summary>The page's own face changed - handed the body face at a new text size - so everything else it keeps in step follows. See TextSize.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>A new text size, reaching this page through its tab's bold face - the only way it does, since the page wears a face of its own. See TextSize.</summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>
    /// The intro, then one card: which day is current, the grid of every
    /// day taking the height that is left, the buttons under it and the
    /// status line. On the layout pieces (LayoutPieces.cs), so nothing is
    /// placed by hand.
    ///
    /// It used to be: the intro measured and the rest placed under it once,
    /// the grid anchored to stretch and the buttons to the bottom, over a
    /// 900x540 floor - and the intro capped at a width that fitted that
    /// floor, so it wrapped short of the page on any wider one.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Every race day the club has run, and the one it's running now - the same days Race Day > Setup works on. "
                 + "A race day groups its rounds: without one, qualifying counts every heat ever recorded for a class, "
                 + "so a second day would seed its mains from both days mixed together.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        // Which day is current, in the heading face - and in the success
        // colour while it is open (RefreshGrid).
        _lblActive.AutoSize = true;
        _textSize.Wear(_lblActive, () => AppTheme.HeadingFont);

        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        // Ctrl-click for individual rows, Shift-click for a range, Ctrl+A
        // for the lot - which DataGridView gives for free once MultiSelect
        // is on. Only Delete Day acts on more than one; everything else
        // here (rename, switch to, close, rounds) reads the current row,
        // which is still exactly one whatever is selected.
        _grid.MultiSelect = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "name", HeaderText = "Race day", FillWeight = 90 },
            new DataGridViewTextBoxColumn { Name = "state", HeaderText = "State", FillWeight = 45 },
            new DataGridViewTextBoxColumn { Name = "started", HeaderText = "Started", FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "days", HeaderText = "Days run", FillWeight = 70 },
            new DataGridViewTextBoxColumn { Name = "rounds", HeaderText = "Rounds", FillWeight = 40 },
            new DataGridViewTextBoxColumn { Name = "recorded", HeaderText = "Recorded", FillWeight = 45 },
            new DataGridViewTextBoxColumn { Name = "notes", HeaderText = "Notes", FillWeight = 90 }
        );

        // Every heading whole, at the text size in use - see Pieces.KeepHeadingsWhole.
        _textSize.Keep(() => Pieces.KeepHeadingsWhole(_grid));
        // Double-click switches to the day, matching what selecting one in
        // The Day's picker does. Not a rename: switching is the thing this
        // page is for, and the one that is safe to do by accident - it is
        // undoable and changes no stored day.
        _grid.CellDoubleClick += (_, args) =>
        {
            if (args.RowIndex >= 0)
            {
                MakeActive();
            }
        };

        // The same command The Day's button runs, through the host - not a
        // second way to make a race day. See StartRaceDayRequested.
        var btnNew = new Button { Text = "New Race Day..." };
        btnNew.Click += (_, _) => StartRaceDayRequested?.Invoke();

        var btnEdit = new Button { Text = "Rename..." };
        btnEdit.Click += (_, _) => AddOrEdit(Selected());

        var btnActive = new Button { Text = "Switch to This Day" };
        btnActive.Click += (_, _) => MakeActive();

        var btnClose = new Button { Text = "Close Day" };
        btnClose.Click += (_, _) => CloseOrReopen();

        var btnRounds = new Button { Text = "Rounds..." };
        btnRounds.Click += (_, _) => ShowRounds();

        var btnDelete = new Button { Text = "Delete Day..." };
        btnDelete.Click += (_, _) => DeleteSelected();

        // What the last command did - a sentence or two, wrapping.
        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = AppTheme.TextMuted;

        var days = new Section("Race days");

        days.Add(_lblActive);
        days.AddFill(_grid, () => _grid.ColumnHeadersHeight + 3 * _grid.RowTemplate.Height);
        days.Add(new ButtonBar().With(btnNew, btnEdit, btnActive, btnClose, btnRounds, btnDelete));
        days.Add(_lblStatus, gapBelow: 0);

        var page = new FillPage();

        page.Add(lblIntro);
        page.AddFill(days, days.LeastHeight, gapBelow: 0);

        Controls.Add(page);
    }

    private RaceEvent? Selected() =>
        _grid.CurrentRow?.Tag is string id ? _events.Get(id) : null;

    public void RefreshAll() => RefreshGrid();

    private void RefreshGrid()
    {
        var selectedId = _grid.CurrentRow?.Tag as string;
        _grid.Rows.Clear();

        var active = _events.Active;
        _lblActive.Text = active switch
        {
            null => "No race day is open - rounds aren't being grouped",

            // Said here as well as in the Status column, because a closed
            // day can be the current one and "Current: Race Day 4 May" on
            // its own reads as a day you can still race.
            { IsOpen: false } shut => $"Current: {shut.DisplayName} (closed - nothing new joins it)",

            { } open => $"Current: {open.DisplayName}",
        };

        _lblActive.ForeColor = active is { IsOpen: true } ? AppTheme.Success : AppTheme.TextMuted;

        foreach (var raceEvent in _events.All)
        {
            var days = _events.DaysFor(raceEvent, _rounds, _history);
            var recorded = raceEvent.RoundIds.Count(id => _rounds.Get(id)?.IsRecorded == true);

            var daysText = days.Count switch
            {
                0 => "-",
                1 => days[0].ToShortDateString(),
                _ => $"{days[0].ToShortDateString()} - {days[^1].ToShortDateString()} ({days.Count})",
            };

            var isActive = active?.Id == raceEvent.Id;

            var rowIndex = _grid.Rows.Add(
                raceEvent.DisplayName + (isActive ? "   [CURRENT]" : string.Empty),
                raceEvent.IsOpen ? "Open" : "Closed",
                AppTime.DateAndTime(raceEvent.StartedUtc.ToLocalTime()),
                daysText,
                raceEvent.RoundIds.Count,
                recorded,
                raceEvent.Notes.Replace('\r', ' ').Replace('\n', ' '));

            _grid.Rows[rowIndex].Tag = raceEvent.Id;

            if (isActive)
            {
                _grid.Rows[rowIndex].Cells[0].Style.Font = AppTheme.BoldFont;
                _grid.Rows[rowIndex].Cells[0].Style.ForeColor = AppTheme.Success;
            }
            else if (!raceEvent.IsOpen)
            {
                _grid.Rows[rowIndex].DefaultCellStyle.ForeColor = AppTheme.TextMuted;
            }

            if (selectedId == raceEvent.Id)
            {
                _grid.Rows[rowIndex].Selected = true;
                _grid.CurrentCell = _grid.Rows[rowIndex].Cells[0];
            }
        }

        _lblStatus.Text = $"{Plural.Of(_events.All.Count, "race day", "race days")}.";
    }

    /// <summary>
    /// Every undo on this page puts back only what its own action changed.
    ///
    /// They used to restore a snapshot of every event, which also undid
    /// whatever else had happened since - rounds generated into an event
    /// dropped back out of it - and set the active event without telling
    /// check-in, tech or the run sheet, which carried on showing the other
    /// event. Editing was worse still: the snapshot was taken after the
    /// dialog had already changed the event, so Undo put back the edit.
    /// </summary>
    /// <summary>
    /// Renames a race day and edits its notes. Nothing else - everything
    /// about how a day RUNS (what kind of night it is, which classes are
    /// racing, the format, the check-in rule) is set on The Day, which is
    /// the one place that edits those.
    ///
    /// The create half of this is gone. It made a race day out of a name
    /// and a notes box, which is not a race day anybody can use: no shape,
    /// no classes, the check-in rule left at its bare default, and nothing
    /// carried over from the last one. New Race Day on this page now runs
    /// the same command The Day's does - see StartRaceDayRequested.
    /// </summary>
    private void AddOrEdit(RaceEvent? existing)
    {
        if (existing is null)
        {
            AppDialog.Show(this, "Pick a race day in the list first.",
                "Nothing picked", MessageBoxButtons.OK, MessageBoxIcon.Information);

            return;
        }

        // Taken BEFORE the dialog opens: its Save writes straight into the
        // race day it was given.
        var previousName = existing.Name;
        var previousNotes = existing.Notes;

        using var form = new EventEditForm(existing);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var eventId = form.Result.Id;

        _undo.Capture($"Rename {form.Result.DisplayName}", () =>
        {
            if (_events.Get(eventId) is { } target)
            {
                target.Name = previousName;
                target.Notes = previousNotes;
                _events.Upsert(target);
            }

            RefreshGrid();
        });

        _events.Upsert(form.Result);
        RefreshGrid();
    }

    private void MakeActive()
    {
        if (Selected() is not { } raceEvent)
        {
            return;
        }

        // A closed day is switched to, not refused. This used to send the
        // club away to reopen it first, while The Day's picker switched to
        // a closed day without a word - the same gesture on two pages
        // giving two answers. Pointing at a closed day is how its results,
        // its standings and its run sheet are looked at, and Today answers
        // it properly: the checklist shows "this day is closed" with
        // "Start Tonight..." under it. Nothing can join a closed day
        // wherever it is current, because every build refuses it by name.
        var previousActiveId = _events.Active?.Id;

        // Whether the day being left was open at the moment of the switch.
        // The undo below must tell "closed since the switch" apart from
        // "already closed when it was current": the second is the very
        // case the comment above supports (a closed day switched to so its
        // results can be looked at), and refusing to go back to it would
        // spend the undo while leaving the new day current.
        var previousWasOpen = _events.Active?.IsOpen ?? false;
        _undo.Capture("Switch to another race day", () =>
        {
            // SetActive, not a restore, so check-in, tech and the run sheet
            // follow. A race day deleted since cannot be made current
            // again; one closed since the switch is left alone rather than
            // pointed back at. One that was closed already when it was
            // current goes back to being current and closed, exactly as it
            // was - making it current never reopens it.
            //
            // And only while the day switched to is still the current one.
            // If another day has been made current since (tonight's new day
            // started from the Calendar, or a day opened from the phone),
            // that later choice stands: undoing this older switch must not
            // take the club off tonight's day and send its check-ins and
            // new rounds to the day before. A later undo that refused (and
            // was spent) can leave this entry next on the stack, which is
            // exactly how it would be reached. The Setup picker's undo and
            // the phone's day-open check the same thing.
            var stillOnSwitchedDay = _events.Active?.Id == raceEvent.Id;
            if (stillOnSwitchedDay
                && (previousActiveId is null
                    || _events.Get(previousActiveId) is { } previous && (previous.IsOpen || !previousWasOpen)))
            {
                _events.SetActive(previousActiveId);
            }

            RefreshGrid();
        });

        _events.SetActive(raceEvent.Id);
        RefreshGrid();

        _lblStatus.Text = raceEvent.IsOpen
            ? $"New rounds will now join \"{raceEvent.DisplayName}\"."
            : $"Now showing \"{raceEvent.DisplayName}\". It is closed, so nothing new joins it - "
              + "reopen it to race, or start a new day.";
    }

    private void CloseOrReopen()
    {
        if (Selected() is not { } raceEvent)
        {
            return;
        }

        var eventId = raceEvent.Id;

        if (raceEvent.IsOpen)
        {
            // The same guards as The Day's Close Day - refused while one of
            // its rounds is on track, and asked with No as the default. This
            // used to be a one-line Yes-default question of its own, so the
            // second button for the same close had neither.
            if (!RaceDayEventControl.AskToCloseDay(this, raceEvent, _rounds))
            {
                return;
            }

            // Closing the current day leaves none current.
            var wasActive = _events.Active?.Id == eventId;

            _undo.Capture($"Close {raceEvent.DisplayName}", () =>
            {
                _events.Reopen(eventId);

                // Active again only if nothing else has been made active
                // in the meantime - that later choice is not this undo's
                // to take back.
                if (wasActive && _events.Active is null && _events.Get(eventId) is not null)
                {
                    _events.SetActive(eventId);
                }

                RefreshGrid();
            });

            _events.Close(eventId);
        }
        else
        {
            var closedAtUtc = raceEvent.FinishedUtc;

            // A closed day can be the current one (MakeActive switches to
            // closed days on purpose), and reopening it leaves it current.
            // Close, below, clears "current" whenever the day it closes is
            // current, so without remembering this the undo would put the
            // day back to closed AND drop check-in, tech and the run sheet
            // onto the legacy holder - not the state before the reopen.
            var wasActive = _events.Active?.Id == eventId;

            _undo.Capture($"Reopen {raceEvent.DisplayName}", () =>
            {
                // Close rather than setting the date, so that an event made
                // active since it was reopened stops being active - and
                // with the time it was really closed, not now.
                _events.Close(eventId, closedAtUtc);

                // Current again only if it was current before the reopen
                // and nothing else has been made current in the meantime -
                // the same rule as the undo of Close above. Close has just
                // cleared "current", so Active is null exactly when this
                // day was still the current one.
                if (wasActive && _events.Active is null && _events.Get(eventId) is not null)
                {
                    _events.SetActive(eventId);
                }

                RefreshGrid();
            });

            // Current again when nothing else is, the same as Reopen Day on
            // Race Day - closing the active event leaves none active, so
            // without this an event could be reopened and still have nothing
            // pointed at it. An event reopened while tonight is running does
            // not steal it. See RaceEventStore.Reopen.
            _events.Reopen(eventId, makeCurrentIfNoneIs: true);
        }

        RefreshGrid();
    }

    /// <summary>Lists what's actually in an event, so its qualifying can be checked rather than trusted.</summary>
    private void ShowRounds()
    {
        if (Selected() is not { } raceEvent)
        {
            return;
        }

        if (raceEvent.RoundIds.Count == 0)
        {
            AppDialog.Show(this,
                $"\"{raceEvent.DisplayName}\" has no rounds yet.\r\n\r\n"
                + "Make it active, then build its heats or mains in the program on Race Day > Next Steps.",
                "No rounds", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var lines = raceEvent.RoundIds
            .Select(_rounds.Get)
            .Where(r => r is not null)
            .Select(r => r!)
            .OrderBy(r => r.ClassName)
            .ThenBy(r => r.Order)
            .Select(r => $"   {r.ClassName}  -  {r.RoundLabel}  ({(r.IsRecorded ? "recorded" : "not run")})");

        AppDialog.Show(this,
            $"{raceEvent.DisplayName}\r\n\r\n" + string.Join("\r\n", lines),
            "Rounds on this race day", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// The SAME delete The Day runs - see RaceDayDeleter.
    ///
    /// This page had its own, and it did far less: it removed the race day
    /// and left its rounds, its results, its track records and its badges
    /// exactly where they were, with a message explaining that the rounds
    /// would now be counted "alongside everything else" - which is the
    /// unbounded qualifying race days exist to prevent, offered as a
    /// footnote. Whichever page you reach a delete from, it now asks the
    /// same questions and does the same things.
    /// </summary>
    private void DeleteSelected()
    {
        // Every row that is selected, not just the current one: Ctrl+A here
        // selects the lot, which is how somebody clears out a season of test
        // days without answering the same dialog forty times. The legacy
        // holder is filtered out rather than refused - it is in the list,
        // it will be caught by a select-all, and it is not a race day.
        var chosen = _grid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(r => r.Tag as string)
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => _events.Get(id!))
            .Where(e => e is { IsLegacy: false })
            .Select(e => e!)
            .ToList();

        if (chosen.Count == 0)
        {
            return;
        }

        if (_seasons is null || _layouts is null)
        {
            // Without them the delete cannot reach the championships and the
            // track records, and a delete that silently does less than the
            // one on the other page is the thing this replaced.
            AppDialog.Show(this,
                "Deleting a race day isn't available from here in this window. Use Delete Day on Race Day > Setup.",
                "Not available here", MessageBoxButtons.OK, MessageBoxIcon.Information);

            return;
        }

        RaceDayDeleter.AskMany(this, chosen, _events, _rounds, _history, _seasons, _layouts, _undo, () =>
        {
            if (!IsDisposed)
            {
                RefreshGrid();
            }
        });
    }
}

/// <summary>
/// Renames a race day and edits its notes. Everything else about a day is set on Race Day &gt; The Day.
///
/// On the layout pieces (DialogFit): the name and the notes lined up in a
/// field grid, running to the right-hand edge, the hint under the name in
/// the field's column, and Save and Cancel at the foot; as tall as that. It
/// used to be placed in pixels in a 440x260 window: the fields at a fixed x
/// of 100 whatever the captions beside them needed, the hint wrapped at a
/// fixed 324px, the notes a fixed 90px tall at every text size, and Save and
/// Cancel in a row starting at a fixed x of 230 - which, at a larger text
/// size, ran Cancel past the window's right-hand edge.
/// </summary>
public sealed class EventEditForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size.</summary>
    private const int DialogWidth = 440;

    /// <summary>How many lines of notes show before the box scrolls - about the 90px it was at the normal text size.</summary>
    private const int NotesLines = 4;

    private readonly TextBox _txtName = new();
    private readonly TextBox _txtNotes = new();

    public RaceEvent Result { get; }

    public EventEditForm(RaceEvent? existing)
    {
        Result = existing ?? new RaceEvent();

        Text = existing is null ? "New Race Day" : $"Edit {Result.DisplayName}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        _txtName.Text = Result.Name;
        _txtName.PlaceholderText = "Spring Regional, Club Night 12 Sept...";

        _txtNotes.Multiline = true;

        // Enter starts a new line here, as in every other notes box in the
        // app. Without it the form's AcceptButton takes Enter, so Save
        // fired and the dialog closed on a half-typed note, and a line
        // break could never be typed at all. Enter in the name box still
        // saves.
        _txtNotes.AcceptsReturn = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        _txtNotes.Text = Result.Notes;

        var btnSave = new Button { Text = "Save" };
        btnSave.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_txtName.Text))
            {
                AppDialog.Show(this, "Give the race day a name.", "Missing name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Result.Name = _txtName.Text.Trim();
            Result.Notes = _txtNotes.Text;
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // Both fields run to the right-hand edge, where the buttons end, as
        // the 324px fields did beside their captions; the hint under the
        // name, wrapping to the field's width.
        var fields = new FieldGrid();
        fields.AddRow("Name:", _txtName, stretch: true);
        fields.AddHint("Rounds built while this race day is the current one belong to it. Qualifying and mains are then worked out "
                       + "from those rounds alone.");
        fields.AddRow("Notes:", _txtNotes, stretch: true);

        var page = new FillPage();
        page.Add(fields);

        // Against the right-hand edge, Cancel furthest right, as they were -
        // and Tab still reaches Save first: the bar takes them right to left,
        // in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);
        btnSave.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        // After the theme pass, in the face it leaves the box in: a few
        // lines of it, as the box draws them (Pieces.LineHeight).
        _txtNotes.Height = NotesLines * Pieces.LineHeight(_txtNotes.Font) + UiLayout.HintGap * 2;

        DialogFit.Keep(this, page, width: DialogWidth);
    }
}
