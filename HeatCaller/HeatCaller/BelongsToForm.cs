namespace HeatCaller;

/// <summary>
/// "Who is on transponder B70E21?" - gives a transponder nobody has to a
/// driver already on file: pick them, say whether it is fitted to one of
/// their cars or moves between cars, and whether it replaces one of theirs
/// (which is then retired, its laps staying theirs).
///
/// Only asks; the caller makes the change (MainForm.GiveTransponderTo), so
/// it can take its undo step.
///
/// The same window transfers a transponder a driver already has to another
/// driver (TransponderTransferring.Run): given the driver it comes from, it
/// leaves them off the list and words itself as a transfer.
///
/// On the layout pieces (DialogFit): the question at the top, the search
/// and the drivers taking the height, the choices under them and the
/// buttons at the foot.
/// </summary>
internal sealed class BelongsToForm : Form
{
    /// <summary>The drivers show at least this many before the window scrolls instead.</summary>
    private const int LeastRows = 5;

    private readonly string _transponder;
    private readonly ProfileStore _profiles;
    private readonly CarStore _cars;

    /// <summary>For a transfer: the driver it comes from (main transponder), left off the list. Null for a transponder nobody has.</summary>
    private readonly string? _fromDriver;

    private readonly TextBox _txtFind = new();
    private readonly DriverList _lstDrivers = new();
    private readonly RadioButton _rdoInCar = new ThemedRadioButton();
    private readonly ComboBox _cmbCar = new();
    private readonly RadioButton _rdoMoves = new ThemedRadioButton();
    private readonly ComboBox _cmbReplaces = new();
    private readonly Label _lblOutcome = new() { AutoSize = true };
    private readonly Button _btnGive = new();

    /// <summary>The driver picked - their main transponder, the profile's key. Set on OK.</summary>
    public string DriverKey { get; private set; } = string.Empty;

    /// <summary>The car it is fitted to (Car.Id), or empty when it moves between cars. Set on OK.</summary>
    public string CarId { get; private set; } = string.Empty;

    /// <summary>The driver's transponder it replaces, to be retired, or empty. Set on OK.</summary>
    public string Replaces { get; private set; } = string.Empty;

    public BelongsToForm(string transponder, string whatItDid, ProfileStore profiles, CarStore cars, string? fromDriver = null)
    {
        _transponder = transponder;
        _profiles = profiles;
        _cars = cars;
        _fromDriver = string.IsNullOrWhiteSpace(fromDriver) ? null : fromDriver.Trim();

        Text = _fromDriver is null ? $"Who is on transponder {transponder}?" : $"Transfer transponder {transponder}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var lblWhat = new Label { Text = whatItDid, AutoSize = true };

        _txtFind.PlaceholderText = "name, number or transponder";
        _txtFind.TextChanged += (_, _) => FillDrivers();

        _lstDrivers.IntegralHeight = false;
        _lstDrivers.SelectedIndexChanged += (_, _) => OnDriverPicked();
        _lstDrivers.DoubleClick += (_, _) => Give();

        _rdoInCar.Text = "In one of their cars:";
        _rdoInCar.AutoSize = true;
        _cmbCar.DropDownStyle = ComboBoxStyle.DropDownList;
        // The two are in different containers (one sits beside its car
        // list), and WinForms only groups radio buttons that share one - so
        // each clears the other itself.
        _rdoInCar.CheckedChanged += (_, _) =>
        {
            _cmbCar.Enabled = _rdoInCar.Checked;

            if (_rdoInCar.Checked)
            {
                _rdoMoves.Checked = false;
            }
        };

        _rdoMoves.Text = "Their own transponder - it moves between cars";
        _rdoMoves.AutoSize = true;
        _rdoMoves.Checked = true;
        _rdoMoves.CheckedChanged += (_, _) =>
        {
            if (_rdoMoves.Checked)
            {
                _rdoInCar.Checked = false;
            }
        };

        _cmbReplaces.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbReplaces.SelectedIndexChanged += (_, _) => UpdateOutcome();

        _btnGive.Text = GiveCaption(null);
        _btnGive.Click += (_, _) => Give();

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // (18 Nov 2026, dialog pass) In two cards - the driver, and where it
        // goes - with what it did across the top and what pressing the button
        // will do over the buttons. Every control in the order it was, so Tab
        // goes round them as it did. The two ticks across the whole card, so
        // the car list sits beside its tick rather than under it.
        var where = new FieldGrid();
        where.AddWide(ButtonsBeside(_rdoInCar, _cmbCar), stretch: false);
        where.AddWide(_rdoMoves, stretch: false);
        where.AddRow("It replaces:", _cmbReplaces);
        where.AddHint("A replaced transponder is retired: it stays theirs, and so does everything timed on it.");

        var find = new FieldGrid();
        find.AddRow("Find:", _txtFind, stretch: true);

        var driverCard = new Section("The driver");
        driverCard.Add(find, gapBelow: UiLayout.RowGap);
        driverCard.AddFill(_lstDrivers, () => LeastRows * _lstDrivers.ItemHeight + 4, gapBelow: 0);

        var whereCard = new Section("Where is it?");
        whereCard.Add(where, gapBelow: 0);

        var page = new FillPage();
        page.Add(lblWhat, stretch: false, gapBelow: UiLayout.RowGap);
        page.AddFill(driverCard, driverCard.LeastHeight, gapBelow: UiLayout.RowGap);
        page.Add(whereCard, gapBelow: UiLayout.RowGap);
        page.Add(_lblOutcome, stretch: false);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, _btnGive), gapBelow: 0);

        Controls.Add(page);
        CancelButton = btnCancel;
        AcceptButton = _btnGive;

        FillDrivers();

        AppTheme.ApplyRecursively(this);
        _lblOutcome.ForeColor = AppTheme.TextMuted;

        // The window's own button in the app's colour, kept as its caption
        // changes with the driver picked.
        AppTheme.StyleButton(_btnGive, AppTheme.ButtonKind.Primary);
        _lstDrivers.Remeasure();

        // (22 Oct 2026, page tidy): the two grids had a caption column each,
        // so the Find box and "It replaces:" started at different places.
        // One column down both now, measured in the faces the theme pass left.
        FieldGrid.LineUp(new[] { find, where });

        // After the theme pass, in the faces it leaves them in.
        FitPickers();

        DialogFit.Keep(this, page, width: 600, height: 600);
    }

    /// <summary>One driver in the list.</summary>
    private sealed record DriverChoice(DriverProfile Profile, string Label, string Name, string Detail)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// The drivers, one to a row (18 Nov 2026, dialog pass): the name in
    /// bold, and their number, transponder and cars in grey after it, the
    /// picked row on the rounded band the app's other lists use. As many
    /// rows to the height as the plain list had, near enough - a row is a
    /// line of text and a little room round it.
    /// </summary>
    private sealed class DriverList : ListBox
    {
        public DriverList()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            IntegralHeight = false;
            Remeasure();
        }

        /// <summary>The rows as tall as a bold line and a little room, at the text size in use.</summary>
        public void Remeasure()
        {
            ItemHeight = Math.Min(255, AppTheme.BoldFont.Height + UiLayout.AtDpi(4));
            Invalidate();
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            using var buffer = BufferedGraphicsManager.Current.Allocate(e.Graphics, e.Bounds);
            DrawRow(buffer.Graphics, e);
            buffer.Render(e.Graphics);
        }

        private void DrawRow(Graphics g, DrawItemEventArgs e)
        {
            using (var back = new SolidBrush(AppTheme.Surface))
            {
                g.FillRectangle(back, e.Bounds);
            }

            if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not DriverChoice choice)
            {
                return;
            }

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var selected = (e.State & DrawItemState.Selected) != 0;
            var row = Rectangle.Inflate(e.Bounds, -UiLayout.AtDpi(2), -UiLayout.AtDpi(1));

            if (selected)
            {
                using var path = AppTheme.RoundedRect(new RectangleF(row.X + 0.5f, row.Y + 0.5f, row.Width - 1f, row.Height - 1f), UiLayout.AtDpi(6));
                using var fill = new SolidBrush(AppTheme.SecondaryButton);
                g.FillPath(fill, path);
                using var pen = new Pen(AppTheme.Primary, Math.Max(1f, UiLayout.AtDpi(1)));
                g.DrawPath(pen, path);
            }

            // TextRenderer ignores the buffer's translate unless told - see LayoutList.
            const TextFormatFlags line = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                                         | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
                                         | TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.PreserveGraphicsClipping;

            var pad = UiLayout.AtDpi(8);
            var room = Math.Max(1, row.Width - pad * 2);

            // The details as wide as they need, up to half the row; the name in what is left.
            var detailWidth = Math.Min(room / 2, TextRenderer.MeasureText(g, choice.Detail, AppTheme.SmallFont, Size.Empty, TextFormatFlags.NoPadding).Width);
            var nameWidth = Math.Max(1, room - detailWidth - pad);

            TextRenderer.DrawText(g, choice.Name, AppTheme.BoldFont,
                new Rectangle(row.X + pad, row.Y, nameWidth, row.Height), AppTheme.TextDark, line);

            TextRenderer.DrawText(g, choice.Detail, AppTheme.SmallFont,
                new Rectangle(row.Right - pad - detailWidth, row.Y, detailWidth, row.Height), AppTheme.TextMuted,
                line & ~TextFormatFlags.Left | TextFormatFlags.Right);
        }

        /// <summary>No flashing white between rows as the list redraws.</summary>
        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using var back = new SolidBrush(AppTheme.Surface);
            pevent.Graphics.FillRectangle(back, ClientRectangle);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }
    }

    /// <summary>One line of a drop-down with a value behind it.</summary>
    private sealed record Item(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    private static string NameOf(DriverProfile profile) =>
        string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();

    /// <summary>The drivers matching what is typed - by name, announcer name, number or any of their transponders.</summary>
    private void FillDrivers()
    {
        var find = _txtFind.Text.Trim();
        var picked = (_lstDrivers.SelectedItem as DriverChoice)?.Profile.Transponder;

        bool Has(string? value) => !string.IsNullOrEmpty(value) && value.Contains(find, StringComparison.OrdinalIgnoreCase);

        var matches = _profiles.All
            .Where(p => _fromDriver is null || !string.Equals(p.Transponder, _fromDriver, StringComparison.OrdinalIgnoreCase))
            .Where(p => find.Length == 0
                        || Has(p.DriverName) || Has(p.AnnouncerName) || Has(p.RaceNumber)
                        || p.AllTransponderIds.Any(Has))
            .OrderBy(NameOf, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _lstDrivers.BeginUpdate();
        _lstDrivers.Items.Clear();

        foreach (var profile in matches)
        {
            var number = string.IsNullOrWhiteSpace(profile.RaceNumber) ? string.Empty : $"  #{profile.RaceNumber.Trim()}";
            var cars = _cars.OwnedBy(profile).Count(c => !c.Retired);
            var carText = cars == 1 ? "1 car" : $"{cars} cars";
            var detail = (number.Length > 0 ? $"#{profile.RaceNumber.Trim()}  ·  " : string.Empty) + $"{profile.Transponder}  ·  {carText}";
            _lstDrivers.Items.Add(new DriverChoice(profile, $"{NameOf(profile)}{number}  ·  {profile.Transponder}  ·  {carText}", NameOf(profile), detail));
        }

        _lstDrivers.EndUpdate();

        var again = _lstDrivers.Items.Cast<DriverChoice>()
            .FirstOrDefault(c => string.Equals(c.Profile.Transponder, picked, StringComparison.OrdinalIgnoreCase));

        if (again is not null)
        {
            _lstDrivers.SelectedItem = again;
        }
        else if (_lstDrivers.Items.Count == 1)
        {
            _lstDrivers.SelectedIndex = 0;
        }

        OnDriverPicked();
    }

    /// <summary>Fills the car and "replaces" lists for the driver picked.</summary>
    /// <summary>The driver the car and "it replaces" boxes were last filled for - see OnDriverPicked.</summary>
    private string? _filledFor;

    /// <summary>The index of the item with this value in a box, or -1.</summary>
    private static int IndexOfValue(ComboBox box, string? value)
    {
        if (value is null)
        {
            return -1;
        }

        for (var i = 0; i < box.Items.Count; i++)
        {
            if (box.Items[i] is Item item && string.Equals(item.Value, value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private void OnDriverPicked()
    {
        var profile = (_lstDrivers.SelectedItem as DriverChoice)?.Profile;

        // The same driver as before - the list refilled by a letter typed in
        // Find, say: the car and "it replaces" stay as they were chosen. They
        // used to go back to the first of each on every keystroke, without a
        // word, and the transponder was then given to the wrong car.
        var sameDriver = profile is not null && string.Equals(profile.Transponder, _filledFor, StringComparison.OrdinalIgnoreCase);
        var carBefore = sameDriver ? (_cmbCar.SelectedItem as Item)?.Value : null;
        var replacesBefore = sameDriver ? (_cmbReplaces.SelectedItem as Item)?.Value : null;
        _filledFor = profile?.Transponder;

        _cmbCar.Items.Clear();
        _cmbReplaces.Items.Clear();
        _cmbReplaces.Items.Add(new Item(string.Empty, "Nothing - it's an extra one"));

        if (profile is not null)
        {
            foreach (var car in _cars.OwnedBy(profile).Where(c => !c.Retired))
            {
                _cmbCar.Items.Add(new Item(car.Id, car.DisplayName));
            }

            foreach (var id in profile.AllTransponderIds.Where(id => !profile.IsRetired(id)))
            {
                _cmbReplaces.Items.Add(new Item(id, id));
            }
        }

        _rdoInCar.Enabled = _cmbCar.Items.Count > 0;

        if (_cmbCar.Items.Count > 0)
        {
            _cmbCar.SelectedIndex = Math.Max(0, IndexOfValue(_cmbCar, carBefore));
        }
        else
        {
            _rdoMoves.Checked = true;
        }

        _cmbCar.Enabled = _rdoInCar.Checked && _cmbCar.Items.Count > 0;
        _cmbReplaces.SelectedIndex = Math.Max(0, IndexOfValue(_cmbReplaces, replacesBefore));

        _btnGive.Enabled = profile is not null;
        _btnGive.Text = GiveCaption(profile);

        FitPickers();
        UpdateOutcome();
    }

    /// <summary>
    /// The car and "it replaces" drop-downs as wide as their longest
    /// choice, between the 220px they were made at and a width that still
    /// fits the window. (22 Oct 2026, page tidy): they were a fixed 220px,
    /// and a long car name was cut short.
    /// </summary>
    private void FitPickers()
    {
        foreach (var picker in new[] { _cmbCar, _cmbReplaces })
        {
            (Font Font, int Count, int Width)? measured = null;

            var width = Math.Clamp(Pieces.PickerWidth(picker, ref measured), UiLayout.AtTextSize(220), UiLayout.AtTextSize(340));

            if (picker.Width != width)
            {
                picker.Width = width;
            }
        }
    }

    /// <summary>The line saying what pressing the button will do.</summary>
    private void UpdateOutcome()
    {
        var profile = (_lstDrivers.SelectedItem as DriverChoice)?.Profile;

        if (profile is null)
        {
            _lblOutcome.Text = "Pick the driver it belongs to.";
            return;
        }

        var replaces = (_cmbReplaces.SelectedItem as Item)?.Value ?? string.Empty;

        _lblOutcome.Text = OutcomeText(_transponder, transfer: _fromDriver is not null, NameOf(profile), replaces);
    }

    /// <summary>The outcome line for <paramref name="transponder"/> given (or, for a transfer, transferred) to <paramref name="driverName"/>, retiring <paramref name="replaces"/> (empty: none).</summary>
    internal static string OutcomeText(string transponder, bool transfer, string driverName, string replaces)
    {
        var retire = replaces.Length > 0 ? $" {replaces} is retired." : string.Empty;

        // A transfer's line used to end "Races already run stay with whoever
        // ran them", which the lead above it says isn't so for a race where it
        // was timed as a car of its own (TransponderTransferring).
        return !transfer
            ? $"Laps timed on {transponder} will count for {driverName}.{retire}"
            : $"{transponder} becomes {driverName}'s, and their laps on it count for them. "
              + $"Races already run where it was timed as a car of its own count for them too.{retire}";
    }

    /// <summary>The button's caption: Give for a transponder nobody has, Transfer for one somebody does.</summary>
    private string GiveCaption(DriverProfile? profile) =>
        _fromDriver is null
            ? profile is null ? "Give to Driver" : $"Give {_transponder} to {NameOf(profile)}"
            : profile is null ? "Transfer" : $"Transfer {_transponder} to {NameOf(profile)}";

    private void Give()
    {
        if (_lstDrivers.SelectedItem is not DriverChoice choice)
        {
            return;
        }

        DriverKey = choice.Profile.Transponder;
        CarId = _rdoInCar.Checked ? (_cmbCar.SelectedItem as Item)?.Value ?? string.Empty : string.Empty;
        Replaces = (_cmbReplaces.SelectedItem as Item)?.Value ?? string.Empty;

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>A control and a drop-down beside it, the drop-down centred up and down on it.</summary>
    private static ButtonBar ButtonsBeside(Control first, ComboBox picker)
    {
        picker.Anchor = AnchorStyles.Left;
        first.Anchor = AnchorStyles.Left;
        return new ButtonBar(tight: true).With(first, picker);
    }
}
