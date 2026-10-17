namespace HeatCaller;

/// <summary>
/// THE ACCOUNT THE NIGHT GIVES OF ITSELF (12 Oct 2026) - every segment the
/// announcer weighed up, what became of it, and why.
///
/// Two questions a club cannot answer today, and they are the two questions
/// a club actually asks.
///
/// "WHY WAS IT SO QUIET?" The commonest way this product disappoints is
/// silence nobody can explain. Every refusal already has a reason and the
/// console already says most of them - but one at a time, scattered through
/// an evening's log, and the two commonest of all are said NOWHERE: a
/// segment with no facts to build from and a segment whose every fact had
/// already been said both return quietly from CommentaryDirector.TryAsync
/// with nothing written down. So a club reading the log sees a handful of
/// refusals and no idea whether that was three or thirty. <see cref="Report"/>
/// adds them up, and names the setting that would release each.
///
/// "WHY DID IT SAY THAT?" Every claim this app makes is derived, and the
/// argument for that is written out at length in a dozen files - none of
/// which the club that bought it will ever open. <see cref="Proof"/> shows
/// the facts a segment was built from, in the words they were given to the
/// model, and nothing else. It is the one thing no competitor can offer,
/// because no competitor's announcer has a proof to show.
///
/// IT COUNTS A TYPED REASON, NEVER A SENTENCE. See
/// <see cref="BudgetRefusal"/>, which was added for this: the budget's own
/// Reason is prose written for a person and grouping it would mean matching
/// on its words. This app has one place that matches on the words of a
/// sentence it was handed and the roadmap records it as the fragile
/// shortcut rather than the pattern.
///
/// ONE NIGHT, like everything else about the show - cleared when the race
/// day changes, through <see cref="NewMeeting"/>, which is what
/// CommentaryDirector already calls on every tick for its own stores.
/// Nothing is written to disk: an account of what was said is interesting
/// for an evening and nothing reads it afterwards. If that ever changes it
/// is a report to save rather than a store to keep.
/// </summary>
internal sealed class NightAccount
{
    /// <summary>
    /// Serialises the tallies.
    ///
    /// The director is single-threaded by design and says so at length
    /// (CommentaryContinuity's note on the one-thread rule), but TryAsync
    /// awaits the model in the middle of itself: the gather happens on the
    /// UI tick and what became of the segment is known on a pool thread
    /// afterwards. Both write here. Counting is cheap enough that the lock
    /// costs nothing worth measuring, and the lesson of 12 Oct 2026 is that
    /// a store written from two threads and read from a third wants its
    /// gate on the day it is written rather than on the night it crashes.
    /// </summary>
    private readonly object _gate = new();

    private string _dayId = string.Empty;

    private readonly Dictionary<SegmentKind, int> _spoke = new();
    private readonly Dictionary<SegmentKind, int> _weighed = new();
    private readonly Dictionary<BudgetRefusal, int> _refused = new();

    private int _nothingToSay;
    private int _everyFactSpent;
    private int _droppedAfterWriting;

    /// <summary>The last segment that went out, and the facts it was built from - see <see cref="Proof"/>.</summary>
    private SegmentKind? _lastKind;

    private PackedFacts? _lastPack;

    /// <summary>
    /// A new race day: last night's account goes.
    ///
    /// Cheap and idempotent for the day it is already on, so it can be
    /// called from a tick - the same contract ClubNotes.NewMeeting and
    /// RoomVote.NewMeeting hold to.
    /// </summary>
    internal void NewMeeting(string? dayId)
    {
        var day = (dayId ?? string.Empty).Trim();

        lock (_gate)
        {
            if (string.Equals(_dayId, day, StringComparison.Ordinal))
            {
                return;
            }

            _dayId = day;

            _spoke.Clear();
            _weighed.Clear();
            _refused.Clear();

            _nothingToSay = 0;
            _everyFactSpent = 0;
            _droppedAfterWriting = 0;

            _lastKind = null;
            _lastPack = null;
        }
    }

    /// <summary>The night this account is of.</summary>
    internal string DayId
    {
        get
        {
            lock (_gate)
            {
                return _dayId;
            }
        }
    }

    /// <summary>
    /// The pass got as far as weighing this segment up.
    ///
    /// Counted BEFORE anything can refuse it, so the denominator is honest:
    /// "eleven of nineteen" means nineteen were considered, not nineteen
    /// were nearly said.
    /// </summary>
    internal void Weighed(SegmentKind kind)
    {
        lock (_gate)
        {
            _weighed[kind] = Count(_weighed, kind) + 1;
        }
    }

    /// <summary>Nothing to build it from - no reader had a fact to give. The commonest answer of all, and said nowhere until now.</summary>
    internal void NothingToSay()
    {
        lock (_gate)
        {
            _nothingToSay++;
        }
    }

    /// <summary>Facts existed and every one of them had already been said, or had just been talked about.</summary>
    internal void EveryFactSpent()
    {
        lock (_gate)
        {
            _everyFactSpent++;
        }
    }

    /// <summary>The budget would not have it - see <see cref="BudgetRefusal"/>.</summary>
    internal void Refused(BudgetRefusal why)
    {
        if (why == BudgetRefusal.None)
        {
            return;
        }

        lock (_gate)
        {
            _refused[why] = _refused.TryGetValue(why, out var had) ? had + 1 : 1;
        }
    }

    /// <summary>Written, then not said: the race moved on, or the segments were held while it was being written.</summary>
    internal void DroppedAfterWriting()
    {
        lock (_gate)
        {
            _droppedAfterWriting++;
        }
    }

    /// <summary>
    /// It went out, and these are the facts it was built from.
    ///
    /// The pack is kept rather than its text pulled out here, because
    /// <see cref="Proof"/> wants the counts beside the words: how many
    /// facts went in, how many the room dropped and how many the night had
    /// just talked about are three different numbers and PackedFacts
    /// already keeps them apart.
    /// </summary>
    internal void Spoke(SegmentKind kind, PackedFacts? pack)
    {
        lock (_gate)
        {
            _spoke[kind] = Count(_spoke, kind) + 1;

            if (pack is not null)
            {
                _lastKind = kind;
                _lastPack = pack;
            }
        }
    }

    /// <summary>How many segments went out tonight.</summary>
    internal int Spoken
    {
        get
        {
            lock (_gate)
            {
                return _spoke.Values.Sum();
            }
        }
    }

    /// <summary>How many were weighed up.</summary>
    internal int WeighedUp
    {
        get
        {
            lock (_gate)
            {
                return _weighed.Values.Sum();
            }
        }
    }

    /// <summary>
    /// What the night came to, in the club's own terms, or null when the
    /// announcer has not been asked for anything yet.
    ///
    /// ONE LINE PER REASON, biggest first, and only the reasons that
    /// actually happened: a report listing nine kinds of nought is a report
    /// nobody reads twice. The setting that would release each is named
    /// where there is one, and where there is not, the line says the
    /// refusal was right rather than leaving a club hunting for a tick box
    /// that does not exist.
    /// </summary>
    internal string? Report()
    {
        lock (_gate)
        {
            var weighed = _weighed.Values.Sum();

            if (weighed == 0)
            {
                return null;
            }

            var spoke = _spoke.Values.Sum();

            var lines = new List<string>
            {
                $"Tonight the announcer weighed up {Plural.Of(weighed, "segment")} and said {spoke}.",
            };

            var held = new List<(int Many, string Why)>();

            if (_nothingToSay > 0)
            {
                held.Add((_nothingToSay, "there was nothing worked out to build one from - which is the ordinary "
                                         + "answer early in a night, before the club's own results have anything to say"));
            }

            if (_everyFactSpent > 0)
            {
                held.Add((_everyFactSpent, "every fact it had was already said tonight, or had just been talked about"));
            }

            foreach (var (why, many) in _refused.OrderByDescending(one => one.Value))
            {
                held.Add((many, Because(why)));
            }

            if (_droppedAfterWriting > 0)
            {
                held.Add((_droppedAfterWriting, "it was written and then the race moved on before it could be said"));
            }

            foreach (var (many, why) in held.OrderByDescending(one => one.Many))
            {
                lines.Add($"  {many} held - {why}.");
            }

            return string.Join("\n", lines);
        }
    }

    /// <summary>
    /// Why the announcer said what it last said: the facts it was given, in
    /// the words it was given them in.
    ///
    /// Null before anything has gone out. THE FACTS AND NOTHING ELSE - not
    /// the keys, which are identifiers, and not the themes, which are for
    /// the cooldown. A club asking "where did it get that" is asking what
    /// it was told, and what it was told is these sentences.
    /// </summary>
    internal string? Proof()
    {
        lock (_gate)
        {
            if (_lastKind is not { } kind || _lastPack is not { } pack || pack.Empty)
            {
                return null;
            }

            var lines = new List<string>
            {
                $"{CommentaryBudget.Spec(kind).Title} was built from these facts, worked out from the club's "
                + "own results, and from nothing else:",
            };

            lines.AddRange(pack.Text
                .Split('\n')
                .Select(one => one.Trim())
                .Where(one => one.Length > 0)
                .Select(one => "  " + one));

            // THE TWO KINDS OF LEFT OUT, kept apart because PackedFacts
            // keeps them apart and for its stated reason: a thin segment
            // should be accountable as a thin segment rather than looking
            // like a cap that bit.
            if (pack.Dropped > 0)
            {
                lines.Add($"  ({Plural.Of(pack.Dropped, "more fact")} did not fit the room this segment gets.)");
            }

            if (pack.HeldBack > 0)
            {
                lines.Add($"  ({Plural.Of(pack.HeldBack, "more fact")} held back - the night had just talked about them.)");
            }

            return string.Join("\n", lines);
        }
    }

    private static int Count(IReadOnlyDictionary<SegmentKind, int> of, SegmentKind kind) =>
        of.TryGetValue(kind, out var many) ? many : 0;

    /// <summary>
    /// A refusal in the club's words, with what would change it.
    ///
    /// Four of these have a setting behind them and five do not, and saying
    /// so is the point: a club told "held because the last ten minutes were
    /// full" will go and look for the dial, and a club told "held because
    /// it had already been said tonight" needs to know that is the feature
    /// working.
    /// </summary>
    private static string Because(BudgetRefusal why) => why switch
    {
        BudgetRefusal.SwitchedOff =>
            "this club has it switched off - tick it under Segments on the AI Commentary tab",
        BudgetRefusal.ItsCapForTheNight =>
            "it had already been said as often as one night allows, which is the variety rule working",
        BudgetRefusal.NothingNewToSay =>
            "there was nothing new to say about it, which is the variety rule working",
        BudgetRefusal.TooSoonAfterTheLast =>
            "it came too soon after the last line - the gap each segment keeps is its own, and a club that "
            + "wants more of them turns the chattiness dial up on the AI Commentary tab",
        BudgetRefusal.TheAirtimeCap =>
            "the last ten minutes were already as full as this race and the chattiness dial allow - the dial "
            + "is on the AI Commentary tab, and a heat is deliberately quieter than a main",
        BudgetRefusal.TheRelayIsBusy =>
            "the relay had as many lines in flight as it will take, and something that mattered more had them",
        BudgetRefusal.TheMonthsAllowance =>
            "the month's allowance is down to what is held back for intros and recaps - the allowance is on "
            + "the Connection tab",
        BudgetRefusal.TheNightsHeadroom =>
            "the night's own budget had no room left for it - the budget is on the AI Commentary tab",
        _ => "the budget would not have it",
    };
}
