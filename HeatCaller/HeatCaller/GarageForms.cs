namespace HeatCaller;

/// <summary>
/// One car: what it is, and whose it is.
///
/// The owner picker carries "The club" as a real choice rather than
/// leaving it as an empty selection, because a club car is a deliberate
/// thing and not a car somebody forgot to assign - see Car.OwnerTransponder.
///
/// On the layout pieces (DialogFit): the name with its hint under it, the
/// owner, the class, the chassis, the shell, the race number and the notes
/// lined up in a field grid, and Save and Cancel at the foot. The fields are
/// the window's body, which scrolls on a screen too short for all of them
/// while Save and Cancel stay in view. As wide as it was made at the normal
/// text size, at the size in use - wider when a drop-down's longest choice
/// needs it. It used to be placed in pixels in a window 540px wide: the
/// captions in a column 180px wide, every field 320px beside it however long
/// the longest driver, class or chassis name, and the notes three lines of
/// the font's height rather than of the lines the box draws.
/// </summary>
internal sealed class CarForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size.</summary>
    private const int DialogWidth = 540;

    /// <summary>The fields' width at the normal text size - wider when a drop-down's longest choice needs it.</summary>
    private const int FieldWidth = 320;

    /// <summary>How many lines of notes show before the box scrolls.</summary>
    private const int NotesLines = 3;

    private readonly Car _car;

    private readonly TextBox _txtName = new();
    private readonly ComboBox _cmbOwner = new();
    private readonly ComboBox _cmbClass = new();
    private readonly ComboBox _cmbChassis = new();
    private readonly TextBox _txtShell = new();
    private readonly TextBox _txtNumber = new();
    private readonly TextBox _txtNotes = new();

    /// <summary>The car as edited. The instance handed in, written back into - the caller kept its own copy for the undo.</summary>
    public Car Result => _car;

    private sealed record OwnerChoice(string Transponder, string Caption)
    {
        public override string ToString() => Caption;
    }

    private sealed record ChassisChoice(string Id, string Caption)
    {
        public override string ToString() => Caption;
    }

    public CarForm(Car car, ChassisTypeStore chassisTypes, ProfileStore profiles, RaceClassStore raceClasses, bool isNew)
    {
        _car = car;

        Text = isNew ? "Add a Car" : "Edit Car";
        Font = AppTheme.BodyFont;

        _txtName.Text = car.Name;
        _txtName.MaxLength = 60;

        _cmbOwner.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbOwner.Items.Add(new OwnerChoice(string.Empty, "The club"));

        foreach (var profile in profiles.All.OrderBy(p => p.DriverName, StringComparer.CurrentCultureIgnoreCase))
        {
            var name = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName;
            _cmbOwner.Items.Add(new OwnerChoice(profile.Transponder, name));
        }

        var ownerChoice = _cmbOwner.Items
            .Cast<OwnerChoice>()
            .FirstOrDefault(o => string.Equals(o.Transponder, car.OwnerTransponder, StringComparison.OrdinalIgnoreCase));

        // Owned under one of a driver's other transponders, or by a driver
        // no longer on file: shown as who it is, never as the club.
        if (ownerChoice is null && !string.IsNullOrWhiteSpace(car.OwnerTransponder))
        {
            var holder = profiles.Get(car.OwnerTransponder);
            ownerChoice = new OwnerChoice(
                car.OwnerTransponder,
                holder is null ? car.OwnerTransponder : string.IsNullOrWhiteSpace(holder.DriverName) ? holder.Transponder : holder.DriverName);
            _cmbOwner.Items.Add(ownerChoice);
        }

        _cmbOwner.SelectedItem = ownerChoice ?? _cmbOwner.Items[0];

        // Editable rather than a fixed list: a club can put a car in a
        // class it has not set up yet, and the Garage groups on the name
        // either way. Refusing here would mean the class had to be created
        // before the car could be described, which is the wrong order.
        _cmbClass.DropDownStyle = ComboBoxStyle.DropDown;
        _cmbClass.Items.Add(string.Empty);

        foreach (var raceClass in raceClasses.All)
        {
            _cmbClass.Items.Add(raceClass.Name);
        }

        _cmbClass.Text = car.ClassName;

        _cmbChassis.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbChassis.Items.Add(new ChassisChoice(string.Empty, "(not said)"));

        foreach (var type in chassisTypes.InUse)
        {
            _cmbChassis.Items.Add(new ChassisChoice(type.Id, type.DisplayName));
        }

        // A chassis that has since been retired is still this car's, so it
        // is added back rather than silently becoming "(not said)".
        if (!string.IsNullOrWhiteSpace(car.ChassisTypeId)
            && chassisTypes.Get(car.ChassisTypeId) is { Retired: true } retired)
        {
            _cmbChassis.Items.Add(new ChassisChoice(retired.Id, retired.DisplayName + "  (retired)"));
        }

        _cmbChassis.SelectedItem = _cmbChassis.Items
            .Cast<ChassisChoice>()
            .FirstOrDefault(c => string.Equals(c.Id, car.ChassisTypeId, StringComparison.Ordinal))
            ?? _cmbChassis.Items[0];

        _txtShell.Text = car.Bodyshell;
        _txtShell.MaxLength = 60;
        _txtShell.PlaceholderText = "e.g. Audi R8";

        _txtNumber.Text = car.RaceNumber;
        _txtNumber.MaxLength = 8;

        _txtNotes.Text = car.Notes;
        _txtNotes.Multiline = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;

        var btnOk = new Button { Text = "Save", DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // Every field runs to the right-hand edge, where the buttons end, as
        // the 320px fields did beside their captions - all but the race
        // number, a box sized for what goes in it. The hint under the name,
        // wrapping to the fields' width.
        var fields = new FieldGrid();
        var captions = new List<Label>
        {
            fields.AddRow("Name:", _txtName, stretch: true),
        };

        fields.AddHint("What people call it - \"the red one\", \"Dave's Audi\". Left blank, the shell is used instead.");

        captions.Add(fields.AddRow("Owner:", _cmbOwner, stretch: true));

        if (!isNew)
        {
            _cmbOwner.Enabled = false;
            fields.AddHint("To sell it or give it away, close this and click Transfer.");
        }
        captions.Add(fields.AddRow("Class:", _cmbClass, stretch: true));
        captions.Add(fields.AddRow("Chassis:", _cmbChassis, stretch: true));
        captions.Add(fields.AddRow("Shell:", _txtShell, stretch: true));
        captions.Add(fields.AddRow("Race number:", _txtNumber));
        captions.Add(fields.AddRow("Notes:", _txtNotes, stretch: true));

        // The fields are the body: on a screen too short for all seven -
        // 1280x720 at a larger text size - they scroll, and Save and Cancel
        // under them stay in view.
        var page = new FillPage();
        var body = DialogFit.Body(page);
        body.Add(fields, gapBelow: 0);

        // Against the right-hand edge, Cancel furthest right, as they were -
        // and Tab still reaches Save first: the bar takes them right to
        // left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnOk), gapBelow: 0);
        btnOk.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        // Read back here rather than in a Click handler, so closing with
        // Enter and closing with the button do exactly the same thing.
        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_txtName.Text) && string.IsNullOrWhiteSpace(_txtShell.Text))
            {
                AppDialog.Show(this,
                    "Give the car a name or a shell, so it can be told apart from the others in the list.",
                    "It needs something to call it", MessageBoxButtons.OK, MessageBoxIcon.Information);

                e.Cancel = true;
                return;
            }

            _car.Name = _txtName.Text.Trim();
            // Only a new car's owner is picked here. A car already in the
            // garage changes hands through Transfer, which takes its
            // transponders and the drivers' car names with it.
            if (isNew)
            {
                _car.OwnerTransponder = (_cmbOwner.SelectedItem as OwnerChoice)?.Transponder ?? string.Empty;
            }

            _car.ClassName = _cmbClass.Text.Trim();
            _car.ChassisTypeId = (_cmbChassis.SelectedItem as ChassisChoice)?.Id ?? string.Empty;
            _car.Bodyshell = _txtShell.Text.Trim();
            _car.RaceNumber = _txtNumber.Text.Trim();
            _car.Notes = _txtNotes.Text.Trim();

            // A car that is no longer anybody's cannot be out on loan to
            // somebody. Cleared here rather than left to puzzle over later.
            if (_car.IsClubCar && string.IsNullOrWhiteSpace(_car.LoanedToTransponder))
            {
                _car.LoanedForEventId = string.Empty;
            }
        };

        AppTheme.ApplyRecursively(this);

        // After the theme pass, in the faces it leaves them in: the race
        // number as wide as it was at the normal text size, at the size in
        // use, and the notes a few lines of their font, as the box draws
        // them (Pieces.LineHeight).
        _txtNumber.Width = UiLayout.AtTextSize(UiLayout.NumericWidth);
        _txtNotes.Height = NotesLines * Pieces.LineHeight(_txtNotes.Font) + UiLayout.HintGap * 2;

        // As wide as it was at the normal text size, at the size in use - or
        // wider when the fields' column needs more: never narrower than the
        // 320px the fields had, and wide enough for each drop-down's longest
        // choice, the class's typed name included - a class not set up yet
        // is not in its list. At 320px a long class's name came out cut
        // short once it was picked. With room for the body's scrollbar,
        // which takes its width from the fields on a screen too short for
        // them. The caption column beside it as the grid works it out: its
        // widest caption and the gap after it.
        (Font Font, int Count, int Width)? ownerWidth = null;
        (Font Font, int Count, int Width)? classWidth = null;
        (Font Font, int Count, int Width)? chassisWidth = null;

        var fieldColumn = new[]
        {
            UiLayout.AtTextSize(FieldWidth),
            Pieces.PickerWidth(_cmbOwner, ref ownerWidth),
            Pieces.PickerWidth(_cmbClass, ref classWidth, _cmbClass.Text),
            Pieces.PickerWidth(_cmbChassis, ref chassisWidth),
        }.Max();

        var captionColumn = captions.Max(caption => caption.GetPreferredSize(Size.Empty).Width + caption.Margin.Horizontal);

        var width = Math.Max(
            UiLayout.AtTextSize(DialogWidth),
            UiLayout.PageLeft + captionColumn + fieldColumn + SystemInformation.VerticalScrollBarWidth + UiLayout.PageRight);

        DialogFit.Keep(this, page, width, body: body, widthMeasured: true);
    }
}

/// <summary>
/// A car's photographs: add, remove, and say which one leads.
///
/// Pictures are copied into the app's own folder at import (see
/// PhotoStore) rather than referenced where they sit, so a car keeps its
/// photos the day somebody tidies their Downloads folder.
///
/// Removing one here takes it off the CAR but leaves the file alone. That
/// is deliberate: the caller captures an undo entry that puts the names
/// back, and a name whose file had been deleted would put back a broken
/// picture. A few kilobytes is the cheaper mistake.
///
/// On the layout pieces (DialogFit): what the window is for across the top;
/// the list of photos down the left at its own width, beside the preview,
/// which takes the width left and the height; and under them the buttons
/// that change the photos on the left, Done and Cancel against the
/// right-hand edge, all on one line - the window made wide enough for it at
/// the text size in use. It opens at the size it had and can be made
/// bigger, the list and the preview taking the room. It used to be placed in
/// pixels in a window 652px wide: the list and the preview each a fixed
/// 260px tall, and the five buttons in one row that did not fit it - Done
/// was placed over Make it First.
/// </summary>
internal sealed class CarPhotosForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size, at the least - wider when its buttons need more to stay on one line.</summary>
    private const int DialogWidth = 652;

    /// <summary>How tall it opens inside its frame at the normal text size - the height it had.</summary>
    private const int DialogHeight = 370;

    /// <summary>The list's width at the normal text size.</summary>
    private const int ListWidth = 220;

    /// <summary>The list shows at least this many photos before the window scrolls instead.</summary>
    private const int LeastRows = 4;

    /// <summary>The preview's least height at the normal text size.</summary>
    private const int PreviewLeastHeight = 160;

    private readonly Car _car;
    private readonly ListBox _list = new();
    private readonly PictureBox _preview = new();
    private readonly Button _btnRemove = new();
    private readonly Button _btnFirst = new();

    private Image? _shown;

    public Car Result => _car;

    public CarPhotosForm(Car car)
    {
        _car = car;

        Text = $"Photos - {car.DisplayName}";
        Font = AppTheme.BodyFont;

        var lblIntro = new Label
        {
            Text = "Pictures are copied into the app, so they stay with the car. The first one is its main picture.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        // Given the height the window has, not snapped to whole rows - see FillPage.AddFill.
        _list.IntegralHeight = false;
        _list.SelectedIndexChanged += (_, _) => ShowSelected();

        _preview.SizeMode = PictureBoxSizeMode.Zoom;
        _preview.BackColor = AppTheme.Background;

        // With no photos, the room the photo would take says so (it was a
        // blank list beside a blank space - layout pictures, 30 Sep).
        _preview.Paint += (_, e) =>
        {
            if (_list.Items.Count > 0)
            {
                return;
            }

            var margin = UiLayout.AtTextSize(16);
            var room = Rectangle.Inflate(_preview.ClientRectangle, -margin, -margin);

            TextRenderer.DrawText(e.Graphics, "No photos of this car yet. Add a Photo picks one from this PC.", AppTheme.BodyFont, room,
                AppTheme.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        };

        var btnAdd = new Button { Text = "Add a Photo..." };
        btnAdd.Click += (_, _) => AddPhoto();

        _btnRemove.Text = "Remove";
        _btnRemove.Click += (_, _) => RemoveSelected();

        _btnFirst.Text = "Make it First";
        _btnFirst.Click += (_, _) => MakeFirst();

        var btnDone = new Button { Text = "Done", DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // The list down the left at the width it had, at the text size in
        // use, and the preview beside it taking the rest - both the height
        // the window leaves them.
        var photos = new Beside(UiLayout.AtTextSize(ListWidth)) { SideOnLeft = true };
        photos.AddFill(_preview, _ => UiLayout.AtTextSize(PreviewLeastHeight));
        photos.AddSideFill(_list, _ => LeastRows * _list.ItemHeight + 4, gapBelow: 0);

        // What changes the photos on the left, Done and Cancel against the
        // right-hand edge, as they were - and Tab still reaches Done before
        // Cancel: the right-hand bar takes them right to left, in the order
        // they were added.
        var buttons = new SplitBar(
            new ButtonBar().With(btnAdd, _btnRemove, _btnFirst),
            new ButtonBar(alignRight: true).With(btnCancel, btnDone));

        btnDone.TabIndex = 0;
        btnCancel.TabIndex = 1;

        var page = new FillPage();
        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.AddFill(photos, photos.LeastHeight, gapBelow: UiLayout.RowGap);
        page.Add(buttons, gapBelow: 0);

        Controls.Add(page);

        AcceptButton = btnDone;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        // After the theme pass - the preview stays neutral whatever the app
        // is wearing, so a photo is judged on its own.
        _preview.BackColor = AppTheme.Background;

        // Done keeps what was done here, and is this window's own button: in
        // the app's colour, as Save and OK are in every other window. The
        // theme goes by the caption, and "Done" is not one it knows.
        AppTheme.StyleButton(btnDone, AppTheme.ButtonKind.Primary);

        Reload();

        // As wide as it was at the normal text size, at the size in use - or
        // wider when the five buttons need more to stay on one line, as they
        // do at every text size: about 814px of them at the normal one.
        // Measured as the row measures itself on one line, in the faces the
        // theme pass left the buttons in.
        var width = Math.Max(
            UiLayout.AtTextSize(DialogWidth),
            UiLayout.PageLeft + buttons.GetPreferredSize(Size.Empty).Width + UiLayout.PageRight);

        DialogFit.Keep(this, page, width, height: DialogHeight, widthMeasured: true);
    }

    private void Reload()
    {
        var selected = _list.SelectedIndex;

        _list.BeginUpdate();
        _list.Items.Clear();

        for (var i = 0; i < _car.PhotoFileNames.Count; i++)
        {
            _list.Items.Add(i == 0 ? "Photo 1  (shown first)" : $"Photo {i + 1}");
        }

        _list.EndUpdate();

        if (_list.Items.Count > 0)
        {
            _list.SelectedIndex = Math.Clamp(selected, 0, _list.Items.Count - 1);
        }
        else
        {
            ShowSelected();
        }

        // The note in the preview comes and goes with the photos.
        _preview.Invalidate();

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var have = _list.SelectedIndex >= 0;

        _btnRemove.Enabled = have;
        _btnFirst.Enabled = have && _list.SelectedIndex > 0;
    }

    private void ShowSelected()
    {
        // Disposed before the next one is loaded: these are full-size
        // images rather than cached thumbnails, and a dialog left open
        // while somebody clicks down a list would otherwise hold every
        // one of them.
        _shown?.Dispose();
        _shown = null;

        var index = _list.SelectedIndex;

        if (index >= 0 && index < _car.PhotoFileNames.Count)
        {
            _shown = PhotoStore.Cars.Full(_car.PhotoFileNames[index]);
        }

        _preview.Image = _shown;
        UpdateButtons();
    }

    private void AddPhoto()
    {
        using var open = new OpenFileDialog
        {
            Title = "Choose a photo of the car",
            Filter = PhotoStore.PickerFilter,
            CheckFileExists = true,
        };

        if (open.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var stored = PhotoStore.Cars.Import(open.FileName, "use as a car photo", out var error);

        if (stored is null)
        {
            AppDialog.Show(this,
                error ?? "That picture could not be read.",
                "Could not add the photo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _car.PhotoFileNames.Add(stored);
        Reload();
        _list.SelectedIndex = _car.PhotoFileNames.Count - 1;
    }

    private void RemoveSelected()
    {
        var index = _list.SelectedIndex;

        if (index < 0 || index >= _car.PhotoFileNames.Count)
        {
            return;
        }

        // The file is left where it is - see the class note.
        _car.PhotoFileNames.RemoveAt(index);
        Reload();
    }

    private void MakeFirst()
    {
        var index = _list.SelectedIndex;

        if (index <= 0 || index >= _car.PhotoFileNames.Count)
        {
            return;
        }

        var name = _car.PhotoFileNames[index];
        _car.PhotoFileNames.RemoveAt(index);
        _car.PhotoFileNames.Insert(0, name);

        Reload();
        _list.SelectedIndex = 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _preview.Image = null;
            _shown?.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Lending a car for one race day.
///
/// One night, never open-ended: the loan carries the race day it is for,
/// so it ends when the night does and there is no state anybody has to
/// remember to clear. See Car.DriverFor.
///
/// On the layout pieces (DialogFit): whose car it is and what lending it
/// means across the top, who is taking it under that, and Save and Cancel at
/// the foot; as tall as that, and wider than it was made when a driver's name
/// needs the drop-down wider. It used to be placed in pixels in a window
/// 452px wide: the caption in a column 180px wide and the drop-down the 232px
/// left beside it, too narrow at every text size for "Nobody - it stays with
/// its owner" or a long name once it was picked.
/// </summary>
internal sealed class LendCarForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size.</summary>
    private const int DialogWidth = 452;

    /// <summary>The drop-down's width at the normal text size - wider when a driver's name needs it.</summary>
    private const int FieldWidth = 232;

    private readonly ComboBox _cmbTo = new();

    /// <summary>Who is taking it, or empty to hand it back.</summary>
    public string ToTransponder { get; private set; } = string.Empty;

    private sealed record DriverChoice(string Transponder, string Caption)
    {
        public override string ToString() => Caption;
    }

    public LendCarForm(Car car, ProfileStore profiles, string dayName, string ownerName, string? eventId = null)
    {
        Text = "Lend a Car";
        Font = AppTheme.BodyFont;

        var owner = car.IsClubCar
            ? "This is a club car."
            : $"This is {(string.IsNullOrWhiteSpace(ownerName) ? "someone's" : ownerName + "'s")} car.";

        var lblIntro = new Label
        {
            Text = $"{owner} Lending it applies to \"{dayName}\" only - it goes back to its owner when the day ends.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        _cmbTo.DropDownStyle = ComboBoxStyle.DropDownList;

        _cmbTo.Items.Add(new DriverChoice(string.Empty, "Nobody - it stays with its owner"));

        // AllRaceable, not All: a car is a thing that physically exists and
        // a ghost cannot be lent one. ProfileStore says so itself.
        foreach (var profile in profiles.All
                     .Where(p => !string.Equals(p.Transponder, car.OwnerTransponder, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(p => p.DriverName, StringComparer.CurrentCultureIgnoreCase))
        {
            var name = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName;
            _cmbTo.Items.Add(new DriverChoice(profile.Transponder, name));
        }

        // WHY (20 Oct 2026): this used to pick whoever the car was last lent
        // to, on any day. Loans are never cleared - a loan simply stops
        // counting once its race day is not the open one (Car.DriverFor) - so
        // the dialog opened on last week's borrower and Save lent the car to
        // them again tonight. Now a loan only counts if it is for the day being
        // lent for; otherwise it starts on "Nobody".
        var lentTo = !string.IsNullOrWhiteSpace(eventId)
                     && string.Equals(car.LoanedForEventId, eventId, StringComparison.Ordinal)
            ? car.LoanedToTransponder
            : string.Empty;

        _cmbTo.SelectedItem = _cmbTo.Items
            .Cast<DriverChoice>()
            .FirstOrDefault(d => string.Equals(d.Transponder, lentTo, StringComparison.OrdinalIgnoreCase))
            ?? _cmbTo.Items[0];

        var btnOk = new Button { Text = "Save", DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // The drop-down runs to the right-hand edge, where the buttons end,
        // as it did beside its caption.
        var fields = new FieldGrid();
        var lblTo = fields.AddRow("Lend it to:", _cmbTo, stretch: true);

        var page = new FillPage();
        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.Add(fields);

        // Against the right-hand edge, Cancel furthest right, as they were -
        // and Tab still reaches Save first: the bar takes them right to
        // left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnOk), gapBelow: 0);
        btnOk.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        FormClosing += (_, _) =>
        {
            if (DialogResult == DialogResult.OK)
            {
                ToTransponder = (_cmbTo.SelectedItem as DriverChoice)?.Transponder ?? string.Empty;
            }
        };

        AppTheme.ApplyRecursively(this);

        // As wide as it was at the normal text size, at the size in use - or
        // wider when the drop-down needs more for its longest choice, in the
        // face the theme pass left it in. The caption column beside it as
        // the grid works it out: the caption and the gap after it.
        (Font Font, int Count, int Width)? toWidth = null;

        var fieldColumn = Math.Max(UiLayout.AtTextSize(FieldWidth), Pieces.PickerWidth(_cmbTo, ref toWidth));
        var captionColumn = lblTo.GetPreferredSize(Size.Empty).Width + lblTo.Margin.Horizontal;

        var width = Math.Max(
            UiLayout.AtTextSize(DialogWidth),
            UiLayout.PageLeft + captionColumn + fieldColumn + UiLayout.PageRight);

        DialogFit.Keep(this, page, width, widthMeasured: true);
    }
}

/// <summary>
/// The club's chassis list.
///
/// Small on purpose. A chassis is a name, a maker and - optionally - the
/// setup sheet a car on it should reach for, which is the one place the
/// physical car and the setup-sheet schema meet. Retiring rather than
/// deleting is offered first, because a chassis nobody runs any more is
/// still what last season's cars were.
///
/// On the layout pieces (DialogFit): what the list is across the top; the
/// chassis taking the height and the width the fields leave, and down the
/// right the fields for the one selected, each under its caption, with the
/// hint under the setup sheet; and under them the buttons that change the
/// list on the left, Close against the right-hand edge, all on one line -
/// the window made wide enough for it at the text size in use. It opens at
/// the height it had and can be made bigger, the list taking the room. It
/// used to be placed in pixels in a window 632px wide: the list a fixed
/// 240x230, the fields 336px beside it however long a setup sheet's name,
/// and the five buttons in one row that did not fit it - Close was placed
/// over Retire and Delete, and Delete ran past the window's edge.
/// </summary>
internal sealed class ChassisTypesForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size, at the least - wider when its buttons or its fields need more.</summary>
    private const int DialogWidth = 632;

    /// <summary>How tall it opens inside its frame at the normal text size - the height it had.</summary>
    private const int DialogHeight = 356;

    /// <summary>The list's width at the normal text size, at the least - it takes whatever the fields leave.</summary>
    private const int ListWidth = 240;

    /// <summary>The fields' column at the normal text size - wider when a setup sheet's name needs the drop-down wider.</summary>
    private const int FieldsWidth = 336;

    /// <summary>The list shows at least this many chassis before the window scrolls instead.</summary>
    private const int LeastRows = 4;

    private readonly ChassisTypeStore _types;
    private readonly ChassisTemplateStore _sheets;
    private readonly CarStore _cars;

    private readonly ListBox _list = new();
    private readonly TextBox _txtName = new();
    private readonly TextBox _txtMaker = new();
    private readonly ComboBox _cmbSheet = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnRetire = new();
    private readonly Button _btnDelete = new();

    /// <summary>
    /// How wide the fields' column is: the width it had at the normal text
    /// size, at the size in use, or as wide as the setup sheet drop-down's
    /// longest choice if that is more. Worked out once the theme pass has
    /// been and the drop-down is filled; the column asks for it whenever it
    /// is placed.
    /// </summary>
    private int _fieldsWidth;

    private sealed record SheetChoice(string Id, string Caption)
    {
        public override string ToString() => Caption;
    }

    public ChassisTypesForm(ChassisTypeStore types, ChassisTemplateStore sheets, CarStore cars)
    {
        _types = types;
        _sheets = sheets;
        _cars = cars;

        Text = "Chassis Types";
        Font = AppTheme.BodyFont;

        var lblIntro = new Label
        {
            Text = "The chassis a car can be. The list is the club's own to change - the starting ones aren't a rulebook.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        // Given the height the window has, not snapped to whole rows - see FillPage.AddFill.
        _list.IntegralHeight = false;
        _list.SelectedIndexChanged += (_, _) => LoadSelected();

        // With every one deleted, the list says so, and what makes one.
        EmptyNote.For(_list, () => "No chassis types. Add makes one.");

        Label Caption(string text) => new()
        {
            Text = text,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        };

        var lblName = Caption("Name");
        var lblMaker = Caption("Maker");
        var lblSheet = Caption("Setup sheet to use");
        var lblSheetHint = Caption("Logging a setup for a car on this chassis opens on that sheet.");

        _txtName.MaxLength = 40;
        _txtMaker.MaxLength = 40;
        _cmbSheet.DropDownStyle = ComboBoxStyle.DropDownList;

        var btnAdd = new Button { Text = "Add" };
        btnAdd.Click += (_, _) => AddType();

        _btnSave.Text = "Save Changes";
        _btnSave.Click += (_, _) => SaveSelected();

        _btnRetire.Text = "Retire";
        _btnRetire.Click += (_, _) => RetireSelected();

        _btnDelete.Text = "Delete";
        _btnDelete.Click += (_, _) => DeleteSelected();

        var btnClose = new Button { Text = "Close", DialogResult = DialogResult.OK };

        // The chassis taking the height and whatever width the fields leave;
        // the fields down the right, each under its caption as they were,
        // as wide as SizeFields works out - until then, the width they had.
        // A caption and the hint are given their heights there too: a part
        // in the column keeps the height it is given.
        _fieldsWidth = UiLayout.AtTextSize(FieldsWidth);

        var band = new Beside(() => _fieldsWidth);
        band.AddFill(_list, _ => LeastRows * _list.ItemHeight + 4);
        band.AddSide(lblName, gapBelow: UiLayout.HintGap);
        band.AddSide(_txtName);
        band.AddSide(lblMaker, gapBelow: UiLayout.HintGap);
        band.AddSide(_txtMaker);
        band.AddSide(lblSheet, gapBelow: UiLayout.HintGap);
        band.AddSide(_cmbSheet, gapBelow: UiLayout.HintGap);
        band.AddSide(lblSheetHint, gapBelow: 0);

        // What changes the list on the left, Close against the right-hand
        // edge, as they were.
        var buttons = new SplitBar(
            new ButtonBar().With(btnAdd, _btnSave, _btnRetire, _btnDelete),
            new ButtonBar(alignRight: true).With(btnClose));

        var page = new FillPage();
        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.AddFill(band, band.LeastHeight, gapBelow: UiLayout.RowGap);
        page.Add(buttons, gapBelow: 0);

        Controls.Add(page);

        // Enter saves: Add leaves the cursor in the Name box, and Enter after
        // typing the name used to press Close - the name was lost and a
        // chassis called "New chassis" was left behind. Esc still closes.
        AcceptButton = _btnSave;
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);

        // Filled before the fields are sized: the setup sheet drop-down is
        // filled here, and its longest choice can widen the column.
        Reload();

        SizeFields(new[] { lblName, lblMaker, lblSheet, lblSheetHint });

        // As wide as it was at the normal text size, at the size in use - or
        // wider when the five buttons need more to stay on one line, as they
        // do at every text size (about 814px of them at the normal one), or
        // the list and the fields beside it need more than that. Measured
        // as the row measures itself on one line, in the faces the theme
        // pass left the buttons in.
        var width = new[]
        {
            UiLayout.AtTextSize(DialogWidth),
            UiLayout.PageLeft + buttons.GetPreferredSize(Size.Empty).Width + UiLayout.PageRight,
            UiLayout.PageLeft + UiLayout.AtTextSize(ListWidth) + UiLayout.RowGap + _fieldsWidth + UiLayout.PageRight,
        }.Max();

        DialogFit.Keep(this, page, width, height: DialogHeight, widthMeasured: true);
    }

    /// <summary>
    /// The fields' column and what is in it, in the faces the theme pass left
    /// them in: as wide as it was at the normal text size, at the size in
    /// use, or as the setup sheet drop-down's longest choice if that is more
    /// - measured once the drop-down is filled, which LoadSelected does again
    /// with the same sheets whenever a chassis is picked. The captions and
    /// the hint each as tall as their text wrapped to it. Once: the dialog
    /// is built at the text size in use and is not kept in step with a
    /// change made while it is up (see DialogFit).
    /// </summary>
    private void SizeFields(IEnumerable<Label> labels)
    {
        (Font Font, int Count, int Width)? sheetWidth = null;

        _fieldsWidth = Math.Max(UiLayout.AtTextSize(FieldsWidth), Pieces.PickerWidth(_cmbSheet, ref sheetWidth));

        foreach (var label in labels)
        {
            label.Height = label.GetPreferredSize(new Size(_fieldsWidth, 0)).Height;
        }
    }

    // keepId names the chassis to leave selected. Save and Retire must pass
    // it: they have already written the change by the time Reload runs, and
    // the store sorts retired types last and the rest by name, so the list's
    // old SelectedIndex can now point at a different chassis. Reading the id
    // back through Selected() then picked whichever chassis slid into that
    // slot - retire MA-020 and MB-010 came up selected with its button
    // saying "Retire", so a second click meant as an undo retired the wrong
    // one. With no id given (first load, Delete) the old slot is still the
    // right guess: after a delete it lands on the chassis that followed.
    private void Reload(string? keepId = null)
    {
        var selectedId = keepId ?? Selected()?.Id;

        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var type in _types.All)
        {
            _list.Items.Add(type.Retired ? type.DisplayName + "  (retired)" : type.DisplayName);
        }

        _list.EndUpdate();

        var index = _types.All.ToList().FindIndex(t => string.Equals(t.Id, selectedId, StringComparison.Ordinal));

        if (_list.Items.Count > 0)
        {
            _list.SelectedIndex = index >= 0 ? index : 0;
        }
        else
        {
            LoadSelected();
        }
    }

    private ChassisType? Selected() =>
        _list.SelectedIndex >= 0 ? _types.All.ElementAtOrDefault(_list.SelectedIndex) : null;

    private void LoadSelected()
    {
        var type = Selected();

        _txtName.Text = type?.Name ?? string.Empty;
        _txtMaker.Text = type?.Maker ?? string.Empty;

        _cmbSheet.Items.Clear();
        _cmbSheet.Items.Add(new SheetChoice(string.Empty, "(none)"));

        foreach (var sheet in _sheets.All)
        {
            _cmbSheet.Items.Add(new SheetChoice(sheet.Id, sheet.Name));
        }

        _cmbSheet.SelectedItem = _cmbSheet.Items
            .Cast<SheetChoice>()
            .FirstOrDefault(s => string.Equals(s.Id, type?.SetupSheetId, StringComparison.Ordinal))
            ?? _cmbSheet.Items[0];

        var have = type is not null;

        _txtName.Enabled = have;
        _txtMaker.Enabled = have;
        _cmbSheet.Enabled = have;

        // Nothing picked, nothing to save: it was in the app's colour and
        // did nothing with no chassis left (layout pictures, 30 Sep).
        _btnSave.Enabled = have;
        _btnDelete.Enabled = have;
        _btnRetire.Enabled = have;
        _btnRetire.Text = type is { Retired: true } ? "Put Back" : "Retire";
    }

    private void AddType()
    {
        var created = new ChassisType { Name = "New chassis" };

        _types.Upsert(created);
        Reload();

        var index = _types.All.ToList().FindIndex(t => string.Equals(t.Id, created.Id, StringComparison.Ordinal));

        if (index >= 0)
        {
            _list.SelectedIndex = index;
        }

        _txtName.Focus();
        _txtName.SelectAll();
    }

    private void SaveSelected()
    {
        if (Selected() is not { } type)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_txtName.Text))
        {
            AppDialog.Show(this, "A chassis needs a name.", "Name it", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        type.Name = _txtName.Text.Trim();
        type.Maker = _txtMaker.Text.Trim();
        type.SetupSheetId = (_cmbSheet.SelectedItem as SheetChoice)?.Id ?? string.Empty;

        _types.Upsert(type);
        Reload(type.Id);
    }

    private void RetireSelected()
    {
        if (Selected() is not { } type)
        {
            return;
        }

        if (!type.Retired && !RetireConfirm.Ask(this, type.DisplayName))
        {
            return;
        }

        type.Retired = !type.Retired;
        _types.Upsert(type);
        Reload(type.Id);
    }

    private void DeleteSelected()
    {
        if (Selected() is not { } type)
        {
            return;
        }

        var inUse = _cars.All.Count(c => string.Equals(c.ChassisTypeId, type.Id, StringComparison.Ordinal));

        if (inUse > 0)
        {
            AppDialog.Show(this,
                $"{Plural.Of(inUse, "car")} in the garage {(inUse == 1 ? "is" : "are")} on a {type.DisplayName}. "
                + "Retire it instead - it stays on those cars and stops being offered for new ones.",
                "Still in use", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (AppDialog.Show(this,
                $"Delete \"{type.DisplayName}\"?",
                "Delete chassis", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _types.Delete(type.Id);
        Reload();
    }
}

/// <summary>
/// One car, opened from the wall: its photographs big, what it is, and
/// every setup ever logged for it.
///
/// A window rather than a panel on the page, because the page is now a
/// wall of pictures and a detail pane underneath it would leave both
/// halves too short to be worth looking at. This is also where the old
/// Tuning page ended up - a setup belongs to a car, so it lives on the
/// car rather than on a separate page pointed at the same row.
///
/// Every change is written through the stores before this closes, so the
/// wall behind it just re-reads. Nothing is handed back.
///
/// On the layout pieces (DialogFit): a strip across the top for the car -
/// a thumbnail of its photo, and beside it the heading, the facts on a line
/// or two, the notes, and the car's three buttons: Edit, Photos and More,
/// whose menu holds Lend Out, Transfer, Retire and Delete Car. Then the
/// setups' heading with Log a Setup against the right-hand edge, the setups
/// themselves as a timeline of cards (SetupTimeline) taking the height, and
/// Close at the foot.
///
/// WHY (7 Nov 2026, the car window redesign): it was the photo big beside
/// four lines of facts, two rows of five buttons - each with its own Edit
/// and its own pink Delete, so which acted on the car and which on the
/// setup was a guess - and an eight-column grid of setups that cut short
/// exactly the two columns worth reading, what changed and what the setup
/// was, while Track and Best Lap sat empty. Now the car has one place for
/// its buttons and the setup has its own, on its card, and no Delete shows
/// until a menu is opened. It opens at the size it had at the normal text
/// size, at the size in use - never taller than the screen - and can be
/// made bigger, the setups taking the room.
/// </summary>
internal sealed class CarDetailForm : Form
{
    /// <summary>How big the window opens inside its frame at the normal text size - the size it had. It can be made bigger; the setups take the height.</summary>
    private const int DialogWidth = 960;
    private const int DialogHeight = 692;

    /// <summary>The thumbnail's width at the normal text size - the strip's left-hand column.</summary>
    private const int PhotoWidth = 180;

    /// <summary>The thumbnail's height at the normal text size - taller when what is said beside it needs more.</summary>
    private const int PhotoHeight = 120;

    /// <summary>
    /// The photo's size, <paramref name="pixels"/> at the normal text size,
    /// at the larger text sizes grown by half as much as the text: at 200%
    /// one and a half times its size, not twice. It is a picture, not words
    /// - grown with the text, it took half the window at 200% and left the
    /// setups two rows under it (layout pictures, 30 Sep). The facts beside
    /// it have the width it leaves.
    /// </summary>
    private static int PhotoAt(int pixels) =>
        UiLayout.AtDpi((int)Math.Round(pixels * (1f + Math.Max(0f, AppTheme.FontScale - 1f) / 2f)));

    private readonly Car _car;
    private readonly CarStore _cars;
    private readonly ChassisTypeStore _chassisTypes;
    private readonly ProfileStore _profiles;
    private readonly RaceClassStore _raceClasses;
    private readonly SetupLogStore _setups;
    private readonly SessionHistoryStore _history;
    private readonly TrackLayoutStore _trackLayouts;
    private readonly ChassisTemplateStore _chassisTemplates;
    private readonly UndoService _undo;
    private readonly RaceEventStore? _events;

    /// <summary>The thumbnail. A click shows the next photo, or opens Photos when there are none yet.</summary>
    private readonly PictureBox _photo = new();

    private readonly Label _lblHeading = new();
    private readonly Label _lblFacts = new();
    private readonly Label _lblNotes = new();

    /// <summary>More's menu: the car's less everyday buttons, Delete Car last and in the danger colour.</summary>
    private readonly ContextMenuStrip _moreMenu = new();
    private readonly ToolStripMenuItem _miLend = new("Lend Out...");
    private readonly ToolStripMenuItem _miRetire = new("Retire");

    private readonly Label _lblSetups = new();
    private readonly SetupTimeline _timeline = new();
    private readonly Button _btnLog = new() { Text = "Log a Setup..." };
    private readonly ContextMenuStrip _setupMenu = new();

    private Image? _shown;
    private int _index;

    public CarDetailForm(
        Car car,
        CarStore cars,
        ChassisTypeStore chassisTypes,
        ProfileStore profiles,
        RaceClassStore raceClasses,
        SetupLogStore setups,
        SessionHistoryStore history,
        TrackLayoutStore trackLayouts,
        ChassisTemplateStore chassisTemplates,
        UndoService undo,
        RaceEventStore? events)
    {
        _car = car;
        _cars = cars;

        // WHY (2026-11-05, Bug hunt 11: a stale car written back whole): a
        // wall built before a phone change hands over the car as it was, and
        // the store now holds a newer object - so the window opens on that.
        if (cars.Get(car.Id) is { } live && !ReferenceEquals(live, car))
        {
            CopyOnto(car, live);
        }

        _chassisTypes = chassisTypes;
        _profiles = profiles;
        _raceClasses = raceClasses;
        _setups = setups;
        _history = history;
        _trackLayouts = trackLayouts;
        _chassisTemplates = chassisTemplates;
        _undo = undo;
        _events = events;

        Text = car.DisplayName;
        Font = AppTheme.BodyFont;

        var page = BuildLayout();
        AppTheme.ApplyRecursively(this);

        // After the theme pass: the picture sits on a neutral field so a
        // car is judged on its own colours, not the app's.
        _photo.BackColor = AppTheme.Background;

        // Logging a setup is what this window is opened for most - the one
        // button in the app's colour. The theme goes by the caption, and
        // "Log a Setup" is not one it knows.
        AppTheme.StyleButton(_btnLog, AppTheme.ButtonKind.Primary);

        AppTheme.StyleContextMenu(_moreMenu);
        AppTheme.StyleContextMenu(_setupMenu);

        RefreshAll();

        DialogFit.Keep(this, page, width: DialogWidth, height: DialogHeight);
    }

    private string? CurrentEventId => _events?.Active is { IsLegacy: false } day ? day.Id : null;

    /// <summary>The window's page, built: see the class notes. Returned for DialogFit to size the window to.</summary>
    private FillPage BuildLayout()
    {
        // --- the car, across the top ---

        _photo.SizeMode = PictureBoxSizeMode.Zoom;
        _photo.Click += (_, _) => PhotoClicked();
        _photo.Paint += (_, e) => PaintPhotoBadge(e.Graphics);

        _lblHeading.AutoSize = true;
        _lblHeading.Font = AppTheme.HeadingFont;

        _lblFacts.AutoSize = true;

        _lblNotes.AutoSize = true;
        _lblNotes.ForeColor = AppTheme.TextMuted;
        _lblNotes.Font = AppTheme.SmallFont;

        var btnEdit = new Button { Text = "Edit..." };
        btnEdit.Click += (_, _) => EditCar();

        var btnPhotos = new Button { Text = "Photos..." };
        btnPhotos.Click += (_, _) => EditPhotos();

        var btnMore = new Button { Text = "More ▾" };
        btnMore.Click += (_, _) => _moreMenu.Show(btnMore, new Point(0, btnMore.Height));

        _miLend.Click += (_, _) => LendCar();
        _miRetire.Click += (_, _) => RetireCar();

        var miTransfer = new ToolStripMenuItem("Transfer...", null, (_, _) => TransferCar());
        var miDelete = new ToolStripMenuItem("Delete Car...", null, (_, _) => DeleteCar()) { Tag = AppTheme.DangerMenuItem };

        _moreMenu.ShowImageMargin = false;
        _moreMenu.Items.AddRange(new ToolStripItem[] { _miLend, miTransfer, _miRetire, new ToolStripSeparator(), miDelete });

        // The thumbnail down the left, and beside it the heading, the facts,
        // the notes and the car's three buttons, each wrapping to the width
        // left - the thumbnail taking any height the words need past its own.
        var band = new Beside(PhotoAt(PhotoWidth))
        {
            SideOnLeft = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        band.AddSideFill(_photo, _ => PhotoAt(PhotoHeight), gapBelow: 0);
        band.Add(_lblHeading, gapBelow: UiLayout.HintGap);
        band.Add(_lblFacts, gapBelow: UiLayout.HintGap);
        // The room over the buttons is the bar's own padding, not a gap
        // under the notes, so a car with no notes - its line shrunk to
        // nothing - keeps the same room there.
        var carButtons = new ButtonBar().With(btnEdit, btnPhotos, btnMore);
        carButtons.Padding = new Padding(0, UiLayout.RowGap, 0, 0);

        band.Add(_lblNotes, gapBelow: 0);
        band.Add(carButtons, gapBelow: 0);

        // --- its setups ---

        _lblSetups.AutoSize = true;
        _lblSetups.Font = AppTheme.BoldFont;
        _lblSetups.Anchor = AnchorStyles.Left;

        _btnLog.Click += (_, _) => AddSetup();

        var setupsHeading = new SplitBar(
            new ButtonBar().With(_lblSetups),
            new ButtonBar(alignRight: true).With(_btnLog));

        _timeline.EmptyText = "No setups logged for this car yet. Log one after a run - each one after it says what changed from the one before.";
        _timeline.Activated += EditSetup;
        _timeline.DeletePressed += DeleteSetup;

        _setupMenu.ShowImageMargin = false;
        _setupMenu.Items.AddRange(new ToolStripItem[]
        {
            new ToolStripMenuItem("Edit...", null, (_, _) => EditSetup()),
            new ToolStripMenuItem("Duplicate", null, (_, _) => DuplicateSetup()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Delete Setup...", null, (_, _) => DeleteSetup()) { Tag = AppTheme.DangerMenuItem },
        });
        _timeline.CardMenu = _setupMenu;

        var btnClose = new Button { Text = "Close", DialogResult = DialogResult.OK };

        btnEdit.TabIndex = 0;
        btnPhotos.TabIndex = 1;
        btnMore.TabIndex = 2;

        // The setups take the height; one card's first lines stay before the
        // window scrolls instead.
        var page = new FillPage();
        page.Add(band, gapBelow: UiLayout.SectionGap);
        page.Add(setupsHeading, gapBelow: UiLayout.RowGap);
        page.AddFill(_timeline, _ => SetupTimeline.LeastHeight, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnClose), gapBelow: 0);

        Controls.Add(page);

        AcceptButton = btnClose;
        CancelButton = btnClose;

        return page;
    }

    private void RefreshAll()
    {
        Text = _car.DisplayName;
        _lblHeading.Text = _car.DisplayName + (_car.Retired ? "   (retired)" : string.Empty);

        // What the car is on one line, wrapping when it has to, and what
        // has happened to it - changed hands, lent out - on the next.
        var facts = new List<string>();
        var lines = new List<string>();

        var owner = _car.IsClubCar ? "The club" : NameOf(_car.OwnerTransponder);
        facts.Add($"Owner:  {(owner.Length == 0 ? "—" : owner)}");

        // The last time it changed hands, and from whom: by their name now,
        // or as it was then for a driver no longer on file.
        if (_car.Transfers.LastOrDefault() is { } last)
        {
            var was = string.IsNullOrWhiteSpace(last.FromTransponder)
                ? "the club"
                : _profiles.Get(last.FromTransponder) is not null
                    ? NameOf(last.FromTransponder)
                    : last.FromName.Length > 0 ? last.FromName : last.FromTransponder;

            lines.Add($"Transferred from:  {was}, {last.AtUtc.ToLocalTime():d MMM yyyy}");
        }

        facts.Add($"Class:  {(string.IsNullOrWhiteSpace(_car.ClassName) ? "—" : _car.ClassName)}");

        var chassis = _chassisTypes.NameOf(_car.ChassisTypeId);
        facts.Add($"Chassis:  {(chassis.Length == 0 ? "—" : chassis)}");

        if (!string.IsNullOrWhiteSpace(_car.Bodyshell))
        {
            facts.Add($"Shell:  {_car.Bodyshell}");
        }

        if (!string.IsNullOrWhiteSpace(_car.RaceNumber))
        {
            facts.Add($"Number:  {_car.RaceNumber}");
        }

        lines.Insert(0, string.Join("     ", facts));

        var eventId = CurrentEventId;
        var driver = _car.DriverFor(eventId);

        if (eventId is not null
            && !string.IsNullOrWhiteSpace(driver)
            && !string.Equals(driver, _car.OwnerTransponder, StringComparison.OrdinalIgnoreCase))
        {
            lines.Add($"On loan to:  {NameOf(driver)}");
        }

        _lblFacts.Text = string.Join("\r\n", lines);

        _lblNotes.Text = _car.Notes;
        // No notes: the line shrunk to nothing rather than hidden. The
        // strip (Beside) measured a hidden part in and placed it out, and
        // the car's buttons under it ran past its foot at the larger text
        // sizes (layout test, 7 Nov 2026).
        var hasNotes = !string.IsNullOrWhiteSpace(_car.Notes);
        _lblNotes.AutoSize = hasNotes;

        if (!hasNotes)
        {
            _lblNotes.Height = 0;
        }

        _miRetire.Text = _car.Retired ? "Put Back" : "Retire";

        // Lending is for one race day; with none open it says why it can't.
        _miLend.Enabled = eventId is not null;
        _miLend.Text = eventId is not null ? "Lend Out..." : "Lend Out (open a race day first)";

        _index = Math.Clamp(_index, 0, Math.Max(0, _car.PhotoFileNames.Count - 1));

        ShowPhoto();
        RefreshSetups();
    }

    private string NameOf(string transponder)
    {
        if (string.IsNullOrWhiteSpace(transponder))
        {
            return string.Empty;
        }

        var profile = _profiles.Get(transponder);

        return profile is null || string.IsNullOrWhiteSpace(profile.DriverName)
            ? transponder
            : profile.DriverName;
    }

    private void Step(int by)
    {
        if (_car.PhotoFileNames.Count == 0)
        {
            return;
        }

        _index = (_index + by + _car.PhotoFileNames.Count) % _car.PhotoFileNames.Count;
        ShowPhoto();
    }

    private void ShowPhoto()
    {
        // Disposed before the next is loaded. These are full-size pictures
        // rather than cached thumbnails, and stepping through a filmstrip
        // would otherwise hold every one of them.
        _photo.Image = null;
        _shown?.Dispose();
        _shown = null;

        if (_index >= 0 && _index < _car.PhotoFileNames.Count)
        {
            _shown = PhotoStore.Cars.Full(_car.PhotoFileNames[_index]);
        }

        _photo.Image = _shown;

        var count = _car.PhotoFileNames.Count;

        _photo.Cursor = count == 1 ? Cursors.Default : Cursors.Hand;
        _photo.AccessibleName = count switch
        {
            0 => "No photographs yet - click to add one",
            1 => "The car's photograph",
            _ => $"Photograph {_index + 1} of {count} - click for the next",
        };

        _photo.Invalidate();
    }

    /// <summary>The thumbnail's click: the next photo, or Photos when there are none yet to step through.</summary>
    private void PhotoClicked()
    {
        if (_car.PhotoFileNames.Count == 0)
        {
            EditPhotos();
        }
        else
        {
            Step(1);
        }
    }

    /// <summary>
    /// Over the thumbnail: "2 / 5" in its corner when there is more than one
    /// photo - the arrows the big photo had are gone, and a click steps on -
    /// and "Add a photo" across it when there are none.
    /// </summary>
    private void PaintPhotoBadge(Graphics g)
    {
        var count = _car.PhotoFileNames.Count;
        var area = _photo.ClientRectangle;

        if (count == 0)
        {
            TextRenderer.DrawText(g, "Add a photo", AppTheme.SmallFont, area, AppTheme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        if (count < 2)
        {
            return;
        }

        var text = $"{_index + 1} / {count}";
        var size = TextRenderer.MeasureText(text, AppTheme.SmallFont);
        var pad = UiLayout.AtTextSize(4);
        var badge = new Rectangle(area.Right - size.Width - pad * 3, area.Bottom - size.Height - pad * 2, size.Width + pad * 2, size.Height);

        AppTheme.DrawPill(g, badge, AppTheme.Surface, string.Empty, AppTheme.TextDark);
        TextRenderer.DrawText(g, text, AppTheme.SmallFont, badge, AppTheme.TextDark,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    // ------------------------------------------------------------- car

    private void EditCar()
    {
        CatchUp();

        var opened = _car.CloneForUndo();

        using var form = new CarForm(opened.CloneForUndo(), _chassisTypes, _profiles, _raceClasses, isNew: false);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var (before, after) = OverWhatIsStoredNow(opened, form.Result);

        _undo.Capture($"Edit {before.DisplayName}", () => _cars.Upsert(before));

        Apply(after);
    }

    private void EditPhotos()
    {
        CatchUp();

        var opened = _car.CloneForUndo();

        using var form = new CarPhotosForm(opened.CloneForUndo());

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var (before, after) = OverWhatIsStoredNow(opened, form.Result);

        // Pictures dropped from the car are left on disk - the undo puts
        // the NAMES back, and a name whose file had been deleted would put
        // back a broken picture.
        _undo.Capture($"Change {before.DisplayName}'s photos", () => _cars.Upsert(before));

        Apply(after);
    }

    /// <summary>
    /// WHY (2026-11-05, Bug hunt 11: a stale car written back whole): a
    /// phone's change to this car (a photo, its name, notes, chassis) is
    /// applied on the roster tick, which runs while this window - and the
    /// dialogs it opens - are up, and it puts a NEW object in the store. The
    /// window's own copy was then stale, and Retire, Edit and Photos wrote
    /// that copy back whole: the phone's photo dropped off the car and its
    /// edits were undone, without a word, after the phone was told "added".
    /// So the window catches up with the store before it starts one.
    /// </summary>
    private void CatchUp()
    {
        if (_cars.Get(_car.Id) is { } live && !ReferenceEquals(live, _car))
        {
            CopyOnto(_car, live);
            RefreshAll();
        }
    }

    /// <summary>
    /// What a dialog's edit writes, and what its undo puts back, when the
    /// store may have changed while the dialog was open (a phone's change -
    /// see CatchUp): the desk's changes over what is stored now (MergeEdit).
    /// Undo puts back what was stored just before the save, not what the
    /// dialog opened on, so it does not take the phone's change away too.
    /// </summary>
    private (Car Before, Car After) OverWhatIsStoredNow(Car opened, Car edited)
    {
        if (_cars.Get(_car.Id) is not { } live)
        {
            return (opened, edited);
        }

        return (live.CloneForUndo(), MergeEdit(opened, edited, live));
    }

    /// <summary>
    /// The desk's edit (<paramref name="edited"/>, made from
    /// <paramref name="opened"/>) laid over the car as it is stored now
    /// (<paramref name="live"/>): each field the desk changed takes the
    /// desk's value, every other field keeps what is stored now. Photos the
    /// desk did not touch stay as stored; when it did touch them, its list
    /// stands, plus photos added since it opened and minus any taken off
    /// since. Bug hunt 11 (2026-11-05) - see CatchUp.
    /// </summary>
    internal static Car MergeEdit(Car opened, Car edited, Car live)
    {
        static string Pick(string was, string desk, string now) =>
            string.Equals(was, desk, StringComparison.Ordinal) ? now : desk;

        var merged = live.CloneForUndo();

        merged.Name = Pick(opened.Name, edited.Name, live.Name);
        merged.OwnerTransponder = Pick(opened.OwnerTransponder, edited.OwnerTransponder, live.OwnerTransponder);
        merged.ClassName = Pick(opened.ClassName, edited.ClassName, live.ClassName);
        merged.ChassisTypeId = Pick(opened.ChassisTypeId, edited.ChassisTypeId, live.ChassisTypeId);
        merged.Bodyshell = Pick(opened.Bodyshell, edited.Bodyshell, live.Bodyshell);
        merged.RaceNumber = Pick(opened.RaceNumber, edited.RaceNumber, live.RaceNumber);
        merged.Notes = Pick(opened.Notes, edited.Notes, live.Notes);
        merged.LoanedToTransponder = Pick(opened.LoanedToTransponder, edited.LoanedToTransponder, live.LoanedToTransponder);
        merged.LoanedForEventId = Pick(opened.LoanedForEventId, edited.LoanedForEventId, live.LoanedForEventId);
        merged.Retired = opened.Retired == edited.Retired ? live.Retired : edited.Retired;

        if (!opened.PhotoFileNames.SequenceEqual(edited.PhotoFileNames, StringComparer.Ordinal))
        {
            bool Was(string p) => opened.PhotoFileNames.Contains(p, StringComparer.Ordinal);
            bool IsNow(string p) => live.PhotoFileNames.Contains(p, StringComparer.Ordinal);

            merged.PhotoFileNames = edited.PhotoFileNames
                .Where(p => IsNow(p) || !Was(p))
                .Concat(live.PhotoFileNames.Where(p => !Was(p) && !edited.PhotoFileNames.Contains(p, StringComparer.Ordinal)))
                .ToList();
        }

        return merged;
    }

    private void LendCar()
    {
        if (CurrentEventId is not { } eventId)
        {
            return;
        }

        var dayName = _events?.Get(eventId)?.DisplayName ?? "tonight";

        using var form = new LendCarForm(_car, _profiles, dayName, NameOf(_car.OwnerTransponder), eventId);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // 5 Nov 2026, Bug hunt 11 review: the undo snapshot is the car as
        // stored now, not the window's copy - a phone's photo or edit that
        // arrived while the lend dialog was open would otherwise be wiped
        // by Undo (see CatchUp).
        CatchUp();

        var before = _car.CloneForUndo();

        _undo.Capture($"Lend {before.DisplayName}", () => _cars.Upsert(before));

        _cars.Lend(_car.Id, form.ToTransponder, eventId);

        if (_cars.Get(_car.Id) is { } live)
        {
            Apply(live);
        }
    }

    private void RetireCar()
    {
        // Bug hunt 11 (2026-11-05): the car as stored now, not as it was
        // when the window opened - see CatchUp.
        CatchUp();

        if (!_car.Retired && !RetireConfirm.Ask(this, _car.DisplayName))
        {
            return;
        }

        // 5 Nov 2026, Bug hunt 11 review: the confirm is modal and the
        // roster tick runs under it, so catch up again before snapshotting.
        CatchUp();

        var before = _car.CloneForUndo();

        // Retiring the car its owner is announced in moves them to another
        // (GarageSync.SyncNames); undoing it puts them back in this one.
        var activeCar = new GarageSync.ActiveCarMemo(_profiles, _car.OwnerTransponder);

        _undo.Capture(before.Retired ? $"Put {before.DisplayName} back" : $"Retire {before.DisplayName}",
            () =>
            {
                _cars.Upsert(before.CloneForUndo());
                activeCar.PutBack();
            });

        var after = _car.CloneForUndo();
        after.Retired = !after.Retired;

        Apply(after);
        activeCar.Settle();
    }

    private void DeleteCar()
    {
        var setups = SetupsForThisCar().Count;

        var warning = setups == 0
            ? string.Empty
            : $"\r\n\r\n{Plural.Of(setups, "setup")} logged against it stay in the log - they lose the car, not the numbers.";

        if (AppDialog.Show(this,
                $"Delete \"{_car.DisplayName}\"?{warning}\r\n\r\nRetiring it instead keeps it out of the way without losing it.",
                "Delete car", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        // 5 Nov 2026, Bug hunt 11 review: Undo puts back the car as stored
        // now (a phone's photo or edit included), not the window's stale
        // copy - see CatchUp.
        CatchUp();

        var before = _car.CloneForUndo();
        var activeCar = new GarageSync.ActiveCarMemo(_profiles, _car.OwnerTransponder);

        _undo.Capture($"Delete {before.DisplayName}", () =>
        {
            _cars.Upsert(before.CloneForUndo());
            activeCar.PutBack();
        });

        // The photographs are left on disk: an undo has to put the car back
        // whole, and a deleted file cannot be un-deleted.
        _cars.Delete(_car.Id);
        activeCar.Settle();

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>Writes an edited copy back to the store and onto the car this window is showing, so both agree without a re-read.</summary>
    private void Apply(Car edited)
    {
        _cars.Upsert(edited);

        CopyOnto(_car, edited);

        RefreshAll();
    }

    /// <summary>Everything the window shows of a car, from <paramref name="source"/> onto <paramref name="target"/>.</summary>
    private static void CopyOnto(Car target, Car source)
    {
        target.Name = source.Name;
        target.OwnerTransponder = source.OwnerTransponder;
        target.ClassName = source.ClassName;
        target.ChassisTypeId = source.ChassisTypeId;
        target.Bodyshell = source.Bodyshell;
        target.RaceNumber = source.RaceNumber;
        target.Notes = source.Notes;
        target.PhotoFileNames = new List<string>(source.PhotoFileNames);
        target.LoanedToTransponder = source.LoanedToTransponder;
        target.LoanedForEventId = source.LoanedForEventId;
        target.Retired = source.Retired;
        target.Transfers = source.Transfers.Select(t => t with { }).ToList();
    }

    /// <summary>Gives the car to someone else for good (CarTransferring), and shows it as it now is.</summary>
    private void TransferCar()
    {
        if (!CarTransferring.Run(this, _car, _cars, _profiles, _undo, _history))
        {
            return;
        }

        if (_cars.Get(_car.Id) is { } live)
        {
            Apply(live.CloneForUndo());
        }
    }

    // ---------------------------------------------------------- setups

    /// <summary>
    /// This car's setups: by id where an entry has one, and otherwise by
    /// the owner's name and the typed car string - which is all an entry
    /// written before the Garage existed has. That fallback is why nothing
    /// had to be rewritten on upgrade.
    /// </summary>
    private IReadOnlyList<SetupEntry> SetupsForThisCar()
    {
        var ownerName = NameOf(_car.OwnerTransponder);

        return _setups.All
            .Where(e =>
                string.Equals(e.CarId, _car.Id, StringComparison.Ordinal)
                || (string.IsNullOrEmpty(e.CarId)
                    && ownerName.Length > 0
                    && string.Equals(e.DriverName.Trim(), ownerName, StringComparison.OrdinalIgnoreCase)
                    && MatchesCarWords(e.CarModel)))
            .ToList();
    }

    /// <summary>
    /// Whether an old entry's typed car string looks like this car.
    /// Deliberately loose: a wrong guess lists a setup under the wrong car
    /// of the same driver, and being strict shows an empty list to everyone
    /// who upgraded.
    /// </summary>
    private bool MatchesCarWords(string typed)
    {
        var text = typed.Trim();

        if (text.Length == 0)
        {
            return false;
        }

        return string.Equals(text, _car.Name.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, _car.Bodyshell.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshSetups()
    {
        var items = new List<SetupTimeline.Item>();

        // Newest first, so the setup before each one is the next card down.
        var setups = SetupsForThisCar();

        for (var i = 0; i < setups.Count; i++)
        {
            var entry = setups[i];
            var template = entry.ChassisTemplateId is null ? null : _chassisTemplates.Get(entry.ChassisTemplateId);

            // Every one of these is this car's, so the one before is simply
            // the next older - no need to match the car again. Not across
            // the change to setup sheets: an older entry and a sheet entry
            // share no fields, and every one would read as a change.
            var previous = i + 1 < setups.Count ? setups[i + 1] : null;
            var changes = previous is null || previous.IsLegacy != entry.IsLegacy
                ? null
                : SetupDiff.Between(previous, entry, _chassisTemplates.Get);

            // The whole list, every change - the card wraps rather than
            // cutting it short as the grid's cell did.
            var (changed, quiet) = (previous, changes) switch
            {
                (null, _) => ("First setup for this car", true),
                (_, null) => ("First on a setup sheet", true),
                (_, { Count: 0 }) => ("No changes from the one before", true),
                _ => ("Changed:  " + SetupDiff.Describe(changes, int.MaxValue), false),
            };

            // A sheet deleted since says so, rather than saying nothing - the
            // delete asks with exactly these words.
            var sheet = template?.Name ?? (entry.IsLegacy ? "(older entry)" : entry.ChassisTemplateId is not null ? "(sheet deleted)" : string.Empty);
            var bestLap = BestLapFor(entry);

            var facts = new[]
                {
                    entry.DriverName.Trim(),
                    entry.TrackLayoutName.Trim(),
                    sheet,
                    entry.IsLegacy ? string.Empty : $"{entry.FilledCount} filled",
                    bestLap.Length == 0 ? string.Empty : $"best lap {bestLap}",
                }
                .Where(f => f.Length > 0);

            items.Add(new SetupTimeline.Item(
                entry.Id,
                AppTime.DateAndTime(entry.RecordedAtUtc.ToLocalTime()),
                string.Join("  ·  ", facts),
                changed,
                quiet,
                SetupDiff.Filled(entry, _chassisTemplates.Get, int.MaxValue)));
        }

        _lblSetups.Text = setups.Count == 0 ? "Setups" : $"Setups  ·  {setups.Count}";
        _timeline.SetItems(items);
    }

    /// <summary>The best lap of the session an entry was linked to, or empty - the reason for linking one at all.</summary>
    private string BestLapFor(SetupEntry entry)
    {
        if (string.IsNullOrEmpty(entry.LinkedSessionId) || _history.Get(entry.LinkedSessionId) is not { } session)
        {
            return string.Empty;
        }

        var mine = session.Results.FirstOrDefault(r =>
            string.Equals(r.DriverName.Trim(), entry.DriverName.Trim(), StringComparison.OrdinalIgnoreCase));

        return mine is { FastLapMs: > 0 } ? $"{mine.FastLapMs / 1000.0:0.000}s" : string.Empty;
    }

    private SetupEntry? SelectedSetup() =>
        _timeline.SelectedId is { } id ? _setups.Get(id) : null;

    private void AddSetup()
    {
        var entry = new SetupEntry
        {
            CarId = _car.Id,
            DriverName = NameOf(_car.OwnerTransponder),
            CarModel = _car.DisplayName,

            // The chassis can say which sheet to reach for - that is what
            // ChassisType.SetupSheetId is for.
            ChassisTemplateId = SheetForChassis(),
        };

        EditSetupEntry(entry, isNew: true);
    }

    private string? SheetForChassis()
    {
        var sheetId = _chassisTypes.Get(_car.ChassisTypeId)?.SetupSheetId;

        return string.IsNullOrWhiteSpace(sheetId) || _chassisTemplates.Get(sheetId) is null ? null : sheetId;
    }

    private void EditSetup()
    {
        if (SelectedSetup() is { } entry)
        {
            EditSetupEntry(entry, isNew: false);
        }
    }

    private void EditSetupEntry(SetupEntry entry, bool isNew)
    {
        var before = isNew ? null : _setups.Get(entry.Id);
        var beforeCopy = before is null ? null : SetupLogStore.CopyOf(before);

        // The form writes onto what it is given as it goes (and folds an old
        // entry's fixed fields into the sheet as it opens), so a stored entry
        // is handed over as a copy - as the setup log does. Handed the stored
        // one, a Cancel left the half-made changes in memory, and the next
        // save of any setup wrote them to disk. A new entry is not stored
        // yet, so it needs no copy.
        using var form = new SetupEntryForm(isNew ? entry : SetupLogStore.CopyOf(entry),
            _chassisTemplates, _history, _profiles, _trackLayouts, _cars, defaultCarId: _car.Id);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var saved = form.Result;

        // The car it belongs to, stamped on save rather than on upgrade -
        // see SetupEntry.CarId.
        if (string.IsNullOrEmpty(saved.CarId))
        {
            saved.CarId = _car.Id;
        }

        var id = saved.Id;

        // WHY (2026-11-05, Bug hunt 11: a desk save over a phone's fix): a
        // driver's phone can fix tonight's entry while this sheet is open
        // (the roster tick applies it under the dialog), and the save wrote
        // the copy taken at open back whole - the fix gone, after the phone
        // was told "Setup fixed". So what the desk changed goes over the
        // entry as it is stored now, and Undo puts back that, not the
        // version from before the fix.
        if (beforeCopy is not null && _setups.Get(id) is { } now)
        {
            saved = SetupLogStore.MergeEdit(beforeCopy, saved, now);
            beforeCopy = SetupLogStore.CopyOf(now);
        }

        _undo.Capture(isNew ? "Log a setup" : "Edit a setup", () => _setups.UndoUpsert(id, beforeCopy));

        _setups.Upsert(saved);
        RefreshSetups();
    }

    private void DuplicateSetup()
    {
        if (SelectedSetup() is { } entry)
        {
            EditSetupEntry(entry.CloneForNewEntry(), isNew: true);
        }
    }

    /// <summary>
    /// Deletes the selected setup, after asking. One at a time: the grid
    /// this replaced took several at once, but a card is picked one by one,
    /// and Undo puts back a slip.
    /// </summary>
    private void DeleteSetup()
    {
        if (SelectedSetup() is not { } entry)
        {
            return;
        }

        var when = AppTime.DateAndTime(entry.RecordedAtUtc.ToLocalTime());

        if (AppDialog.Show(this, $"Delete the setup logged {when}?",
                "Delete setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var removed = new List<SetupEntry> { SetupLogStore.CopyOf(entry) };

        _undo.Capture("Delete a setup", () => _setups.RestoreDeleted(removed));

        _setups.Delete(entry.Id);

        RefreshSetups();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _photo.Image = null;
            _shown?.Dispose();
            _moreMenu.Dispose();
            _setupMenu.Dispose();
        }

        base.Dispose(disposing);
    }
}
