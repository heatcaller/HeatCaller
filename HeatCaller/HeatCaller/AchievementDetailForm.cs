using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace HeatCaller;

/// <summary>
/// Everything about one badge: what it takes, who at the club has it, who
/// got there first, and how close this driver is to it.
///
/// A badge on its own is a line of text. What makes one worth chasing is
/// knowing that three people have it and one of them got it in April - so
/// this is mostly other people's names, deliberately.
///
/// On the layout pieces (DialogFit): the header across the top and the
/// holders taking the height under it, both edge to edge as they always
/// were - each paints its own background - and the hint and Close along the
/// foot, in from the window's edges by the page's margins. The header and
/// the holders are drawn here, each measurement in them chosen at the normal
/// text size and drawn at the text size in use (Px), so they keep their
/// proportions at every size - at fixed pixels the badge's name was cut off
/// from 150%, and the chips and rows were too small for their text. It used
/// to be placed in pixels: the header and a 58px footer docked to a 760x640
/// window, the hint at a fixed place in the footer and Close a fixed 100x34
/// in from its right-hand edge.
///
/// Built at the text size in use when it opens, as every dialog is (see
/// DialogFit): it is modal, and the text size is changed from the main
/// window, so it cannot change while this is up.
/// </summary>
public sealed class AchievementDetailForm : Form
{
    /// <summary>
    /// The header's height, at the text size in use - everything drawn in it
    /// is (see Header.OnPaint), and its last line is placed from the bottom,
    /// so the two stay in step. The page is given it as the header's own
    /// height, which it lays the header out at.
    /// </summary>
    private static int HeaderHeight => Px(214);

    /// <summary>A measurement chosen at the normal text size, at the text size in use - the header's and the holders', as LapDetailPopup and the badge page do theirs.</summary>
    private static int Px(float value) => UiLayout.AtTextSize(value);

    /// <summary>The holders show at least this many before the window scrolls instead.</summary>
    private const int LeastRows = 4;

    private readonly AchievementDefinition _badge;
    private readonly string _transponder;
    private readonly AchievementStore _store;
    private readonly ProfileStore _profiles;

    private readonly List<Holder> _holders = new();
    private readonly HolderList _list;

    /// <summary>A driver the user picked from the holders list, so the page behind can jump to them.</summary>
    public string? SelectedTransponder { get; private set; }

    /// <summary>One driver who has earned this badge.</summary>
    public sealed record Holder(string Transponder, string Name, DateTime EarnedUtc, string Detail, string? SessionId)
    {
        public bool IsFirst { get; set; }
    }

    public AchievementDetailForm(AchievementDefinition badge, string transponder, AchievementStore store, ProfileStore profiles)
    {
        _badge = badge;
        _transponder = transponder;
        _store = store;
        _profiles = profiles;

        BuildHolders();

        Text = badge.Name;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var header = new Header(this) { Height = HeaderHeight };

        _list = new HolderList(_holders, _transponder);
        _list.Picked += driver =>
        {
            SelectedTransponder = driver;
            DialogResult = DialogResult.OK;
            Close();
        };

        // With nobody to double-click, nothing: that nobody has it is said
        // under "Who has it", where the list would be. Said here as well, and
        // on the line above that, it was the same thing three times (layout
        // pictures, 30 Sep).
        var hint = new Label
        {
            Text = _holders.Count > 0 ? "Double-click somebody to see their achievements." : string.Empty,
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        var close = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.Cancel,
        };

        // No margins round the page: the header and the holders run to the
        // window's edges, as they did docked, and draw their own insets. Only
        // the foot is in from the edges - the page's usual margins, on the
        // row itself - with the page's usual margin under it.
        var page = new FillPage { Padding = new Padding(0, 0, 0, UiLayout.PageTop) };
        page.Add(header, gapBelow: 0);
        page.AddFill(_list, _ => HolderList.LeastHeight(LeastRows), gapBelow: UiLayout.RowGap);

        // The hint on the left and Close against the right-hand edge, on one
        // line - or Close under the hint when a narrow window has no room
        // for both (SplitBar).
        var footer = page.Add(
            new SplitBar(new ButtonBar().With(hint), new ButtonBar(alignRight: true).With(close)),
            gapBelow: 0);

        footer.Margin = new Padding(UiLayout.PageLeft, 0, UiLayout.PageRight, 0);

        Controls.Add(page);

        CancelButton = close;
        AcceptButton = close;

        AppTheme.ApplyRecursively(this);

        // The size it had. It can be made bigger, or shorter down to the
        // header, a few of the holders and the foot.
        DialogFit.Keep(this, page, width: 760, height: 640);
    }

    /// <summary>Everyone at the club with this badge, oldest first.</summary>
    private void BuildHolders()
    {
        foreach (var driver in _store.All)
        {
            var unlock = driver.Unlocked.FirstOrDefault(u => string.Equals(u.Id, _badge.Id, StringComparison.Ordinal));

            // A ghost's old awards stay in the file but are not shown - the
            // same rule the badge page's driver list follows. Listed here, a
            // ghost could be "first to earn it" ahead of every real driver.
            if (unlock is null || GhostService.IsGhost(driver.Transponder))
            {
                continue;
            }

            var profile = _profiles.Get(driver.Transponder);

            _holders.Add(new Holder(
                driver.Transponder,
                string.IsNullOrWhiteSpace(profile?.DriverName) ? driver.Transponder : profile!.DriverName,
                unlock.UnlockedUtc,
                unlock.Detail,
                unlock.SessionId));
        }

        _holders.Sort((a, b) => a.EarnedUtc.CompareTo(b.EarnedUtc));

        if (_holders.Count > 0)
        {
            _holders[0].IsFirst = true;
        }
    }

    /// <summary>How many drivers this club has any record for - the denominator for "how rare is it".</summary>
    private int FieldSize()
    {
        var known = _store.All.Count(d => (d.RacesFinished > 0 || d.Unlocked.Count > 0) && !GhostService.IsGhost(d.Transponder));
        return Math.Max(known, _holders.Count);
    }

    private static string RarityWord(int holders, int field)
    {
        if (holders == 0)
        {
            return "Nobody has this yet";
        }

        var share = field <= 0 ? 1.0 : holders / (double)field;

        return share switch
        {
            >= 0.6 => "Most of the club has this",
            >= 0.3 => "A few people have this",
            >= 0.1 => "Rare",
            _ => "Very rare",
        };
    }

    /// <summary>
    /// The top of the window: the medallion at a size worth looking at, what
    /// it takes to earn, how rare it is, and - if this driver hasn't got it -
    /// how far off they are.
    /// </summary>
    private sealed class Header : Control
    {
        private readonly AchievementDetailForm _owner;

        public Header(AchievementDetailForm owner)
        {
            _owner = owner;

            SetStyle(ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw, true);

            DoubleBuffered = true;
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (var background = new SolidBrush(AppTheme.Surface))
            {
                g.FillRectangle(background, ClientRectangle);
            }

            var badge = _owner._badge;
            var mine = _owner._holders.FirstOrDefault(h =>
                string.Equals(h.Transponder, _owner._transponder, StringComparison.OrdinalIgnoreCase));

            var earned = mine is not null;

            // Every measurement below is at the text size in use (Px), so the
            // header keeps its proportions as its text grows - the name, the
            // chips' captions and the lines under them are all in faces that
            // follow the text size.
            var pad = Px(24);

            // Medallion, big. This is the one place the artwork gets to be
            // the size it deserves.
            var medallion = new RectangleF(pad, pad, Px(104), Px(104));
            BadgeArt.DrawMedallion(g, medallion, badge, earned);

            var left = medallion.Right + pad;
            var right = Width - pad;
            var width = Math.Max(Px(80), right - left);

            using var titleBrush = new SolidBrush(AppTheme.TextDark);
            using var mutedBrush = new SolidBrush(AppTheme.TextMuted);
            using var near = new StringFormat
            {
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter,
                LineAlignment = StringAlignment.Center,
            };

            using var nameFont = FontLibrary.Create(AppTheme.DisplayFamily,
                Math.Max(6f, 19f * AppTheme.FontScale), FontStyle.Bold);

            // The name's box at the text size in use, as its face is: a fixed
            // 32px box was shorter than a line of it from 150%, which cut the
            // name off.
            g.DrawString(badge.Name, nameFont, titleBrush, new RectangleF(left, Px(22), width, Px(32)), near);

            // Tier and group, as chips.
            float chipY = Px(58);
            float tierWidth = Px(86);
            float chipHeight = Px(24);

            AppTheme.DrawPill(g, new RectangleF(left, chipY, tierWidth, chipHeight),
                BadgeArt.TierInk(badge.Tier), BadgeArt.TierName(badge.Tier), Color.White);

            float groupWidth = Px(130);

            // The group chip is filled with a look colour (Warning, Success,
            // Accent or Primary), so its text is that colour's own "on"
            // colour - white on an amber or a pale green is unreadable. The
            // tier chip above keeps white: tier inks are fixed medal colours,
            // not the look's.
            var groupInk = badge.Category switch
            {
                AchievementCategory.Milestone => AppTheme.OnWarning,
                AchievementCategory.Progress => AppTheme.OnSuccess,
                AchievementCategory.Rivalry => AppTheme.OnAccent,
                _ => AppTheme.OnPrimary,
            };

            AppTheme.DrawPill(g, new RectangleF(left + tierWidth + Px(8), chipY, groupWidth, chipHeight),
                BadgeArt.CategoryColour(badge.Category), BadgeArt.CategoryName(badge.Category), groupInk);

            // What it takes.
            TextRenderer.DrawText(g, badge.Description, AppTheme.BodyFont,
                Rectangle.Round(new RectangleF(left, chipY + Px(34), width, Px(44))),
                AppTheme.TextDark,
                TextFormatFlags.WordBreak | TextFormatFlags.Top | TextFormatFlags.Left | TextFormatFlags.NoPrefix);

            // How this driver stands.
            float statusY = Px(136);
            float lineHeight = Px(22);

            if (earned)
            {
                var when = mine!.EarnedUtc.ToLocalTime().ToString("d MMM yyyy");

                var text = string.IsNullOrWhiteSpace(mine.Detail)
                    ? $"Earned {when}"
                    : $"Earned {when} - {mine.Detail}";

                if (_owner.SessionOf(mine) is { } session)
                {
                    text += $", at {session}";
                }

                using var got = new SolidBrush(AppTheme.Success);
                g.DrawString(text, AppTheme.BoldFont, got, new RectangleF(pad, statusY, Width - Px(48), lineHeight), near);
            }
            else if (AchievementProgress.For(badge.Id, _owner._store.For(_owner._transponder)) is { } progress)
            {
                // Not yet - but here is exactly how far off.
                var fraction = Math.Clamp(progress.Current / (double)Math.Max(1, progress.Target), 0, 1);

                g.DrawString($"{progress.Current} of {progress.Target} {progress.Noun}", AppTheme.BoldFont, titleBrush,
                    new RectangleF(pad, statusY, Px(260), lineHeight), near);

                var track = new RectangleF(pad, statusY + Px(26), Math.Max(Px(120), Width - Px(48)), Px(10));
                var radius = Px(5);

                using (var trackPath = AppTheme.RoundedRect(track, radius))
                using (var trackFill = new SolidBrush(AppTheme.RowAlt))
                {
                    g.FillPath(trackFill, trackPath);
                }

                if (fraction > 0)
                {
                    var fill = new RectangleF(track.Left, track.Top, Math.Max(Px(10), (float)(track.Width * fraction)), track.Height);

                    using var fillPath = AppTheme.RoundedRect(fill, radius);
                    using var brush = new LinearGradientBrush(fill, AppTheme.Primary, AppTheme.Accent, LinearGradientMode.Horizontal);
                    g.FillPath(brush, fillPath);
                }
            }
            else
            {
                g.DrawString("Not earned yet.", AppTheme.BoldFont, mutedBrush,
                    new RectangleF(pad, statusY, Width - Px(48), lineHeight), near);
            }

            // Rarity, and who got there first.
            var field = _owner.FieldSize();
            var holders = _owner._holders.Count;

            // With nobody holding it, the count alone: "Who has it" under
            // this says that nobody does, and what that means.
            var count = $"{holders} of {field} driver{(field == 1 ? string.Empty : "s")}";
            var rarity = holders == 0 ? count : $"{RarityWord(holders, field)}   ·   {count}";

            if (_owner._holders.FirstOrDefault() is { } first)
            {
                rarity += $"   ·   first was {first.Name}, {first.EarnedUtc.ToLocalTime():d MMM yyyy}";
            }

            g.DrawString(rarity, AppTheme.SmallFont, mutedBrush,
                new RectangleF(pad, Height - Px(34), Width - Px(48), Px(20)), near);

            using var rule = new Pen(AppTheme.Border);
            g.DrawLine(rule, 0, Height - 1, Width, Height - 1);
        }
    }

    /// <summary>The race a badge was earned in, described, or null when it can't be traced.</summary>
    private string? SessionOf(Holder holder)
    {
        if (string.IsNullOrWhiteSpace(holder.SessionId))
        {
            return null;
        }

        _history ??= SessionHistoryStore.Load();

        var record = _history.Get(holder.SessionId!);

        if (record is null)
        {
            return null;
        }

        var label = string.IsNullOrWhiteSpace(record.Label) ? "a race" : record.Label;

        return string.IsNullOrWhiteSpace(record.TrackLayoutName)
            ? label
            : $"{label} on {record.TrackLayoutName}";
    }

    private SessionHistoryStore? _history;

    /// <summary>
    /// Who has it, oldest first - the club's roll of honour for this badge.
    /// </summary>
    private sealed class HolderList : Control
    {
        /// <summary>A holder's row, at the text size in use - it carries a line of the bold face and one of the small face, which grow with it.</summary>
        private static int RowHeight => Px(46);

        /// <summary>Where the first row starts, under "WHO HAS IT", at the text size in use as the heading is.</summary>
        private static int FirstRowTop => Px(34);

        private readonly List<Holder> _holders;
        private readonly string _mine;

        private int _hover = -1;
        private int _scroll;

        public event Action<string>? Picked;

        /// <summary>
        /// How tall the list has to be to show its heading and
        /// <paramref name="rows"/> holders whole: the first row starts
        /// FirstRowTop down, under "WHO HAS IT" (see RowAt and OnPaint). At
        /// the text size in use, as the rows are - asked by the window's page
        /// whenever it works out its least height.
        /// </summary>
        public static int LeastHeight(int rows) => FirstRowTop + rows * RowHeight;

        public HolderList(List<Holder> holders, string mine)
        {
            _holders = holders;
            _mine = mine;

            SetStyle(ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw, true);

            DoubleBuffered = true;
            TabStop = false;
        }

        private int RowAt(Point point)
        {
            var index = (point.Y - FirstRowTop + _scroll) / RowHeight;
            return index >= 0 && index < _holders.Count ? index : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            var row = RowAt(e.Location);

            if (row == _hover)
            {
                return;
            }

            _hover = row;
            Cursor = row >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            var row = RowAt(e.Location);

            if (row >= 0)
            {
                Picked?.Invoke(_holders[row].Transponder);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            var content = _holders.Count * RowHeight + FirstRowTop;
            var most = Math.Max(0, content - Height);

            // A notch moves the same share of a row at every text size.
            _scroll = Math.Clamp(_scroll - UiLayout.AtTextSize(e.Delta / 2), 0, most);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (var background = new SolidBrush(AppTheme.Background))
            {
                g.FillRectangle(background, ClientRectangle);
            }

            using var mutedBrush = new SolidBrush(AppTheme.TextMuted);
            using var near = new StringFormat
            {
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter,
                LineAlignment = StringAlignment.Center,
            };

            // The heading, the empty-list line and the rows' band at the text
            // size in use, as their faces and the rows are.
            var pad = Px(24);

            g.DrawString(AppTheme.Spaced("WHO HAS IT"), AppTheme.MicroFont, mutedBrush,
                new RectangleF(pad, Px(8), Px(300), Px(20)), near);

            if (_holders.Count == 0)
            {
                g.DrawString("Nobody at the club has earned this one yet - it's there to be taken.",
                    AppTheme.BodyFont, mutedBrush, new RectangleF(pad, Px(44), Width - Px(48), Px(24)), near);
                return;
            }

            var clip = g.Save();
            var bandTop = Px(30);
            g.SetClip(new RectangleF(0, bandTop, Width, Height - bandTop));

            var rowHeight = RowHeight;
            var firstRowTop = FirstRowTop;
            var hiddenAbove = Px(24);

            for (var i = 0; i < _holders.Count; i++)
            {
                var top = firstRowTop + i * rowHeight - _scroll;

                if (top > Height || top + rowHeight < hiddenAbove)
                {
                    continue;
                }

                DrawRow(g, _holders[i], i, top);
            }

            g.Restore(clip);
        }

        private void DrawRow(Graphics g, Holder holder, int index, float top)
        {
            var isMine = string.Equals(holder.Transponder, _mine, StringComparison.OrdinalIgnoreCase);

            // The row's card and everything in it at the text size in use, as
            // the lines in it are. The card's rounding stays the app's one
            // (AppTheme.CornerRadius, which DrawCard uses), so the hover wash
            // keeps it too.
            var bounds = new RectangleF(Px(16), top, Width - Px(32), RowHeight - Px(6));

            AppTheme.DrawCard(g, bounds, isMine ? AppTheme.Primary : null);

            if (index == _hover)
            {
                using var wash = new SolidBrush(Color.FromArgb(26, AppTheme.Primary));
                using var path = AppTheme.RoundedRect(bounds, AppTheme.CornerRadius);
                g.FillPath(wash, path);
            }

            using var nameBrush = new SolidBrush(AppTheme.TextDark);
            using var mutedBrush = new SolidBrush(AppTheme.TextMuted);
            using var near = new StringFormat
            {
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter,
                LineAlignment = StringAlignment.Center,
            };
            using var far = new StringFormat
            {
                Alignment = StringAlignment.Far,
                FormatFlags = StringFormatFlags.NoWrap,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
            };

            var left = bounds.Left + Px(14);

            // Two lines on each side: the upper in a line of the bold or body
            // face, the lower in a line of the small face.
            float upperTop = bounds.Top + Px(4);
            float upperHeight = Px(20);
            float lowerTop = bounds.Top + Px(21);
            float lowerHeight = Px(18);

            // Whoever got there first gets to be marked as such, for as long
            // as the club keeps records - on the name's line, beside it. It
            // was centred on the row, lower than the name it marks (layout
            // pictures, 30 Sep).
            if (holder.IsFirst)
            {
                AppTheme.DrawPill(g, new RectangleF(left, upperTop, Px(52), upperHeight),
                    AppTheme.Warning, "FIRST", AppTheme.OnWarning);

                left += Px(62);
            }

            var name = isMine ? holder.Name + "  (this driver)" : holder.Name;

            g.DrawString(name, AppTheme.BoldFont, nameBrush,
                new RectangleF(left, upperTop, bounds.Width * 0.45f, upperHeight), near);

            if (!string.IsNullOrWhiteSpace(holder.Detail))
            {
                g.DrawString(holder.Detail, AppTheme.SmallFont, mutedBrush,
                    new RectangleF(left, lowerTop, bounds.Width * 0.45f, lowerHeight), near);
            }

            float dateLeft = bounds.Right - Px(190);
            float dateWidth = Px(176);

            g.DrawString(holder.EarnedUtc.ToLocalTime().ToString("d MMM yyyy"), AppTheme.BodyFont, mutedBrush,
                new RectangleF(dateLeft, upperTop, dateWidth, upperHeight), far);

            g.DrawString(Ago(holder.EarnedUtc), AppTheme.SmallFont, mutedBrush,
                new RectangleF(dateLeft, lowerTop, dateWidth, lowerHeight), far);
        }

        private static string Ago(DateTime utc)
        {
            var days = (int)(DateTime.UtcNow - utc).TotalDays;

            return days switch
            {
                <= 0 => "today",
                1 => "yesterday",
                < 14 => $"{days} days ago",
                < 60 => $"{days / 7} weeks ago",
                < 730 => $"{Math.Max(1, days / 30)} months ago",
                _ => $"{days / 365} years ago",
            };
        }
    }
}

/// <summary>
/// How far along a counting badge somebody is.
///
/// Only the ones with a number behind them - "ten races", "a thousand
/// laps". A badge like Wire to Wire either happens in a race or it doesn't,
/// and inventing a percentage for it would be worse than saying nothing.
/// </summary>
public static class AchievementProgress
{
    public readonly record struct Step(int Current, int Target, string Noun);

    public static Step? For(string id, DriverAchievements driver) => id switch
    {
        AchievementCatalogue.FirstRace => new Step(driver.RacesFinished, 1, "races"),
        AchievementCatalogue.TenRaces => new Step(driver.RacesFinished, 10, "races"),
        AchievementCatalogue.FiftyRaces => new Step(driver.RacesFinished, 50, "races"),
        AchievementCatalogue.HundredRaces => new Step(driver.RacesFinished, 100, "races"),

        AchievementCatalogue.ThousandLaps => new Step(driver.TotalLaps, 1000, "laps"),
        AchievementCatalogue.FiveThousandLaps => new Step(driver.TotalLaps, 5000, "laps"),

        AchievementCatalogue.FirstWin => new Step(driver.Wins, 1, "wins"),
        AchievementCatalogue.TenWins => new Step(driver.Wins, 10, "wins"),
        AchievementCatalogue.TwentyFiveWins => new Step(driver.Wins, 25, "wins"),

        AchievementCatalogue.FirstPodium => new Step(driver.Podiums, 1, "podiums"),
        AchievementCatalogue.PodiumRegular => new Step(driver.Podiums, 3, "podiums"),
        AchievementCatalogue.TenPodiums => new Step(driver.Podiums, 10, "podiums"),

        AchievementCatalogue.ThreeTrackRecords => new Step(driver.TrackRecords.Count, 3, "track records"),

        _ => null,
    };
}
