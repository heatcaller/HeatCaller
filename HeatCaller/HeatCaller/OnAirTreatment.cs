using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// The Big Screen while the announcer's show is on air: a broadcast lower
/// third across the foot of the crowd's screen - a steady ON AIR tally and
/// what is on air in words - for as long as ShowOnAir says a show's music is
/// playing.
///
/// WHY THIS EXISTS. The show treatment (ShowTreatment) plays the club's own
/// opener, runs a bed under the voice and plays out at the end; it genuinely
/// sounds like a programme coming on air. The crowd's screen knew nothing
/// about any of it and carried on showing the leaderboard exactly as it had
/// a second earlier - so the room heard a broadcast and saw a spreadsheet.
/// ShowOnAir already said when a show was on, for the club music's sake;
/// this is the second thing to listen to it.
///
/// WHY A LOWER THIRD AND NOT A TITLE CARD. A title card across the whole
/// screen is what a broadcast would do, and it is the one thing this screen
/// may never do: a race always wins the screen, and the two commonest shows
/// sit either side of one - the round intro is said while the grid roll call
/// is up, the recap while the finished board is still being read. Television
/// has exactly this problem and solves it the same way: the furniture goes
/// along the bottom and the pictures carry on behind it. So this takes a
/// band at the foot of the window, below even the On Deck bar, and nothing
/// above it moves - the top band, the elapsed clock, the countdown, the flag
/// and warning banner, the board, the roll call, a sponsor slide and the
/// track map are all left where they were. The board gives up a band for the
/// length of the show, the way it already gives one up for a warning
/// (BigScreenForm._lblWarning): the one precedent on this screen for
/// furniture that comes and goes, and the reason the strip is modest rather
/// than the half-screen a title card wants.
///
/// WHY IT NEVER CHANGES SHAPE. It would be easy to put a title card up when
/// nothing is on track and fall back to a lower third when something is.
/// Turned down: furniture that is a different shape depending on the race
/// reads as a fault rather than as a decision, and a cold open with nothing
/// on track - the one time the room is watching this screen hardest - is
/// precisely when it must look like it was always meant to be there.
///
/// WHY NOT THE BOTTOM BAR IT ALREADY HAS. The On Deck bar was the tempting
/// answer, since the furniture is already built. But during a race that bar
/// carries the championship as it would stand if the race ended now, which
/// is live information about the race on track, and it is one line of
/// twenty-point type - too small to read as broadcast furniture from the
/// back of a hall. It keeps its bar.
/// </summary>
internal static class OnAirTreatment
{
    /// <summary>
    /// What the band says when the show did not call itself anything.
    ///
    /// ShowOnAir's caption is optional on purpose - "a show with nothing to
    /// call itself is still a show" - and an ON AIR tally sitting beside an
    /// empty plate reads as a half-drawn screen rather than as a decision.
    /// </summary>
    internal const string Unnamed = "THE SHOW";

    /// <summary>
    /// Whether the lower third is up: a show's music playing, and the club
    /// has not switched the treatment off.
    ///
    /// Deliberately NOT asked anything about the race. A cold open and a
    /// sign-off happen with nothing on track at all, so a rule that needed a
    /// race would have the band missing for two of the four shows - and
    /// because the band covers nothing, there is no race state it has to
    /// stand down for. See the note on the class.
    /// </summary>
    internal static bool Showing(bool playing, bool wanted) => playing && wanted;

    /// <summary>
    /// What is on air, as the crowd reads it: upper case, one line, never
    /// empty.
    ///
    /// Folded to one line here rather than trusted. The caption comes from
    /// whoever started the show, and a line break in it would be drawn as a
    /// box by the single-line draw in <see cref="OnAirBar"/> - across the
    /// wall, at the one moment the room is looking at this band.
    /// </summary>
    internal static string Caption(string? onAir)
    {
        if (string.IsNullOrWhiteSpace(onAir))
        {
            return Unnamed;
        }

        // An empty separator array means "split on any whitespace", which is
        // the whole point: tabs and line breaks go the same way as spaces.
        var words = onAir.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries);

        return words.Length == 0 ? Unnamed : string.Join(' ', words).ToUpperInvariant();
    }

    /// <summary>
    /// The crowd's words for a kind of show, for whoever starts one to hand
    /// ShowOnAir: <c>ShowOnAir.Begin(OnAirTreatment.Words(kind))</c>.
    ///
    /// Here rather than in the show's own file because this is the only place
    /// that knows these words go on a wall: they are what a spectator reads,
    /// not what the console logs, so they are the club's name for the
    /// segment rather than the enum's. They are ShowSegments.Candidates -
    /// the only kinds that ever get the music - and anything else falls
    /// back rather than putting an enum name on the screen. Counted here as
    /// "the eleven" until the honours joined them on 10 Oct 2026; a number
    /// in a comment goes stale the first time the list grows, so it says
    /// which list instead.
    ///
    /// ShowTreatment still calls Begin() with no caption, so the band reads
    /// THE SHOW until it passes one. That was not worth waiting for: the
    /// band is right either way, and the alternative was the Big Screen
    /// guessing from the race what the announcer was doing.
    /// </summary>
    /// <summary>
    /// What a segment is called on screen.
    ///
    /// Delegates, deliberately: this used to hold its own copy of the table
    /// and ShowBlock.CaptionFor held another, written the same afternoon,
    /// disagreeing on seven of the eleven segments. The show is what names
    /// itself - it is the thing that passes the caption to ShowOnAir - so
    /// its table is the one that survived, carrying this one's wording. Kept
    /// as a method rather than deleted because the fallback belongs here:
    /// CaptionFor answers null for anything that is not a show, and a band
    /// on a crowd screen still has to say something.
    /// </summary>
    internal static string Words(SegmentKind kind) => ShowBlock.CaptionFor(kind) ?? Unnamed;

    // ------------------------------------------------------------------
    //  The club's switch
    // ------------------------------------------------------------------

    /// <summary>
    /// Serialises the answer and the file it is kept in.
    ///
    /// Nothing to do with ShowOnAir's lock and never taken from inside it -
    /// see BigScreenForm.NudgeOnAir, which is the only code that runs in
    /// there and which reads nothing at all. In practice this is only ever
    /// taken from the one UI thread every window in the app shares, but a
    /// lock costs nothing here and the web server already reads the club's
    /// settings from threads of its own.
    /// </summary>
    private static readonly object Gate = new();

    /// <summary>Null until the file has been read - once a run, not twice a second.</summary>
    private static bool? _wanted;

    private static string FilePath => Path.Combine(AppPaths.Root, "big-screen.json");

    private sealed class ScreenFile
    {
        /// <summary>
        /// Whether the Big Screen dresses the announcer's show. On by
        /// default: a club that has gone to the trouble of giving its show
        /// opener music wants the screen to agree, and a club that never
        /// runs a show never sees this band at all.
        /// </summary>
        public bool OnAir { get; set; } = true;
    }

    /// <summary>
    /// Whether the club wants the on-air treatment at all.
    ///
    /// WHERE THE SWITCH LIVES. In a file of its own, toggled by O on the Big
    /// Screen itself. The Big Screen's other settings - its columns, its
    /// look, its logo - live in AppSettings with the rest of the club's
    /// settings, and that is where this would normally go. It is here
    /// instead because the Big Screen's own switches already are: F11 for
    /// full screen and M for the track map, both keys on the screen, both
    /// listed in the reminder strip in its top-right corner, which is where
    /// a club looks for them. Unlike M, this one is remembered - a club that
    /// turns the treatment off wants it off next week too, and a setting
    /// that forgets is a setting a volunteer redoes every race night.
    ///
    /// A file rather than a look (Looks): a look is a set of colours, the
    /// built-in ones cannot be edited at all, and "whether the screen dresses
    /// the show" is not a colour.
    /// </summary>
    internal static bool Wanted
    {
        get
        {
            lock (Gate)
            {
                return _wanted ??= Read();
            }
        }

        set
        {
            lock (Gate)
            {
                if (_wanted == value)
                {
                    return;
                }

                _wanted = value;

                // The answer is kept whether or not the file took it: a club
                // whose AppData has gone read-only still gets the switch for
                // tonight, which is the night they are standing in.
                Write(value);
            }
        }
    }

    private static bool Read()
    {
        try
        {
            if (File.Exists(FilePath)
                && JsonSerializer.Deserialize<ScreenFile>(SafeFile.ReadAllTextWithRetry(FilePath)) is { } file)
            {
                return file.OnAir;
            }
        }
        catch
        {
            // No file, or one hand-edited into nonsense: on, as a fresh
            // install is. Deliberately NOT SafeFile.HoldWritesAfterUnreadable,
            // which the stores that hold a club's work use - there is one
            // switch in this file, so there is nothing of theirs to lose by
            // writing over it, and refusing to save would leave the key
            // doing nothing with no way to find out why.
        }

        return true;
    }

    private static void Write(bool onAir) => SafeFile.WriteAllText(
        FilePath,
        JsonSerializer.Serialize(new ScreenFile { OnAir = onAir }, new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>
    /// Forgets the saved answer, for the test run.
    ///
    /// The switch is the whole process's and each test's data folder is its
    /// own (TempAppData), so a test that turned the treatment off would turn
    /// it off for every test that followed - with nothing in the next test's
    /// folder to say why. The bug ShowOnAir.ResetForTests and
    /// AdBreak.ResetForTests both exist for.
    /// </summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            _wanted = null;
        }
    }
}

/// <summary>
/// The lower third, drawn: a steady ON AIR tally in broadcast red, the
/// caption beside it on a plate in the club's look, and the look's accent
/// underlining it. It wipes on from the left and then sits still.
///
/// A STEADY tally, not a blinking one. A real studio tally lamp is steady,
/// and anything that blinks or breathes on a crowd screen reads as a
/// notification - the one thing this band must not look like. It also means
/// the only animation is the wipe, so nothing in here repaints while a race
/// is being timed, exactly as the roll call's cards and the between-races
/// cards only animate while they are actually moving.
///
/// Colours are handed in by the Big Screen from its look, except the tally's
/// red and white: ON AIR means the same thing in every look, as the roll
/// call's green and amber do and as the warning banner's yellow and black
/// do. The caption's ink is worked out from the plate it sits on
/// (Looks.ClearestTextOn) rather than assumed light - this screen is read in
/// Daylight as often as in Midnight, and a club on Synthwave has a plate
/// that is neither.
/// </summary>
internal sealed class OnAirBar : Control
{
    /// <summary>The tally, in every look - see the note on the class.</summary>
    private static readonly Color TallyRed = Color.FromArgb(206, 32, 39);

    private const string TallyText = "ON AIR";

    /// <summary>How long the band takes to wipe on.</summary>
    private const int WipeMs = 380;

    private readonly System.Windows.Forms.Timer _animate = new() { Interval = 30 };

    private long _shownAt;

    public Color Canvas { get; set; } = Color.Black;

    /// <summary>The plate the caption sits on: the look's Surface, or its hairline colour in a look whose Surface IS its background.</summary>
    public Color Plate { get; set; } = Color.FromArgb(30, 30, 30);

    /// <summary>The look's accent, for the rule under the caption. It carries no meaning, so a look where it barely shows loses nothing.</summary>
    public Color Highlight { get; set; } = Color.Gold;

    /// <summary>What the band says now - see <see cref="Say"/>. Empty draws nothing at all.</summary>
    public string Saying { get; private set; } = string.Empty;

    /// <summary>
    /// Who the line on air is about - "Hargrove · Bellamy", or empty where
    /// the line names nobody, which is most lines.
    ///
    /// ADDED 11 Oct 2026, and it is the whole reason this band stopped
    /// being decoration. "ON AIR │ BATTLE WATCH" told a room that something
    /// was happening; "ON AIR │ BATTLE WATCH │ Hargrove · Bellamy" tells
    /// them what to look at, at the moment the voice says it. See
    /// LowerThird, which works out who from the line itself.
    ///
    /// A SECOND PLATE rather than more words on the caption's. The caption
    /// is what is playing and the names are who it is about: two different
    /// kinds of thing, and running them into one line - "BATTLE WATCH -
    /// HARGROVE, BELLAMY" - is how a lower third turns into a sentence
    /// nobody reads. It is also what lets the names be dropped first when
    /// the band is squeezed, which is the right thing to lose.
    /// </summary>
    public string About { get; private set; } = string.Empty;

    public OnAirBar()
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

            if (Environment.TickCount64 - _shownAt > WipeMs)
            {
                _animate.Stop();
            }
        };
    }

    /// <summary>
    /// What is on air, in the crowd's words.
    ///
    /// Only wipes again when the words change: the Big Screen hands this
    /// over twice a second for as long as the show runs, and re-wiping on
    /// each would be a band flickering across the bottom of the wall for
    /// the whole segment.
    /// </summary>
    public void Say(string caption) => Say(caption, string.Empty);

    /// <summary>
    /// What is on air and who it is about - see <see cref="About"/>.
    ///
    /// THE WIPE IS FOR THE CAPTION ONLY. The names change while one
    /// segment runs, because a two-hander names one driver in its first
    /// turn and another in its second - and a band that wiped across the
    /// bottom of the wall at each of those would be the flicker the
    /// single-argument overload's note already warns about. So a change of
    /// names repaints and a change of caption wipes.
    /// </summary>
    public void Say(string caption, string? about)
    {
        var who = (about ?? string.Empty).Trim();
        var sameWho = string.Equals(About, who, StringComparison.Ordinal);

        if (string.Equals(Saying, caption, StringComparison.Ordinal))
        {
            if (sameWho)
            {
                return;
            }

            About = who;
            Invalidate();

            return;
        }

        Saying = caption;
        About = who;
        _shownAt = Environment.TickCount64;

        // Not started while the band is down: a timer ticking behind a
        // hidden control is the kind of thing that is only ever found by
        // wondering why a race laptop is busy.
        if (Visible)
        {
            _animate.Start();
        }

        Invalidate();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (Visible)
        {
            // From the start of the wipe each time it comes up, so a show
            // beginning mid-evening is a band arriving rather than one that
            // was apparently always there.
            _shownAt = Environment.TickCount64;
            _animate.Start();
            Invalidate();
        }
        else
        {
            _animate.Stop();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        using (var back = new SolidBrush(Canvas))
        {
            g.FillRectangle(back, ClientRectangle);
        }

        // Nothing to say, or no band to say it in - a window dragged to its
        // smallest, or the moment before the first layout has run.
        if (Saying.Length == 0 || Width < 80 || Height < 24)
        {
            return;
        }

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var pad = Math.Max(3, Height / 10);
        var plate = new Rectangle(pad, pad, Width - pad * 2, Height - pad * 2);

        // The wipe: the band is REVEALED from the left, easing to a stop,
        // rather than faded in. The same reason the between-races cards
        // slide - TextRenderer draws with GDI, which ignores a colour's
        // alpha, so a fade would fade the plates and leave the words solid
        // on top of them.
        var through = Math.Clamp((Environment.TickCount64 - _shownAt) / (double)WipeMs, 0, 1);
        var ease = 1 - Math.Pow(1 - through, 3);

        g.SetClip(new Rectangle(plate.Left, 0, Math.Max(1, (int)(plate.Width * ease)), Height));

        Draw(g, plate);

        // No ResetClip: this Graphics belongs to this paint and nothing
        // after here draws with it.
    }

    /// <summary>
    /// The band itself, inside the wipe's clip: the tally block, then the
    /// caption's plate beside it.
    ///
    /// Two blocks side by side rather than the tally inset into one plate.
    /// A rounded block inside a rounded block leaves a sliver of the outer
    /// one showing at the corner, which at this size reads as a drawing
    /// fault from across a hall - and a flag beside a name plate is what a
    /// lower third actually looks like.
    /// </summary>
    private void Draw(Graphics g, Rectangle plate)
    {
        var radius = Math.Max(4, plate.Height / 5);
        var gap = Math.Max(4, plate.Height / 8);

        // Sized from the band, not from the window: the Big Screen already
        // grows the band with the window (BigScreenForm.ApplyScale), so
        // following it is how this ends up the right size on a laptop and on
        // a projector without knowing which it is on.
        using var tallyFont = new Font(AppTheme.BoldFont.FontFamily, Math.Max(9f, plate.Height * 0.30f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var capFont = new Font(AppTheme.BoldFont.FontFamily, Math.Max(11f, plate.Height * 0.50f), FontStyle.Bold, GraphicsUnit.Pixel);

        var measure = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        var flags = measure | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;

        var lamp = Math.Max(5, (int)(plate.Height * 0.18f));
        var words = TextRenderer.MeasureText(g, TallyText, tallyFont, Size.Empty, measure).Width;

        // Never more than half the band, however wide the face measures: the
        // caption is the part a spectator is reading.
        var tally = new Rectangle(plate.Left, plate.Top,
            Math.Min(plate.Width / 2, gap + lamp + gap + words + gap), plate.Height);

        using (var path = AppTheme.RoundedRect(tally, radius))
        using (var red = new SolidBrush(TallyRed))
        {
            g.FillPath(red, path);
        }

        using (var white = new SolidBrush(Color.White))
        {
            g.FillEllipse(white, tally.Left + gap, tally.Top + (tally.Height - lamp) / 2, lamp, lamp);
        }

        TextRenderer.DrawText(g, TallyText, tallyFont,
            new Rectangle(tally.Left + gap + lamp + gap, tally.Top, Math.Max(0, tally.Width - (gap + lamp + gap * 2)), tally.Height),
            Color.White, TallyRed, flags);

        // WHO IT IS ABOUT, on a plate of its own at the right-hand end -
        // and sized from the words rather than given a share, because the
        // caption is the part a spectator is reading and the names are the
        // part that makes them look at the board. Never more than a third
        // of the band, and dropped entirely when there is not room for it:
        // see About for why these are the right thing to lose.
        using var whoFont = new Font(AppTheme.BoldFont.FontFamily, Math.Max(10f, plate.Height * 0.38f), FontStyle.Regular, GraphicsUnit.Pixel);

        var whoWidth = 0;

        if (About.Length > 0)
        {
            var wanted = TextRenderer.MeasureText(g, About, whoFont, Size.Empty, measure).Width + radius * 2;

            if (wanted <= plate.Width / 3)
            {
                whoWidth = wanted;
            }
        }

        var caption = new Rectangle(tally.Right + gap, plate.Top,
            Math.Max(0, plate.Right - tally.Right - gap - (whoWidth > 0 ? whoWidth + gap : 0)), plate.Height);

        // A band squeezed so hard that the plate would be narrower than its
        // own corners: the tally alone, which is still the thing that says
        // the show is on.
        if (caption.Width <= radius * 2)
        {
            return;
        }

        using (var path = AppTheme.RoundedRect(caption, radius))
        using (var fill = new SolidBrush(Plate))
        {
            g.FillPath(fill, path);
        }

        // Worked out from the plate rather than taken from the look's text
        // colour: this screen's whole argument is contrast and type size,
        // and the plate can be any colour a club's look makes it.
        var ink = Looks.ClearestTextOn(Plate);

        var rule = Math.Max(2, plate.Height / 16);

        TextRenderer.DrawText(g, Saying, capFont,
            new Rectangle(caption.Left + radius, caption.Top, caption.Width - radius * 2, caption.Height - rule * 3),
            ink, Plate, flags);

        // Inside the rounded shape, not along its edge, so the corners stay
        // round.
        using (var accent = new SolidBrush(Highlight))
        {
            g.FillRectangle(accent, caption.Left + radius, caption.Bottom - rule * 2, caption.Width - radius * 2, rule);
        }

        if (whoWidth <= 0)
        {
            return;
        }

        var who = new Rectangle(caption.Right + gap, plate.Top, whoWidth, plate.Height);

        // THE ACCENT'S PLATE, where the caption's is the look's Surface.
        // The two plates have to be told apart from across a hall, and a
        // second Surface-coloured plate beside the first reads as one plate
        // with a line drawn down it. The ink is worked out from the accent
        // for the reason the caption's is worked out from its plate: a
        // club's accent can be any colour, Gold included, and white on gold
        // is unreadable.
        using (var path = AppTheme.RoundedRect(who, radius))
        using (var fill = new SolidBrush(Highlight))
        {
            g.FillPath(fill, path);
        }

        TextRenderer.DrawText(g, About, whoFont,
            new Rectangle(who.Left + radius, who.Top, who.Width - radius * 2, who.Height),
            Looks.ClearestTextOn(Highlight), Highlight, flags);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animate.Dispose();
        }

        base.Dispose(disposing);
    }
}
