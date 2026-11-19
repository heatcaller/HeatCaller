namespace HeatCaller;

/// <summary>
/// Picks a sanctioning body's rules to apply to a class.
///
/// Most of this dialog is PROSE, which is unusual for a settings screen
/// and is the point of it. The numbers a preset carries are the easy part;
/// what a race director needs before pressing Apply is where they came
/// from, how current they are, and - above all - which parts of the
/// rulebook are NOT in here. A preset that quietly leaves the points scale
/// unset is fine. A preset that leaves it unset and does not say so is how
/// a championship gets scored wrong for a season.
///
/// So the three blocks on the right are fixed furniture: source, what it
/// sets, what it does not. They are not a tooltip and they are not
/// collapsible.
///
/// On the layout pieces (DialogFit): the introduction across the top, and
/// under it the presets down the left (a Beside, the list its column) with
/// the three blocks beside them, each paragraph as tall as the longest any
/// preset has for it - all of that the window's body, which scrolls on a
/// screen too short for it. Under the body, the note about the app default
/// when the class follows it, and Apply and Cancel at the foot. It used to
/// be placed in pixels: the list a fixed 250px, too narrow for the presets'
/// names, and each paragraph five lines of Font.Height, which cut the
/// longest - what PN Racing's preset does not set - short.
///
/// 18 Nov 2026, dialog jazz-up: the presets in a "Presets" card down the
/// left, and the three blocks each a card of its own beside it, in the
/// order they always were - what it does not set no longer in the accent
/// colour, which in most looks is a link's blue and read as one. The
/// introduction a hint line in the small face, the note about the app
/// default in the caution colour, and Apply in the app's colour.
/// </summary>
public sealed class SanctioningPresetForm : Form
{
    /// <summary>The list's width at the normal text size, as it was made - or wider, when a preset's name needs more.</summary>
    private const int ListWidth = 250;

    /// <summary>The column of prose beside the list, at the normal text size - the measure it was made with.</summary>
    private const int ProseWidth = 430;

    /// <summary>The list shows at least this many presets, whatever the prose beside it comes to.</summary>
    private const int LeastRows = 3;

    /// <summary>What is said under the list when applying the preset picked would end the class's following the app default - see ShowDetail.</summary>
    private const string FollowsDefaultNote =
        "Note: this class follows the app's default race format. A preset that sets a duration gives it a "
        + "format of its own, so later changes to the app default stop reaching it.";

    private readonly RaceClass _raceClass;
    private readonly ListBox _list = new();
    private readonly SteadyLabel _lblSource = new();
    private readonly SteadyLabel _lblEncodes = new();
    private readonly SteadyLabel _lblCaveats = new();
    private readonly SteadyLabel _lblBase = new();
    private readonly Button _btnApply = new() { Text = "Apply to This Class" };

    /// <summary>What the applied preset actually changed. Empty until OK.</summary>
    public IReadOnlyList<string> Applied { get; private set; } = new List<string>();

    /// <summary>The preset that was applied, for the caller's status line.</summary>
    public SanctioningPreset? Chosen { get; private set; }

    /// <summary>
    /// What a class that follows the app default is running now, so a
    /// preset changes only the lines it names on top of the club's own
    /// settings. A bare new RaceFormat() here put the built-in minimum lap
    /// time, staging and streak window back on such a class.
    /// </summary>
    private readonly RaceFormat _appDefault;

    /// <summary>How the question before applying ends - see SavingNote.</summary>
    private readonly string _savingNote;

    /// <summary>
    /// What the question before applying says about saving. The class
    /// editor saves with its own Save; the Classes page's button
    /// (<paramref name="savedAtOnce"/>) saves on Yes.
    /// </summary>
    // WHY (13 Nov 2026, bug hunt 17): both doors said "not saved until you
    // press Save on the class itself", and from the Classes page there is no
    // Save - Yes had already written the class's rules to disk.
    internal static string SavingNote(bool savedAtOnce, bool canUndo) =>
        !savedAtOnce ? "This is not saved until you press Save on the class itself."
        : canUndo ? "This is saved straight away; Undo puts it back."
        : "This is saved straight away.";

    public SanctioningPresetForm(RaceClass raceClass, RaceFormat? appDefault = null, bool savedAtOnce = false, bool canUndo = false)
    {
        _savingNote = SavingNote(savedAtOnce, canUndo);
        _raceClass = raceClass;
        _appDefault = appDefault ?? new RaceFormat();

        Text = $"Sanctioning preset - {(string.IsNullOrWhiteSpace(raceClass.Name) ? "class" : raceClass.Name)}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var lblIntro = new Label
        {
            Text = "Applies only the settings the rulebook actually states. Everything else on this class is left "
                 + "exactly as it is - a preset never fills a gap in with a plausible number.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        // Given the height the column has, not snapped to whole rows - see Beside.AddSideFill.
        _list.IntegralHeight = false;
        _list.Font = AppTheme.BodyFont;
        _list.BackColor = AppTheme.Surface;
        _list.SelectedIndexChanged += (_, _) => ShowDetail();

        foreach (var preset in SanctioningPresets.All)
        {
            _list.Items.Add(preset);
        }

        // As wide as it was made, at the text size in use - or as wide as the
        // longest preset's name needs, with room for the bar a list keeps down
        // its right-hand side when it scrolls and a little either side of the
        // text. At 250px the names were cut short - BRCA's losing the
        // "(duration NOT set)" it exists to say. Measured in the list's own
        // face, which the theme pass leaves as it is. The column is a pixel
        // wider on each side, for the ring the theme draws round the list
        // (Pieces.RingRoom).
        var listWidth = UiLayout.AtTextSize(ListWidth);

        foreach (var item in _list.Items)
        {
            listWidth = Math.Max(
                listWidth,
                TextRenderer.MeasureText(_list.GetItemText(item) ?? string.Empty, _list.Font).Width + SystemInformation.VerticalScrollBarWidth + 8);
        }

        // The list in its card, with the card's insets either side of it.
        var listColumn = listWidth + Pieces.RingRoom(_list).Horizontal + Section.Inset.Horizontal;

        // The presets down the left, taking the height the three cards
        // beside them come to; the cards in the rest of the width.
        var band = new Beside(listColumn)
        {
            SideOnLeft = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var presetsCard = new Section("Presets");
        presetsCard.AddFill(_list, () => LeastRows * _list.ItemHeight + 4, gapBelow: 0);
        band.AddSideFill(presetsCard, width => presetsCard.LeastHeight(width), gapBelow: 0);

        AddBlock(band, new Section("Where it came from"), _lblSource, SanctioningPresets.All.Select(p => p.Source), UiLayout.RowGap);
        AddBlock(band, new Section("What it sets"), _lblEncodes, SanctioningPresets.All.Select(p => p.Encodes), UiLayout.RowGap);
        AddBlock(band, new Section("What it does not set"), _lblCaveats, SanctioningPresets.All.Select(p => p.Caveats), gapBelow: 0);

        _lblBase.Font = AppTheme.SmallFont;
        _lblBase.ForeColor = AppTheme.Warning;

        // As tall as the note whether or not the preset picked says it, so
        // moving down the list never moves the buttons. Only a class that
        // follows the app default is ever given the note (ShowDetail), and
        // that cannot change while this is open - so for a class with a
        // format of its own there is no room kept for it at all.
        _lblBase.Wordings = new[] { FollowsDefaultNote };
        _lblBase.Visible = _raceClass.Format is null;

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        _btnApply.Click += (_, _) => Apply();

        // The introduction and the presets are the body: on a screen too
        // short for them they scroll, and the note and the buttons under
        // them stay in view.
        var page = new FillPage();
        var body = DialogFit.Body(page);
        body.Add(lblIntro, gapBelow: UiLayout.RowGap);
        body.Add(band, gapBelow: 0);

        page.Add(_lblBase, gapBelow: UiLayout.RowGap);

        // Against the right-hand edge, Cancel furthest right, as they were -
        // and Tab still reaches Apply first: the bar takes them right to
        // left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, _btnApply), gapBelow: 0);
        _btnApply.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        CancelButton = btnCancel;

        if (_list.Items.Count > 0)
        {
            _list.SelectedIndex = 0;
        }

        ShowDetail();

        AppTheme.ApplyRecursively(this);

        // After the theme pass, which does not know it: Apply in the app's colour.
        AppTheme.StyleButton(_btnApply, AppTheme.ButtonKind.Primary);

        // The list's column, the gap the Beside keeps after it, and the prose
        // at its measure at the text size in use in its cards, inside the
        // page's margins.
        DialogFit.Keep(
            this,
            page,
            UiLayout.PageLeft + listColumn + UiLayout.RowGap + UiLayout.AtTextSize(ProseWidth) + Section.Inset.Horizontal + UiLayout.PageRight,
            body: body,
            widthMeasured: true);
    }

    /// <summary>
    /// One card - its caption the heading - and one wrapped paragraph in it,
    /// in the column beside the list. The paragraph is as tall as the longest of <paramref name="wordings"/>
    /// - what every preset says there - rather than what it says now,
    /// because it changes with the selection, and a paragraph that grew and
    /// shrank as somebody moved down the list would move everything under it
    /// every time.
    /// </summary>
    private static void AddBlock(Beside band, Section card, SteadyLabel body, IEnumerable<string> wordings, int gapBelow)
    {
        body.Font = AppTheme.SmallFont;
        body.ForeColor = AppTheme.TextDark;
        body.Wordings = wordings.ToList();

        card.Add(body, gapBelow: 0);
        band.Add(card, gapBelow: gapBelow);
    }

    private void ShowDetail()
    {
        if (_list.SelectedItem is not SanctioningPreset preset)
        {
            _lblSource.Text = _lblEncodes.Text = _lblCaveats.Text = string.Empty;
            _btnApply.Enabled = false;
            _lblBase.Text = string.Empty;
            return;
        }

        _btnApply.Enabled = true;

        _lblSource.Text = preset.Source;
        _lblEncodes.Text = preset.Encodes;
        _lblCaveats.Text = preset.Caveats;

        // Said out loud only when it is true, because it is the one thing
        // here that is about this app rather than about the rulebook: a
        // class that has never had a format of its own FOLLOWS the app
        // default, and applying a preset that writes a format ends that.
        var writesFormat = preset.Main is not null || preset.Qualifier is not null;
        var followsDefault = _raceClass.Format is null;

        _lblBase.Text = writesFormat && followsDefault
            ? FollowsDefaultNote
            : string.Empty;
    }

    private void Apply()
    {
        if (_list.SelectedItem is not SanctioningPreset preset)
        {
            return;
        }

        // Tried on a copy first. It used to be applied to the class itself
        // before the question was asked, so answering No still changed the
        // class - and that change was saved with the next edit to any class.
        var trial = System.Text.Json.JsonSerializer.Deserialize<RaceClass>(System.Text.Json.JsonSerializer.Serialize(_raceClass)) ?? new RaceClass();
        var changes = preset.ApplyTo(trial, _appDefault);

        if (changes.Count == 0)
        {
            AppDialog.Show(this, "That preset sets nothing this class does not already have.",
                "Nothing to change", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var summary = string.Join("\r\n", changes.Select(c => $"   - {c}"));

        if (AppDialog.Show(this,
                $"Apply {preset.Body} - {preset.Name} to \"{_raceClass.Name}\"?\r\n\r\n{summary}\r\n\r\n"
                + "Everything else on this class is left alone. " + _savingNote,
                "Apply preset", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        preset.ApplyTo(_raceClass, _appDefault);

        Applied = changes;
        Chosen = preset;

        DialogResult = DialogResult.OK;
        Close();
    }
}
