namespace HeatCaller;

/// <summary>
/// Hearing how tonight's names come out, before they are read to a room.
///
/// A driver's "Announcer name" has been settable for a long time, and the
/// announcer has always preferred it over the real name
/// (<see cref="DriverProfile.SpokenName"/>, which is what the voice queue
/// puts into {driver} - AnnouncementService.VoiceQueue.cs). What nobody
/// could do was HEAR the result. The first time a club found out that a
/// name comes out wrong was over the PA, mid-programme, to forty people -
/// and the fix for it was a guess at a respelling that could not be tried
/// until the following week. That is the gap this closes: the roster read
/// aloud on demand, one name at a time, with the exact words being handed
/// to the voice shown underneath.
///
/// "Say the next one" is the point of the window rather than a convenience.
/// It walks the list from wherever you are, so checking forty drivers is
/// one button pressed forty times before the doors open, instead of forty
/// separate pieces of navigation that nobody will do.
///
/// It changes nothing and saves nothing. A name that comes out wrong is
/// fixed on the driver's profile, in the Announcer name box - which has a
/// Hear It of its own, so the respelling can be tried on the spot. This is
/// the ear, not the editor, in the way RacingWordsForm is a reference sheet
/// and nothing else.
///
/// On the layout pieces (DialogFit): one card with the introduction as its
/// hint line, the drivers taking the height the window has and the line
/// saying what is being read under them; and at the foot the tick box and
/// the count on the left with Say It, Say the Next One (in the app's colour
/// - it is what the window is for) and Close against the right-hand edge,
/// on one line (18 Nov 2026, dialog jazz-up; it was the same parts with no
/// card, the count on a line of its own over the buttons).
/// </summary>
public sealed class NameCheckForm : Form
{
    /// <summary>The list shows its headings and at least this many drivers before the window scrolls instead.</summary>
    private const int LeastRows = 5;

    /// <summary>What the line under the list says before anything has been read.</summary>
    private const string Idle = "Nothing read yet.";

    /// <summary>What the list says with nobody in it.</summary>
    private const string EmptyList = "No names to check. The drivers listed where you opened this show here, each with the name the announcer reads.";

    /// <summary>How wide the window is inside its frame at the normal text size.</summary>
    private const int DialogWidth = 780;

    /// <summary>How tall the window is inside its frame at the normal text size.</summary>
    private const int DialogHeight = 560;

    /// <summary>
    /// One driver to listen to: who they are, and the words the announcer
    /// will actually be handed for them.
    /// </summary>
    /// <param name="Transponder">Their main transponder - the key the rest of the app files them under, and what the check-in sheet is asked about.</param>
    /// <param name="DriverName">Their real name, or empty for a driver who has only ever been a transponder number.</param>
    /// <param name="Spoken">
    /// What the voice is given. Taken from
    /// <see cref="DriverProfile.SpokenName"/> and from nowhere else - see
    /// <see cref="NamesToCheck"/>.
    /// </param>
    /// <param name="Respelled">
    /// Whether somebody has already put something in this driver's
    /// Announcer name box. Not "does the spoken name differ": a club that
    /// types the real name into that box has still had a go at this driver,
    /// and the column exists to show which ones have been looked at.
    /// </param>
    public sealed record NameToCheck(string Transponder, string DriverName, string Spoken, bool Respelled)
    {
        /// <summary>
        /// Who the row is about. A driver with no name at all is shown by
        /// their transponder in brackets, so a row never reads as a blank -
        /// an unnamed profile is one of the things this window is for
        /// finding, because the announcer reads the number out.
        /// </summary>
        public string Who => DriverName.Length > 0 ? DriverName : $"({Transponder})";
    }

    /// <summary>
    /// The drivers to listen to, in the order they were handed over, each
    /// with the name the announcer will read for them.
    ///
    /// The name comes from <see cref="DriverProfile.SpokenName"/> and from
    /// nowhere else. That property is what the voice queue reads when it
    /// fills {driver} (AnnouncementService.VoiceQueue.cs), so what this
    /// window plays is the same string the race calls play. A second copy
    /// of that announcer-name-then-driver-name-then-transponder ladder
    /// written out here would be a check that agrees with itself and
    /// disagrees with the PA, which is worse than no check at all.
    ///
    /// Nulls are skipped - ProfileStore.Get answers null for a transponder
    /// that has gone since the roster was drawn - as is anyone with nothing
    /// to read at all, which is a half-made profile with no name and no
    /// transponder. A driver listed twice is listened to once: a search can
    /// match two of the same driver's transponders, and hearing the same
    /// name twice while walking the list reads as the button misfiring.
    /// </summary>
    public static IReadOnlyList<NameToCheck> NamesToCheck(IEnumerable<DriverProfile?>? drivers)
    {
        var rows = new List<NameToCheck>();

        if (drivers is null)
        {
            return rows;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var driver in drivers)
        {
            if (driver is null)
            {
                continue;
            }

            var spoken = driver.SpokenName?.Trim() ?? string.Empty;

            if (spoken.Length == 0)
            {
                continue;
            }

            var transponder = driver.Transponder?.Trim() ?? string.Empty;

            if (transponder.Length > 0 && !seen.Add(transponder))
            {
                continue;
            }

            rows.Add(new NameToCheck(
                transponder,
                driver.DriverName?.Trim() ?? string.Empty,
                spoken,
                !string.IsNullOrWhiteSpace(driver.AnnouncerName)));
        }

        return rows;
    }

    private readonly DataGridView _grid = new();

    /// <summary>Everyone handed over, in the order they were handed over.</summary>
    private readonly IReadOnlyList<NameToCheck> _all;

    /// <summary>The ones checked in for the race day that is open - empty without a check-in sheet to ask.</summary>
    private readonly IReadOnlyList<NameToCheck> _tonight;

    /// <summary>
    /// Speaks a line in the club's own announcer voice - see
    /// AnnouncementService.SpeakTest. Null when the window was opened
    /// without one, and then it shows the names without reading them.
    /// </summary>
    private readonly Action<string>? _speak;

    /// <summary>Whether there is a check-in sheet with anybody on it, which is what the tick box needs to mean anything.</summary>
    private readonly bool _canNarrowToTonight;

    private readonly CheckBox _chkTonight = new ThemedCheckBox();
    private readonly Label _lblCount = new() { AutoSize = true };

    /// <summary>
    /// What is being read, word for word. A SteadyLabel measured against
    /// every name it can show, so the longest one does not push the buttons
    /// under it down a line the first time a name is read.
    /// </summary>
    private readonly SteadyLabel _lblSaying = new();

    private readonly Button _btnSay = new() { Text = "Say It" };
    private readonly Button _btnNext = new() { Text = "Say the Next One" };

    /// <param name="drivers">
    /// Whose names to check, in the order they should be walked. Nulls are
    /// allowed, so a caller can hand over the answers of a store lookup
    /// without sifting them first.
    /// </param>
    /// <param name="speakName">
    /// How a name is read out: wire it to AnnouncementService.SpeakTest, so
    /// the voice, the engine and the speed are the club's own rather than
    /// this window's idea of them. Null shows the names and reads none.
    /// </param>
    /// <param name="checkIns">
    /// Tonight's check-in sheet, which is the only thing that knows whose
    /// names are actually going to be read. Null leaves the tick box off
    /// and checks everyone given.
    /// </param>
    public NameCheckForm(IEnumerable<DriverProfile?>? drivers, Action<string>? speakName = null, CheckInStore? checkIns = null)
    {
        Text = "Check How Names Are Read";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        _speak = speakName;
        _all = NamesToCheck(drivers);

        // The main transponder only, as every other "is this driver racing
        // tonight" test in the app asks it (RaceProgramControl's roster,
        // Who's Racing, the relay) - a driver is checked in, not one of
        // their transponders.
        _tonight = checkIns is null
            ? new List<NameToCheck>()
            : _all.Where(row => row.Transponder.Length > 0 && checkIns.IsCheckedIn(row.Transponder)).ToList();

        _canNarrowToTonight = _tonight.Count > 0;

        var lblIntro = new Label
        {
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
            Text = _speak is null
                ? "The names the announcer will read tonight, word for word. The voice isn't available from this "
                  + "window, so they can't be heard here."
                : "Press Say It for the picked driver, or Say the Next One to work down the list - exactly as the "
                  + "announcer will say it tonight. To fix one, close this and type how the name sounds in that "
                  + "driver's Announcer name box.",
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
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 100 },
            new DataGridViewTextBoxColumn { Name = "spoken", HeaderText = "What the announcer says", FillWeight = 100 },
            new DataGridViewTextBoxColumn { Name = "set", HeaderText = "Spelling set", FillWeight = 40 });

        // Reading a name is the obvious thing to do to a row, so the obvious
        // gesture does it.
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                SayRow(e.RowIndex);
            }
        };

        // An empty list says what fills it, painted where the rows would be
        // rather than a control of its own, so nothing moves when it fills.
        _grid.Paint += (_, e) =>
        {
            if (_grid.Rows.Count > 0)
            {
                return;
            }

            var room = Rectangle.FromLTRB(
                UiLayout.RowGap,
                _grid.ColumnHeadersHeight + UiLayout.SectionGap,
                Math.Max(UiLayout.RowGap + 1, _grid.ClientSize.Width - UiLayout.RowGap),
                Math.Max(_grid.ColumnHeadersHeight + UiLayout.SectionGap + 1, _grid.ClientSize.Height - UiLayout.RowGap));

            TextRenderer.DrawText(e.Graphics, EmptyList, AppTheme.BodyFont, room, AppTheme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak);
        };

        _chkTonight.Text = "Only the drivers checked in tonight";
        _chkTonight.AutoSize = true;
        _chkTonight.Font = AppTheme.BodyFont;

        // Ticked to start with whenever there is a check-in sheet to narrow
        // by: the question this window answers is "what will be read out
        // tonight", and a club with two hundred saved drivers is not going
        // to listen to two hundred names. Unticking it gives the whole list,
        // which is what a quiet afternoon's tidying wants.
        _chkTonight.Checked = _canNarrowToTonight;
        _chkTonight.Visible = _canNarrowToTonight;
        _chkTonight.CheckedChanged += (_, _) => Fill();

        _lblCount.ForeColor = AppTheme.TextMuted;
        _lblCount.Font = AppTheme.SmallFont;

        _lblSaying.ForeColor = AppTheme.TextDark;
        _lblSaying.Font = AppTheme.BoldFont;

        // Everything it can come to, so its height is settled before a name
        // is ever read - a line that grows from one to two the first time
        // the button is pressed moves the buttons out from under the mouse.
        _lblSaying.Wordings = new[] { Idle }
            .Concat(_all.Select(Saying))
            .ToList();

        _lblSaying.Text = Idle;

        _btnSay.Click += (_, _) => SayThisOne();
        _btnNext.Click += (_, _) => SayTheNextOne();

        var btnClose = new Button { Text = "Close", DialogResult = DialogResult.OK };

        // The tick box and the count level with the middle of the buttons:
        // in a row of their own, anchored to neither its top nor its bottom,
        // each is centred up and down on it - as RacerPinsForm's footer is.
        _chkTonight.Anchor = AnchorStyles.Left;
        _lblCount.Anchor = AnchorStyles.Left;

        // Close furthest right, as a dialog's buttons go, with the two that
        // do the work to its left. Tab still reaches Say It first: the
        // right-hand bar takes its buttons right to left, in the order they
        // were added.
        var footer = new SplitBar(
            new ButtonBar().With(_chkTonight, _lblCount),
            new ButtonBar(alignRight: true).With(btnClose, _btnNext, _btnSay));

        _btnSay.TabIndex = 0;
        _btnNext.TabIndex = 1;
        btnClose.TabIndex = 2;

        // No row of tiles over it: at 1280x720 the window is as tall as the
        // screen lets it be, and tiles there came out of the list - two
        // drivers fewer on show (layout pictures, 18 Nov 2026). The count
        // in the footer says what they would.
        //
        // The names in one card: what to do as its hint, the list taking the
        // height, and what is being read under it.
        var card = new Section("Names to hear");
        card.Add(lblIntro, gapBelow: UiLayout.RowGap);
        card.AddFill(_grid, () => _grid.ColumnHeadersHeight + LeastRows * _grid.RowTemplate.Height + 2, gapBelow: UiLayout.RowGap);
        card.Add(_lblSaying, gapBelow: 0);

        var page = new FillPage();
        page.AddFill(card, card.LeastHeight, gapBelow: UiLayout.RowGap);
        page.Add(footer, gapBelow: 0);

        Controls.Add(page);

        // Both Close, as a read-only window's are: the list takes Enter for
        // its own moving about, and a dialog's Enter is expected to shut it.
        AcceptButton = btnClose;
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);

        // After the theme pass: the button the window is for in the app's
        // colour - pressed over and over to work down the roster.
        AppTheme.StyleButton(_btnNext, AppTheme.ButtonKind.Primary);

        // Every heading whole, the rest shared by weight - see Pieces.KeepHeadingsWhole.
        Pieces.KeepHeadingsWhole(_grid);

        Fill();

        // Wider than the 720 it had at the normal text size, so the count
        // and the three buttons share a line and the card's caption costs
        // the list nothing; the height it had. It can be made bigger: a long
        // respelling beside a long name wants the width.
        DialogFit.Keep(this, page, width: DialogWidth, height: DialogHeight);
    }

    /// <summary>The line under the list while a name is being read.</summary>
    private static string Saying(NameToCheck row) => $"Saying: {row.Spoken}";

    /// <summary>
    /// Opens it, owned, so it centres on the page that asked - as
    /// RacingWordsForm.ShowFor does.
    /// </summary>
    public static void ShowFor(IWin32Window? owner, IEnumerable<DriverProfile?>? drivers, Action<string>? speakName = null, CheckInStore? checkIns = null)
    {
        using var form = new NameCheckForm(drivers, speakName, checkIns);
        form.ShowDialog(owner);
    }

    /// <summary>
    /// The rows on show: tonight's when the tick box asks for them, otherwise
    /// everyone given.
    ///
    /// Not called Shown - a Form already has a Shown event, and a method of
    /// that name hides it (CS0108) for no gain.
    /// </summary>
    private IReadOnlyList<NameToCheck> RowsOnShow() =>
        _canNarrowToTonight && _chkTonight.Checked ? _tonight : _all;

    /// <summary>
    /// Fills the list, and says how many there are and how many have had a
    /// spelling set - the number a race director wants is "three of forty
    /// have been looked at", not the list's length.
    /// </summary>
    private void Fill()
    {
        var rows = RowsOnShow();

        _grid.Rows.Clear();

        foreach (var row in rows)
        {
            // The row carries what it is about: a click on a column heading
            // re-sorts the grid, so a row's index is not its place in the
            // list it came from (the bug RacerPinsForm's Tag exists for).
            var index = _grid.Rows.Add(row.Who, row.Spoken, row.Respelled ? "Yes" : string.Empty);
            _grid.Rows[index].Tag = row;
        }

        var respelled = rows.Count(row => row.Respelled);

        _lblCount.Text = rows.Count == 0
            ? "Nobody to check."
            : $"{Plural.Of(rows.Count, "driver")} - {respelled} with a spelling set.";

        var canSay = _speak is not null && rows.Count > 0;
        _btnSay.Enabled = canSay;
        _btnNext.Enabled = canSay;

        // A name read from the old list is no longer on screen, so the line
        // saying it was read would be about a row nobody can see.
        _lblSaying.Text = Idle;

        // The first driver highlighted from the off, so Say It always has
        // somebody to read and Say the Next One always has somewhere to go
        // from. A list that opens with nothing picked makes the first press
        // of either button do nothing, which reads as the window being
        // broken rather than as a selection being wanted.
        if (_grid.Rows.Count > 0)
        {
            Highlight(0);
        }
    }

    /// <summary>The row the list is on, or -1 when it is on none.</summary>
    private int Picked() => _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Index : -1;

    /// <summary>
    /// Puts the list on a row, so it shows which name spoke.
    ///
    /// The current cell is only set once the grid really has a window: a
    /// DataGridView refuses one before it is created, and this window is
    /// built and measured without ever being shown (the layout sweep), where
    /// that would throw from the constructor. The selection, which is what
    /// the eye and <see cref="Picked"/> follow, needs no window.
    /// </summary>
    private void Highlight(int index)
    {
        if (index < 0 || index >= _grid.Rows.Count)
        {
            return;
        }

        _grid.ClearSelection();
        _grid.Rows[index].Selected = true;

        if (_grid.IsHandleCreated && _grid.Columns.Count > 0)
        {
            _grid.CurrentCell = _grid.Rows[index].Cells[0];
        }
    }

    /// <summary>Reads the row at <paramref name="index"/>, and puts the list on it.</summary>
    private void SayRow(int index)
    {
        if (index < 0 || index >= _grid.Rows.Count || _grid.Rows[index].Tag is not NameToCheck row)
        {
            return;
        }

        Highlight(index);

        // Shown before it is spoken, not after: synthesis is a network round
        // trip, which on a club's wifi is a second or two, and a button that
        // looks dead for two seconds gets pressed again.
        _lblSaying.Text = Saying(row);

        _speak?.Invoke(row.Spoken);
    }

    /// <summary>
    /// Reads the row the list is on - what Say It does.
    ///
    /// Internal, with <see cref="SayTheNextOne"/>, so the tests can work
    /// down the list without a window on screen: a button only clicks while
    /// its window is visible, and a test that has to show windows to press
    /// buttons is a test that fails on a build machine with no desktop.
    /// </summary>
    internal void SayThisOne() => SayRow(Picked());

    /// <summary>
    /// Reads the row after the one the list is on, back round to the top at
    /// the end - what Say the Next One does, pressed over and over to work
    /// down a roster.
    ///
    /// By row index rather than by place in the list it came from, on
    /// purpose: it follows the order the list is sorted in, so a director
    /// who has clicked "Spelling set" to bring the respelt ones together
    /// walks those.
    /// </summary>
    internal void SayTheNextOne()
    {
        if (_grid.Rows.Count == 0)
        {
            return;
        }

        var next = Picked() + 1;

        if (next < 0 || next >= _grid.Rows.Count)
        {
            next = 0;
        }

        SayRow(next);
    }
}
