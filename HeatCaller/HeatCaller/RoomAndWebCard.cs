namespace HeatCaller;

/// <summary>
/// The "Room & Web" card on the Club Website page (17 Oct 2026): the club's
/// notes, the room's vote and listening along - the three things the people
/// in the room or at home can send in or hear through the club's own pages.
///
/// MOVED FROM THE COMMENTARY PAGE, where they were three cards of their own -
/// the Voice regroup left AI Commentary with what the AI writes, and these
/// are about the club's web page and Wi-Fi rather than the writing. Same
/// settings, the same status lines and the same rule that switching a relay
/// route OFF is SENT to the relay rather than left for it to infer: the host
/// (MainForm.Pages) joins the three events up to the publisher.
///
/// A card rather than a page, the way ShowTreatmentCard is: Section is
/// sealed, so this builds the card and RelaySettingsControl adds it, and the
/// page's TextSize keeps it in step with the text size. None of it is gated
/// on AI commentary - a club can collect messages all evening with the AI off
/// and read them out itself, and with the AI off listening along still
/// carries the lap calls, flag calls and results reads.
/// </summary>
internal sealed class RoomAndWebCard
{
    private readonly AppSettings _settings;
    private readonly TextSize _textSize;

    // --- Club notes (12 Oct 2026) ---
    //
    // The asides that are not about the racing, and the two ways the room
    // can send one in. Both OFF until a club asks for them: each writes to
    // this PC's disk on a stranger's request, and nothing from either can
    // be said without the race director ticking it off (ClubNotes).
    private readonly CheckBox _chkNotesWifi = new ThemedCheckBox();
    private readonly CheckBox _chkNotesRelay = new ThemedCheckBox();
    private readonly Label _lblNotesWhere = new();

    // --- The room's vote (12 Oct 2026) ---
    //
    // Off until a club asks for it, for the one reason every public route
    // here is: it writes to this PC's disk on a stranger's request. See
    // RoomVote.
    private readonly CheckBox _chkVoteWifi = new ThemedCheckBox();
    private readonly CheckBox _chkVoteRelay = new ThemedCheckBox();
    private readonly Label _lblVoteWhere = new();

    // --- Listening along (13 Oct 2026) ---
    private readonly CheckBox _chkListenAlong = new ThemedCheckBox();
    private readonly Label _lblListenAlong = new();

    /// <summary>Set while the boxes are put back to what settings hold, so that is not taken for a change - see RefreshAll.</summary>
    private bool _loading;

    /// <summary>
    /// Raised when the club switches shout-outs from its web page OFF, so
    /// MainForm can tell the relay to stop taking them and throw away
    /// whatever it is holding - see RelayPublisher.CloseClubNotesOnTheRelay,
    /// which also explains why switching it off has to be SENT rather than
    /// inferred from the polling stopping.
    /// </summary>
    internal event Action? NotesClosedOnTheRelay;

    /// <summary>The mirror of <see cref="NotesClosedOnTheRelay"/>, for the relay's vote route.</summary>
    internal event Action? VoteClosedOnTheRelay;

    /// <summary>
    /// Raised when the club switches listening along OFF, so the relay is
    /// told to stop offering the announcer and to throw away the lines it is
    /// holding. The most important of the three to actually send: the other
    /// two leave the relay collecting things nobody reads; this one leaves
    /// the club's own announcer - possibly a named member's cloned voice -
    /// playing to anybody with the link. See RelayPublisher.CloseListeningOnTheRelay.
    /// </summary>
    internal event Action? ListeningClosedOnTheRelay;

    /// <summary>The card itself, for the host to put on its page.</summary>
    public Section Card { get; }

    public RoomAndWebCard(AppSettings settings, TextSize textSize)
    {
        _settings = settings;
        _textSize = textSize;

        Card = new Section("Room & Web");

        Build();
    }

    // A line of help in the small muted type the app's hints use, wrapping
    // to the room it is given.
    private Label Hint(string text) => _textSize.Wear(
        new Label { Text = text, AutoSize = true, ForeColor = AppTheme.TextMuted },
        () => AppTheme.SmallFont);

    // A heading inside the card, in the bold face, for each of the three.
    private Label Heading(string text) => _textSize.Wear(
        new Label { Text = text, AutoSize = true, ForeColor = AppTheme.TextDark },
        () => AppTheme.BoldFont);

    private static CheckBox Check(CheckBox box, string text)
    {
        box.Text = text;
        box.AutoSize = true;
        return box;
    }

    private void Build()
    {
        foreach (var label in new[] { _lblNotesWhere, _lblVoteWhere, _lblListenAlong })
        {
            label.AutoSize = true;
            label.ForeColor = AppTheme.TextMuted;
            _textSize.Wear(label, () => AppTheme.SmallFont);
        }

        // ------------------------------------------------------------------
        // Club notes (12 Oct 2026)
        // ------------------------------------------------------------------

        Check(_chkNotesWifi, "Take them from phones on the club's Wi-Fi");
        _chkNotesWifi.Checked = _settings.ClubNotesFromTheRoom;
        _chkNotesWifi.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.ClubNotesFromTheRoom = _chkNotesWifi.Checked;
            _settings.Save();
            DescribeNoteRoutes();
        };

        Check(_chkNotesRelay, "Take them from the club's own web page");
        _chkNotesRelay.Checked = _settings.ClubNotesFromTheRelay;
        _chkNotesRelay.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.ClubNotesFromTheRelay = _chkNotesRelay.Checked;
            _settings.Save();

            // THE RELAY IS TOLD WHEN IT IS SWITCHED OFF, not left to work
            // it out. The publisher stops polling the moment this is
            // unticked, so without this the relay would go on taking
            // strangers' messages for a club that had declined them - and
            // holding them. See RelayPublisher.CloseClubNotesOnTheRelay.
            if (!_chkNotesRelay.Checked)
            {
                NotesClosedOnTheRelay?.Invoke();
            }

            DescribeNoteRoutes();
        };

        DescribeNoteRoutes();

        // ------------------------------------------------------------------
        // The room's vote (12 Oct 2026)
        // ------------------------------------------------------------------

        Check(_chkVoteWifi, "Let phones on the club's Wi-Fi vote");
        _chkVoteWifi.Checked = _settings.RoomVoteFromTheRoom;
        _chkVoteWifi.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.RoomVoteFromTheRoom = _chkVoteWifi.Checked;
            _settings.Save();
            DescribeVoteRoutes();
        };

        Check(_chkVoteRelay, "Let the club's own web page vote");
        _chkVoteRelay.Checked = _settings.RoomVoteFromTheRelay;
        _chkVoteRelay.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.RoomVoteFromTheRelay = _chkVoteRelay.Checked;
            _settings.Save();

            // THE RELAY IS TOLD WHEN IT IS SWITCHED OFF, exactly as the
            // notes route is. See RelayPublisher.CloseRoomVoteOnTheRelay.
            if (!_chkVoteRelay.Checked)
            {
                VoteClosedOnTheRelay?.Invoke();
            }

            DescribeVoteRoutes();
        };

        DescribeVoteRoutes();

        // ------------------------------------------------------------------
        // Listening along (13 Oct 2026)
        // ------------------------------------------------------------------

        Check(_chkListenAlong, "Let people listen along on the club's web page");
        _chkListenAlong.Checked = _settings.ListenAlongOnTheRelay;
        _chkListenAlong.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.ListenAlongOnTheRelay = _chkListenAlong.Checked;
            _settings.Save();

            // SENT when it goes off, not inferred from the announcer going
            // quiet. Without this the relay goes on offering the club's own
            // voice to anybody with the address. See
            // RelayPublisher.CloseListeningOnTheRelay.
            if (!_chkListenAlong.Checked)
            {
                ListeningClosedOnTheRelay?.Invoke();
            }

            DescribeListening();
        };

        DescribeListening();

        // ------------------------------------------------------------------
        // The card
        // ------------------------------------------------------------------

        Card.Add(Hint(
            "What the room, and people at home, can send in or hear through the club's Wi-Fi and web page - all off "
            + "until you switch them on. With AI commentary off, you read the notes and the vote out yourself."),
            gapBelow: UiLayout.SectionGap);

        Card.Add(Heading("Club notes"), stretch: false, gapBelow: UiLayout.HintGap);
        Card.Add(Hint(
            "A dedication, a birthday, the raffle - typed on the Notes button in the Broadcast window and "
            + "said once between races. The room can send them in too."));
        Card.Add(_chkNotesWifi, stretch: false, gapBelow: 0);
        Card.Add(_chkNotesRelay, stretch: false, gapBelow: 0);
        Card.Add(_lblNotesWhere, gapBelow: UiLayout.SectionGap);

        Card.Add(Heading("The room's vote"), stretch: false, gapBelow: UiLayout.HintGap);

        // Bug hunt 14 (7 Nov 2026): it said the vote closes when the result
        // comes in. It closes at that race's green (RoomVote.StartedRace) -
        // a prediction gathered after the flag is not one - and the result
        // is only the backstop. RoomVoteForm's blurb had it right.
        Card.Add(Hint(
            "The room votes on who wins, and after the race the announcer says how it did. Open a vote on the Votes "
            + "button in the Broadcast window; it closes when that race goes green (or when its result is filed, "
            + "if that comes first). The announcer stays quiet "
            + "if too few voted or the room split, and never says who picked whom."));
        Card.Add(_chkVoteWifi, stretch: false, gapBelow: 0);
        Card.Add(_chkVoteRelay, stretch: false, gapBelow: 0);
        Card.Add(_lblVoteWhere, gapBelow: UiLayout.SectionGap);

        Card.Add(Heading("Listening along"), stretch: false, gapBelow: UiLayout.HintGap);
        Card.Add(Hint(
            "The race called on your club's web page, a few seconds behind the room - listeners just press Listen. "
            + "This puts your chosen voice, even one cloned from somebody at the club, on an address anyone with the "
            + "link can play. Switch it off and the button goes."));
        Card.Add(_chkListenAlong, stretch: false, gapBelow: 0);
        Card.Add(_lblListenAlong, gapBelow: 0);
    }

    /// <summary>
    /// Puts the boxes back to what settings hold, and says again what each
    /// means - when the page is navigated to. Nothing is saved and nothing
    /// is sent to the relay for it: only a box the club changes does that.
    /// </summary>
    internal void RefreshAll()
    {
        _loading = true;

        try
        {
            Set(_chkNotesWifi, _settings.ClubNotesFromTheRoom);
            Set(_chkNotesRelay, _settings.ClubNotesFromTheRelay);
            Set(_chkVoteWifi, _settings.RoomVoteFromTheRoom);
            Set(_chkVoteRelay, _settings.RoomVoteFromTheRelay);
            Set(_chkListenAlong, _settings.ListenAlongOnTheRelay);
        }
        finally
        {
            _loading = false;
        }

        DescribeNoteRoutes();
        DescribeVoteRoutes();
        DescribeListening();

        static void Set(CheckBox box, bool value)
        {
            if (box.Checked != value)
            {
                box.Checked = value;
            }
        }
    }

    /// <summary>
    /// What the listening box means right now, under the box.
    ///
    /// Says the TWO things a club gets wrong about this: that the club
    /// website has to be switched on as well (this rides on the relay, and
    /// a club can have the announcer without the relay), and that what goes
    /// out is whatever voice is chosen on the Voice tab. Asked again every
    /// time the page's status is (RelaySettingsControl.RefreshStatus), since
    /// the club website is switched on and off on the same page - and only
    /// written when the sentence changes, so the card is not laid out again
    /// once a second for nothing.
    /// </summary>
    internal void DescribeListening()
    {
        string said;

        if (!_chkListenAlong.Checked)
        {
            said = "Nobody can listen in at the moment. The club's page shows the results as usual.";
        }
        else
        {
            said = _settings.RelayEnabled
                ? "A Listen button appears on your club's page while the announcer is speaking. It plays the last few "
                  + "lines in whatever voice you have chosen, and it starts from whatever is being said when they press it."
                : "This needs your club website switched on as well - the Relay card at the top of this page. Until then "
                  + "nothing is sent and nobody can listen.";
        }

        if (!string.Equals(_lblListenAlong.Text, said, StringComparison.Ordinal))
        {
            _lblListenAlong.Text = said;
        }
    }

    /// <summary>
    /// Says where the room can send a note from, and what the address is -
    /// which is the part a club actually needs, because an address nobody
    /// is given is a route nobody uses.
    /// </summary>
    private void DescribeNoteRoutes()
    {
        var wifi = _chkNotesWifi.Checked;
        var relay = _chkNotesRelay.Checked;

        if (!wifi && !relay)
        {
            _lblNotesWhere.Text = "Only you can add one at the moment, on the Notes button in the Broadcast window.";
            return;
        }

        var said = new List<string>();

        if (wifi)
        {
            // The same address Local Screens gives out for the live results,
            // with the page's own path on the end. Said rather than linked
            // because somebody is going to write it on a whiteboard.
            said.Add($"on the club Wi-Fi at {WebServerService.NotesPath} under this PC's own address");
        }

        if (relay)
        {
            said.Add("from your club's web page, for the people who could not make it");
        }

        _lblNotesWhere.Text = "Messages can come in " + string.Join(", and ", said)
            + ". Every one waits on Race Control until you approve it.";
    }

    /// <summary>
    /// Where the room votes, said rather than linked - somebody is going to
    /// write it on a whiteboard.
    /// </summary>
    private void DescribeVoteRoutes()
    {
        var wifi = _chkVoteWifi.Checked;
        var relay = _chkVoteRelay.Checked;

        if (!wifi && !relay)
        {
            _lblVoteWhere.Text = "Nobody can vote at the moment. You can still open a vote on Race Control to see "
                + "the grid, but no phone has a way in.";
            return;
        }

        var said = new List<string>();

        if (wifi)
        {
            said.Add($"on the club Wi-Fi at {WebServerService.VotePath} under this PC's own address");
        }

        if (relay)
        {
            said.Add("from your club's web page, for the people who could not make it");
        }

        _lblVoteWhere.Text = "The room can vote " + string.Join(", and ", said)
            + ". No running tally is ever shown to a phone, so nobody votes with the crowd.";
    }
}
