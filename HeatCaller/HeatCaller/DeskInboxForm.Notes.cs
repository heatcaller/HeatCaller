namespace HeatCaller;

/// <summary>
/// Tonight's club notes in the Desk Inbox - what the club notes window did
/// (12 Oct 2026 onwards), moved here.
///
/// THE APPROVAL IS STILL THE WHOLE POINT. Anything from the room arrives
/// unapproved and nothing unapproved is offered to the announcer - see
/// <see cref="ClubNotes"/>, which keeps that gate; this is its door. The
/// button still says what it does out loud ("Let the announcer say it")
/// rather than OK: the person pressing it agrees forty people will hear it.
/// </summary>
internal sealed partial class DeskInboxForm
{
    /// <summary>How wide the typing box is at the normal text size - a sentence, not a word.</summary>
    private const int NoteBoxWidth = 520;

    private readonly ButtonBar _noteBox = new(tight: true);
    private readonly Label _noteCaption = new();
    private readonly TextBox _txtNote = new();
    private readonly Button _btnAddNote = new() { Text = "Add" };

    /// <summary>
    /// A note was just added from the box and the box has not been left
    /// since. Enter in the box only ever adds - the list's answers are on
    /// letters and the list's own Enter - so a long or doubled Enter can
    /// never approve a stranger's note unread (13 Nov 2026, bug hunt 17,
    /// as the notes window had it). Kept for the test that says so.
    /// </summary>
    private bool _justAdded;

    /// <summary>The flag above, for tests; also what keeps it from reading as unused (CS0414).</summary>
    internal bool JustAddedForTests => _justAdded;

    private void BuildNoteBox()
    {
        var caption = _noteCaption;
        caption.Text = "Your own note:";
        caption.AutoSize = true;
        caption.Anchor = AnchorStyles.Left;

        _txtNote.PlaceholderText = "For the announcer to read out between races";
        _txtNote.MaxLength = ClubNotes.MostChars;
        _txtNote.Width = UiLayout.AtTextSize(NoteBoxWidth);
        _txtNote.AccessibleName = "Your own note for the announcer";

        // Enter adds it, which is what somebody typing one between races
        // expects. Nothing typed is nothing to add.
        _txtNote.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;

                if (_txtNote.Text.Trim().Length > 0)
                {
                    AddTypedNote();
                }
            }
        };

        _txtNote.TextChanged += (_, _) => _btnAddNote.Enabled = _txtNote.Text.Trim().Length > 0;
        _txtNote.Leave += (_, _) => _justAdded = false;
        _btnAddNote.Enabled = false;
        _btnAddNote.Click += (_, _) => AddTypedNote();

        _noteBox.With(caption, _txtNote, _btnAddNote);

        // The box takes the width the list has, with its caption and Add
        // beside it on one line - rather than a fixed width that wrapped
        // onto three lines in the middle column.
        _list.SizeChanged += (_, _) => FitNoteBox(caption);
        caption.SizeChanged += (_, _) => FitNoteBox(caption);
    }

    private void FitNoteBox(Label caption)
    {
        var room = _list.ClientSize.Width - _list.Padding.Horizontal
            - (caption.Visible || !Visible ? caption.Width + caption.Margin.Horizontal : 0)
            - _btnAddNote.Width - _btnAddNote.Margin.Horizontal
            - _txtNote.Margin.Horizontal - UiLayout.AtDpi(8);
        var width = Math.Max(UiLayout.AtTextSize(120), room);

        if (_txtNote.Width != width)
        {
            _txtNote.Width = width;
        }
    }

    /// <summary>Notes from the room still waiting for the desk's OK.</summary>
    private IEnumerable<InboxItem> NoteItems()
    {
        if (_notes is not { } notes)
        {
            yield break;
        }

        // A shout-out naming a junior here tonight is flagged (15 Nov 2026, plan 4.5).
        var juniors = ClubNotes.JuniorsTonight(_profiles.All, _profiles.Get, _checkIns?.SnapshotCheckedIn());

        foreach (var note in notes.All.Where(n => !n.Approved).OrderBy(n => n.AddedAtUtc))
        {
            yield return new InboxItem(
                InboxSource.Note,
                note.Id,
                "note",
                "Club note",
                NoteFrom(note),
                note.Text,
                ClubNotes.NamesAJunior(note.Text, juniors) is not null ? ClubNotes.NamesAJuniorWords : "For the announcer to say",
                note.AddedAtUtc,
                RaceBlocking: false,
                note);
        }
    }

    /// <summary>"You" for one the desk typed, the room for one with no name - never an empty column.</summary>
    private static string NoteFrom(ClubNote note) =>
        note.From.Length > 0 ? note.From : ClubNotes.WasTyped(note) ? "You" : "The room";

    private void UpdateNoteButtons(InboxItem item, List<InboxItem>? batch)
    {
        _btnApprove.Text = "Let the announcer say it";
        _btnApprove.Enabled = true;
        _btnNoted.Visible = false;
        _btnDecline.Enabled = false;
        _btnDiscard.Visible = false;
        _btnRemove.Visible = true;
        _btnRemove.Enabled = batch is null;
    }

    private void AddTypedNote()
    {
        if (_notes is not { } notes)
        {
            return;
        }

        if (notes.Add(_txtNote.Text) is { } why)
        {
            AppDialog.Show(this, why, "Not added", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _justAdded = true;
        _txtNote.Clear();
        RefreshAll(force: true);
        _txtNote.Focus();
        Toast("Added. The announcer works it in between races.");
    }

    /// <summary>Lets the announcer say one or several - with Undo for the next ten seconds, while none has been said.</summary>
    private void ApproveNotes(List<InboxItem> items)
    {
        if (_notes is not { } notes)
        {
            return;
        }

        var ids = items.Select(i => i.Id).ToList();

        foreach (var id in ids)
        {
            notes.Approve(id);
        }

        Toast(ids.Count == 1 ? "The announcer can say it now." : $"The announcer can say all {ids.Count} now.",
            () =>
            {
                foreach (var id in ids)
                {
                    notes.Unapprove(id);
                }
            });
    }

    /// <summary>
    /// Takes one off tonight's list. Asks first only for one not said yet:
    /// removing one that went out is tidying; removing one still to say is
    /// deciding the room will never hear it.
    /// </summary>
    private void RemoveNote(ClubNote? note)
    {
        if (_notes is not { } notes || note is null)
        {
            return;
        }

        if (note.SaidAtUtc is null
            && AppDialog.Show(this, $"Remove this note? The announcer will never say it.\n\n“{note.Text}”",
                "Remove the note", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        notes.Remove(note.Id);
        RefreshAll(force: true);
    }
}
