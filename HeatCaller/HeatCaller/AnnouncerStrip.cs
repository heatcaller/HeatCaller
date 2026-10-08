namespace HeatCaller;

/// <summary>
/// What the announcer is about to do, on Race Control, where the race
/// director is actually looking.
///
/// WHY THIS EXISTS
///
/// Everything else in the commentary layer reports to the developer
/// console, which nobody has open during a race. That is fine for working
/// out afterwards why a night was quiet, and no use at all at the moment
/// it matters: the race director picks up the mic, and a battle call
/// starts over the top of them.
///
/// So the one piece of state worth a place on the page is the two to five
/// seconds between a line being asked for and being spoken. That is the
/// window in which somebody can decide not to talk - or to hold the
/// segments and talk anyway.
///
/// HOLD IS THE POINT
///
/// A warning you cannot act on is just an extra thing to look at. The
/// button is what makes the light worth having: a prize giving, a crash
/// to talk through, a word with a driver, and the announcer stays out of
/// it until it comes off. Nothing is lost by holding - a fight is still a
/// fight afterwards, and a fact that went unsaid is unspent.
/// </summary>
internal sealed class AnnouncerStrip : FlowLayoutPanel
{
    private const int MostOfALine = 90;

    private readonly Label _lamp = new();
    private readonly SteadyLabel _state = new();
    private readonly Label _said = new();
    private readonly Button _hold = new();

    private bool _holding;

    public AnnouncerStrip()
    {
        FlowDirection = FlowDirection.LeftToRight;
        WrapContents = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;
        Tag = AppTheme.KeepBackColor;

        _lamp.AutoSize = true;
        _lamp.Text = "●";
        _lamp.Margin = new Padding(0, 2, 6, 0);

        // As wide as the longest thing it says, so the line beside it
        // never shuffles sideways as the state changes.
        _state.AutoSize = true;
        _state.Margin = new Padding(0, 2, 10, 0);
        _state.Wordings = new[] { OffText, ReadyText, WritingText, HeldText, OnAirText };

        _said.AutoSize = true;
        _said.ForeColor = AppTheme.TextMuted;
        _said.Margin = new Padding(0, 2, 10, 0);

        _hold.Text = HoldCaption;
        _hold.AutoSize = true;
        _hold.Margin = new Padding(0, 0, 0, 0);
        _hold.Click += (_, _) =>
        {
            _holding = !_holding;
            _hold.Text = _holding ? ReleaseCaption : HoldCaption;
            HoldChanged?.Invoke(_holding);
        };

        Controls.Add(_lamp);
        Controls.Add(_state);
        Controls.Add(_said);
        Controls.Add(_hold);
    }

    private const string OffText = "Announcer: off";
    private const string ReadyText = "Announcer: ready";
    private const string WritingText = "Announcer: writing...";
    private const string HeldText = "Announcer: held";
    private const string OnAirText = "Announcer: on air";

    private const string HoldCaption = "Hold";
    private const string ReleaseCaption = "Release";

    /// <summary>Raised when the race director holds the segments, or lets them go again.</summary>
    public event Action<bool>? HoldChanged;

    /// <summary>
    /// Draws the strip for how things stand.
    ///
    /// Called on Race Control's own once-a-second tick. "Writing" is the
    /// state worth catching, and a line takes seconds to write, so once a
    /// second is quick enough to be a warning rather than a record of one.
    /// </summary>
    /// <param name="director">The director, or null before one exists.</param>
    /// <param name="enabled">Whether commentary is switched on at all.</param>
    /// <param name="speaking">
    /// Whether the PA is saying anything at all right now - a lap call and
    /// a flag count, not just a segment. The race director reaching for
    /// the mic cares that the speakers are busy, not whose line it is.
    /// </param>
    public void Show(CommentaryDirector? director, bool enabled, bool speaking)
    {
        if (director is null || !enabled)
        {
            // Off the page rather than greyed out: this is the busiest
            // screen in the app, and a club that does not use commentary
            // should not be given a row about it every race night.
            if (Visible)
            {
                Visible = false;
            }

            _state.Text = OffText;
            _said.Text = string.Empty;

            return;
        }

        if (!Visible)
        {
            Visible = true;
        }

        // On air first: whatever else is true, somebody talking over the
        // PA right now is the thing not to talk over. Held is still
        // readable from the button, which says Release.
        if (speaking)
        {
            _state.Text = OnAirText;
            _state.ForeColor = AppTheme.Accent;
            _lamp.ForeColor = AppTheme.Accent;
        }
        else if (director.Held)
        {
            _state.Text = HeldText;
            _state.ForeColor = AppTheme.TextMuted;
            _lamp.ForeColor = AppTheme.TextMuted;
        }
        else if (director.Writing)
        {
            _state.Text = WritingText;
            _state.ForeColor = AppTheme.Accent;
            _lamp.ForeColor = AppTheme.Accent;
        }
        else
        {
            _state.Text = ReadyText;
            _state.ForeColor = AppTheme.TextMuted;
            _lamp.ForeColor = AppTheme.TextMuted;
        }

        _said.Text = Lately(director);
    }

    /// <summary>
    /// What to show beside the state: the last thing said while there is
    /// one, and what the night has spent before that.
    ///
    /// The last line matters more than the budget during a race - it is
    /// how the director knows a moment was covered without having to have
    /// heard it - so it wins the space once there is one.
    /// </summary>
    private static string Lately(CommentaryDirector director)
    {
        if (director.LastSaid is not { } last)
        {
            return director.Describe();
        }

        // One line: an exchange is two, and the strip is one row high.
        var text = last.Text.Replace('\n', ' ').Replace("  ", " ").Trim();

        return text.Length <= MostOfALine
            ? $"“{text}”"
            : $"“{text[..MostOfALine].TrimEnd()}…”";
    }
}
