using System.Net.Http.Headers;
using System.Security.Cryptography;
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

    /// <summary>
    /// Whether the switch was turned off and the relay has not yet taken
    /// the close. Cleared only when it has - see <see cref="SendVoteClose"/>.
    /// </summary>
    private volatile bool _voteClosePending;

    /// <summary>
    /// The poll id the relay's ballot was last published for, so the votes
    /// read back from it are cast into that poll and no other - see the
    /// expectedPollId on <see cref="RoomVote.Cast"/>.
    /// </summary>
    private volatile string? _pollAsPublished;

    /// <summary>
    /// The key the relay's ballot was last published with (22 Oct 2026,
    /// known issues). The votes read back are matched with THIS key, not
    /// whatever <see cref="RelayVoteKey"/> says by the time they are read:
    /// a check-in key minted or reissued between the publish and the read
    /// changed every id, and that pass's votes matched nobody. The next
    /// pass puts the ballot up again with the new key - the fingerprint
    /// carries its mark - and reads with that.
    /// </summary>
    private volatile string? _ballotKeyAsPublished;

    /// <summary>
    /// The key before that, when a reissue moved it on: phones that voted
    /// on the old ballot before it was replaced are still in the relay's
    /// inbox under the old ids, and are still counted - for that poll
    /// only: a new poll or a close lets it go (see NextKeyBefore).
    /// </summary>
    private volatile string? _ballotKeyBefore;

    /// <summary>
    /// What <see cref="_ballotKeyBefore"/> becomes when a ballot is
    /// published: the key the same open poll went up under before, when the
    /// key has just changed; that key again when nothing has changed since;
    /// otherwise nothing - a new poll, or a vote closed, lets it go.
    /// </summary>
    internal static string? NextKeyBefore(
        string? keyBefore,
        string? keyPublished,
        string key,
        string? pollBefore,
        string poll,
        bool open)
    {
        if (!open || !string.Equals(pollBefore ?? string.Empty, poll, StringComparison.Ordinal))
        {
            return null;
        }

        if (keyPublished is { } published && !string.Equals(published, key, StringComparison.Ordinal))
        {
            return published;
        }

        return keyBefore;
    }

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
    /// The stand-in key for <see cref="RelayVoteKey"/> while the club has no
    /// check-in key yet - made once per run. No initializer: this is a part
    /// file (see the house rule in RelayPublisher.cs).
    /// </summary>
    private static string? _runVoteKey;

    /// <summary>
    /// The key the relay's ballot ids are made with (20 Oct 2026, Bug Hunt 8).
    ///
    /// WHY: the ballot used to go to the relay as each driver's transponder
    /// beside their full name, and the relay hands that ballot to anyone
    /// who asks for /vote/ballot - no token, no PIN. That is the exact
    /// transponder-to-person map /api/state stopped giving out on 9 Oct.
    /// Now each driver goes up under an id made from this key, which the
    /// relay never sees, so it cannot be turned back into a transponder.
    ///
    /// The club's check-in key when there is one: it is kept on disk, so
    /// the ids for an open vote are the same after a restart and the votes
    /// the relay is holding still match. It is only READ here - minting it
    /// saves settings, which is not this worker thread's job. Until a club
    /// has one, a key made for this run stands in; the only cost is that a
    /// restart in the middle of an open vote drops the relay votes cast
    /// before it.
    /// </summary>
    private string RelayVoteKey()
    {
        var secret = _settings.RelayCheckInSecret;

        if (!string.IsNullOrWhiteSpace(secret))
        {
            return secret;
        }

        if (_runVoteKey is null)
        {
            Interlocked.CompareExchange(
                ref _runVoteKey,
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),
                null);
        }

        return _runVoteKey!;
    }

    /// <summary>
    /// A driver's id on the relay's ballot: twenty hex characters, the same
    /// for the whole of one poll and different in the next, so ids cannot
    /// be followed from one race to another either. Case and spacing in the
    /// transponder do not change it.
    /// </summary>
    internal static string RelayVoteId(string pollId, string optionId, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(
            (pollId ?? string.Empty) + "|" + (optionId ?? string.Empty).Trim().ToUpperInvariant()));

        return Convert.ToHexString(hash, 0, 10).ToLowerInvariant();
    }

    /// <summary>
    /// The drivers as the relay's ballot carries them: the relay's id for
    /// each and a name fit for a public page - "Unnamed driver" where the
    /// name was only the transponder standing in for one. Ghosts left out,
    /// as from every document bound for the relay.
    /// </summary>
    /// <param name="abbreviate">
    /// The club's "abbreviate names" setting (22 Oct 2026, bug hunt): the
    /// ballot is a public route like the roster, so it shortens names the
    /// same way - full names went out on it whatever the club had chosen.
    /// </param>
    internal static List<object> RelayBallotDrivers(IEnumerable<VoteOption> options, string pollId, string key, bool abbreviate = false)
    {
        var real = options.Where(one => !Ghosts.IsGhost(one.Id)).ToList();
        var names = RelayPublisher.DisplayNames(
            real.Select(one => RelayDocuments.PublicNameOrUnnamed(one.Name, one.Id)).ToList(),
            abbreviate,
            real.Select(one => (string?)one.Id).ToList());

        var drivers = new List<object>();

        for (var i = 0; i < real.Count; i++)
        {
            drivers.Add(new
            {
                id = RelayVoteId(pollId, real[i].Id, key),
                name = names[i],
            });
        }

        return drivers;
    }

    /// <summary>
    /// What decides whether the ballot goes up again: see PublishTheBallot.
    /// </summary>
    internal static string BallotFingerprint(
        bool enabled,
        bool open,
        string raceKey,
        string race,
        string ballot,
        string key,
        IEnumerable<VoteOption> options,
        bool abbreviate,
        List<object> drivers) =>
        $"{enabled}|{open}|{raceKey}|{race}|{ballot}|{RelayVoteId(string.Empty, string.Empty, key)}|{string.Join(",", options.Select(one => one.Id))}"
        // The name setting too (22 Oct 2026, bug hunt), so changing it
        // puts the ballot up again with the names it asks for.
        + $"|{abbreviate}"
        // AND THE NAMES AS PUBLISHED (9 Nov 2026, bug hunt 15). They come
        // through PublicPrivacy.Current as well as the setting, so a driver
        // ticked "initials only online", or renamed, while the vote was open
        // left the fingerprint as it was - and the public ballot went on
        // showing the full name after every other document had "S.P.".
        + $"|{JsonSerializer.Serialize(drivers)}";

    /// <summary>
    /// The way back: the relay's id for each driver on the ballot, to the
    /// option it stands for. An id that is not here was never on this
    /// poll's ballot and is refused.
    /// </summary>
    internal static Dictionary<string, string> RelayVoteOptions(IEnumerable<VoteOption> options, string pollId, string key)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var one in options)
        {
            if (!Ghosts.IsGhost(one.Id))
            {
                map[RelayVoteId(pollId, one.Id, key)] = one.Id;
            }
        }

        return map;
    }

    /// <summary>
    /// When the relay took a vote, on this laptop's clock, or null when it
    /// did not say (a relay from before it sent "at").
    ///
    /// WHY (20 Oct 2026, Bug Hunt 8): the inbox is read every fifteen
    /// seconds, and a vote the relay took in the last few seconds before
    /// the race director closed the vote - or before the green flag closed
    /// it - was refused here as "closed" and lost for good, though the
    /// voter's phone had been told it counted. With the relay's own stamp,
    /// RoomVote.Cast can count a vote by when it was cast rather than when
    /// it was read. "at" is the relay's clock in milliseconds.
    ///
    /// The relay sends "at" from readRoomVotes, and RoomVote.Cast judges
    /// "still open" by it (20 Oct 2026).
    /// </summary>
    internal static DateTime? RelayVoteCastAt(JsonElement item, TimeSpan relayAhead)
    {
        if (!item.TryGetProperty("at", out var at)
            || at.ValueKind != JsonValueKind.Number
            || !at.TryGetInt64(out var millis))
        {
            return null;
        }

        // Out of DateTimeOffset's range is as good as no stamp at all.
        if (millis < -62135596800000L || millis > 253402300799999L)
        {
            return null;
        }

        return ToLaptopClock(DateTimeOffset.FromUnixTimeMilliseconds(millis).UtcDateTime, relayAhead);
    }

    /// <summary>
    /// Has the next pass deal with the vote now rather than at its
    /// fifteen-second tick - for a vote that has just closed.
    ///
    /// WHY (20 Oct 2026, Bug Hunt 8): the relay goes on taking votes, and
    /// telling each phone it counted, until it is sent the closed ballot -
    /// which used to wait for the next tick, up to fifteen seconds after
    /// the close. Meant to be called when a vote closes, so the closed
    /// ballot goes up at once and a late phone is told "closed" instead.
    /// MainForm hooks this to RoomVote.Closed (20 Oct 2026). Safe from any
    /// thread - it only sets a number and the worker's wake-up, as
    /// CloseRoomVoteOnTheRelay does.
    /// </summary>
    internal void WakeRoomVote()
    {
        _nextVotePollTick = 0;

        // WHY (20 Oct 2026): raised from inside RoomVote.Close and
        // StartedRace, so a throw here would land in the Close button or the
        // green flag. Dispose frees the wake-up once the worker has gone, and
        // a vote closed after that (the vote window left open at exit) has
        // no worker to wake anyway.
        try
        {
            _wake.Set();
        }
        catch (ObjectDisposedException)
        {
        }
    }

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

        // THE POLL THE RELAY'S BALLOT IS FOR, taken now. The relay holds
        // that poll's votes, so they are cast into that poll only: a new
        // vote opened while the inbox is being read is refused them by
        // RoomVote.Cast, and gets its own on the next pass, once its own
        // ballot has been published.
        var polledFor = _pollAsPublished;

        // The relay's ids for the drivers on that poll's ballot, back to
        // the drivers themselves - see RelayVoteKey (20 Oct 2026).
        // With the key that ballot went up with - see _ballotKeyAsPublished.
        var onTheBallot = RelayVoteOptions(vote.Options, polledFor ?? string.Empty, _ballotKeyAsPublished ?? RelayVoteKey());

        if (_ballotKeyBefore is { } oldKey)
        {
            foreach (var (id, driver) in RelayVoteOptions(vote.Options, polledFor ?? string.Empty, oldKey))
            {
                onTheBallot.TryAdd(id, driver);
            }
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
                    // Relay time to laptop time - see RelayClockOffset. Read
                    // once, so every vote in this set is turned by the same
                    // offset (20 Oct 2026, Bug Hunt 8, #22).
                    var relayAhead = RelayClockOffset;

                    foreach (var item in items.EnumerateArray())
                    {
                        if (!item.TryGetProperty("v", out var voter) || voter.ValueKind != JsonValueKind.String
                            || !item.TryGetProperty("d", out var picked) || picked.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        var who = $"{RelayVoterMark}{voter.GetString()}";
                        // WHY (20 Oct 2026): the relay holds the opaque id,
                        // not the transponder - turned back here. One that
                        // is not on this poll's ballot is refused.
                        if (!onTheBallot.TryGetValue(picked.GetString() ?? string.Empty, out var driver))
                        {
                            refused++;
                            continue;
                        }

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
                        // driver no longer on the grid. Left on the relay:
                        // it is refused again next pass for the same
                        // reason, and it costs nothing.
                        //
                        // COUNTED BY WHEN THE RELAY TOOK IT (20 Oct 2026, Bug
                        // Hunt 8, #22): one the relay took in the seconds
                        // before the close, but read here after it, used to
                        // be lost. A relay too old to send "at" gives null,
                        // and Cast falls back to "is it open now".
                        if (vote.Cast(who, driver, polledFor ?? string.Empty, RelayVoteCastAt(item, relayAhead)) is not null)
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
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Shutting down, or the pass was cancelled. Nothing to report.
            //
            // ONLY THEN (7 Nov 2026, Bug Hunt 13). Send's own time limit
            // throws the same exception with the pass token untouched, and
            // that used to land here too - so a relay too slow to answer
            // was never reported, and the Club Website page showed the
            // vote as working while no votes came in. A timeout now falls
            // through to the report below, as the check-ins already did.
        }
        catch (Exception ex)
        {
            VoteTrouble($"Could not read the room's vote: {Plain(ex)}");
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

        // The label, for the page to show people.
        var race = vote.OnRace ?? string.Empty;

        // AND THE ROUND'S OWN ID, which is what the relay decides identity
        // on (13 Oct 2026). Only the label was sent before, so the relay
        // was identifying a race the one way RoomVote.IsTheRace refuses to
        // - and a club running two classes has two rounds both labelled
        // "A-Main". That cost the Mod A-main's ballot opening with the
        // Stock A-main's votes in it, and the per-ballot salt being reused
        // across both, which is what makes two races correlatable by voter.
        var raceKey = vote.OnRaceKey ?? string.Empty;

        // THE POLL'S OWN ID, so the relay can tell a vote reopened on the
        // same race from the same vote. The label cannot: reopening makes
        // a new, empty poll here, and a relay deciding by the label kept
        // the old votes and handed them back into the new one.
        var ballot = vote.OnPollId ?? string.Empty;

        // Built rather than selected into a ternary: one branch would be
        // an array of an anonymous type and the other an array of object,
        // and leaving the compiler to reconcile those is the sort of line
        // that works until somebody edits it.
        var drivers = new List<object>();

        // WHY (20 Oct 2026, Bug Hunt 8): not the transponders. This list is
        // on a public relay route, and it went up as each driver's
        // transponder beside their full name. Opaque ids now, and a name fit
        // for a public page - see RelayVoteKey.
        var key = RelayVoteKey();

        if (open)
        {
            drivers = RelayBallotDrivers(vote.Options, ballot, key, _settings.RelayAbbreviateNames);
        }

        // THE RACE IS IN THE FINGERPRINT, so moving to the next race is
        // always a publish - which is what tells the relay to throw away
        // the race before's votes. And the poll's own id, for a vote
        // reopened on the same race with the same grid.
        //
        // THE KEY AS WELL AS THE LABEL, and the key is the one that
        // matters: two rounds labelled "A-Main" with the same drivers on
        // the grid - one club, one driver, two cars, which is an ordinary
        // evening - produced the same fingerprint, so the ballot was never
        // republished at all and the relay was never told the race had
        // moved. The label stays in it because a round renamed mid-evening
        // has to reach the page.
        //
        // AND THE KEY'S MARK (20 Oct 2026): a key that changes - the club's
        // check-in key minted or reissued - changes every relay id, and the
        // ballot has to go up again with the new ones or no vote from the
        // relay would match. A mark made from the key, not the key itself.
        var fingerprint = BallotFingerprint(enabled, open, raceKey, race, ballot, key, vote.Options, _settings.RelayAbbreviateNames, drivers);

        if (string.Equals(_ballotAsPublished, fingerprint, StringComparison.Ordinal))
        {
            _pollAsPublished = ballot;
            _ballotKeyAsPublished = key;
            return true;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{target.Root}/api/v1/c/{club}/vote/open");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { enabled, open, race, raceKey, ballot, drivers }),
                Encoding.UTF8,
                "application/json");

            using var response = Send(request, token);

            if (!enabled && IsGoneForAClose(response))
            {
                // Nothing there to close (17 Oct 2026, Bug Hunt 8, A16-1):
                // the close is armed on every start now, and an old relay
                // must not log "redeploy" for a switch that is off.
                return true;
            }

            if (!response.IsSuccessStatusCode)
            {
                VoteTrouble((int)response.StatusCode == 404
                    ? "The relay does not know about the room's vote yet - redeploy it (fly deploy --ha=false)."
                    : $"Could not publish the vote to the relay: it answered HTTP {(int)response.StatusCode}.");

                return false;
            }

            var pollBefore = _pollAsPublished;

            _ballotAsPublished = fingerprint;
            _pollAsPublished = ballot;

            // The old key is kept only for the SAME poll, still open, that
            // went up under it: a new poll, or a closed one, has no votes
            // cast against the old ids worth reading, and keeping it would
            // go on counting them for the rest of the run.
            _ballotKeyBefore = NextKeyBefore(_ballotKeyBefore, _ballotKeyAsPublished, key, pollBefore, ballot, open);

            _ballotKeyAsPublished = key;

            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            VoteTrouble($"Could not reach the relay about the room's vote: {Plain(ex)}");
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
    ///
    /// THE WORKER SENDS IT, AND KEEPS SENDING IT - the pattern listening
    /// along and drivers' nights already use. This used to send once on a
    /// thread of its own and forget the answer, so an untick during a
    /// Wi-Fi blip left the relay taking votes. It also sent nothing when
    /// this run had not published a ballot yet, when a previous run had.
    /// Now it is marked here and the worker's pass tries it, on the vote's
    /// own fifteen-second tick, until the relay takes it.
    /// </summary>
    public void CloseRoomVoteOnTheRelay()
    {
        _voteClosePending = true;

        // Tried on the next pass rather than after the rest of the tick.
        _nextVotePollTick = 0;
        _wake.Set();
    }

    /// <summary>
    /// The close, from the worker's pass while the switch is off. Clears
    /// <see cref="_voteClosePending"/> only once the relay has it.
    /// </summary>
    private void SendVoteClose(CancellationToken token)
    {
        var target = _target;

        if (!IsSafeForToken(target.Root))
        {
            return;
        }

        // Left pending until MainForm has wired the vote in: the ballot is
        // built from it, even a closed one.
        if (TonightsVote?.Invoke() is not { } vote)
        {
            return;
        }

        var club = Uri.EscapeDataString(target.Club);

        if (PublishTheBallot(target, club, vote, enabled: false, token))
        {
            _voteClosePending = false;
        }
    }
}
