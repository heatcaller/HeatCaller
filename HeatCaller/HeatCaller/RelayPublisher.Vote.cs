using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// The room's vote, taken in from the relay as well as from the hall -
/// the other half of the room, again.
///
/// WHY BOTH ROUTES EXIST (12 Oct 2026)
///
/// The same argument the shout-outs make: HeatCaller's own web server
/// covers every phone in the building, and this covers the people who are
/// not in it. A prediction from a driver's family watching the live
/// results at home is as good a prediction as one from the bank, and the
/// announcer's line is better the more of the room is in it.
///
/// THE SHAPE: PUBLISH THE BALLOT, THEN READ THE VOTES
///
/// This is NOT the collect-file-confirm loop the notes use, and the
/// difference is the whole of it: a note is said once, so collecting it is
/// the end of it. A vote can be CHANGED - somebody taps a different name
/// two minutes later - so a drained inbox would lose the change and leave
/// the announcer counting a pick its owner had replaced.
///
/// So the relay keeps the votes for the race it is holding a ballot for,
/// and this reads the whole set every poll. That is free: RoomVote.Cast
/// holds one vote per voter, so re-applying a pick somebody already has
/// does nothing at all, and <see cref="RoomVote.PickOf"/> means the file is
/// not even rewritten for it.
///
/// AND NOTHING FROM THIS ROUTE CAN BE A WORD THE ANNOUNCER SAYS
///
/// Unlike a shout-out, there is no approval gate here and none is needed.
/// The relay refuses any driver id that is not on the ballot this laptop
/// published, and RoomVote refuses one that is not on the grid, so the
/// worst anybody with the club's link can achieve is being wrong about who
/// wins.
/// </summary>
public sealed partial class RelayPublisher
{
    /// <summary>
    /// How often the vote inbox is read.
    ///
    /// Fifteen seconds, against the notes' thirty. A dedication can wait
    /// half a minute; a vote cannot, because the number the race director
    /// is watching to decide when to close the vote has to be moving. The
    /// window itself is only a few minutes long.
    /// </summary>
    private static readonly TimeSpan VotePollEvery = TimeSpan.FromSeconds(15);

    /// <summary>When the vote inbox is next due - its own tick.</summary>
    private long _nextVotePollTick;

    /// <summary>What went wrong with the vote, for the page to show. Null while it is working.</summary>
    private volatile string? _votePollProblem;

    /// <summary>
    /// The ballot last published, as a fingerprint.
    ///
    /// So the ballot is sent when it CHANGES rather than every poll: a
    /// request per poll carrying a grid that has not moved would be most
    /// of what this feature cost on the wire.
    /// </summary>
    private volatile string? _ballotAsPublished;

    /// <summary>What went wrong taking the room's vote from the relay, or null.</summary>
    public string? VoteProblem => _votePollProblem;

    /// <summary>The problem last said out loud, so it is said once rather than every fifteen seconds.</summary>
    private volatile string? _voteProblemAsSaid;

    /// <summary>
    /// Records what went wrong and says it once.
    ///
    /// SAID, not merely recorded. A property nothing reads is how a route
    /// whose whole failure mode is "the votes silently never arrive" ends
    /// up undiagnosable on a race night - and the console is where a club
    /// looks afterwards. Only on a change, so a relay that is down does
    /// not fill the log.
    /// </summary>
    /// <summary>
    /// Clears the problem without saying anything, for the club switching
    /// the route off - which is not a fault and must not log "working
    /// again" on its way out.
    /// </summary>
    private void ForgetVoteTrouble()
    {
        _votePollProblem = null;
        _voteProblemAsSaid = null;
    }

    private void VoteTrouble(string? problem)
    {
        _votePollProblem = problem;

        if (string.Equals(_voteProblemAsSaid, problem, StringComparison.Ordinal))
        {
            return;
        }

        _voteProblemAsSaid = problem;

        if (problem is { Length: > 0 })
        {
            Log?.Invoke($"[show] The room's vote on the club's page: {problem}");
        }
        else
        {
            Log?.Invoke("[show] The room's vote on the club's page is working again.");
        }
    }

    /// <summary>
    /// Tonight's vote. Set by MainForm to the one RoomVote instance the
    /// announcer reads its verdict from; null until then, and then nothing
    /// is published or collected.
    /// </summary>
    /// <remarks>Internal because <see cref="RoomVote"/> is - CS0053 on a public property.</remarks>
    internal Func<RoomVote?>? TonightsVote { get; set; }

    /// <summary>
    /// What a voter from the relay is called in the store.
    ///
    /// Prefixed so a relay voter can never collide with a key the Wi-Fi
    /// route made from a client address - the two routes write into one
    /// list of votes, and a collision would be one person's vote replacing
    /// another's.
    /// </summary>
    private const string RelayVoterMark = "relay:";

    /// <summary>
    /// Keeps the relay's ballot in step with tonight's vote, and reads
    /// whatever has been voted.
    ///
    /// On the publisher's own background pass, like every other collector
    /// here, so nothing on this path touches the UI thread.
    /// </summary>
    private void CollectRoomVotes(CancellationToken token)
    {
        if (TonightsVote?.Invoke() is not { } vote)
        {
            return;
        }

        var target = _target;
        var club = Uri.EscapeDataString(target.Club);

        if (!IsSafeForToken(target.Root))
        {
            VoteTrouble(UnsafeAddressMessage);
            return;
        }

        if (!PublishTheBallot(target, club, vote, enabled: true, token))
        {
            return;
        }

        // NOTHING TO READ WHILE NO VOTE IS OPEN. The relay would answer
        // with an empty set anyway, and a club that has the switch on but
        // has not opened a vote tonight should not be making a request
        // every fifteen seconds for it.
        if (vote.OnRace is not { Length: > 0 })
        {
            VoteTrouble(null);
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{target.Root}/api/v1/c/{club}/vote/inbox");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);

            using var response = Send(request, token);

            if (!response.IsSuccessStatusCode)
            {
                VoteTrouble((int)response.StatusCode == 404
                    ? "The relay answered \"not found\" for the room's vote. Check the club id on the Club Website "
                      + "page matches the relay's; if it does, the relay predates the vote - redeploy it "
                      + "(fly deploy --ha=false)."
                    : $"Could not read the room's vote: the relay answered HTTP {(int)response.StatusCode}.");

                return;
            }

            var text = response.Content.ReadAsStringAsync(token).GetAwaiter().GetResult();

            var added = 0;
            var changed = 0;
            var refused = 0;

            using (var doc = JsonDocument.Parse(text))
            {
                if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        if (!item.TryGetProperty("v", out var voter) || voter.ValueKind != JsonValueKind.String
                            || !item.TryGetProperty("d", out var picked) || picked.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        var who = $"{RelayVoterMark}{voter.GetString()}";
                        var driver = picked.GetString();

                        // ALREADY HELD, so nothing to do. This is why
                        // re-reading the whole set every fifteen seconds
                        // costs nothing: on a settled vote every pass
                        // falls through here and the file is never
                        // rewritten.
                        if (string.Equals(vote.PickOf(who), driver, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var hadOne = vote.PickOf(who) is not null;

                        // Refused by the store - voting closed, or a
                        // driver no longer on the grid. Left on the relay,
                        // which is right: it is refused again next pass
                        // for the same reason, and it costs nothing.
                        if (vote.Cast(who, driver) is not null)
                        {
                            refused++;
                        }
                        else if (hadOne)
                        {
                            changed++;
                        }
                        else
                        {
                            added++;
                        }
                    }
                }
            }

            VoteTrouble(null);

            if (added == 0 && changed == 0)
            {
                return;
            }

            // Through Log, which is this class's one way of saying
            // anything - it has no StatusMessage of its own.
            Log?.Invoke(
                $"[show] {Plural.Of(added, "vote")} from the club's page"
                + (changed > 0 ? $", and {changed} changed" : string.Empty)
                + (refused > 0 ? $", and {refused} the vote had no room for" : string.Empty)
                + $". {Plural.Of(vote.Votes, "vote")} in on {vote.OnRace}.");
        }
        catch (OperationCanceledException)
        {
            // Shutting down, or the pass was cancelled. Nothing to report.
        }
        catch (Exception ex)
        {
            VoteTrouble($"Could not read the room's vote: {ex.Message}");
        }
    }

    /// <summary>
    /// Publishes the ballot - whether the club wants this, whether a vote
    /// is running, and the grid to pick from.
    ///
    /// SENT WHEN IT CHANGES, not every poll, which is what
    /// <see cref="_ballotAsPublished"/> is for. The relay keeps it, and the
    /// grid moves about once a race.
    ///
    /// False when it could not be sent, and then nothing is read - the
    /// relay would have the wrong grid, and a vote against the wrong grid
    /// is worse than no vote.
    /// </summary>
    private bool PublishTheBallot(
        RelayTarget target,
        string club,
        RoomVote vote,
        bool enabled,
        CancellationToken token)
    {
        var open = enabled && vote.Taking;
        var race = vote.OnRace ?? string.Empty;

        // Built rather than selected into a ternary: one branch would be
        // an array of an anonymous type and the other an array of object,
        // and leaving the compiler to reconcile those is the sort of line
        // that works until somebody edits it.
        var drivers = new List<object>();

        if (open)
        {
            foreach (var one in vote.Options)
            {
                drivers.Add(new { id = one.Id, name = one.Name });
            }
        }

        // THE RACE IS IN THE FINGERPRINT, so moving to the next race is
        // always a publish - which is what tells the relay to throw away
        // the race before's votes.
        var fingerprint = $"{enabled}|{open}|{race}|{string.Join(",", vote.Options.Select(one => one.Id))}";

        if (string.Equals(_ballotAsPublished, fingerprint, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{target.Root}/api/v1/c/{club}/vote/open");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { enabled, open, race, drivers }),
                Encoding.UTF8,
                "application/json");

            using var response = Send(request, token);

            if (!response.IsSuccessStatusCode)
            {
                VoteTrouble((int)response.StatusCode == 404
                    ? "The relay does not know about the room's vote yet - redeploy it (fly deploy --ha=false)."
                    : $"Could not publish the vote to the relay: it answered HTTP {(int)response.StatusCode}.");

                return false;
            }

            _ballotAsPublished = fingerprint;

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            VoteTrouble($"Could not reach the relay about the room's vote: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Tells the relay the club no longer wants the room's vote, which
    /// also throws away anything it was holding.
    ///
    /// Called when the setting is switched off rather than left to the
    /// poll loop, because the poll loop stops asking the moment it is off
    /// - so without this the relay would go on taking votes for a club
    /// that had declined them.
    /// </summary>
    public void CloseRoomVoteOnTheRelay()
    {
        var target = _target;

        if (!IsSafeForToken(target.Root))
        {
            return;
        }

        // Nothing to close if the relay was never told it was open.
        if (_ballotAsPublished is null)
        {
            return;
        }

        if (TonightsVote?.Invoke() is not { } vote)
        {
            return;
        }

        var club = Uri.EscapeDataString(target.Club);

        // On a thread of its own: this is called from a settings page, and
        // a page that waited for a network round trip to redraw a tick box
        // is a page that appears to have frozen.
        Task.Run(() => PublishTheBallot(target, club, vote, enabled: false, CancellationToken.None));
    }
}
