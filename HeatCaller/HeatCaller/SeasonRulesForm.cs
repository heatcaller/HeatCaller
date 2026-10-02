namespace HeatCaller;

/// <summary>
/// What a championship counts, and how it adds it up.
///
/// A dialog rather than another column on the Championship page. These are
/// set once when a season is created and then left alone for months, and
/// the page they would otherwise live on is already three columns of
/// lists that all want the height - a fourth block of settings wedged in
/// there would cost the session list exactly the room it needs and be read
/// twice a year.
///
/// On the layout pieces (DialogFit): under WHAT COUNTS, the automatic tick
/// with its note a step in under it, the class beside its caption, the
/// mains-only and every-race-day ticks, and the race days a step in with
/// the note on them under the list; under HOW IT ADDS UP, the rounds
/// counted with what that means beside the box, and the note on it. All of
/// that is the window's body, which scrolls on a screen too short for it,
/// with Save and Cancel under it, always in view. It used to be placed in
/// pixels: a window 484px wide whatever the text size, the notes wrapped at
/// a fixed 440px and the class a fixed 260px - a longer class name cut
/// short. At a larger text size the words beside the rounds box ran past
/// the window's edge, and the window grew taller than the screen, Save and
/// Cancel under its edge.
/// </summary>
public sealed class SeasonRulesForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size - wider for a line that has to be on one (see the constructor).</summary>
    private const int DialogWidth = 484;

    /// <summary>The class drop-down's width at the normal text size - wider for a longer class name.</summary>
    private const int ClassWidth = 260;

    /// <summary>The rounds box's width at the normal text size.</summary>
    private const int BestWidth = 80;

    /// <summary>How many race days show before the list scrolls, as it always did.</summary>
    private const int EventRows = 5;

    /// <summary>What is said beside the rounds box when it is 0 - the longer of the two things said there.</summary>
    private const string EveryRoundHint = "rounds  -  0 counts every round";

    private readonly Season _season;

    private readonly CheckBox _chkAuto = new ThemedCheckBox();
    private readonly ComboBox _cmbClass = new();
    private readonly CheckBox _chkMainsOnly = new ThemedCheckBox();
    private readonly CheckedListBox _lstEvents = new ThemedCheckedListBox();
    private readonly CheckBox _chkAllEvents = new ThemedCheckBox();
    private readonly NumericUpDown _numBest = new();
    private readonly Label _lblBestHint = new();

    private const string AllClasses = "(every class)";

    public SeasonRulesForm(Season season, RaceClassStore classes, RaceEventStore? events)
    {
        _season = season;

        Text = $"Scoring rules - {(string.IsNullOrWhiteSpace(season.Name) ? "championship" : season.Name)}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var (page, body, classCaption, bestCaption) = BuildLayout(classes, events);
        LoadFromSeason(events);

        AppTheme.ApplyRecursively(this);

        // After the theme pass, in the faces it leaves them in, and after
        // the season is loaded - a class it names that is no longer on file
        // is added to the drop-down then (ClassIndexFor). The class as wide
        // as its longest choice, never narrower than it was at the normal
        // text size; the rounds box the width it had, or wider for its
        // largest value; the race days so many rows of the list.
        (Font Font, int Count, int Width)? classPicker = null;
        var classWidth = Math.Max(UiLayout.AtTextSize(ClassWidth), Pieces.PickerWidth(_cmbClass, ref classPicker));
        _cmbClass.Width = classWidth;

        var bestWidth = Math.Max(
            UiLayout.AtTextSize(BestWidth),
            TextRenderer.MeasureText("00", _numBest.Font).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);
        _numBest.Width = bestWidth;

        _lstEvents.Height = EventRows * _lstEvents.ItemHeight + 4;

        // As wide as it was at the normal text size, at the size in use - or
        // wider for the two lines that have to be on one: the class's caption
        // and its drop-down, which cannot wrap; and the rounds counted - the
        // caption, the box and the longer of the two things said beside it,
        // which would otherwise go under the box. Each as the grid it is in
        // works it out: the caption and the gap after it, then the field -
        // the box and the words each with the gap the row keeps after them.
        // And room beside them for the body's scrollbar, on a screen too
        // short for all of it. Worked out from the widths set above, never
        // read back from the controls: the page has cut them to the room it
        // has before the window is sized.
        var classLine = CaptionWidth(classCaption) + classWidth + _cmbClass.Margin.Horizontal;

        var bestLine = CaptionWidth(bestCaption)
            + bestWidth + _numBest.Margin.Horizontal
            + TextRenderer.MeasureText(EveryRoundHint, _lblBestHint.Font).Width + _lblBestHint.Margin.Horizontal;

        var width = Math.Max(
            UiLayout.AtTextSize(DialogWidth),
            UiLayout.PageLeft + Math.Max(classLine, bestLine) + SystemInformation.VerticalScrollBarWidth + UiLayout.PageRight);

        DialogFit.Keep(this, page, width, body: body, widthMeasured: true);
    }

    /// <summary>A caption's width on one line in its face, with the gap the grid keeps after it.</summary>
    private static int CaptionWidth(Label caption) =>
        caption.GetPreferredSize(Size.Empty).Width + caption.Margin.Horizontal;

    /// <summary>
    /// The page: everything but the buttons in its body, top to bottom, and
    /// Save and Cancel under it. Returns the page and the body, for
    /// DialogFit, and the class's and the rounds' captions, which the
    /// window's width is worked out from.
    /// </summary>
    private (FillPage Page, PageStack Body, Label ClassCaption, Label BestCaption) BuildLayout(RaceClassStore classes, RaceEventStore? events)
    {
        var lblWhat = new Label
        {
            Text = "WHAT COUNTS",
            AutoSize = true,
            Font = AppTheme.MicroFont,
            ForeColor = AppTheme.Primary,
        };

        _chkAuto.Text = "Add new races to this championship automatically";
        _chkAuto.AutoSize = true;
        _chkAuto.Font = AppTheme.BodyFont;
        _chkAuto.CheckedChanged += (_, _) => UpdateEnabled();

        var lblAutoHint = new Label
        {
            Text = "Off means you tick every race by hand, the way this always worked. "
                 + "On means anything matching the rules below joins as it is recorded - "
                 + "and you can still untick a round to force it out.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        // --- Class ---
        _cmbClass.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbClass.Font = AppTheme.BodyFont;
        _cmbClass.Items.Add(AllClasses);

        foreach (var raceClass in classes.All)
        {
            _cmbClass.Items.Add(raceClass.Name);
        }

        var classRow = new FieldGrid();
        var classCaption = classRow.AddRow("Class:", _cmbClass);

        _chkMainsOnly.Text = "Only count mains - leave qualifying heats out";
        _chkMainsOnly.AutoSize = true;
        _chkMainsOnly.Font = AppTheme.BodyFont;

        // --- Race days ---
        _chkAllEvents.Text = "Count every race day";
        _chkAllEvents.AutoSize = true;
        _chkAllEvents.Font = AppTheme.BodyFont;
        _chkAllEvents.CheckedChanged += (_, _) => UpdateEnabled();

        // With no race days on file, the list says so.
        EmptyNote.For(_lstEvents, () => "No race days on file yet. A race day started on Race Day appears here.");

        _lstEvents.CheckOnClick = true;
        _lstEvents.BorderStyle = BorderStyle.None;
        _lstEvents.IntegralHeight = false;
        _lstEvents.Font = AppTheme.BodyFont;

        if (events is not null)
        {
            foreach (var raceEvent in events.Real)
            {
                _lstEvents.Items.Add(new EventChoice(raceEvent, string.Equals(raceEvent.ChampionshipId, _season.Id, StringComparison.Ordinal)));
            }
        }

        // The other way a day joins a championship, said here so the two
        // do not look like they disagree.
        //
        // A day can be entered from its own page - "Championship:" on The
        // Day - and those days count whether or not they are ticked above.
        // They are marked in the list rather than silently ticked: ticking
        // them would write them into this season's own list, and un-
        // ticking one would then look like it had taken it out when the
        // tag on the day would still be counting it.
        var lblTagged = new Label
        {
            Text = "A race day that names this championship on Race Day > Setup counts as well, and is marked \u2713 above.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        // --- Scoring ---
        var lblScoring = new Label
        {
            Text = "HOW IT ADDS UP",
            AutoSize = true,
            Font = AppTheme.MicroFont,
            ForeColor = AppTheme.Primary,
        };

        _numBest.Minimum = 0;
        _numBest.Maximum = 60;
        _numBest.Font = AppTheme.BodyFont;
        _numBest.ValueChanged += (_, _) => UpdateBestHint();

        _lblBestHint.AutoSize = true;
        _lblBestHint.ForeColor = AppTheme.TextMuted;
        _lblBestHint.Font = AppTheme.BodyFont;

        // The box and what it means beside it, going under the box on a line
        // too narrow for both - see the width the constructor works out.
        var bestRow = new FieldGrid();
        var bestCaption = bestRow.AddRow("Count each driver's best:", FieldGrid.Inline(_numBest, _lblBestHint));

        var lblDropHint = new Label
        {
            Text = "A driver who has raced fewer rounds than this drops nothing - "
                 + "missing a round is already its own penalty.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        // --- Buttons ---
        var btnOk = new Button { Text = "Save", DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // Enter presses Save too (AcceptButton), and goes through this same
        // Click, so both meet the check below.
        btnOk.Click += (_, _) =>
        {
            if (!WouldSaveNoRaceDays())
            {
                return;
            }

            AppDialog.Show(
                this,
                "Tick the race days this championship counts, or tick \"Count every race day\".\n\n"
                + "A championship can't be saved with no race day ticked: that is stored the same way as "
                + "\"every race day\", so it would count races from every meeting. "
                + "To count only the days marked \u2713, tick those days.",
                "No race days ticked",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            // Set to OK by the button before this ran; None keeps the window open.
            DialogResult = DialogResult.None;
        };

        // --- Layout ---
        //
        // Every setting in the body: at a larger text size, or on a small
        // screen, more than the window has room for, so it scrolls there and
        // Save and Cancel stay in view under it. The ticks are only as wide
        // as their captions, so the room beside one is not part of it; the
        // headings, the notes and the list run to the right-hand edge, the
        // notes wrapping there.
        var page = new FillPage();
        var body = DialogFit.Body(page);

        body.Add(lblWhat, gapBelow: UiLayout.RowGap);
        body.Add(_chkAuto, stretch: false, gapBelow: UiLayout.HintGap);
        Indented(body.Add(lblAutoHint, gapBelow: UiLayout.RowGap));
        body.Add(classRow, gapBelow: UiLayout.RowGap);
        body.Add(_chkMainsOnly, stretch: false, gapBelow: UiLayout.RowGap);
        body.Add(_chkAllEvents, stretch: false, gapBelow: UiLayout.RowGap);

        // A pixel on the right for the ring the theme draws round the list,
        // outside it (Pieces.RingRoom): at the edge of the body's column it
        // would be drawn where nothing shows.
        body.Add(_lstEvents, gapBelow: UiLayout.HintGap);
        _lstEvents.Margin = new Padding(TickBox.CaptionIndent, 0, 1, UiLayout.HintGap);

        Indented(body.Add(lblTagged, gapBelow: UiLayout.SectionGap));
        body.Add(lblScoring, gapBelow: UiLayout.RowGap);
        body.Add(bestRow, gapBelow: UiLayout.HintGap);
        body.Add(lblDropHint, gapBelow: 0);

        // Against the right-hand edge, Cancel furthest right, as they were -
        // and Tab still reaches Save first: the bar takes them right to left,
        // in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnOk), gapBelow: 0);
        btnOk.TabIndex = 0;
        btnCancel.TabIndex = 1;

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        Controls.Add(page);

        return (page, body, classCaption, bestCaption);

        // A step in, under the tick it belongs to, as it always was - and
        // under its caption once the tick box has grown past the step with
        // the text (TickBox.CaptionIndent). The column measures a part
        // inside its margins, so the part is narrower by the step and wraps
        // to that.
        static void Indented(Control part) =>
            part.Margin = new Padding(TickBox.CaptionIndent, part.Margin.Top, part.Margin.Right, part.Margin.Bottom);
    }

    /// <summary>
    /// One race day in the list. <paramref name="Tagged"/> is true when the
    /// day itself names this championship, which counts it whether or not
    /// it is ticked here - see the note under the list.
    /// </summary>
    private sealed record EventChoice(RaceEvent Event, bool Tagged = false)
    {
        public override string ToString() =>
            $"{Event.DisplayName}  ({Event.StartedUtc.ToLocalTime():d MMM yyyy})"
            + (Tagged ? "  \u2713" : string.Empty);
    }

    private void LoadFromSeason(RaceEventStore? events)
    {
        _chkAuto.Checked = _season.AutoInclude;
        _chkMainsOnly.Checked = _season.MainsOnly;
        _numBest.Value = Math.Clamp(_season.CountBestRounds, 0, 60);

        _cmbClass.SelectedIndex = ClassIndexFor(_season.ClassName);

        _chkAllEvents.Checked = _season.IncludedEventIds.Count == 0;

        for (var i = 0; i < _lstEvents.Items.Count; i++)
        {
            if (_lstEvents.Items[i] is EventChoice choice)
            {
                _lstEvents.SetItemChecked(i, _season.IncludedEventIds.Contains(choice.Event.Id, StringComparer.Ordinal));
            }
        }

        _ = events;

        UpdateBestHint();
        UpdateEnabled();
    }

    /// <summary>Writes the dialog back onto the season. Called by the caller after an OK.</summary>
    public void ApplyTo(Season season)
    {
        season.AutoInclude = _chkAuto.Checked;
        season.MainsOnly = _chkMainsOnly.Checked;
        season.CountBestRounds = (int)_numBest.Value;

        season.ClassName = _cmbClass.SelectedItem switch
        {
            MissingClass missing => missing.Name,
            string name when _cmbClass.SelectedIndex > 0 => name,
            _ => string.Empty,
        };

        // Race days the season names that are not in the list (deleted
        // since, or the legacy holder) are kept, as a missing class is. They
        // cannot be ticked, so a season naming only such days came back from
        // this dialog with an EMPTY list - which means "every race day" -
        // and a one-meeting championship quietly started counting them all.
        var listed = _lstEvents.Items
            .OfType<EventChoice>()
            .Select(c => c.Event.Id)
            .ToHashSet(StringComparer.Ordinal);

        var unlisted = season.IncludedEventIds.Where(id => !listed.Contains(id)).ToList();

        season.IncludedEventIds = _chkAllEvents.Checked
            ? new List<string>()
            : _lstEvents.CheckedItems
                .Cast<object>()
                .OfType<EventChoice>()
                .Select(c => c.Event.Id)
                .Concat(unlisted)
                .Distinct(StringComparer.Ordinal)
                .ToList();
    }

    /// <summary>
    /// True when Save would write an empty race-day list without "Count
    /// every race day" being ticked.
    ///
    /// An empty list is how a season says "every race day" (see
    /// EventSpine.RuleAccepts), and there is no separate switch for "only
    /// the days chosen". So unticking "Count every race day" and ticking
    /// nothing saved exactly what the tick meant: every meeting's races
    /// counted, and the dialog came back with the tick on again. The case a
    /// club meets is a championship whose days are tagged on Race Day >
    /// Setup and marked with a check here: unticking "every race day" to count
    /// only those widened it to all of them instead. Refused rather than
    /// guessed at - writing the tagged days into the list would tie them in
    /// twice, which the note on the list is there to avoid.
    ///
    /// Only while the rule is on. With it off the race-day choices are
    /// greyed out and decide nothing, and a dialog that refused to save
    /// over a setting nobody can change would be a trap.
    /// </summary>
    private bool WouldSaveNoRaceDays()
    {
        if (!_chkAuto.Checked || _chkAllEvents.Checked || _lstEvents.CheckedItems.Count > 0)
        {
            return false;
        }

        // Days the season names that are not in the list are kept by
        // ApplyTo, so with one of those the list is not empty after all.
        var listed = _lstEvents.Items
            .OfType<EventChoice>()
            .Select(c => c.Event.Id)
            .ToHashSet(StringComparer.Ordinal);

        return !_season.IncludedEventIds.Any(id => !listed.Contains(id));
    }

    /// <summary>
    /// Where the season's class sits in the list - matched ignoring case,
    /// the way every other store matches a class name.
    ///
    /// A class that no longer exists (renamed or deleted since) is ADDED,
    /// marked as missing, rather than falling back to "every class". The
    /// fallback looked harmless on screen and then Save wrote it back, so
    /// simply opening and closing this dialog widened a one-class
    /// championship to every class without anybody choosing that.
    /// </summary>
    private int ClassIndexFor(string? className)
    {
        if (string.IsNullOrEmpty(className))
        {
            return 0;
        }

        for (var i = 1; i < _cmbClass.Items.Count; i++)
        {
            if (_cmbClass.Items[i] is string name && string.Equals(name, className, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return _cmbClass.Items.Add(new MissingClass(className));
    }

    /// <summary>A class the season names that is not in the class list any more. Kept, and saved back, as it was.</summary>
    private sealed record MissingClass(string Name)
    {
        public override string ToString() => $"(missing class) {Name}";
    }

    private void UpdateBestHint() =>
        _lblBestHint.Text = _numBest.Value == 0
            ? EveryRoundHint
            : Plural.Word((long)_numBest.Value, "round");

    /// <summary>
    /// Greys out what the current choices make meaningless, rather than
    /// leaving a control that looks live and does nothing.
    /// </summary>
    private void UpdateEnabled()
    {
        var auto = _chkAuto.Checked;

        _cmbClass.Enabled = auto;
        _chkMainsOnly.Enabled = auto;
        _chkAllEvents.Enabled = auto;
        _lstEvents.Enabled = auto && !_chkAllEvents.Checked;
    }
}
