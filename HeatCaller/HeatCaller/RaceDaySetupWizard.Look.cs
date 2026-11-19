using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// The wizard's look (18 Nov 2026, dialog jazz-up): which step you are on
/// and how many are left - a numbered badge beside each step on the rail, a
/// bar of the four steps over the step's title with "STEP 2 OF 4" above it -
/// the welcome page's list of what is ahead, and the tiles on the last page.
/// Nothing here changes what a step does or saves; it is all looks.
/// </summary>
internal sealed partial class RaceDaySetupWizard
{
    /// <summary>The numbered badge beside a step on the rail, and the room after it, at the normal text size.</summary>
    private const int BadgeSize = 22;

    private const int BadgeGap = 10;

    /// <summary>The steps that count - all but the welcome page and the last one.</summary>
    private static int CountedSteps => StepNames.Length - 2;

    /// <summary>"STEP 2 OF 4", over the step's title.</summary>
    private readonly Label _lblEyebrow = new();

    /// <summary>A bar of the four steps over the step's title, filled up to the one on show.</summary>
    private readonly StepTrack _stepTrack = new(StepNames.Length - 2);

    /// <summary>The welcome page: what the four steps are.</summary>
    // Made in the constructor, inside the window's build hold - see _building.
    private readonly PageStack _welcomeStep;

    /// <summary>The last page's tiles: the day, the classes racing, who is in, and who is on file.</summary>
    private readonly StatTile[] _summaryTiles = { new(), new(), new(), new() };

    /// <summary>The card the program's plan is in, while there is one to build.</summary>
    private Section? _buildCard;

    /// <summary>The short hint lines under the cards' captions, coloured with the other hints (ApplyFrameColours).</summary>
    private readonly List<Label> _cardHints = new();

    /// <summary>A card's hint line: a line in the small type saying what the card is for.</summary>
    private Label CardHint(string text)
    {
        var hint = new Label
        {
            Text = text,
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        };

        _cardHints.Add(hint);
        return hint;
    }

    /// <summary>The welcome page: a card with each of the four steps, numbered as they are on the rail.</summary>
    private void BuildWelcomeStep()
    {
        var card = new Section("What's ahead");

        var lines = new (string Name, string What)[]
        {
            ("The day", "What kind of night it is, where, and on which track."),
            ("Classes", "Which classes race tonight, and how they race."),
            ("Drivers", "Tick who's here, and add anyone new."),
            ("Program", "Tonight's races, built from all of that."),
        };

        for (var i = 0; i < lines.Length; i++)
        {
            card.Add(new StepLine(i + 1, lines[i].Name, lines[i].What), gapBelow: i == lines.Length - 1 ? 0 : UiLayout.RowGap);
        }

        _welcomeStep.Add(card, gapBelow: 0);
    }

    /// <summary>The last page: tiles of where the night stands, and the summary in a card under them.</summary>
    private void BuildSummaryTiles(Section summaryCard)
    {
        foreach (var tile in _summaryTiles)
        {
            tile.Value.Font = AppTheme.TitleFont;
            tile.Caption.Font = AppTheme.MicroFont;
        }

        _summaryStep.Add(new TileStrip(_summaryTiles), gapBelow: UiLayout.RowGap);
        _summaryStep.Add(summaryCard, gapBelow: 0);
    }

    /// <summary>The tiles on the last page, from the stores as they are now.</summary>
    private void FillSummaryTiles()
    {
        var day = OpenDay;
        var culture = System.Globalization.CultureInfo.CurrentCulture;

        _summaryTiles[0].Set(day is null ? "None" : "Open", "Race day");
        _summaryTiles[1].Set(day is null ? "-" : day.ClassNames.Count.ToString(culture), "Classes");
        _summaryTiles[2].Set(day is null ? "-" : _checkIns.SnapshotCheckedIn().Count.ToString(culture), "Checked in");
        _summaryTiles[3].Set(_profiles.All.Count.ToString(culture), "Drivers");
    }

    /// <summary>
    /// Over the step's title: "STEP 2 OF 4", and the bar of the four steps
    /// filled up to this one - or what the welcome and last pages are.
    /// </summary>
    private void ShowWhereWeAre()
    {
        var last = _step == StepNames.Length - 1;

        var eyebrow = _step == 0 ? $"{CountedSteps} short steps"
            : last ? "All done"
            : $"Step {_step} of {CountedSteps}";

        _lblEyebrow.Text = AppTheme.Spaced(eyebrow.ToUpperInvariant());
        _stepTrack.Show(_step == 0 ? 0 : last ? CountedSteps : _step, last);
    }

    /// <summary>
    /// The badge before a step's name on the rail: its number in a ring, or
    /// filled in the app's colour on the step on show. The welcome page and
    /// the last one have a dot rather than a number - they are not counted.
    /// </summary>
    private void PaintRailBadge(Graphics g, Label label, int index)
    {
        var size = UiLayout.AtTextSize(BadgeSize);
        var box = new Rectangle(UiLayout.AtTextSize(RailStepInset), (label.Height - size) / 2, size, size);
        var current = index == _step;
        var counted = index > 0 && index < StepNames.Length - 1;

        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (current)
        {
            using var fill = new SolidBrush(AppTheme.Primary);
            g.FillEllipse(fill, box);
        }
        else
        {
            using var ring = new Pen(index < _step ? AppTheme.Primary : AppTheme.SidebarTextMuted, Math.Max(1f, UiLayout.AtTextSize(1.5f)));
            g.DrawEllipse(ring, new RectangleF(box.X + 0.75f, box.Y + 0.75f, box.Width - 1.5f, box.Height - 1.5f));
        }

        if (!counted)
        {
            var dot = Math.Max(4, size / 4);

            using var fill = new SolidBrush(current ? AppTheme.OnPrimary : AppTheme.SidebarTextMuted);
            g.FillEllipse(fill, box.X + (size - dot) / 2f, box.Y + (size - dot) / 2f, dot, dot);
            return;
        }

        TextRenderer.DrawText(g, index.ToString(System.Globalization.CultureInfo.InvariantCulture), AppTheme.MicroFont, box,
            current ? AppTheme.OnPrimary : index < _step ? AppTheme.Primary : AppTheme.SidebarText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    /// <summary>Where a step's name starts on the rail: after the inset, its badge and the gap.</summary>
    private static int RailNameLeft => UiLayout.AtTextSize(RailStepInset) + UiLayout.AtTextSize(BadgeSize) + UiLayout.AtTextSize(BadgeGap);

    /// <summary>
    /// The four steps as a bar: a rounded segment each, in the app's colour
    /// up to the step on show and quiet after it - all of them in the
    /// success colour once the night is set. Sizes itself, as the layout
    /// pieces do: as wide as it is given, a few pixels tall at the text size.
    /// </summary>
    private sealed class StepTrack : Panel
    {
        private const int Thickness = 6;
        private const int Gap = 6;

        private readonly int _count;
        private int _filled;
        private bool _finished;

        public StepTrack(int count)
        {
            _count = count;

            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        /// <summary>Fills the first <paramref name="filled"/> segments - in the success colour when <paramref name="finished"/>.</summary>
        public void Show(int filled, bool finished)
        {
            _filled = filled;
            _finished = finished;
            Invalidate();
        }

        public override Size GetPreferredSize(Size proposedSize) =>
            new(proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : Width, Math.Max(4, UiLayout.AtTextSize(Thickness)));

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var behind = new SolidBrush(Parent?.BackColor ?? AppTheme.Background))
            {
                g.FillRectangle(behind, ClientRectangle);
            }

            var gap = UiLayout.AtTextSize(Gap);
            var width = (Width - gap * (_count - 1)) / (float)_count;

            for (var i = 0; i < _count; i++)
            {
                var colour = i >= _filled ? AppTheme.Border : _finished ? AppTheme.Success : AppTheme.Primary;
                var box = new RectangleF(i * (width + gap), 0, width, Height);

                using var path = Pill(box);
                using var fill = new SolidBrush(colour);
                g.FillPath(fill, path);
            }
        }

        private static GraphicsPath Pill(RectangleF r)
        {
            var path = new GraphicsPath();
            var d = Math.Max(1f, Math.Min(r.Height, r.Width));

            path.AddArc(r.Left, r.Top, d, d, 90, 180);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 180);
            path.CloseFigure();

            return path;
        }
    }

    /// <summary>
    /// One step on the welcome page: its number in a badge, its name in bold
    /// and what it is about under that, wrapping to the room left. Painted,
    /// as the readiness cards are, so the badge and the text line up exactly;
    /// as tall as what it says needs at the width it is given.
    /// </summary>
    private sealed class StepLine : Panel
    {
        private const TextFormatFlags NameFlags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

        private const TextFormatFlags WhatFlags =
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding;

        private readonly int _number;
        private readonly string _name;
        private readonly string _what;

        public StepLine(int number, string name, string what)
        {
            _number = number;
            _name = name;
            _what = what;

            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            DoubleBuffered = true;
            ResizeRedraw = true;

            // On the card's surface - kept through the theme pass, which
            // would otherwise give it the page's colour.
            BackColor = AppTheme.Surface;
            Tag = AppTheme.KeepBackColor;

            AccessibleName = $"{number}. {name}: {what}";
        }

        private static int Badge => UiLayout.AtTextSize(BadgeSize + 6);

        private static int TextLeft => Badge + UiLayout.AtTextSize(BadgeGap + 4);

        private int NameHeight => TextRenderer.MeasureText(_name, AppTheme.BoldFont, Size.Empty, NameFlags).Height;

        private int WhatHeight(int width) => TextRenderer.MeasureText(_what, AppTheme.SmallFont,
            new Size(Math.Max(20, width - TextLeft), int.MaxValue), WhatFlags).Height;

        public override Size GetPreferredSize(Size proposedSize)
        {
            var width = proposedSize.Width is > 0 and < int.MaxValue / 4
                ? proposedSize.Width
                : TextLeft + Math.Max(
                    TextRenderer.MeasureText(_name, AppTheme.BoldFont, Size.Empty, NameFlags).Width,
                    TextRenderer.MeasureText(_what, AppTheme.SmallFont, Size.Empty, NameFlags).Width);

            return new Size(width, Math.Max(Badge, NameHeight + UiLayout.HintGap + WhatHeight(width)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var behind = new SolidBrush(Parent?.BackColor ?? AppTheme.Surface))
            {
                g.FillRectangle(behind, ClientRectangle);
            }

            var badge = new Rectangle(0, 0, Badge, Badge);

            using (var fill = new SolidBrush(AppTheme.PrimaryTint))
            {
                g.FillEllipse(fill, badge);
            }

            TextRenderer.DrawText(g, _number.ToString(System.Globalization.CultureInfo.InvariantCulture), AppTheme.BoldFont, badge,
                AppTheme.Primary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            TextRenderer.DrawText(g, _name, AppTheme.BoldFont, new Point(TextLeft, 0), AppTheme.TextDark, NameFlags);

            var what = new Rectangle(TextLeft, NameHeight + UiLayout.HintGap, Math.Max(20, Width - TextLeft), Height);
            TextRenderer.DrawText(g, _what, AppTheme.SmallFont, what, AppTheme.TextMuted, WhatFlags);
        }
    }
}
