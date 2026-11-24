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
/// On the layout pieces (DialogFit): tiles counting each verdict across the
/// top (18 Nov 2026, dialog jazz-up), the verdict wrapping under them, the
/// list taking the height and the buttons at the foot, Check again in the
/// app's colour. It used to be a
/// strip sized by hand to the first verdict over a list with columns of
/// fixed widths, which a larger text size cut short.
/// </summary>
public sealed class ReadinessCheckForm : Form
{
    /// <summary>The list shows its headings and at least this many cars before the window scrolls instead.</summary>
    private const int LeastRows = 4;

    // Internal rather than private (23 Nov 2026, bug hunt 24) so the rules in
    // Build and Headline can be tested without opening the window.
    internal enum Verdict
    {
        Ready,
        Weak,
        Missing,
        Stranger,
    }

    internal sealed record Line(Verdict Verdict, string Driver, string Transponder, string Detail);

    /// <summary>The colour the verdict over the list is shown in.</summary>
    internal enum HeadlineTone
    {
        Muted,
        Success,
        Warning,
        Danger,
    }

    /// <summary>How tall the window is inside its frame at the normal text size: the 520 it had, and room for the tiles.</summary>
    private const int DialogHeight = 520 + 70;

    private readonly List<Line> _lines;
    private readonly Label _lblHeadline = new();
    private readonly ListView _list = new();

    /// <summary>The cars heard fine, only just, not at all, and on track but not in this race - see ShowTiles.</summary>
    private readonly StatTile[] _tiles = { new(), new(), new(), new() };

    public ReadinessCheckForm(
        IEnumerable<string> roster,
        TransponderWatch watch,
        ProfileStore profiles,
        string rosterSource,
        bool splitLoop = false,
        Action? openMissionControl = null)
    {
        _lines = Build(roster, watch, profiles, splitLoop);

        Text = "Pre-race check";

        // WHY (23 Nov 2026, bug hunt 24): the verdict needs to know when there
        // was no list at all - see Headline.
        var rosterEmpty = !roster.Any(t => !string.IsNullOrWhiteSpace(t));

        BuildHeadline(rosterSource, rosterEmpty);
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
            _lines.AddRange(Build(roster, watch, profiles, splitLoop));
            BuildHeadline(rosterSource, rosterEmpty);
            Fill();
            ShowTiles();
        };

        _lblHeadline.AutoSize = true;

        // THE LOOK (18 Nov 2026, dialog jazz-up): the count of each verdict
        // in tiles across the top, red or amber while there is something to
        // fix, over the verdict and the list. The window is taller by the
        // tiles, so the list keeps its height. The dialog is built at the
        // text size in use, so the tiles take their faces here.
        foreach (var tile in _tiles)
        {
            tile.Value.Font = AppTheme.TitleFont;
            tile.Caption.Font = AppTheme.MicroFont;
        }

        Fill();

        var page = new FillPage();
        page.Add(new TileStrip(_tiles), gapBelow: UiLayout.RowGap);
        page.Add(_lblHeadline);
        page.AddFill(_list, _ => (LeastRows + 1) * (Pieces.LineHeight(_list.Font) + 6));

        // Against the right-hand edge, Close furthest right, as a dialog's
        // buttons go. Mission Control, when there is one to open, takes the
        // check further - the decoder, timing, the website - and closes this.
        var buttons = new ButtonBar(alignRight: true).With(btnClose, btnRecheck);

        if (openMissionControl is not null)
        {
            var btnMissionControl = new Button { Text = "Mission Control..." };
            btnMissionControl.Click += (_, _) =>
            {
                openMissionControl();
                Close();
            };

            buttons.With(btnMissionControl);
        }

        page.Add(buttons, gapBelow: 0);

        Controls.Add(page);

        AcceptButton = btnClose;
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);

        // Check again in the app's colour: it is what the window is kept
        // open for. After the theme pass, which does not know it.
        AppTheme.StyleButton(btnRecheck, AppTheme.ButtonKind.Primary);

        // After the theme pass too, which would otherwise give the values
        // the page's own colour.
        ShowTiles();

        // Each column at least as wide as it was made, or its heading; what
        // the decoder hears takes the rest - see Pieces.FitListColumns.
        // WHY (13 Nov 2026): the mark column was 34, which cut "OK" to "O..."
        // once the list's own padding was taken off; 48 holds it.
        Pieces.FitListColumns(_list, 3, 48, 170, 110, 420);

        DialogFit.Keep(this, page, width: 760, height: DialogHeight);
    }

    /// <summary>The tiles from the lines: each verdict's count, in the colour its rows are in while it is not nought.</summary>
    private void ShowTiles()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;

        void Show(StatTile tile, Verdict verdict, string caption, Color warn)
        {
            var n = _lines.Count(l => l.Verdict == verdict);

            tile.Set(n.ToString(culture), caption);
            tile.Value.ForeColor = n > 0 ? warn : AppTheme.TextDark;
        }

        Show(_tiles[0], Verdict.Ready, "Heard", AppTheme.Success);
        Show(_tiles[1], Verdict.Weak, "Weak", AppTheme.Warning);
        Show(_tiles[2], Verdict.Missing, "Not heard", AppTheme.Danger);
        Show(_tiles[3], Verdict.Stranger, "Not in race", AppTheme.Accent);
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
    internal static List<Line> Build(IEnumerable<string> roster, TransponderWatch watch, ProfileStore profiles, bool splitLoop)
    {
        // With a split loop, a car the line hears fine but that has no split
        // transponder races normally - it just gets no sector times. Said on
        // its line, without making it a fault.
        string SplitNote(string transponder) =>
            splitLoop && profiles.Get(transponder) is { } driver
                      && !(driver.SplitTransponders ?? new List<OwnedTransponder>()).Any(t => t is not null && !t.Retired)
                ? " No split transponder, so no sector times."
                : string.Empty;

        var lines = new List<Line>();
        var expected = new HashSet<string>(roster, StringComparer.OrdinalIgnoreCase);

        // WHY (23 Nov 2026, bug hunt 24): the watch files every pass under the
        // driver's MAIN transponder (TransponderWatch.AlternateTransponderOwner),
        // but a grid can hold a driver's second car's transponder. The old code
        // asked the watch for the id on the grid, never found it, and called a
        // car circulating fine "not heard at all" - then listed the same car
        // again as "on track but not in this race". Each id on the list is now
        // looked up as the watch files it, and the stranger test uses those ids.
        string HeardAs(string transponder) =>
            profiles.PrimaryForAlternate(transponder) ?? transponder;

        var expectedHeardAs = new HashSet<string>(expected.Select(HeardAs), StringComparer.OrdinalIgnoreCase);

        // WHY (23 Nov 2026, bug hunt 24): with no list at all (no round picked
        // and nobody on the board yet), every car warming up used to be called
        // "on track but not in this race" - in the wrong heat - when there was
        // simply nothing to check it against. They are shown as heard instead.
        if (!expected.Any(t => !string.IsNullOrWhiteSpace(t)))
        {
            foreach (var heard in watch.Recent())
            {
                lines.Add(HeardLine(heard, heard.TransponderId));
            }

            return Sorted(lines);
        }

        foreach (var transponder in expected.OrderBy(t => DriverNameFor(profiles, t), StringComparer.CurrentCultureIgnoreCase))
        {
            // The id as given as well, for a watch that files under each id.
            var sighting = watch.Of(HeardAs(transponder)) ?? watch.Of(transponder);

            if (sighting is null)
            {
                lines.Add(new Line(Verdict.Missing, DriverNameFor(profiles, transponder), transponder,
                    "Not heard at all. Transponder off, flat, or not fitted."));

                continue;
            }

            lines.Add(HeardLine(sighting, transponder));
        }

        foreach (var sighting in watch.Recent().Where(s => !expected.Contains(s.TransponderId) && !expectedHeardAs.Contains(s.TransponderId)))
        {
            lines.Add(new Line(Verdict.Stranger, DriverNameFor(profiles, sighting.TransponderId), sighting.TransponderId,
                $"On track but not in this race - {sighting.Passes} pass{(sighting.Passes == 1 ? string.Empty : "es")}. "
                + "Either they are in the wrong heat, or this is a car from the last one still circulating."));
        }

        return Sorted(lines);

        // A car that is being heard, shown under the id it is listed by: fine,
        // or only just.
        Line HeardLine(TransponderWatch.Sighting sighting, string transponder)
        {
            var ago = sighting.Ago;

            var when = ago.TotalSeconds < 90
                ? "just now"
                : $"{(int)ago.TotalMinutes} min ago";

            if (sighting.Weak)
            {
                return new Line(Verdict.Weak, DriverNameFor(profiles, transponder), transponder,
                    $"Heard {when}, but only just - {sighting.Passes} pass{(sighting.Passes == 1 ? string.Empty : "es")}, "
                    + $"best {sighting.BestHits} hit{(sighting.BestHits == 1 ? string.Empty : "s")}. "
                    + "Likely to be missed at racing speed. Check the battery and how it is mounted." + SplitNote(transponder));
            }

            return new Line(Verdict.Ready, DriverNameFor(profiles, transponder), transponder,
                $"Heard {when}, {sighting.Passes} pass{(sighting.Passes == 1 ? string.Empty : "es")}{(sighting.SignalKnown ? $", signal {sighting.AverageStrength}." : ".")}" + SplitNote(transponder));
        }

        static List<Line> Sorted(List<Line> unsorted) =>
            unsorted
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

    private void BuildHeadline(string rosterSource, bool rosterEmpty)
    {
        _lblHeadline.Font = AppTheme.BodyFont;

        var (text, tone) = Headline(_lines, rosterSource, rosterEmpty);

        _lblHeadline.Text = text;
        _lblHeadline.ForeColor = tone switch
        {
            HeadlineTone.Success => AppTheme.Success,
            HeadlineTone.Warning => AppTheme.Warning,
            HeadlineTone.Danger => AppTheme.Danger,
            _ => AppTheme.TextMuted,
        };
    }

    /// <summary>The verdict over the list, and the colour it is shown in - from the lines alone, so it can be tested without the window.</summary>
    internal static (string Text, HeadlineTone Tone) Headline(IReadOnlyList<Line> lines, string rosterSource, bool rosterEmpty)
    {
        var missing = lines.Count(l => l.Verdict == Verdict.Missing);
        var weak = lines.Count(l => l.Verdict == Verdict.Weak);
        var strangers = lines.Count(l => l.Verdict == Verdict.Stranger);
        var ready = lines.Count(l => l.Verdict == Verdict.Ready);

        if (lines.Count == 0)
        {
            return ("Nothing to check - no roster, and nothing heard from the decoder in the last few minutes.", HeadlineTone.Muted);
        }

        // WHY (23 Nov 2026, bug hunt 24): with no list to check against, the
        // old verdict said "N on track who should not be ... Put the cars that
        // failed on the track", about cars that were simply warming up. It now
        // says there is no list, and still warns about any car only just heard.
        if (rosterEmpty)
        {
            var heard = $"No list to check against yet - pick a round to check who should be out there. "
                        + $"{lines.Count} car{(lines.Count == 1 ? " is" : "s are")} being heard.";

            return weak > 0
                ? (heard + $" {weak} only just being heard - check the battery and how it is mounted.", HeadlineTone.Warning)
                : (heard, HeadlineTone.Muted);
        }

        if (missing == 0 && weak == 0 && strangers == 0)
        {
            return ($"All {ready} clear. Every car on the {rosterSource} is being heard. Good to go.", HeadlineTone.Success);
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

        return ($"{string.Join(", ", parts)}. Checked against the {rosterSource}. "
                + "Put the cars that failed on the track and press Check again.",
            missing > 0 ? HeadlineTone.Danger : HeadlineTone.Warning);
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
