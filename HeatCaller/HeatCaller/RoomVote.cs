using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// One option on tonight's vote - a driver on the grid for the race being
/// voted on.
/// </summary>
/// <param name="Id">The driver's transponder, which is what the rest of the race already identifies them by.</param>
/// <param name="Name">Their name as the room would say it.</param>
internal sealed record VoteOption(string Id, string Name);

/// <summary>
/// How one option did, for the window that shows the tally live.
/// </summary>
/// <param name="Name">The driver.</param>
/// <param name="Votes">How many of the room had them.</param>
/// <param name="Share">That as a percentage of the votes cast, rounded.</param>
internal sealed record VoteStanding(string Name, int Votes, int Share);

/// <summary>
/// What the room reckoned, once the race has been run - the thing the
/// announcer says afterwards.
/// </summary>
/// <param name="RoundName">The race it was about.</param>
/// <param name="Votes">How many people voted.</param>
/// <param name="PickName">Who the room had.</param>
/// <param name="PickShare">Their share, as a percentage.</param>
/// <param name="RoomWasRight">True when the room's pick won it.</param>
/// <param name="WinnerShare">The winner's own share of the vote - 0 when nobody had them.</param>
/// <param name="NobodyHadTheWinner">True when not one person in the room picked the driver who won.</param>
internal sealed record RoomCall(
    string RoundName,
    int Votes,
    string PickName,
    int PickShare,
    bool RoomWasRight,
    int WinnerShare,
    bool NobodyHadTheWinner);

/// <summary>
/// A verdict ready to be said, with the winner's name as the rest of the
/// broadcast says it - what <see cref="CommentaryDirector.RoomsCall"/>
/// hands the verdict segment.
/// </summary>
/// <param name="Call">What the room reckoned, already judged worth saying.</param>
/// <param name="WinnerName">Who won, for the line to name.</param>
/// <param name="RaceKey">The round's own id, which is what is handed back to RoomVote.Said once it has been spoken.</param>
internal sealed record RoomsVerdict(RoomCall Call, string? WinnerName, string RaceKey);

/// <summary>
/// The room votes on who wins the main, and afterwards the announcer says
/// what they reckoned.
///
/// WHY THIS IS WORTH BUILDING (12 Oct 2026)
///
/// Everything else the announcer says is something the app worked out: a
/// result, a lap, a run of mains, a form guide. All of it is true and none
/// of it belongs to the room. A club note is the room's words; this is the
/// room's OPINION, and it is the only thing in the broadcast that the
/// people standing on the bank made themselves.
///
/// It is also the only segment that can be wrong, and that is the whole
/// point of it. "The room had Bill at forty-eight per cent, and Bill has
/// delivered" is a decent line. "Not one person in this room picked Sam
/// Teague" is the line people repeat in the car park, and no amount of
/// timing data could ever produce it.
///
/// WHY ONE POLL AT A TIME AND WHY IT CLOSES ITSELF
///
/// A poll belongs to a race. Opening one for the next race closes the one
/// before it, so a club can never have two running and the announcer can
/// never read out a verdict about the wrong race.
///
/// It also stops taking votes <see cref="LongestOpen"/> after it opened,
/// whatever anybody pressed. The race director closing it is the normal
/// path and the button is right there, but a poll left open through two
/// more heats would be collecting votes for a race that had already been
/// run - and then telling the room what it "predicted". A prediction
/// gathered after the flag is not a prediction, and the one thing this must
/// never do is claim the room called something it was actually told.
///
/// WHY A VOTE CAN BE CHANGED BUT NOT DOUBLED
///
/// Votes are held per voter rather than counted as they arrive, so a second
/// vote from the same phone REPLACES the first. Somebody who picked the
/// wrong name on a phone screen gets to fix it, and nobody gets two.
/// Counting arrivals instead would have made the tally a measure of who
/// pressed hardest.
///
/// WHY IT SAYS NOTHING ON A THIN VOTE
///
/// Under <see cref="LeastVotes"/> there is no verdict at all. Four people's
/// guesses are not "the room", and a club in its first week being told the
/// room was unanimous about something three people voted on is the kind of
/// claim that makes everything else the announcer says sound invented.
/// Same for a tie: the room was split, so there is nobody it picked.
/// </summary>
internal sealed class RoomVote
{
    private static string FilePath => Path.Combine(AppPaths.Root, "room-vote.json");

    /// <summary>
    /// The most drivers one vote may offer.
    ///
    /// Sixteen, which is more than any club runs in one race - a main is
    /// usually ten. It is a ceiling on the file and on the phone page
    /// rather than a rule about racing.
    /// </summary>
    internal const int MostOptions = 16;

    /// <summary>
    /// The most people who may vote in one poll.
    ///
    /// Five hundred, against a club night of forty. Nobody reaches it by
    /// voting: it is there so a public route cannot fill a race PC's disk,
    /// and so the file stays something a person could open and read.
    /// </summary>
    internal const int MostVoters = 500;

    /// <summary>The longest a driver's name may be on the voting page.</summary>
    internal const int MostChars = 60;

    /// <summary>
    /// How long a poll keeps taking votes, however it was left.
    ///
    /// Twenty minutes covers calling a main to the grid, running it and
    /// reading the result, with room to spare. See the note on this class
    /// for why it is not open-ended.
    /// </summary>
    internal static readonly TimeSpan LongestOpen = TimeSpan.FromMinutes(20);

    /// <summary>
    /// The fewest votes that make a verdict worth saying.
    ///
    /// Five. Below it the announcer says nothing about the vote at all -
    /// see the note on this class.
    /// </summary>
    internal const int LeastVotes = 5;

    private sealed class Poll
    {
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The ROUND this vote belongs to, by its own id - see the note on
        /// RoomVote for why this is not the race's label.
        /// </summary>
        public string RaceKey { get; set; } = string.Empty;

        /// <summary>Its label, for the announcer to say and the window to show.</summary>
        public string RoundName { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public List<VoteOption> Options { get; set; } = new();

        /// <summary>Voter id to the option they picked. See the note on this class for why it is held this way round.</summary>
        public Dictionary<string, string> Votes { get; set; } = new();

        public DateTime OpenedUtc { get; set; }
        public DateTime? ClosedUtc { get; set; }
        public bool Said { get; set; }

        /// <summary>Who won the race this was about, once it has been run. Empty until then.</summary>
        public string WinnerId { get; set; } = string.Empty;

        /// <summary>And their name, as the broadcast says it.</summary>
        public string WinnerName { get; set; } = string.Empty;

        /// <summary>True once the race has been run, so a verdict is never offered before there is a result.</summary>
        public bool Finished { get; set; }
    }

    private sealed class VoteFile
    {
        public string DayId { get; set; } = string.Empty;
        public Poll? Current { get; set; }
    }

    private readonly VoteFile _file;

    /// <summary>
    /// Serialises every read and write of <see cref="_file"/>.
    ///
    /// WHY THIS CLASS NEEDS ONE, which it shipped without (12 Oct 2026).
    /// Three threads reach it. A phone's vote arrives on an HttpListener
    /// thread (WebServerService.Vote); the relay's votes are folded in on
    /// the publisher's own thread (RelayPublisher.Vote.CollectRoomVotes);
    /// and the Room Vote window reads the standings off a one-second timer
    /// on the UI thread. The votes are a Dictionary, and a dictionary
    /// written while it is being enumerated throws
    /// InvalidOperationException - which on the UI thread is a crash
    /// dialog in front of a room, at the exact moment a vote is busy.
    ///
    /// Save() is called from inside this, which puts the file write under
    /// the lock as well. That is wanted rather than tolerated: two threads
    /// serialising the same object at once is how a half-written vote file
    /// gets onto a disk.
    ///
    /// Monitor is re-entrant, so a member taking the lock and calling
    /// another that takes it (Taking -> TakingAt, CallWaiting -> CallFor)
    /// is fine and is done on purpose rather than unpicked into an
    /// unlocked twin of each.
    /// </summary>
    private readonly object _gate = new();

    private RoomVote(VoteFile file) => _file = file;

    /// <summary>True when the file was there but could not be read - see SafeFile.HoldWritesAfterUnreadable.</summary>
    internal bool LoadedUnreadable { get; private init; }

    internal static RoomVote Load()
    {
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var file = JsonSerializer.Deserialize<VoteFile>(SafeFile.ReadAllTextWithRetry(FilePath));

                if (file is not null)
                {
                    if (file.Current is { } poll)
                    {
                        poll.Options ??= new List<VoteOption>();
                        poll.Votes ??= new Dictionary<string, string>();
                    }

                    return new RoomVote(file);
                }
            }
        }
        catch (Exception ex)
        {
            unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        return new RoomVote(new VoteFile()) { LoadedUnreadable = unreadable };
    }

    private void Save()
    {
        if (LoadedUnreadable)
        {
            return;
        }

        SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(
            _file,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Which race day this vote belongs to.</summary>
    internal string DayId
    {
        get
        {
            lock (_gate)
            {
                return _file.DayId;
            }
        }
    }

    /// <summary>
    /// A new race day: last week's vote goes.
    ///
    /// Cleared rather than kept, for the same reason club notes are - a
    /// verdict about a race run last Thursday read out to this week's room
    /// is worse than no verdict. Cheap and idempotent for the day it is
    /// already on, so it can be called from a tick.
    /// </summary>
    internal void NewMeeting(string? dayId)
    {
        lock (_gate)
        {
            var day = (dayId ?? string.Empty).Trim();

            if (string.Equals(_file.DayId, day, StringComparison.Ordinal))
            {
                return;
            }

            _file.DayId = day;
            _file.Current = null;

            Save();
        }
    }

    /// <summary>
    /// Opens a vote on a race, closing whatever was open before it.
    ///
    /// Null means it opened. Anything else is why not, in the race
    /// director's words.
    /// </summary>
    /// <param name="raceKey">The round's own id. See the note on this class for why the label will not do.</param>
    /// <param name="roundName">The race's label, as the program names it.</param>
    /// <param name="className">Its class, for the announcer to say.</param>
    /// <param name="options">The drivers on the grid. Duplicates by id are folded; blanks are dropped.</param>
    internal string? OpenFor(string? raceKey, string? roundName, string? className, IEnumerable<VoteOption>? options)
    {
        lock (_gate)
        {
            var key = Clean(raceKey, MostChars);
            var race = Clean(roundName, MostChars);

            if (key.Length == 0 || race.Length == 0)
            {
                return "There is no race to vote on - pick the round first.";
            }

            var picked = new List<VoteOption>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var option in options ?? Enumerable.Empty<VoteOption>())
            {
                if (option is null)
                {
                    continue;
                }

                var id = Clean(option.Id, MostChars);
                var name = Clean(option.Name, MostChars);

                if (id.Length == 0 || name.Length == 0 || !seen.Add(id))
                {
                    continue;
                }

                picked.Add(new VoteOption(id, name));

                if (picked.Count >= MostOptions)
                {
                    break;
                }
            }

            // TWO IS THE FEWEST A VOTE CAN BE. One name is not a question, and
            // a club would be told the room was unanimous about the only
            // driver it was offered.
            if (picked.Count < 2)
            {
                return "There are not enough drivers on the grid to vote on yet.";
            }

            _file.Current = new Poll
            {
                Id = Guid.NewGuid().ToString("N"),
                RaceKey = key,
                RoundName = race,
                ClassName = Clean(className, MostChars),
                Options = picked,
                OpenedUtc = DateTime.UtcNow,
            };

            Save();

            return null;
        }
    }

    /// <summary>
    /// Stops the vote taking any more. Safe when nothing is open and safe
    /// twice - the race director may well press it after it has timed out
    /// on its own.
    /// </summary>
    internal void Close()
    {
        lock (_gate)
        {
            if (_file.Current is not { } poll || poll.ClosedUtc is not null)
            {
                return;
            }

            poll.ClosedUtc = DateTime.UtcNow;

            Save();
        }
    }

    /// <summary>
    /// Whether a vote is taking votes right now: one is open, it was not
    /// closed, and it has not been open longer than
    /// <see cref="LongestOpen"/>.
    /// </summary>
    internal bool Taking
    {
        get
        {
            lock (_gate)
            {
                return TakingAt(DateTime.UtcNow);
            }
        }
    }

    /// <summary>
    /// <see cref="Taking"/> at a given moment, so the timeout can be
    /// tested without waiting twenty minutes.
    /// </summary>
    internal bool TakingAt(DateTime nowUtc)
    {
        lock (_gate)
        {
            return _file.Current is { } poll
                && poll.ClosedUtc is null
                && nowUtc - poll.OpenedUtc < LongestOpen;
        }
    }

    /// <summary>The race the open vote is about, by its label, or null when none is open.</summary>
    internal string? OnRace
    {
        get
        {
            lock (_gate)
            {
                return _file.Current?.RoundName;
            }
        }
    }

    /// <summary>
    /// And by the round's own id, which is what every method here matches
    /// on and what the announcer hands back to <see cref="Said"/>.
    /// </summary>
    internal string? OnRaceKey
    {
        get
        {
            lock (_gate)
            {
                return _file.Current?.RaceKey;
            }
        }
    }

    /// <summary>The drivers the open vote offers, for the page and the phones. Empty when none is open.</summary>
    internal IReadOnlyList<VoteOption> Options
    {
        get
        {
            lock (_gate)
            {
                if (_file.Current is not { } poll)
                {
                    return Array.Empty<VoteOption>();
                }

                // A COPY, not the list itself. Handing the live list out
                // put the caller's enumeration outside the lock, where
                // opening the next vote replaces it - and a window reading
                // the drivers off a one-second timer would have been
                // walking a list being rebuilt. The lists are a grid of
                // drivers: sixteen at the very most (MostOptions).
                return poll.Options.ToList();
            }
        }
    }

    /// <summary>How many people have voted.</summary>
    internal int Votes
    {
        get
        {
            lock (_gate)
            {
                return _file.Current?.Votes.Count ?? 0;
            }
        }
    }

    /// <summary>
    /// Records a vote, replacing anything this voter picked before.
    ///
    /// Null means it counted. Anything else is why not, in words a stranger
    /// on a phone can read - these go back over a web route to somebody who
    /// has never seen this app.
    /// </summary>
    /// <param name="voterId">Whatever identifies the phone. Never read for anything but keeping one vote to one voter.</param>
    /// <param name="optionId">The driver they picked.</param>
    internal string? Cast(string? voterId, string? optionId)
    {
        lock (_gate)
        {
            var voter = Clean(voterId, MostChars);
            var pick = Clean(optionId, MostChars);

            if (voter.Length == 0 || pick.Length == 0)
            {
                return "That vote did not come through - try again.";
            }

            if (_file.Current is not { } poll)
            {
                return "There is no vote running at the moment.";
            }

            if (!Taking)
            {
                return "Voting on this one has closed.";
            }

            if (!poll.Options.Any(one => string.Equals(one.Id, pick, StringComparison.Ordinal)))
            {
                return "That driver is not on the grid for this race.";
            }

            // THE CEILING ONLY BITES ON A NEW VOTER. Somebody changing their
            // mind must never be turned away for a limit about how many people
            // have voted.
            if (!poll.Votes.ContainsKey(voter) && poll.Votes.Count >= MostVoters)
            {
                return "That is as many votes as this one can take.";
            }

            poll.Votes[voter] = pick;

            Save();

            return null;
        }
    }

    /// <summary>
    /// What one voter has picked, or null when they have not voted.
    ///
    /// For the relay's collector, which re-reads the whole held set on
    /// every poll (a vote can be changed, so the relay is not drained by
    /// being read). Without this, every poll would rewrite the file with
    /// the votes it already had.
    /// </summary>
    internal string? PickOf(string? voterId)
    {
        lock (_gate)
        {
            var voter = Clean(voterId, MostChars);

            if (voter.Length == 0 || _file.Current is not { } poll)
            {
                return null;
            }

            return poll.Votes.TryGetValue(voter, out var picked) ? picked : null;
        }
    }

    /// <summary>
    /// The tally as it stands, best first, for the window that shows it -
    /// and for the Big Screen one day.
    ///
    /// Shares are of the votes CAST, so they add to a hundred give or take
    /// the rounding. A driver nobody picked is still in the list on nought,
    /// because an empty row is the interesting one before the race and the
    /// whole story after it.
    /// </summary>
    internal IReadOnlyList<VoteStanding> Standing()
    {
        lock (_gate)
        {
            if (_file.Current is not { } poll)
            {
                return Array.Empty<VoteStanding>();
            }

            var cast = poll.Votes.Count;

            return poll.Options
                .Select(option =>
                {
                    var votes = poll.Votes.Count(vote => string.Equals(vote.Value, option.Id, StringComparison.Ordinal));

                    return new VoteStanding(option.Name, votes, ShareOf(votes, cast));
                })
                .OrderByDescending(one => one.Votes)
                .ThenBy(one => one.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    /// <summary>
    /// A count as a percentage of the votes cast, rounded the way a person
    /// would round it. Nought when nobody voted, rather than a division by
    /// zero.
    /// </summary>
    internal static int ShareOf(int votes, int cast) =>
        cast <= 0 ? 0 : (int)Math.Round(votes * 100.0 / cast, MidpointRounding.AwayFromZero);

    /// <summary>
    /// What the room reckoned about the race that has just finished, or
    /// null when there is nothing worth saying.
    ///
    /// Null for: no vote, a vote about a different race, a vote already
    /// said, fewer than <see cref="LeastVotes"/> votes, or a room that
    /// split evenly and so picked nobody. Each of those is a case where
    /// the honest answer is silence - see the note on this class.
    /// </summary>
    /// <param name="raceKey">The round just run, which must be the one voted on.</param>
    /// <param name="winnerId">Who won it, by transponder.</param>
    internal RoomCall? CallFor(string? raceKey, string? winnerId)
    {
        lock (_gate)
        {
            if (_file.Current is not { } poll || poll.Said)
            {
                return null;
            }

            // THE RACE MUST BE THE ONE VOTED ON. Without this a club that
            // opened a vote on the A-main and then ran a tie-break heat would
            // have the room's A-main prediction read out over the heat.
            if (!IsTheRace(poll, raceKey))
            {
                return null;
            }

            var cast = poll.Votes.Count;

            if (cast < LeastVotes)
            {
                return null;
            }

            var counted = poll.Options
                .Select(option => (option, Votes: poll.Votes.Count(vote => string.Equals(vote.Value, option.Id, StringComparison.Ordinal))))
                .OrderByDescending(one => one.Votes)
                .ToList();

            // A HAND-EDITED FILE could have no options in it, and OpenFor's
            // own floor of two cannot speak for a poll it did not write.
            if (counted.Count == 0)
            {
                return null;
            }

            var top = counted[0];

            // A SPLIT ROOM PICKED NOBODY. Saying "the room had Bill" off a
            // four-all tie is a claim about the room that is not true.
            if (counted.Count > 1 && counted[1].Votes == top.Votes)
            {
                return null;
            }

            var winner = Clean(winnerId, MostChars);

            var winnerVotes = winner.Length == 0
                ? 0
                : poll.Votes.Count(vote => string.Equals(vote.Value, winner, StringComparison.Ordinal));

            var roomWasRight = winner.Length > 0 && string.Equals(top.option.Id, winner, StringComparison.Ordinal);

            return new RoomCall(
                RoundName: poll.RoundName,
                Votes: cast,
                PickName: top.option.Name,
                PickShare: ShareOf(top.Votes, cast),
                RoomWasRight: roomWasRight,

                // NOT CLAIMED WHEN THE WINNER WAS NOT ON THE GRID WE OFFERED.
                // A driver who was not an option had nobody pick them for a
                // reason that is about this app, not about the room.
                WinnerShare: ShareOf(winnerVotes, cast),
                NobodyHadTheWinner: winnerVotes == 0
                    && winner.Length > 0
                    && poll.Options.Any(one => string.Equals(one.Id, winner, StringComparison.Ordinal)));
        }
    }

    /// <summary>
    /// Files the result of the race that was voted on, which is what makes
    /// a verdict available at all.
    ///
    /// HELD HERE RATHER THAN ASKED FOR LATER. The announcer picks its
    /// segments on a tick, minutes after the flag, and asking "who won the
    /// last race" at that moment means asking whichever part of the app
    /// happens to still be holding it. The result is recorded once, by the
    /// code that has just filed it, against the poll it belongs to - so a
    /// verdict can never be about a different race than the votes were.
    ///
    /// Does nothing when the race is not the one voted on, and nothing on
    /// a second call for the same race: the first result filed is the one
    /// the room is told about, and a correction after the fact must not
    /// change a line that has already gone out.
    /// </summary>
    /// <param name="raceKey">The round just run, by its own id.</param>
    /// <param name="winnerId">Who won it, by transponder.</param>
    /// <param name="winnerName">And their name, for the announcer to say.</param>
    internal void Finished(string? raceKey, string? winnerId, string? winnerName)
    {
        lock (_gate)
        {
            if (_file.Current is not { } poll || poll.Finished)
            {
                return;
            }

            if (!IsTheRace(poll, raceKey))
            {
                return;
            }

            poll.WinnerId = Clean(winnerId, MostChars);
            poll.WinnerName = Clean(winnerName, MostChars);
            poll.Finished = true;

            // THE RACE HAS BEEN RUN, so the vote is over whatever anybody
            // pressed. Without this a club that never closed it would keep
            // taking votes while the result was being read out.
            if (poll.ClosedUtc is null)
            {
                poll.ClosedUtc = DateTime.UtcNow;
            }

            Save();
        }
    }

    /// <summary>
    /// The verdict waiting to be said, or null when there is none - which
    /// is the usual answer, since most races are not voted on.
    ///
    /// Null until <see cref="Finished"/> has filed a result: a verdict
    /// offered before the race had been run would be the announcer telling
    /// the room what it predicted about something that had not happened.
    /// </summary>
    internal RoomsVerdict? CallWaiting()
    {
        lock (_gate)
        {
            if (_file.Current is not { Finished: true } poll)
            {
                return null;
            }

            if (CallFor(poll.RaceKey, poll.WinnerId) is not { } call)
            {
                return null;
            }

            return new RoomsVerdict(call, poll.WinnerName.Length > 0 ? poll.WinnerName : null, poll.RaceKey);
        }
    }

    /// <summary>
    /// Marks the verdict as said, so it is never read out twice.
    ///
    /// Takes the race rather than nothing, so a call settled for one race
    /// cannot mark a vote that has since been opened for the next one.
    /// </summary>
    internal void Said(string? raceKey)
    {
        lock (_gate)
        {
            if (_file.Current is not { } poll || poll.Said)
            {
                return;
            }

            if (!IsTheRace(poll, raceKey))
            {
                return;
            }

            poll.Said = true;

            Save();
        }
    }

    /// <summary>
    /// Whether a round is the one this poll was opened for.
    ///
    /// BY THE ROUND'S OWN ID, NOT ITS LABEL, and this is the whole reason
    /// a key is carried at all: a club running two classes has two rounds
    /// both labelled "A-Main". Matching on the label would read the room's
    /// Stock prediction out over the Mod A-main - a verdict about the
    /// wrong race, said with complete confidence, which is the worst
    /// failure this feature has available to it.
    /// </summary>
    private static bool IsTheRace(Poll poll, string? raceKey)
    {
        var key = Clean(raceKey, MostChars);

        return key.Length > 0 && string.Equals(poll.RaceKey, key, StringComparison.Ordinal);
    }

    /// <summary>
    /// One line of free text on its way somewhere it will be read out or
    /// shown, through the same guard every other such string in the app
    /// goes through.
    /// </summary>
    private static string Clean(string? value, int maxChars) =>
        CommentaryService.Field(value, maxChars);
}
