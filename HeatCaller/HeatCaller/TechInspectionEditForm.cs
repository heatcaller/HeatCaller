using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Records one car's inspection against its class checklist.
///
/// Each check has three states rather than a tickbox, because "not looked
/// at" and "looked at and failed" are different things and collapsing them
/// would make an incomplete inspection read as a clean one.
///
/// On the layout pieces (DialogFit), in cards with a line each on what they
/// are for, in two columns (18 Nov 2026, dialog jazz-up - it was one column):
/// on the left the driver, class and car lined up in a field grid, and the
/// checklist taking the height the rest leave - a line per check, what is
/// checked wrapping to the room left of its drop-down, red when it is FAIL -
/// scrolling on its own when the class has more checks than the window
/// shows; on the right tiles counting the checks OK, FAIL and to do, the
/// verdict (or the result picked by hand) and the inspector, their captions
/// in one column with the car's, and the notes taking the height; and Save
/// and Cancel at the foot. Before that it was one column, and it was placed
/// in pixels in a 520x600 window: every field at a fixed 110px, which the
/// captions ran into at a larger text size; the checks 32px apart, what is
/// checked wrapped at 300px beside a drop-down at 320px; and the verdict,
/// the inspector, the notes and the buttons anchored at fixed distances
/// from the foot.
/// </summary>
public sealed class TechInspectionEditForm : Form
{
    private const string StateUnchecked = "not checked";
    private const string StatePassed = "OK";
    private const string StateFailed = "FAIL";

    /// <summary>
    /// The window's inside at the normal text size. It opens this big at
    /// the text size in use - wider when its fields need more across, never
    /// taller than the screen - and can be made bigger, the checklist taking
    /// the height.
    ///
    /// The six checks Add Common gives a class must all show without
    /// scrolling, at 1920x1080 and at 1366x768 (layout pictures, 30 Sep). In
    /// two columns (18 Nov 2026) the checklist has the left one under the
    /// car to itself, so it is wider and no taller than the 640 it was.
    /// </summary>
    private const int DialogWidth = 900;
    private const int DialogHeight = 600;

    /// <summary>The right-hand column - the tiles, the result and the notes - at the normal text size.</summary>
    private const int SideWidth = 340;

    /// <summary>The field column at the normal text size: the driver, the car, the inspector and the notes all ran this wide.</summary>
    private const int FieldColumnWidth = 380;

    /// <summary>A check's drop-down at the normal text size.</summary>
    private const int StateWidth = 120;

    /// <summary>The checklist shows at least this many checks before the window scrolls instead.</summary>
    private const int LeastChecks = 3;

    /// <summary>Lines of notes the box shows before it scrolls.</summary>
    private const int NotesLines = 3;

    /// <summary>
    /// Holds the layout pieces' layouts while the window is built, let go
    /// at the end of the constructor (LayoutFreeze.HoldWhileBuilding) -
    /// first among the fields, so the checklist's PageStack below is made
    /// under it too.
    ///
    /// Why (19 Nov 2026): built without one, every check line added laid the
    /// checklist, its card and the page out again, and 18 of the window's
    /// parts made their windows before it was shown - about a tenth of a
    /// second to open one with a filled-in checklist.
    /// </summary>
    private readonly IDisposable _building = LayoutFreeze.HoldWhileBuilding();

    private readonly ProfileStore _profiles;
    private readonly RaceClassStore _classes;

    private readonly ComboBox _cmbDriver = new();
    private readonly ComboBox _cmbClass = new();
    private readonly TextBox _txtCar = new();
    private readonly TextBox _txtInspector = new();
    private readonly TextBox _txtNotes = new();

    /// <summary>The checklist, a line per check, scrolling on its own - see BuildChecklist.</summary>
    private readonly PageStack _checksPanel = new();

    /// <summary>What the checklist says when there is nothing on it to check.</summary>
    private readonly Label _lblNoChecklist = new();

    /// <summary>Every line the checklist has made, in order, the ones past the selected class's checks hidden - see BuildChecklist.</summary>
    private readonly List<CheckRow> _checkRows = new();

    /// <summary>What the checks' drop-downs were last measured at - see StateColumnWidth.</summary>
    private (Font Font, int Count, int Width)? _stateMeasured;

    private readonly Label _lblVerdict = new();

    /// <summary>
    /// The checklist at a glance, over the result: how many checks are OK,
    /// FAIL and still to look at - following the drop-downs (ShowTiles).
    /// Read only.
    /// </summary>
    private readonly StatTile[] _tiles = { new(), new(), new() };

    private readonly Dictionary<string, ComboBox> _checkControls = new();

    /// <summary>
    /// Checks this inspection recorded that are no longer on the class's
    /// checklist. Shown so they can be seen and changed - dropping them
    /// silently is how a failed car used to come back as passed.
    /// </summary>
    private readonly HashSet<string> _staleItems = new(StringComparer.Ordinal);

    /// <summary>
    /// The verdict by hand, for when there is no checklist to decide it.
    /// Before this existed an inspection with no checklist could only ever
    /// be saved as Passed - a car could not be failed at all, and editing a
    /// failed one turned it into a pass.
    /// </summary>
    private readonly ComboBox _cmbManualVerdict = new();

    /// <summary>Its caption, the field grid's own (FieldGrid.AddRow), hidden and shown with it.</summary>
    private readonly Label _lblManualVerdict;

    /// <summary>The stored inspection as it was before this dialog, or null for a new one. Never written to.</summary>
    private readonly TechInspection? _original;

    /// <summary>
    /// What Save hands back. A COPY of the stored inspection when editing,
    /// never the stored object itself: the callers take their undo snapshot
    /// after this dialog closes, and when the dialog wrote straight onto the
    /// stored record that snapshot already held the edit, so Undo restored
    /// the edited version and did nothing.
    /// </summary>
    public TechInspection Result { get; private set; }

    /// <summary>The class a new inspection opens on instead of the driver's main one - see FillFields.</summary>
    private readonly string? _presetClass;

    public TechInspectionEditForm(TechInspection? existing, ProfileStore profiles, RaceClassStore classes,
        string? presetTransponder = null, string? presetClass = null)
    {
        using var building = _building;

        _profiles = profiles;
        _classes = classes;
        _presetClass = presetClass;

        // presetTransponder only applies when this is genuinely a NEW
        // inspection - it pre-selects who it's for (jumping in from
        // Check-In, say) without turning the dialog into an edit of a
        // record that doesn't exist yet.
        _original = existing;
        Result = existing is null
            ? new TechInspection { Transponder = presetTransponder ?? string.Empty }
            : JsonSerializer.Deserialize<TechInspection>(JsonSerializer.Serialize(existing)) ?? existing;

        Text = existing is null ? "New Tech Inspection" : $"Edit Inspection - {Result.DriverName}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        FillFields();

        // The checklist scrolls down on its own, its lines as wide as the
        // fields above and below it: no margins of its own - the page's are
        // round it - and no room under its last line, which the theme pass
        // gives a panel that scrolls (see ScrollTheming.NoRoomBelow).
        _checksPanel.Padding = Padding.Empty;
        ScrollTheming.NoRoomBelow(_checksPanel);

        // First in it, over the lines, for a class with nothing to check -
        // see BuildChecklist.
        _lblNoChecklist.AutoSize = true;
        _lblNoChecklist.ForeColor = AppTheme.TextMuted;
        _checksPanel.Add(_lblNoChecklist, gapBelow: UiLayout.RowGap);

        _lblVerdict.AutoSize = true;
        _lblVerdict.Font = AppTheme.BoldFont;

        var btnSave = new Button { Text = "Save" };
        btnSave.Click += BtnSave_Click;

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // Who, in what, and the car - the driver and the car running to the
        // card's right-hand edge, as they always ran to the window's.
        var fields = new FieldGrid();
        var captions = new List<Label>
        {
            fields.AddRow("Driver:", _cmbDriver, stretch: true),
            fields.AddRow("Class:", _cmbClass),
            fields.AddRow("Car:", _txtCar, stretch: true),
        };

        // What the checklist comes to: the verdict it gives, over the grid
        // (in the card - see below), or - when there is no checklist to give
        // one - the result picked by hand, in the same place
        // (ShowManualVerdict). Then who inspected the car. The notes have a
        // card of their own.
        var outcome = new FieldGrid();

        _lblManualVerdict = outcome.AddRow("Result:", _cmbManualVerdict);
        captions.Add(_lblManualVerdict);
        captions.Add(outcome.AddRow("Inspector:", _txtInspector, stretch: true));

        // THE LOOK (18 Nov 2026, dialog jazz-up): cards, each with a line on
        // what it is for, in two columns so the whole inspection is in view
        // at once at the pit table. On the left the car, then the checklist
        // taking the height; on the right the count of checks OK, FAIL and
        // still to look at - big, following the drop-downs (ShowTiles) - then
        // the result and the notes, the notes taking the height. It was one
        // column, the checklist under a bold heading. The dialog is built at
        // the text size in use, so the tiles take their faces here.
        foreach (var tile in _tiles)
        {
            tile.Value.Font = AppTheme.TitleFont;
            tile.Caption.Font = AppTheme.MicroFont;
        }

        var carCard = new Section("Car");
        carCard.Add(CardHint("Who is racing what. The class picks the checklist."), gapBelow: UiLayout.RowGap);
        carCard.Add(fields, gapBelow: 0);

        // The checklist takes the card's height, and the card the column's;
        // it keeps a few checks' worth - a drop-down and the gap under it for
        // each - before the window scrolls instead.
        var checksCard = new Section("Checklist");
        checksCard.Add(CardHint("Set each check to OK or FAIL. One FAIL fails the car."), gapBelow: UiLayout.RowGap);
        checksCard.AddFill(_checksPanel, () => LeastChecks * (_cmbManualVerdict.Height + UiLayout.RowGap), gapBelow: 0);

        var resultCard = new Section("Result");
        resultCard.Add(CardHint("What the car comes out as, and who looked."), gapBelow: UiLayout.RowGap);

        // The verdict in the card rather than across the grid: hidden, a
        // card leaves its row out, where the grid, measured in a card in a
        // column, kept a blank line's room for it over the result picked by
        // hand (layout pictures, 18 Nov 2026).
        resultCard.Add(_lblVerdict, gapBelow: UiLayout.RowGap);
        resultCard.Add(outcome, gapBelow: 0);

        // The notes fill what the right-hand column has left, never fewer
        // than a few lines, as the box draws them (Pieces.LineHeight).
        var notesCard = new Section("Notes");
        notesCard.Add(CardHint("What to fix, and anything to check again later."), gapBelow: UiLayout.RowGap);
        notesCard.AddFill(_txtNotes, () => NotesLines * Pieces.LineHeight(_txtNotes.Font) + UiLayout.HintGap * 2, gapBelow: 0);

        // The right-hand column's width, worked out once the theme pass has
        // set the faces - see below.
        var sideWidth = 0;

        var columns = new Beside(() => sideWidth);
        columns.Add(carCard, gapBelow: UiLayout.RowGap);
        columns.AddFill(checksCard, checksCard.LeastHeight);
        columns.AddSideMeasured(new TileStrip(_tiles), gapBelow: UiLayout.RowGap);
        columns.AddSideCard(resultCard, gapBelow: UiLayout.RowGap);
        columns.AddSideFill(notesCard, notesCard.LeastHeight, gapBelow: 0);

        var page = new FillPage();
        page.AddFill(columns, columns.LeastHeight, gapBelow: UiLayout.RowGap);

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
        AppTheme.StyleButton(btnSave, AppTheme.ButtonKind.Primary);

        // After the theme pass: the lines are themed as they are made, and
        // measured in the face it leaves them in (AddCheckRow).
        BuildChecklist();

        // The captions in one column down both grids, as they were, so the
        // two cards' fields start the same distance in.
        FieldGrid.LineUp(new[] { fields, outcome });

        // The class and the result as wide as they were at the normal text
        // size, at the size in use - or as their longest choice, which a
        // long class name, or one since deleted, can make wider.
        (Font Font, int Count, int Width)? classMeasured = null;
        (Font Font, int Count, int Width)? verdictMeasured = null;
        (Font Font, int Count, int Width)? driverMeasured = null;

        var classWidth = Math.Max(UiLayout.AtTextSize(UiLayout.FieldWidth), Pieces.PickerWidth(_cmbClass, ref classMeasured));
        var verdictWidth = Math.Max(UiLayout.AtTextSize(UiLayout.FieldWidth), Pieces.PickerWidth(_cmbManualVerdict, ref verdictMeasured));

        _cmbClass.Width = classWidth;
        _cmbManualVerdict.Width = verdictWidth;

        // The left-hand column as wide as the car's fields need: never
        // narrower than the 380px the driver and the car ran to, and wide
        // enough for the longest driver's name and transponder and the
        // class, beside the caption column as the grids work it out - the
        // widest caption and the gap after it - inside its card. The checks
        // wrap to whatever width that comes to.
        var fieldColumn = Math.Max(
            Math.Max(UiLayout.AtTextSize(FieldColumnWidth), Pieces.PickerWidth(_cmbDriver, ref driverMeasured)),
            classWidth);

        var captionColumn = captions.Max(caption => caption.GetPreferredSize(Size.Empty).Width + caption.Margin.Horizontal);

        // The right-hand column as wide as it is at the normal text size, at
        // the size in use - or as the result picked by hand needs beside its
        // caption, inside its card.
        sideWidth = Math.Max(UiLayout.AtTextSize(SideWidth), Section.Inset.Horizontal + captionColumn + verdictWidth);

        var width = Math.Max(
            UiLayout.AtTextSize(DialogWidth),
            UiLayout.PageLeft + Section.Inset.Horizontal + captionColumn + fieldColumn + UiLayout.RowGap + sideWidth + UiLayout.PageRight);

        DialogFit.Keep(this, page, width, height: DialogHeight, widthMeasured: true);
    }

    /// <summary>
    /// The fields filled in from the inspection, and the handlers that keep
    /// the class and the checklist following the driver - hooked after the
    /// first choices are made, so making them changes nothing else.
    /// </summary>
    private void FillFields()
    {
        _cmbDriver.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var profile in _profiles.All.OrderBy(p => p.DriverName))
        {
            _cmbDriver.Items.Add(new DriverItem(profile));
        }

        // A driver who is no longer in the list is kept as a choice, the
        // way a deleted class is below. An inspection outlives its driver's
        // profile (deleting one keeps their inspections, to match again if
        // they come back) and a change of transponder does not move them
        // either. Selecting nobody fell back to the first driver in the
        // list, without a word: correcting a note on Zed's old inspection
        // and pressing Save handed it to Aaron, re-stamped at today's race
        // day, so Aaron counted as through tech with a car nobody had looked
        // at and Zed's record was gone. The stand-in carries only the
        // transponder and name the inspection was saved with, so saving it
        // writes back exactly those and is not counted as a re-inspection.
        if (!string.IsNullOrWhiteSpace(Result.Transponder) && !HasDriver(Result.Transponder))
        {
            _cmbDriver.Items.Add(new DriverItem(
                new DriverProfile { Transponder = Result.Transponder, DriverName = Result.DriverName },
                NoProfile: true));
        }

        SelectDriver(Result.Transponder);
        _cmbDriver.SelectedIndexChanged += (_, _) => OnDriverChanged();

        _cmbClass.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var raceClass in _classes.All)
        {
            _cmbClass.Items.Add(raceClass.Name);
        }
        if (!string.IsNullOrWhiteSpace(Result.ClassName))
        {
            // A class that has since been deleted is kept as a choice rather
            // than dropped: selecting a name that is not in the list selects
            // nothing, and Save then wiped the inspection's class.
            var match = _cmbClass.Items.Cast<object>()
                .OfType<string>()
                .FirstOrDefault(n => string.Equals(n, Result.ClassName, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                _cmbClass.Items.Add(Result.ClassName);
                match = Result.ClassName;
            }

            _cmbClass.SelectedItem = match;
        }

        _txtCar.Text = Result.CarModel;

        // A new inspection takes its class and car from the driver it opens
        // on, as picking a driver does. The driver is chosen above before
        // OnDriverChanged is hooked (and before there are classes to pick),
        // so one opened from Check-In came up with no class: its pass was
        // saved against no class and never cleared the car. Before the
        // class box's own handler is hooked - the constructor builds the
        // checklist once everything is in place.
        if (_original is null)
        {
            OnDriverChanged();

            // Then the class Check-In is asking about, when it is not their
            // main one - a driver through tech in Stock still to be inspected
            // in Modified. After OnDriverChanged, which picks the main class.
            var preset = _cmbClass.Items.Cast<object>()
                .OfType<string>()
                .FirstOrDefault(n => string.Equals(n, _presetClass, StringComparison.OrdinalIgnoreCase));

            if (preset is not null)
            {
                _cmbClass.SelectedItem = preset;
            }
        }

        _cmbClass.SelectedIndexChanged += (_, _) => BuildChecklist();

        _cmbManualVerdict.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbManualVerdict.Items.AddRange(new object[] { VerdictPassed, VerdictPassedWithNote, VerdictFailed });

        // An existing inspection keeps the result it was saved with until
        // somebody changes it here; a new one starts at a pass, as it
        // always did.
        _cmbManualVerdict.SelectedItem = Result.Result switch
        {
            TechResult.Failed => VerdictFailed,
            TechResult.PassedWithNote => VerdictPassedWithNote,
            _ => VerdictPassed,
        };

        _txtInspector.Text = Result.Inspector;

        _txtNotes.Multiline = true;

        // Enter starts a new line, as in every other notes box. Without
        // this it pressed Save, part-way through a note (bug hunt 17).
        _txtNotes.AcceptsReturn = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        _txtNotes.Text = Result.Notes;
    }

    /// <summary>
    /// What a check starts at with this class picked: as the inspection
    /// recorded it, but only in the class it was recorded in.
    ///
    /// Every check used to be filled in from the inspection whatever class
    /// was picked. Classes sharing check names (Add Common gives them all
    /// the same six) came up with the other car's ticks already on, so one
    /// click of Save passed a car nobody had looked at.
    /// </summary>
    internal static string StartingState(TechInspection inspection, string? className, string item)
    {
        if (className is null || !string.Equals(className, inspection.ClassName, StringComparison.OrdinalIgnoreCase))
        {
            return StateUnchecked;
        }

        return inspection.ItemsFailed.Contains(item) ? StateFailed
            : inspection.ItemsPassed.Contains(item) ? StatePassed
            : StateUnchecked;
    }

    /// <summary>Whether saving takes an inspection already on file out of its class and into another one.</summary>
    internal static bool MovesToAnotherClass(TechInspection? original, string? className) =>
        original is { ClassName.Length: > 0 }
        && !string.IsNullOrEmpty(className)
        && !string.Equals(className, original.ClassName, StringComparison.OrdinalIgnoreCase);

    private bool HasDriver(string transponder) =>
        _cmbDriver.Items.Cast<object>().OfType<DriverItem>()
            .Any(item => string.Equals(item.Profile.Transponder, transponder, StringComparison.OrdinalIgnoreCase));

    private void SelectDriver(string transponder)
    {
        for (var i = 0; i < _cmbDriver.Items.Count; i++)
        {
            if (_cmbDriver.Items[i] is DriverItem item
                && string.Equals(item.Profile.Transponder, transponder, StringComparison.OrdinalIgnoreCase))
            {
                _cmbDriver.SelectedIndex = i;
                return;
            }
        }

        if (_cmbDriver.Items.Count > 0)
        {
            _cmbDriver.SelectedIndex = 0;
        }
    }

    /// <summary>Pre-fills class and car from the driver's profile - almost always right, and still editable.</summary>
    private void OnDriverChanged()
    {
        if (_cmbDriver.SelectedItem is not DriverItem item)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(item.Profile.ClassName) && _cmbClass.Items.Contains(item.Profile.ClassName))
        {
            _cmbClass.SelectedItem = item.Profile.ClassName;
        }

        if (string.IsNullOrWhiteSpace(_txtCar.Text))
        {
            _txtCar.Text = item.Profile.CarModel;
        }
    }

    /// <summary>
    /// Shows the selected class's configured items, a line each.
    ///
    /// The lines are kept, and handed the new class's checks in order, the
    /// ones left over hidden; a line is made only when a class has more
    /// checks than any before it (AddCheckRow). They used to be made afresh
    /// on every change of class, the last set taken out and disposed - taken
    /// out alone, every set was leaked, each control holding a window
    /// handle - and a scrutineer working down a queue, switching class per
    /// car, does this a hundred times in an evening.
    /// </summary>
    private void BuildChecklist()
    {
        _checkControls.Clear();
        _staleItems.Clear();

        var className = _cmbClass.SelectedItem as string;
        var raceClass = className is null
            ? null
            : _classes.All.FirstOrDefault(rc => string.Equals(rc.Name, className, StringComparison.OrdinalIgnoreCase));

        // WHY (23 Oct 2026, bug hunt 10): a check listed twice word for word
        // (Add Common, then the same check typed by hand) drew two rows but
        // kept only the second's drop-down - a FAIL on the first was never
        // counted and the car came out cleared. One row per check now, matched
        // exactly as _checkControls and the saved ItemsPassed/ItemsFailed are.
        var items = (raceClass?.TechCheckItems ?? new List<string>()).Distinct(StringComparer.Ordinal).ToList();

        // Checks this inspection recorded against ITS class that the class's
        // checklist no longer has. Only for the class it was recorded in -
        // under another class they would be somebody else's rules.
        if (className is not null && string.Equals(className, Result.ClassName, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var recorded in Result.ItemsFailed.Concat(Result.ItemsPassed))
            {
                if (!items.Contains(recorded) && _staleItems.Add(recorded))
                {
                    items.Add(recorded);
                }
            }
        }

        ShowManualVerdict(items.Count == 0);

        // A new class's checks from its first, wherever the last class's
        // were scrolled to.
        _checksPanel.AutoScrollPosition = Point.Empty;

        if (items.Count == 0)
        {
            _lblNoChecklist.Text = className is null
                ? "Pick a class to see its checklist."
                : $"{className} has no checklist set. Add one on the Classes page, or just pick the result on the right.";
        }

        _lblNoChecklist.Visible = items.Count == 0;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var row = i < _checkRows.Count ? _checkRows[i] : AddCheckRow();

            row.Item.Text = _staleItems.Contains(item) ? $"{item}  (no longer on the checklist)" : item;

            row.State.SelectedItem = StartingState(Result, className, item);

            row.Line.Visible = true;

            // A field row's gap between one check and the next, and none
            // under the last: the list scrolled by it. At 1366x768, where the
            // window is a few pixels shorter, six checks that fitted showed a
            // scrollbar for a sliver of nothing (layout pictures, 30 Sep).
            var gapBelow = i == items.Count - 1 ? 0 : UiLayout.RowGap;

            if (row.Line.Margin.Bottom != gapBelow)
            {
                row.Line.Margin = new Padding(0, 0, 0, gapBelow);
            }

            _checkControls[item] = row.State;
        }

        for (var i = items.Count; i < _checkRows.Count; i++)
        {
            _checkRows[i].Line.Visible = false;
        }

        UpdateVerdict();
    }

    /// <summary>
    /// A new line at the end of the checklist: what is checked, wrapping to
    /// the room left of its drop-down, and the drop-down against the
    /// right-hand edge - a Beside, the drop-down its column. Themed here as
    /// the rest of the window was: a line can be made after the window's own
    /// theme pass, when a class has more checks than any before it.
    /// </summary>
    private CheckRow AddCheckRow()
    {
        var item = new Label { AutoSize = true };

        var state = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        state.Items.AddRange(new object[] { StateUnchecked, StatePassed, StateFailed });
        state.SelectedIndexChanged += (_, _) => UpdateVerdict();

        var line = new Beside(() => StateColumnWidth(state))
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        line.Add(item, gapBelow: 0);
        line.AddSide(state, gapBelow: 0);

        // The line is as tall as the drop-down's own height when it is
        // placed, and a drop-down settles its height by itself once it has
        // a window - which lays nothing out. Told, it lays the line out
        // again at the height it has (see Pieces.KeepHeightTold).
        Pieces.KeepHeightTold(state);

        _checksPanel.Add(line, gapBelow: UiLayout.RowGap);

        AppTheme.ApplyRecursively(line);

        // What is checked level with the drop-down's text while it is one
        // line: down by half what the drop-down is taller than a line of it,
        // as the label draws one (Pieces.LineHeight). Again whenever the
        // drop-down's height changes - it settles on its own once it is on
        // screen.
        void Level()
        {
            var top = Math.Max(0, (state.Height - Pieces.LineHeight(item.Font)) / 2);

            if (item.Margin.Top != top)
            {
                item.Margin = new Padding(0, top, 0, 0);
            }
        }

        Level();
        state.SizeChanged += (_, _) => Level();

        // A failed check's words in red, so the ones to fix stand out down
        // the list (18 Nov 2026, dialog jazz-up). After the theme pass on
        // the line, which would put them back.
        var plain = item.ForeColor;
        state.SelectedIndexChanged += (_, _) =>
            item.ForeColor = (string?)state.SelectedItem == StateFailed ? AppTheme.Danger : plain;

        var row = new CheckRow(line, item, state);
        _checkRows.Add(row);

        return row;
    }

    /// <summary>
    /// How wide a check's drop-down is: as wide as it was at the normal text
    /// size, at the size in use, or as its longest state needs if that is
    /// more. Every one holds the same three states in the same face, so they
    /// share one measurement.
    /// </summary>
    private int StateColumnWidth(ComboBox state) =>
        Math.Max(UiLayout.AtTextSize(StateWidth), Pieces.PickerWidth(state, ref _stateMeasured));

    /// <summary>
    /// The verdict follows the checks: any failure fails the car, and an
    /// incomplete checklist is called out rather than quietly passing.
    /// </summary>
    private void UpdateVerdict()
    {
        var failed = _checkControls.Count(kv => (string?)kv.Value.SelectedItem == StateFailed);

        // A check that is no longer on the list and has been set back to
        // "not checked" has been taken off this inspection, not left to do.
        var unchecked_ = _checkControls.Count(kv => (string?)kv.Value.SelectedItem == StateUnchecked && !_staleItems.Contains(kv.Key));

        if (failed > 0)
        {
            _lblVerdict.Text = $"FAILS - {Plural.Of(failed, "check")} failed.";
            _lblVerdict.ForeColor = AppTheme.Danger;
        }
        else if (unchecked_ > 0)
        {
            _lblVerdict.Text = $"{Plural.Of(unchecked_, "check")} not looked at yet.";
            _lblVerdict.ForeColor = AppTheme.Accent;
        }
        else if (_checkControls.Count > 0)
        {
            _lblVerdict.Text = "All checks passed.";
            _lblVerdict.ForeColor = AppTheme.Success;
        }
        else
        {
            _lblVerdict.Text = string.Empty;
        }

        ShowTiles();
    }

    /// <summary>
    /// The tiles across the right-hand column: how many checks are OK, FAIL
    /// and not looked at yet, as the drop-downs say - the FAIL count red
    /// when there is one, the ones to do orange. With no checklist there is
    /// nothing to count, and they say so.
    /// </summary>
    private void ShowTiles()
    {
        var counting = _checkControls.Count > 0;

        int Count(string state) => _checkControls.Count(kv => (string?)kv.Value.SelectedItem == state
            && (state != StateUnchecked || !_staleItems.Contains(kv.Key)));

        var ok = Count(StatePassed);
        var failed = Count(StateFailed);
        var toDo = Count(StateUnchecked);

        string Shown(int n) => counting ? n.ToString(System.Globalization.CultureInfo.CurrentCulture) : "-";

        _tiles[0].Value.ForeColor = counting && ok > 0 ? AppTheme.Success : AppTheme.TextDark;
        _tiles[0].Set(Shown(ok), "OK");

        _tiles[1].Value.ForeColor = failed > 0 ? AppTheme.Danger : AppTheme.TextDark;
        _tiles[1].Set(Shown(failed), "Fail");

        _tiles[2].Value.ForeColor = toDo > 0 ? AppTheme.Accent : AppTheme.TextDark;
        _tiles[2].Set(Shown(toDo), "To check");
    }

    /// <summary>A card's line saying what it is for, in the small muted face.</summary>
    private static Label CardHint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = AppTheme.TextMuted,
        Font = AppTheme.SmallFont,
    };

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (_cmbDriver.SelectedItem is not DriverItem driver)
        {
            AppDialog.Show(this, "Pick a driver.", "No driver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // An inspection is matched to a car by its class, so one with no
        // class clears the car for nothing. Asked rather than refused: a
        // club with classes that don't run tech may still note a look-over.
        if (_cmbClass.SelectedItem is not string { Length: > 0 } && _cmbClass.Items.Count > 0)
        {
            var noClass = AppDialog.Show(this,
                "No class is picked. An inspection with no class doesn't count as tech for any class, "
                + "so this car will still show as not inspected.\r\n\r\nSave it anyway?",
                "No class", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (noClass != DialogResult.Yes)
            {
                return;
            }
        }

        // An inspection already on file, saved under another class, MOVES
        // there - how the Scrutineering log fixes one filed under the wrong
        // class - and stops counting for the class it was in. Asked, because
        // nothing on screen said so, and re-checking a driver's other car
        // this way quietly lost the first car's pass.
        if (MovesToAnotherClass(_original, _cmbClass.SelectedItem as string))
        {
            var move = AppDialog.Show(this,
                $"This moves the inspection from {_original!.ClassName} to {_cmbClass.SelectedItem}, "
                + $"so it no longer counts for {_original.ClassName}.\r\n\r\nSave it anyway?",
                "Move inspection", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (move != DialogResult.Yes)
            {
                return;
            }
        }

        var passed = _checkControls.Where(kv => (string?)kv.Value.SelectedItem == StatePassed).Select(kv => kv.Key).ToList();
        var failed = _checkControls.Where(kv => (string?)kv.Value.SelectedItem == StateFailed).Select(kv => kv.Key).ToList();
        var notChecked = _checkControls.Count(kv => (string?)kv.Value.SelectedItem == StateUnchecked && !_staleItems.Contains(kv.Key));

        // A car failed BY HAND - recorded as FAILED with no check on a list
        // saying why, because its class had no checklist then. If the class
        // has gained one since, the hand-picked result is not on screen, and
        // saving a corrected note used to turn the failure into a pass on
        // the strength of a checklist nobody had filled in. Asked rather
        // than assumed: the inspector may have re-checked the car and it
        // now passes.
        var keepHandFailure = false;

        if (_checkControls.Count > 0
            && failed.Count == 0
            && _original is { Result: TechResult.Failed, ItemsFailed.Count: 0 })
        {
            var keepAnswer = AppDialog.Show(this,
                "This car was recorded as FAILED, but no check on the list is marked FAIL.\r\n\r\n"
                + "Yes  - keep it FAILED\r\n"
                + "No   - record the result of the checklist instead\r\n"
                + "Cancel - go back to the checklist",
                "Car was failed", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (keepAnswer == DialogResult.Cancel)
            {
                return;
            }

            keepHandFailure = keepAnswer == DialogResult.Yes;
        }

        TechResult verdict;
        if (_checkControls.Count == 0)
        {
            // No checklist, so nothing on it can decide: the result is the
            // one picked by hand, which on an edit starts as the stored one.
            verdict = _cmbManualVerdict.SelectedItem switch
            {
                VerdictFailed => TechResult.Failed,
                VerdictPassedWithNote => TechResult.PassedWithNote,
                _ => TechResult.Passed,
            };
        }
        else if (failed.Count > 0)
        {
            verdict = TechResult.Failed;
        }
        else if (keepHandFailure)
        {
            verdict = TechResult.Failed;
        }
        else if (notChecked > 0)
        {
            // An incomplete checklist shouldn't silently become a clean
            // pass - confirm what's intended rather than guessing.
            var answer = AppDialog.Show(this,
                $"{Plural.Of(notChecked, "check")} {(notChecked == 1 ? "hasn't" : "haven't")} been looked at.\r\n\r\n"
                + "Yes  - record as passed with a note\r\n"
                + "No   - record as a full pass anyway\r\n"
                + "Cancel - go back and finish the checklist",
                "Incomplete checklist", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (answer == DialogResult.Cancel)
            {
                return;
            }

            verdict = answer == DialogResult.Yes ? TechResult.PassedWithNote : TechResult.Passed;
        }
        else
        {
            verdict = TechResult.Passed;
        }

        var className = _cmbClass.SelectedItem as string ?? string.Empty;

        // The time is when the car was LOOKED AT. Correcting a note, the
        // inspector's name or a typo in the car is not a re-inspection, and
        // re-stamping it moved an old inspection onto today - which is what
        // decides whether a car counts as inspected at all.
        var reinspected = _original is null
            || verdict != _original.Result
            || !new HashSet<string>(passed, StringComparer.Ordinal).SetEquals(_original.ItemsPassed)
            || !new HashSet<string>(failed, StringComparer.Ordinal).SetEquals(_original.ItemsFailed)
            || !string.Equals(driver.Profile.Transponder, _original.Transponder, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(className, _original.ClassName, StringComparison.OrdinalIgnoreCase);

        Result.Transponder = driver.Profile.Transponder;
        Result.DriverName = driver.Profile.DriverName;
        Result.ClassName = className;
        Result.CarModel = _txtCar.Text.Trim();
        Result.Inspector = _txtInspector.Text.Trim();
        Result.Notes = _txtNotes.Text;
        Result.ItemsPassed = passed;
        Result.ItemsFailed = failed;
        Result.Result = verdict;

        if (reinspected)
        {
            Result.InspectedAtUtc = DateTime.UtcNow;

            // A re-inspection happens at the race day open NOW, not the one
            // the car was first looked at under. Keeping the old event left
            // a car inspected before the race day was opened (filed under
            // "no race day") uncountable however many times it was edited:
            // the tech warning only reads the current race day. Emptied
            // rather than set here because this dialog does not know the
            // current race day - the store's Upsert stamps it on the way in.
            Result.EventId = string.Empty;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private const string VerdictPassed = "Passed";
    private const string VerdictPassedWithNote = "Passed with a note";
    private const string VerdictFailed = "FAILED";

    /// <summary>The hand-picked result shows only when there is no checklist to decide it - two verdicts on one screen would be one too many.</summary>
    private void ShowManualVerdict(bool show)
    {
        _lblManualVerdict.Visible = show;
        _cmbManualVerdict.Visible = show;
        _lblVerdict.Visible = !show;

        // The verdict's card and the result's grid told, as the visibility
        // changing does not tell them while the window is still being built
        // (nothing of it on screen yet): each kept the hidden part's row -
        // the card, measured without it, too short for what it then laid
        // out, and the grid a blank line over the inspector (layout
        // pictures, 18 Nov 2026).
        _lblVerdict.Parent?.PerformLayout(_lblVerdict, nameof(Visible));
        _cmbManualVerdict.Parent?.PerformLayout(_cmbManualVerdict, nameof(Visible));
    }

    /// <summary>One check's line in the checklist, kept to be handed another check when the class changes - see BuildChecklist.</summary>
    private sealed record CheckRow(Control Line, Label Item, ComboBox State);

    /// <summary>
    /// One driver in the list. NoProfile marks the stand-in for an
    /// inspection's driver who has no profile under that transponder any
    /// more (see FillFields), said so in the list so nobody takes it for a
    /// driver on file.
    /// </summary>
    private sealed record DriverItem(DriverProfile Profile, bool NoProfile = false)
    {
        public override string ToString()
        {
            var shown = string.IsNullOrWhiteSpace(Profile.DriverName)
                ? Profile.Transponder
                : $"{Profile.DriverName}   [{Profile.Transponder}]";

            return NoProfile ? $"{shown}   (no profile)" : shown;
        }
    }
}
