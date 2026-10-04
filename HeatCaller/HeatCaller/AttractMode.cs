namespace HeatCaller;

/// <summary>One line on an attract card: a name or a label on the left, a figure on the right.</summary>
public sealed record AttractLine(string Left, string Right = "", bool Strong = false);

/// <summary>One card of the Big Screen's between-races show.</summary>
public sealed record AttractCard(string Title, IReadOnlyList<AttractLine> Lines, string Footer = "");

/// <summary>A championship's top few, for its attract card.</summary>
public sealed record ChampionshipTop(string SeasonName, IReadOnlyList<(string Name, int Points)> Rows, int RoundsCounted);

/// <summary>
/// The Big Screen between races (attract mode): rather than the last race's
/// board sitting there for ten minutes, a run of cards that turn over every
/// few seconds - who is up next and where they grid, tonight's fastest laps,
/// how the track has changed since the first race, and the championship.
/// The screen keeps the room watching, and tells drivers what they came to
/// find out without anyone asking at the desk.
///
/// When: with no race on (Idle), and once a finished race's result has had
/// its time on screen. Never over the roll call, a sponsor slide, practice
/// or a demo - those are the screen's for a reason.
/// </summary>
public static class AttractMode
{
    /// <summary>How long a finished race's board stays up before the cards take over.</summary>
    public static readonly TimeSpan ResultTime = TimeSpan.FromSeconds(45);

    /// <summary>How long each card is shown.</summary>
    public const int CardSeconds = 8;

    /// <summary>The most drivers listed on the Up Next card.</summary>
    internal const int GridShown = 10;

    /// <summary>Whether the cards are up: no race on, or a finished one whose board has had <see cref="ResultTime"/>.</summary>
    public static bool Showing(RaceState state, TimeSpan finishedFor, bool warmingUp, bool practice, bool demo) =>
        !warmingUp && !practice && !demo
        && (state == RaceState.Idle || (state == RaceState.Finished && finishedFor >= ResultTime));

    /// <summary>
    /// The cards, in the order they are shown. A card with nothing to say
    /// is left out, so a club with no championship or no program simply sees
    /// fewer cards - and none at all leaves the board up.
    /// </summary>
    public static IReadOnlyList<AttractCard> Cards(
        IReadOnlyList<UpcomingRace> upcoming,
        Func<string, string> nameOf,
        IReadOnlyList<FastestTonight> fastest,
        IReadOnlyList<TrackEvolution> evolution,
        ChampionshipTop? championship)
    {
        var cards = new List<AttractCard>();

        // Up next: the first race to run, its grid in order, and what follows.
        if (upcoming.FirstOrDefault(r => !r.OnTrack) is { } next)
        {
            var lines = next.Grid.Take(GridShown)
                .Select((t, i) => new AttractLine($"{i + 1}.  {nameOf(t)}"))
                .ToList();

            if (next.Grid.Count > GridShown)
            {
                lines.Add(new AttractLine($"... and {next.Grid.Count - GridShown} more"));
            }

            var later = upcoming.Where(r => !r.OnTrack).Skip(1).Take(2).Select(r => r.Title).ToList();

            cards.Add(new AttractCard($"UP NEXT - {next.Title}", lines,
                later.Count == 0 ? string.Empty : "Then: " + string.Join("  •  ", later)));
        }

        if (fastest.Count > 0)
        {
            cards.Add(new AttractCard("TONIGHT'S FASTEST LAPS",
                fastest.Select(f => new AttractLine($"{f.ClassName}  -  {f.DriverName}", TonightAtTheTrack.Lap(f.LapMs), Strong: true)).ToList(),
                "Best single lap in each class, any race tonight"));
        }

        if (evolution.Count > 0)
        {
            cards.Add(new AttractCard("THE TRACK TONIGHT",
                evolution.Select(e => new AttractLine(e.ClassName, e.Words, Strong: e.DeltaMs < 0)).ToList(),
                "Each driver's laps in their latest race tonight against their first"));
        }

        if (championship is { Rows.Count: > 0 } champ)
        {
            cards.Add(new AttractCard($"CHAMPIONSHIP - {champ.SeasonName}".ToUpperInvariant(),
                champ.Rows.Select((r, i) => new AttractLine($"{i + 1}.  {r.Name}", Plural.Of(r.Points, "pt"), Strong: i == 0)).ToList(),
                champ.RoundsCounted > 0 ? $"After {Plural.Of(champ.RoundsCounted, "race")}" : string.Empty));
        }

        return cards;
    }
}

/// <summary>
/// The attract cards, drawn: the card's title large across the top, its
/// lines below, and each new card sliding in over the last. Asks
/// <see cref="Source"/> for the cards afresh each time one turns over, so
/// they keep up with the night without being rebuilt twice a second.
/// </summary>
internal sealed class AttractBoard : Control
{
    private const int SlideMs = 450;

    private readonly System.Windows.Forms.Timer _turn = new() { Interval = AttractMode.CardSeconds * 1000 };
    private readonly System.Windows.Forms.Timer _animate = new() { Interval = 30 };

    private IReadOnlyList<AttractCard> _cards = Array.Empty<AttractCard>();
    private int _index;
    private long _turnedAt;

    /// <summary>The cards to show now - Race Control's, on the UI thread.</summary>
    public Func<IReadOnlyList<AttractCard>>? Source { get; set; }

    public Color Canvas { get; set; } = Color.Black;
    public Color Ink { get; set; } = Color.White;
    public Color Muted { get; set; } = Color.Gray;
    public Color Highlight { get; set; } = Color.Gold;
    public Color Rule { get; set; } = Color.FromArgb(60, 60, 60);

    /// <summary>Whether there is anything to show - the Big Screen keeps its board up when there isn't.</summary>
    public bool HasCards => _cards.Count > 0;

    public AttractBoard()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);

        DoubleBuffered = true;
        TabStop = false;

        _turn.Tick += (_, _) => Next();
        _animate.Tick += (_, _) =>
        {
            Invalidate();

            if (Environment.TickCount64 - _turnedAt > SlideMs)
            {
                _animate.Stop();
            }
        };
    }

    /// <summary>Reads the cards again without turning over - for the Big Screen to decide whether to show the board at all.</summary>
    public void Reload()
    {
        try
        {
            _cards = Source?.Invoke() ?? Array.Empty<AttractCard>();
        }
        catch
        {
            _cards = Array.Empty<AttractCard>();
        }

        if (_index >= _cards.Count)
        {
            _index = 0;
        }
    }

    private void Next()
    {
        Reload();

        if (_cards.Count == 0)
        {
            return;
        }

        var was = _index;
        _index = (_index + 1) % _cards.Count;

        // One card: it stays put rather than sliding in over itself.
        if (_index != was)
        {
            _turnedAt = Environment.TickCount64;
            _animate.Start();
        }

        Invalidate();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (Visible)
        {
            // Always from the first card - "up next" is the one people look for.
            Reload();
            _index = 0;
            _turnedAt = Environment.TickCount64;
            _animate.Start();
            _turn.Start();
        }
        else
        {
            _turn.Stop();
            _animate.Stop();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using (var back = new SolidBrush(Canvas))
        {
            g.FillRectangle(back, ClientRectangle);
        }

        if (_cards.Count == 0 || Width < 60 || Height < 60)
        {
            return;
        }

        var card = _cards[Math.Min(_index, _cards.Count - 1)];

        // Sliding in from the right, easing to a stop. (Slid rather than faded:
        // TextRenderer draws with GDI, which ignores a colour's alpha.)
        var t = Math.Clamp((Environment.TickCount64 - _turnedAt) / (double)SlideMs, 0, 1);
        var ease = 1 - Math.Pow(1 - t, 3);
        var shift = (int)((1 - ease) * Width * 0.15);

        var margin = Math.Max(16, Math.Min(Width, Height) / 16);
        var left = margin + shift;
        var width = Width - 2 * margin;

        // Sized to the room: the title about a ninth of the height, the lines
        // shared out of what is left, never bigger than the title.
        var titlePx = Math.Max(14f, Height / 9f);
        var rows = Math.Max(5, card.Lines.Count + (card.Footer.Length > 0 ? 1 : 0));
        var linePx = Math.Min(titlePx * 0.75f, (Height - margin * 2 - titlePx * 1.8f) / (rows * 1.45f));
        linePx = Math.Max(10f, linePx);

        using var titleFont = new Font(AppTheme.BoldFont.FontFamily, titlePx, FontStyle.Bold, GraphicsUnit.Pixel);
        using var lineFont = new Font(AppTheme.BoldFont.FontFamily, linePx, FontStyle.Regular, GraphicsUnit.Pixel);
        using var strongFont = new Font(AppTheme.BoldFont.FontFamily, linePx, FontStyle.Bold, GraphicsUnit.Pixel);
        using var footFont = new Font(AppTheme.BoldFont.FontFamily, Math.Max(9f, linePx * 0.7f), FontStyle.Regular, GraphicsUnit.Pixel);

        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        var y = margin;

        TextRenderer.DrawText(g, card.Title, titleFont, new Rectangle(left, y, width, (int)(titlePx * 1.3f)),
            Highlight, Canvas, flags);

        y += (int)(titlePx * 1.4f);

        using (var rule = new Pen(Rule, Math.Max(2f, titlePx / 20)))
        {
            g.DrawLine(rule, left, y, left + width, y);
        }

        y += (int)(linePx * 0.6f);

        var lineHeight = (int)(linePx * 1.45f);
        var rightWidth = card.Lines.Count == 0 ? 0 : (int)Math.Min(width * 0.55f,
            card.Lines.Max(l => TextRenderer.MeasureText(g, l.Right, strongFont, Size.Empty, flags).Width) + linePx);

        foreach (var line in card.Lines)
        {
            if (y + lineHeight > Height - margin)
            {
                break;
            }

            var font = line.Strong ? strongFont : lineFont;

            TextRenderer.DrawText(g, line.Left, font, new Rectangle(left, y, width - rightWidth, lineHeight),
                Ink, Canvas, flags | TextFormatFlags.VerticalCenter);

            if (line.Right.Length > 0)
            {
                TextRenderer.DrawText(g, line.Right, font, new Rectangle(left + width - rightWidth, y, rightWidth, lineHeight),
                    line.Strong ? Highlight : Ink, Canvas, flags | TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }

            y += lineHeight;
        }

        if (card.Footer.Length > 0)
        {
            TextRenderer.DrawText(g, card.Footer, footFont, new Rectangle(left, Height - margin - (int)(linePx * 1.2f), width, (int)(linePx * 1.2f)),
                Muted, Canvas, flags);
        }

        // Which card of how many, as dots in the corner.
        var dot = Math.Max(4f, linePx / 4);

        for (var i = 0; i < _cards.Count; i++)
        {
            using var brush = new SolidBrush(i == _index ? Highlight : Rule);
            g.FillEllipse(brush, Width - margin - (_cards.Count - i) * dot * 2.6f, margin / 2f, dot, dot);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _turn.Dispose();
            _animate.Dispose();
        }

        base.Dispose(disposing);
    }
}
