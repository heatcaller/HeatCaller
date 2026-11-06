namespace HeatCaller;

/// <summary>
/// The grid roll call on the Big Screen: while a race is staged, counting
/// down or warming up, every driver due in it gets a card, and each card
/// flips to green as that car crosses the line. Drivers watch their own name
/// go green; the director sees at a glance whose transponder hasn't been
/// heard - with time still to change a battery, rather than finding out a
/// lap into the race.
///
/// The same question the pre-race check asks (ReadinessCheckForm), asked
/// continuously and shown to the room. The roster is Race Control's
/// (PreRaceRoster), so the two always check the same cars.
///
/// "Heard" means heard since the roll call began, less a two-minute grace:
/// a car warming up just before the heat was staged counts, but one heard
/// in the heat before - still inside TransponderWatch's twelve minutes -
/// does not, or a car whose transponder died since would show green.
/// </summary>
internal static class RollCall
{
    /// <summary>How long before the roll call began a pass still counts.</summary>
    internal static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    /// <summary>When the last race finished (DateTime.UtcNow ticks), or 0 before any has this session. Any thread.</summary>
    private static long _lastFinishTicks;

    /// <summary>When the last race finished, or null before any has this session.</summary>
    public static DateTime? LastFinishUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastFinishTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    /// <summary>Notes that a race has just finished. Any thread; a later finish always wins.</summary>
    public static void NoteFinish(DateTime finishedUtc)
    {
        var ticks = finishedUtc.Ticks;
        long seen;

        do
        {
            seen = Interlocked.Read(ref _lastFinishTicks);

            if (ticks <= seen)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _lastFinishTicks, ticks, seen) != seen);
    }

    /// <summary>
    /// Since when a pass counts as heard for a roll call beginning at
    /// <paramref name="nowUtc"/>: the grace before it, but never from before
    /// <paramref name="lastFinishUtc"/>.
    ///
    /// WHY (2026-11-05, Bug hunt 11: the Big Screen's roll call started from
    /// now less the grace with no regard for the last finish, so a heat
    /// staged inside two minutes of the one before counted that heat's
    /// passes - a transponder that died on its last lap showed HEARD, even
    /// "ALL n CARS HEARD - READY", while Mission Control, clamped to the
    /// finish, still said waiting. The same rule as Mission Control's now.)
    /// </summary>
    public static DateTime Since(DateTime nowUtc, DateTime? lastFinishUtc)
    {
        var graced = nowUtc - Grace;
        return lastFinishUtc is { } finished && finished > graced ? finished : graced;
    }

    public enum State
    {
        Waiting,
        Heard,

        /// <summary>Heard, but only just - TransponderWatch calls it weak.</summary>
        Weak,
    }

    public sealed record Entry(string Transponder, string Name, State State);

    /// <summary>Whether the roll call is up: a real race staged, counting down or warming up, with a grid to call.</summary>
    public static bool Showing(RaceState state, bool warmingUp, bool practice, bool demo, int rosterCount) =>
        rosterCount > 0 && !practice && !demo && (state is RaceState.Staging or RaceState.Countdown || warmingUp);

    /// <summary>Each car on <paramref name="roster"/>, in roster order, and whether it has been heard since <paramref name="sinceUtc"/>.</summary>
    public static IReadOnlyList<Entry> Build(IEnumerable<string> roster, TransponderWatch watch, Func<string, string?> nameOf, DateTime sinceUtc)
    {
        var entries = new List<Entry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var transponder in roster)
        {
            if (string.IsNullOrWhiteSpace(transponder) || !seen.Add(transponder))
            {
                continue;
            }

            var sighting = watch.Of(transponder);
            var state = sighting is null || sighting.LastSeenUtc < sinceUtc
                ? State.Waiting
                : sighting.SignalKnown && sighting.Weak ? State.Weak : State.Heard;

            var name = nameOf(transponder);
            entries.Add(new Entry(transponder, string.IsNullOrWhiteSpace(name) ? transponder : name!, state));
        }

        return entries;
    }

    /// <summary>The line over the cards: "6 of 8 cars heard", or that the whole grid is in.</summary>
    public static string Headline(IReadOnlyList<Entry> entries)
    {
        var heard = entries.Count(e => e.State != State.Waiting);

        return heard == entries.Count
            ? $"ALL {entries.Count} CARS HEARD - READY"
            : $"ROLL CALL - {heard} OF {entries.Count} CARS HEARD";
    }
}

/// <summary>
/// The roll call's cards, drawn: as many to a row as makes each as big as
/// the space allows, a car's name large on each, and a card that turns green
/// as it is heard turning over to show it. Colours are handed in by the Big
/// Screen from its look, except the two that mean something whatever the
/// look - green for heard, amber for only just.
/// </summary>
internal sealed class RollCallBoard : Control
{
    private static readonly Color HeardGreen = Color.FromArgb(22, 163, 74);
    private static readonly Color WeakAmber = Color.FromArgb(234, 179, 8);

    /// <summary>How long a card takes to turn over.</summary>
    private const int FlipMs = 450;

    private IReadOnlyList<RollCall.Entry> _entries = Array.Empty<RollCall.Entry>();

    /// <summary>When each card last changed state (Environment.TickCount64) and what it was before, for its turn.</summary>
    private readonly Dictionary<string, (long At, RollCall.State From)> _flippedAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RollCall.State> _was = new(StringComparer.OrdinalIgnoreCase);

    private readonly System.Windows.Forms.Timer _animate = new() { Interval = 30 };

    /// <summary>The faces made for the last size drawn at - made again only when it changes.</summary>
    private (int CardHeight, int HeadHeight, Font Name, Font Small, Font Head)? _faces;

    public Color Canvas { get; set; } = Color.Black;
    public Color Card { get; set; } = Color.FromArgb(30, 30, 30);
    public Color Ink { get; set; } = Color.White;
    public Color Muted { get; set; } = Color.Gray;
    public Color Highlight { get; set; } = Color.Gold;

    public RollCallBoard()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);

        DoubleBuffered = true;
        TabStop = false;

        _animate.Tick += (_, _) =>
        {
            Invalidate();

            var now = Environment.TickCount64;

            if (_flippedAt.Values.All(flip => now - flip.At > FlipMs))
            {
                _animate.Stop();
            }
        };
    }

    /// <summary>Forgets every card's last state, so a new roll call starts with none mid-turn.</summary>
    public void Reset()
    {
        _was.Clear();
        _flippedAt.Clear();
        _entries = Array.Empty<RollCall.Entry>();
        Invalidate();
    }

    public void ShowEntries(IReadOnlyList<RollCall.Entry> entries)
    {
        var now = Environment.TickCount64;
        var changed = entries.Count != _entries.Count;

        foreach (var entry in entries)
        {
            if (_was.TryGetValue(entry.Transponder, out var before))
            {
                if (before != entry.State)
                {
                    _flippedAt[entry.Transponder] = (now, before);
                    _animate.Start();
                    changed = true;
                }
            }
            else
            {
                changed = true;
            }

            _was[entry.Transponder] = entry.State;
        }

        _entries = entries;

        if (changed)
        {
            Invalidate();
        }
    }

    /// <summary>How much wider than tall a card is at most.</summary>
    private const float CardShape = 2.2f;

    /// <summary>The columns that make the cards biggest in <paramref name="area"/>, cards at most <see cref="CardShape"/> times as wide as tall.</summary>
    internal static int BestColumns(int count, Size area, float gap)
    {
        if (count <= 0 || area.Width <= 0 || area.Height <= 0)
        {
            return 1;
        }

        var best = 1;
        var bestHeight = 0f;

        for (var columns = 1; columns <= count; columns++)
        {
            var rows = (int)Math.Ceiling(count / (double)columns);
            var width = (area.Width - (columns - 1) * gap) / columns;
            var height = Math.Min(width / CardShape, (area.Height - (rows - 1) * gap) / rows);

            if (height > bestHeight)
            {
                bestHeight = height;
                best = columns;
            }
        }

        return best;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using (var back = new SolidBrush(Canvas))
        {
            g.FillRectangle(back, ClientRectangle);
        }

        if (_entries.Count == 0 || Width < 20 || Height < 20)
        {
            return;
        }

        var margin = Math.Max(8, Height / 40f);
        var headHeight = Math.Max(24, Height / 10f);
        var area = new RectangleF(margin, margin + headHeight, Width - 2 * margin, Height - 2 * margin - headHeight);
        var gap = Math.Max(6, area.Height / 50f);

        var columns = BestColumns(_entries.Count, Size.Round(area.Size), gap);
        var rows = (int)Math.Ceiling(_entries.Count / (double)columns);
        var cardWidth = (area.Width - (columns - 1) * gap) / columns;
        var cardHeight = Math.Min(cardWidth / CardShape, (area.Height - (rows - 1) * gap) / rows);

        var faces = Faces((int)cardHeight, (int)headHeight);

        // The headline, in the look's highlight colour while cars are still
        // to be heard, and green once the whole grid is in.
        var allIn = _entries.All(en => en.State != RollCall.State.Waiting);

        TextRenderer.DrawText(g, RollCall.Headline(_entries), faces.Head,
            new Rectangle((int)margin, (int)margin, (int)(Width - 2 * margin), (int)headHeight),
            allIn ? HeardGreen : Highlight,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        // Centred up and down when the cards don't fill the height.
        var used = rows * cardHeight + (rows - 1) * gap;
        var top = area.Top + Math.Max(0, (area.Height - used) / 2);
        var now = Environment.TickCount64;

        for (var i = 0; i < _entries.Count; i++)
        {
            var column = i % columns;
            var row = i / columns;
            var bounds = new RectangleF(area.Left + column * (cardWidth + gap), top + row * (cardHeight + gap), cardWidth, cardHeight);

            DrawCard(g, bounds, _entries[i], faces, now);
        }
    }

    private void DrawCard(Graphics g, RectangleF bounds, RollCall.Entry entry, (int CardHeight, int HeadHeight, Font Name, Font Small, Font Head) faces, long now)
    {
        // The turn: the card narrows to nothing showing what it was, and
        // widens again showing what it is now.
        var state = entry.State;
        var squeeze = 1f;

        if (_flippedAt.TryGetValue(entry.Transponder, out var flip) && now - flip.At < FlipMs)
        {
            var t = (now - flip.At) / (float)FlipMs;
            squeeze = Math.Abs((float)Math.Cos(t * Math.PI));

            if (t < 0.5f)
            {
                // Still showing the face it had.
                state = flip.From;
            }
        }

        var width = Math.Max(1f, bounds.Width * squeeze);
        var card = new RectangleF(bounds.Left + (bounds.Width - width) / 2, bounds.Top, width, bounds.Height);

        var (fill, ink, word) = state switch
        {
            RollCall.State.Heard => (HeardGreen, Color.White, "HEARD"),
            RollCall.State.Weak => (WeakAmber, Color.Black, "WEAK SIGNAL"),
            _ => (Card, Muted, "WAITING"),
        };

        var radius = Math.Max(4, (int)(bounds.Height / 8));

        using (var path = AppTheme.RoundedRect(card, radius))
        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);

            if (state == RollCall.State.Waiting)
            {
                using var pen = new Pen(Muted, Math.Max(1f, bounds.Height / 60f));
                g.DrawPath(pen, path);
            }
        }

        // No words while the card is edge-on: they would be squashed.
        if (squeeze < 0.6f)
        {
            return;
        }

        var pad = (int)(bounds.Height / 8);
        var nameInk = state == RollCall.State.Waiting ? Ink : ink;
        var text = Rectangle.Round(new RectangleF(card.Left + pad, card.Top + pad / 2f, card.Width - 2 * pad, card.Height * 0.62f));

        TextRenderer.DrawText(g, entry.Name, faces.Name, text, nameInk,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var status = Rectangle.Round(new RectangleF(card.Left + pad, card.Top + card.Height * 0.58f, card.Width - 2 * pad, card.Height * 0.36f));

        TextRenderer.DrawText(g, word, faces.Small, status, ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    /// <summary>The name, status and headline faces for a card this tall - made once per size, not per paint.</summary>
    private (int CardHeight, int HeadHeight, Font Name, Font Small, Font Head) Faces(int cardHeight, int headHeight)
    {
        if (_faces is { } kept && kept.CardHeight == cardHeight && kept.HeadHeight == headHeight)
        {
            return kept;
        }

        DisposeFaces();

        var family = AppTheme.BoldFont.FontFamily;

        var made = (
            cardHeight,
            headHeight,
            new Font(family, Math.Max(6f, cardHeight * 0.30f), FontStyle.Bold, GraphicsUnit.Pixel),
            new Font(family, Math.Max(5f, cardHeight * 0.16f), FontStyle.Bold, GraphicsUnit.Pixel),
            new Font(family, Math.Max(6f, headHeight * 0.55f), FontStyle.Bold, GraphicsUnit.Pixel));

        _faces = made;
        return made;
    }

    private void DisposeFaces()
    {
        if (_faces is { } old)
        {
            old.Name.Dispose();
            old.Small.Dispose();
            old.Head.Dispose();
            _faces = null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animate.Dispose();
            DisposeFaces();
        }

        base.Dispose(disposing);
    }
}
