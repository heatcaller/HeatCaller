namespace HeatCaller;

/// <summary>
/// The first tab of Race Analytics: what happened at the club lately, and
/// the lap records.
///
/// 13 Nov 2026, the Race Engineer rework (see RaceCoach): the tab opens with
/// a headline about the club's month, then the news from the last race
/// night - a record still standing, the PBs taken, who got quicker through
/// a race, whose mistakes cost the most (RaceCoach.Overview) - and then the
/// track records, which are still the numbers on the wall of every club in
/// the world. It used to open on four counts, which said how much racing
/// there had been and nothing about it.
///
/// Everything is read from the saved session history, so it costs nothing to
/// keep and it is right the moment a race finishes.
/// </summary>
public sealed class AnalyticsOverviewControl : UserControl
{
    /// <summary>The track layouts, for a layout's length - the car lengths beside each gap (RaceCoach). Null says no car lengths.</summary>
    public TrackLayoutStore? Layouts { get; set; }

    private readonly SessionHistoryStore _history;

    // 17 Oct 2026, Bug Hunt 8, A30-2: for the shared record rule (rentals
    // and excluded guests hold no records). Null reads as "no profiles",
    // which is the rule's own answer for an unknown transponder.
    private readonly ProfileStore? _profiles;

    private readonly Label _headline;
    private readonly Label _lblNight = new();
    private readonly NewsFeed _feed = new();
    private Section? _newsCard;

    // The quickest lap anywhere and the club's totals, in a quiet line under
    // the records: the old "at a glance" tiles, kept as a sentence.
    private readonly Label _lblBestWho = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblEmpty = new();

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    public AnalyticsOverviewControl(SessionHistoryStore history, ProfileStore? profiles = null)
    {
        _history = history;
        _profiles = profiles;

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once: this page sits in a tab
        // whose row is drawn in the bold face, which it would otherwise take,
        // and set once it stayed at the size it was built at. See TextSize.
        _textSize.Wear(this, () => AppTheme.BodyFont);

        _headline = CoachPieces.Headline(_textSize);

        BuildLayout();
        RefreshAll();
    }

    /// <summary>The headline sentence - for the tests.</summary>
    internal string HeadlineText => _headline.Text;

    /// <summary>The news on show - for the tests.</summary>
    internal IReadOnlyList<RaceCoach.NewsItem> News => _feed.Items;

    /// <summary>The track records table - for the tests.</summary>
    internal DataGridView RecordsGrid => _grid;

    /// <summary>The page's own face changed - handed the body face at a new text size - so everything else it keeps in step follows. See TextSize.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>A new text size, reaching this page through its tab's bold face - the only way it does, since the page wears a face of its own. See TextSize.</summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>
    /// The headline, the news card, and the track records in a card taking
    /// the height that is left. On the layout pieces (LayoutPieces.cs), so
    /// nothing is placed by hand.
    /// </summary>
    private void BuildLayout()
    {
        // A driver's or a layout's name is text: an '&' in it was dropped and
        // the letter after it underlined (10 Nov 2026, bug hunt 16).
        _headline.UseMnemonic = false;

        // --- From the last race night ---
        _lblNight.AutoSize = true;
        _lblNight.UseMnemonic = false;
        _lblNight.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_lblNight, () => AppTheme.SmallFont);

        _textSize.Wear(_feed, () => AppTheme.BodyFont);

        var news = new Section("From the last race night");

        news.Add(_lblNight);
        news.Add(_feed, gapBelow: 0);
        _newsCard = news;

        // --- Track records ---
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "layout", HeaderText = "Track layout", FillWeight = 110 },
            new DataGridViewTextBoxColumn { Name = "record", HeaderText = "Lap record", FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "holder", HeaderText = "Held by", FillWeight = 100 },
            new DataGridViewTextBoxColumn { Name = "set", HeaderText = "Set", FillWeight = 65 },
            new DataGridViewTextBoxColumn { Name = "next", HeaderText = "Next closest", FillWeight = 170 },
            new DataGridViewTextBoxColumn { Name = "races", HeaderText = "Races here", FillWeight = 55 });

        // Every heading whole, at the text size in use - see Pieces.KeepHeadingsWhole.
        _textSize.Keep(() => Pieces.KeepHeadingsWhole(_grid));

        _lblBestWho.AutoSize = true;
        _lblBestWho.UseMnemonic = false;
        _lblBestWho.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_lblBestWho, () => AppTheme.SmallFont);

        var records = new Section("Track records");

        records.AddFill(_grid, () => _grid.ColumnHeadersHeight + 3 * _grid.RowTemplate.Height, gapBelow: UiLayout.RowGap);
        records.Add(_lblBestWho, gapBelow: 0);

        // Shown in place of the list when there is nothing in it - in the
        // list's own room, behind it, and centred there. An empty grid with
        // column headings and no rows reads as broken; a sentence reads as
        // "nothing yet".
        _lblEmpty.AutoSize = false;
        _lblEmpty.Dock = DockStyle.Fill;
        _lblEmpty.TextAlign = ContentAlignment.MiddleCenter;
        _lblEmpty.ForeColor = AppTheme.TextMuted;
        _lblEmpty.Text = "No races recorded yet - run one and the records start here.";
        _lblEmpty.Visible = false;

        _grid.Parent!.Controls.Add(_lblEmpty);

        // --- The page ---
        var page = new FillPage();

        page.Add(_headline, gapBelow: UiLayout.RowGap);
        page.Add(news);
        page.AddFill(records, records.LeastHeight, gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>Called when the tab is opened, since races finish elsewhere.</summary>
    public void RefreshAll()
    {
        // Saved demo races are left out of every figure here: they are
        // simulated, so counting them would pad the race and lap totals
        // and could put an invented lap at the top of the records.
        var everything = _history.All
            .Where(s => !s.IsNotScored())
            .ToList();

        // --- The headline and the news ---
        var club = RaceCoach.Overview(everything, _profiles, Layouts);

        _headline.Text = club.Headline;

        if (club.NightLocal is { } night)
        {
            var layouts = RaceCoach.LastNight(everything)
                .Select(s => s.TrackLayoutName.Trim())
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _lblNight.Text = layouts.Count == 0
                ? night.ToString("dddd d MMMM")
                : $"{night:dddd d MMMM}, on {string.Join(" and ", layouts)}";
        }

        _feed.Show(club.Items);

        if (_newsCard is not null)
        {
            _newsCard.Visible = club.NightLocal is not null;
        }

        // --- The quickest lap anywhere, and the totals ---
        var races = everything.Count(s => s.SessionType == RaceSessionType.Race);

        var laps = everything
            .SelectMany(s => s.Results)
            .Where(r => !GhostService.IsGhost(r.Transponder))
            .Sum(r => (long)r.Laps);

        var best = RecordLaps(everything, _profiles).FirstOrDefault();

        if (best.Result is null)
        {
            _lblBestWho.Text = string.Empty;
        }
        else
        {
            var layout = string.IsNullOrWhiteSpace(best.Session.TrackLayoutName)
                ? "an unnamed layout"
                : best.Session.TrackLayoutName;

            _lblBestWho.Text = $"Quickest lap anywhere: {Lap(best.Result.FastLapMs)} by {RaceCoach.WhoIs(best.Result)} on {layout}, "
                + $"{best.Session.StartedAtUtc.ToLocalTime():d MMM yyyy}. {races:N0} race{(races == 1 ? "" : "s")} and {laps:N0} laps so far.";
        }

        FillRecords(everything);
    }

    /// <summary>
    /// Every lap that could stand as a record, quickest first.
    ///
    /// 17 Oct 2026, Bug Hunt 8, A30-2: through
    /// AchievementEvaluator.CanHoldTrackRecord rather than a ghost check
    /// alone, so this page names the same holder as the badge and the live
    /// record - a rental or an ExcludeFromRecords guest is never it. No
    /// SessionType filter: the evaluator's own BestEverOn has none either.
    /// The rule itself is RaceCoach's (13 Nov 2026), so the news and the
    /// Personal Bests tab's gap to the record use the same one.
    /// </summary>
    internal static IEnumerable<(SessionRecord Session, SessionResultEntry Result)> RecordLaps(
        IEnumerable<SessionRecord> sessions, ProfileStore? profiles) =>
        RaceCoach.RecordLaps(sessions, profiles);

    /// <summary>
    /// One row per track layout: its record, who holds it, who is next
    /// closest and by how much, and how much racing has happened there.
    ///
    /// Grouped by layout name rather than by a layout id, because a session
    /// records the name it ran under - renaming a layout starts a new line
    /// rather than rewriting history, which is the honest behaviour when
    /// nobody can say whether the track was also changed.
    /// </summary>
    private void FillRecords(IReadOnlyList<SessionRecord> sessions)
    {
        _grid.Rows.Clear();

        var groups = sessions
            .Where(s => !string.IsNullOrWhiteSpace(s.TrackLayoutName))
            .GroupBy(s => s.TrackLayoutName, StringComparer.CurrentCultureIgnoreCase)
            .Select(group =>
            {
                var laps = RecordLaps(group, _profiles).ToList();

                var record = "-";
                var holder = "-";
                var when = "-";
                var next = "-";

                if (laps.Count > 0)
                {
                    var (session, result) = laps[0];

                    record = Lap(result.FastLapMs);
                    holder = RaceCoach.WhoIs(result);
                    when = session.StartedAtUtc.ToLocalTime().ToString("d MMM yyyy");

                    // The quickest lap by anybody else: how close the field
                    // is to the record, in car lengths when the layout has a
                    // length (Track Layouts).
                    var key = holder.ToUpperInvariant();
                    var other = laps.FirstOrDefault(x => RaceCoach.WhoIs(x.Result).ToUpperInvariant() != key);

                    if (other.Result is not null)
                    {
                        var gap = other.Result.FastLapMs - result.FastLapMs;
                        var lengths = RaceCoach.SayLengths(gap, result.FastLapMs, RaceCoach.TrackFeet(Layouts, group.Key));

                        next = $"{RaceCoach.WhoIs(other.Result)}, +{RaceCoach.Gap(gap)}"
                            + (lengths is null ? string.Empty : $" ({lengths})");
                    }
                }

                return new
                {
                    Layout = group.Key,
                    Record = record,
                    Holder = holder,
                    When = when,
                    Next = next,
                    Races = group.Count(s => s.SessionType == RaceSessionType.Race),
                };
            })
            .OrderBy(g => g.Layout, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        foreach (var group in groups)
        {
            _grid.Rows.Add(group.Layout, group.Record, group.Holder, group.When, group.Next, group.Races.ToString());
        }

        var empty = _grid.Rows.Count == 0;

        _grid.Visible = !empty;
        _lblEmpty.Visible = empty;
    }

    /// <summary>A lap time as people say it: seconds to three places.</summary>
    private static string Lap(long ms) => $"{ms / 1000.0:0.000}";
}

/// <summary>
/// The Overview's news: each item a short coloured badge (REC, PB, an up
/// arrow, "!") beside a title in the bold face and a line of figures under
/// it, one under another with a rule between. Drawn rather than built from
/// labels, so a feed of five is one control to measure; as tall as its
/// items need at the width it is given, so it sits in a card like a hint.
/// </summary>
internal sealed class NewsFeed : Control
{
    private IReadOnlyList<RaceCoach.NewsItem> _items = Array.Empty<RaceCoach.NewsItem>();

    private const TextFormatFlags Wrap =
        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix
        | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl;

    private const TextFormatFlags Centred =
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
        | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    /// <summary>What it says when the night had nothing worth telling.</summary>
    public const string Quiet = "A quiet night: no records, PBs or standout races to report.";

    public NewsFeed()
    {
        // Held while the window is being built - see LayoutFreeze.
        LayoutFreeze.Adopt(this);

        AutoSize = true;
        Margin = Padding.Empty;
        DoubleBuffered = true;
        ResizeRedraw = true;
        Tag = AppTheme.KeepBackColor;
        AccessibleRole = AccessibleRole.List;
        AccessibleName = "News from the last race night";
    }

    /// <summary>The items on show.</summary>
    public IReadOnlyList<RaceCoach.NewsItem> Items => _items;

    /// <summary>Shows these items, and asks to be measured again.</summary>
    public void Show(IReadOnlyList<RaceCoach.NewsItem> items)
    {
        _items = items;

        // Read aloud as the page reads: each title and its figures.
        Text = items.Count == 0 ? Quiet : string.Join(Environment.NewLine, items.Select(i => $"{i.Title}. {i.Detail}"));

        Changed();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Changed();
    }

    /// <summary>A new height, perhaps: the card it is in measures it again.</summary>
    private void Changed()
    {
        Parent?.PerformLayout(this, nameof(Bounds));
        Invalidate();
    }

    private static int Gap => UiLayout.AtTextSize(UiLayout.RowGap);

    /// <summary>The badge's size: wide enough for "REC" in the micro face, as tall as a title line and a little.</summary>
    private static Size BadgeSize()
    {
        var text = TextRenderer.MeasureText("REC", AppTheme.MicroFont, Size.Empty, Centred);
        var height = AppTheme.BoldFont.Height + UiLayout.AtTextSize(8);

        return new Size(Math.Max(height, text.Width + UiLayout.AtTextSize(14)), height);
    }

    /// <summary>Each item's room at <paramref name="width"/>: where its title and its figures go, and how tall it is.</summary>
    private List<(Rectangle Title, Rectangle Detail, int Height)> Arrange(int width)
    {
        var badge = BadgeSize();
        var left = badge.Width + UiLayout.AtTextSize(12);
        var room = Math.Max(1, width - left);
        var arranged = new List<(Rectangle, Rectangle, int)>();

        foreach (var item in _items)
        {
            var title = TextRenderer.MeasureText(item.Title, AppTheme.BoldFont, new Size(room, 0), Wrap);
            var detail = TextRenderer.MeasureText(item.Detail, AppTheme.SmallFont, new Size(room, 0), Wrap);
            var titleBox = new Rectangle(left, 0, room, title.Height);
            var detailBox = new Rectangle(left, title.Height + UiLayout.AtTextSize(3), room, detail.Height);

            arranged.Add((titleBox, detailBox, Math.Max(badge.Height, detailBox.Bottom)));
        }

        return arranged;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 1 and < int.MaxValue / 4 ? proposedSize.Width : Math.Max(Width, 400);

        if (_items.Count == 0)
        {
            var quiet = TextRenderer.MeasureText(Quiet, AppTheme.BodyFont, new Size(width, 0), Wrap);
            return new Size(width, quiet.Height);
        }

        var rows = Arrange(width);

        return new Size(width, rows.Sum(r => r.Height) + Math.Max(0, rows.Count - 1) * (2 * Gap + 1));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;

        if (_items.Count == 0)
        {
            TextRenderer.DrawText(g, Quiet, AppTheme.BodyFont, ClientRectangle, AppTheme.TextMuted, Wrap);
            return;
        }

        var badge = BadgeSize();
        var rows = Arrange(Width);
        var top = 0;

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var (title, detail, height) = rows[i];
            var tone = Tone(item.Kind);

            var box = new RectangleF(0, top, badge.Width, badge.Height);

            using (var path = AppTheme.RoundedRect(box, UiLayout.AtDpi(6)))
            using (var fill = new SolidBrush(RosterInk.Blend(tone, AppTheme.Surface, 0.84f)))
            {
                g.FillPath(fill, path);
            }

            // The words in the micro face; a lone mark (the arrow, the "!")
            // in the bold one, where it is big enough to read - the micro
            // face drew the arrow as a speck.
            var face = item.Badge.Length > 1 ? AppTheme.MicroFont : AppTheme.BoldFont;

            TextRenderer.DrawText(g, item.Badge, face, Rectangle.Round(box), tone, Centred);

            title.Offset(0, top);
            detail.Offset(0, top);

            TextRenderer.DrawText(g, item.Title, AppTheme.BoldFont, title, AppTheme.TextDark, Wrap);
            TextRenderer.DrawText(g, item.Detail, AppTheme.SmallFont, detail, AppTheme.TextMuted, Wrap);

            top += height;

            if (i < _items.Count - 1)
            {
                top += Gap;

                using var rule = new Pen(AppTheme.Border);
                g.DrawLine(rule, title.Left, top, Width, top);

                top += Gap + 1;
            }
        }
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        using var behind = new SolidBrush(Parent?.BackColor ?? AppTheme.Surface);
        pevent.Graphics.FillRectangle(behind, ClientRectangle);
    }

    /// <summary>The badge's colour: the accent for a record, the primary for a PB, good for quicker, warning for mistakes.</summary>
    private static Color Tone(RaceCoach.NewsKind kind) => kind switch
    {
        RaceCoach.NewsKind.Record => AppTheme.Accent,
        RaceCoach.NewsKind.PersonalBest => AppTheme.Primary,
        RaceCoach.NewsKind.GotQuicker => AppTheme.Success,
        _ => AppTheme.WarningDark,
    };
}
