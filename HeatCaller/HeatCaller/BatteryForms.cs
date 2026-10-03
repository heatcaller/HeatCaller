namespace HeatCaller;

/// <summary>
/// What every place that changes packs does the same way - a driver's
/// Batteries tab, the strip on their Garage tab, Club Batteries on the
/// Garage page and Race Control's right-click - each with its undo, so a
/// pack is changed one way whichever page it was changed from.
///
/// Each undo puts back only what its own step changed (BatteryPackStore
/// .Remember / PutBack), and refuses, saying why, when something done
/// since would be undone with it.
/// </summary>
internal static class BatteryActions
{
    public static string NameOf(DriverProfile profile) =>
        string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();

    /// <summary>Whose a pack is, in words: "the club", the driver's name, or the transponder of one no longer on file.</summary>
    public static string OwnerName(BatteryPack pack, ProfileStore profiles) =>
        pack.IsClubPack
            ? "the club"
            : profiles.Get(pack.OwnerTransponder) is { } owner ? NameOf(owner) : pack.OwnerTransponder;

    /// <summary>
    /// Add Pack (for <paramref name="ownerTransponder"/> - a driver's main
    /// transponder, or empty for the club) or Edit. Returns the pack saved,
    /// or null when cancelled.
    /// </summary>
    public static BatteryPack? AddOrEdit(IWin32Window owner, BatteryPackStore store, UndoService undo, BatteryPack? existing, string ownerTransponder)
    {
        // The dialog writes onto whatever it is given, so it gets a copy.
        // Handed the stored pack, it changed it before the undo could note
        // the old values - Undo put back the edited pack and did nothing -
        // and Cancel left half an edit in memory.
        using var form = new BatteryPackEditForm(existing is null ? null : BatteryPackStore.CopyOf(existing), ownerTransponder);

        if (form.ShowDialog(owner) != DialogResult.OK)
        {
            return null;
        }

        var saved = form.Result;
        var name = saved.DisplayName;
        var before = store.Remember(saved.Id);

        undo.Capture(existing is null ? $"Add battery pack {name}" : $"Edit pack {name}", () => UndoChange(store, before, name));

        store.Upsert(saved);
        return saved;
    }

    /// <summary>
    /// Takes a pack out of rotation, or brings it back. Retired, it comes
    /// out of anybody's car; its sessions and pace stay.
    /// </summary>
    public static void SetRetired(BatteryPackStore store, UndoService undo, BatteryPack pack, bool retired)
    {
        if (store.Get(pack.Id) is not { } now || now.Retired == retired)
        {
            return;
        }

        var name = now.DisplayName;
        var before = store.Remember(now.Id);
        var changed = BatteryPackStore.CopyOf(now);
        changed.Retired = retired;

        undo.Capture(retired ? $"Retire pack {name}" : $"Bring back pack {name}", () => UndoChange(store, before, name));

        store.Upsert(changed);
    }

    /// <summary>
    /// The undo of an add, an edit or a retire: the pack's own details back,
    /// but whoever has it now kept - a transfer since has an undo of its
    /// own, and this one is not about who owns it.
    /// </summary>
    private static void UndoChange(BatteryPackStore store, BatteryPackStore.PackMemo before, string name)
    {
        // Undoing an add deletes the pack - refused once it has been raced
        // on or put in a car since, which this step knows nothing about and
        // would quietly take with it. Delete asks first; this cannot.
        if (before.Pack is null && store.Get(before.PackId) is not null
            && (store.SessionsOn(before.PackId) > 0 || store.InCarsOf(before.PackId).Count > 0))
        {
            throw new InvalidOperationException(
                $"pack {name} has been put in a car or raced on since, and undoing adding it would delete that too. "
                + "Nothing was changed - delete or retire it on its Batteries tab instead.");
        }

        if (before.Pack is not null && store.Get(before.PackId) is { } now)
        {
            var back = BatteryPackStore.CopyOf(before.Pack);
            back.OwnerTransponder = now.OwnerTransponder;
            back.Transfers = BatteryPackStore.CopyOf(now).Transfers;
            before = before with { Pack = back };
        }

        if (!store.PutBack(before))
        {
            throw new InvalidOperationException($"pack {name} has been deleted since, so there is nothing to put back.");
        }
    }

    /// <summary>Deletes a pack after asking - its session tags with it. Returns whether it went.</summary>
    public static bool Delete(IWin32Window owner, BatteryPackStore store, UndoService undo, BatteryPack pack)
    {
        var used = store.SessionsOn(pack.Id);

        if (AppDialog.Show(owner,
                $"Delete pack \"{pack.DisplayName}\"?"
                + (used > 0
                    ? $"\r\n\r\nIt's recorded on {Plural.Of(used, "session")} - {(used == 1 ? "that is" : "those are")} removed, so its pace history is lost. "
                      + "Retiring it instead keeps the history."
                    : string.Empty),
                "Delete pack", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return false;
        }

        var name = pack.DisplayName;
        var before = store.Remember(pack.Id);

        undo.Capture($"Delete pack {name}", () =>
        {
            if (!store.PutBack(before, wasDeleted: true))
            {
                throw new InvalidOperationException($"pack {name} is back already, so there is nothing to put back.");
            }
        });

        store.Delete(pack.Id);
        return true;
    }

    /// <summary>Gives a pack to another driver or to the club, for good, after asking who. Returns whether it moved.</summary>
    public static bool Transfer(IWin32Window owner, BatteryPackStore store, ProfileStore profiles, UndoService undo, BatteryPack pack)
    {
        using var form = new PackTransferForm(pack, profiles);

        if (form.ShowDialog(owner) != DialogResult.OK)
        {
            return false;
        }

        var to = form.ToTransponder;
        var name = pack.DisplayName;

        if (store.Get(pack.Id) is not { } now || string.Equals(now.OwnerTransponder, to, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var before = store.Remember(pack.Id);
        var toName = to.Length == 0 ? "the club" : profiles.Get(to) is { } p ? NameOf(p) : to;

        // The undo only once it has happened: a refused transfer leaves no
        // step behind.
        if (store.Transfer(pack.Id, to) is { } refused)
        {
            AppDialog.Show(owner, refused, "Transfer pack", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        undo.Capture($"Transfer pack {name} to {toName}", () =>
        {
            if (store.Get(before.PackId) is not { } current)
            {
                throw new InvalidOperationException($"pack {name} has been deleted since, so there is nothing to put back.");
            }

            if (!string.Equals(current.OwnerTransponder, to, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"pack {name} has changed hands again since, and undoing this now would undo that too. Nothing was changed.");
            }

            store.PutBack(before);
        });

        return true;
    }

    /// <summary>
    /// Puts a pack in a driver's car (their main transponder), or takes the
    /// one there out (null). Sessions already recorded keep their pack.
    /// Returns whether anything changed. <paramref name="prefix"/> starts
    /// the undo's description - "Phone: " from the phone.
    /// </summary>
    public static bool PutInCar(BatteryPackStore store, UndoService undo, string driverKey, string driverName, string? packId, string prefix = "")
    {
        var previous = store.CurrentPackId(driverKey);

        if (string.Equals(previous ?? string.Empty, packId ?? string.Empty, StringComparison.Ordinal))
        {
            return false;
        }

        var label = store.Get(packId)?.DisplayName;
        var what = label is null ? $"Take the pack out of {driverName}'s car" : $"Put pack {label} in {driverName}'s car";

        // Checked by the store too; asked first so a pack that has gone
        // leaves no undo step behind.
        if (packId is not null && label is null)
        {
            return false;
        }

        undo.Capture(prefix + what, () =>
        {
            if (!string.Equals(store.CurrentPackId(driverKey) ?? string.Empty, packId ?? string.Empty, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{driverName}'s pack in the car has been changed again since, and undoing this now would undo that too. Nothing was changed.");
            }

            store.SetCurrent(driverKey, previous);
        });

        return store.SetCurrent(driverKey, packId);
    }

    /// <summary>
    /// Records the pack a driver ran a session on (null: "no pack
    /// recorded"), picked by hand - and, with <paramref name="intoCar"/>,
    /// puts it in their car too, as Race Control does: a swap is usually
    /// what prompted it. One undo step for both.
    /// </summary>
    public static void TagSession(BatteryPackStore store, UndoService undo, string sessionId, string sessionLabel,
        string driverKey, string driverName, string? packId, PackTagSource source, bool intoCar)
    {
        var beforeTag = store.TagFor(sessionId, driverKey);
        var beforeCar = store.CurrentPackId(driverKey);
        var label = store.Get(packId)?.DisplayName;
        var moveCar = intoCar && label is not null && beforeCar != packId;
        var session = string.IsNullOrWhiteSpace(sessionLabel) ? "the session" : sessionLabel.Trim();

        undo.Capture(label is null ? $"Clear {driverName}'s pack for {session}" : $"Pack {label} for {driverName} in {session}", () =>
        {
            store.RestoreTag(sessionId, driverKey, beforeTag);

            // The car only while it still has this pack in it: one put in
            // since is newer than this, and stays.
            if (moveCar && store.CurrentPackId(driverKey) == packId)
            {
                store.SetCurrent(driverKey, beforeCar);
            }
        });

        store.Tag(sessionId, driverKey, label is null ? null : packId, source);

        if (moveCar)
        {
            store.SetCurrent(driverKey, packId);
        }
    }
}

/// <summary>
/// One pack on a picker's list: its label and type, its notes, and whose it
/// is when it is not the driver's own.
/// </summary>
internal sealed record PackChoice(BatteryPack? Pack, string Caption)
{
    public override string ToString() => Caption;

    public static string CaptionOf(BatteryPack pack, bool showClub) =>
        $"{pack.DisplayName}   {pack.Chemistry}"
        + (showClub && pack.IsClubPack ? "   (club)" : string.Empty)
        + (pack.Retired ? "   (retired)" : string.Empty)
        + (string.IsNullOrWhiteSpace(pack.Notes) ? string.Empty : $"   - {pack.Notes.Replace('\r', ' ').Replace('\n', ' ')}");
}

/// <summary>
/// Which pack - for a session on a driver's Batteries tab: their packs and
/// the club's, and "No pack recorded" at the end. Internal, so the layout
/// test can build it (WindowCatalog) without a click.
///
/// On the layout pieces (DialogFit): the packs taking the height, and Set
/// and Cancel at the foot; as wide as it was made, or wider for the
/// longest pack's line and for Set and Cancel on one line.
/// </summary>
internal sealed class PackPickerForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size.</summary>
    private const int DialogWidth = 320;

    /// <summary>The most the longest pack's line widens the window to, at the normal text size - see the constructor.</summary>
    private const int MostWidth = 640;

    /// <summary>The packs show at least this many before the window scrolls instead.</summary>
    private const int LeastRows = 4;

    /// <summary>The line for no pack.</summary>
    public const string NoPackCaption = "No pack recorded";

    private readonly ListBox _list = new();

    /// <param name="packs">The packs to offer, in the order to list them.</param>
    /// <param name="current">The pack recorded now, selected to start with - or null, when the first line is.</param>
    /// <param name="offerNone">Whether "No pack recorded" is on the list, at the end.</param>
    public PackPickerForm(IReadOnlyList<BatteryPack> packs, BatteryPack? current, bool offerNone = true)
    {
        Text = "Which pack?";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        _list.BackColor = AppTheme.Surface;

        // Given the height the window has, not snapped to whole rows - see FillPage.AddFill.
        _list.IntegralHeight = false;

        foreach (var pack in packs)
        {
            _list.Items.Add(new PackChoice(pack, PackChoice.CaptionOf(pack, showClub: true)));
        }

        if (offerNone)
        {
            _list.Items.Add(new PackChoice(null, NoPackCaption));
        }

        // The pack recorded now - or, with none recorded, "No pack
        // recorded", so Enter on a session with no pack changes nothing.
        if (_list.Items.Count > 0)
        {
            var at = current is null ? -1 : packs.ToList().FindIndex(p => p.Id == current.Id);
            _list.SelectedIndex = at >= 0 ? at : offerNone && current is null ? _list.Items.Count - 1 : 0;
        }

        _list.DoubleClick += (_, _) =>
        {
            if (_list.SelectedItem is not null)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };

        var ok = new Button { Text = "Set", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        var page = new FillPage();
        page.AddFill(_list, _ => LeastRows * _list.ItemHeight + 4);

        // Against the right-hand edge, Cancel furthest right - and Tab still
        // reaches Set first: the bar takes them right to left, in the order
        // they were added.
        var buttons = page.Add(new ButtonBar(alignRight: true).With(cancel, ok), gapBelow: 0);
        ok.TabIndex = 0;
        cancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = ok;
        CancelButton = cancel;

        AppTheme.ApplyRecursively(this);

        // The window's own button in the app's colour, as Save and OK are
        // in every other window - the theme goes by the caption, and "Set"
        // is not one it knows.
        AppTheme.StyleButton(ok, AppTheme.ButtonKind.Primary);

        // The width it was made at, at the text size in use - or wider for
        // the longest pack's line, up to twice that: a pack's notes can run
        // to a paragraph, and the rest of a long one is cut at the list's
        // edge. Never too narrow for Set and Cancel on one line.
        var widest = _list.Items.Cast<object>()
            .Take(400)
            .Select(item => TextRenderer.MeasureText(
                _list.GetItemText(item) ?? string.Empty,
                _list.Font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine).Width)
            .DefaultIfEmpty(0)
            .Max();

        var forPacks = Math.Min(
            UiLayout.AtTextSize(MostWidth),
            UiLayout.PageLeft + widest + SystemInformation.VerticalScrollBarWidth + 8 + UiLayout.PageRight);

        var width = Math.Max(
            Math.Max(UiLayout.AtTextSize(DialogWidth), forPacks),
            UiLayout.PageLeft + buttons.OneLineWidth + UiLayout.PageRight);

        DialogFit.Keep(this, page, width, height: 300, widthMeasured: true);
    }

    /// <summary>The pack picked, or null for "No pack recorded". Only meaningful after DialogResult.OK.</summary>
    public BatteryPack? Chosen => (_list.SelectedItem as PackChoice)?.Pack;
}

/// <summary>
/// Transfer: who a pack goes to for good - another driver, or the club.
/// The club's line first, then every driver but the owner, found by name,
/// number or transponder.
/// </summary>
internal sealed class PackTransferForm : Form
{
    /// <summary>The list shows at least this many before the window scrolls instead.</summary>
    private const int LeastRows = 5;

    /// <summary>The club's line in the list.</summary>
    private const string ClubKey = "\u0001club";

    private readonly BatteryPack _pack;
    private readonly ProfileStore _profiles;
    private readonly DriverProfile? _owner;

    private readonly TextBox _txtFind = new();
    private readonly ListBox _lstTo = new();
    private readonly Label _lblOutcome = new() { AutoSize = true };
    private readonly Button _btnTransfer = new();

    /// <summary>The new owner's main transponder, or empty for the club. Set on OK.</summary>
    public string ToTransponder { get; private set; } = string.Empty;

    public PackTransferForm(BatteryPack pack, ProfileStore profiles)
    {
        _pack = pack;
        _profiles = profiles;
        _owner = pack.IsClubPack ? null : profiles.Get(pack.OwnerTransponder);

        Text = $"Transfer {pack.DisplayName}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var lblWhat = new Label
        {
            Text = $"Give pack {pack.DisplayName} to someone else for good, sold or given. It's {BatteryActions.OwnerName(pack, profiles)}'s now.",
            AutoSize = true,
        };

        _txtFind.PlaceholderText = "name, number or transponder";
        _txtFind.TextChanged += (_, _) => FillList();

        _lstTo.IntegralHeight = false;
        _lstTo.SelectedIndexChanged += (_, _) => OnPicked();
        _lstTo.DoubleClick += (_, _) => Transfer();

        _btnTransfer.Text = "Transfer";
        _btnTransfer.Click += (_, _) => Transfer();

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        var find = new FieldGrid();
        find.AddRow("Find:", _txtFind, stretch: true);

        var page = new FillPage();
        page.Add(lblWhat, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(new Label { Text = "Who gets it?", AutoSize = true, Font = AppTheme.BoldFont }, stretch: false, gapBelow: UiLayout.HintGap);
        page.Add(find, gapBelow: UiLayout.HintGap);
        page.AddFill(_lstTo, _ => LeastRows * _lstTo.ItemHeight + 4, gapBelow: UiLayout.RowGap);
        page.Add(_lblOutcome, stretch: false);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, _btnTransfer), gapBelow: 0);

        Controls.Add(page);
        CancelButton = btnCancel;
        AcceptButton = _btnTransfer;

        FillList();

        AppTheme.ApplyRecursively(this);
        _lblOutcome.ForeColor = AppTheme.TextMuted;

        DialogFit.Keep(this, page, width: 480, height: 480);
    }

    /// <summary>One line of the list: a driver, or the club.</summary>
    private sealed record Choice(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private Choice? Picked => _lstTo.SelectedItem as Choice;

    private bool ToClub => Picked is { Key: ClubKey };

    /// <summary>The club, then every driver but the owner, matching what is typed.</summary>
    private void FillList()
    {
        var find = _txtFind.Text.Trim();
        var picked = Picked?.Key;

        bool Has(string? value) => !string.IsNullOrEmpty(value) && value.Contains(find, StringComparison.OrdinalIgnoreCase);

        _lstTo.BeginUpdate();
        _lstTo.Items.Clear();

        if (!_pack.IsClubPack && (find.Length == 0 || "the club".Contains(find, StringComparison.OrdinalIgnoreCase)))
        {
            _lstTo.Items.Add(new Choice(ClubKey, "The club"));
        }

        foreach (var profile in _profiles.All
                     .Where(p => !string.Equals(p.Transponder, _owner?.Transponder ?? _pack.OwnerTransponder, StringComparison.OrdinalIgnoreCase))
                     .Where(p => find.Length == 0
                                 || Has(p.DriverName) || Has(p.AnnouncerName) || Has(p.RaceNumber)
                                 || p.AllTransponderIds.Any(Has))
                     .OrderBy(BatteryActions.NameOf, StringComparer.CurrentCultureIgnoreCase))
        {
            var number = string.IsNullOrWhiteSpace(profile.RaceNumber) ? string.Empty : $"  #{profile.RaceNumber.Trim()}";
            _lstTo.Items.Add(new Choice(profile.Transponder, $"{BatteryActions.NameOf(profile)}{number}  ·  {profile.Transponder}"));
        }

        _lstTo.EndUpdate();

        var again = _lstTo.Items.Cast<Choice>().FirstOrDefault(c => string.Equals(c.Key, picked, StringComparison.OrdinalIgnoreCase));

        if (again is not null)
        {
            _lstTo.SelectedItem = again;
        }
        else if (_lstTo.Items.Count == 1)
        {
            _lstTo.SelectedIndex = 0;
        }

        OnPicked();
    }

    private void OnPicked()
    {
        if (IsDisposed)
        {
            return;
        }

        if (Picked is not { } choice)
        {
            _btnTransfer.Enabled = false;
            _btnTransfer.Text = "Transfer";
            _lblOutcome.Text = "Pick who it goes to.";
            return;
        }

        var who = ToClub ? "the club" : BatteryActions.NameOf(_profiles.Get(choice.Key)!);

        _btnTransfer.Enabled = true;
        _btnTransfer.Text = ToClub ? "Transfer to the Club" : $"Transfer to {who}";
        _lblOutcome.Text = $"Pack {_pack.DisplayName} becomes {who}'s. The sessions run on it stay recorded, and it comes out of anybody else's car.";
    }

    private void Transfer()
    {
        if (Picked is not { } choice || (choice.Key != ClubKey && _profiles.Get(choice.Key) is null))
        {
            return;
        }

        ToTransponder = ToClub ? string.Empty : choice.Key;

        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>
/// Adds or edits a pack.
///
/// On the layout pieces (DialogFit): the label, the type and the notes lined
/// up in a field grid, the hint under the label and the Retired tick under
/// the notes, and Save and Cancel at the foot; as tall as that, and wider
/// than it was made when the Retired tick's caption needs more than the
/// fields had.
/// </summary>
public sealed class BatteryPackEditForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size.</summary>
    private const int DialogWidth = 400;

    /// <summary>The label's and the notes' width at the normal text size - wider when the Retired tick's caption needs it.</summary>
    private const int FieldWidth = 284;

    /// <summary>The type's width at the normal text size - wider for a longer type, up to the fields' width.</summary>
    private const int TypeWidth = 160;

    /// <summary>How many lines of notes show before the box scrolls.</summary>
    private const int NotesLines = 3;

    private readonly TextBox _txtLabel = new();
    private readonly ComboBox _cmbChemistry = new();
    private readonly TextBox _txtNotes = new();
    private readonly CheckBox _chkRetired = new ThemedCheckBox();

    public BatteryPack Result { get; }

    /// <param name="existing">The pack to edit (a copy - the dialog writes onto it), or null to add one.</param>
    /// <param name="ownerTransponder">For a new pack: whose it is - a driver's main transponder, or empty for the club's.</param>
    public BatteryPackEditForm(BatteryPack? existing, string? ownerTransponder = null)
    {
        Result = existing ?? new BatteryPack { OwnerTransponder = ownerTransponder?.Trim() ?? string.Empty };

        Text = existing is null ? "Add Pack" : $"Edit {Result.DisplayName}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        _txtLabel.Text = Result.Label;
        _txtLabel.PlaceholderText = "3, Blue tape, A2...";

        _cmbChemistry.DropDownStyle = ComboBoxStyle.DropDown;
        _cmbChemistry.Items.AddRange(new object[] { "NiMH", "LiPo", "LiFe", "Alkaline" });
        _cmbChemistry.Text = string.IsNullOrWhiteSpace(Result.Chemistry) ? "NiMH" : Result.Chemistry;

        _txtNotes.Multiline = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        _txtNotes.Text = Result.Notes;

        _chkRetired.Text = "Retired (out of rotation, keeps its history)";
        _chkRetired.AutoSize = true;
        _chkRetired.Checked = Result.Retired;

        var btnSave = new Button { Text = "Save" };
        btnSave.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_txtLabel.Text))
            {
                AppDialog.Show(this, "Give the pack a label.", "Missing label", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Result.Label = _txtLabel.Text.Trim();
            Result.Chemistry = _cmbChemistry.Text.Trim();
            Result.Notes = _txtNotes.Text;
            Result.Retired = _chkRetired.Checked;

            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        var fields = new FieldGrid();
        var captions = new List<Label>
        {
            fields.AddRow("Label:", _txtLabel, stretch: true),
        };

        fields.AddHint("Whatever's written on the pack.");

        captions.Add(fields.AddRow("Type:", _cmbChemistry));
        captions.Add(fields.AddRow("Notes:", _txtNotes, stretch: true));
        fields.AddField(_chkRetired);

        var page = new FillPage();
        page.Add(fields);

        // Against the right-hand edge, Cancel furthest right - and Tab still
        // reaches Save first: the bar takes them right to left, in the order
        // they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);
        btnSave.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        // After the theme pass, in the faces it leaves them in: the notes a
        // few lines of their font, as the box draws them (Pieces.LineHeight);
        // the type as wide as its longest choice, or as the type the pack
        // has when that is one typed in - but never wider than the label and
        // the notes are made (FieldWidth).
        _txtNotes.Height = NotesLines * Pieces.LineHeight(_txtNotes.Font) + UiLayout.HintGap * 2;

        (Font Font, int Count, int Width)? typeWidth = null;

        _cmbChemistry.Width = Math.Max(
            UiLayout.AtTextSize(TypeWidth),
            Math.Min(UiLayout.AtTextSize(FieldWidth), Pieces.PickerWidth(_cmbChemistry, ref typeWidth, _cmbChemistry.Text)));

        // As wide as it was at the normal text size, at the size in use - or
        // wider when the fields' column needs more: wide enough for the
        // Retired tick's caption on one line.
        var fieldColumn = Math.Max(
            UiLayout.AtTextSize(FieldWidth),
            _chkRetired.GetPreferredSize(Size.Empty).Width + _chkRetired.Margin.Horizontal);

        var captionColumn = captions.Max(caption => caption.GetPreferredSize(Size.Empty).Width + caption.Margin.Horizontal);

        var width = Math.Max(
            UiLayout.AtTextSize(DialogWidth),
            UiLayout.PageLeft + captionColumn + fieldColumn + UiLayout.PageRight);

        DialogFit.Keep(this, page, width, widthMeasured: true);
    }
}
