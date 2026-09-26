namespace HeatCaller;

/// <summary>
/// The Announcements page - one row per announcement the race can make,
/// presented the way Sound Effects presents its rows.
/// </summary>
/// <remarks>
/// WHY THIS IS A PARTIAL OF VoiceSettingsControl RATHER THAN A CONTROL OF
/// ITS OWN.
///
/// These rows are part of the voice profile. Every one of them is written
/// by BuildProfileFromControls, alongside the voice, the model and the
/// tuning parameters, into a single VoiceProfile that is saved as a whole -
/// and read back by TestEventLine so a phrasing can be heard with the
/// voice currently chosen two tabs away. Splitting that across two classes
/// would mean either two save paths racing each other over one file, or one
/// class reaching into the other's controls, which is the same coupling
/// with more ceremony.
///
/// So the class stays whole and only the PAGE is separate: this file builds
/// a standalone panel that the host puts in its own tab, and the fields,
/// the save and the test path go on working exactly as they did.
///
/// WHAT CHANGED FROM THE OLD "Announcements" GROUP BOX.
///
/// It used to be the fifth group at the bottom of Voice Settings, below the
/// engine, the connection, the parameters and the line library - a long way
/// below the fold on a page that already scrolled. And it had a mode: an
/// "Editing: Race announcements / Practice announcements" combo that swapped
/// every tick on the page between two remembered sets. One tick box, two
/// meanings, and no way to see both at once or to tell which you were
/// looking at without reading the combo.
///
/// Sound Effects had already solved this: Race and Practice are two tick
/// COLUMNS on the row, both visible, both editable, no mode to be in. That
/// is what this now does, and the combo is gone. Nothing is lost in the
/// move - the templates were always shared between the two contexts, so the
/// combo only ever switched the ticks and the streak cadence, and both of
/// those now have a column and a box each.
/// </remarks>
public sealed partial class VoiceSettingsControl
{
    /// <summary>The page itself. The host parents this in its own tab; see the class remarks.</summary>
    private readonly Panel _announcementsPage = new() { Dock = DockStyle.Fill };

    /// <summary>The Announcements page, for the host to put in a tab of its own.</summary>
    public Control AnnouncementsPage => _announcementsPage;

    /// <summary>The scrolling band the rows live in - held so a rebuild can refill it.</summary>
    private Panel? _annRows;

    /// <summary>This page's own status line. See MirrorTestStatus.</summary>
    private readonly Label _lblAnnouncementStatus = new();

    /// <summary>
    /// The streak cadence for practice.
    ///
    /// There used to be one box and two remembered numbers, swapped by the
    /// context combo - which meant the box showed the race value while the
    /// page claimed to be editing practice, and the next save wrote it over
    /// the practice one. Two boxes, two values, nothing to remember.
    /// </summary>
    private readonly NumericUpDown _numStreakEveryNPractice = new();

    /// <summary>Space between one column and the next.</summary>
    private const int AnnColumnGap = 12;

    /// <summary>Space above and below a row's contents - the gap between rows is twice this.</summary>
    private const int AnnRowPad = 8;

    /// <summary>A tick box with no caption of its own, sitting under a column heading.</summary>
    private const int AnnTickWidth = 18;

    /// <summary>How wide the phrasing box is. Three lines tall; more scrolls inside it.</summary>
    private const int AnnPhrasingWidth = 430;

    /// <summary>
    /// Every x on a row, worked out once from the content rather than
    /// written down. Same shape as SoundEffectsControl.Columns, and for the
    /// same reason: a remembered x is right for one font and wrong for the
    /// next one, and thirty rows of wrong is what the layout repair pass
    /// spends a startup undoing.
    /// </summary>
    private sealed class AnnouncementColumns
    {
        public int Race;
        public int RaceWidth;
        public int Practice;
        public int PracticeWidth;
        public int Name;
        public int NameWidth;
        public int Phrasing;
        public int PhrasingWidth;
        public int Weight;
        public int WeightWidth;
        public int Speed;
        public int SpeedWidth;
        public int Caution;
        public int CautionWidth;
        public int Test;
        public int TestWidth;

        /// <summary>How far an extra control under the phrasing box may run.</summary>
        public int ExtraWidth;

        public int ControlHeight;

        /// <summary>The right-hand edge of a row, for the hairline drawn under it.</summary>
        public int Right => Test + TestWidth;
    }

    private readonly AnnouncementColumns _annCol = new();

    private static int AnnTextWidth(string text, Font font) =>
        TextRenderer.MeasureText(
            text,
            font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;

    /// <summary>The announcements with a row, in the order they are shown.</summary>
    /// <remarks>
    /// Reminder is left out deliberately: it speaks fully dynamic text ("5
    /// minutes to Stock Buggy A-Main") rather than picking from configured
    /// lines, so it has nothing to edit here and would render as an empty
    /// row that looks broken.
    /// </remarks>
    private static AnnouncementEventType[] AnnouncementTypes() =>
        Enum.GetValues<AnnouncementEventType>()
            .Where(t => t != AnnouncementEventType.Reminder)
            .ToArray();

    private void MeasureAnnouncementColumns()
    {
        var c = _annCol;
        var heading = AppTheme.MicroFont;

        c.Race = UiLayout.PageLeft;
        c.RaceWidth = Math.Max(AnnTickWidth, AnnTextWidth("Race", heading));

        c.Practice = c.Race + c.RaceWidth + AnnColumnGap;
        c.PracticeWidth = Math.Max(AnnTickWidth, AnnTextWidth("Practice", heading));

        c.Name = c.Practice + c.PracticeWidth + AnnColumnGap + 4;

        // Wide enough for the longest announcement name, and no wider. The
        // old row put the name at a literal x=36 and the template box at
        // x=160, and at this body size a name like "Qualifying improvement"
        // is wider than that gap - so the name ran into the box on every
        // row, thirty overlaps for the repair pass to find on every launch.
        var names = AnnouncementTypes()
            .Select(t => AnnTextWidth(VoiceProfile.Describe(t).Label, AppTheme.BoldFont))
            .DefaultIfEmpty(140)
            .Max();

        c.NameWidth = Math.Clamp(names, 140, 260);

        c.Phrasing = c.Name + c.NameWidth + AnnColumnGap;
        c.PhrasingWidth = AnnPhrasingWidth;

        c.Weight = c.Phrasing + c.PhrasingWidth + AnnColumnGap;
        c.WeightWidth = Math.Max(UiLayout.NumericWidth, AnnTextWidth("Weight", heading));

        c.Speed = c.Weight + c.WeightWidth + AnnColumnGap;
        c.SpeedWidth = Math.Max(UiLayout.NumericWidth, AnnTextWidth("Speed", heading));

        c.Caution = c.Speed + c.SpeedWidth + AnnColumnGap;
        c.CautionWidth = Math.Max(AnnTickWidth, AnnTextWidth("Caution", heading));

        c.Test = c.Caution + c.CautionWidth + AnnColumnGap;
        c.TestWidth = AnnTextWidth("Test", AppTheme.BoldFont) + UiLayout.ButtonPaddingX * 2;

        // An extra control under the phrasing box may run wider than the box
        // - the lap-time cadence row and the position-report row both do -
        // but never as far as the Test column. Weight, Speed and Caution sit
        // on the row's top line, so running under them costs nothing.
        c.ExtraWidth = Math.Max(260, c.Test - AnnColumnGap - c.Phrasing);

        c.ControlHeight = Math.Max(30, AppTheme.BoldFont.Height + 12);
    }

    /// <summary>
    /// Builds the whole page: a header band, the scrolling rows, and a
    /// status line along the bottom.
    /// </summary>
    private void BuildAnnouncementsPage(VoiceProfile current)
    {
        MeasureAnnouncementColumns();

        var header = BuildAnnouncementsHeader(current);

        var rows = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            AutoScrollMargin = new Size(UiLayout.PageRight, AnnRowPad + 14),
        };

        _annRows = rows;

        // The column headings stay on screen while the rows scroll under
        // them, and follow the rows sideways. The frame carries the border
        // the rows panel used to. See StickyHeadings.
        var list = StickyHeadings.Wrap(rows, BuildAnnouncementHeadings());

        _lblAnnouncementStatus.AutoSize = true;
        _lblAnnouncementStatus.ForeColor = AppTheme.TextMuted;
        _lblAnnouncementStatus.Font = AppTheme.BodyFont;
        _lblAnnouncementStatus.Location = new Point(UiLayout.PageLeft, UiLayout.RowGap);
        _lblAnnouncementStatus.Text = "Changes save as you make them.";

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = AppTheme.BodyFont.Height + UiLayout.RowGap * 2,
        };

        footer.Controls.Add(_lblAnnouncementStatus);

        // Fill first: WinForms resolves docking from the last child
        // backwards, so whatever is added first is left with the space the
        // others did not take.
        _announcementsPage.Controls.Add(list);
        _announcementsPage.Controls.Add(footer);
        _announcementsPage.Controls.Add(header);

        FillAnnouncementRows(current);

        // The page is not a child of this control, so the theming pass that
        // walks this one never reaches it.
        AppTheme.ApplyRecursively(_announcementsPage);
    }

    private Control BuildAnnouncementsHeader(VoiceProfile current)
    {
        var hintFont = AppTheme.SmallFont;
        var y = UiLayout.PageTop;
        var width = Math.Max(520, _annCol.Phrasing + _annCol.PhrasingWidth - UiLayout.PageLeft);

        var header = new Panel { Dock = DockStyle.Top };

        var lblLegend = new Label
        {
            Text = "Placeholders you can use in a line: {driver} {car} {team} {sponsors} {lap} {time} {laptime} {gate}",
            Location = new Point(UiLayout.PageLeft, y),
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            Font = AppTheme.BodyFont,
            ForeColor = AppTheme.TextMuted,
        };

        var lblLegend2 = new Label
        {
            Text = "One phrasing per line - a random one is picked each time the announcement fires, so it doesn't repeat "
                 + "the same sentence back to back. WEIGHT decides which announcement wins when several fire on the same "
                 + "lap; SPEED multiplies the voice's speaking rate, and is worth lowering for long lines like the "
                 + "running order. The lines themselves are shared between races and practice - only the ticks differ.",
            Location = new Point(UiLayout.PageLeft, UiLayout.Below(lblLegend, UiLayout.HintGap)),
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            Font = hintFont,
            ForeColor = AppTheme.TextMuted,
        };

        y = UiLayout.Below(lblLegend2, UiLayout.RowGap);

        // Kept as a field so the HeatCaller voice can switch it off with the
        // wording it restores: with those lines fixed there is nothing to
        // restore. See UpdateEngineControls.
        var btnRestore = new Button { Text = "Restore Default Lines..." };
        btnRestore.Size = new Size(UiLayout.WidthFor(btnRestore), UiLayout.ButtonHeight);
        btnRestore.Location = new Point(UiLayout.PageLeft, y);
        btnRestore.Click += BtnRestoreLines_Click;
        _btnRestoreLines = btnRestore;

        var lblRestoreHint = new Label
        {
            Text = "Replaces every line below with the built-in set. Your own wording is lost; ticks, weights and "
                 + "speeds are kept.",
            AutoSize = true,
            MaximumSize = new Size(Math.Max(240, width - UiLayout.After(btnRestore) - UiLayout.RowGap), 0),
            Font = hintFont,
            ForeColor = AppTheme.TextMuted,
        };

        lblRestoreHint.Location = new Point(
            UiLayout.After(btnRestore) + UiLayout.RowGap,
            UiLayout.CentreOn(lblRestoreHint, y, UiLayout.ButtonHeight));

        y = Math.Max(
            UiLayout.Below(btnRestore, UiLayout.RowGap),
            UiLayout.Below(lblRestoreHint, UiLayout.RowGap));

        // Worked out from the saved profile when the page is built, like the
        // rest of it - so it follows a change of model or voice the next
        // time the page is built rather than as it happens.
        _lblAudioTags.Location = new Point(UiLayout.PageLeft, y);
        _lblAudioTags.AutoSize = true;
        _lblAudioTags.MaximumSize = new Size(width, 0);
        _lblAudioTags.Font = hintFont;
        _lblAudioTags.ForeColor = AppTheme.Accent;
        _lblAudioTags.Text = current.EffectiveEngine == VoiceEngine.Windows
            ? "Audio tags like [excited] only work with ElevenLabs' eleven_v3 model - the Windows voice would read them "
              + "out as words, so they're taken out before anything is spoken. Your lines can keep them for ElevenLabs."
            : current.SupportsAudioTags
            ? "Audio tags are ON for this model. Put one in a line to colour the delivery: [excited] [cheerful] [sad] "
              + "[angry] [whispers] [laughs] [sighs] [awe] [sarcastic] [curious] [crying] [shouting]. "
              + "One per line is usually plenty - stacked tags fight each other."
            : "Audio tags like [excited] need eleven_v3 - on this model they'd be read out literally, so they're "
              + "stripped before anything is spoken. Switch the model on Voice Settings to use them.";

        header.Controls.Add(lblLegend);
        header.Controls.Add(lblLegend2);
        header.Controls.Add(btnRestore);
        header.Controls.Add(lblRestoreHint);
        header.Controls.Add(_lblAudioTags);

        header.Height = UiLayout.Below(_lblAudioTags, UiLayout.RowGap);

        return header;
    }

    /// <summary>
    /// The column headings and one row per announcement, into the scrolling
    /// band. Separate from BuildAnnouncementsPage so a restore can refill
    /// the band without rebuilding the page around it.
    /// </summary>
    /// <remarks>
    /// THE ROW SEPARATORS ARE REAL CONTROLS, one pixel high, not lines drawn
    /// in the panel's Paint.
    /// </remarks>
    /// <remarks>
    /// Drawing them was the obvious way and it is wrong here. A scrolling
    /// WinForms panel does not repaint when it scrolls - it BLITS what it
    /// already has and only invalidates the strip newly come into view. A
    /// line drawn at "row bottom plus the scroll offset" is therefore
    /// redrawn at its new place while the blitted copy of it stays where it
    /// was, so every few pixels of scrolling leaves another line behind.
    /// After a page or two of a list this tall the whole panel is ruled like
    /// a notepad.
    ///
    /// Invalidating on scroll fixes the scrollbar and the wheel and still
    /// misses the case that matters most here: tabbing between the phrasing
    /// boxes calls ScrollControlIntoView, which moves the view without
    /// raising a scroll event at all. There is no event to hang the fix on.
    ///
    /// A one-pixel Panel is a child like any other. It is blitted along with
    /// the row it belongs to, so it is never in the wrong place and never
    /// needs repainting. It is tagged KeepBackColor, or the theme pass paints
    /// it the page background like every other Panel.
    /// </remarks>
    private void FillAnnouncementRows(VoiceProfile current)
    {
        if (_annRows is not { } rows)
        {
            return;
        }

        var c = _annCol;
        var types = AnnouncementTypes();
        // The headings are not in here any more - they sit in a strip above
        // the band so they stay on screen. The first row starts at the top.
        var y = 0;

        for (var i = 0; i < types.Length; i++)
        {
            current.Events.TryGetValue(types[i].ToString(), out var config);
            config ??= new AnnouncementEventConfig();

            BuildAnnouncementRow(rows, types[i], config, ref y);

            // A hairline between rows, so a row reads as one line across a
            // list this wide. NOT the last one - a rule under the final row
            // is a line under nothing.
            if (i < types.Length - 1)
            {
                rows.Controls.Add(new Panel
                {
                    BackColor = AppTheme.Border,
                    Location = new Point(UiLayout.PageLeft, y),
                    Size = new Size(Math.Max(1, c.Right - UiLayout.PageLeft), 1),

                    // The theme pass gives every Panel the page background,
                    // which is what made these rules invisible - they were
                    // drawn, in the same colour as what was behind them.
                    Tag = AppTheme.KeepBackColor,
                });
            }
        }
    }

    /// <summary>
    /// The column headings, on a track of their own for the strip above the
    /// rows. Built once: a restore refills the rows, and the columns do not
    /// change.
    /// </summary>
    private Panel BuildAnnouncementHeadings()
    {
        var c = _annCol;
        var y = AnnRowPad;
        var track = new Panel();

        Label Heading(string text, int left) => new()
        {
            Text = text,
            Location = new Point(left, y),
            AutoSize = true,
            Font = AppTheme.MicroFont,
            ForeColor = AppTheme.TextMuted,
        };

        var headings = new[]
        {
            Heading("Race", c.Race),
            Heading("Practice", c.Practice),
            Heading("Announcement", c.Name),
            Heading("Phrasing", c.Phrasing),
            Heading("Weight", c.Weight),
            Heading("Speed", c.Speed),
            Heading("Caution", c.Caution),
        };

        foreach (var heading in headings)
        {
            track.Controls.Add(heading);
        }

        track.Height = UiLayout.Below(headings[0], AnnRowPad);

        return track;
    }

    /// <summary>
    /// One announcement. Ticks, name, phrasing and the three per-row
    /// numbers, with whatever extra controls that particular announcement
    /// needs tucked under its phrasing box.
    /// </summary>
    private void BuildAnnouncementRow(Panel rows, AnnouncementEventType type, AnnouncementEventConfig config, ref int y)
    {
        var c = _annCol;
        var hintFont = AppTheme.SmallFont;
        var (label, hint) = VoiceProfile.Describe(type);

        var top = y + AnnRowPad;

        // --- The phrasing column, which is what sets the row's height ---
        var phrasingHeight = AppTheme.BodyFont.Height * 3 + 8;

        var txt = new TextBox
        {
            Location = new Point(c.Phrasing, top),
            Size = new Size(c.PhrasingWidth, phrasingHeight),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Font = AppTheme.BodyFont,
            Text = string.Join(Environment.NewLine, config.EffectiveTemplates),

            // Themed at creation rather than left to the recursive pass.
            // These are the only multiline boxes on the page and they were
            // rendering white-on-black against the dark themes.
            BackColor = AppTheme.Surface,
            ForeColor = AppTheme.TextDark,
            BorderStyle = BorderStyle.FixedSingle,
        };

        // Measured rather than assumed to be one line - several of these run
        // to two or three at this column width, and a fixed row height is
        // how the cadence toggles below used to land on the hint above them.
        var hintHeight = TextRenderer.MeasureText(
            hint,
            hintFont,
            new Size(c.PhrasingWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;

        var lblHint = new Label
        {
            Text = hint,
            Location = new Point(c.Phrasing, UiLayout.Below(txt, UiLayout.HintGap)),
            Size = new Size(c.PhrasingWidth, hintHeight),
            Font = hintFont,
            ForeColor = AppTheme.TextMuted,
        };

        rows.Controls.Add(txt);
        rows.Controls.Add(lblHint);

        var extrasY = UiLayout.Below(lblHint, UiLayout.RowGap);
        var extrasBottom = BuildAnnouncementExtras(rows, type, config, extrasY);

        // --- The ticks, the name and the three numbers ---
        //
        // All centred on the first line of the phrasing box rather than on
        // the row, so a row whose hint runs to three lines still reads
        // left to right along its top edge.
        var lineTop = top;

        var chkRace = new CheckBox
        {
            Location = new Point(c.Race, lineTop + 4),
            AutoSize = true,
            Checked = config.Enabled,
        };

        var chkPractice = new CheckBox
        {
            Location = new Point(c.Practice, lineTop + 4),
            AutoSize = true,

            // Falls back to the race value, so a settings file written
            // before practice had ticks of its own starts with the two
            // agreeing rather than with practice silently off.
            Checked = config.EnabledInPractice ?? config.Enabled,
        };

        var lbl = new Label
        {
            Text = label,
            Location = new Point(c.Name, lineTop + 2),
            AutoSize = true,
            MaximumSize = new Size(c.NameWidth, 0),
            Font = AppTheme.BoldFont,
        };

        rows.Controls.Add(chkRace);
        rows.Controls.Add(chkPractice);
        rows.Controls.Add(lbl);

        if (PerLapWeightedTypes.Contains(type))
        {
            var numWeight = new NumericUpDown
            {
                Font = AppTheme.BodyFont,
                Location = new Point(c.Weight, lineTop),
                Width = c.WeightWidth,
                Minimum = 0,
                Maximum = 100,
                Value = Math.Clamp(config.Weight, 0, 100),
            };

            rows.Controls.Add(numWeight);
            _eventWeights[type] = numWeight;
        }

        var numSpeed = new NumericUpDown
        {
            Font = AppTheme.BodyFont,
            Location = new Point(c.Speed, lineTop),
            Width = c.SpeedWidth,
            DecimalPlaces = 2,
            Increment = 0.05m,
            Minimum = 0.50m,
            Maximum = 1.50m,
            Value = (decimal)Math.Clamp(config.SpeedMultiplier, 0.5, 1.5),
        };

        // Under a yellow or a red everything routine is held, so the
        // announcer isn't talking over an instruction to the drivers. This
        // is the per-announcement exception to that - a column of its own
        // now rather than a captioned tick squeezed under the name.
        var chkCaution = new CheckBox
        {
            Location = new Point(c.Caution, lineTop + 4),
            AutoSize = true,
            Checked = config.AllowUnderCaution,
        };

        var btnTest = new Button
        {
            Text = "Test",
            Location = new Point(c.Test, lineTop),
            Size = new Size(c.TestWidth, UiLayout.ButtonHeight),
        };

        var testType = type;
        var testBox = txt;
        var testSpeed = numSpeed;

        btnTest.Click += (_, _) => TestEventLine(testType, testBox, testSpeed, btnTest);

        rows.Controls.Add(numSpeed);
        rows.Controls.Add(chkCaution);
        rows.Controls.Add(btnTest);

        _eventSpeeds[type] = numSpeed;
        _eventCaution[type] = chkCaution;
        _eventTestButtons.Add(btnTest);
        _eventRows[type] = (chkRace, chkPractice, txt);

        // The row ends wherever its tallest column ended - including the
        // name, which wraps to two lines for the longest of them.
        var contentBottom = new[]
        {
            extrasBottom,
            UiLayout.Below(lblHint, 0),
            UiLayout.Below(lbl, 0),
            lineTop + c.ControlHeight,
        }.Max();

        y = contentBottom + AnnRowPad;
    }

    /// <summary>
    /// The handful of announcements that need more than a phrasing box,
    /// tucked under theirs. Returns the y their last control ends at.
    /// </summary>
    private int BuildAnnouncementExtras(Panel rows, AnnouncementEventType type, AnnouncementEventConfig config, int y)
    {
        var c = _annCol;
        var bottom = y - UiLayout.RowGap;

        if (type == AnnouncementEventType.FastLap)
        {
            _chkFastLapIncludeTime.Text = "Include lap time (auto-appended if the chosen line doesn't already say {time})";
            _chkFastLapIncludeTime.Location = new Point(c.Phrasing, y);
            _chkFastLapIncludeTime.AutoSize = true;
            _chkFastLapIncludeTime.MaximumSize = new Size(c.ExtraWidth, 0);
            _chkFastLapIncludeTime.Checked = config.IncludeLapTime;

            rows.Controls.Add(_chkFastLapIncludeTime);
            bottom = UiLayout.Below(_chkFastLapIncludeTime, 0);
        }

        if (type == AnnouncementEventType.LapCompleted)
        {
            _chkIncludeLapTime.Text = "Include lap time";
            _chkIncludeLapTime.Location = new Point(c.Phrasing, y);
            _chkIncludeLapTime.AutoSize = true;
            _chkIncludeLapTime.Checked = config.IncludeLapTime;

            _chkOnlyBestLap.Text = "Only announce best lap";
            _chkOnlyBestLap.AutoSize = true;
            _chkOnlyBestLap.Checked = config.OnlyBestLap;
            _chkOnlyBestLap.Location = new Point(UiLayout.After(_chkIncludeLapTime) + UiLayout.RowGap, y);

            rows.Controls.Add(_chkIncludeLapTime);
            rows.Controls.Add(_chkOnlyBestLap);
            bottom = UiLayout.Below(_chkIncludeLapTime, 0);
        }

        if (type == AnnouncementEventType.LapTime)
        {
            _chkSayLapTimeLabel.Text = "Say \"lap time\" (untick to just speak the number)";
            _chkSayLapTimeLabel.Location = new Point(c.Phrasing, y);
            _chkSayLapTimeLabel.AutoSize = true;
            _chkSayLapTimeLabel.MaximumSize = new Size(c.ExtraWidth, 0);
            _chkSayLapTimeLabel.Checked = config.SayLapTimeLabel;

            _chkIncludeDriverNameInLapTime.Text = "Say the driver's name first (e.g. \"Sam, 5 point 23 seconds\")";
            _chkIncludeDriverNameInLapTime.Location = new Point(c.Phrasing, UiLayout.Below(_chkSayLapTimeLabel, UiLayout.HintGap));
            _chkIncludeDriverNameInLapTime.AutoSize = true;
            _chkIncludeDriverNameInLapTime.MaximumSize = new Size(c.ExtraWidth, 0);
            _chkIncludeDriverNameInLapTime.Checked = config.IncludeDriverNameInLapTime;

            var cadenceY = UiLayout.Below(_chkIncludeDriverNameInLapTime, UiLayout.RowGap);

            _numLapTimeEveryN.Font = AppTheme.BodyFont;
            _numLapTimeEveryN.Width = UiLayout.NumericWidth;
            _numLapTimeEveryN.Minimum = 1;
            _numLapTimeEveryN.Maximum = 20;
            _numLapTimeEveryN.Value = Math.Clamp(config.LapTimeEveryNLaps, 1, 20);

            var lblEvery = new Label { Text = "Announce every", AutoSize = true, Font = AppTheme.BodyFont };
            lblEvery.Location = new Point(c.Phrasing, UiLayout.CentreOn(lblEvery, cadenceY, _numLapTimeEveryN.Height));

            _numLapTimeEveryN.Location = new Point(UiLayout.After(lblEvery), cadenceY);

            var lblSuffix = new Label { Text = "lap(s) per driver (1 = every lap)", AutoSize = true, Font = AppTheme.BodyFont };
            lblSuffix.Location = new Point(
                _numLapTimeEveryN.Right + UiLayout.HintGap,
                UiLayout.CentreOn(lblSuffix, cadenceY, _numLapTimeEveryN.Height));

            rows.Controls.Add(_chkSayLapTimeLabel);
            rows.Controls.Add(_chkIncludeDriverNameInLapTime);
            rows.Controls.Add(lblEvery);
            rows.Controls.Add(_numLapTimeEveryN);
            rows.Controls.Add(lblSuffix);

            bottom = UiLayout.Below(_numLapTimeEveryN, 0);
        }

        if (type == AnnouncementEventType.Streak)
        {
            // TWO cadence boxes, one per context - see the field's note.
            // The row's Race and Practice ticks are side by side now, so a
            // single box that meant whichever one the page was "in" has
            // nothing left to mean.
            foreach (var box in new[] { _numStreakEveryN, _numStreakEveryNPractice })
            {
                box.Font = AppTheme.BodyFont;
                box.Width = UiLayout.NumericWidth;
                box.Minimum = 1;
                box.Maximum = 20;
            }

            _numStreakEveryN.Value = Math.Clamp(config.StreakAnnounceEveryN, 1, 20);
            _numStreakEveryNPractice.Value = Math.Clamp(
                config.StreakAnnounceEveryNInPractice ?? config.StreakAnnounceEveryN, 1, 20);

            var lblEvery = new Label { Text = "Announce every", AutoSize = true, Font = AppTheme.BodyFont };
            lblEvery.Location = new Point(c.Phrasing, UiLayout.CentreOn(lblEvery, y, _numStreakEveryN.Height));

            _numStreakEveryN.Location = new Point(UiLayout.After(lblEvery), y);

            var lblRace = new Label { Text = "lap(s) in races,", AutoSize = true, Font = AppTheme.BodyFont };
            lblRace.Location = new Point(
                _numStreakEveryN.Right + UiLayout.HintGap,
                UiLayout.CentreOn(lblRace, y, _numStreakEveryN.Height));

            _numStreakEveryNPractice.Location = new Point(UiLayout.After(lblRace), y);

            var lblPractice = new Label { Text = "in practice", AutoSize = true, Font = AppTheme.BodyFont };
            lblPractice.Location = new Point(
                _numStreakEveryNPractice.Right + UiLayout.HintGap,
                UiLayout.CentreOn(lblPractice, y, _numStreakEveryN.Height));

            rows.Controls.Add(lblEvery);
            rows.Controls.Add(_numStreakEveryN);
            rows.Controls.Add(lblRace);
            rows.Controls.Add(_numStreakEveryNPractice);
            rows.Controls.Add(lblPractice);

            var togglesY = UiLayout.Below(_numStreakEveryN, UiLayout.RowGap);

            _chkOnlyLongestStreak.Text = "Only the longest streak in the field";
            _chkOnlyLongestStreak.Location = new Point(c.Phrasing, togglesY);
            _chkOnlyLongestStreak.AutoSize = true;
            _chkOnlyLongestStreak.Checked = config.OnlyLongestStreak;

            _chkMentionStreakChaser.Text = "Mention the runner-up";
            _chkMentionStreakChaser.AutoSize = true;
            _chkMentionStreakChaser.Checked = config.MentionStreakChaser;
            _chkMentionStreakChaser.Location = new Point(
                UiLayout.After(_chkOnlyLongestStreak) + UiLayout.RowGap, togglesY);

            rows.Controls.Add(_chkOnlyLongestStreak);
            rows.Controls.Add(_chkMentionStreakChaser);

            bottom = UiLayout.Below(_chkOnlyLongestStreak, 0);
        }

        if (type == AnnouncementEventType.PositionReport)
        {
            _numPositionEveryLaps.Font = AppTheme.BodyFont;
            _numPositionEveryLaps.Width = UiLayout.NumericWidth;
            _numPositionEveryLaps.Minimum = 1;
            _numPositionEveryLaps.Maximum = 50;
            _numPositionEveryLaps.Value = Math.Clamp(config.PositionReportEveryLaps, 1, 50);

            _numPositionDepth.Font = AppTheme.BodyFont;
            _numPositionDepth.Width = UiLayout.NumericWidth;
            _numPositionDepth.Minimum = 2;
            _numPositionDepth.Maximum = 10;
            _numPositionDepth.Value = Math.Clamp(config.PositionReportDepth, 2, 10);

            var height = _numPositionEveryLaps.Height;

            var lblEvery = new Label { Text = "Read every", AutoSize = true, Font = AppTheme.BodyFont };
            lblEvery.Location = new Point(c.Phrasing, UiLayout.CentreOn(lblEvery, y, height));

            _numPositionEveryLaps.Location = new Point(UiLayout.After(lblEvery), y);

            var lblSuffix = new Label { Text = "lap(s) of the LEADER,", AutoSize = true, Font = AppTheme.BodyFont };
            lblSuffix.Location = new Point(
                _numPositionEveryLaps.Right + UiLayout.HintGap,
                UiLayout.CentreOn(lblSuffix, y, height));

            var lblDepth = new Label { Text = "down to P", AutoSize = true, Font = AppTheme.BodyFont };
            lblDepth.Location = new Point(
                UiLayout.After(lblSuffix) + UiLayout.RowGap,
                UiLayout.CentreOn(lblDepth, y, height));

            _numPositionDepth.Location = new Point(UiLayout.After(lblDepth), y);

            rows.Controls.Add(lblEvery);
            rows.Controls.Add(_numPositionEveryLaps);
            rows.Controls.Add(lblSuffix);
            rows.Controls.Add(lblDepth);
            rows.Controls.Add(_numPositionDepth);

            bottom = UiLayout.Below(_numPositionEveryLaps, 0);
        }

        return bottom;
    }

    /// <summary>
    /// Refills the rows from what is saved - which is what they are meant to
    /// show, and where Restore Default Lines has just put the built-in set.
    /// </summary>
    private void RebuildAnnouncementRows()
    {
        // Refilling repopulates every row, and without this each one counts
        // as an edit and triggers a save.
        _loadingSettings = true;

        if (_annRows is not { } rows)
        {
            // Lift the guard on the way out, or auto-save stays switched off
            // for the rest of the session - silently, which is the worst
            // kind of off.
            _loadingSettings = false;
            return;
        }

        // These are FIELDS reused across rebuilds, not controls the band
        // owns. Disposing them with it would leave the refill re-adding
        // destroyed controls, which is exactly what used to throw
        // ObjectDisposedException. Detached first, so the rest can go.
        foreach (Control shared in new Control[]
                 {
                     _chkFastLapIncludeTime, _chkIncludeLapTime, _chkOnlyBestLap,
                     _chkSayLapTimeLabel, _chkIncludeDriverNameInLapTime,
                     _numStreakEveryN, _numStreakEveryNPractice,
                     _chkOnlyLongestStreak, _chkMentionStreakChaser,
                     _numPositionEveryLaps, _numPositionDepth, _numLapTimeEveryN,
                 })
        {
            shared.Parent?.Controls.Remove(shared);
        }

        // Every remaining child belongs to the band and goes with it. The
        // dictionaries point at those controls, and _eventRows is what the
        // save reads from - leaving them stale would write the next save
        // from dead text boxes.
        var doomed = rows.Controls.Cast<Control>().ToList();
        rows.Controls.Clear();

        foreach (var control in doomed)
        {
            control.Dispose();
        }

        _eventRows.Clear();
        _eventWeights.Clear();
        _eventSpeeds.Clear();
        _eventTestButtons.Clear();
        _eventCaution.Clear();

        FillAnnouncementRows(_store.Current);
        AppTheme.ApplyRecursively(rows);

        // Re-wire the new controls and lift the guard.
        WireAutoSave(rows);
        _loadingSettings = false;

        // The rows are new controls, so whatever the chosen voice takes away
        // has to be taken away again - a rebuild under the HeatCaller voice
        // would otherwise hand back thirty Test buttons and editable lines.
        UpdateEngineControls();

        // The rebuilt rows are the saved state plus whatever was just
        // restored, so persist once rather than waiting for an edit.
        AutoSave();
    }

    /// <summary>
    /// Keeps this page's status line in step with the one on Voice Settings.
    ///
    /// TestEventLine reports through _lblTestStatus, which lives beside the
    /// global Test button in the Parameters group - and that is now a
    /// different tab. Pressing Test on a row and having the answer appear
    /// somewhere you cannot see is worse than no answer at all, so the two
    /// say the same thing. Mirroring the label rather than editing thirty
    /// call sites also means anything added to that path later is carried
    /// across for free.
    /// </summary>
    private void MirrorTestStatus()
    {
        void Copy(object? sender, EventArgs e)
        {
            _lblAnnouncementStatus.Text = _lblTestStatus.Text;
            _lblAnnouncementStatus.ForeColor = _lblTestStatus.ForeColor;
        }

        // Both events: every call site sets the text and then the colour, so
        // listening to one alone would always be a beat behind.
        _lblTestStatus.TextChanged += Copy;
        _lblTestStatus.ForeColorChanged += Copy;
    }
}
