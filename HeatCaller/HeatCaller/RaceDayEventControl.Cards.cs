using System.Globalization;

namespace HeatCaller;

/// <summary>
/// Tonight's summary cards (17 Nov 2026, mock-up B in
/// plans/tonight-redesign-mockups.html). The night's settings used to be
/// five long cards of fields with a paragraph of help under most of them,
/// the fields stopping halfway across a wide window. Now a banner says what
/// tonight is and what the phones are told, and each card says in a few
/// lines what is set - Change opens its fields in place, Done folds them
/// back. On a wide window the cards go in two columns, and the whole night
/// fits on one screen.
///
/// The fields are the same ones (NightFields, shared with Edit Template)
/// and save as they always have; the summaries only read them.
/// </summary>
public sealed partial class RaceDayEventControl
{
    // --- The banner ---
    private readonly Label _lblHeroName = new() { AutoSize = true, UseMnemonic = false };
    private readonly Label _lblHeroMeta = new() { AutoSize = true, UseMnemonic = false, ForeColor = AppTheme.TextMuted };

    // --- The times: one tile each, in the order the night goes ---
    private readonly StatTile _tileEntries = new();
    private readonly StatTile _tileFirstRace = new();
    private readonly StatTile _tileBreaks = new();
    private readonly StatTile _tileOutBy = new();

    // --- The night ---
    private readonly Label _sumKind = SummaryValue();
    private readonly Label _sumVenue = SummaryValue();
    private readonly Label _sumLayout = SummaryValue();
    private readonly Label _sumChampionship = SummaryValue();
    private readonly Label _sumNotes = SummaryValue();

    // --- Heats ---
    private FlowLayoutPanel? _sumClasses;
    private readonly Label _sumHeats = SummaryValue();
    private readonly Label _sumSeeding = SummaryValue();
    private readonly Label _sumOrder = SummaryValue();

    // --- Money and running the night ---
    private readonly Label _sumMoney = SummaryValue();
    private readonly Label _sumNextRace = SummaryValue();
    private readonly Label _sumBetween = SummaryValue();

    private Label? _lblHeatsCaption;

    /// <summary>The cards that fold, for the tests and for the text size.</summary>
    private readonly List<FoldingCard> _folds = new();

    /// <summary>The summaries' grids, lined up together.</summary>
    private readonly List<FieldGrid> _summaryGrids = new();

    private static Label SummaryValue() => new() { AutoSize = true, UseMnemonic = false };

    /// <summary>Tonight's folding cards, in the order they were made: The night, Times, Heats, Money, Running the night.</summary>
    internal IReadOnlyList<FoldingCard> TonightCards => _folds;

    /// <summary>The banner's name and its line under it.</summary>
    internal string HeroName => _lblHeroName.Text;

    internal string HeroMeta => _lblHeroMeta.Text;

    /// <summary>The Times card's tiles, as they read: entries, first race, breaks, out by.</summary>
    internal IReadOnlyList<string> TimesSummary =>
        new[] { _tileEntries, _tileFirstRace, _tileBreaks, _tileOutBy }.Select(tile => tile.Value.Text).ToList();

    /// <summary>The Heats card's class pills.</summary>
    internal IReadOnlyList<string> ClassesSummary =>
        _sumClasses?.Controls.OfType<InboxPill>().Select(pill => pill.Text).ToList() ?? new List<string>();

    internal string MoneySummary => _sumMoney.Text;

    /// <summary>The shared fields, for a test that changes one and reads the summary.</summary>
    internal NightFields TonightFields => _fields;

    /// <summary>The banner: tonight's name, what kind of night, and the phones' next night with Announce and Call It Off.</summary>
    private Section BuildHero()
    {
        _textSize.Wear(_lblHeroName, () => AppTheme.TitleFont);

        AppTheme.StyleButton(_btnAnnounce, AppTheme.ButtonKind.Primary);

        // Quiet beside Announce, and not red: Announce takes it back.
        AppTheme.StyleButton(_btnCallOff, AppTheme.ButtonKind.Secondary);

        var phones = new Label { Text = "Next night on phones:", AutoSize = true, ForeColor = AppTheme.TextMuted };

        var hero = new Section();

        hero.Add(_lblHeroName, gapBelow: UiLayout.HintGap);
        hero.Add(_lblHeroMeta, gapBelow: UiLayout.RowGap);
        hero.Add(BuildNextNightRow(phones), gapBelow: UiLayout.HintGap);

        var hint = hero.Add(new Label
        {
            Text = "Shows the next race night on the Home page of the club's phone pages so drivers can say they're coming. Call It Off tells them it's off, and emails the regulars.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        }, gapBelow: 0);

        _textSize.Wear(hint, () => AppTheme.SmallFont);

        return hero;
    }

    /// <summary>The times as four tiles, in the order the night goes. Short captions: a tile is a quarter of half the page.</summary>
    private TileStrip BuildTimesSummary()
    {
        var tiles = new[] { _tileEntries, _tileFirstRace, _tileBreaks, _tileOutBy };

        foreach (var tile in tiles)
        {
            // The heading face, not the title one: in half a page a tile is
            // about 150px, and "5:00 PM" in the title face lost its "PM" to
            // the ellipsis. Centred in the room the strip keeps for a value.
            tile.Value.TextAlign = ContentAlignment.MiddleLeft;
            _textSize.Wear(tile.Value, () => AppTheme.HeadingFont);
            _textSize.Wear(tile.Caption, () => AppTheme.MicroFont);
        }

        return new TileStrip(tiles);
    }

    private FieldGrid BuildNightSummary() => SummaryGrid(
        ("Kind:", _sumKind),
        ("Venue:", _sumVenue),
        ("Track layout:", _sumLayout),
        ("Championship:", _sumChampionship),
        ("Notes:", _sumNotes));

    private FieldGrid BuildHeatsSummary()
    {
        _sumClasses = FieldGrid.Inline();

        var grid = SummaryGrid(
            ("Classes:", _sumClasses),
            ("Heats:", _sumHeats),
            ("Seeding:", _sumSeeding),
            ("Running order:", _sumOrder));

        _lblHeatsCaption = grid.Controls.OfType<Label>().FirstOrDefault(label => label.Text == "Heats:");

        return grid;
    }

    private FieldGrid BuildMoneySummary() => SummaryGrid(("Entry:", _sumMoney));

    private FieldGrid BuildRunSummary() => SummaryGrid(
        ("Next race:", _sumNextRace),
        ("Between races:", _sumBetween));

    private FieldGrid SummaryGrid(params (string Caption, Control Value)[] rows)
    {
        var grid = new FieldGrid();

        foreach (var (caption, value) in rows)
        {
            var label = grid.AddRow(caption, value, stretch: true);

            label.ForeColor = AppTheme.TextMuted;
        }

        _summaryGrids.Add(grid);
        return grid;
    }

    /// <summary>A card that shows its summary until Change is pressed, and its fields until Done is.</summary>
    private FoldingCard Fold(string caption, Control summary, Control fields)
    {
        var fold = new FoldingCard(caption, summary, fields, _textSize);

        _folds.Add(fold);
        return fold;
    }

    /// <summary>
    /// The banner and every summary, from the fields as they are now - so
    /// what a summary says is always what Change would show. Run when the
    /// day is loaded and whenever a field changes.
    /// </summary>
    private void ShowSummaries()
    {
        _lblHeroName.Text = string.IsNullOrWhiteSpace(_txtName.Text) ? "Tonight" : _txtName.Text.Trim();

        var classes = _fields.CheckedClasses;
        var championship = _fields.Championship.SelectedIndex > 0 ? _fields.Championship.Text : "No championship";
        var clubNight = _fields.SelectedShape == RaceDayShape.ClubNight;

        _lblHeroMeta.Text = string.Join(" · ", new[]
        {
            Words(_fields.Shape.Text),
            championship,
            classes.Count switch
            {
                0 => "every class with a driver",
                1 => "1 class",
                _ => $"{classes.Count} classes",
            },
        });

        // The times.
        ShowTime(_tileEntries, "Entries", _fields.EntriesClose, _fields.EntriesCloseSet);
        ShowTime(_tileFirstRace, "First race", _fields.FirstRace, _fields.FirstRaceSet);
        _tileBreaks.Set(Words(_fields.BreaksLine.Text), "Breaks");
        _tileBreaks.Value.ForeColor = _tileBreaks.Value.Text == "None" ? AppTheme.TextMuted : AppTheme.TextDark;
        ShowTime(_tileOutBy, "Out by", _fields.OutBy, _fields.OutBySet);

        // The night.
        _sumKind.Text = Words(_fields.Shape.Text);
        _sumVenue.Text = OrNone(_fields.Venue.Text);
        _sumLayout.Text = OrNone(_fields.Layout.Text);
        _sumChampionship.Text = _fields.Championship.SelectedIndex > 0 ? _fields.Championship.Text : "Standalone";
        _sumNotes.Text = OrNone(FirstLine(_txtNotes.Text));

        // Heats.
        ShowClassPills(classes);

        if (_lblHeatsCaption is not null)
        {
            _lblHeatsCaption.Text = clubNight ? "Races:" : "Heats:";
        }

        _sumHeats.Text = clubNight
            ? $"{_fields.Rounds.Value:0} each · {_fields.HeatSize.Value:0} per race"
            : $"{_fields.Rounds.Value:0} each · {_fields.HeatSize.Value:0} per heat";
        _sumSeeding.Text = Words(_fields.FirstRound.Text);
        _sumOrder.Text = Words(_fields.ClassOrderLine.Text);

        // Money.
        _sumMoney.Text = _fields.EntryFee.Value == 0 && _fields.ExtraFee.Value == 0
            ? "Free"
            : $"{Money(_fields.EntryFee.Value)} · {Money(_fields.ExtraFee.Value)} each extra class";

        // Running the night.
        _sumNextRace.Text = _chkAutoAdvance.Checked ? "Starts itself" : "Started by hand";
        _sumBetween.Text = Words(_fields.GapPreset.Text);

        static string OrNone(string text) => string.IsNullOrWhiteSpace(text) ? "None" : text.Trim();

        static string Words(string text) => string.IsNullOrWhiteSpace(text) ? "-" : text.Trim();

        static string Money(decimal amount) => amount.ToString("C", CultureInfo.CurrentCulture);

        static string FirstLine(string text)
        {
            var line = (text ?? string.Empty).Replace("\r", string.Empty).Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? string.Empty;
            var more = (text ?? string.Empty).Trim().Length > line.Length;

            return more ? line + " ..." : line;
        }
    }

    /// <summary>A time's tile: the time, or "Not set" in the quiet colour, so a real time stands out from a missing one.</summary>
    private static void ShowTime(StatTile tile, string caption, TimePicker picker, CheckBox set)
    {
        tile.Set(set.Checked ? DateTime.Today.Add(picker.Value).ToString("t", CultureInfo.CurrentCulture) : "Not set", caption);
        tile.Value.ForeColor = set.Checked ? AppTheme.TextDark : AppTheme.TextMuted;
    }

    /// <summary>The classes racing as pills - remade only when the list changed, so a save doesn't lay the card out for nothing.</summary>
    private void ShowClassPills(List<string> classes)
    {
        if (_sumClasses is null)
        {
            return;
        }

        var shown = _sumClasses.Controls.OfType<InboxPill>().Select(pill => pill.Text).ToList();
        var wanted = classes.Count == 0 ? new List<string> { "Every class with a driver" } : classes;

        if (shown.SequenceEqual(wanted, StringComparer.Ordinal))
        {
            return;
        }

        _sumClasses.SuspendLayout();

        try
        {
            foreach (var pill in _sumClasses.Controls.OfType<InboxPill>().ToList())
            {
                _sumClasses.Controls.Remove(pill);
                pill.Dispose();
            }

            foreach (var name in wanted)
            {
                _sumClasses.Controls.Add(new InboxPill
                {
                    Text = name,
                    Tone = classes.Count == 0 ? PillTone.Plain : PillTone.Accent,
                    Dot = false,
                    Anchor = AnchorStyles.Left,
                });
            }
        }
        finally
        {
            _sumClasses.ResumeLayout(true);
        }
    }

    /// <summary>
    /// One card that folds: its caption and Change (or Done) on the top
    /// line, and under it either the summary or the fields. Starts folded.
    /// </summary>
    internal sealed class FoldingCard
    {
        private readonly Button _toggle = new() { Text = "Change", AutoSize = true };
        private readonly Control _summary;
        private readonly Control _fields;
        private readonly string _name;
        private bool _open;

        public FoldingCard(string caption, Control summary, Control fields, TextSize textSize)
        {
            _summary = summary;
            _fields = fields;
            _name = caption.Trim();

            // The caption as Section draws its own: upper case, spaced.
            var text = _name.ToUpperInvariant();
            var label = new Label
            {
                Text = text.Length <= 28 ? AppTheme.Spaced(text) : text,
                AutoSize = true,
                ForeColor = AppTheme.TextMuted,
                UseMnemonic = false,
                AccessibleName = _name,
            };

            textSize.Wear(label, () => AppTheme.MicroFont);
            AppTheme.StyleButton(_toggle, AppTheme.ButtonKind.Secondary);
            _toggle.Click += (_, _) => Open = !Open;

            Card.Add(new SplitBar((ButtonBar)FieldGrid.Inline(label), (ButtonBar)FieldGrid.Inline(_toggle)), gapBelow: UiLayout.HeadingGap);
            Card.Add(summary, gapBelow: 0);
            Card.Add(fields, gapBelow: 0);

            Apply();
        }

        public Section Card { get; } = new();

        /// <summary>The Change / Done button.</summary>
        public Button Toggle => _toggle;

        /// <summary>The card's caption, as written.</summary>
        public string Name => _name;

        /// <summary>Whether the fields are showing rather than the summary.</summary>
        public bool Open
        {
            get => _open;
            set
            {
                if (_open == value)
                {
                    return;
                }

                // Off the field first, so one being typed in saves as it
                // loses focus, before it is hidden.
                if (!value && _fields.ContainsFocus)
                {
                    _toggle.Focus();
                }

                _open = value;
                Apply();
            }
        }

        private void Apply()
        {
            _summary.Visible = !_open;
            _fields.Visible = _open;
            _toggle.Text = _open ? "Done" : "Change";
            _toggle.AccessibleName = (_open ? "Done with " : "Change ") + _name;
        }
    }
}
