namespace HeatCaller;

/// <summary>
/// Tonight's room vote: opening one on the race about to run, watching it
/// fill, and closing it.
///
/// A WINDOW RATHER THAN A CARD, and modal, for the reason the club notes
/// window gives at length: it is opened between races, used for a few
/// seconds and closed, and a card would take permanent room on the busiest
/// screen in the app to show an empty tally on most nights.
///
/// WHY THE TALLY REFRESHES WHILE IT IS OPEN
///
/// The whole of the race director's job here is knowing when to close it,
/// and that decision is "has the room finished voting" - which is a number
/// that has to be moving for them to read it. A window showing the count
/// as it stood when they opened it would have them closing a vote with
/// half the room still in it.
/// </summary>
internal sealed class RoomVoteForm : Form
{
    /// <summary>The tally shows its headings and at least this many drivers before the window scrolls.</summary>
    private const int LeastRows = 6;

    /// <summary>
    /// How often the tally is redrawn while the window is open.
    ///
    /// NOT CALLED "Refresh", which is what it shipped as: a member of that
    /// name on a Form hides Control.Refresh(), so `this.Refresh()` inside
    /// this class would have resolved to a TimeSpan and nothing in here
    /// could repaint itself. CS0108, and the compiler was right.
    /// </summary>
    private static readonly TimeSpan TallyEvery = TimeSpan.FromSeconds(1);

    private readonly RoomVote _vote;

    private readonly string? _raceKey;
    private readonly string? _raceName;
    private readonly string? _className;
    private readonly IReadOnlyList<VoteOption> _grid;

    private readonly DataGridView _tally = new();
    private readonly Label _lblState = new();
    private readonly Button _btnOpen = new();
    private readonly Button _btnClose = new();
    private readonly System.Windows.Forms.Timer _tick = new();

    /// <param name="vote">Tonight's vote - the one instance the phones vote in and the announcer reads.</param>
    /// <param name="raceKey">The round about to run, by its own id. Null when Race Control has no round picked.</param>
    /// <param name="raceName">Its label, for the window and for the announcer to say.</param>
    /// <param name="className">Its class.</param>
    /// <param name="grid">The drivers staged for it.</param>
    /// <param name="whereToVote">The address to read out to the room, when the Wi-Fi route is switched on.</param>
    public RoomVoteForm(
        RoomVote vote,
        string? raceKey,
        string? raceName,
        string? className,
        IReadOnlyList<VoteOption> grid,
        string? whereToVote)
    {
        _vote = vote;
        _raceKey = raceKey;
        _raceName = raceName;
        _className = className;
        _grid = grid;

        Text = "Tonight's room vote";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var blurb = new Label
        {
            Text = "The room votes on who wins, and after the race the announcer says how the room did - even that "
                   + "nobody picked the winner. Open it when the race is called to the grid; it stops itself at the "
                   + "start and when the result comes in. The announcer stays quiet unless enough people voted.",
            AutoSize = true,
            MaximumSize = new Size(UiLayout.AtTextSize(640), 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        var where = new Label
        {
            Text = string.IsNullOrWhiteSpace(whereToVote)
                ? "Voting from the room is switched off - turn it on under The room's vote on the Club Website page, and "
                  + "the address to read out appears here."
                : $"The room votes at {whereToVote} on the club's Wi-Fi.",
            AutoSize = true,
            MaximumSize = new Size(UiLayout.AtTextSize(640), 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        _tally.ReadOnly = true;
        _tally.AllowUserToAddRows = false;
        _tally.AllowUserToDeleteRows = false;
        _tally.AllowUserToResizeRows = false;
        _tally.RowHeadersVisible = false;
        _tally.MultiSelect = false;
        _tally.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _tally.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        _tally.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 160 },
            new DataGridViewTextBoxColumn { Name = "votes", HeaderText = "Votes", FillWeight = 50 },
            new DataGridViewTextBoxColumn { Name = "share", HeaderText = "Share", FillWeight = 50 });

        // SAYS WHAT RACE, not just "Open". The thing that goes wrong with
        // a vote is opening it on the wrong race, and the button is where
        // somebody would notice.
        _btnOpen.Click += (_, _) => OpenVoting();

        // "Stop voting" and "Done", not "Close voting" beside "Close": side
        // by side, one word apart, the two were easy to mix up - one ends the
        // poll, the other only shuts this window.
        _btnClose.Text = "Stop voting";
        _btnClose.Click += (_, _) => CloseVoting();

        var btnDone = new Button { Text = "Done", DialogResult = DialogResult.OK };

        _lblState.AutoSize = true;
        _lblState.ForeColor = AppTheme.TextMuted;
        _lblState.Font = AppTheme.SmallFont;
        _lblState.Anchor = AnchorStyles.Left;

        var footer = new SplitBar(
            new ButtonBar().With(_lblState),
            new ButtonBar(alignRight: true).With(btnDone, _btnClose, _btnOpen));

        _btnOpen.TabIndex = 0;
        _btnClose.TabIndex = 1;
        btnDone.TabIndex = 2;

        var page = new FillPage();
        page.Add(blurb, gapBelow: UiLayout.RowGap);
        page.Add(where, gapBelow: UiLayout.RowGap);
        page.AddFill(_tally, _ => _tally.ColumnHeadersHeight + LeastRows * _tally.RowTemplate.Height + 2, gapBelow: UiLayout.RowGap);
        page.Add(footer, gapBelow: 0);

        Controls.Add(page);

        CancelButton = btnDone;

        AppTheme.ApplyRecursively(this);
        Pieces.KeepHeadingsWhole(_tally);

        Fill();

        _tick.Interval = (int)TallyEvery.TotalMilliseconds;
        _tick.Tick += (_, _) => Fill();
        _tick.Start();

        // Stopped before the window goes, so a tick cannot land on a
        // disposed grid - the same hazard every timer in this app carries.
        FormClosed += (_, _) =>
        {
            _tick.Stop();
            _tick.Dispose();
        };

        DialogFit.Keep(this, page, width: 620, height: 520);
    }

    /// <summary>
    /// Draws the tally and the state line from the store.
    ///
    /// EVERY DRIVER, including the ones nobody has picked. An empty row is
    /// the interesting one before the race and the whole story after it.
    /// </summary>
    private void Fill()
    {
        var standings = _vote.Standing();

        // IN PLACE while the grid is the same size. This runs every second,
        // and clearing the rows each time threw the tally back to the top -
        // so on a grid long enough to scroll, the drivers nobody picked
        // could not be read while the vote was open.
        if (_tally.Rows.Count == standings.Count)
        {
            for (var i = 0; i < standings.Count; i++)
            {
                var cells = _tally.Rows[i].Cells;

                cells[0].Value = standings[i].Name;
                cells[1].Value = standings[i].Votes.ToString();
                cells[2].Value = $"{standings[i].Share}%";
            }
        }
        else
        {
            _tally.Rows.Clear();

            foreach (var standing in standings)
            {
                _tally.Rows.Add(standing.Name, standing.Votes.ToString(), $"{standing.Share}%");
            }
        }

        var taking = _vote.Taking;

        if (_vote.OnRace is { Length: > 0 } onRace)
        {
            var howMany = Plural.Of(_vote.Votes, "vote");

            _lblState.Text = taking
                ? $"Voting is OPEN on {onRace} - {howMany} in."
                : $"Voting on {onRace} is closed - {howMany} in.";
        }
        else
        {
            _lblState.Text = "No vote has been opened tonight.";
        }

        _lblState.ForeColor = taking ? AppTheme.Accent : AppTheme.TextMuted;

        var canOpen = _raceKey is { Length: > 0 } && _grid.Count >= 2;

        _btnOpen.Text = canOpen && _raceName is { Length: > 0 } name
            ? $"Open voting on {name}"
            : "Open voting";

        // ENABLED EVEN WHILE ONE IS OPEN, because opening a vote on the
        // next race is how a club moves on - RoomVote closes the one
        // before it. What it must not do is offer to open one with no race
        // picked or nobody on the grid.
        _btnOpen.Enabled = canOpen;

        _btnClose.Enabled = taking;
    }

    private void OpenVoting()
    {
        // 17 Oct 2026, Bug Hunt 8, A29-3: the button stays enabled (and
        // has the focus when the window reopens) while this same race is
        // already taking votes, and OpenFor always starts an empty poll -
        // so a stray click, Enter or Space threw away every vote cast with
        // no warning. Restarting is still allowed (after an accidental
        // Stop, say), just asked first.
        if (string.Equals(_vote.OnRaceKey, _raceKey, StringComparison.Ordinal)
            && _vote.Votes > 0
            && AppDialog.Show(
                this,
                $"Start the vote on {_raceName} again? The {Plural.Of(_vote.Votes, "vote")} already in will be thrown away.",
                "Start again",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        if (_vote.OpenFor(_raceKey, _raceName, _className, _grid) is { } why)
        {
            AppDialog.Show(this, why, "Can't open the vote", MessageBoxButtons.OK, MessageBoxIcon.Information);

            return;
        }

        Fill();
    }

    private void CloseVoting()
    {
        _vote.Close();

        Fill();
    }
}
