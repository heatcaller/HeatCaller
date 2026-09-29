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
/// Being a page of its own, it also keeps itself in step with the text size
/// on its own: a new text size reaches it through its tab, not through this
/// control, so it wears its own face and keeps its own TextSize
/// (AnnouncementsHost).
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
    private readonly AnnouncementsHost _announcementsPage = new() { Dock = DockStyle.Fill };

    /// <summary>The Announcements page, for the host to put in a tab of its own.</summary>
    public Control AnnouncementsPage => _announcementsPage;

    /// <summary>The scrolling band the rows live in - held so a rebuild can refill it.</summary>
    private AnnouncementRowsPanel? _annRows;

    /// <summary>Every row in the band, in order: what PlaceAnnouncementRows places.</summary>
    private readonly List<AnnouncementRow> _annRowList = new();

    /// <summary>The column headings, on the track that stays above the rows - see StickyHeadings.</summary>
    private readonly Label _annHeadRace = new();
    private readonly Label _annHeadPractice = new();
    private readonly Label _annHeadName = new();
    private readonly Label _annHeadPhrasing = new();
    private readonly Label _annHeadWeight = new();
    private readonly Label _annHeadSpeed = new();
    private readonly Label _annHeadCaution = new();

    /// <summary>The headings' track, and how much taller than it the strip holding it is (the rule under the headings).</summary>
    private Panel? _annHeadingsTrack;
    private int _annStripExtra;

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

    /// <summary>A tick box with no caption of its own, sitting under a column heading - until a row's own box has been measured.</summary>
    private const int AnnTickWidth = 18;

    /// <summary>
    /// The narrowest the phrasing box gets, at the normal text size. It takes
    /// whatever width the other columns leave, and never less than this: past
    /// it, the list scrolls sideways under its headings rather than squeeze
    /// the box that holds the words.
    /// </summary>
    private const int AnnPhrasingLeastWidth = 300;

    /// <summary>The announcement names' column, at its narrowest and widest, at the normal text size.</summary>
    private const int AnnNameLeastWidth = 140;
    private const int AnnNameMostWidth = 260;

    /// <summary>How many phrasing boxes the list shows at least before the page scrolls instead.</summary>
    private const int AnnListLeastBoxes = 2;

    /// <summary>
    /// Every x on a row, and every width, measured from the content in the
    /// faces in use rather than written down - and the phrasing column's
    /// width from the room the band has. Same shape as
    /// SoundEffectsControl.Columns, and for the same reason: a remembered x
    /// is right for one font and wrong for the next one, and thirty rows of
    /// wrong is what the layout repair pass used to spend a startup undoing.
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
        public int PhrasingLeast;
        public int Weight;
        public int WeightWidth;
        public int Speed;
        public int SpeedWidth;
        public int Caution;
        public int CautionWidth;
        public int Test;
        public int TestWidth;
        public int TestHeight;

        /// <summary>A tick box with no caption: every row's three are the same size.</summary>
        public Size Tick;

        /// <summary>The height of a row's first line: its tallest tick, number box or button.</summary>
        public int ControlHeight;

        /// <summary>The widest line of extra controls any row has under its phrasing box, from the phrasing column's left.</summary>
        public int ExtrasWidth;

        /// <summary>The right-hand edge of a row, for the hairline drawn under it.</summary>
        public int Right => Test + TestWidth;
    }

    private readonly AnnouncementColumns _annCol = new();

    /// <summary>
    /// The longest announcement name on one line, the face it was measured
    /// in and how many names there were - measured again only when either
    /// changes. The rows are placed every time the band is laid out, and the
    /// theme pass lays the band out once for every control it restyles there.
    /// </summary>
    private (Font Font, int Count, int Width)? _annNamesMeasured;

    /// <summary>
    /// The Announcements page's own panel, which keeps it in step with the
    /// text size.
    ///
    /// The page is not a child of this control - the host parents it in a
    /// tab of its own - so a new text size reaches it through that tab and
    /// never through this control's OnParentFontChanged. It wears the body
    /// face itself (the tab's row is bold, which it would otherwise take),
    /// and applies its own TextSize whenever its face or its tab's changes.
    /// </summary>
    private sealed class AnnouncementsHost : Panel
    {
        /// <summary>What the Announcements page keeps in step with the text size - see TextSize.</summary>
        public TextSize TextSize { get; } = new();

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            TextSize.Apply();
        }

        protected override void OnParentFontChanged(EventArgs e)
        {
            base.OnParentFontChanged(e);
            TextSize.Apply();
        }
    }

    /// <summary>
    /// The band the rows are in. It places them itself, every time it is laid
    /// out - see PlaceAnnouncementRows - and scrolls down, and sideways when
    /// the rows are wider than it is.
    /// </summary>
    private sealed class AnnouncementRowsPanel : Panel
    {
        /// <summary>Null only while the panel underneath is being made, before this is set.</summary>
        private readonly Action<Panel>? _place;

        public AnnouncementRowsPanel(Action<Panel> place)
        {
            _place = place;
        }

        /// <summary>
        /// The rows placed across the width the band has, then its scrolling
        /// range worked out from where they are (base) - and again if that
        /// brought a scrollbar in or took one away, which changes the width
        /// they are placed across. Three times at most, as PageStack does.
        /// </summary>
        protected override void OnLayout(LayoutEventArgs levent)
        {
            for (var pass = 0; pass < 3; pass++)
            {
                var width = ClientSize.Width;

                _place?.Invoke(this);
                base.OnLayout(levent);

                if (ClientSize.Width == width)
                {
                    break;
                }
            }
        }
    }

    /// <summary>One announcement's controls, as the band places them.</summary>
    private sealed class AnnouncementRow
    {
        public AnnouncementRow(
            CheckBox race, CheckBox practice, Label name, TextBox phrasing, Label hint,
            NumericUpDown? weight, NumericUpDown speed, CheckBox caution, Button test,
            IReadOnlyList<ExtraLine> extras)
        {
            Race = race;
            Practice = practice;
            Name = name;
            Phrasing = phrasing;
            Hint = hint;
            Weight = weight;
            Speed = speed;
            Caution = caution;
            Test = test;
            Extras = extras;
        }

        public CheckBox Race { get; }
        public CheckBox Practice { get; }
        public Label Name { get; }
        public TextBox Phrasing { get; }
        public Label Hint { get; }
        public NumericUpDown? Weight { get; }
        public NumericUpDown Speed { get; }
        public CheckBox Caution { get; }
        public Button Test { get; }

        /// <summary>The lines of extra controls tucked under the phrasing box, top to bottom. Empty for most rows.</summary>
        public IReadOnlyList<ExtraLine> Extras { get; }

        /// <summary>The hairline under the row, or null for the last one - a rule under the final row is a line under nothing.</summary>
        public Panel? Rule { get; set; }

        /// <summary>How tall the name and the hint came to, and at what width and in what face - see WrappedHeight.</summary>
        public (int Width, Font Font, int Height)? NameMeasured;
        public (int Width, Font Font, int Height)? HintMeasured;
    }

    /// <summary>One line of extra controls: each with the gap before it (the first one's is not used), and the gap above the line.</summary>
    private sealed record ExtraLine(IReadOnlyList<(Control Control, int GapBefore)> Items, int GapAbove);

    private static ExtraLine Line(int gapAbove, params (Control Control, int GapBefore)[] items) => new(items, gapAbove);

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

    /// <summary>
    /// Builds the whole page on the layout pieces (LayoutPieces.cs):
    ///
    ///   the placeholders you can use, and how lines are picked;
    ///   Restore Default Lines, with what it does beside it - or under it,
    ///     when the line is too narrow for both (FieldRow);
    ///   what the chosen model does with audio tags;
    ///   the rows, taking the height that is left (FillPage), never less
    ///     than two phrasing boxes under their headings;
    ///   this page's status line.
    ///
    /// The rows are a list of their own, laid out by the band they are in
    /// (PlaceAnnouncementRows): every column measured in the faces in use,
    /// the phrasing box taking the width the others leave, and the list
    /// scrolling sideways under its headings only when even the narrowest
    /// phrasing box leaves the row wider than the window - at the largest
    /// text size.
    ///
    /// It used to be placed in pixels: a header band whose height was added
    /// up at build, the phrasing box 430px wide at every text size and
    /// window, and every x on a row worked out once, in the font of the day.
    /// </summary>
    private void BuildAnnouncementsPage(VoiceProfile current)
    {
        var textSize = _announcementsPage.TextSize;

        // The body face, worn by the page itself rather than taken from its
        // tab's bold row - see AnnouncementsHost.
        textSize.Wear(_announcementsPage, () => AppTheme.BodyFont);

        var lblLegend = new Label
        {
            Text = "Placeholders you can use in a line: {driver} {car} {team} {sponsors} {lap} {time} {laptime} {gate}",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        var lblLegend2 = textSize.Wear(
            new Label
            {
                Text = "One phrasing per line - a random one is picked each time the announcement fires, so it doesn't repeat "
                     + "the same sentence back to back. WEIGHT decides which announcement wins when several fire on the same "
                     + "lap; SPEED multiplies the voice's speaking rate, and is worth lowering for long lines like the "
                     + "running order. The lines themselves are shared between races and practice - only the ticks differ.",
                AutoSize = true,
                ForeColor = AppTheme.TextMuted,
            },
            () => AppTheme.SmallFont);

        // Kept as a field so the HeatCaller voice can switch it off with the
        // wording it restores: with those lines fixed there is nothing to
        // restore. See UpdateEngineControls.
        var btnRestore = new Button { Text = "Restore Default Lines..." };
        btnRestore.Click += BtnRestoreLines_Click;
        _btnRestoreLines = btnRestore;

        // Beside the button while the line has room for all of it, and on a
        // line of its own under it when it hasn't (FieldRow).
        var lblRestoreHint = textSize.Wear(
            new Label
            {
                Text = "Replaces every line below with the built-in set. Your own wording is lost; ticks, weights and "
                     + "speeds are kept.",
                AutoSize = false,
                AutoEllipsis = true,
                ForeColor = AppTheme.TextMuted,
            },
            () => AppTheme.SmallFont);

        var restore = new FieldRow();

        restore.Add(null, new ButtonBar().With(btnRestore));
        restore.AddLast(null, lblRestoreHint, () => lblRestoreHint.GetPreferredSize(Size.Empty).Width);

        // Worked out from the saved profile when the page is built, like the
        // rest of it - so it follows a change of model or voice the next
        // time the page is built rather than as it happens.
        _lblAudioTags.AutoSize = true;
        _lblAudioTags.ForeColor = AppTheme.Accent;
        textSize.Wear(_lblAudioTags, () => AppTheme.SmallFont);
        _lblAudioTags.Text = current.EffectiveEngine == VoiceEngine.Windows
            ? "Audio tags like [excited] only work with ElevenLabs' eleven_v3 model - the Windows voice would read them "
              + "out as words, so they're taken out before anything is spoken. Your lines can keep them for ElevenLabs."
            : current.SupportsAudioTags
            ? "Audio tags are ON for this model. Put one in a line to colour the delivery: [excited] [cheerful] [sad] "
              + "[angry] [whispers] [laughs] [sighs] [awe] [sarcastic] [curious] [crying] [shouting]. "
              + "One per line is usually plenty - stacked tags fight each other."
            : "Audio tags like [excited] need eleven_v3 - on this model they'd be read out literally, so they're "
              + "stripped before anything is spoken. Switch the model on Voice Settings to use them.";

        // --- The rows ---

        var rows = new AnnouncementRowsPanel(PlaceAnnouncementRows)
        {
            AutoScroll = true,
            AutoScrollMargin = new Size(UiLayout.PageRight, AnnRowPad + 14),
        };

        _annRows = rows;

        // The column headings stay on screen while the rows scroll under
        // them, and follow the rows sideways. The frame carries the border
        // the rows panel used to. See StickyHeadings.
        var track = BuildAnnouncementHeadings();
        _annHeadingsTrack = track;

        var list = StickyHeadings.Wrap(rows, track);

        // The strip holding the headings is the track and the rule under it;
        // it follows the track's height when the text size changes it.
        _annStripExtra = Math.Max(0, (track.Parent?.Height ?? track.Height) - track.Height);

        _lblAnnouncementStatus.AutoSize = true;
        _lblAnnouncementStatus.ForeColor = AppTheme.TextMuted;
        _lblAnnouncementStatus.Text = "Changes save as you make them.";

        // --- The page ---

        var page = new FillPage();

        page.Add(lblLegend, gapBelow: UiLayout.HintGap);
        page.Add(lblLegend2, gapBelow: UiLayout.RowGap);
        page.Add(restore, gapBelow: UiLayout.RowGap);
        page.Add(_lblAudioTags, gapBelow: UiLayout.RowGap);
        page.AddFill(list, _ => AnnouncementListLeastHeight(), gapBelow: UiLayout.RowGap);
        page.Add(_lblAnnouncementStatus, gapBelow: 0);

        _announcementsPage.Controls.Add(page);

        FillAnnouncementRows(current);

        // Once everything is on the page and wears its face - what it is
        // measured in. A line kept for the status, which every Test rewrites;
        // a longer message wraps onto more.
        textSize.ReserveLines(_lblAnnouncementStatus, 1);
        textSize.Keep(KeepAnnouncementsInStep);

        // The page is not a child of this control, so the theming pass that
        // walks this one never reaches it. The band's layout is held while it
        // runs - every control it restyles in the band would otherwise place
        // all the rows again - and done once when it has finished.
        rows.SuspendLayout();

        try
        {
            AppTheme.ApplyRecursively(_announcementsPage);
        }
        finally
        {
            rows.ResumeLayout(true);
        }
    }

    /// <summary>
    /// The least height the list can do with: its headings, its frame, and
    /// two phrasing boxes with the room round them - past that, the page
    /// scrolls rather than squash the list to a sliver (FillPage).
    /// </summary>
    private int AnnouncementListLeastHeight()
    {
        var strip = _annHeadingsTrack?.Parent?.Height ?? 0;
        var box = Pieces.LineHeight(_announcementsPage.Font) * 3 + 8;

        // The frame's border and its one-pixel padding, top and bottom.
        const int frame = 4;

        return strip + frame + (box + AnnRowPad * 2) * AnnListLeastBoxes;
    }

    /// <summary>
    /// The faces the headings and the rows' names and hints wear, and the
    /// headings' height, at the text size in use - then the rows placed again
    /// in them. Run whenever the text size changes (TextSize.Keep).
    /// </summary>
    private void KeepAnnouncementsInStep()
    {
        foreach (var heading in AnnouncementHeadings())
        {
            SetFace(heading, AppTheme.MicroFont);
        }

        foreach (var row in _annRowList)
        {
            SetFace(row.Name, AppTheme.BoldFont);
            SetFace(row.Hint, AppTheme.SmallFont);
        }

        if (_annHeadingsTrack is { } track)
        {
            var height = AnnouncementHeadingsHeight();

            if (track.Height != height)
            {
                track.Height = height;
            }

            if (track.Parent is { } strip && strip.Height != height + _annStripExtra)
            {
                strip.Height = height + _annStripExtra;
            }
        }

        _annRows?.PerformLayout();

        static void SetFace(Control control, Font face)
        {
            if (!Equals(control.Font, face))
            {
                control.Font = face;
            }
        }
    }

    private Label[] AnnouncementHeadings() => new[]
    {
        _annHeadRace, _annHeadPractice, _annHeadName, _annHeadPhrasing, _annHeadWeight, _annHeadSpeed, _annHeadCaution,
    };

    /// <summary>The headings' track: a row pad above and below the tallest heading.</summary>
    private int AnnouncementHeadingsHeight() =>
        AnnRowPad * 2 + AnnouncementHeadings().Max(heading => heading.GetPreferredSize(Size.Empty).Height);

    /// <summary>
    /// The column headings, on a track of their own for the strip above the
    /// rows. Built once: a restore refills the rows, and the headings stay.
    /// They are placed over their columns with the rows (PlaceAnnouncementRows),
    /// since the columns follow the band's width.
    /// </summary>
    private Panel BuildAnnouncementHeadings()
    {
        var track = new Panel();

        foreach (var (heading, text) in new[]
                 {
                     (_annHeadRace, "Race"),
                     (_annHeadPractice, "Practice"),
                     (_annHeadName, "Announcement"),
                     (_annHeadPhrasing, "Phrasing"),
                     (_annHeadWeight, "Weight"),
                     (_annHeadSpeed, "Speed"),
                     (_annHeadCaution, "Caution"),
                 })
        {
            heading.Text = text;
            heading.AutoSize = true;
            heading.Font = AppTheme.MicroFont;
            heading.ForeColor = AppTheme.TextMuted;

            track.Controls.Add(heading);
        }

        track.Height = AnnouncementHeadingsHeight();

        return track;
    }

    /// <summary>
    /// One row per announcement into the band, with a hairline between rows.
    /// Separate from BuildAnnouncementsPage so a restore can refill the band
    /// without rebuilding the page around it. Nothing is placed here: the
    /// band places the rows once they are all in (PlaceAnnouncementRows).
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

        var types = AnnouncementTypes();

        // Laid out once, when every row is in, rather than once per control
        // added.
        rows.SuspendLayout();

        try
        {
            for (var i = 0; i < types.Length; i++)
            {
                current.Events.TryGetValue(types[i].ToString(), out var config);
                config ??= new AnnouncementEventConfig();

                var row = BuildAnnouncementRow(rows, types[i], config);

                // A hairline between rows, so a row reads as one line across
                // a list this wide. NOT the last one - a rule under the final
                // row is a line under nothing.
                if (i < types.Length - 1)
                {
                    var rule = new Panel
                    {
                        BackColor = AppTheme.Border,

                        // The theme pass gives every Panel the page
                        // background, which is what made these rules
                        // invisible - they were drawn, in the same colour as
                        // what was behind them.
                        Tag = AppTheme.KeepBackColor,
                    };

                    rows.Controls.Add(rule);
                    row.Rule = rule;
                }

                _annRowList.Add(row);
            }
        }
        finally
        {
            rows.ResumeLayout(true);
        }
    }

    /// <summary>
    /// One announcement. Ticks, name, phrasing and the three per-row
    /// numbers, with whatever extra controls that particular announcement
    /// needs tucked under its phrasing box. Placed by the band - see
    /// PlaceAnnouncementRow.
    /// </summary>
    private AnnouncementRow BuildAnnouncementRow(Panel rows, AnnouncementEventType type, AnnouncementEventConfig config)
    {
        var (label, hint) = VoiceProfile.Describe(type);

        // --- The phrasing column, which is what sets the row's height ---
        //
        // Three lines tall in the face it is drawn in; more scrolls inside
        // it.
        var txt = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Text = string.Join(Environment.NewLine, config.EffectiveTemplates),

            // Themed at creation rather than left to the recursive pass.
            // These are the only multiline boxes on the page and they were
            // rendering white-on-black against the dark themes.
            BackColor = AppTheme.Surface,
            ForeColor = AppTheme.TextDark,
            BorderStyle = BorderStyle.FixedSingle,
        };

        // Wrapped to the phrasing box's width, whatever that comes to, and
        // measured rather than assumed to be one line - several of these run
        // to two or three, and a fixed row height is how the cadence toggles
        // below used to land on the hint above them.
        var lblHint = new Label
        {
            Text = hint,
            AutoSize = false,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        };

        rows.Controls.Add(txt);
        rows.Controls.Add(lblHint);

        var extras = BuildAnnouncementExtras(rows, type, config);

        // --- The ticks, the name and the three numbers ---
        var chkRace = new CheckBox
        {
            AutoSize = true,
            Checked = config.Enabled,
        };

        var chkPractice = new CheckBox
        {
            AutoSize = true,

            // Falls back to the race value, so a settings file written
            // before practice had ticks of its own starts with the two
            // agreeing rather than with practice silently off.
            Checked = config.EnabledInPractice ?? config.Enabled,
        };

        // Wraps onto a second line when it is wider than the names' column -
        // the longest of them do at the larger text sizes.
        var lbl = new Label
        {
            Text = label,
            AutoSize = false,
            Font = AppTheme.BoldFont,
        };

        rows.Controls.Add(chkRace);
        rows.Controls.Add(chkPractice);
        rows.Controls.Add(lbl);

        NumericUpDown? numWeight = null;

        if (PerLapWeightedTypes.Contains(type))
        {
            numWeight = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 100,
                Value = Math.Clamp(config.Weight, 0, 100),
            };

            rows.Controls.Add(numWeight);
            _eventWeights[type] = numWeight;
        }

        var numSpeed = new NumericUpDown
        {
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
            AutoSize = true,
            Checked = config.AllowUnderCaution,
        };

        var btnTest = new Button { Text = "Test" };

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

        return new AnnouncementRow(chkRace, chkPractice, lbl, txt, lblHint, numWeight, numSpeed, chkCaution, btnTest, extras);
    }

    /// <summary>
    /// The handful of announcements that need more than a phrasing box,
    /// tucked under theirs: their controls added to the band, and returned
    /// as the lines they go on, left to right. Every check box is one line
    /// - a check box is as wide as its caption and never wraps it - and the
    /// row is kept wide enough for the widest of these lines
    /// (AnnouncementColumns.ExtrasWidth).
    /// </summary>
    private IReadOnlyList<ExtraLine> BuildAnnouncementExtras(Panel rows, AnnouncementEventType type, AnnouncementEventConfig config)
    {
        var lines = new List<ExtraLine>();

        // Beside what it follows, or a caption's gap from its box, or a
        // row's gap more between two things that are each their own.
        const int caption = UiLayout.LabelGap;
        const int apart = UiLayout.LabelGap + UiLayout.RowGap;

        if (type == AnnouncementEventType.FastLap)
        {
            _chkFastLapIncludeTime.Text = "Include lap time (auto-appended if the chosen line doesn't already say {time})";
            _chkFastLapIncludeTime.AutoSize = true;
            _chkFastLapIncludeTime.Checked = config.IncludeLapTime;

            rows.Controls.Add(_chkFastLapIncludeTime);

            lines.Add(Line(UiLayout.RowGap, (_chkFastLapIncludeTime, 0)));
        }

        if (type == AnnouncementEventType.LapCompleted)
        {
            _chkIncludeLapTime.Text = "Include lap time";
            _chkIncludeLapTime.AutoSize = true;
            _chkIncludeLapTime.Checked = config.IncludeLapTime;

            _chkOnlyBestLap.Text = "Only announce best lap";
            _chkOnlyBestLap.AutoSize = true;
            _chkOnlyBestLap.Checked = config.OnlyBestLap;

            rows.Controls.Add(_chkIncludeLapTime);
            rows.Controls.Add(_chkOnlyBestLap);

            lines.Add(Line(UiLayout.RowGap, (_chkIncludeLapTime, 0), (_chkOnlyBestLap, apart)));
        }

        if (type == AnnouncementEventType.LapTime)
        {
            _chkSayLapTimeLabel.Text = "Say \"lap time\" (untick to just speak the number)";
            _chkSayLapTimeLabel.AutoSize = true;
            _chkSayLapTimeLabel.Checked = config.SayLapTimeLabel;

            _chkIncludeDriverNameInLapTime.Text = "Say the driver's name first (e.g. \"Sam, 5 point 23 seconds\")";
            _chkIncludeDriverNameInLapTime.AutoSize = true;
            _chkIncludeDriverNameInLapTime.Checked = config.IncludeDriverNameInLapTime;

            _numLapTimeEveryN.Minimum = 1;
            _numLapTimeEveryN.Maximum = 20;
            _numLapTimeEveryN.Value = Math.Clamp(config.LapTimeEveryNLaps, 1, 20);

            var lblEvery = new Label { Text = "Announce every", AutoSize = true };
            var lblSuffix = new Label { Text = "lap(s) per driver (1 = every lap)", AutoSize = true };

            rows.Controls.Add(_chkSayLapTimeLabel);
            rows.Controls.Add(_chkIncludeDriverNameInLapTime);
            rows.Controls.Add(lblEvery);
            rows.Controls.Add(_numLapTimeEveryN);
            rows.Controls.Add(lblSuffix);

            lines.Add(Line(UiLayout.RowGap, (_chkSayLapTimeLabel, 0)));
            lines.Add(Line(UiLayout.HintGap, (_chkIncludeDriverNameInLapTime, 0)));
            lines.Add(Line(UiLayout.RowGap, (lblEvery, 0), (_numLapTimeEveryN, caption), (lblSuffix, UiLayout.HintGap)));
        }

        if (type == AnnouncementEventType.Streak)
        {
            // TWO cadence boxes, one per context - see the field's note.
            // The row's Race and Practice ticks are side by side now, so a
            // single box that meant whichever one the page was "in" has
            // nothing left to mean.
            foreach (var box in new[] { _numStreakEveryN, _numStreakEveryNPractice })
            {
                box.Minimum = 1;
                box.Maximum = 20;
            }

            _numStreakEveryN.Value = Math.Clamp(config.StreakAnnounceEveryN, 1, 20);
            _numStreakEveryNPractice.Value = Math.Clamp(
                config.StreakAnnounceEveryNInPractice ?? config.StreakAnnounceEveryN, 1, 20);

            var lblEvery = new Label { Text = "Announce every", AutoSize = true };
            var lblRace = new Label { Text = "lap(s) in races,", AutoSize = true };
            var lblPractice = new Label { Text = "in practice", AutoSize = true };

            rows.Controls.Add(lblEvery);
            rows.Controls.Add(_numStreakEveryN);
            rows.Controls.Add(lblRace);
            rows.Controls.Add(_numStreakEveryNPractice);
            rows.Controls.Add(lblPractice);

            _chkOnlyLongestStreak.Text = "Only the longest streak in the field";
            _chkOnlyLongestStreak.AutoSize = true;
            _chkOnlyLongestStreak.Checked = config.OnlyLongestStreak;

            _chkMentionStreakChaser.Text = "Mention the runner-up";
            _chkMentionStreakChaser.AutoSize = true;
            _chkMentionStreakChaser.Checked = config.MentionStreakChaser;

            rows.Controls.Add(_chkOnlyLongestStreak);
            rows.Controls.Add(_chkMentionStreakChaser);

            lines.Add(Line(
                UiLayout.RowGap,
                (lblEvery, 0),
                (_numStreakEveryN, caption),
                (lblRace, UiLayout.HintGap),
                (_numStreakEveryNPractice, caption),
                (lblPractice, UiLayout.HintGap)));

            lines.Add(Line(UiLayout.RowGap, (_chkOnlyLongestStreak, 0), (_chkMentionStreakChaser, apart)));
        }

        if (type == AnnouncementEventType.PositionReport)
        {
            _numPositionEveryLaps.Minimum = 1;
            _numPositionEveryLaps.Maximum = 50;
            _numPositionEveryLaps.Value = Math.Clamp(config.PositionReportEveryLaps, 1, 50);

            _numPositionDepth.Minimum = 2;
            _numPositionDepth.Maximum = 10;
            _numPositionDepth.Value = Math.Clamp(config.PositionReportDepth, 2, 10);

            var lblEvery = new Label { Text = "Read every", AutoSize = true };
            var lblSuffix = new Label { Text = "lap(s) of the LEADER,", AutoSize = true };
            var lblDepth = new Label { Text = "down to P", AutoSize = true };

            rows.Controls.Add(lblEvery);
            rows.Controls.Add(_numPositionEveryLaps);
            rows.Controls.Add(lblSuffix);
            rows.Controls.Add(lblDepth);
            rows.Controls.Add(_numPositionDepth);

            lines.Add(Line(
                UiLayout.RowGap,
                (lblEvery, 0),
                (_numPositionEveryLaps, caption),
                (lblSuffix, UiLayout.HintGap),
                (lblDepth, apart),
                (_numPositionDepth, caption)));
        }

        return lines;
    }

    /// <summary>
    /// Places every row, and the headings over their columns, across the
    /// width the band has - the band's own layout (AnnouncementRowsPanel),
    /// so it follows the window, the scrollbars and the text size.
    ///
    /// Scrolled, the band's content starts above and to the left of it by
    /// however far; everything is placed that far along
    /// (AutoScrollPosition), as PageStack places its column.
    /// </summary>
    private void PlaceAnnouncementRows(Panel rows)
    {
        var c = _annCol;

        MeasureAnnouncementColumns();
        SetAnnouncementColumns(rows.ClientSize.Width);

        // The headings are on a track that moves sideways with the rows
        // (StickyHeadings), so they go at the columns' own x.
        PlaceHeading(_annHeadRace, c.Race);
        PlaceHeading(_annHeadPractice, c.Practice);
        PlaceHeading(_annHeadName, c.Name);
        PlaceHeading(_annHeadPhrasing, c.Phrasing);
        PlaceHeading(_annHeadWeight, c.Weight);
        PlaceHeading(_annHeadSpeed, c.Speed);
        PlaceHeading(_annHeadCaution, c.Caution);

        // As wide as the rows, and the margin after them, so it covers them
        // however far they are scrolled.
        if (_annHeadingsTrack is { } track && track.Width != c.Right + UiLayout.PageRight)
        {
            track.Width = c.Right + UiLayout.PageRight;
        }

        var offset = rows.AutoScrollPosition;
        var y = 0;

        foreach (var row in _annRowList)
        {
            y = PlaceAnnouncementRow(row, y, offset);

            if (row.Rule is { } rule)
            {
                Put(rule, UiLayout.PageLeft + offset.X, y + offset.Y, c.Right - UiLayout.PageLeft, 1);
            }
        }

        static void PlaceHeading(Label heading, int left)
        {
            var size = heading.GetPreferredSize(Size.Empty);

            Put(heading, left, AnnRowPad, size.Width, size.Height);
        }
    }

    /// <summary>
    /// Places one row with its top at <paramref name="y"/>, and returns the y
    /// the next row starts at.
    ///
    /// The ticks, the numbers and Test are centred on the row's first line,
    /// and the name's first line with them, so a row whose hint runs to
    /// three lines still reads left to right along its top edge. Under the
    /// phrasing box, its hint; under that, any extra controls, which may run
    /// on under the Weight, Speed and Caution columns - those sit on the
    /// first line, well above.
    /// </summary>
    private int PlaceAnnouncementRow(AnnouncementRow row, int y, Point offset)
    {
        var c = _annCol;
        var top = y + AnnRowPad;
        var line = c.ControlHeight;

        CentreOn(row.Race, c.Race, c.Tick);
        CentreOn(row.Practice, c.Practice, c.Tick);

        if (row.Weight is { } weight)
        {
            CentreOn(weight, c.Weight, new Size(c.WeightWidth, weight.PreferredHeight));
        }

        CentreOn(row.Speed, c.Speed, new Size(c.SpeedWidth, row.Speed.PreferredHeight));
        CentreOn(row.Caution, c.Caution, c.Tick);
        CentreOn(row.Test, c.Test, new Size(c.TestWidth, c.TestHeight));

        var nameTop = top + Math.Max(0, (line - row.Name.Font.Height) / 2);
        var nameHeight = WrappedHeight(row.Name, c.NameWidth, ref row.NameMeasured);

        Put(row.Name, c.Name + offset.X, nameTop + offset.Y, c.NameWidth, nameHeight);

        // --- The phrasing column ---
        var boxHeight = Pieces.LineHeight(row.Phrasing.Font) * 3 + 8;

        Put(row.Phrasing, c.Phrasing + offset.X, top + offset.Y, c.PhrasingWidth, boxHeight);

        var hintTop = top + boxHeight + UiLayout.HintGap;
        var hintHeight = WrappedHeight(row.Hint, c.PhrasingWidth, ref row.HintMeasured);

        Put(row.Hint, c.Phrasing + offset.X, hintTop + offset.Y, c.PhrasingWidth, hintHeight);

        // The extras start under the hint, and never beside the first line's
        // controls, which they may run under.
        var lineY = Math.Max(hintTop + hintHeight, top + line);
        var extrasBottom = lineY;

        foreach (var extra in row.Extras)
        {
            lineY += extra.GapAbove;

            var height = extra.Items.Max(item => ExtraSize(item.Control).Height);
            var x = c.Phrasing;

            for (var i = 0; i < extra.Items.Count; i++)
            {
                var (control, gapBefore) = extra.Items[i];
                var size = ExtraSize(control);

                if (i > 0)
                {
                    x += gapBefore;
                }

                Put(control, x + offset.X, lineY + (height - size.Height) / 2 + offset.Y, size.Width, size.Height);

                x += size.Width;
            }

            lineY += height;
            extrasBottom = lineY;
        }

        // The row ends wherever its tallest column ended - including the
        // name, which wraps to two lines for the longest of them.
        var bottom = new[] { top + line, nameTop + nameHeight, hintTop + hintHeight, extrasBottom }.Max();

        return bottom + AnnRowPad;

        void CentreOn(Control control, int left, Size size) =>
            Put(control, left + offset.X, top + (line - size.Height) / 2 + offset.Y, size.Width, size.Height);
    }

    /// <summary>
    /// What a row's first line and its extra controls need, in the faces in
    /// use: the widths of the columns that do not stretch, the tallest first
    /// line, the least the phrasing box can do with, and the widest line of
    /// extras. Measured every time the rows are placed - a handful of short
    /// captions and thirty names - so nothing measured in an old face is
    /// ever used.
    /// </summary>
    private void MeasureAnnouncementColumns()
    {
        var c = _annCol;
        var first = _annRowList.Count > 0 ? _annRowList[0] : null;

        // A tick box with no caption is only its box.
        var tick = first?.Race.GetPreferredSize(Size.Empty) ?? new Size(AnnTickWidth, AnnTickWidth);

        c.Tick = tick;

        c.RaceWidth = Math.Max(tick.Width, HeadingWidth(_annHeadRace));
        c.PracticeWidth = Math.Max(tick.Width, HeadingWidth(_annHeadPractice));

        // Wide enough for the longest announcement name on one line, within
        // reason: a name wider than the column wraps. The old row put the
        // name at a literal x=36 and the template box at x=160, and at this
        // body size a name like "Qualifying improvement" is wider than that
        // gap - so the name ran into the box on every row, thirty overlaps
        // for the repair pass to find on every launch.
        var names = LongestName();

        c.NameWidth = Math.Max(
            HeadingWidth(_annHeadName),
            Math.Clamp(names, UiLayout.AtTextSize(AnnNameLeastWidth), UiLayout.AtTextSize(AnnNameMostWidth)));

        c.PhrasingLeast = Math.Max(UiLayout.AtTextSize(AnnPhrasingLeastWidth), HeadingWidth(_annHeadPhrasing));

        // Each number box as wide as its largest value needs - Weight 100,
        // Speed 1.50 - and never narrower than its heading.
        var weightBox = _annRowList.Select(row => row.Weight).FirstOrDefault(box => box is not null);

        c.WeightWidth = Math.Max(
            HeadingWidth(_annHeadWeight),
            weightBox is null ? UiLayout.AtTextSize(UiLayout.NumericWidth) : NumberBoxWidth(weightBox, UiLayout.NumericWidth));

        c.SpeedWidth = Math.Max(
            HeadingWidth(_annHeadSpeed),
            first is null ? UiLayout.AtTextSize(UiLayout.NumericWidth) : NumberBoxWidth(first.Speed, UiLayout.NumericWidth));

        c.CautionWidth = Math.Max(tick.Width, HeadingWidth(_annHeadCaution));

        // A dense row's button: its caption and a little room, as tall as
        // the theme makes buttons (UiLayout.TightButtonSizeFor).
        var test = first is null ? Size.Empty : UiLayout.TightButtonSizeFor(first.Test);

        c.TestWidth = test.Width;
        c.TestHeight = test.Height;
        c.ControlHeight = Math.Max(Math.Max(test.Height, tick.Height), first?.Speed.PreferredHeight ?? 0);

        c.ExtrasWidth = _annRowList
            .SelectMany(row => row.Extras)
            .Select(ExtraLineWidth)
            .DefaultIfEmpty(0)
            .Max();

        static int HeadingWidth(Label heading) => heading.GetPreferredSize(Size.Empty).Width;
    }

    /// <summary>The longest announcement name on one line, in the face the names wear - see _annNamesMeasured.</summary>
    private int LongestName()
    {
        if (_annRowList.Count == 0)
        {
            return 0;
        }

        var face = _annRowList[0].Name.Font;

        if (_annNamesMeasured is { } known && ReferenceEquals(known.Font, face) && known.Count == _annRowList.Count)
        {
            return known.Width;
        }

        var widest = _annRowList
            .Select(row => TextRenderer.MeasureText(row.Name.Text, row.Name.Font, new Size(int.MaxValue, int.MaxValue), WrapFlags(row.Name)).Width)
            .Max();

        _annNamesMeasured = (face, _annRowList.Count, widest);
        return widest;
    }

    /// <summary>
    /// Every column's x across a band <paramref name="width"/> wide. The
    /// phrasing box takes what the other columns leave, so a row ends a
    /// margin short of the band's right-hand edge - but never narrower than
    /// its least, nor so narrow that the widest line of extras under it
    /// would run past the row. Then the row is wider than the band, and the
    /// band scrolls sideways under its headings.
    /// </summary>
    private void SetAnnouncementColumns(int width)
    {
        var c = _annCol;

        c.Race = UiLayout.PageLeft;
        c.Practice = c.Race + c.RaceWidth + AnnColumnGap;
        c.Name = c.Practice + c.PracticeWidth + AnnColumnGap + 4;
        c.Phrasing = c.Name + c.NameWidth + AnnColumnGap;

        // Everything to the right of the phrasing box.
        var tail = AnnColumnGap + c.WeightWidth
                 + AnnColumnGap + c.SpeedWidth
                 + AnnColumnGap + c.CautionWidth
                 + AnnColumnGap + c.TestWidth;

        c.PhrasingWidth = Math.Max(
            Math.Max(c.PhrasingLeast, c.ExtrasWidth - tail),
            width - UiLayout.PageRight - c.Phrasing - tail);

        c.Weight = c.Phrasing + c.PhrasingWidth + AnnColumnGap;
        c.Speed = c.Weight + c.WeightWidth + AnnColumnGap;
        c.Caution = c.Speed + c.SpeedWidth + AnnColumnGap;
        c.Test = c.Caution + c.CautionWidth + AnnColumnGap;
    }

    /// <summary>A line of extras from its first control's left to its last one's right.</summary>
    private static int ExtraLineWidth(ExtraLine line) =>
        line.Items.Select((item, index) => ExtraSize(item.Control).Width + (index > 0 ? item.GapBefore : 0)).Sum();

    /// <summary>
    /// An extra control's size: a number box as wide as its largest value
    /// needs and as tall as its face makes it, anything else - a caption, a
    /// check box - as big as it wants to be, on one line.
    /// </summary>
    private static Size ExtraSize(Control control) => control switch
    {
        NumericUpDown box => new Size(NumberBoxWidth(box, UiLayout.NumericWidth), box.PreferredHeight),
        _ => control.GetPreferredSize(Size.Empty),
    };

    /// <summary>
    /// How tall a label is at <paramref name="width"/>, wrapped the way a
    /// label draws its text - which is how the layout check measures it -
    /// kept until the width or the label's face changes.
    /// </summary>
    private static int WrappedHeight(Label label, int width, ref (int Width, Font Font, int Height)? kept)
    {
        if (kept is { } known && known.Width == width && ReferenceEquals(known.Font, label.Font))
        {
            return known.Height;
        }

        var height = TextRenderer.MeasureText(label.Text, label.Font, new Size(Math.Max(1, width), int.MaxValue), WrapFlags(label)).Height;

        kept = (width, label.Font, height);
        return height;
    }

    /// <summary>The flags a label wraps its text with.</summary>
    private static TextFormatFlags WrapFlags(Label label) =>
        TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | (label.UseMnemonic ? TextFormatFlags.Default : TextFormatFlags.NoPrefix);

    /// <summary>
    /// Moves or sizes a control only when it is somewhere else: given the
    /// bounds it already has, nothing happens, but new ones lay its parent
    /// out again.
    /// </summary>
    private static void Put(Control control, int x, int y, int width, int height)
    {
        var bounds = new Rectangle(x, y, Math.Max(1, width), Math.Max(1, height));

        if (control.Bounds != bounds)
        {
            control.Bounds = bounds;
        }
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

        // Forgotten first, so the band has nothing to place while its
        // controls are taken out below.
        _annRowList.Clear();

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

        // Themed with the band's layout held, as when the page was built.
        rows.SuspendLayout();

        try
        {
            AppTheme.ApplyRecursively(rows);
        }
        finally
        {
            rows.ResumeLayout(true);
        }

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
    /// global Test button in the Voice parameters card - and that is now a
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
