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
/// On the layout pieces (DialogFit): the header as a card across the top and
/// the holders in a "Who has it" card taking the height under it (18 Nov
/// 2026, dialog jazz-up - they used to run edge to edge), and the hint and
/// Close along the foot. The header and
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
    private static int HeaderHeight => Px(196);

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

        // A header card over a card of the holders (18 Nov 2026, dialog
        // jazz-up), in from the window's edges by the page's margins as every
        // other dialog is. They used to run edge to edge, the header a band
        // with a rule under it and the holders on the page's colour under a
        // "WHO HAS IT" of their own; the heading is the card's caption now.
        var holdersCard = new Section("Who has it");
        holdersCard.AddFill(_list, () => HolderList.LeastHeight(LeastRows), gapBelow: 0);

        var page = new FillPage();
        page.Add(header, gapBelow: UiLayout.RowGap);
        page.AddFill(holdersCard, holdersCard.LeastHeight, gapBelow: UiLayout.RowGap);

        // The hint on the left and Close against the right-hand edge, on one
        // line - or Close under the hint when a narrow window has no room
        // for both (SplitBar).
        page.Add(
            new SplitBar(new ButtonBar().With(hint), new ButtonBar(alignRight: true).With(close)),
            gapBelow: 0);

        Controls.Add(page);

        CancelButton = close;
        AcceptButton = close;

        AppTheme.ApplyRecursively(this);

        // The width it had, and 40px taller than its 640 so the holders keep
        // the room the card's caption and insets take - wherever the screen
        // has it. It can be made bigger, or shorter down to the header, a few
        // of the holders and the foot.
        DialogFit.Keep(this, page, width: 760, height: 680);
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

            // A card on the page, as the cards under it are: the page's
            // colour in the corners.
            using (var background = new SolidBrush(Parent?.BackColor ?? AppTheme.Background))
            {
                g.FillRectangle(background, ClientRectangle);
            }

            AppTheme.DrawCard(g, new RectangleF(0, 0, Width, Height));

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
            var medallion = new RectangleF(pad, Px(20), Px(96), Px(96));
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
            g.DrawString(badge.Name, nameFont, titleBrush, new RectangleF(left, Px(16), width, Px(32)), near);

            // Tier and group, as chips.
            float chipY = Px(52);
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
                Rectangle.Round(new RectangleF(left, chipY + Px(32), width, Px(42))),
                AppTheme.TextDark,
                TextFormatFlags.WordBreak | TextFormatFlags.Top | TextFormatFlags.Left | TextFormatFlags.NoPrefix);

            // How this driver stands.
            float statusY = Px(128);
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

                var track = new RectangleF(pad, statusY + Px(24), Math.Max(Px(120), Width - Px(48)), Px(10));
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
                new RectangleF(pad, Height - Px(30), Width - Px(48), Px(20)), near);
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
    /// The words under a holder's date: how long ago they earned it.
    ///
    /// WHY (23 Nov 2026, bug hunt 24): counted in club nights (6am to 6am,
    /// TonightAtTheTrack.NightOf), as PastMeetings.HowLongAgo is. It used to
    /// count whole 24-hour lumps, so a badge earned at 9pm read "today" at
    /// 10am the next morning - under yesterday's date - and one from two
    /// nights back read "yesterday".
    /// </summary>
    internal static string HolderAgo(DateTime earnedUtc, DateTime nowUtc, TimeZoneInfo? zone = null)
    {
        var days = (TonightAtTheTrack.NightOf(LocalOf(nowUtc, zone)) - TonightAtTheTrack.NightOf(LocalOf(earnedUtc, zone))).Days;

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

    private static DateTime LocalOf(DateTime utc, TimeZoneInfo? zone) =>
        zone is null
            ? DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime()
            : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    /// <summary>
    /// How far the holder list can be scrolled: never above the first row,
    /// never so far that there is empty room under the last one.
    /// </summary>
    internal static int ClampHolderScroll(int scroll, int rows, int rowHeight, int firstRowTop, int height) =>
        Math.Clamp(scroll, 0, Math.Max(0, firstRowTop + rows * rowHeight - height));

    /// <summary>
    /// Where the holder list scrolls to on a key: a row for Up and Down, a
    /// screenful for Page Up and Page Down, the ends for Home and End. Null
    /// for any other key.
    ///
    /// WHY (23 Nov 2026, bug hunt 24): the list only moved on the mouse
    /// wheel, so holders below the first screenful could not be reached
    /// from the keyboard, or with a mouse that has no wheel.
    /// </summary>
    internal static int? HolderScrollForKey(Keys key, int scroll, int rows, int rowHeight, int firstRowTop, int height)
    {
        var page = Math.Max(rowHeight, height - rowHeight);

        int? wanted = key switch
        {
            Keys.Down => scroll + rowHeight,
            Keys.Up => scroll - rowHeight,
            Keys.PageDown => scroll + page,
            Keys.PageUp => scroll - page,
            Keys.Home => 0,
            Keys.End => int.MaxValue / 2,
            _ => null,
        };

        return wanted is { } to ? ClampHolderScroll(to, rows, rowHeight, firstRowTop, height) : null;
    }

    /// <summary>
    /// Who has it, oldest first - the club's roll of honour for this badge.
    /// </summary>
    private sealed class HolderList : Control
    {
        /// <summary>A holder's row, at the text size in use - it carries a line of the bold face and one of the small face, which grow with it.</summary>
        private static int RowHeight => Px(46);

        /// <summary>
        /// Where the first row starts: at the top, now the heading over the
        /// rows is the caption of the card the list is in (18 Nov 2026,
        /// dialog jazz-up). It used to be Px(34) down, under a "WHO HAS IT"
        /// the list drew itself.
        /// </summary>
        private static int FirstRowTop => 0;

        private readonly List<Holder> _holders;
        private readonly string _mine;

        private int _hover = -1;
        private int _scroll;

        public event Action<string>? Picked;

        /// <summary>
        /// How tall the list has to be to show <paramref name="rows"/>
        /// holders whole: the first row starts FirstRowTop down (see RowAt
        /// and OnPaint). At
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
                     | ControlStyles.ResizeRedraw
                     | ControlStyles.Selectable, true);

            DoubleBuffered = true;

            // WHY (23 Nov 2026, bug hunt 24): the list can now take the focus
            // (Tab, or a click on it) so the arrow and page keys scroll it.
            // It was TabStop = false and moved only on the mouse wheel, so
            // holders past the first screenful could not be reached without
            // one.
            TabStop = true;
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End
            || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            var to = HolderScrollForKey(e.KeyData, _scroll, _holders.Count, RowHeight, FirstRowTop, Height);

            if (to is { } scroll)
            {
                _scroll = scroll;
                e.Handled = true;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            // The base constructor can size the control before the holders
            // are set.
            if (_holders is null)
            {
                return;
            }

            // WHY (23 Nov 2026, bug hunt 24): the scroll was only kept in
            // range when the wheel turned, so after scrolling to the end and
            // making the window taller the rows stayed pushed up with an
            // empty gap under the last one.
            _scroll = ClampHolderScroll(_scroll, _holders.Count, RowHeight, FirstRowTop, Height);
            Invalidate();
        }

        private int RowAt(Point point)
        {
            // WHY (20 Oct 2026): C# integer division rounds toward zero, so a
            // point a little above the first row came out as row 0 - the
            // first holder lit up and a double-click there picked them. Now
            // anything above the list, or above the first row, is no row at
            // all.
            if (point.Y < 0)
            {
                return -1;
            }

            var offset = point.Y - FirstRowTop + _scroll;

            if (offset < 0)
            {
                return -1;
            }

            var index = offset / RowHeight;
            return index < _holders.Count ? index : -1;
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

            // On the card it is in.
            using (var background = new SolidBrush(AppTheme.Surface))
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

            // The empty-list lines at the text size in use, as their faces
            // are: that nobody has it, and what fills the list.
            if (_holders.Count == 0)
            {
                g.DrawString("Nobody at the club has earned this one yet - it's there to be taken.",
                    AppTheme.BodyFont, mutedBrush, new RectangleF(0, Px(4), Width, Px(24)), near);
                g.DrawString("Whoever earns it first is listed here, marked FIRST, with everyone after them under it.",
                    AppTheme.SmallFont, mutedBrush, new RectangleF(0, Px(30), Width, Px(20)), near);
                return;
            }

            var rowHeight = RowHeight;
            var firstRowTop = FirstRowTop;

            for (var i = 0; i < _holders.Count; i++)
            {
                var top = firstRowTop + i * rowHeight - _scroll;

                if (top > Height || top + rowHeight < 0)
                {
                    continue;
                }

                DrawRow(g, _holders[i], i, top);
            }

            // A mark round the list while the keys scroll it (bug hunt 24).
            if (Focused && ShowFocusCues)
            {
                ControlPaint.DrawFocusRectangle(g, ClientRectangle);
            }
        }

        private void DrawRow(Graphics g, Holder holder, int index, float top)
        {
            var isMine = string.Equals(holder.Transponder, _mine, StringComparison.OrdinalIgnoreCase);

            // The row's card and everything in it at the text size in use, as
            // the lines in it are. The card's rounding stays the app's one
            // (AppTheme.CornerRadius, which DrawCard uses), so the hover wash
            // keeps it too.
            // Across the card's inside, which gives the room either side.
            var bounds = new RectangleF(0, top, Width, RowHeight - Px(6));

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

        // Counted in club nights - see HolderAgo (bug hunt 24).
        private static string Ago(DateTime utc) => HolderAgo(utc, DateTime.UtcNow);
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
