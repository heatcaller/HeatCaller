namespace HeatCaller;

/// <summary>
/// Tonight's room vote in the Desk Inbox: a card pinned above the list, not
/// a row in it - a vote is not a request but something live that needs
/// closing, so the card shows the tally moving and the one button that
/// matters. What the room vote window did (2 Nov 2026 onwards), moved here.
///
/// The race director's whole job here is knowing when to close it, and that
/// is "has the room finished voting" - a number that has to be moving, so
/// the tally is redrawn every second, in place (a grid long enough to scroll
/// must not be thrown back to the top each time).
/// </summary>
internal sealed partial class DeskInboxForm
{
    private readonly Section _voteCard = new("Room vote");
    private readonly Label _lblVote = new();
    private readonly Label _lblVoteWhere = new();
    private readonly Button _btnOpenVote = new() { Text = "Open voting" };
    private readonly Button _btnStopVote = new() { Text = "Stop voting" };
    private readonly Button _btnTally = new() { Text = "Show the tally" };
    private readonly DataGridView _tally = new();

    /// <summary>The race the card offers to open a vote on, as Race Control last said.</summary>
    private VoteTarget? _target;

    private void BuildVoteCard()
    {
        _lblVote.AutoSize = true;
        _lblVote.MaximumSize = new Size(UiLayout.AtTextSize(940), 0);

        _lblVoteWhere.AutoSize = true;
        _lblVoteWhere.MaximumSize = new Size(UiLayout.AtTextSize(940), 0);
        _lblVoteWhere.ForeColor = AppTheme.TextMuted;
        _lblVoteWhere.Font = AppTheme.SmallFont;

        SetUpGrid(_tally);
        _tally.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 160 },
            new DataGridViewTextBoxColumn { Name = "votes", HeaderText = "Votes", FillWeight = 50 },
            new DataGridViewTextBoxColumn { Name = "share", HeaderText = "Share", FillWeight = 50 });
        _tally.Height = _tally.ColumnHeadersHeight + 6 * _tally.RowTemplate.Height + 2;
        _tally.Visible = false;

        // SAYS WHAT RACE, not just "Open" - the thing that goes wrong with a
        // vote is opening it on the wrong race. "Stop voting", not "Close":
        // one ends the poll, the window's Close only shuts the window.
        _btnOpenVote.Click += (_, _) => OpenVoting();
        _btnStopVote.Click += (_, _) => StopVoting();
        _btnTally.Click += (_, _) =>
        {
            _tally.Visible = !_tally.Visible;
            _btnTally.Text = _tally.Visible ? "Hide the tally" : "Show the tally";
            _page.PerformLayout();
        };

        _voteCard.Add(_lblVote, gapBelow: UiLayout.HintGap);
        _voteCard.Add(_lblVoteWhere, gapBelow: UiLayout.HintGap);
        _voteCard.Add(new ButtonBar().With(_btnOpenVote, _btnStopVote, _btnTally), gapBelow: UiLayout.HintGap);
        _voteCard.Add(_tally, gapBelow: 0);
        _voteCard.Visible = _vote is not null;
    }

    /// <summary>The card from the vote, every second: the state, the top of the tally, and what may be pressed.</summary>
    private void FillVoteCard()
    {
        if (_vote is not { } vote)
        {
            return;
        }

        _target = _voteTarget?.Invoke();

        var standings = vote.Standing();
        var taking = vote.Taking;

        string text;

        if (vote.OnRace is { Length: > 0 } onRace)
        {
            var howMany = Plural.Of(vote.Votes, "vote");
            var top = string.Join(", ", standings.Where(s => s.Votes > 0).Take(3).Select(s => $"{s.Name} {s.Votes}"));

            text = taking
                ? $"Voting is OPEN on {onRace} - {howMany} in{(top.Length > 0 ? $": {top}" : ".")}"
                : $"Voting on {onRace} is closed - {howMany} in{(top.Length > 0 ? $": {top}" : ".")}";
        }
        else
        {
            text = "No vote has been opened tonight. The room votes on who wins, and after the race the announcer says how the room did.";
        }

        if (_lblVote.Text != text)
        {
            _lblVote.Text = text;
        }

        _lblVote.ForeColor = taking ? AppTheme.Accent : AppTheme.TextDark;
        _lblVote.Font = taking ? AppTheme.BoldFont : AppTheme.BodyFont;

        var where = string.IsNullOrWhiteSpace(_target?.WhereToVote)
            ? "Voting from the room is switched off - turn it on under The room's vote on the Club Website page, and the address to read out appears here."
            : $"The room votes at {_target!.WhereToVote} on the club's Wi-Fi. It stops itself at the start and when the result comes in.";

        if (_lblVoteWhere.Text != where)
        {
            _lblVoteWhere.Text = where;
        }

        // In place while the grid is the same size (the vote window's fix).
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

        var canOpen = _target is { RaceKey.Length: > 0, Grid.Count: >= 2 };
        var openText = canOpen && _target!.RaceName is { Length: > 0 } name ? $"Open voting on {name}" : "Open voting";

        if (_btnOpenVote.Text != openText)
        {
            _btnOpenVote.Text = openText;
        }

        // Enabled even while one is open: opening a vote on the next race is
        // how a club moves on, and RoomVote closes the one before it. Never
        // with no race picked or nobody on the grid.
        _btnOpenVote.Enabled = canOpen;
        _btnStopVote.Enabled = taking;
    }

    private void OpenVoting()
    {
        if (_vote is not { } vote || _target is not { } target)
        {
            return;
        }

        // A stray click must not throw away every vote cast on the race
        // already open (17 Oct 2026, Bug Hunt 8, A29-3) - asked first.
        if (string.Equals(vote.OnRaceKey, target.RaceKey, StringComparison.Ordinal)
            && vote.Votes > 0
            && AppDialog.Show(this,
                $"Start the vote on {target.RaceName} again? The {Plural.Of(vote.Votes, "vote")} already in will be thrown away.",
                "Start again", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        if (vote.OpenFor(target.RaceKey, target.RaceName, target.ClassName, target.Grid) is { } why)
        {
            AppDialog.Show(this, why, "Can't open the vote", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        FillVoteCard();
    }

    private void StopVoting()
    {
        _vote?.Close();
        FillVoteCard();
    }
}
