namespace HeatCaller;

/// <summary>
/// STREAM OVERLAYS, YOUR OWN (14 Nov 2026, stream overlay plan section 4,
/// Asher: "the ability to roll your own"). Opened from the Web Server page's
/// Stream overlay row.
///
/// Down the left, every layout: the presets (read-only - "Make a copy" turns
/// one into the club's own) and the club's own. On the right, the picked
/// layout's graphics, each with a tick, and the picked graphic's settings:
/// where it sits (nine places, or where the broadcast page has always put
/// it), a nudge across and down, its size, how many rows, and which parts of
/// the night it shows in.
///
/// CHANGES SAVE AS THEY ARE MADE (OverlayStore.Update), and an open overlay
/// page redraws when its layout changes - so a club editing with OBS open
/// sees each change on the stream there and then. That is also why there is
/// no OK and Cancel, only Close.
///
/// A dialog rather than a page of its own (the plan said "under Screens"):
/// it is opened from where the overlay's address already is, and it is set
/// up once, not used on a race night.
/// </summary>
public sealed class OverlayEditorForm : Form
{
    private readonly OverlayStore _store;
    private readonly Func<string, string> _addressOf;

    private readonly ListBox _lstLayouts = new();
    private readonly Button _btnCopy = new() { Text = "Make a copy..." };
    private readonly Button _btnRename = new() { Text = "Rename..." };
    private readonly Button _btnDelete = new() { Text = "Delete" };

    private readonly Label _lblNote = new() { AutoSize = true };
    private readonly CheckedListBox _lstElements = new();
    private readonly ComboBox _cmbAnchor = new();
    private readonly NumericUpDown _numX = new();
    private readonly NumericUpDown _numY = new();
    private readonly NumericUpDown _numScale = new();
    private readonly NumericUpDown _numRows = new();
    private readonly CheckBox _chkStaging = new ThemedCheckBox { Text = "The grid" };
    private readonly CheckBox _chkRacing = new ThemedCheckBox { Text = "Racing" };
    private readonly CheckBox _chkFinished = new ThemedCheckBox { Text = "The result" };
    private readonly CheckBox _chkBetween = new ThemedCheckBox { Text = "Between races" };
    private readonly NumericUpDown _numGlass = new();
    private readonly NumericUpDown _numTextScale = new();
    private readonly Label _lblOverlap = new() { AutoSize = true };
    private readonly Button _btnUndo = new() { Text = "Undo" };
    private readonly Button _btnBasics = new() { Text = "Basics cards..." };

    /// <summary>The club's settings, for the basics cards - null leaves that button off.</summary>
    private readonly AppSettings? _settings;

    /// <summary>Each change's layout as it was before, newest last, for Undo. This window's changes only.</summary>
    private readonly List<OverlayLayout> _undo = new();
    private readonly TextBox _txtAddress = new() { ReadOnly = true };
    private readonly Button _btnCopyAddress = new() { Text = "Copy" };
    private readonly Button _btnPreview = new() { Text = "Preview" };

    /// <summary>Set while the controls are being filled, so filling them is not taken as an edit.</summary>
    private bool _filling;

    private static readonly OverlayElementKind[] Kinds = Enum.GetValues<OverlayElementKind>();

    /// <summary>What each graphic is called here - the words a club would use, not the page's names.</summary>
    internal static string KindWords(OverlayElementKind kind) => kind switch
    {
        OverlayElementKind.Tower => "Positions down the side (the pylon)",
        OverlayElementKind.Battle => "Battle box",
        OverlayElementKind.Fastest => "Fastest lap",
        OverlayElementKind.Moment => "Lead changes and milestones",
        OverlayElementKind.Grid => "The grid",
        OverlayElementKind.Podium => "The podium",
        OverlayElementKind.Champ => "Championship if it ended now",
        OverlayElementKind.Captions => "Captions (what the announcer says)",
        OverlayElementKind.Status => "Race and clock",
        OverlayElementKind.Ticker => "Positions along the bottom",
        OverlayElementKind.Bug => "Corner bug",
        OverlayElementKind.Sponsor => "Sponsor logo",
        OverlayElementKind.ClubLogo => "Club logo",
        OverlayElementKind.Schedule => "Up next",
        OverlayElementKind.Basics => "New to RC racing?",
        OverlayElementKind.Mark => "HeatCaller's mark (always on)",
        _ => kind.ToString(),
    };

    private static readonly (OverlayAnchor Anchor, string Words)[] Anchors =
    {
        (OverlayAnchor.Default, "Where it usually goes"),
        (OverlayAnchor.TopLeft, "Top left"),
        (OverlayAnchor.Top, "Top middle"),
        (OverlayAnchor.TopRight, "Top right"),
        (OverlayAnchor.Left, "Middle left"),
        (OverlayAnchor.Middle, "Middle"),
        (OverlayAnchor.Right, "Middle right"),
        (OverlayAnchor.BottomLeft, "Bottom left"),
        (OverlayAnchor.Bottom, "Bottom middle"),
        (OverlayAnchor.BottomRight, "Bottom right"),
    };

    public OverlayEditorForm(OverlayStore store, Func<string, string> addressOf, string? pick = null, AppSettings? clubSettings = null)
    {
        _store = store;
        _addressOf = addressOf;
        _settings = clubSettings;

        Text = "Stream overlays";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        _lstLayouts.IntegralHeight = false;
        _lstLayouts.SelectedIndexChanged += (_, _) => ShowLayout();
        _btnCopy.Click += (_, _) => MakeCopy();
        _btnRename.Click += (_, _) => RenameLayout();
        _btnDelete.Click += (_, _) => DeleteLayout();

        _lstElements.CheckOnClick = true;
        _lstElements.IntegralHeight = false;
        _lstElements.BorderStyle = BorderStyle.None;
        foreach (var kind in Kinds)
        {
            _lstElements.Items.Add(KindWords(kind));
        }

        _lstElements.SelectedIndexChanged += (_, _) => ShowElement();
        _lstElements.ItemCheck += (_, e) =>
        {
            if (_filling)
            {
                return;
            }

            // The mark can be moved but never turned off.
            if (Kinds[e.Index] == OverlayElementKind.Mark && e.NewValue != CheckState.Checked)
            {
                e.NewValue = CheckState.Checked;
                return;
            }

            var on = e.NewValue == CheckState.Checked;
            Edit(layout => ElementOf(layout, Kinds[e.Index]).On = on);
        };

        _cmbAnchor.DropDownStyle = ComboBoxStyle.DropDownList;

        // Wide enough for "Where it usually goes", and the address for most
        // of a LAN address and an id - left to themselves both were cut off.
        _cmbAnchor.Width = UiLayout.AtTextSize(240);
        _txtAddress.Width = UiLayout.AtTextSize(330);

        foreach (var (_, words) in Anchors)
        {
            _cmbAnchor.Items.Add(words);
        }

        _cmbAnchor.SelectedIndexChanged += (_, _) => EditElement(e => e.Anchor = Anchors[Math.Max(0, _cmbAnchor.SelectedIndex)].Anchor);

        Spinner(_numX, -50, 50, 0.5m, 1);
        Spinner(_numY, -50, 50, 0.5m, 1);
        Spinner(_numScale, 50, 200, 5, 0);
        Spinner(_numRows, 0, 20, 1, 0);
        Spinner(_numGlass, 40, 100, 5, 0);
        Spinner(_numTextScale, 80, 160, 5, 0);
        _numTextScale.ValueChanged += (_, _) => Edit(layout => layout.TextScale = (double)_numTextScale.Value / 100);
        _btnUndo.Click += (_, _) => UndoLast();
        _btnBasics.Click += (_, _) => EditBasics();
        _btnBasics.Visible = clubSettings is not null;
        _lblOverlap.ForeColor = AppTheme.Danger;
        _numX.ValueChanged += (_, _) => EditElement(e => e.X = (double)_numX.Value);
        _numY.ValueChanged += (_, _) => EditElement(e => e.Y = (double)_numY.Value);
        _numScale.ValueChanged += (_, _) => EditElement(e => e.Scale = (double)_numScale.Value / 100);
        _numRows.ValueChanged += (_, _) => EditElement(e => e.Rows = (int)_numRows.Value);
        _numGlass.ValueChanged += (_, _) => Edit(layout => layout.Glass = (double)_numGlass.Value / 100);

        foreach (var box in new[] { _chkStaging, _chkRacing, _chkFinished, _chkBetween })
        {
            box.AutoSize = true;
            box.CheckedChanged += (_, _) => EditElement(e => e.During =
                (_chkStaging.Checked ? OverlayDuring.Staging : 0)
                | (_chkRacing.Checked ? OverlayDuring.Racing : 0)
                | (_chkFinished.Checked ? OverlayDuring.Finished : 0)
                | (_chkBetween.Checked ? OverlayDuring.Between : 0));
        }

        _btnCopyAddress.Click += (_, _) =>
        {
            try
            {
                if (_txtAddress.Text.Length > 0)
                {
                    Clipboard.SetText(_txtAddress.Text);
                }
            }
            catch
            {
                // Another program holds the clipboard; the address is on screen.
            }
        };
        _btnPreview.Click += (_, _) =>
        {
            if (_txtAddress.Text.Length > 0)
            {
                AppSupport.Open(_txtAddress.Text + "?demo=1");
            }
        };

        _lblNote.ForeColor = AppTheme.TextMuted;

        // --- Layout ---
        var settings = new FieldGrid();
        settings.AddRow("Where:", _cmbAnchor);
        settings.AddRow("Across:", _numX);
        settings.AddHint("From the place above towards the middle, in hundredths of the picture's width.");
        settings.AddRow("Down:", _numY);
        settings.AddRow("Size (%):", _numScale);
        settings.AddRow("Rows:", _numRows);
        settings.AddHint("For the positions: how many drivers. 0 is its usual number.");
        settings.AddRow("Shows during:", FieldGrid.Inline(_chkStaging, _chkRacing, _chkFinished, _chkBetween));
        settings.AddHint("None ticked: whenever it has something to show.");
        settings.AddRow("Boxes (% solid):", _numGlass);
        settings.AddRow("Everything (% size):", _numTextScale);
        settings.AddHint("Makes every graphic bigger or smaller at once - bigger reads better on a phone.");
        settings.AddRow("Its address:", FieldGrid.Inline(_txtAddress, _btnCopyAddress, _btnPreview));

        var main = new FillPage();
        main.Add(_lblNote, gapBelow: UiLayout.RowGap);
        main.Add(_lblOverlap, gapBelow: UiLayout.RowGap);
        main.AddFill(_lstElements, _ => _lstElements.ItemHeight * 6 + 4, gapBelow: UiLayout.RowGap);
        main.Add(settings, gapBelow: 0);

        var columns = new Beside(() => UiLayout.AtTextSize(220)) { SideOnLeft = true };
        columns.AddFill(main, main.LeastHeight);
        columns.AddSideFill(_lstLayouts, _ => _lstLayouts.ItemHeight * 6 + 4, gapBelow: UiLayout.RowGap);
        columns.AddSide(_btnCopy, gapBelow: UiLayout.ButtonGap);
        columns.AddSide(_btnRename, gapBelow: UiLayout.ButtonGap);
        columns.AddSide(_btnDelete, gapBelow: UiLayout.SectionGap);
        columns.AddSide(_btnUndo, gapBelow: UiLayout.ButtonGap);
        columns.AddSide(_btnBasics, gapBelow: 0);

        var intro = new Label
        {
            Text = "Pick a layout. A preset can't be changed: make a copy, then turn graphics on and off and move them. "
                 + "Changes save as you make them, and an overlay open in OBS updates straight away.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        var btnClose = new Button { Text = "Close", DialogResult = DialogResult.OK };

        var page = new FillPage();
        page.Add(intro, gapBelow: UiLayout.RowGap);
        page.AddFill(columns, columns.LeastHeight, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnClose), gapBelow: 0);

        Controls.Add(page);
        AcceptButton = btnClose;
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);

        FillLayouts(pick);
        DialogFit.Keep(this, page, width: 860, height: 700);
    }

    private static void Spinner(NumericUpDown box, decimal least, decimal most, decimal step, int places)
    {
        box.Minimum = least;
        box.Maximum = most;
        box.Increment = step;
        box.DecimalPlaces = places;
        box.Width = UiLayout.AtTextSize(UiLayout.NumericWidth);
    }

    /// <summary>A layout in the list: the presets, then the club's own.</summary>
    private sealed record Item(OverlayLayout Layout, bool Preset)
    {
        public override string ToString() => Preset ? Layout.Name + " (preset)" : Layout.Name;
    }

    private void FillLayouts(string? pick)
    {
        _lstLayouts.BeginUpdate();
        _lstLayouts.Items.Clear();

        foreach (var preset in OverlayLayouts.BuiltIns.Where(l => l.Id != OverlayLayouts.ResultsId))
        {
            _lstLayouts.Items.Add(new Item(preset, Preset: true));
        }

        foreach (var mine in _store.Layouts)
        {
            _lstLayouts.Items.Add(new Item(mine, Preset: false));
        }

        _lstLayouts.EndUpdate();

        var at = _lstLayouts.Items.Cast<Item>().ToList().FindIndex(i => i.Layout.Id == pick);
        _lstLayouts.SelectedIndex = at >= 0 ? at : 0;
    }

    private Item? Picked => _lstLayouts.SelectedItem as Item;

    /// <summary>The picked layout as it is now: a preset as built, the club's own fresh from the store.</summary>
    private OverlayLayout? Current => Picked is not { } item ? null : item.Preset ? item.Layout : _store.Get(item.Layout.Id) ?? item.Layout;

    private void ShowLayout()
    {
        var item = Picked;
        var layout = Current;

        _filling = true;

        try
        {
            for (var i = 0; i < Kinds.Length; i++)
            {
                _lstElements.SetItemChecked(i, layout?.Element(Kinds[i]) is { On: true });
            }

            _numGlass.Value = (decimal)Math.Round(Math.Clamp(layout?.Glass ?? 0.82, 0.4, 1) * 100);
            _numTextScale.Value = (decimal)Math.Round(Math.Clamp(layout?.TextScale ?? 1, 0.8, 1.6) * 100);
            _txtAddress.Text = layout is null ? string.Empty : _addressOf(layout.Id);
        }
        finally
        {
            _filling = false;
        }

        var editable = item is { Preset: false };
        _lblNote.Text = item is null ? string.Empty
            : editable ? "Your own layout. " + (layout?.About is { Length: > 0 } about ? about : string.Empty)
            : "A preset: " + item.Layout.About + " Make a copy to change it.";
        _lstElements.Enabled = editable;
        _btnRename.Enabled = editable;
        _btnDelete.Enabled = editable;
        _numGlass.Enabled = editable;
        _numTextScale.Enabled = editable;
        _btnUndo.Enabled = editable && _undo.Any(u => u.Id == item!.Layout.Id);
        var warnings = layout is null ? Array.Empty<string>() : OverlayOverlap.Warnings(layout, KindWords);
        _lblOverlap.Text = warnings.Count == 0 ? string.Empty : string.Join(" ", warnings.Take(3));
        _btnPreview.Enabled = _txtAddress.Text.Length > 0;
        _btnCopyAddress.Enabled = _txtAddress.Text.Length > 0;

        if (_lstElements.SelectedIndex < 0 && _lstElements.Items.Count > 0)
        {
            _lstElements.SelectedIndex = 0;
        }

        ShowElement();
    }

    private void ShowElement()
    {
        var layout = Current;
        var kind = _lstElements.SelectedIndex >= 0 ? Kinds[_lstElements.SelectedIndex] : (OverlayElementKind?)null;
        var element = kind is { } k ? layout?.Element(k) : null;

        _filling = true;

        try
        {
            _cmbAnchor.SelectedIndex = Math.Max(0, Array.FindIndex(Anchors, a => a.Anchor == (element?.Anchor ?? OverlayAnchor.Default)));
            _numX.Value = (decimal)Math.Clamp(element?.X ?? 2, -50, 50);
            _numY.Value = (decimal)Math.Clamp(element?.Y ?? 2, -50, 50);
            _numScale.Value = (decimal)Math.Round(Math.Clamp(element?.Scale ?? 1, 0.5, 2) * 100);
            _numRows.Value = Math.Clamp(element?.Rows ?? 0, 0, 20);
            var during = element?.During ?? OverlayDuring.Always;
            _chkStaging.Checked = during.HasFlag(OverlayDuring.Staging);
            _chkRacing.Checked = during.HasFlag(OverlayDuring.Racing);
            _chkFinished.Checked = during.HasFlag(OverlayDuring.Finished);
            _chkBetween.Checked = during.HasFlag(OverlayDuring.Between);
        }
        finally
        {
            _filling = false;
        }

        var editable = Picked is { Preset: false } && element is { On: true };
        var placed = editable && element!.Anchor != OverlayAnchor.Default;

        _cmbAnchor.Enabled = editable;
        _numX.Enabled = placed;
        _numY.Enabled = placed;
        _numScale.Enabled = placed;
        _numRows.Enabled = editable && kind is OverlayElementKind.Tower or OverlayElementKind.Ticker;
        foreach (var box in new[] { _chkStaging, _chkRacing, _chkFinished, _chkBetween })
        {
            box.Enabled = editable && kind != OverlayElementKind.Mark;
        }
    }

    /// <summary>The element of this kind in the layout, added (off, where it usually goes) when the layout has none yet.</summary>
    private static OverlayElement ElementOf(OverlayLayout layout, OverlayElementKind kind)
    {
        if (layout.Element(kind) is { } e)
        {
            return e;
        }

        var added = new OverlayElement { Kind = kind, On = false };
        layout.Elements.Add(added);
        return added;
    }

    /// <summary>One change to the picked layout, saved at once. Nothing for a preset or while filling.</summary>
    private void Edit(Action<OverlayLayout> change)
    {
        if (_filling || Picked is not { Preset: false } item || _store.Get(item.Layout.Id) is not { } layout)
        {
            return;
        }

        _undo.Add(layout.Copy());
        if (_undo.Count > 50)
        {
            _undo.RemoveAt(0);
        }

        change(layout);
        _store.Update(layout);
        ShowLayoutKeepingElement();
    }

    /// <summary>Redraws the picked layout's state - the warnings, Undo - without losing the graphic picked.</summary>
    private void ShowLayoutKeepingElement()
    {
        var at = _lstElements.SelectedIndex;
        ShowLayout();
        if (at >= 0 && at < _lstElements.Items.Count)
        {
            _lstElements.SelectedIndex = at;
        }
    }

    /// <summary>Puts the picked layout back as it was before its last change in this window.</summary>
    private void UndoLast()
    {
        if (Picked is not { Preset: false } item)
        {
            return;
        }

        var at = _undo.FindLastIndex(u => u.Id == item.Layout.Id);

        if (at < 0)
        {
            return;
        }

        var before = _undo[at];
        _undo.RemoveAt(at);
        _store.Update(before);
        ShowLayoutKeepingElement();
    }

    /// <summary>
    /// The club's own "New to RC racing?" cards (overlay plan 7.6): one a
    /// line, "Heading | words". Emptied, HeatCaller's own come back.
    /// </summary>
    private void EditBasics()
    {
        if (_settings is null)
        {
            return;
        }

        using var ask = new Form { Text = "Basics cards", Font = AppTheme.BodyFont, BackColor = AppTheme.Background };
        var text = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            WordWrap = true,
            Text = string.IsNullOrWhiteSpace(_settings.StreamBasics) ? StreamBasics.BuiltInText : _settings.StreamBasics,
            Height = UiLayout.AtTextSize(260),
        };
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        var reset = new Button { Text = "HeatCaller's own" };
        reset.Click += (_, _) => text.Text = StreamBasics.BuiltInText;

        var page = new FillPage();
        page.Add(new Label
        {
            Text = "One card a line: a heading, a bar, then the words - \"Tonight's classes | Junior: under-16s on 1/28 cars\". "
                 + "They show between races, one every twelve seconds, on layouts with New to RC racing? on.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        }, gapBelow: UiLayout.RowGap);
        page.AddFill(text, UiLayout.AtTextSize(200), gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(cancel, ok, reset), gapBelow: 0);
        ask.Controls.Add(page);
        ask.AcceptButton = null;
        ask.CancelButton = cancel;
        AppTheme.ApplyRecursively(ask);
        DialogFit.Keep(ask, page, width: 720, height: 460);

        if (ask.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // The built-in cards saved unchanged are kept as "HeatCaller's own",
        // so a later version's better wording still reaches the club.
        var typed = text.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        _settings.StreamBasics = typed == StreamBasics.BuiltInText.Replace("\r\n", "\n", StringComparison.Ordinal) ? string.Empty : typed;
        _settings.Save();
    }

    private void EditElement(Action<OverlayElement> change)
    {
        if (_lstElements.SelectedIndex < 0)
        {
            return;
        }

        var kind = Kinds[_lstElements.SelectedIndex];
        Edit(layout => change(ElementOf(layout, kind)));
    }

    private void MakeCopy()
    {
        if (Current is not { } from)
        {
            return;
        }

        var name = AskName("Make a copy", "What should your layout be called?", from.Name + " (mine)");

        if (name is null)
        {
            return;
        }

        var copy = _store.AddCopy(from, name);
        FillLayouts(copy.Id);
    }

    private void RenameLayout()
    {
        if (Picked is not { Preset: false } || Current is not { } layout)
        {
            return;
        }

        var name = AskName("Rename", "A new name for this layout. Its address stays the same, so OBS keeps working.", layout.Name);

        if (name is null)
        {
            return;
        }

        layout.Name = name;
        _store.Update(layout);
        FillLayouts(layout.Id);
    }

    private void DeleteLayout()
    {
        if (Picked is not { Preset: false } || Current is not { } layout)
        {
            return;
        }

        if (AppDialog.Show(this,
                $"Delete \"{layout.Name}\"? An OBS source still using its address will say the overlay has gone.",
                "Delete layout", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _store.Remove(layout.Id);
        FillLayouts(null);
    }

    /// <summary>A name, asked for in a small box of the app's own; null when cancelled or left blank.</summary>
    private string? AskName(string title, string prompt, string suggestion)
    {
        using var ask = new Form
        {
            Text = title,
            Font = AppTheme.BodyFont,
            BackColor = AppTheme.Background,
        };

        var text = new TextBox { Text = suggestion, Width = UiLayout.AtTextSize(360) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        var page = new FillPage();
        page.Add(new Label { Text = prompt, AutoSize = true, ForeColor = AppTheme.TextMuted }, gapBelow: UiLayout.RowGap);
        page.Add(text, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(cancel, ok), gapBelow: 0);
        ask.Controls.Add(page);
        ask.AcceptButton = ok;
        ask.CancelButton = cancel;
        AppTheme.ApplyRecursively(ask);
        DialogFit.Keep(ask, page, width: 420);

        return ask.ShowDialog(this) == DialogResult.OK && text.Text.Trim().Length > 0 ? text.Text.Trim() : null;
    }
}
