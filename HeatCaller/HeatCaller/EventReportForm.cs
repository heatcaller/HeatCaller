using System.Diagnostics;

namespace HeatCaller;

/// <summary>
/// Chooses what goes in the event report and where it's saved.
///
/// The day is picked from days that actually have sessions, rather than a
/// free date picker - choosing a day with nothing on it is the only way to
/// get an empty report, so the dialog simply doesn't offer it.
/// </summary>
public sealed class EventReportForm : Form
{
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
    private readonly CheckBox _chkQualifying = new();
    private readonly CheckBox _chkLapDetail = new();
    private readonly CheckBox _chkTech = new();
    private readonly CheckBox _chkRentals = new();
    private readonly Label _lblPreview = new();

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
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 380);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        BuildLayout();
        AppTheme.ApplyRecursively(this);
    }

    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "A single page covering the whole day - results by class, fastest laps, and anything still outstanding. "
                 + "Saves as HTML so it can be printed, emailed, or opened on a phone.",
            Location = new Point(16, 16),
            AutoSize = true,
            MaximumSize = new Size(448, 0),
            ForeColor = AppTheme.TextMuted,
        };

        var lblDay = new Label { Text = "Day:", Location = new Point(16, 74), AutoSize = true };
        _cmbDay.Location = new Point(110, 71);
        _cmbDay.Width = 354;
        _cmbDay.DropDownStyle = ComboBoxStyle.DropDownList;

        // Only days that have sessions - an empty report helps nobody.
        // Demo races are left out of the report, so they don't count here.
        var days = _history.All
            .Where(s => !s.IsDemoSession())
            .Select(s => s.StartedAtUtc.ToLocalTime().Date)
            .Distinct()
            .OrderByDescending(d => d)
            .ToList();

        foreach (var day in days)
        {
            _cmbDay.Items.Add(new DayItem(day, _history.All.Count(s => !s.IsDemoSession() && s.StartedAtUtc.ToLocalTime().Date == day)));
        }

        if (_cmbDay.Items.Count > 0)
        {
            _cmbDay.SelectedIndex = 0;
        }

        _cmbDay.SelectedIndexChanged += (_, _) => UpdatePreview();

        var lblName = new Label { Text = "Event name:", Location = new Point(16, 110), AutoSize = true };
        _txtName.Location = new Point(110, 107);
        _txtName.Width = 354;
        _txtName.Text = "Club Race Day";

        var lblInclude = new Label { Text = "Include", Location = new Point(16, 152), AutoSize = true, Font = AppTheme.BoldFont };

        _chkQualifying.Text = "Qualifying standings";
        _chkQualifying.Location = new Point(28, 178);
        _chkQualifying.AutoSize = true;
        _chkQualifying.Checked = true;

        _chkTech.Text = "Tech inspection summary";
        _chkTech.Location = new Point(28, 204);
        _chkTech.AutoSize = true;
        _chkTech.Checked = true;

        _chkRentals.Text = "Rental transponders not returned";
        _chkRentals.Location = new Point(28, 230);
        _chkRentals.AutoSize = true;
        _chkRentals.Checked = true;

        _chkLapDetail.Text = "Every lap time (makes the report much longer)";
        _chkLapDetail.Location = new Point(28, 256);
        _chkLapDetail.AutoSize = true;
        _chkLapDetail.Checked = false;

        _lblPreview.Location = new Point(16, 292);
        _lblPreview.Size = new Size(448, 36);
        _lblPreview.ForeColor = AppTheme.TextMuted;
        _lblPreview.Font = AppTheme.SmallFont;

        var btnGenerate = new Button { Text = "Generate..." };
        btnGenerate.Click += (_, _) => Generate();

        var btnClose = new Button { Text = "Close", DialogResult = DialogResult.Cancel };

        AppTheme.LayoutButtonRow(250, 336, 8, btnGenerate, btnClose);

        Controls.AddRange(new Control[]
        {
            lblIntro, lblDay, _cmbDay, lblName, _txtName, lblInclude,
            _chkQualifying, _chkTech, _chkRentals, _chkLapDetail, _lblPreview,
            btnGenerate, btnClose,
        });

        CancelButton = btnClose;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (_cmbDay.SelectedItem is not DayItem day)
        {
            _lblPreview.Text = "No recorded sessions to report on yet.";
            return;
        }

        var outstanding = _rentals.OutNow.Count;
        _lblPreview.Text = $"{Plural.Of(day.SessionCount, "session")} on this day."
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
            var html = EventReportBuilder.Build(options, _history, _rounds, _classes,
                _profiles, _trackLayouts, _tech, _rentals, _handicaps);

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
