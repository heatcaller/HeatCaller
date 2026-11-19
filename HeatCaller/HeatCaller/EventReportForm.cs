using System.Diagnostics;

namespace HeatCaller;

/// <summary>
/// Chooses what goes in the event report and where it's saved.
///
/// The day is picked from days that actually have sessions, rather than a
/// free date picker - choosing a day with nothing on it is the only way to
/// get an empty report, so the dialog simply doesn't offer it.
///
/// On the layout pieces (DialogFit): what the report is across the top, the
/// day and the event's name lined up in a field grid, what to include under
/// its heading, then what the chosen day comes to and Generate and Close at
/// the foot. All but those last two is the window's body, which scrolls on a
/// screen too short for it. The line about the day is as tall as the longest
/// thing it can say for any day on the list (SteadyLabel), so picking
/// another day never moves the buttons. It used to be placed in pixels in a
/// 480x380 window: the captions in a column 94px wide, the fields 354px
/// beside them however long the longest day on the list, and the line about
/// the day in a box kept two lines tall by hand.
/// </summary>
public sealed class EventReportForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size.</summary>
    private const int DialogWidth = 480;

    /// <summary>The fields' width at the normal text size - wider when the longest day on the list needs the drop-down wider.</summary>
    private const int FieldWidth = 354;

    private readonly SessionHistoryStore _history;
    private readonly RoundDefinitionStore _rounds;
    private readonly RaceClassStore _classes;
    private readonly ProfileStore _profiles;
    private readonly TrackLayoutStore _trackLayouts;
    private readonly TechInspectionStore _tech;
    private readonly RentalTransponderStore _rentals;
    private readonly HandicapStore? _handicaps;
    private readonly RaceEventStore? _events;

    private readonly ComboBox _cmbDay = new();
    private readonly TextBox _txtName = new();
    private readonly CheckBox _chkQualifying = new ThemedCheckBox();
    private readonly CheckBox _chkLapDetail = new ThemedCheckBox();
    private readonly CheckBox _chkTech = new ThemedCheckBox();
    private readonly CheckBox _chkRentals = new ThemedCheckBox();

    /// <summary>What the chosen day comes to - as tall as the longest thing it can say for any day on the list, so picking another never moves the buttons under it.</summary>
    private readonly SteadyLabel _lblPreview = new();

    public EventReportForm(
        SessionHistoryStore history,
        RoundDefinitionStore rounds,
        RaceClassStore classes,
        ProfileStore profiles,
        TrackLayoutStore trackLayouts,
        TechInspectionStore tech,
        RentalTransponderStore rentals,
        HandicapStore? handicaps = null,
        RaceEventStore? events = null)
    {
        _history = history;
        _handicaps = handicaps;
        _events = events;
        _rounds = rounds;
        _classes = classes;
        _profiles = profiles;
        _trackLayouts = trackLayouts;
        _tech = tech;
        _rentals = rentals;

        Text = "Event Report";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var (page, body, captions) = BuildLayout();
        AppTheme.ApplyRecursively(this);

        // As wide as it was at the normal text size, at the size in use - or
        // wider when the drop-down needs more for the longest day on the
        // list: a long weekday and month with a three-figure count of
        // sessions came to about the 354px it had, and a date written out in
        // another language can come to more. Measured in the face the theme
        // pass left it in; the drop-down itself takes the whole of the
        // fields' column (stretch). The caption column beside it as the grid
        // works it out: its widest caption and the gap after it.
        (Font Font, int Count, int Width)? dayWidth = null;

        var fieldColumn = Math.Max(UiLayout.AtTextSize(FieldWidth), Pieces.PickerWidth(_cmbDay, ref dayWidth));
        var captionColumn = captions.Max(caption => caption.GetPreferredSize(Size.Empty).Width + caption.Margin.Horizontal);

        var width = Math.Max(
            UiLayout.AtTextSize(DialogWidth),
            UiLayout.PageLeft + Section.Inset.Horizontal + captionColumn + fieldColumn + UiLayout.PageRight);

        DialogFit.Keep(this, page, width, body: body, widthMeasured: true);
    }

    /// <summary>
    /// The page: what the report is, the fields and what to include in the
    /// body, and the line about the day and the buttons under it. Returns
    /// them, and the fields' captions, for the window's width.
    /// </summary>
    private (FillPage Page, PageStack Body, Label[] Captions) BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "A single page covering the whole day - results by class, fastest laps, and anything still outstanding. "
                 + "Saves as HTML so it can be printed, emailed, or opened on a phone.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _cmbDay.DropDownStyle = ComboBoxStyle.DropDownList;

        // Only days that have sessions - an empty report helps nobody.
        // Demo races are left out of the report, so they don't count here.
        var days = _history.All
            .Where(s => !s.IsNotScored())
            .Select(s => s.StartedAtUtc.ToLocalTime().Date)
            .Distinct()
            .OrderByDescending(d => d)
            .ToList();

        foreach (var day in days)
        {
            _cmbDay.Items.Add(new DayItem(day, _history.All.Count(s => !s.IsNotScored() && s.StartedAtUtc.ToLocalTime().Date == day)));
        }

        if (_cmbDay.Items.Count > 0)
        {
            _cmbDay.SelectedIndex = 0;
        }

        _cmbDay.SelectedIndexChanged += (_, _) => UpdatePreview();

        _txtName.Text = "Club Race Day";

        _chkQualifying.Text = "Qualifying standings";
        _chkQualifying.AutoSize = true;
        _chkQualifying.Checked = true;

        _chkTech.Text = "Tech inspection summary";
        _chkTech.AutoSize = true;
        _chkTech.Checked = true;

        _chkRentals.Text = "Rental transponders not returned";
        _chkRentals.AutoSize = true;
        _chkRentals.Checked = true;

        _chkLapDetail.Text = "Every lap time (makes the report much longer)";
        _chkLapDetail.AutoSize = true;
        _chkLapDetail.Checked = false;

        _lblPreview.ForeColor = AppTheme.TextMuted;
        _lblPreview.Font = AppTheme.SmallFont;

        // Everything it can say, for each day on the list - which is fixed
        // for as long as the window is open - and for none.
        _lblPreview.Wordings = _cmbDay.Items
            .OfType<DayItem>()
            .Select(PreviewFor)
            .Append(PreviewFor(null))
            .Distinct()
            .ToArray();

        var btnGenerate = new Button { Text = "Generate..." };
        btnGenerate.Click += (_, _) => Generate();

        var btnClose = new Button { Text = "Close", DialogResult = DialogResult.Cancel };

        // Both fields run to the right-hand edge, as they did.
        var fields = new FieldGrid();
        var captions = new[]
        {
            fields.AddRow("Day:", _cmbDay, stretch: true),
            fields.AddRow("Event name:", _txtName, stretch: true),
        };

        var page = new FillPage();

        // Everything but the line about the day and the buttons: on a screen
        // too short for all of it, it scrolls and they stay in view.
        //
        // In two cards (18 Nov 2026, dialog look): the day and the name it
        // goes out under, and what to put in, each with a line on what it
        // is for.
        var dayCard = new Section("The report");
        dayCard.Add(fields, gapBelow: UiLayout.HintGap);
        dayCard.Add(CardHint("Only days with races are listed. The name heads the page."), gapBelow: 0);

        var includeCard = new Section("Include");
        includeCard.Add(CardHint("What goes in beside the results and the fastest laps, which are always there."), gapBelow: UiLayout.HintGap);
        includeCard.Add(_chkQualifying, stretch: false, gapBelow: UiLayout.HintGap);
        includeCard.Add(_chkTech, stretch: false, gapBelow: UiLayout.HintGap);
        includeCard.Add(_chkRentals, stretch: false, gapBelow: UiLayout.HintGap);
        includeCard.Add(_chkLapDetail, stretch: false, gapBelow: 0);

        var body = DialogFit.Body(page);
        body.Add(lblIntro, gapBelow: UiLayout.RowGap);
        body.Add(dayCard, gapBelow: UiLayout.RowGap);
        body.Add(includeCard, gapBelow: 0);

        // Beside the buttons, so what Generate will cover is in view however
        // far the body is scrolled.
        page.Add(_lblPreview, gapBelow: UiLayout.RowGap);

        // Against the right-hand edge, Close furthest right, as they were -
        // and Tab still reaches Generate first: the bar takes them right to
        // left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnClose, btnGenerate), gapBelow: 0);
        btnGenerate.TabIndex = 0;
        btnClose.TabIndex = 1;

        Controls.Add(page);

        CancelButton = btnClose;
        UpdatePreview();

        return (page, body, captions);
    }

    private void UpdatePreview() => _lblPreview.Text = PreviewFor(_cmbDay.SelectedItem as DayItem);

    /// <summary>A card's line on what it is for, in the small muted type of a hint.</summary>
    private static Label CardHint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = AppTheme.TextMuted,
        Font = AppTheme.SmallFont,
    };

    /// <summary>What the line about the day says for <paramref name="day"/> - or, with none chosen, that there is nothing to report on.</summary>
    private string PreviewFor(DayItem? day)
    {
        if (day is null)
        {
            return "No recorded sessions to report on yet.";
        }

        var outstanding = _rentals.OutNow.Count;
        return $"{Plural.Of(day.SessionCount, "session")} on this day."
               + (outstanding > 0
                   ? $"  {Plural.Of(outstanding, "rental")} still out - {(outstanding == 1 ? "it" : "these")} will be flagged at the top."
                   : string.Empty);
    }

    private void Generate()
    {
        if (_cmbDay.SelectedItem is not DayItem day)
        {
            AppDialog.Show(this, "There are no recorded sessions to report on.", "Nothing to report",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var (raceEvent, severalRaceDays) = EventFor(day.Day);

        var options = new EventReportBuilder.Options(
            day.Day,
            string.IsNullOrWhiteSpace(_txtName.Text) ? "Race Day" : _txtName.Text.Trim(),
            _chkQualifying.Checked,
            _chkLapDetail.Checked,
            _chkTech.Checked,
            _chkRentals.Checked,
            raceEvent,
            severalRaceDays);

        using var dialog = new SaveFileDialog
        {
            Title = "Save event report",
            Filter = "Web page (*.html)|*.html",
            FileName = $"{day.Day:yyyy-MM-dd}_event-report.html",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            // WHY (23 Oct 2026, bug hunt 10): the days go in so a race whose
            // round belongs to no real race day is still reported on the day
            // it ran.
            var html = EventReportBuilder.Build(options, _history, _rounds, _classes,
                _profiles, _trackLayouts, _tech, _rentals, _handicaps, _events, EntryPaymentStore.Load());

            File.WriteAllText(dialog.FileName, html);

            if (AppDialog.Show(this, $"Report saved.\r\n\r\n{dialog.FileName}\r\n\r\nOpen it now?",
                    "Report ready", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't save the report.\r\n\r\n{ex.Message}", "Save failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// The race day the chosen date belongs to, so the report covers that
    /// meeting's own rounds - including any mains that ran past midnight -
    /// rather than whatever happened to start on the calendar date: the one
    /// race day that raced on that date. Null (the plain by-date report)
    /// when there is no event store or no race day raced then.
    ///
    /// When MORE than one race day raced on the date, also null, with
    /// SeveralRaceDays set so the report says why. It used to pick the
    /// current race day whenever that was one of them, and the other day's
    /// results silently vanished from the only report of that date; the
    /// by-date report has both.
    /// </summary>
    private (RaceEvent? Event, bool SeveralRaceDays) EventFor(DateTime day)
    {
        if (_events is not { } events)
        {
            return (null, false);
        }

        var matches = events.Real
            .Where(e => events.DaysFor(e, _rounds, _history).Contains(day.Date))
            .ToList();

        return matches.Count switch
        {
            1 => (matches[0], false),
            > 1 => (null, true),
            _ => (null, false),
        };
    }

    private sealed record DayItem(DateTime Day, int SessionCount)
    {
        public override string ToString() =>
            $"{Day.ToLongDateString()}   ({SessionCount} session{(SessionCount == 1 ? "" : "s")})";
    }
}
