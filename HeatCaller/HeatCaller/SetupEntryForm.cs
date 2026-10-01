namespace HeatCaller;

/// <summary>
/// Fills in a setup sheet: the chassis diagram with its pinned callouts on
/// the left, and the template's sections of fields on the right.
///
/// The field editors are generated from the template rather than hardcoded,
/// so a chassis with different springs or an extra adjustment needs no code
/// change - just an edit to its template.
///
/// On the layout pieces (DialogFit): who, where and in what car along one
/// line, and the chassis sheet and the session it ran in along the next -
/// each a FieldRow, wrapping when the window is narrow, its last field
/// running to the right-hand edge; under them the diagram down the left and
/// the sheet's fields beside it (a Beside), the two taking the height, the
/// fields scrolling on their own - a line per field, its caption wrapping to
/// the room left of its editor and the editors in one column down the right
/// (BuildFieldLine); and the hint, and Save and Cancel against the right-hand
/// edge, along the foot. It used to be placed in pixels in a 1120x760 window
/// with a 900x600 floor - taller than a 1280x720 screen has room for, so it
/// opened there with Save and Cancel under the screen's edge - the header's
/// captions and fields at fixed places whatever their length, the diagram a
/// fixed 520px, and each field's caption wrapped at 210px beside an editor
/// at a fixed place, 280px wide, whatever the text size.
/// </summary>
public sealed class SetupEntryForm : Form
{
    private const string NotSet = "(not set)";

    /// <summary>
    /// The window's inside at the normal text size - the size it always
    /// opened at. It opens this big at the text size in use, never bigger
    /// than the screen, and can be made bigger, the diagram and the fields
    /// taking the height.
    /// </summary>
    private const int DialogWidth = 1120;
    private const int DialogHeight = 760;

    /// <summary>The header's fields at the normal text size, as they were - a drop-down wider when its longest choice needs it, the car and the session running on to the right-hand edge.</summary>
    private const int DriverWidth = 190;
    private const int TrackWidth = 180;
    private const int CarWidth = 170;
    private const int TemplateWidth = 250;
    private const int SessionWidth = 330;

    /// <summary>The diagram's width at the normal text size, as it was - narrower only to leave the fields their least (see DiagramColumnWidth) - and the narrowest it gets before it shares the room with them.</summary>
    private const int DiagramWidth = 520;
    private const int DiagramLeastWidth = 240;

    /// <summary>The diagram's least height at the normal text size, below which the window scrolls instead.</summary>
    private const int DiagramLeastHeight = 240;

    /// <summary>A field's editor at the normal text size, as it was - every editor wider when a drop-down's longest option needs it (SizeEditors).</summary>
    private const int EditorWidth = 280;

    /// <summary>The least room a field's caption has at the normal text size, its marker included - about the 210px it used to be wrapped at.</summary>
    private const int CaptionLeastWidth = 200;

    /// <summary>The fields show at least this many lines before the window scrolls instead.</summary>
    private const int LeastFieldLines = 3;

    private readonly ChassisTemplateStore _templates;
    private readonly SessionHistoryStore _history;

    private readonly ComboBox _cmbDriver = new();
    private readonly ComboBox _cmbTrack = new();
    private readonly ComboBox _cmbTemplate = new();
    private readonly ComboBox _cmbSession = new();
    private readonly TextBox _txtCar = new();

    private readonly ChassisCanvas _canvas = new();

    /// <summary>
    /// Where the fields go, beside the diagram. The fields themselves are a
    /// column made afresh for each chassis sheet (LoadTemplateIntoUi) - a
    /// sheet's fields are not another's - and this holds whichever is in use.
    /// </summary>
    private readonly Panel _fieldsHost = new() { Tag = AppTheme.KeepBackColor };

    /// <summary>The current sheet's fields, a line each, scrolling on their own - see LoadTemplateIntoUi. Null only until the first sheet is loaded.</summary>
    private PageStack? _fields;

    /// <summary>Editor control per field id, read back on save.</summary>
    private readonly Dictionary<string, Control> _editors = new();

    /// <summary>The current sheet's lines, for sizing their editors and levelling their captions (SizeEditors).</summary>
    private readonly List<FieldLine> _lines = new();

    /// <summary>How wide every field's editor is: the width it was at the normal text size, at the size in use, or wider for a drop-down's longest option - see SizeEditors.</summary>
    private int _editorWidth;

    /// <summary>Set once the window's own theme pass has run: a sheet loaded after that themes its own lines.</summary>
    private bool _themed;

    private ChassisTemplate _template;

    public SetupEntry Result { get; }

    public SetupEntryForm(SetupEntry? existing, ChassisTemplateStore templates,
        SessionHistoryStore history, ProfileStore profiles, TrackLayoutStore trackLayouts)
    {
        // How the time goes while it opens, for the session log - see
        // WhereTimeGoes. It took 4 to 5 seconds, and holding its layout
        // while it was built did not change that.
        WhereTimeGoes.Start("Setup Sheet");

        _templates = templates;
        _history = history;

        Result = existing ?? new SetupEntry();
        _template = _templates.Get(Result.ChassisTemplateId) ?? _templates.Fallback;

        // Old entries predate templates - fold their fixed fields into the
        // template's matching fields so nothing is stranded.
        if (Result.IsLegacy)
        {
            Result.MigrateLegacyFields(_template);
        }

        // Built with its layout held, and laid out once at the end - see
        // LayoutFreeze.HoldWhileBuilding. Forty lines of fields, each laid
        // out again for every part added after it and every face the theme
        // pass changed, took four seconds.
        FillPage page;

        using (LayoutFreeze.HoldWhileBuilding())
        {
            Text = "Setup Sheet";
            Font = AppTheme.BodyFont;
            BackColor = AppTheme.Background;

            _editorWidth = UiLayout.AtTextSize(EditorWidth);

            var (who, sheet) = BuildHeader(profiles, trackLayouts);
            BuildCanvas();
            var footer = BuildButtons();
            WhereTimeGoes.Mark("header");

            // The diagram down the left, the width it always was; the fields
            // beside it taking the rest, and never less than they can do with -
            // the diagram gives way first (DiagramColumnWidth). Both take the
            // height the header and the foot leave.
            Beside? band = null;

            band = new Beside(() => DiagramColumnWidth(band?.Width ?? 0)) { SideOnLeft = true };

            // A few lines of fields - a drop-down and the gap under it for each -
            // before the window scrolls instead.
            band.AddFill(_fieldsHost, _ => LeastFieldLines * (_cmbTemplate.Height + UiLayout.RowGap));
            band.AddSideFill(_canvas, _ => UiLayout.AtTextSize(DiagramLeastHeight), gapBelow: 0);

            page = new FillPage();
            page.Add(who, gapBelow: UiLayout.RowGap);
            page.Add(sheet);
            page.AddFill(band, band.LeastHeight);
            page.Add(footer, gapBelow: 0);

            Controls.Add(page);

            // Once the fields' place is in the window, so what they are measured
            // in is the window's face.
            LoadTemplateIntoUi();

            AppTheme.ApplyRecursively(this);
            _themed = true;
            WhereTimeGoes.Mark("theme pass");
        }

        WhereTimeGoes.Mark("letting go");

        DialogFit.Keep(this, page, width: DialogWidth, height: DialogHeight);
        WhereTimeGoes.LogWhenShown(this);
    }

    /// <summary>
    /// Who, where and in what car along one line; the chassis sheet and the
    /// session along the next - each a FieldRow, so a line too narrow for all
    /// of it wraps rather than running off the edge. The drop-downs as wide as
    /// they were at the normal text size, at the size in use, or as their
    /// longest choice needs - measured whenever the row is, and kept until
    /// the face or the list changes (Pieces.PickerWidth). The car and the
    /// session run on to the right-hand edge, never narrower than they were.
    /// </summary>
    private (FieldRow Who, FieldRow Sheet) BuildHeader(ProfileStore profiles, TrackLayoutStore trackLayouts)
    {
        _cmbDriver.DropDownStyle = ComboBoxStyle.DropDown;
        foreach (var name in profiles.All.Select(p => p.DriverName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
        {
            _cmbDriver.Items.Add(name);
        }
        _cmbDriver.Text = Result.DriverName;

        _cmbTrack.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTrack.Items.Add("(none)");
        foreach (var t in trackLayouts.All)
        {
            _cmbTrack.Items.Add(t.Name);
        }
        _cmbTrack.SelectedItem = string.IsNullOrWhiteSpace(Result.TrackLayoutName) ? "(none)" : Result.TrackLayoutName;
        if (_cmbTrack.SelectedIndex < 0) { _cmbTrack.SelectedIndex = 0; }

        _txtCar.Text = Result.CarModel;
        _txtCar.PlaceholderText = "e.g. MR-03 / Audi R8";

        _cmbTemplate.DropDownStyle = ComboBoxStyle.DropDownList;
        RefreshTemplateList();
        _cmbTemplate.SelectedIndexChanged += (_, _) =>
        {
            if (_cmbTemplate.SelectedItem is TemplateItem item && item.Template.Id != _template.Id)
            {
                // Capture what's on screen before swapping, so switching
                // sheets and switching back doesn't lose anything typed.
                ReadEditorsIntoResult();
                _template = item.Template;
                LoadTemplateIntoUi();
            }
        };

        _cmbSession.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbSession.Items.Add(SessionItem.None);
        foreach (var s in _history.All.Take(60))
        {
            _cmbSession.Items.Add(new SessionItem(s));
        }
        SelectSession(Result.LinkedSessionId);

        // The name typed on an existing entry counts too: a driver no longer
        // on file is not in the list, and would be cut short in the box.
        var who = new FieldRow();
        who.Add(new Label { Text = "Driver:" }, _cmbDriver, Picker(_cmbDriver, DriverWidth, Result.DriverName));
        who.Add(new Label { Text = "Track:" }, _cmbTrack, Picker(_cmbTrack, TrackWidth));
        who.AddLast(new Label { Text = "Car:" }, _txtCar, () => UiLayout.AtTextSize(CarWidth));

        var sheet = new FieldRow();
        sheet.Add(new Label { Text = "Chassis sheet:" }, _cmbTemplate, Picker(_cmbTemplate, TemplateWidth));
        sheet.AddLast(new Label { Text = "Ran in session:" }, _cmbSession, Picker(_cmbSession, SessionWidth));

        return (who, sheet);

        static Func<int> Picker(ComboBox combo, int least, string alsoFits = "")
        {
            (Font Font, int Count, int Width)? measured = null;

            return () => Math.Max(UiLayout.AtTextSize(least), Pieces.PickerWidth(combo, ref measured, alsoFits));
        }
    }

    private void BuildCanvas()
    {
        _canvas.EditMode = false;

        // Clicking a callout jumps to its editor on the right, which is what
        // ties the picture to the form rather than leaving them separate.
        _canvas.SelectionChanged += FocusSelectedField;
        _canvas.PointActivated += _ => FocusSelectedField();
    }

    /// <summary>
    /// The foot: the hint on the left, and Save and Cancel against the
    /// right-hand edge, on one line as they were (a SplitBar) - Cancel
    /// furthest right, and Tab still reaching Save first: the bar takes them
    /// right to left, in the order they were added.
    /// </summary>
    private SplitBar BuildButtons()
    {
        var btnSave = new Button { Text = "Save" };
        btnSave.Click += BtnSave_Click;

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        var lblHint = new Label
        {
            Text = "Click a callout on the diagram to jump to its field. Filled callouts are highlighted.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        var footer = new SplitBar(
            new ButtonBar().With(lblHint),
            new ButtonBar(alignRight: true).With(btnCancel, btnSave));

        btnSave.TabIndex = 0;
        btnCancel.TabIndex = 1;

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        return footer;
    }

    private void RefreshTemplateList()
    {
        _cmbTemplate.Items.Clear();
        foreach (var t in _templates.All)
        {
            _cmbTemplate.Items.Add(new TemplateItem(t));
        }

        for (var i = 0; i < _cmbTemplate.Items.Count; i++)
        {
            if (_cmbTemplate.Items[i] is TemplateItem item && item.Template.Id == _template.Id)
            {
                _cmbTemplate.SelectedIndex = i;
                return;
            }
        }

        if (_cmbTemplate.Items.Count > 0)
        {
            _cmbTemplate.SelectedIndex = 0;
        }
    }

    /// <summary>
    /// Rebuilds the diagram and the generated field editors for the current
    /// template.
    ///
    /// The fields go in a new column each time, in place of the last, which
    /// is disposed: a line per field under its section's heading, a row's gap
    /// between lines and a section's between sections, nothing under the
    /// last (the column scrolls by it). A sheet loaded once the window is
    /// themed has its lines themed here as the rest of the window was, then
    /// sized in the faces that leaves them in (SizeEditors).
    /// </summary>
    private void LoadTemplateIntoUi()
    {
        // The new lines built with their layout held, as the window is (see
        // the constructor), and laid out once when the hold ends.
        using var hold = LayoutFreeze.HoldWhileBuilding();

        _canvas.Values = Result.Values;
        _canvas.SetTemplate(_template, _templates.ResolveImagePath(_template));
        WhereTimeGoes.Mark("diagram");

        _editors.Clear();
        _lines.Clear();

        // As wide as the diagram leaves it, its lines running to its edges:
        // no margins of its own - the page's are round it - and no room under
        // its last line, which the theme pass gives a panel that scrolls (see
        // ScrollTheming.NoRoomBelow).
        var fields = new PageStack { Padding = Padding.Empty };
        ScrollTheming.NoRoomBelow(fields);

        var sections = _template.Sections.Where(section => section.Fields.Count > 0).ToList();

        for (var s = 0; s < sections.Count; s++)
        {
            var section = sections[s];

            fields.Add(new Label
            {
                Text = section.Name.ToUpperInvariant(),
                AutoSize = true,
                Font = AppTheme.BoldFont,
                ForeColor = AppTheme.Primary,

                // "CHASSIS & DRIVETRAIN" shows its ampersand, rather than
                // underlining the space after it.
                UseMnemonic = false,
            }, gapBelow: UiLayout.HintGap);

            for (var f = 0; f < section.Fields.Count; f++)
            {
                var gapBelow = f < section.Fields.Count - 1
                    ? UiLayout.RowGap
                    : s < sections.Count - 1 ? UiLayout.SectionGap : 0;

                fields.Add(BuildFieldLine(section.Fields[f]), gapBelow: gapBelow);
            }
        }

        var old = _fields;
        _fields = fields;

        if (old is not null)
        {
            _fieldsHost.Controls.Remove(old);
            old.Dispose();
        }

        _fieldsHost.Controls.Add(fields);

        WhereTimeGoes.Mark($"{_lines.Count} fields");

        if (_themed)
        {
            AppTheme.ApplyRecursively(_fieldsHost);
        }

        SizeEditors();
        WhereTimeGoes.Mark("sizing the fields");
    }

    /// <summary>
    /// A field's line: its caption - the field's label and unit - wrapping to
    /// the room left of its editor, and the editor for the field's kind down
    /// the right, every editor as wide as the others (a Beside, the editor its
    /// column). The caption is a StatusLine whose light is the marker a
    /// field pinned to the diagram gets - one that draws nothing for the
    /// others, so every caption starts in the same place.
    /// </summary>
    private Beside BuildFieldLine(SetupField field)
    {
        var suffix = string.IsNullOrWhiteSpace(field.Unit) ? "" : $"  ({field.Unit})";
        var text = new Label { Text = field.Label + suffix, UseMnemonic = false };

        // Pinned fields get a marker, so it's clear which ones also appear
        // on the diagram.
        var caption = new StatusLine(new PinMarker(field.OnDiagram), text);

        var current = Result.Values.TryGetValue(field.Id, out var existing) ? existing : string.Empty;
        var listRows = 0;
        Control editor;

        switch (field.Kind)
        {
            case SetupFieldKind.Choice:
            {
                var combo = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                };
                combo.Items.Add(NotSet);
                foreach (var option in field.Options)
                {
                    combo.Items.Add(option);
                }
                combo.SelectedItem = string.IsNullOrWhiteSpace(current) ? NotSet : current;
                if (combo.SelectedIndex < 0)
                {
                    // A value that isn't in the option list any more (the
                    // template was edited) - keep it rather than silently
                    // discarding what was recorded.
                    combo.Items.Add(current);
                    combo.SelectedItem = current;
                }
                combo.SelectedIndexChanged += (_, _) => { ReadEditorsIntoResult(); _canvas.Invalidate(); };
                editor = combo;
                break;
            }

            case SetupFieldKind.MultiChoice:
            {
                // As many rows as it has options, from two to five, as it
                // always showed - sized in its face (SizeEditors). Given
                // that height, not snapped to whole rows.
                listRows = Math.Clamp(field.Options.Count, 2, 5);

                var list = new ThemedCheckedListBox
                {
                    CheckOnClick = true,
                    BorderStyle = BorderStyle.FixedSingle,
                    IntegralHeight = false,
                };
                var selected = current.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var option in field.Options)
                {
                    var index = list.Items.Add(option);
                    if (selected.Contains(option, StringComparer.OrdinalIgnoreCase))
                    {
                        list.SetItemChecked(index, true);
                    }
                }
                list.ItemCheck += (_, _) =>
                {
                    if (!IsHandleCreated || IsDisposed) { return; }
                    BeginInvoke(() => { ReadEditorsIntoResult(); _canvas.Invalidate(); });
                };
                editor = list;
                break;
            }

            case SetupFieldKind.Boolean:
            {
                var check = new ThemedCheckBox
                {
                    Checked = string.Equals(current, "true", StringComparison.OrdinalIgnoreCase),
                };
                check.CheckedChanged += (_, _) => { ReadEditorsIntoResult(); _canvas.Invalidate(); };
                editor = check;
                break;
            }

            default:
            {
                var box = new TextBox
                {
                    Text = current,
                };
                if (field.Kind == SetupFieldKind.Number)
                {
                    box.PlaceholderText = field.Unit;
                }
                box.Leave += (_, _) => { ReadEditorsIntoResult(); _canvas.Invalidate(); };
                editor = box;
                break;
            }
        }

        _editors[field.Id] = editor;

        var row = new Beside(() => _editorWidth)
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        row.Add(caption, gapBelow: 0);
        row.AddSide(editor, gapBelow: 0);

        // The line is as tall as a drop-down's or a box's own height when it
        // is placed, and those settle their heights by themselves - once they
        // have a window, once the theme takes a box's border off - which lays
        // nothing out. Told, the line lays itself out again at the height the
        // editor has (see Pieces.KeepHeightTold).
        Pieces.KeepHeightTold(editor);

        var line = new FieldLine(row, caption, text, editor, listRows);
        _lines.Add(line);

        // The caption level with the editor again whenever the editor's
        // height changes - see Level.
        editor.SizeChanged += (_, _) => Level(line);

        return row;
    }

    /// <summary>
    /// The lines' editors, in the faces they wear in the window: every editor
    /// as wide as the widest drop-down's longest option needs, and never
    /// narrower than the 280px they were at the normal text size, at the
    /// size in use; a checklist its rows of options; a tick box a line of its
    /// caption tall, its box level with the caption's middle; and each
    /// caption level with its editor's first line. Run as each sheet is
    /// loaded, once its lines are in the window.
    /// </summary>
    private void SizeEditors()
    {
        var width = UiLayout.AtTextSize(EditorWidth);

        foreach (var line in _lines)
        {
            if (line.Editor is ComboBox combo)
            {
                (Font Font, int Count, int Width)? measured = null;
                width = Math.Max(width, Pieces.PickerWidth(combo, ref measured));
            }
            else if (line.Editor is CheckedListBox list)
            {
                var height = line.ListRows * list.ItemHeight + 4;

                if (list.Height != height)
                {
                    list.Height = height;
                }
            }
            else if (line.Editor is CheckBox check)
            {
                var height = Math.Max(check.GetPreferredSize(Size.Empty).Height, Pieces.LineHeight(line.Text.Font));

                if (check.Height != height)
                {
                    check.Height = height;
                }
            }

            Level(line);
        }

        _editorWidth = width;

        // A line's column is not told when its editor's width rule or a
        // height given here changes - its own size has not - so each is
        // placed again.
        foreach (var line in _lines)
        {
            line.Row.PerformLayout();
        }

        // And the diagram gives way to the fields' new least width
        // (DiagramColumnWidth) when the editors got wider - the band hears
        // it through its filling part's layout. Needed after a change of
        // chassis sheet: on opening, DialogFit lays everything out anyway.
        _fieldsHost.PerformLayout();
    }

    /// <summary>
    /// Puts a caption's first line level with its editor's: centred on a
    /// drop-down, a box or a tick box, and on the first option of a
    /// checklist. The editor sits a pixel in for the ring the theme draws
    /// round a box or a list (Pieces.RingRoom), which counts.
    /// </summary>
    private static void Level(FieldLine line)
    {
        var first = (line.Editor is CheckedListBox list ? list.ItemHeight : line.Editor.Height)
            + Pieces.RingRoom(line.Editor).Vertical;

        var top = Math.Max(0, (first - Pieces.LineHeight(line.Text.Font)) / 2);

        if (line.Caption.Margin.Top != top)
        {
            line.Caption.Margin = new Padding(0, top, 0, 0);
        }
    }

    /// <summary>
    /// The diagram's width in a band <paramref name="band"/> wide: the 520px
    /// it always was, at the text size in use, the fields beside it taking
    /// the rest - narrower when that would leave the fields less than their
    /// least (FieldsLeastWidth), down to 240px at the text size in use; and
    /// with too little room for both at that, each gives up the same share.
    /// The gap a Beside keeps between the two comes off first.
    /// </summary>
    private int DiagramColumnWidth(int band)
    {
        var wanted = UiLayout.AtTextSize(DiagramWidth);
        var least = UiLayout.AtTextSize(DiagramLeastWidth);
        var fields = FieldsLeastWidth();
        var room = Math.Max(0, band - UiLayout.RowGap);

        if (wanted + fields <= room)
        {
            return wanted;
        }

        if (least + fields <= room)
        {
            return room - fields;
        }

        return (int)((long)room * least / (least + fields));
    }

    /// <summary>
    /// The least the fields can do with across: a caption's least room, the
    /// gap a line keeps before its editor, the editors, and the scrollbar the
    /// fields take their width from when there are more than the window
    /// shows.
    /// </summary>
    private int FieldsLeastWidth() =>
        UiLayout.AtTextSize(CaptionLeastWidth) + UiLayout.RowGap + _editorWidth + SystemInformation.VerticalScrollBarWidth;

    /// <summary>Pulls every editor's current state into Result.Values.</summary>
    private void ReadEditorsIntoResult()
    {
        foreach (var (fieldId, editor) in _editors)
        {
            var value = editor switch
            {
                ComboBox combo => combo.SelectedItem as string is { } s && s != NotSet ? s : string.Empty,
                CheckedListBox list => string.Join(", ", list.CheckedItems.Cast<object>().Select(o => o.ToString())),
                CheckBox check => check.Checked ? "true" : string.Empty,
                TextBox box => box.Text.Trim(),
                _ => string.Empty,
            };

            if (string.IsNullOrWhiteSpace(value))
            {
                Result.Values.Remove(fieldId);
            }
            else
            {
                Result.Values[fieldId] = value;
            }
        }
    }

    /// <summary>Scrolls the fields to the editor for whichever callout is selected.</summary>
    private void FocusSelectedField()
    {
        if (_canvas.SelectedPointId is not { } id || !_editors.TryGetValue(id, out var editor))
        {
            return;
        }

        _fields?.ScrollControlIntoView(editor);
        editor.Focus();
    }

    private void SelectSession(string? sessionId)
    {
        if (sessionId is not null)
        {
            for (var i = 0; i < _cmbSession.Items.Count; i++)
            {
                if (_cmbSession.Items[i] is SessionItem item && item.Session?.Id == sessionId)
                {
                    _cmbSession.SelectedIndex = i;
                    return;
                }
            }
        }
        _cmbSession.SelectedIndex = 0;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_cmbDriver.Text))
        {
            AppDialog.Show(this, "Enter a driver name.", "Missing driver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ReadEditorsIntoResult();

        Result.DriverName = _cmbDriver.Text.Trim();
        var track = _cmbTrack.SelectedItem as string;
        Result.TrackLayoutName = track is null or "(none)" ? string.Empty : track;
        Result.CarModel = _txtCar.Text.Trim();
        Result.ChassisTemplateId = _template.Id;
        Result.LinkedSessionId = (_cmbSession.SelectedItem as SessionItem)?.Session?.Id;

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>A field's line and what in it is sized and levelled once it is in the window - see SizeEditors. ListRows is a checklist's rows of options, 0 for any other editor.</summary>
    private sealed record FieldLine(Beside Row, StatusLine Caption, Label Text, Control Editor, int ListRows);

    /// <summary>
    /// The mark beside a field that is also a callout on the diagram: a dot
    /// in the success colour. It used to be a dot character in the small
    /// type, a few pixels left of the caption; drawn now, at a little over
    /// half the light it is (StatusLine), it grows with the text size. A
    /// field that is not on the diagram has one that draws nothing.
    /// </summary>
    private sealed class PinMarker : Control
    {
        private readonly bool _pinned;

        public PinMarker(bool pinned)
        {
            _pinned = pinned;

            SetStyle(
                ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw,
                true);

            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (!_pinned)
            {
                return;
            }

            // A little over half the light, centred in it - about the dot
            // the character drew at the normal text size.
            var side = Math.Max(1, Math.Min(Width, Height));
            var dot = Math.Max(4, side * 4 / 7);

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using var brush = new SolidBrush(AppTheme.Success);
            e.Graphics.FillEllipse(brush, (Width - dot) / 2f, (Height - dot) / 2f, dot, dot);
        }
    }

    private sealed record TemplateItem(ChassisTemplate Template)
    {
        public override string ToString() => Template.Name;
    }

    private sealed record SessionItem(SessionRecord? Session)
    {
        public static readonly SessionItem None = new((SessionRecord?)null);

        public override string ToString()
        {
            if (Session is null)
            {
                return "(not linked to a session)";
            }

            var track = string.IsNullOrWhiteSpace(Session.TrackLayoutName) ? "" : $" - {Session.TrackLayoutName}";
            return $"{AppTime.DateAndTime(Session.StartedAtUtc.ToLocalTime())}{track} - {Session.Label}";
        }
    }
}
