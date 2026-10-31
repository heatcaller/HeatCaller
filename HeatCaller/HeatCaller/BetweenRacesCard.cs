namespace HeatCaller;

/// <summary>
/// THE BETWEEN RACES CARD (31 Oct 2026) on Voice → AI Commentary, after
/// Recap show, so the page still reads in the order the night happens. Every
/// setting the gap after the recap has, in four groups - how much, how often,
/// when, what - with a preset at the top that sets them all, and Hear the gap
/// at the bottom to judge them without running a race. See GapRules for what
/// each one does, and BetweenRaces.cs for the rules themselves.
///
/// Built here rather than inside CommentaryControl, which is long enough, and
/// handed back as a Section with its grids so the page lines its captions up
/// with every other card's (FieldGrid.LineUp).
///
/// The parts that need no model - call-ups, the countdown, custom lines, the
/// hand-over - work with Enable AI commentary off, so nothing here greys out
/// with it; Coming up next and the segments simply have nothing to say then.
/// </summary>
internal sealed class BetweenRacesCard
{
    private readonly AppSettings _settings;
    private readonly TextSize _textSize;
    private readonly Func<GapFacts?>? _tonight;
    private readonly Action<IReadOnlyList<string>>? _play;
    private readonly Func<bool>? _commentaryOn;
    private readonly Func<bool>? _sponsorReads;

    private readonly ComboBox _cmbPreset = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _lblPreset = new();
    private readonly ComboBox _cmbLevel = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _lblLevel = new();
    private readonly ComboBox _cmbLength = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _numMostLines = new() { Minimum = GapRules.FewestLines, Maximum = GapRules.MostLinesAllowed };

    private readonly NumericUpDown _numSpace = new() { Minimum = GapRules.ShortestSpace, Maximum = GapRules.LongestSpace, Increment = 5 };
    private readonly ComboBox _cmbRepeat = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _numCallUps = new() { Minimum = GapRules.FewestCallUps, Maximum = GapRules.MostCallUps };
    private readonly NumericUpDown _numSponsor = new() { Minimum = 1, Maximum = 10 };

    private readonly NumericUpDown _numQuietAfter = new() { Minimum = 0, Maximum = GapRules.LongestQuiet, Increment = 5 };
    private readonly NumericUpDown _numQuietBefore = new() { Minimum = 0, Maximum = GapRules.LongestQuiet, Increment = 5 };
    private readonly CheckBox _chkWithOnAir = new ThemedCheckBox();
    private readonly CheckBox _chkLongBreak = new ThemedCheckBox();
    private readonly NumericUpDown _numLongBreak = new() { Minimum = 5, Maximum = 60, Increment = 5 };

    private readonly CheckBox _chkCallUps = new ThemedCheckBox();
    private readonly CheckBox _chkTwoOut = new ThemedCheckBox();
    private readonly CheckBox _chkMarshals = new ThemedCheckBox();
    private readonly CheckBox _chkNotCheckedIn = new ThemedCheckBox();
    private readonly CheckBox _chkHandOver = new ThemedCheckBox();
    private readonly CheckBox _chkComingUp = new ThemedCheckBox();
    private readonly CheckBox _chkSegments = new ThemedCheckBox();
    private readonly CheckBox _chkClubNotes = new ThemedCheckBox();
    private readonly CheckBox _chkCountdown = new ThemedCheckBox();

    private readonly CheckBox _chkAutoCountdown = new ThemedCheckBox();
    private readonly Dictionary<int, CheckBox> _chkMarks = new();
    private readonly CheckBox _chkTimesUp = new ThemedCheckBox();

    private readonly TextBox _txtCustom = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, AcceptsReturn = true };
    private readonly Button _btnExamples = new() { Text = "Put in Examples" };

    private readonly CheckBox _chkFlagButtons = new ThemedCheckBox();

    private readonly Button _btnHear = new() { Text = "Hear the Gap" };
    private readonly Label _lblRunningOrder = new();

    /// <summary>The repeat choices, in seconds; 0 is Never.</summary>
    private static readonly int[] Repeats = { 30, 45, 60, 90, 120, 180, 240, 300, 0 };

    private bool _loading;

    private BetweenRacesCard(AppSettings settings, TextSize textSize, Func<GapFacts?>? tonight, Action<IReadOnlyList<string>>? play,
        Func<bool>? commentaryOn, Func<bool>? sponsorReads)
    {
        _sponsorReads = sponsorReads;
        _settings = settings;
        _textSize = textSize;
        _tonight = tonight;
        _play = play;
        _commentaryOn = commentaryOn;
    }

    /// <summary>The card, and its grids for the page's caption column.</summary>
    /// <param name="tonight">Tonight's next race for Hear the gap, or null for sample names.</param>
    /// <param name="play">Speaks the preview's lines back to back. Null leaves the button showing the running order only.</param>
    public static (Section Card, IReadOnlyList<FieldGrid> Grids) Build(AppSettings settings, TextSize textSize,
        Func<GapFacts?>? tonight, Action<IReadOnlyList<string>>? play, Func<bool>? commentaryOn, Func<bool>? sponsorReads)
    {
        var card = new BetweenRacesCard(settings, textSize, tonight, play, commentaryOn, sponsorReads);
        return card.Lay();
    }

    private (Section, IReadOnlyList<FieldGrid>) Lay()
    {
        var card = new Section("Between races");
        var grids = new List<FieldGrid>();

        Label Hint(string text) => _textSize.Wear(
            new Label { Text = text, AutoSize = true, ForeColor = AppTheme.TextMuted },
            () => AppTheme.SmallFont);

        Label Suffix(string text) => _textSize.Wear(
            new Label { Text = text, AutoSize = true, ForeColor = AppTheme.TextMuted },
            () => AppTheme.SmallFont);

        void Heading(string text) =>
            card.Add(_textSize.Wear(new Label { Text = text, AutoSize = true, ForeColor = AppTheme.TextDark }, () => AppTheme.BoldFont),
                stretch: false, gapBelow: UiLayout.HintGap);

        FieldGrid Grid()
        {
            var grid = new FieldGrid();
            grids.Add(grid);
            return grid;
        }

        void Tick(CheckBox box, string text)
        {
            box.Text = text;
            box.AutoSize = true;
            box.CheckedChanged += (_, _) => Changed();
        }

        card.Add(Hint("What the announcer says after the recap and before the next race is called. Call-ups, the countdown, "
                      + "the hand-over and your own lines are written by HeatCaller and cost nothing."));

        // --- Preset
        foreach (var preset in GapPreset.All)
        {
            _cmbPreset.Items.Add(preset.Name);
        }

        _cmbPreset.Items.Add("Custom");
        var presetGrid = Grid();
        presetGrid.AddRow("Preset:", _cmbPreset);
        presetGrid.AddHint(_lblPreset);
        _cmbPreset.SelectedIndexChanged += (_, _) => PresetPicked();
        card.Add(presetGrid, gapBelow: UiLayout.SectionGap);

        // --- How much
        Heading("How much");
        foreach (var level in Enum.GetValues<GapLevel>())
        {
            _cmbLevel.Items.Add(level.ToString());
        }

        foreach (var length in Enum.GetValues<GapLineLength>())
        {
            _cmbLength.Items.Add(length.ToString());
        }

        var much = Grid();
        much.AddRow("Between races:", _cmbLevel);
        much.AddHint(_lblLevel);
        much.AddRow("Longest a line can run:", _cmbLength);
        much.AddHint("How long a written line in the gap may be. Short is one sentence; Long lets a form guide run three "
                     + "or four. Call-ups are always short.");
        much.AddRow("Most lines in one gap:", FieldGrid.Inline(_numMostLines, Suffix("call-ups always get through, and count toward it")));
        _cmbLevel.SelectedIndexChanged += (_, _) => { DescribeLevel(); Changed(); };
        _cmbLength.SelectedIndexChanged += (_, _) => Changed();
        _numMostLines.ValueChanged += (_, _) => Changed();
        card.Add(much, gapBelow: UiLayout.SectionGap);

        // --- How often
        Heading("How often");
        foreach (var seconds in Repeats)
        {
            _cmbRepeat.Items.Add(seconds == 0 ? "Never" : GapWords.Duration(seconds));
        }

        var often = Grid();
        often.AddRow("Space between lines:", FieldGrid.Inline(_numSpace, Suffix("seconds")));
        often.AddRow("Repeat the call-up every:", _cmbRepeat);
        often.AddRow("Call-ups per race, at most:", _numCallUps);
        often.AddRow("Sponsor reads, at most one every:", FieldGrid.Inline(_numSponsor, Suffix("minutes (when Sponsors puts reads on rotation)")));
        _numSpace.ValueChanged += (_, _) => Changed();
        _cmbRepeat.SelectedIndexChanged += (_, _) => Changed();
        _numCallUps.ValueChanged += (_, _) => Changed();
        _numSponsor.ValueChanged += (_, _) => Changed();
        card.Add(often, gapBelow: UiLayout.SectionGap);

        // --- When it speaks
        Heading("When it speaks");
        var when = Grid();
        when.AddRow("Quiet after the recap:", FieldGrid.Inline(_numQuietAfter, Suffix("seconds")));
        when.AddRow("Quiet before the call:", FieldGrid.Inline(_numQuietBefore, Suffix("seconds before the next race's run-sheet time")));
        Tick(_chkWithOnAir, "Play call-ups after the recap when On Air opens the next race");
        when.AddField(_chkWithOnAir);
        when.AddHint("The hand-over and Coming up next stay off then, because On Air's opening says the same thing.");
        Tick(_chkLongBreak, "In a long break, say when racing resumes, once");
        when.AddField(FieldGrid.Inline(_chkLongBreak, Suffix("longer than"), _numLongBreak, Suffix("minutes")));
        when.AddHint("Hold Talk (F10) on the flag row stops everything between races until it is pressed again.");
        _numQuietAfter.ValueChanged += (_, _) => Changed();
        _numQuietBefore.ValueChanged += (_, _) => Changed();
        _numLongBreak.ValueChanged += (_, _) => Changed();
        card.Add(when, gapBelow: UiLayout.SectionGap);

        // --- What it says
        Heading("What it says");
        var what = Grid();
        Tick(_chkCallUps, "Call-up: next race to the drivers stand");
        Tick(_chkTwoOut, "Call-up: two races out");
        Tick(_chkMarshals, "Marshals to their posts");
        Tick(_chkNotCheckedIn, "Not checked in (names only drivers the check-in list shows as missing)");
        Tick(_chkHandOver, "Hand-over (\"That was Heat 2. Still to come, Heat 3.\")");
        Tick(_chkComingUp, "Coming up next (written by the commentary)");
        Tick(_chkSegments, "Segments from Through the night");
        Tick(_chkClubNotes, "Club notes");
        Tick(_chkCountdown, "Timetable countdown");

        foreach (var box in new[] { _chkCallUps, _chkTwoOut, _chkMarshals, _chkNotCheckedIn, _chkHandOver, _chkComingUp, _chkSegments, _chkClubNotes, _chkCountdown })
        {
            what.AddField(box);
        }

        what.AddHint("Sponsor reads are switched on or off on the Sponsors page. These boxes only shape the gap - "
                     + "what plays during races is set above.");

        Tick(_chkAutoCountdown, "Start the countdown by itself from the run sheet, at");
        var marks = new List<Control> { _chkAutoCountdown };

        foreach (var mark in GapRules.AllMarks)
        {
            var box = new ThemedCheckBox();
            Tick(box, $"{mark} min");
            _chkMarks[mark] = box;
            marks.Add(box);
        }

        Tick(_chkTimesUp, "time's up");
        marks.Add(_chkTimesUp);
        what.AddField(FieldGrid.Inline(marks.ToArray()));
        what.AddHint("Needs a time against the next race on Race Day → Timetable. Also on the Timetable's Reminder countdown card.");
        card.Add(what, gapBelow: UiLayout.SectionGap);

        // --- Custom lines
        Heading("Your own lines");
        card.Add(Hint("One per line. A line with {round}, {class} or {drivers} becomes the call-up; one with {marshals} the "
                      + "marshal call; the rest are notices, one a gap. Also: {minutes} until the next race, {track}."),
            gapBelow: UiLayout.HintGap);
        _txtCustom.TextChanged += (_, _) => Changed();
        card.Add(_txtCustom, gapBelow: UiLayout.HintGap);
        _btnExamples.Click += (_, _) =>
        {
            var have = GapRules.LinesFrom(_txtCustom.Text.Replace("\r", string.Empty, StringComparison.Ordinal));
            var add = GapWords.Examples.Where(e => !have.Contains(e)).ToList();
            _txtCustom.Text = string.Join(Environment.NewLine, have.Concat(add));
        };
        card.Add(new ButtonBar().With(_btnExamples), stretch: false, gapBelow: UiLayout.SectionGap);

        // --- Flag row
        Tick(_chkFlagButtons, "Show Skip, Hold Talk and Call Up on the flag row after the flag");
        card.Add(_chkFlagButtons, stretch: false, gapBelow: 0);
        card.Add(Hint("F9 skips, F10 holds the talk and F12 calls up the next race whether or not the buttons are showing."),
            gapBelow: UiLayout.SectionGap);

        // --- Hear the gap
        _btnHear.Click += (_, _) => HearTheGap();
        card.Add(new ButtonBar().With(_btnHear), stretch: false, gapBelow: UiLayout.HintGap);
        _lblRunningOrder.Text = "Plays a sample gap in under a minute, and writes down what would be said when.";
        card.Add(_textSize.Wear(_lblRunningOrder, () => AppTheme.SmallFont), gapBelow: 0);
        _lblRunningOrder.AutoSize = true;
        _lblRunningOrder.ForeColor = AppTheme.TextMuted;

        foreach (var label in new[] { _lblPreset, _lblLevel })
        {
            label.AutoSize = true;
            label.ForeColor = AppTheme.TextMuted;
        }

        _textSize.Keep(SizeFields);

        Load(GapRules.From(_settings));
        return (card, grids);
    }

    private void SizeFields()
    {
        foreach (var picker in new[] { _cmbPreset, _cmbLevel, _cmbLength, _cmbRepeat })
        {
            var width = UiLayout.AtTextSize(200);

            if (picker.Width != width)
            {
                picker.Width = width;
            }
        }

        foreach (var number in new[] { _numMostLines, _numSpace, _numCallUps, _numSponsor, _numQuietAfter, _numQuietBefore, _numLongBreak })
        {
            var width = UiLayout.AtTextSize(UiLayout.NumericWidth);

            if (number.Width != width)
            {
                number.Width = width;
            }
        }

        var tall = Pieces.LineHeight(_txtCustom.Font) * 5 + UiLayout.HintGap * 2;

        if (_txtCustom.Height != tall)
        {
            _txtCustom.Height = tall;
        }
    }

    /// <summary>Puts <paramref name="rules"/> on the card, without saving anything back.</summary>
    private void Load(GapRules rules)
    {
        _loading = true;

        try
        {
            _cmbLevel.SelectedIndex = (int)rules.Level;
            _cmbLength.SelectedIndex = (int)rules.Length;
            _numMostLines.Value = Math.Clamp(rules.MostLines, GapRules.FewestLines, GapRules.MostLinesAllowed);
            _numSpace.Value = Math.Clamp(rules.SpaceSeconds, GapRules.ShortestSpace, GapRules.LongestSpace);
            var repeatAt = Array.IndexOf(Repeats, rules.CallUpRepeatSeconds);
            _cmbRepeat.SelectedIndex = repeatAt >= 0 ? repeatAt : Array.IndexOf(Repeats, 120);
            _numCallUps.Value = Math.Clamp(rules.CallUpsPerRace, GapRules.FewestCallUps, GapRules.MostCallUps);
            _numSponsor.Value = Math.Clamp(rules.SponsorEveryMinutes, 1, 10);
            _numQuietAfter.Value = Math.Clamp(rules.QuietAfterRecapSeconds, 0, GapRules.LongestQuiet);
            _numQuietBefore.Value = Math.Clamp(rules.QuietBeforeCallSeconds, 0, GapRules.LongestQuiet);
            _chkWithOnAir.Checked = rules.CallUpsWithOnAir;
            _chkLongBreak.Checked = rules.LongBreakNotice;
            _numLongBreak.Value = Math.Clamp(rules.LongBreakMinutes, 5, 60);
            _chkCallUps.Checked = rules.CallUps;
            _chkTwoOut.Checked = rules.TwoOut;
            _chkMarshals.Checked = rules.Marshals;
            _chkNotCheckedIn.Checked = rules.NotCheckedIn;
            _chkHandOver.Checked = rules.HandOver;
            _chkComingUp.Checked = rules.ComingUpNext;
            _chkSegments.Checked = rules.Segments;
            _chkClubNotes.Checked = rules.ClubNotes;
            _chkCountdown.Checked = rules.Countdown;
            _chkAutoCountdown.Checked = rules.AutoCountdown;

            foreach (var (mark, box) in _chkMarks)
            {
                box.Checked = rules.CountdownMarks.Contains(mark);
            }

            _chkTimesUp.Checked = rules.CountdownTimesUp;
            _txtCustom.Text = string.Join(Environment.NewLine, rules.CustomLines);
            _chkFlagButtons.Checked = _settings.BetweenRacesFlagButtons;

            ShowPreset(rules);
            DescribeLevel();
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>The card as it stands, as rules.</summary>
    private GapRules Read() => new()
    {
        Level = (GapLevel)Math.Max(0, _cmbLevel.SelectedIndex),
        Length = (GapLineLength)Math.Max(0, _cmbLength.SelectedIndex),
        MostLines = (int)_numMostLines.Value,
        SpaceSeconds = (int)_numSpace.Value,
        CallUpRepeatSeconds = _cmbRepeat.SelectedIndex >= 0 ? Repeats[_cmbRepeat.SelectedIndex] : 120,
        CallUpsPerRace = (int)_numCallUps.Value,
        SponsorEveryMinutes = (int)_numSponsor.Value,
        QuietAfterRecapSeconds = (int)_numQuietAfter.Value,
        QuietBeforeCallSeconds = (int)_numQuietBefore.Value,
        CallUpsWithOnAir = _chkWithOnAir.Checked,
        LongBreakNotice = _chkLongBreak.Checked,
        LongBreakMinutes = (int)_numLongBreak.Value,
        AutoCountdown = _chkAutoCountdown.Checked,
        CountdownMarks = _chkMarks.Where(m => m.Value.Checked).Select(m => m.Key).OrderByDescending(m => m).ToList(),
        CountdownTimesUp = _chkTimesUp.Checked,
        CallUps = _chkCallUps.Checked,
        TwoOut = _chkTwoOut.Checked,
        Marshals = _chkMarshals.Checked,
        NotCheckedIn = _chkNotCheckedIn.Checked,
        HandOver = _chkHandOver.Checked,
        ComingUpNext = _chkComingUp.Checked,
        Segments = _chkSegments.Checked,
        ClubNotes = _chkClubNotes.Checked,
        Countdown = _chkCountdown.Checked,
        CustomLines = GapRules.LinesFrom(_txtCustom.Text.Replace("\r", string.Empty, StringComparison.Ordinal)),
    };

    /// <summary>Any setting changed: saved straight away, as the rest of the page saves, and the preset shown again.</summary>
    private void Changed()
    {
        if (_loading)
        {
            return;
        }

        var rules = Read();
        rules.SaveTo(_settings);
        _settings.BetweenRacesFlagButtons = _chkFlagButtons.Checked;
        _settings.Save();

        _loading = true;

        try
        {
            ShowPreset(rules);
        }
        finally
        {
            _loading = false;
        }
    }

    private void PresetPicked()
    {
        if (_loading || _cmbPreset.SelectedIndex < 0 || _cmbPreset.SelectedIndex >= GapPreset.All.Count)
        {
            return;
        }

        var rules = GapPreset.All[_cmbPreset.SelectedIndex].Over(Read());
        rules.SaveTo(_settings);
        _settings.Save();
        Load(rules);
    }

    private void ShowPreset(GapRules rules)
    {
        var matching = GapPreset.Matching(rules);
        _cmbPreset.SelectedIndex = matching is null ? GapPreset.All.Count : GapPreset.All.ToList().IndexOf(matching);
        _lblPreset.Text = matching is null
            ? "Set by hand. Pick a preset to set everything below in one go."
            : matching.What + " A race day can use a different preset on Race Day → Setup.";
    }

    private void DescribeLevel()
    {
        _lblLevel.Text = (GapLevel)Math.Max(0, _cmbLevel.SelectedIndex) switch
        {
            GapLevel.Quiet => "Call-ups, marshals and the countdown only.",
            GapLevel.Light => "Call-ups and the hand-over, plus your own notices.",
            GapLevel.Busy => "Everything ticked below, two more lines a gap, and sponsor reads twice as often.",
            _ => "Everything ticked below. How chatty, above, still governs the races themselves.",
        };
    }

    /// <summary>Works the sample gap out with these settings, writes its running order and plays it.</summary>
    private void HearTheGap()
    {
        var rules = Read();
        var now = DateTime.UtcNow;
        var facts = _tonight?.Invoke() is { HasNext: true } real
            ? real with { DueUtc = real.DueUtc is { } due && due > now ? due : now + TimeSpan.FromMinutes(3) }
            : GapPreview.Sample(now, TimeSpan.FromMinutes(3));

        // Run as if the due time sat three minutes out at most, so a heat
        // an hour away still previews a normal gap rather than a long break
        // that says nothing for an hour. A long break is previewed when the
        // run sheet really has one.
        var steps = GapPreview.Run(rules, facts, now, withTheDirector: true, commentaryOn: _commentaryOn?.Invoke() ?? true,
            sponsorReads: _sponsorReads?.Invoke() ?? false, onAirOpensTheNextRace: false);

        _lblRunningOrder.Text = GapPreview.RunningOrder(steps);

        if (_play is not null && steps.Count > 0)
        {
            _play(steps.Select(s => s.Text).ToList());
        }
    }
}
