using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace HeatCaller;

/// <summary>
/// Every badge, for one driver at a time: what they've earned and what they
/// haven't.
///
/// Locked badges are shown rather than hidden, greyed with the rule that
/// earns them. A list of only what you already have says nothing about what
/// to go and do; the point of the page is the ones still out of reach.
///
/// Drawn as a wall of medallion cards rather than a details-view list. A
/// badge is a reward, and a reward rendered as a row in a table with a
/// single glyph in the first column does not feel like one - which is
/// exactly how this page used to look.
/// </summary>
public sealed class AchievementsControl : UserControl
{
    private readonly ProfileStore _profiles;

    /// <summary>Where the app is used (AppSettings.RacingAt) - null on a page built without it, which then offers every badge.</summary>
    private readonly AppSettings? _settings;

    private readonly ComboBox _cmbDriver = new();
    private readonly CheckBox _chkHideLocked = new ThemedCheckBox();

    /// <summary>Where you race: Club or At home - which badges are on offer.</summary>
    private readonly ComboBox _cmbWhere = new();

    /// <summary>Shows the badges not on offer where you race too (club-sized ones at home, home ones at a club).</summary>
    private readonly CheckBox _chkShowOthers = new ThemedCheckBox();

    /// <summary>Whether any badge is not on offer where you race - kept rather than read back from the tick's Visible, which reads false while the page is hidden.</summary>
    private bool _haveOthers;

    /// <summary>Set while the Where combo is being filled, so filling it changes nothing.</summary>
    private bool _fillingWhere;

    /// <summary>Raised after Where you race was changed here - for the club website's badge list to be sent again.</summary>
    public event Action? RacingAtChanged;
    private readonly ProgressStrip _progress = new();
    private readonly Panel _scroll = new();
    private readonly BadgeWall _wall = new();

    private AchievementStore _store = AchievementStore.Load();

    public AchievementsControl(ProfileStore profiles, AppSettings? settings = null)
    {
        _profiles = profiles;
        _settings = settings;

        Dock = DockStyle.Fill;
        BackColor = AppTheme.Background;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize. No MinimumSize: the 860x520 floor was honoured
        // over the size the page was given. A short window scrolls the page
        // instead (FillPage), and the wall keeps its own scrollbar.
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshAll();
    }

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The driver picker's width at the normal text size - never narrower, and as wide as the longest entry.</summary>
    private const int DriverPickerWidth = 280;

    /// <summary>The least the wall gets at the normal text size - about a row of badges. Below that the page scrolls.</summary>
    private const int WallLeastHeight = 150;

    /// <summary>The driver picker's width as last measured - see Pieces.PickerWidth. Forgotten when the list is refilled.</summary>
    private (Font Font, int Count, int Width)? _driverWidth;

    /// <summary>The where-you-race picker's width as last measured - see Pieces.PickerWidth.</summary>
    private (Font Font, int Count, int Width)? _whereWidth;

    /// <summary>The picker's line - held so a refilled list can re-measure it.</summary>
    private FieldRow? _pickRow;

    /// <summary>The page's own face changed - handed the body face at a new text size - so everything else it keeps in step follows. See TextSize.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>A new text size, reaching this page through its parent's font - the only way it does, since the page wears a face of its own. See TextSize.</summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>
    /// The driver picker along a line with its switch and a hint, the
    /// progress strip under it, and the wall taking the height that is
    /// left, scrolling inside itself. On the layout pieces (LayoutPieces.cs).
    ///
    /// It used to be a header placed by hand - the picker's line measured
    /// once, at build, in faces set once - over the wall docked to fill,
    /// with an 860x520 floor under all of it.
    /// </summary>
    private void BuildLayout()
    {
        _cmbDriver.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbDriver.SelectedIndexChanged += (_, _) => RenderBadges();

        _chkHideLocked.Text = "Earned only";
        _chkHideLocked.AutoSize = true;
        _chkHideLocked.CheckedChanged += (_, _) => RenderBadges();

        // At the end of the line, in the small type - or on a line of its
        // own when it no longer fits there (FieldRow).
        var lblOpenHint = new Label
        {
            Text = "Double-click an achievement to see who else has it.",
            AutoSize = false,
            AutoEllipsis = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblOpenHint, () => AppTheme.SmallFont);

        var lblDriver = new Label { Text = "Driver:" };
        _textSize.Wear(lblDriver, () => AppTheme.BoldFont);

        // Where you race - Club or At home - decides which badges are on
        // offer here (AchievementCatalogue.IsOffered). Earned ones always show.
        _cmbWhere.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbWhere.Items.AddRange(new object[] { "Club", "At home" });
        _cmbWhere.SelectedIndexChanged += (_, _) => OnWhereChanged();

        _chkShowOthers.AutoSize = true;
        _chkShowOthers.CheckedChanged += (_, _) => RenderBadges();

        var lblWhere = new Label { Text = "Where you race:" };
        _textSize.Wear(lblWhere, () => AppTheme.BoldFont);

        var pickRow = new FieldRow();

        pickRow.Add(lblDriver, _cmbDriver, DriverWidth);
        pickRow.Add(null, _chkHideLocked);

        if (_settings is not null)
        {
            // (22 Oct 2026, page tidy): as wide as its longer choice too, the
            // way every other picker is (Pieces.PickerWidth) - it was a fixed
            // 130px at the text size in use, measured against nothing.
            pickRow.Add(lblWhere, _cmbWhere, () => Math.Max(UiLayout.AtTextSize(130), Pieces.PickerWidth(_cmbWhere, ref _whereWidth)));
            pickRow.Add(null, _chkShowOthers);
            ShowWhere();
        }

        pickRow.AddLast(null, lblOpenHint, () => lblOpenHint.GetPreferredSize(Size.Empty).Width);
        _pickRow = pickRow;

        // As tall as the three lines it draws, at the text size in use - see
        // ProgressStrip.HeightFor.
        _textSize.Keep(() =>
        {
            var height = ProgressStrip.HeightFor();

            if (_progress.Height != height)
            {
                _progress.Height = height;
            }
        });

        // The wall is taller than the page and scrolls inside this panel -
        // it sizes itself to its content and lets the panel do the
        // scrolling, rather than trying to paint a viewport by hand.
        _scroll.AutoScroll = true;
        _scroll.BackColor = AppTheme.Background;
        _scroll.Controls.Add(_wall);
        _scroll.Resize += (_, _) => LayoutWall();

        _wall.Opened += entry => ShowBadgeDetail(entry.Badge);

        // A wheel notch should move a row, not a pixel.
        _scroll.AutoScrollMargin = new Size(0, 8);

        // The badges are measured in the text size - see BadgeWall - so a
        // new one lays the wall out again, whether or not the panel changed
        // size with it.
        _textSize.Keep(LayoutWall);

        var page = new FillPage();

        page.Add(pickRow, gapBelow: UiLayout.RowGap);
        page.Add(_progress, gapBelow: UiLayout.RowGap);
        page.AddFill(_scroll, _ => UiLayout.AtTextSize(WallLeastHeight), gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>Where you race, as the settings have it - Club on a page built without them.</summary>
    private RacingSetting Where => _settings?.RacingAt ?? RacingSetting.Club;

    /// <summary>The Where combo and the show-the-others tick put to the setting.</summary>
    private void ShowWhere()
    {
        _fillingWhere = true;

        try
        {
            _cmbWhere.SelectedIndex = Where == RacingSetting.Home ? 1 : 0;
        }
        finally
        {
            _fillingWhere = false;
        }

        var others = AchievementCatalogue.All.Count(b => !AchievementCatalogue.IsOffered(b, Where));
        var text = Where == RacingSetting.Home ? $"Show club achievements ({others})" : $"Show home achievements ({others})";

        if (!string.Equals(_chkShowOthers.Text, text, StringComparison.Ordinal))
        {
            _chkShowOthers.Text = text;
        }

        _haveOthers = others > 0;
        _chkShowOthers.Visible = _haveOthers;
    }

    /// <summary>Where you race changed here: saved, and the wall drawn again with what is on offer now.</summary>
    private void OnWhereChanged()
    {
        if (_fillingWhere || _settings is null)
        {
            return;
        }

        var wanted = _cmbWhere.SelectedIndex == 1 ? RacingSetting.Home : RacingSetting.Club;

        if (_settings.RacingAt == wanted)
        {
            return;
        }

        _settings.RacingAt = wanted;
        _settings.Save();

        _chkShowOthers.Checked = false;
        ShowWhere();
        RenderBadges();
        RacingAtChanged?.Invoke();
    }

    /// <summary>The driver picker: as wide as its longest entry, and never under its width at the normal text size.</summary>
    private int DriverWidth() =>
        Math.Max(UiLayout.AtTextSize(DriverPickerWidth), Pieces.PickerWidth(_cmbDriver, ref _driverWidth));

    private void LayoutWall()
    {
        var width = Math.Max(320, _scroll.ClientSize.Width - 4);

        // Where the top of the wall is now - above the panel by however far
        // it is scrolled. Put back at 0,0, a wall laid out again while
        // scrolled (a new text size, a resize) jumped to the top of the view
        // with the scrollbar still part-way down.
        _wall.Location = _scroll.AutoScrollPosition;
        _wall.Width = width;
        _wall.Height = _wall.MeasureContent(width);
    }

    /// <summary>
    /// Reloads from disk and redraws.
    ///
    /// Called on tab entry rather than only at construction, because badges
    /// are awarded by Race Control while this page sits in the background -
    /// a page built once would show yesterday's picture forever.
    /// </summary>
    public void RefreshAll()
    {
        _store = AchievementStore.Load();

        // The setting may have been changed elsewhere (the first-run wizard).
        if (_settings is not null)
        {
            ShowWhere();
        }

        var selected = _cmbDriver.SelectedItem as string;

        _cmbDriver.Items.Clear();

        // Anyone with a profile, plus anyone who has earned something
        // without one - a transponder that raced before it was named still
        // has a record, and hiding it would look like the badges were lost.
        var named = _profiles.All
            .Select(p => Describe(p.Transponder))
            .ToList();

        var unnamed = _store.All
            .Select(d => d.Transponder)
            .Where(t => !_profiles.All.Any(p => string.Equals(p.Transponder, t, StringComparison.OrdinalIgnoreCase)))

            // Ghosts are filtered here as well as in the evaluator, and the
            // reason is that they land in THIS list rather than the named
            // one: a ghost does have a profile, but ProfileStore.All hides
            // it, so "has earned something without a profile" caught every
            // ghost that ever raced.
            //
            // Filtered rather than deleted. Anything a ghost was awarded
            // before this stops being shown, but it stays in the file - the
            // achievements store is somebody's history and it is not for
            // this page to quietly rewrite it.
            .Where(t => !GhostService.IsGhost(t))
            .Select(Describe);

        foreach (var entry in named.Concat(unnamed).Distinct().OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            _cmbDriver.Items.Add(entry);
        }

        // The longest entry may have come or gone: measured again, and the
        // line with it.
        _driverWidth = null;
        _pickRow?.PerformLayout();

        if (_cmbDriver.Items.Count == 0)
        {
            _progress.Apply("No drivers yet", "Achievements appear once somebody has raced.", 0,
                AchievementCatalogue.All.Count(b => AchievementCatalogue.IsOffered(b, Where)), string.Empty);
            _wall.Apply(new List<BadgeWall.Entry>());
            LayoutWall();
            return;
        }

        var index = selected is not null ? _cmbDriver.Items.IndexOf(selected) : -1;
        _cmbDriver.SelectedIndex = index >= 0 ? index : 0;

        RenderBadges();
    }

    /// <summary>
    /// Opens one badge in full: who at the club has it, who got there first,
    /// and how close this driver is.
    ///
    /// Picking somebody out of that list switches the page to them, because
    /// the obvious next question after "three people have this" is "what
    /// else have they got".
    /// </summary>
    private void ShowBadgeDetail(AchievementDefinition badge)
    {
        var transponder = SelectedTransponder();

        if (string.IsNullOrWhiteSpace(transponder))
        {
            return;
        }

        using var form = new AchievementDetailForm(badge, transponder, _store, _profiles);

        if (form.ShowDialog(this) != DialogResult.OK || form.SelectedTransponder is not { } picked)
        {
            return;
        }

        var entry = Describe(picked);
        var index = _cmbDriver.Items.IndexOf(entry);

        if (index >= 0)
        {
            _cmbDriver.SelectedIndex = index;
        }
    }

    /// <summary>"Sam (3F21)" - the name people know, with the id that actually owns the badges.</summary>
    private string Describe(string transponder)
    {
        var profile = _profiles.Get(transponder);

        return string.IsNullOrWhiteSpace(profile?.DriverName)
            ? transponder
            : $"{profile!.DriverName}  ({transponder})";
    }

    /// <summary>The transponder behind the selected entry - badges are keyed by it, not by name.</summary>
    private string? SelectedTransponder()
    {
        if (_cmbDriver.SelectedItem is not string entry)
        {
            return null;
        }

        var open = entry.LastIndexOf('(');
        var close = entry.LastIndexOf(')');

        return open >= 0 && close > open
            ? entry[(open + 1)..close]
            : entry;
    }

    private void RenderBadges()
    {
        var transponder = SelectedTransponder();

        if (string.IsNullOrWhiteSpace(transponder))
        {
            return;
        }

        var driver = _store.For(transponder);
        var entries = new List<BadgeWall.Entry>();
        var showOthers = _settings is null || (_haveOthers && _chkShowOthers.Checked);
        var total = 0;

        foreach (var badge in AchievementCatalogue.All)
        {
            var earned = driver.Unlocked.FirstOrDefault(u => string.Equals(u.Id, badge.Id, StringComparison.Ordinal));

            // On offer where you race, or earned anyway (somewhere else, or
            // before the setting changed): what the count is out of.
            var offered = AchievementCatalogue.IsOffered(badge, Where);

            if (offered || earned is not null)
            {
                total++;
            }

            if (earned is null && (_chkHideLocked.Checked || (!offered && !showOthers)))
            {
                continue;
            }

            entries.Add(new BadgeWall.Entry(badge, earned));
        }

        _wall.Apply(entries);
        LayoutWall();

        var held = driver.Unlocked.Count(u => AchievementCatalogue.Get(u.Id) is not null);

        var name = _cmbDriver.SelectedItem as string ?? transponder;

        // The rarest thing they hold, which is the line somebody actually
        // wants to read about themselves.
        var best = driver.Unlocked
            .Select(u => AchievementCatalogue.Get(u.Id))
            .Where(b => b is not null)
            .OrderByDescending(b => (int)b!.Tier)
            .FirstOrDefault();

        var stats =
            $"{driver.RacesFinished} race{(driver.RacesFinished == 1 ? string.Empty : "s")}   ·   "
            + $"{driver.TotalLaps} lap{(driver.TotalLaps == 1 ? string.Empty : "s")}   ·   "
            + $"{driver.Wins} win{(driver.Wins == 1 ? string.Empty : "s")}   ·   "
            + $"{driver.Podiums} podium{(driver.Podiums == 1 ? string.Empty : "s")}";

        _progress.Apply(name, stats, held, total,
            best is null ? string.Empty : $"Best achievement: {best.Name} ({BadgeArt.TierName(best.Tier)})");
    }

    /// <summary>
    /// The summary bar over the wall: who this is, how far through the set
    /// they are, and the career numbers the milestone badges are counting.
    /// </summary>
    private sealed class ProgressStrip : Control
    {
        /// <summary>Scales a designed-at-100% measurement to the current text size, the same way BadgeWall does.</summary>
        private static int Px(float value) => UiLayout.AtTextSize(value);

        /// <summary>How tall the strip needs to be for the text it is about to draw.</summary>
        public static int HeightFor() =>
            Px(8) + AppTheme.BoldFont.Height + Px(2) + AppTheme.SmallFont.Height + Px(1)
            + AppTheme.SmallFont.Height + Px(10);

        private string _title = string.Empty;
        private string _stats = string.Empty;
        private string _best = string.Empty;
        private int _held;
        private int _total = 1;

        public ProgressStrip()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw, true);

            DoubleBuffered = true;
            TabStop = false;
        }

        public void Apply(string title, string stats, int held, int total, string best)
        {
            _title = title;
            _stats = stats;
            _held = held;
            _total = Math.Max(1, total);
            _best = best;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // Explicitly the theme background rather than the parent's,
            // which is Transparent here - filling with a transparent brush
            // erases nothing and leaves the last frame behind.
            using (var canvas = new SolidBrush(AppTheme.Background))
            {
                g.FillRectangle(canvas, ClientRectangle);
            }

            if (Width < 80 || Height < 30)
            {
                return;
            }

            var bounds = new RectangleF(0, 0, Width, Height);
            AppTheme.DrawCard(g, bounds, AppTheme.Primary);

            using var titleBrush = new SolidBrush(AppTheme.TextDark);
            using var mutedBrush = new SolidBrush(AppTheme.TextMuted);
            using var near = new StringFormat
            {
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter,
                LineAlignment = StringAlignment.Center,
            };

            // EVERY rectangle below is measured from the font that draws
            // into it. They used to be literals - 20px tall at y=6, 18 at
            // 26, 16 at 42, and the meter's caption 20 tall at y=8 - which
            // are the right numbers at one text size and at no other.
            //
            // That is what lost the caption. GDI+ will not part-draw a line
            // into a rectangle too short to hold it, so at a larger text
            // size "N of M earned" simply stopped being drawn, while the
            // bar underneath it - pure geometry, no font involved - carried
            // on. A gradient floating on the right of the card with nothing
            // to say what it counted.
            var pad = Px(16);
            var titleHeight = AppTheme.BoldFont.Height;
            var lineHeight = AppTheme.SmallFont.Height;

            var textWidth = Math.Max(Px(80), Width * 0.45f);

            var y = (float)Px(8);

            g.DrawString(_title, AppTheme.BoldFont, titleBrush, new RectangleF(pad, y, textWidth, titleHeight), near);
            y += titleHeight + Px(2);

            g.DrawString(_stats, AppTheme.SmallFont, mutedBrush, new RectangleF(pad, y, textWidth, lineHeight), near);
            y += lineHeight + Px(1);

            if (!string.IsNullOrEmpty(_best))
            {
                g.DrawString(_best, AppTheme.SmallFont, mutedBrush, new RectangleF(pad, y, textWidth, lineHeight), near);
            }

            // The bar sits on the right half, big enough to read across a
            // room, because "how many have I got" is the whole question
            // this page answers.
            var barWidth = Math.Max(Px(120), Width * 0.42f);
            var barLeft = Width - barWidth - pad;

            using var right = new StringFormat
            {
                Alignment = StringAlignment.Far,
                FormatFlags = StringFormatFlags.NoWrap,
                LineAlignment = StringAlignment.Center,
            };

            // Caption first, then the bar directly under it - so the two
            // read as one thing rather than as a label and a stray stripe.
            var captionTop = (float)Px(8);

            g.DrawString($"{_held} of {_total} earned", AppTheme.BoldFont, titleBrush,
                new RectangleF(barLeft, captionTop, barWidth, titleHeight), right);

            var track = new RectangleF(barLeft, captionTop + titleHeight + Px(6), barWidth, Px(12));
            var radius = Math.Max(3, Px(6));

            using (var trackPath = AppTheme.RoundedRect(track, radius))
            using (var trackFill = new SolidBrush(AppTheme.RowAlt))
            {
                g.FillPath(trackFill, trackPath);
            }

            var fraction = Math.Clamp(_held / (double)_total, 0, 1);

            if (fraction > 0)
            {
                var fill = new RectangleF(track.Left, track.Top,
                    Math.Max(Px(12), (float)(track.Width * fraction)), track.Height);

                using var fillPath = AppTheme.RoundedRect(fill, radius);
                using var brush = new LinearGradientBrush(fill, AppTheme.Primary, AppTheme.Accent, LinearGradientMode.Horizontal);
                g.FillPath(brush, fillPath);
            }
        }
    }

    /// <summary>
    /// The wall of badge cards. Sizes itself to its content so the panel
    /// around it can scroll, groups by category, and lifts whichever card is
    /// under the pointer.
    /// </summary>
    private sealed class BadgeWall : Control
    {
        public sealed record Entry(AchievementDefinition Badge, UnlockedAchievement? Earned);

        private static int Px(float value) => UiLayout.AtTextSize(value);

        private static int Gap => Px(12);
        private static int SidePad => Px(4);
        private static int TileHeight => Px(116);
        private static int PreferredTileWidth => Px(320);
        // Px(34) put the caption almost against the rule under it once
        // the caption stopped being centred and started sitting on a
        // baseline of its own. The band carries a caption and a rule and
        // needs the room for both.
        private static int GroupHeaderHeight => Px(42);

        private readonly List<Entry> _entries = new();
        private readonly List<(RectangleF Bounds, Entry Entry)> _hitBoxes = new();

        private int _hoverIndex = -1;

        /// <summary>A badge the user has double-clicked, to be opened in full.</summary>
        public event Action<Entry>? Opened;

        public BadgeWall()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw, true);

            DoubleBuffered = true;
            TabStop = false;
        }

        public void Apply(IReadOnlyList<Entry> entries)
        {
            _entries.Clear();
            _entries.AddRange(entries);
            _hoverIndex = -1;
            Invalidate();
        }

        private static int ColumnsFor(int width) =>
            Math.Max(1, (width - SidePad * 2 + Gap) / (PreferredTileWidth + Gap));

        /// <summary>
        /// How tall the wall needs to be at a given width - asked by the
        /// page before it sets the height, since the panel can only scroll
        /// something that already knows how big it is.
        /// </summary>
        public int MeasureContent(int width)
        {
            if (_entries.Count == 0)
            {
                return Px(80);
            }

            var columns = ColumnsFor(width);
            var height = Px(4);

            foreach (var group in _entries.GroupBy(e => e.Badge.Category))
            {
                var rows = (int)Math.Ceiling(group.Count() / (double)columns);
                height += GroupHeaderHeight + rows * (TileHeight + Gap);
            }

            return height + Px(12);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            var found = -1;

            for (var i = 0; i < _hitBoxes.Count; i++)
            {
                if (_hitBoxes[i].Bounds.Contains(e.Location))
                {
                    found = i;
                    break;
                }
            }

            if (found == _hoverIndex)
            {
                return;
            }

            _hoverIndex = found;
            Cursor = found >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);

            if (_hoverIndex < 0)
            {
                return;
            }

            _hoverIndex = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            foreach (var (bounds, entry) in _hitBoxes)
            {
                if (bounds.Contains(e.Location))
                {
                    Opened?.Invoke(entry);
                    return;
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (var canvas = new SolidBrush(AppTheme.Background))
            {
                g.FillRectangle(canvas, ClientRectangle);
            }

            _hitBoxes.Clear();

            if (_entries.Count == 0)
            {
                using var brush = new SolidBrush(AppTheme.TextMuted);
                using var centred = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

                g.DrawString("Nothing earned yet - achievements land at the end of a race.", AppTheme.BodyFont, brush,
                    new RectangleF(0, 0, Width, Px(70)), centred);
                return;
            }

            var columns = ColumnsFor(Width);
            var tileWidth = (Width - SidePad * 2 - Gap * (columns - 1)) / (float)columns;
            var y = (float)Px(4);

            foreach (var group in _entries.GroupBy(x => x.Badge.Category))
            {
                DrawGroupHeader(g, group.Key, group.Count(a => a.Earned is not null), group.Count(), y);
                y += GroupHeaderHeight;

                var index = 0;

                foreach (var entry in group)
                {
                    var column = index % columns;
                    var bounds = new RectangleF(
                        SidePad + column * (tileWidth + Gap),
                        y + index / columns * (TileHeight + Gap),
                        tileWidth,
                        TileHeight);

                    _hitBoxes.Add((bounds, entry));
                    DrawTile(g, bounds, entry, _hitBoxes.Count - 1 == _hoverIndex);

                    index++;
                }

                var rows = (int)Math.Ceiling(group.Count() / (double)columns);
                y += rows * (TileHeight + Gap);
            }
        }

        private void DrawGroupHeader(Graphics g, AchievementCategory category, int earned, int total, float y)
        {
            var colour = BadgeArt.CategoryColour(category);

            using var brush = new SolidBrush(colour);
            using var mutedBrush = new SolidBrush(AppTheme.TextMuted);
            using var flat = new StringFormat { FormatFlags = StringFormatFlags.NoWrap };

            var label = AppTheme.Spaced(BadgeArt.CategoryName(category).ToUpperInvariant());

            // Set out from where the capitals sit, rather than centred in a
            // band.
            //
            // Centring places the font's whole line box - the room it keeps
            // above for accents and below for descenders included. An
            // all-capitals caption fills neither, so its letters rode high
            // in the band: a wide gap above them, a narrow one down to the
            // rule, and a colour bar and a count that each sat on their own
            // idea of the middle. Everything here hangs off one baseline
            // instead, set a deliberate distance down the band.
            var capHeight = CapHeight(g, AppTheme.MicroFont);
            var baseline = y + Px(16) + capHeight;

            // A short colour bar ahead of the caption, the same device the
            // cards use down their left edge - exactly as tall as the
            // capitals beside it, so Ctrl +/- keeps it right rather than
            // stretching a fixed fourteen pixels past them.
            using (var bar = new SolidBrush(colour))
            {
                g.FillRectangle(bar, SidePad, baseline - capHeight, Px(3), capHeight);
            }

            var measured = g.MeasureString(label, AppTheme.MicroFont);

            g.DrawString(label, AppTheme.MicroFont, brush,
                new RectangleF(
                    SidePad + Px(10),
                    baseline - BaselineOffset(g, AppTheme.MicroFont),
                    measured.Width + Px(4),
                    AppTheme.MicroFont.GetHeight(g)),
                flat);

            // Dropped onto the caption's baseline rather than centred on
            // its own, so the two read as one line however far apart their
            // sizes are.
            g.DrawString($"{earned} of {total}", AppTheme.SmallFont, mutedBrush,
                new RectangleF(
                    SidePad + Px(14) + measured.Width,
                    baseline - BaselineOffset(g, AppTheme.SmallFont),
                    Px(120),
                    AppTheme.SmallFont.GetHeight(g)),
                flat);

            using var rule = new Pen(AppTheme.Border);
            var ruleY = baseline + Px(11);
            g.DrawLine(rule, SidePad, ruleY, Width - SidePad, ruleY);
        }

        /// <summary>How far below the top of a line of this font its baseline falls.</summary>
        private static float BaselineOffset(Graphics g, Font font)
        {
            var family = font.FontFamily;
            var style = font.Style;
            var spacing = family.GetLineSpacing(style);

            // A family that reports nothing usable - four fifths down is
            // where a baseline normally lands, and it is only ever a
            // fallback for a font that cannot answer.
            return spacing <= 0
                ? font.GetHeight(g) * 0.8f
                : font.GetHeight(g) * family.GetCellAscent(style) / spacing;
        }

        /// <summary>
        /// The height of this font's capitals, in pixels.
        ///
        /// No font tells GDI+ its cap height, so it is taken as a share of
        /// the em. That is close enough to stand a bar beside the letters
        /// and to hang a row off them, and it follows the text size rather
        /// than being a fixed count of pixels that only looks right at one
        /// setting.
        /// </summary>
        private static float CapHeight(Graphics g, Font font)
        {
            var family = font.FontFamily;
            var style = font.Style;
            var lineHeight = font.GetHeight(g);
            var spacing = family.GetLineSpacing(style);
            var emPixels = spacing <= 0 ? lineHeight : lineHeight * family.GetEmHeight(style) / spacing;

            return Math.Max(1f, emPixels * 0.7f);
        }

        private static void DrawTile(Graphics g, RectangleF bounds, Entry entry, bool hovered)
        {
            var badge = entry.Badge;
            var earned = entry.Earned is not null;
            var accent = earned ? BadgeArt.TierInk(badge.Tier) : AppTheme.Border;

            AppTheme.DrawCard(g, bounds, accent);

            if (hovered)
            {
                // Lifted rather than recoloured: the tier colour is doing a
                // job already, and a second highlight colour on top of it
                // would fight with it.
                using var glow = new Pen(earned ? accent : AppTheme.Primary, 1.6f);
                using var path = AppTheme.RoundedRect(RectangleF.Inflate(bounds, -1f, -1f), AppTheme.CornerRadius);
                g.DrawPath(glow, path);
            }

            var pad = Px(12);
            var medallion = Px(64);
            var medallionBox = new RectangleF(bounds.Left + pad, bounds.Top + (bounds.Height - medallion) / 2f, medallion, medallion);

            BadgeArt.DrawMedallion(g, medallionBox, badge, earned);

            var textLeft = medallionBox.Right + pad;
            var textWidth = Math.Max(40f, bounds.Right - pad - textLeft);

            using var nameBrush = new SolidBrush(earned ? AppTheme.TextDark : AppTheme.TextMuted);
            using var mutedBrush = new SolidBrush(AppTheme.TextMuted);
            using var tierBrush = new SolidBrush(accent);
            using var near = new StringFormat
            {
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter,
                LineAlignment = StringAlignment.Center,
            };

            using var nameFont = FontLibrary.Create(AppTheme.DisplayFamily,
                Math.Max(5f, 12.5f * AppTheme.FontScale), FontStyle.Bold);

            g.DrawString(badge.Name, nameFont, nameBrush,
                new RectangleF(textLeft, bounds.Top + Px(10), textWidth, Px(22)), near);

            // Description, wrapped to the card rather than cut off at the
            // first line - the rule IS the badge for a locked one.
            var descriptionRect = Rectangle.Round(new RectangleF(textLeft, bounds.Top + Px(32), textWidth, Px(38)));

            TextRenderer.DrawText(g, badge.Description, AppTheme.SmallFont, descriptionRect,
                earned ? AppTheme.TextMuted : AppTheme.TextMuted,
                TextFormatFlags.WordBreak | TextFormatFlags.Top | TextFormatFlags.Left
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            // Bottom line: the tier always, and what it was worth when it
            // was earned - the date and the number are half the point of
            // having earned it.
            var footTop = bounds.Bottom - Px(26);

            g.DrawString(AppTheme.Spaced(BadgeArt.TierName(badge.Tier).ToUpperInvariant()), AppTheme.MicroFont, tierBrush,
                new RectangleF(textLeft, footTop, Px(86), Px(18)), near);

            var foot = entry.Earned is { } unlocked
                ? unlocked.UnlockedUtc.ToLocalTime().ToString("d MMM yyyy")
                  + (string.IsNullOrWhiteSpace(unlocked.Detail) ? string.Empty : $"   ·   {unlocked.Detail}")
                : "Not earned yet";

            g.DrawString(foot, AppTheme.SmallFont, mutedBrush,
                new RectangleF(textLeft + Px(88), footTop, Math.Max(20f, textWidth - Px(88)), Px(18)), near);
        }
    }
}
