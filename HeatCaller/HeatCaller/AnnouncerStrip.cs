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
    private readonly Label _lamp = new();
    private readonly SteadyLabel _state = new();
    private readonly Label _said = new();
    private readonly Button _hold = new();

    /// <summary>Plays the last segment again - see <see cref="ReplayWanted"/>.</summary>
    private readonly Button _replay = new();

    /// <summary>Stops whatever is on air now - see <see cref="SkipWanted"/>.</summary>
    private readonly Button _skip = new();

    /// <summary>
    /// Opens tonight's club notes - see <see cref="NotesWanted"/>.
    ///
    /// ON THIS ROW because a dedication arrives in the middle of an
    /// evening and the person it has to reach is holding a microphone and
    /// looking at this screen. It is also the only button here that
    /// carries a NUMBER: a route from the room is no use at all if nobody
    /// notices anything came in, and nobody has the developer console
    /// open on a race night.
    /// </summary>
    private readonly Button _notes = new();

    /// <summary>
    /// Opens tonight's room vote: who the room reckons wins the next one,
    /// and what it said once the race has been run.
    ///
    /// BESIDE NOTES because it is the same kind of button - something
    /// about the night that no amount of lap data knows, reached from the
    /// screen the person with the microphone is already looking at. It
    /// carries a number for the same reason too: a vote nobody can see
    /// filling up is a vote the race director forgets to close.
    /// </summary>
    private readonly Button _votes = new();

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
        _hold.Margin = new Padding(0, 0, 6, 0);
        _hold.Click += (_, _) =>
        {
            _holding = !_holding;
            _hold.Text = _holding ? ReleaseCaption : HoldCaption;
            HoldChanged?.Invoke(_holding);
        };

        _replay.Text = ReplayCaption;
        _replay.AutoSize = true;
        _replay.Margin = new Padding(0, 0, 6, 0);
        _replay.Click += (_, _) => ReplayWanted?.Invoke();

        _skip.Text = SkipCaption;
        _skip.AutoSize = true;
        _skip.Margin = new Padding(0, 0, 6, 0);
        _skip.Click += (_, _) => SkipWanted?.Invoke();

        _notes.Text = NotesCaption;
        _notes.AutoSize = true;
        _notes.Margin = new Padding(0, 0, 6, 0);
        _notes.Click += (_, _) => NotesWanted?.Invoke();

        _votes.Text = VotesCaption;
        _votes.AutoSize = true;
        _votes.Margin = new Padding(0, 0, 0, 0);
        _votes.Click += (_, _) => VotesWanted?.Invoke();

        // Focus off after a click, for the reason the flag buttons do it:
        // a button that keeps focus keeps answering to Space and Enter, and
        // on this row that means a stray keypress puts the last segment out
        // over a live PA, or cuts off the one being spoken. Done here
        // rather than by Race Control because the buttons are this class's.
        AppTheme.ReleaseFocusAfterClick(_hold, _replay, _skip, _notes, _votes);

        Controls.Add(_lamp);
        Controls.Add(_state);
        Controls.Add(_said);
        Controls.Add(_hold);
        Controls.Add(_replay);
        Controls.Add(_skip);
        Controls.Add(_notes);
        Controls.Add(_votes);
    }

    private const string OffText = "Announcer: off";
    private const string ReadyText = "Announcer: ready";
    private const string WritingText = "Announcer: writing...";
    private const string HeldText = "Announcer: held";
    private const string OnAirText = "Announcer: on air";

    private const string HoldCaption = "Hold";
    private const string ReleaseCaption = "Release";
    private const string ReplayCaption = "Play again";
    private const string SkipCaption = "Skip";
    private const string NotesCaption = "Notes";
    private const string VotesCaption = "Votes";

    /// <summary>What the strip says when nothing has been said yet tonight.</summary>
    private const string NothingYet = "Nothing yet";

    /// <summary>Raised when the race director holds the segments, or lets them go again.</summary>
    public event Action<bool>? HoldChanged;

    /// <summary>
    /// Raised when the race director wants the last segment played again -
    /// a line talked over, a PA that cut out, a room that missed it.
    /// </summary>
    public event Action? ReplayWanted;

    /// <summary>
    /// Raised when the race director wants whatever is on air stopped
    /// where it is. The counterpart to Hold: Hold stops the NEXT thing,
    /// this stops the one in progress.
    /// </summary>
    public event Action? SkipWanted;

    /// <summary>
    /// Raised when the race director wants tonight's club notes - to tick
    /// off something the room sent in, or to type one of their own.
    /// </summary>
    public event Action? NotesWanted;

    /// <summary>
    /// Opens tonight's room vote - see <see cref="VotesWanted"/>. Beside
    /// the notes button because it is the same kind of thing: something
    /// about the night that the timing cannot know, reached from the
    /// screen the person with the microphone is looking at.
    /// </summary>
    public event Action? VotesWanted;

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
    /// <param name="notesWaiting">
    /// How many club notes are waiting to be approved - see ClubNotes. The
    /// one number on this row, because something arriving from the room
    /// that nobody notices is the same as no route at all.
    /// </param>
    /// <param name="votesIn">
    /// How many people have voted in the poll that is open, or nought when
    /// none is - see RoomVote. The second number on this row, and the
    /// reason is the same as the first's.
    /// </param>
    public void Show(CommentaryDirector? director, bool enabled, bool speaking, int notesWaiting = 0, int votesIn = 0)
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

        // Both only mean anything once there is something to act on, and a
        // button that does nothing reads as a fault rather than as a
        // setting - the same rule the Commentary page's Test button goes by.
        _replay.Enabled = director.LastSaid is not null;
        _skip.Enabled = speaking || ShowOnAir.Playing;

        // ALWAYS ENABLED, unlike the two above, and the difference is the
        // point: Play again and Skip act on something that may not exist,
        // where this opens a window - and the window is where a club types
        // its first note, so it has to be reachable when the list is
        // empty.
        //
        // The count only when there is one. "Notes" with a nought beside it
        // reads as a fault; "Notes (3)" reads as three people waiting.
        var caption = notesWaiting > 0 ? $"{NotesCaption} ({notesWaiting})" : NotesCaption;

        if (!string.Equals(_notes.Text, caption, StringComparison.Ordinal))
        {
            _notes.Text = caption;
        }

        // In the look's accent while something is waiting, so it catches
        // an eye that is on the leaderboard rather than on this row.
        _notes.ForeColor = notesWaiting > 0 ? AppTheme.Accent : AppTheme.TextDark;

        // The same rule as the notes count, for the same reason: a nought
        // beside it reads as a fault. A vote that is open with nobody in
        // it yet still shows plain "Votes", because the useful signal is
        // the number climbing.
        var votes = votesIn > 0 ? $"{VotesCaption} ({votesIn})" : VotesCaption;

        if (!string.Equals(_votes.Text, votes, StringComparison.Ordinal))
        {
            _votes.Text = votes;
        }

        _votes.ForeColor = votesIn > 0 ? AppTheme.Accent : AppTheme.TextDark;
    }

    /// <summary>
    /// What to show beside the state: what the announcer last did, and
    /// what the night has spent before there is one.
    ///
    /// THE SEGMENT'S NAME, NOT THE LINE ITSELF (11 Oct 2026, asked for
    /// from the floor). This used to print the first ninety characters of
    /// what had just been said, in quotes, and it was the wrong thing on
    /// every count. A race director does not read a quote - they HEARD the
    /// line, or they were busy, and a wall of text in a strip is not how
    /// anybody catches up. It pushed the buttons about on the row as it
    /// grew and shrank, it put a truncated half-sentence on the busiest
    /// screen in the app, and for a two-hander it showed one flattened
    /// line of two people talking.
    ///
    /// What is actually wanted is which part of the programme just went
    /// out - "Race Recap", "Club Honours", "Cold Open" - because that is
    /// what says where the show is up to, and whether Play again is worth
    /// pressing. ShowBlock.CaptionFor is the app's one table of those
    /// names, and CommentaryBudget's own title covers the in-race kinds it
    /// has no show name for.
    /// </summary>
    private static string Lately(CommentaryDirector director)
    {
        if (director.LastSaid is not { } last)
        {
            return director.Describe();
        }

        var named = ShowBlock.CaptionFor(last.Kind)
            ?? CommentaryBudget.Spec(last.Kind).Title;

        if (string.IsNullOrWhiteSpace(named))
        {
            named = NothingYet;
        }

        // Said plainly that it was the two of them, since the strip no
        // longer shows the words and so no longer shows the two voices.
        return last.IsExchange ? $"Last: {named} (two voices)" : $"Last: {named}";
    }
}
