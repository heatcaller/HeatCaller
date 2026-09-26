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
/// </summary>
public sealed class SeasonRulesForm : Form
{
    private readonly Season _season;

    private readonly CheckBox _chkAuto = new();
    private readonly ComboBox _cmbClass = new();
    private readonly CheckBox _chkMainsOnly = new();
    private readonly CheckedListBox _lstEvents = new();
    private readonly CheckBox _chkAllEvents = new();
    private readonly NumericUpDown _numBest = new();
    private readonly Label _lblBestHint = new();

    private const string AllClasses = "(every class)";

    public SeasonRulesForm(Season season, RaceClassStore classes, RaceEventStore? events)
    {
        _season = season;

        Text = $"Scoring rules - {(string.IsNullOrWhiteSpace(season.Name) ? "championship" : season.Name)}";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        BuildLayout(classes, events);
        LoadFromSeason(events);

        AppTheme.ApplyRecursively(this);
    }

    private void BuildLayout(RaceClassStore classes, RaceEventStore? events)
    {
        const int left = 16;

        var y = 16;

        var lblWhat = new Label
        {
            Text = "WHAT COUNTS",
            AutoSize = true,
            Font = AppTheme.MicroFont,
            ForeColor = AppTheme.Primary,
            Location = new Point(left, y),
        };

        y = UiLayout.Below(lblWhat, UiLayout.RowGap);

        _chkAuto.Text = "Add new races to this championship automatically";
        _chkAuto.AutoSize = true;
        _chkAuto.Font = AppTheme.BodyFont;
        _chkAuto.Location = new Point(left, y);
        _chkAuto.CheckedChanged += (_, _) => UpdateEnabled();

        y = UiLayout.Below(_chkAuto, 2);

        var lblAutoHint = new Label
        {
            Text = "Off means you tick every race by hand, the way this always worked. "
                 + "On means anything matching the rules below joins as it is recorded - "
                 + "and you can still untick a round to force it out.",
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
            Location = new Point(left + UiLayout.Indent, y),
        };

        y = UiLayout.Below(lblAutoHint, UiLayout.RowGap);

        // --- Class ---
        var lblClass = new Label { Text = "Class:", AutoSize = true, Font = AppTheme.BodyFont };

        _cmbClass.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbClass.Font = AppTheme.BodyFont;
        _cmbClass.Width = 260;
        _cmbClass.Items.Add(AllClasses);

        foreach (var raceClass in classes.All)
        {
            _cmbClass.Items.Add(raceClass.Name);
        }

        lblClass.Location = new Point(left, UiLayout.CentreOn(lblClass, y, _cmbClass.Height));
        _cmbClass.Location = new Point(UiLayout.After(lblClass) + 40, y);

        y = UiLayout.Below(_cmbClass, UiLayout.RowGap);

        _chkMainsOnly.Text = "Only count mains - leave qualifying heats out";
        _chkMainsOnly.AutoSize = true;
        _chkMainsOnly.Font = AppTheme.BodyFont;
        _chkMainsOnly.Location = new Point(left, y);

        y = UiLayout.Below(_chkMainsOnly, UiLayout.RowGap);

        // --- Race days ---
        _chkAllEvents.Text = "Count every race day";
        _chkAllEvents.AutoSize = true;
        _chkAllEvents.Font = AppTheme.BodyFont;
        _chkAllEvents.Location = new Point(left, y);
        _chkAllEvents.CheckedChanged += (_, _) => UpdateEnabled();

        y = UiLayout.Below(_chkAllEvents, UiLayout.RowGap);

        _lstEvents.CheckOnClick = true;
        _lstEvents.BorderStyle = BorderStyle.None;
        _lstEvents.IntegralHeight = false;
        _lstEvents.Font = AppTheme.BodyFont;
        _lstEvents.Location = new Point(left + UiLayout.Indent, y);
        _lstEvents.Size = new Size(420, AppTheme.BodyFont.Height * 5 + 10);

        if (events is not null)
        {
            foreach (var raceEvent in events.Real)
            {
                _lstEvents.Items.Add(new EventChoice(raceEvent, string.Equals(raceEvent.ChampionshipId, _season.Id, StringComparison.Ordinal)));
            }
        }

        y = UiLayout.Below(_lstEvents, UiLayout.HintGap);

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
            MaximumSize = new Size(440, 0),
            Location = new Point(left + UiLayout.Indent, y),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        y = UiLayout.Below(lblTagged, UiLayout.SectionGap);

        // --- Scoring ---
        var lblScoring = new Label
        {
            Text = "HOW IT ADDS UP",
            AutoSize = true,
            Font = AppTheme.MicroFont,
            ForeColor = AppTheme.Primary,
            Location = new Point(left, y),
        };

        y = UiLayout.Below(lblScoring, UiLayout.RowGap);

        var lblBest = new Label { Text = "Count each driver's best:", AutoSize = true, Font = AppTheme.BodyFont };

        _numBest.Minimum = 0;
        _numBest.Maximum = 60;
        _numBest.Font = AppTheme.BodyFont;
        _numBest.Width = 80;
        _numBest.ValueChanged += (_, _) => UpdateBestHint();

        lblBest.Location = new Point(left, UiLayout.CentreOn(lblBest, y, _numBest.Height));
        _numBest.Location = new Point(UiLayout.After(lblBest), y);

        _lblBestHint.AutoSize = true;
        _lblBestHint.ForeColor = AppTheme.TextMuted;
        _lblBestHint.Font = AppTheme.BodyFont;
        _lblBestHint.Location = new Point(UiLayout.After(_numBest), UiLayout.CentreOn(_lblBestHint, y, _numBest.Height));

        y = UiLayout.Below(_numBest, 2);

        var lblDropHint = new Label
        {
            Text = "A driver who has raced fewer rounds than this drops nothing - "
                 + "missing a round is already its own penalty.",
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
            Location = new Point(left, y),
        };

        y = UiLayout.Below(lblDropHint, UiLayout.SectionGap);

        // --- Buttons ---
        var btnOk = new Button { Text = "Save", DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        btnOk.Size = new Size(UiLayout.WidthFor(btnOk), UiLayout.ButtonHeight);
        btnCancel.Size = new Size(UiLayout.WidthFor(btnCancel), UiLayout.ButtonHeight);

        var right = left + 452;

        btnCancel.Location = new Point(right - btnCancel.Width, y);
        btnOk.Location = new Point(btnCancel.Left - UiLayout.ButtonGap - btnOk.Width, y);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        Controls.AddRange(new Control[]
        {
            lblWhat, _chkAuto, lblAutoHint,
            lblClass, _cmbClass, _chkMainsOnly,
            _chkAllEvents, _lstEvents, lblTagged,
            lblScoring, lblBest, _numBest, _lblBestHint, lblDropHint,
            btnOk, btnCancel,
        });

        ClientSize = new Size(right + left, UiLayout.Below(btnOk, 16));
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
            ? "rounds  -  0 counts every round"
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
