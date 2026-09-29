namespace HeatCaller;

/// <summary>
/// The check worth doing before the green flag.
///
/// It answers one question - is every car that is supposed to be in this
/// race actually being heard by the decoder - and it answers it while there
/// is still time to do something. A flat transponder found here costs
/// somebody two minutes; found after the race it costs everybody a re-run,
/// and the driver a result they cannot get back.
///
/// The roster is the round's if a round is selected, because that is the
/// list somebody has already agreed to. With no round it falls back to the
/// drivers already on the board, which is the next most likely answer and
/// is at least a list of real people rather than every profile ever saved.
///
/// On the layout pieces (DialogFit): the verdict wrapping across the top,
/// the list taking the height and the buttons at the foot. It used to be a
/// strip sized by hand to the first verdict over a list with columns of
/// fixed widths, which a larger text size cut short.
/// </summary>
public sealed class ReadinessCheckForm : Form
{
    /// <summary>The list shows its headings and at least this many cars before the window scrolls instead.</summary>
    private const int LeastRows = 4;

    private enum Verdict
    {
        Ready,
        Weak,
        Missing,
        Stranger,
    }

    private sealed record Line(Verdict Verdict, string Driver, string Transponder, string Detail);

    private readonly List<Line> _lines;
    private readonly Label _lblHeadline = new();
    private readonly ListView _list = new();

    public ReadinessCheckForm(
        IEnumerable<string> roster,
        TransponderWatch watch,
        ProfileStore profiles,
        string rosterSource)
    {
        _lines = Build(roster, watch, profiles);

        Text = "Pre-race check";

        BuildHeadline(rosterSource);
        BuildList();

        var btnClose = new Button { Text = "Close" };
        btnClose.Click += (_, _) => Close();

        // A verdict of a different length - one line of "All clear" after
        // two lines of faults, or the other way round - is a label that
        // wraps to the page, so the list under it moves with it.
        var btnRecheck = new Button { Text = "Check again" };
        btnRecheck.Click += (_, _) =>
        {
            _lines.Clear();
            _lines.AddRange(Build(roster, watch, profiles));
            BuildHeadline(rosterSource);
            Fill();
        };

        _lblHeadline.AutoSize = true;

        Fill();

        var page = new FillPage();
        page.Add(_lblHeadline);
        page.AddFill(_list, _ => (LeastRows + 1) * (Pieces.LineHeight(_list.Font) + 6));

        // Against the right-hand edge, Close furthest right, as a dialog's buttons go.
        page.Add(new ButtonBar(alignRight: true).With(btnClose, btnRecheck), gapBelow: 0);

        Controls.Add(page);

        AcceptButton = btnClose;
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);

        // Each column at least as wide as it was made, or its heading; what
        // the decoder hears takes the rest - see Pieces.FitListColumns.
        Pieces.FitListColumns(_list, 3, 34, 170, 110, 420);

        DialogFit.Keep(this, page, width: 760, height: 520);
    }

    /// <summary>
    /// Compares who should be out there against who is being heard.
    ///
    /// Both directions matter. A driver on the roster the decoder cannot
    /// hear is the obvious fault. A transponder being heard that is NOT on
    /// the roster is the other one, and it is the fault that quietly ruins
    /// a heat - somebody warming up on the wrong side of the fence, or a
    /// car from the previous class still circulating, either of which will
    /// appear in the results as a driver nobody expected.
    /// </summary>
    private static List<Line> Build(IEnumerable<string> roster, TransponderWatch watch, ProfileStore profiles)
    {
        var lines = new List<Line>();
        var expected = new HashSet<string>(roster, StringComparer.OrdinalIgnoreCase);

        foreach (var transponder in expected.OrderBy(t => DriverNameFor(profiles, t), StringComparer.CurrentCultureIgnoreCase))
        {
            var sighting = watch.Of(transponder);

            if (sighting is null)
            {
                lines.Add(new Line(Verdict.Missing, DriverNameFor(profiles, transponder), transponder,
                    "Not heard at all. Transponder off, flat, or not fitted."));

                continue;
            }

            var ago = sighting.Ago;

            var when = ago.TotalSeconds < 90
                ? "just now"
                : $"{(int)ago.TotalMinutes} min ago";

            if (sighting.Weak)
            {
                lines.Add(new Line(Verdict.Weak, DriverNameFor(profiles, transponder), transponder,
                    $"Heard {when}, but only just - {sighting.Passes} pass{(sighting.Passes == 1 ? string.Empty : "es")}, "
                    + $"best {sighting.BestHits} hit{(sighting.BestHits == 1 ? string.Empty : "s")}. "
                    + "Likely to be missed at racing speed. Check the battery and how it is mounted."));

                continue;
            }

            lines.Add(new Line(Verdict.Ready, DriverNameFor(profiles, transponder), transponder,
                $"Heard {when}, {sighting.Passes} pass{(sighting.Passes == 1 ? string.Empty : "es")}{(sighting.SignalKnown ? $", signal {sighting.AverageStrength}." : ".")}"));
        }

        foreach (var sighting in watch.Recent().Where(s => !expected.Contains(s.TransponderId)))
        {
            lines.Add(new Line(Verdict.Stranger, DriverNameFor(profiles, sighting.TransponderId), sighting.TransponderId,
                $"On track but not in this race - {sighting.Passes} pass{(sighting.Passes == 1 ? string.Empty : "es")}. "
                + "Either they are in the wrong heat, or this is a car from the last one still circulating."));
        }

        return lines
            .OrderBy(l => l.Verdict switch
            {
                Verdict.Missing => 0,
                Verdict.Weak => 1,
                Verdict.Stranger => 2,
                _ => 3,
            })
            .ThenBy(l => l.Driver, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Whoever that transponder belongs to, or the bare id when nobody.
    ///
    /// Not called Name: a Form inherits Control.Name, and a static
    /// method of that name hides it - which compiles, with a warning,
    /// and leaves a member of the base class shadowed for no reason.
    /// </summary>
    private static string DriverNameFor(ProfileStore profiles, string transponder)
    {
        var profile = profiles.Get(transponder);

        return profile is null || string.IsNullOrWhiteSpace(profile.DriverName)
            ? transponder
            : profile.DriverName;
    }

    private void BuildHeadline(string rosterSource)
    {
        _lblHeadline.Font = AppTheme.BodyFont;

        var missing = _lines.Count(l => l.Verdict == Verdict.Missing);
        var weak = _lines.Count(l => l.Verdict == Verdict.Weak);
        var strangers = _lines.Count(l => l.Verdict == Verdict.Stranger);
        var ready = _lines.Count(l => l.Verdict == Verdict.Ready);

        if (_lines.Count == 0)
        {
            _lblHeadline.Text = "Nothing to check - no roster, and nothing heard from the decoder in the last few minutes.";
            _lblHeadline.ForeColor = AppTheme.TextMuted;
            return;
        }

        if (missing == 0 && weak == 0 && strangers == 0)
        {
            _lblHeadline.Text = $"All {ready} clear. Every car on the {rosterSource} is being heard. Good to go.";
            _lblHeadline.ForeColor = AppTheme.Success;
            return;
        }

        var parts = new List<string>();

        if (missing > 0)
        {
            parts.Add($"{missing} not heard at all");
        }

        if (weak > 0)
        {
            parts.Add($"{weak} only just being heard");
        }

        if (strangers > 0)
        {
            parts.Add($"{strangers} on track who should not be");
        }

        _lblHeadline.Text = $"{string.Join(", ", parts)}. Checked against the {rosterSource}. "
                            + "Put the cars that failed on the track and press Check again.";

        _lblHeadline.ForeColor = missing > 0 ? AppTheme.Danger : AppTheme.Warning;
    }

    private void BuildList()
    {
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = false;
        _list.HideSelection = false;

        // Their widths are the list's to fit - see the constructor.
        _list.Columns.Add(string.Empty);
        _list.Columns.Add("Driver");
        _list.Columns.Add("Transponder");
        _list.Columns.Add("What the decoder hears");
    }

    private void Fill()
    {
        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var line in _lines)
        {
            var mark = line.Verdict switch
            {
                Verdict.Ready => "OK",
                Verdict.Weak => "!",
                Verdict.Missing => "X",
                _ => "?",
            };

            var item = new ListViewItem(new[] { mark, line.Driver, line.Transponder, line.Detail });

            item.ForeColor = line.Verdict switch
            {
                Verdict.Ready => AppTheme.TextDark,
                Verdict.Weak => AppTheme.Warning,
                Verdict.Missing => AppTheme.Danger,
                _ => AppTheme.Accent,
            };

            _list.Items.Add(item);
        }

        _list.EndUpdate();
    }
}
