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
/// button is what makes the warning worth having: a prize giving, a crash
/// to talk through, a word with a driver, and the announcer stays out of
/// it until it comes off. Nothing is lost by holding - a fight is still a
/// fight afterwards, and a fact that went unsaid is unspent.
///
/// ONE LABEL, NOT A LAMP AND A LABEL (14 Oct 2026)
///
/// The row began as a lamp, an "Announcer: on air" state and a line
/// saying what had last been said. Once the middle one could say WHAT was
/// going out rather than merely that something was, the first two were
/// saying a weaker version of the same thing - so both came off, and the
/// name itself goes to the accent while it is being spoken. A race
/// director glances at this row for half a second on their way to a flag
/// button; it should take one reading, not three.
/// </summary>
internal sealed class AnnouncerStrip : FlowLayoutPanel
{
    /// <summary>
    /// What is on air now, or what was last said - see <see cref="NowOn"/>
    /// and <see cref="Lately"/>.
    ///
    /// A STEADY LABEL, which it had to become when it started saying what
    /// is on air (14 Oct 2026). The note on Lately records what happened
    /// the last time this label was allowed to size itself to whatever it
    /// said: it pushed every button along the row about as it grew and
    /// shrank. Showing what is ON now changes it far more often than
    /// showing what was last said - a lap call, a flag, the next piece of a
    /// recap - so on a busy race the whole row would have jittered once a
    /// second, on the screen where somebody is reaching for a flag button.
    ///
    /// Sized to every wording it can hold rather than to a width somebody
    /// typed: see <see cref="EverySaying"/>.
    /// </summary>
    private readonly SteadyLabel _said = new();
    private readonly Button _hold = new();

    /// <summary>Plays the last segment again - see <see cref="ReplayWanted"/>.</summary>
    private readonly Button _replay = new();

    /// <summary>
    /// Plays the last twenty seconds of race audio again - see
    /// <see cref="ReplayMomentWanted"/> (22 Oct 2026).
    ///
    /// SHOWN ONLY WHILE THERE IS SOMETHING TO REPLAY, which is only while
    /// race recording is on: on a night without it the button could do
    /// nothing but explain itself, and this row is the busiest on the page.
    /// </summary>
    private readonly Button _replayMoment = new();

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

    /// <summary>
    /// How many of the announcer's own timing calls did not get said this
    /// race - see <see cref="AnnouncerLoad"/>.
    ///
    /// ASKED FOR FROM THE FLOOR (14 Oct 2026), in these words: "I didn't
    /// know how many are being dropped as I don't have a visual." They
    /// were all in the console, which nobody reads on a race night, and
    /// the person they would have told is standing at a microphone looking
    /// at THIS row. From here the announcer sounded busy, which is exactly
    /// what it sounds like when it is throwing most of its work away.
    ///
    /// LAST IN THE ROW on purpose. It is a plain label that sizes itself,
    /// and the note on <see cref="_said"/> records what that did the last
    /// time one was allowed to: it pushed every button along the row as it
    /// grew. At the end of the flow it pushes nothing.
    /// </summary>
    private readonly Label _load = new();

    private bool _holding;

    /// <summary>
    /// Whether the row shows its own "On air / Last" label. Off in the
    /// Broadcast window (25 Oct 2026), which says the same thing larger,
    /// across its top - see BroadcastWindow.
    /// </summary>
    internal bool ShowsSaying
    {
        get => _showsSaying;
        set
        {
            _showsSaying = value;

            // At once, not on the next tick: the window would otherwise open
            // with the label shown twice for up to a second.
            if (!value)
            {
                _said.Visible = false;
            }
        }
    }

    private bool _showsSaying = true;

    /// <summary>What the row's label says now - on air, last, writing or muted - for the Broadcast window's heading.</summary>
    internal string Saying => _said.Text;

    /// <summary>Whether that is lit - something going out, or a line seconds away.</summary>
    internal bool SayingLit => _said.ForeColor == AppTheme.Accent;

    /// <summary>Whether the announcer is held - so Race Control's Broadcast button can say HELD while the window is shut.</summary>
    internal bool Holding => _holding;

    /// <summary>
    /// Puts Hold's own colours back after a theme pass has reset every
    /// button's (BroadcastWindow.Restyle) - and each button's width with
    /// them. The pass turns AutoSize off on a button shorter than it likes,
    /// and this row is styled before it has ever been laid out, so every
    /// button would keep the 75px a new one starts at: "Replay Moment" cut
    /// in half, "HELD - Release" worse. Back on, as tall as the pass made
    /// it, and shrinking again when Hold's caption does.
    /// </summary>
    internal void Restyle()
    {
        ShowHold();

        foreach (var button in new[] { _hold, _replay, _replayMoment, _skip, _notes, _votes })
        {
            button.MinimumSize = new Size(0, button.Height);
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.AutoSize = true;
        }
    }

    public AnnouncerStrip()
    {
        FlowDirection = FlowDirection.LeftToRight;
        WrapContents = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;
        Tag = AppTheme.KeepBackColor;

        _said.AutoSize = true;
        _said.ForeColor = AppTheme.TextMuted;
        _said.Margin = new Padding(0, 2, 10, 0);
        _said.Wordings = EverySaying();

        _hold.Text = HoldCaption;
        _hold.AutoSize = true;
        _hold.Margin = new Padding(0, 0, 6, 0);
        _hold.Click += (_, _) =>
        {
            _holding = !_holding;
            ShowHold();
            HoldChanged?.Invoke(_holding);
        };

        _replay.Text = ReplayCaption;
        _replay.AutoSize = true;
        _replay.Margin = new Padding(0, 0, 6, 0);
        _replay.Click += (_, _) => ReplayWanted?.Invoke();

        _replayMoment.Text = ReplayMomentCaption;
        _replayMoment.AutoSize = true;
        _replayMoment.Margin = new Padding(0, 0, 6, 0);
        _replayMoment.Visible = false;
        _replayMoment.Click += (_, _) => ReplayMomentWanted?.Invoke();

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

        _load.AutoSize = true;
        _load.ForeColor = AppTheme.TextMuted;
        _load.Margin = new Padding(10, 2, 0, 0);
        _load.Tag = AppTheme.KeepBackColor;

        // THE COUNT OPENS THE LIST (20 Oct 2026): "17 lost" says the
        // announcer is running out of room; clicking it says which calls
        // it lost, and to what.
        _load.Cursor = Cursors.Hand;
        _load.Click += (_, _) => LoadWanted?.Invoke();

        // Focus off after a click, for the reason the flag buttons do it:
        // a button that keeps focus keeps answering to Space and Enter, and
        // on this row that means a stray keypress puts the last segment out
        // over a live PA, or cuts off the one being spoken. Done here
        // rather than by Race Control because the buttons are this class's.
        AppTheme.ReleaseFocusAfterClick(_hold, _replay, _replayMoment, _skip, _notes, _votes);

        // WHAT HOLD AND SKIP STOP, said where they are pressed (17 Oct
        // 2026), now that both reach the recap show as well as the
        // director's segments. Neither cuts a word off: the voice queue
        // holds the flag calls too - see RaceControlControl.SkipWhatIsOnAir.
        var tips = new ToolTip();
        AppTheme.StyleToolTip(tips, keep: true);
        tips.SetToolTip(_hold,
            "Holds the announcer: no new commentary segment, hand-over or recap show starts until you press Release. "
            + "A recap show already running stops after the line being spoken.");
        tips.SetToolTip(_skip,
            "Stops what is on air: a commentary segment block, or the post-race recap show (teaser, recap, achievements, "
            + "interview, sign-off). The line being spoken finishes; nothing after it starts, and the show music rides down.");
        tips.SetToolTip(_replay, "Plays the last commentary segment again.");
        tips.SetToolTip(_replayMoment,
            "Plays the last 20 seconds the room heard again - the call, the crowd and the music - from the race "
            + "recording. Skip stops it.");
        tips.SetToolTip(_load, "Click to see each line the announcer didn't say this race, and why.");

        Controls.Add(_said);
        Controls.Add(_hold);
        Controls.Add(_replay);
        Controls.Add(_replayMoment);
        Controls.Add(_skip);
        Controls.Add(_notes);
        Controls.Add(_votes);
        Controls.Add(_load);
    }

    /// <summary>
    /// Every line <see cref="_said"/> can show, so it is sized once to the
    /// widest of them and never moves the buttons beside it again.
    ///
    /// WORKED OUT FROM THE TABLES RATHER THAN TYPED. A list of names
    /// written out here would be a fourth copy of ShowBlock's table, and
    /// the first segment anybody added would be the one that made the row
    /// jump - which is exactly the class of fault nobody reports, because
    /// it only shows up on a race night. Walking the enums means a new
    /// segment kind or a new call type is measured the first time the strip
    /// is built.
    ///
    /// Both readings of the slot, because it swaps between them: "On air:"
    /// while something is being spoken and "Last:" when it is not.
    /// </summary>
    private static IReadOnlyList<string> EverySaying()
    {
        var sayings = new List<string>();

        foreach (var kind in Enum.GetValues<SegmentKind>())
        {
            var named = ShowBlock.NameFor(kind);

            sayings.Add($"On air: {named}");
            sayings.Add($"Last: {named}");

            // The longest of the three, and the one most likely to be the
            // widest thing this label ever holds.
            sayings.Add($"Last: {named} (two voices)");
        }

        foreach (var call in Enum.GetValues<AnnouncementEventType>())
        {
            sayings.Add($"On air: {VoiceProfile.Describe(call).Label}");
        }

        foreach (var part in OnAirParts.All)
        {
            sayings.Add($"On air: {part}");
        }

        // An imaging piece is named after its file, which can be anything -
        // so the slot is sized for a long one, and a longer one is cut.
        sayings.Add($"On air: {new string('W', ImagingNow.LongestName)}");

        sayings.Add(WritingText);
        sayings.Add(MutedText);

        return sayings;
    }

    /// <summary>
    /// What the one label says when nothing is being spoken and the
    /// announcer is busy writing.
    ///
    /// THE ONE READING THE LAMP HAD THAT NOTHING ELSE DOES. The lamp and
    /// its "Announcer: ..." label came off on 14 Oct 2026 - the state the
    /// row was built to show is now shown by the name lighting up, and two
    /// indicators for one fact is one too many on this screen. Held was
    /// already readable from the Hold button, which says Release; ready and
    /// on air are the lit state and its absence. Writing was the only one
    /// left with nowhere to go, and it is the state the class's own notes
    /// call the one worth catching: it is the two to five seconds in which
    /// somebody can decide not to talk.
    /// </summary>
    private const string WritingText = "Writing...";

    /// <summary>
    /// What the label says when the announcer is muted on the Mixer.
    ///
    /// REPORTED FROM THE FLOOR (14 Oct 2026): the row "displays something
    /// entirely different when on mute". It did, and it was worse than
    /// different - it was wrong. Muting returns before anything is queued,
    /// so nothing speaks and the row fell through to its quiet reading:
    /// "Last: Form Guide", for a segment the brain really had written and
    /// the room had never heard a word of. A row claiming lines went out
    /// over a muted PA is the one thing on this screen that must not
    /// happen, because it is the screen somebody checks when they think
    /// the announcer has stopped working.
    ///
    /// FIRST, BEFORE EVERYTHING ELSE, for the same reason. Writing is
    /// still true while muted and so is the last segment, and neither is
    /// worth saying to somebody whose PA is silent: the answer they came
    /// for is that it is muted.
    /// </summary>
    private const string MutedText = "Muted";

    private const string HoldCaption = "Hold";
    private const string ReleaseCaption = "HELD \u00B7 Release";

    /// <summary>
    /// The Hold button as the hold stands (21 Oct 2026). While held it is
    /// amber and says HELD: a hold lasts until it is released, and it stops
    /// the throw to pit lane, the teaser and the recap after every race -
    /// so one pressed by accident during an opening silently took the whole
    /// post-race show with it, and the only sign was the word on the button
    /// changing to "Release". Not now.
    /// </summary>
    private void ShowHold()
    {
        _hold.Text = _holding ? ReleaseCaption : HoldCaption;
        AppTheme.StyleButton(_hold, _holding ? AppTheme.ButtonKind.Warning : AppTheme.ButtonKind.Auto);
    }
    private const string ReplayCaption = "Play again";
    private const string ReplayMomentCaption = "Replay Moment";
    private const string SkipCaption = "Skip";
    private const string NotesCaption = "Notes";
    private const string VotesCaption = "Votes";

    /// <summary>Raised when the race director holds the segments, or lets them go again.</summary>
    public event Action<bool>? HoldChanged;

    /// <summary>
    /// Raised when the race director wants the last segment played again -
    /// a line talked over, a PA that cut out, a room that missed it.
    /// </summary>
    public event Action? ReplayWanted;

    /// <summary>
    /// Raised when the race director wants the last twenty seconds of race
    /// audio played again - a finish the room cheered over, a pass the
    /// far end of the hall missed.
    /// </summary>
    public event Action? ReplayMomentWanted;

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
    /// Raised when the race director clicks the lost/held count (20 Oct
    /// 2026) - to see each line that wasn't said, and why.
    /// </summary>
    public event Action? LoadWanted;

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
    /// <param name="onAir">
    /// What is going out of the speakers RIGHT NOW, in words - the segment's
    /// name where one is on, and otherwise the kind of call. From
    /// AnnouncementService.OnAirNow; null when nothing is being spoken.
    ///
    /// ASKED FOR FROM THE FLOOR (14 Oct 2026). "Announcer: on air" and
    /// "Last: Race Recap" between them could not answer the one question
    /// somebody holding a microphone has, which is what the thing they can
    /// hear IS: a four-minute recap is worth waiting out and a two-second
    /// lap call is not, and the strip gave the same light for both.
    /// </param>
    /// <param name="muted">
    /// Whether the announcer is muted on the Mixer. Shown ahead of
    /// everything else, because nothing else on this row is audible while
    /// it is true - see <see cref="MutedText"/>.
    /// </param>
    /// <param name="programmeRunning">
    /// Race Control's recap show is running (17 Oct 2026) - Skip has
    /// something to stop even in the quiet while the model writes it.
    /// </param>
    public void Show(CommentaryDirector? director, bool enabled, bool speaking, string? onAir = null, bool muted = false, int notesWaiting = 0, int votesIn = 0, AnnouncerLoad? load = null, bool programmeRunning = false, bool replaying = false)
    {
        // Not tied to the announcer's mute: this is the room's audio, not
        // the announcer's words. Shown once there is a moment to replay.
        //
        // WHY (22 Oct 2026): this used to be decided after the "commentary
        // off" return below, so a club recording races with AI Commentary
        // off never saw Replay Moment (the strip was hidden), and switching
        // commentary off mid-replay took away the Skip that stops it. It is
        // decided first now, and the strip stays up with just those two
        // buttons while there is a moment or a replay to act on.
        var moment = RaceReplayBuffer.Shared.HeldSeconds >= RaceControlControl.LeastMomentSeconds;

        // Set every time rather than on a change: while the strip itself is
        // hidden a child reads Visible as false whatever its own setting
        // is, so a "has it changed" test can leave a stale true behind.
        _replayMoment.Visible = moment;

        if (director is null || !enabled)
        {
            // Off the page rather than greyed out: this is the busiest
            // screen in the app, and a club that does not use commentary
            // should not be given a row about it every race night - unless
            // the race recording has a moment to replay, or one is playing,
            // when the row stays for Replay Moment and Skip alone.
            var roomOnly = moment || replaying;

            ShowCommentaryControls(false);
            _skip.Visible = roomOnly;
            _skip.Enabled = replaying;

            // Set every time, not on a change (25 Oct 2026): the row lives in
            // the Broadcast window now, hidden most of the night, and while
            // its window is hidden Visible reads false whatever the row's own
            // setting - so "has it changed" never hid it, and the window
            // opened with the announcer off on an empty row squeezed to 0x0
            // (LayoutTests, "Broadcast (the announcer off)"). As _replayMoment
            // above.
            Visible = roomOnly;

            _said.Text = string.Empty;

            // WHY (20 Oct 2026): hiding the strip used to leave a standing
            // Hold on, with its Release button gone from the screen - so
            // turning commentary off mid-hold silenced the unofficial result
            // at the flag every race after. Hiding the strip now releases
            // the hold through the usual HoldChanged handler.
            if (_holding)
            {
                _holding = false;
                ShowHold();
                HoldChanged?.Invoke(false);
            }

            return;
        }

        if (!Visible)
        {
            Visible = true;
        }

        // Back from the Replay-Moment-only row (22 Oct 2026), if it was on.
        ShowCommentaryControls(true);
        _skip.Visible = true;

        // ONE LABEL, LIT WHILE SOMETHING IS BEING SPOKEN (14 Oct 2026).
        //
        // What is on air takes the slot and the name goes to the accent;
        // the last thing said gets the slot back, muted, the moment the
        // speakers are quiet. The lamp and the "Announcer: on air" label
        // that used to carry this came off in the same change: they said
        // THAT something was on where this says WHAT, and a race director
        // glancing at this row for half a second should not have to read
        // two things to learn one.
        //
        // On air first, whatever else is true: somebody talking over the PA
        // right now is the thing not to talk over. Held is readable from
        // the Hold button, which says Release, so it does not compete for
        // the slot - a held announcer has still said something last, and
        // that is the more useful of the two.
        if (muted)
        {
            // Not in the accent. The accent on this row means "something is
            // going out"; mute is the opposite of that, and a lit "Muted"
            // would read as an alarm where this is a setting somebody
            // chose. See MutedText.
            _said.Text = MutedText;
            _said.ForeColor = AppTheme.TextMuted;
        }
        else if (speaking)
        {
            _said.Text = NowOn(director, onAir);
            _said.ForeColor = AppTheme.Accent;
        }
        else if (ImagingNow.Current is { } piece)
        {
            // NOTHING SPOKEN, BUT A JINGLE, SWEEPER, BUMPER OR STINGER IS
            // GOING OUT (18 Oct 2026). The room hears it, so the strip says
            // so - by the file's own name - rather than "Last:" over it.
            _said.Text = $"On air: {piece}";
            _said.ForeColor = AppTheme.Accent;
        }
        else if (director.Writing)
        {
            // In the accent too, because it is a warning rather than a
            // record: a line is seconds away. See WritingText.
            _said.Text = WritingText;
            _said.ForeColor = AppTheme.Accent;
        }
        else
        {
            _said.Text = Lately(director);
            _said.ForeColor = AppTheme.TextMuted;
        }

        // Both only mean anything once there is something to act on, and a
        // button that does nothing reads as a fault rather than as a
        // setting - the same rule the Commentary page's Test button goes by.
        // AND NEITHER WORKS MUTED, which both of them already knew and
        // only found out about after the press - Play again answers "not
        // playing it again, the announcer is muted" and Skip stops a show
        // that was never audible. A button that can only fail is better
        // greyed. See MutedText.
        _replay.Enabled = !muted && director.LastSaid is not null;

        // A replayed moment is the room's audio, not the announcer's, so
        // Skip stops it muted or not (22 Oct 2026).
        _skip.Enabled = (!muted && (speaking || ShowOnAir.Playing || programmeRunning)) || replaying;

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

        ShowTheLoad(load);
    }

    private void ShowCommentaryControls(bool show)
    {
        // The controls that belong to the announcer rather than to the
        // room's recording (22 Oct 2026): hidden while the strip is up only
        // for Replay Moment and Skip, with commentary off. Notes and Votes
        // go with them - both feed the announcer, and the row's rule is
        // that a club not using commentary is not given buttons about it.
        // Set every time, not on a change: a child of a hidden strip reads
        // Visible as false whatever its own setting is (see moment, above).
        foreach (Control c in new Control[] { _said, _hold, _replay, _notes, _votes, _load })
        {
            c.Visible = show;
        }

        // Kept even when its text is, so the heading of the Broadcast window
        // can read it - just not shown there twice.
        _said.Visible = show && ShowsSaying;
    }

    /// <summary>
    /// The count of what the announcer did not say, or nothing at all when
    /// there is nothing to say - see <see cref="_load"/> for the report
    /// this answers.
    ///
    /// EMPTY RATHER THAN A NOUGHT, the rule the notes and votes counts
    /// beside it go by: "0 lost" on a quiet practice session reads as a
    /// fault, and this row does not get to spend space saying nothing
    /// happened.
    ///
    /// LIT ONLY FOR LOST LINES. Held lines are the club's own pacing dial
    /// doing its job, and a held count in the alarm colour is how a club
    /// ends up turning the fix back off.
    /// </summary>
    private void ShowTheLoad(AnnouncerLoad? load)
    {
        var badge = load?.Badge ?? string.Empty;

        if (!string.Equals(_load.Text, badge, StringComparison.Ordinal))
        {
            _load.Text = badge;
        }

        _load.ForeColor = load?.BadgeIsAWarning == true ? AppTheme.Accent : AppTheme.TextMuted;
    }

    /// <summary>
    /// What to show beside the state while the speakers are busy.
    ///
    /// A SHOW NAMES ITSELF FIRST. ShowOnAir.Caption is set by the
    /// treatment that opened the music, so it is right for every part of
    /// the programme that has a bed under it - and it is right even when
    /// the line being spoken is the third chunk of a recap, which carries
    /// no name of its own.
    ///
    /// Then what the line itself said it was, which covers the segments
    /// read plainly - a club with no music files, or a segment whose music
    /// the club unticked. Then the kind of call, which is already what
    /// OnAirNow falls back to.
    ///
    /// AND "On air" RATHER THAN A BARE NAME, because the slot it shares
    /// says "Last:" the rest of the time and a label that silently
    /// switched meaning would be worse than no label at all.
    ///
    /// THE LINE'S OWN NAME FIRST NOW (18 Oct 2026). The show's caption is
    /// one name for everything under its music, so the booth interview and
    /// the sign-off read "Race Recap" and the On Air run read whatever the
    /// first piece called itself. Every broadcast line now says which part
    /// it is (OnAirParts), and that is what is actually going out; the
    /// show's caption is for the lines that carry no name.
    /// </summary>
    private static string NowOn(CommentaryDirector director, string? onAir)
    {
        if (!string.IsNullOrWhiteSpace(onAir))
        {
            return $"On air: {onAir.Trim()}";
        }

        if (ShowOnAir.Caption is { Length: > 0 } caption)
        {
            return $"On air: {caption}";
        }

        // Nothing named it and nothing said what kind it was, which should
        // not happen - but a strip saying something true and vague beats
        // one that goes blank at the moment it is being read. The director
        // is taken for the same reason Lately takes it: if a segment is
        // what is on, its name is the useful thing to say.
        return director.LastSaid is { } last
            ? $"On air: {ShowBlock.NameFor(last.Kind)}"
            : "On air: race call";
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

        var named = ShowBlock.NameFor(last.Kind);

        // Said plainly that it was the two of them, since the strip no
        // longer shows the words and so no longer shows the two voices.
        return last.IsExchange ? $"Last: {named} (two voices)" : $"Last: {named}";
    }
}

/// <summary>
/// What each part of the broadcast calls itself on the race director's
/// strip while it is going out (18 Oct 2026) - passed as onAir with every
/// line Race Control speaks, so the strip says which part is on rather than
/// the call kind or the name of whatever show's music is underneath.
/// </summary>
internal static class OnAirParts
{
    internal const string ShowOpen = "Show Open";
    internal const string PitLane = "Pit Lane";
    internal const string DriverIntros = "Driver Intros";
    internal const string RoundIntro = "Round Intro";
    internal const string Result = "Unofficial Result";
    internal const string TechnicalLead = "Pit Lane: Technical Lead";
    internal const string Teaser = "Teaser";
    internal const string Recap = "Race Recap";
    internal const string Achievements = "Achievements";
    internal const string Booth = "Booth: Team Principal";
    internal const string SignOff = "Sign-Off";
    internal const string StopSet = "Stop Set";

    internal static readonly IReadOnlyList<string> All = new[]
    {
        ShowOpen, PitLane, DriverIntros, RoundIntro, Result, TechnicalLead, Teaser, Recap, Achievements, Booth, SignOff, StopSet,
    };
}

/// <summary>
/// Which imaging pieces are playing right now (18 Oct 2026), by the name of
/// their file - so the strip can say a jingle is on air when nobody is
/// talking. The newest piece is the one named. Beds are not counted: they
/// run under the voice, and the voice is what the strip names then.
/// </summary>
internal static class ImagingNow
{
    /// <summary>How many characters of a piece's name the strip shows.</summary>
    internal const int LongestName = 28;

    private static readonly object Gate = new();
    private static readonly List<(int Id, string Name)> Playing = new();
    private static int _next;

    /// <summary>The piece most recently started that is still playing, or null.</summary>
    internal static string? Current
    {
        get
        {
            lock (Gate)
            {
                return Playing.Count == 0 ? null : Playing[^1].Name;
            }
        }
    }

    /// <summary>A piece has started; the number given back is handed to <see cref="Ended"/>.</summary>
    internal static int Started(string? name)
    {
        var shown = string.IsNullOrWhiteSpace(name) ? "Imaging" : name.Trim();

        if (shown.Length > LongestName)
        {
            shown = shown[..(LongestName - 1)].TrimEnd() + "\u2026";
        }

        lock (Gate)
        {
            var id = ++_next;
            Playing.Add((id, shown));
            return id;
        }
    }

    /// <summary>A piece has played out or been crossfaded away.</summary>
    internal static void Ended(int id)
    {
        lock (Gate)
        {
            Playing.RemoveAll(p => p.Id == id);
        }
    }
}
