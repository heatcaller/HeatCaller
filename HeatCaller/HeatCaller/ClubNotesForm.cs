namespace HeatCaller;

/// <summary>
/// Tonight's club notes: the asides that are not about the racing, and the
/// one screen in the app whose job is deciding what a stranger's words are
/// allowed to become.
///
/// WHY IT IS A WINDOW OFF RACE CONTROL (12 Oct 2026)
///
/// A dedication arrives in the middle of an evening. The person it has to
/// reach is holding a microphone and looking at Race Control, and they have
/// about fifteen seconds of attention to give it between a flag and a grid.
/// So it is one button on the announcer strip - which already carries Hold,
/// Play again and Skip for the same reason - and the button says how many
/// are waiting, because a route from the room is no use if nobody notices
/// anything came in.
///
/// WHAT THE LIST IS FOR, AND IT IS NOT BROWSING
///
/// Three things, in the order they are wanted: tick off what the room sent,
/// type one of your own, and take back one that should not have gone on.
/// Everything else a list could offer - sorting, editing, history - is
/// absent on purpose. A note is one sentence, it lives for one night, and
/// an evening is not the time to be administering a database.
///
/// THE APPROVAL IS THE WHOLE POINT
///
/// Anything from the room arrives unapproved and nothing unapproved is ever
/// offered to the announcer - see <see cref="ClubNotes"/>, which built that
/// gate before either route existed. This window is the gate's only door.
/// So the waiting ones are listed FIRST and marked, Approve is the default
/// button, and the wording on it says what it does out loud rather than
/// saying OK: the person pressing it is agreeing that forty people are
/// about to hear this.
/// </summary>
internal sealed class ClubNotesForm : Form
{
    /// <summary>The list shows its headings and at least this many notes before the window scrolls instead.</summary>
    private const int LeastRows = 5;

    /// <summary>How wide the typing box is at the normal text size - a sentence, not a word.</summary>
    private const int NoteBoxWidth = 460;

    private const string WaitingMark = "Waiting";
    private const string ReadyMark = "Ready";
    private const string SaidMark = "Said";

    private readonly ClubNotes _notes;

    private readonly DataGridView _grid = new();
    private readonly TextBox _txtNote = new();
    private readonly Label _lblCount = new();
    private readonly Button _btnAdd = new();
    private readonly Button _btnApprove = new();
    private readonly Button _btnRemove = new();

    // 17 Oct 2026, Bug Hunt 8, A29-4: notes from the room arrive on other
    // threads while this modal window sits open waiting for approvals, and
    // nothing redrew it. Polled like RoomVoteForm's tick, and redrawn only
    // when the list actually changed, so rows are not rebuilt under the
    // race director's mouse.
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 2000 };
    private string _drawn = "";

    /// <summary>
    /// A note was just added from the box and the box has not been left
    /// since - Enter does not approve while this is set.
    ///
    /// WHY (13 Nov 2026, bug hunt 17): adding a note empties the box, and
    /// an empty box makes Approve the accept button. An Enter held a
    /// little long, or tapped twice, was then one press to add and the
    /// next to approve whichever note from the room the list had current -
    /// the oldest waiting one when no row had been clicked - unread.
    /// Clicking the list or a button leaves the box and puts Enter back.
    /// </summary>
    private bool _justAdded;

    public ClubNotesForm(ClubNotes notes)
    {
        _notes = notes;

        Text = "Tonight's club notes";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var blurb = new Label
        {
            Text = "The things about tonight the timing can't know - a dedication, a birthday, the raffle closing at "
                   + "eight. The announcer works one into a sentence of its own between races, says it once, and "
                   + "adds nothing to it. Anything sent in from the room waits here until you approve it.",
            AutoSize = true,
            MaximumSize = new Size(UiLayout.AtTextSize(640), 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "state", HeaderText = "State", FillWeight = 34 },
            new DataGridViewTextBoxColumn { Name = "from", HeaderText = "From", FillWeight = 42 },
            new DataGridViewTextBoxColumn { Name = "note", HeaderText = "Note", FillWeight = 190 });

        // 17 Oct 2026, Bug Hunt 8, A29-5: a note longer than the column
        // was cut off with an ellipsis, so the race director approved text
        // they had not seen all of. Wrapped, and the row grows to show it.
        _grid.Columns["note"]!.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;

        _grid.SelectionChanged += (_, _) => UpdateButtons();

        _txtNote.MaxLength = ClubNotes.MostChars;

        // Wide enough for the sentence it holds. A FieldGrid gives a field
        // its own width, and a text box with none asked for gets whatever
        // is left - which on this row is a few characters.
        _txtNote.Width = UiLayout.AtTextSize(NoteBoxWidth);

        // Enter adds it, which is what somebody typing one between races
        // expects - and it is why Approve is NOT the accept button while
        // the box has anything in it. See UpdateButtons.
        _txtNote.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;

                // Nothing typed is nothing to add - the Enter after one
                // was added must not put up "Not added" either.
                if (_txtNote.Text.Trim().Length > 0)
                {
                    AddTyped();
                }
            }
        };

        _txtNote.TextChanged += (_, _) => UpdateButtons();

        // See _justAdded.
        _txtNote.Leave += (_, _) =>
        {
            _justAdded = false;
            UpdateButtons();
        };

        _btnAdd.Text = "Add";
        _btnAdd.Click += (_, _) => AddTyped();

        // SAYS WHAT IT DOES, not OK. The person pressing this is agreeing
        // that the room is about to hear somebody else's words over a PA.
        _btnApprove.Text = "Let the announcer say it";
        _btnApprove.Click += (_, _) => ApproveSelected();

        _btnRemove.Text = "Remove";
        _btnRemove.Click += (_, _) => RemoveSelected();

        var btnClose = new Button { Text = "Close", DialogResult = DialogResult.OK };

        _lblCount.AutoSize = true;
        _lblCount.ForeColor = AppTheme.TextMuted;
        _lblCount.Font = AppTheme.SmallFont;
        _lblCount.Anchor = AnchorStyles.Left;

        var typing = new FieldGrid();
        typing.AddRow("Your own note:", FieldGrid.Inline(_txtNote, _btnAdd));

        var footer = new SplitBar(
            new ButtonBar().With(_lblCount),
            new ButtonBar(alignRight: true).With(btnClose, _btnRemove, _btnApprove));

        _btnApprove.TabIndex = 0;
        _btnRemove.TabIndex = 1;
        btnClose.TabIndex = 2;

        var page = new FillPage();
        page.Add(blurb, gapBelow: UiLayout.RowGap);
        page.AddFill(_grid, _ => _grid.ColumnHeadersHeight + LeastRows * _grid.RowTemplate.Height + 2, gapBelow: UiLayout.RowGap);
        page.Add(typing, gapBelow: UiLayout.RowGap);
        page.Add(footer, gapBelow: 0);

        Controls.Add(page);

        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);
        Pieces.KeepHeadingsWhole(_grid);

        Fill();

        // See _tick (Bug Hunt 8, A29-4).
        _tick.Tick += (_, _) =>
        {
            if (Signature() != _drawn)
            {
                Fill();
            }
        };
        _tick.Start();
        FormClosed += (_, _) =>
        {
            _tick.Stop();
            _tick.Dispose();
        };

        // Wider than it is tall: a note is a sentence, and a column of
        // sentences wants the width more than it wants the rows.
        DialogFit.Keep(this, page, width: 720, height: 460);
    }

    /// <summary>
    /// Draws the list from the store.
    ///
    /// WAITING FIRST, then ready to be said, then what has gone out -
    /// which is the order of what the person looking at this can do
    /// something about. Within each, oldest first, because a note typed at
    /// seven is about something that was happening at seven.
    /// </summary>
    private void Fill()
    {
        var wanted = _grid.CurrentRow?.Tag as string;

        _grid.Rows.Clear();

        // One read of the list for the rows AND the signature (22 Oct 2026,
        // bug hunt): two reads let a note that arrived between them into the
        // signature but not the grid, and the tick then never redrew it.
        var all = _notes.All.ToList();

        var ordered = all
            .OrderBy(one => one.Approved ? (one.SaidAtUtc is null ? 1 : 2) : 0)
            .ThenBy(one => one.AddedAtUtc)
            .ToList();

        foreach (var note in ordered)
        {
            var state = !note.Approved ? WaitingMark : note.SaidAtUtc is null ? ReadyMark : SaidMark;

            // "You" rather than blank for one the race director typed, so
            // the column is never empty and the two sources are telling
            // apart at a glance - which is the thing somebody who has
            // approved forty of them actually wants.
            var from = note.From.Length > 0
                ? note.From
                : ClubNotes.WasTyped(note) ? "You" : "The room";

            var at = _grid.Rows.Add(state, from, note.Text);
            _grid.Rows[at].Tag = note.Id;

            if (!note.Approved)
            {
                _grid.Rows[at].DefaultCellStyle.ForeColor = AppTheme.Accent;
            }
            else if (note.SaidAtUtc is not null)
            {
                _grid.Rows[at].DefaultCellStyle.ForeColor = AppTheme.TextMuted;
            }
        }

        // Back onto the row that was selected, where it is still there -
        // approving one must not jump the selection to the top of a list
        // somebody is working down.
        //
        // THROUGH THE CURRENT CELL, not by marking the row selected
        // (12 Oct 2026). A DataGridView's CurrentRow follows its CURRENT
        // CELL, and clearing the rows puts that back on the first one - so
        // this left the highlight on the row somebody was working on and
        // CurrentRow at the top of the list. Selected() reads CurrentRow.
        // "Let the announcer say it" therefore approved the OLDEST waiting
        // note rather than the one on the screen, with nothing on the
        // screen to say so, and Remove offered to delete it. With
        // FullRowSelect and MultiSelect off, moving the cell moves the
        // selection with it.
        if (wanted is { Length: > 0 })
        {
            var column = _grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(one => one.Visible);

            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Tag as string != wanted)
                {
                    continue;
                }

                if (column is not null && row.Visible)
                {
                    _grid.CurrentCell = row.Cells[column.Index];
                }

                row.Selected = true;
                break;
            }
        }

        var (toSay, toApprove, said) = _notes.Counts();

        _lblCount.Text = _notes.All.Count == 0
            ? "Nothing for tonight yet."
            : $"{Plural.Of(toApprove, "note")} waiting for you, {toSay} ready to be said, {said} already said.";

        _drawn = Signature(all);

        UpdateButtons();
    }

    /// <summary>
    /// What the list holds, by id and state, so the tick can tell a change
    /// (a note added, approved, said or removed) from nothing happening.
    /// See _tick.
    /// </summary>
    private string Signature() => Signature(_notes.All);

    private static string Signature(IEnumerable<ClubNote> notes) =>
        string.Join("|", notes.Select(one => $"{one.Id}:{one.Approved}:{one.SaidAtUtc is null}"));

    private ClubNote? Selected()
    {
        // The id off the row's Tag rather than its index: the grid is
        // redrawn from the store on every change, so an index is stale the
        // moment anything is approved.
        var id = _grid.CurrentRow?.Tag as string;

        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        return _notes.All.FirstOrDefault(one => string.Equals(one.Id, id, StringComparison.Ordinal));
    }

    /// <summary>
    /// Which buttons mean anything right now - a button that does nothing
    /// reads as a fault rather than as a state.
    ///
    /// AND WHICH IS THE ACCEPT BUTTON. Enter sends the typed note while
    /// there is one being typed, and approves the selection otherwise.
    /// Both are what somebody pressing Enter at that moment meant, and
    /// getting it the other way round would either lose a half-typed note
    /// or put a stranger's words on the PA with a stray keypress.
    /// </summary>
    private void UpdateButtons()
    {
        var selected = Selected();
        var typing = _txtNote.Text.Trim().Length > 0;

        _btnAdd.Enabled = typing;
        _btnApprove.Enabled = selected is { Approved: false };
        _btnRemove.Enabled = selected is not null;

        AcceptButton = typing ? _btnAdd : _btnApprove.Enabled && !_justAdded ? _btnApprove : null;
    }

    private void AddTyped()
    {
        if (_notes.Add(_txtNote.Text) is { } why)
        {
            AppDialog.Show(this, why, "Not added", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Set before the box is emptied, which is what works out the
        // accept button. See _justAdded.
        _justAdded = true;

        _txtNote.Clear();
        Fill();
        _txtNote.Focus();
    }

    private void ApproveSelected()
    {
        if (Selected() is not { } note)
        {
            return;
        }

        _notes.Approve(note.Id);
        Fill();
    }

    /// <summary>
    /// Takes one off tonight's list.
    ///
    /// ASKS FIRST, and only for one that has not been said: removing a
    /// note that already went out is tidying, and removing one that is
    /// waiting or ready is deciding the room will never hear it. The
    /// second deserves a question; the first does not.
    /// </summary>
    private void RemoveSelected()
    {
        if (Selected() is not { } note)
        {
            return;
        }

        if (note.SaidAtUtc is null)
        {
            var answer = AppDialog.Show(
                this,
                $"Remove this note? The announcer will never say it.\n\n“{note.Text}”",
                "Remove the note",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer != DialogResult.Yes)
            {
                return;
            }
        }

        _notes.Remove(note.Id);
        Fill();
    }
}
