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
/// </summary>
public sealed class AchievementDetailForm : Form
{
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
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 640);
        MinimumSize = new Size(620, 520);
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var header = new Header(this) { Dock = DockStyle.Top, Height = 214 };

        _list = new HolderList(_holders, _transponder) { Dock = DockStyle.Fill };
        _list.Picked += driver =>
        {
            SelectedTransponder = driver;
            DialogResult = DialogResult.OK;
            Close();
        };

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 58, BackColor = AppTheme.Surface };

        var hint = new Label
        {
            Text = _holders.Count > 0
                ? "Double-click somebody to see their badges."
                : "Nobody has this one yet.",
            Location = new Point(16, 20),
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        var close = new Button
        {
            Text = "Close",
            Width = 100,
            Height = 34,
            Location = new Point(ClientSize.Width - 116, 12),
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            DialogResult = DialogResult.Cancel,
        };

        footer.Controls.Add(hint);
        footer.Controls.Add(close);

        Controls.Add(_list);
        Controls.Add(header);
        Controls.Add(footer);

        CancelButton = close;
        AcceptButton = close;

        AppTheme.ApplyRecursively(this);
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

            // Medallion, big. This is the one place the artwork gets to be
            // the size it deserves.
            var medallion = new RectangleF(24, 24, 104, 104);
            BadgeArt.DrawMedallion(g, medallion, badge, earned);

            var left = medallion.Right + 24;
            var right = Width - 24;
            var width = Math.Max(80f, right - left);

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

            g.DrawString(badge.Name, nameFont, titleBrush, new RectangleF(left, 22, width, 32), near);

            // Tier and group, as chips.
            var chipY = 58f;
            var tierWidth = 86f;

            AppTheme.DrawPill(g, new RectangleF(left, chipY, tierWidth, 24),
                BadgeArt.TierInk(badge.Tier), BadgeArt.TierName(badge.Tier), Color.White);

            var groupWidth = 130f;

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

            AppTheme.DrawPill(g, new RectangleF(left + tierWidth + 8, chipY, groupWidth, 24),
                BadgeArt.CategoryColour(badge.Category), BadgeArt.CategoryName(badge.Category), groupInk);

            // What it takes.
            TextRenderer.DrawText(g, badge.Description, AppTheme.BodyFont,
                Rectangle.Round(new RectangleF(left, chipY + 34, width, 44)),
                AppTheme.TextDark,
                TextFormatFlags.WordBreak | TextFormatFlags.Top | TextFormatFlags.Left | TextFormatFlags.NoPrefix);

            // How this driver stands.
            var statusY = 136f;

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
                g.DrawString(text, AppTheme.BoldFont, got, new RectangleF(24, statusY, Width - 48, 22), near);
            }
            else if (AchievementProgress.For(badge.Id, _owner._store.For(_owner._transponder)) is { } progress)
            {
                // Not yet - but here is exactly how far off.
                var fraction = Math.Clamp(progress.Current / (double)Math.Max(1, progress.Target), 0, 1);

                g.DrawString($"{progress.Current} of {progress.Target} {progress.Noun}", AppTheme.BoldFont, titleBrush,
                    new RectangleF(24, statusY, 260, 22), near);

                var track = new RectangleF(24, statusY + 26, Math.Max(120f, Width - 48), 10);

                using (var trackPath = AppTheme.RoundedRect(track, 5))
                using (var trackFill = new SolidBrush(AppTheme.RowAlt))
                {
                    g.FillPath(trackFill, trackPath);
                }

                if (fraction > 0)
                {
                    var fill = new RectangleF(track.Left, track.Top, Math.Max(10f, (float)(track.Width * fraction)), track.Height);

                    using var fillPath = AppTheme.RoundedRect(fill, 5);
                    using var brush = new LinearGradientBrush(fill, AppTheme.Primary, AppTheme.Accent, LinearGradientMode.Horizontal);
                    g.FillPath(brush, fillPath);
                }
            }
            else
            {
                g.DrawString("Not earned yet.", AppTheme.BoldFont, mutedBrush,
                    new RectangleF(24, statusY, Width - 48, 22), near);
            }

            // Rarity, and who got there first.
            var field = _owner.FieldSize();
            var holders = _owner._holders.Count;

            var rarity = $"{RarityWord(holders, field)}   ·   {holders} of {field} driver{(field == 1 ? string.Empty : "s")}";

            if (_owner._holders.FirstOrDefault() is { } first)
            {
                rarity += $"   ·   first was {first.Name}, {first.EarnedUtc.ToLocalTime():d MMM yyyy}";
            }

            g.DrawString(rarity, AppTheme.SmallFont, mutedBrush,
                new RectangleF(24, Height - 34, Width - 48, 20), near);

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
        private const int RowHeight = 46;

        private readonly List<Holder> _holders;
        private readonly string _mine;

        private int _hover = -1;
        private int _scroll;

        public event Action<string>? Picked;

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
            var index = (point.Y - 34 + _scroll) / RowHeight;
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

            var content = _holders.Count * RowHeight + 34;
            var most = Math.Max(0, content - Height);

            _scroll = Math.Clamp(_scroll - e.Delta / 2, 0, most);
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

            g.DrawString(AppTheme.Spaced("WHO HAS IT"), AppTheme.MicroFont, mutedBrush,
                new RectangleF(24, 8, 300, 20), near);

            if (_holders.Count == 0)
            {
                g.DrawString("Nobody at the club has earned this one yet - it's there to be taken.",
                    AppTheme.BodyFont, mutedBrush, new RectangleF(24, 44, Width - 48, 24), near);
                return;
            }

            var clip = g.Save();
            g.SetClip(new RectangleF(0, 30, Width, Height - 30));

            for (var i = 0; i < _holders.Count; i++)
            {
                var top = 34 + i * RowHeight - _scroll;

                if (top > Height || top + RowHeight < 24)
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
            var bounds = new RectangleF(16, top, Width - 32, RowHeight - 6);

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

            var left = bounds.Left + 14;

            // Whoever got there first gets to be marked as such, for as long
            // as the club keeps records.
            if (holder.IsFirst)
            {
                AppTheme.DrawPill(g, new RectangleF(left, bounds.Top + 11, 52, 20),
                    AppTheme.Warning, "FIRST", AppTheme.OnWarning);

                left += 62;
            }

            var name = isMine ? holder.Name + "  (this driver)" : holder.Name;

            g.DrawString(name, AppTheme.BoldFont, nameBrush,
                new RectangleF(left, bounds.Top + 4, bounds.Width * 0.45f, 20), near);

            if (!string.IsNullOrWhiteSpace(holder.Detail))
            {
                g.DrawString(holder.Detail, AppTheme.SmallFont, mutedBrush,
                    new RectangleF(left, bounds.Top + 21, bounds.Width * 0.45f, 18), near);
            }

            g.DrawString(holder.EarnedUtc.ToLocalTime().ToString("d MMM yyyy"), AppTheme.BodyFont, mutedBrush,
                new RectangleF(bounds.Right - 190, bounds.Top + 4, 176, 20), far);

            g.DrawString(Ago(holder.EarnedUtc), AppTheme.SmallFont, mutedBrush,
                new RectangleF(bounds.Right - 190, bounds.Top + 21, 176, 18), far);
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
