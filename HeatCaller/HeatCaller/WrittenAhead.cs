namespace HeatCaller;

/// <summary>
/// A line written before it was needed, held, and thrown away the moment it
/// stops being true.
///
/// WHAT THIS IS FOR
///
/// Every segment in this app is written at the moment it is wanted, and
/// that one fact sets a ceiling on the whole show. It does not show up as
/// bad writing; it shows up as waiting. The staging chain waits up to
/// StagingWaitWithIntro for a round intro, twenty seconds of which is the
/// model (RaceControlControl.RoundIntroModelBudget), and the comment on
/// that constant records what it cost: a start that waited that long went
/// on to the countdown while the intro was still being written.
///
/// A round intro does not need to be written then. The programme says what
/// is next and the entry list says who is in it, so the words can be ready
/// before anybody presses Stage - and then a start costs no model time at
/// all.
///
/// THE WHOLE FEATURE IS UPSIDE, AND THAT IS BY CONSTRUCTION. A held line
/// that is still true saves the wait. A held line that is not is thrown
/// away and the caller writes one now, exactly as it did before any of
/// this. There is no path here that makes a night worse than it was; the
/// only thing a wrong answer can cost is a model call nobody heard, which
/// is counted and reported rather than hidden.
///
/// WHEN A HELD LINE IS NO LONGER TRUE
///
/// This is the whole of the class and the reason it is written down rather
/// than guessed at. A held intro is wrong if:
///
///   1. it is for a different round - the programme moved, or the club ran
///      the B-main first;
///   2. the grid changed - somebody scratched, somebody was added, or the
///      order moved, and an intro that names the front row by name is
///      wrong the moment the front row is not those two;
///   3. a FACT in it has since been said by something else. This is the
///      subtle one and it is the rule the plan settled on: hold the words,
///      re-check the facts' keys against SaidTonight at the moment of
///      speaking, abandon on any mismatch. Every key is unspent when a line
///      is held, because marking happens on speaking and not on writing -
///      so a key that is spent by the time it is wanted means some other
///      segment got there first, and the held words would repeat it;
///   4. it is simply old. A night moves.
///
/// Any one of those and the words go. There is deliberately no "mostly
/// still true".
///
/// AND THE ONE THAT IS NOT ON THAT LIST, BECAUSE IT IS SOLVED BY WHEN THE
/// LINE IS WRITTEN RATHER THAN BY A CHECK HERE (12 Oct 2026)
///
/// The plan this came from says the next race's intro is knowable "the
/// moment the current race goes green". For the field and the venue that
/// is true. For the STAKES and the FORM it is not, and the half that is
/// wrong is the half that would put a false championship claim over a PA:
/// an intro written at the green of race four, saying Bill leads by four
/// points, is wrong by the time race four has been won by Bill.
///
/// No check here can catch that. A points total is not a key that gets
/// spent; it is simply a different number, and the held sentence states the
/// old one with every appearance of being a fact. So the answer is not a
/// rule in this class but a rule about the CALLER: a line is written ahead
/// once the previous race's result is FILED, never while it is running.
/// That still leaves the whole gap between the flag and the next call to
/// the grid - minutes, not seconds - and every reader it uses then sees a
/// night that is up to date.
///
/// And a result filed or CORRECTED after a line was written makes it just
/// as stale, so every filing and every correction calls ResultsChanged,
/// which throws the held line away and refuses the Hold of any write that
/// was still in flight when it came.
///
/// ONE SLOT, NOT A QUEUE
///
/// Only ever the next thing. A queue of held intros sounds thriftier and is
/// worse: the programme can be changed between races, so everything past
/// the next round is a line that will probably be thrown away, and holding
/// several means several model calls spent to use one. A second hold
/// replaces the first and the first is counted as wasted, which is the
/// figure a club should see if the programme is being reordered all night.
/// </summary>
internal sealed class WrittenAhead
{
    /// <summary>
    /// How long a held line stays worth saying.
    ///
    /// Twenty minutes, which is comfortably more than the gap between two
    /// races and comfortably less than a break. The figure is not doing
    /// much work - the round and grid checks catch nearly everything this
    /// would - but a club that files a result and then stops for half an
    /// hour should not be introduced to the next race with words written
    /// before the raffle.
    /// </summary>
    internal static readonly TimeSpan StaysFresh = TimeSpan.FromMinutes(20);

    /// <summary>Why a held line was or was not used. Typed, so it can be counted rather than matched on its wording - see BudgetRefusal for the same decision.</summary>
    internal enum Why
    {
        /// <summary>It was still true and it was used.</summary>
        Used,

        /// <summary>Nothing was written ahead.</summary>
        NothingHeld,

        /// <summary>What was held is for another round.</summary>
        NotThatRound,

        /// <summary>Same round, different grid.</summary>
        GridChanged,

        /// <summary>Something else said one of its facts first.</summary>
        AFactWasSaid,

        /// <summary>Held too long.</summary>
        TooOld,

        /// <summary>
        /// The same round's intro was written ahead a second time, so the
        /// first set of words went.
        ///
        /// Its own reason rather than being folded into NotThatRound,
        /// because it is a different fault with a different fix: the
        /// programme moving is the club's doing and nothing is wrong, while
        /// this is the producer having written twice for one round - which
        /// ResultsChanged now stops, and which a club should never see a
        /// count of. If this is ever non-zero, that check has a hole in it.
        /// </summary>
        WrittenAgain,

        /// <summary>
        /// A result was filed or corrected after it was written, so its
        /// points and form may no longer be true.
        /// </summary>
        ResultsChanged,
    }

    /// <summary>What TakeFor answers: the words where they are still true, and why where they are not.</summary>
    /// <param name="Line">The held words, or null.</param>
    /// <param name="Facts">The pack those words were built from, so the caller marks them spent once the line has actually been spoken - never here. Null where the line carried no worked-out facts, which is a real answer rather than a gap.</param>
    /// <param name="Why">Used, or which of the four refusals it was.</param>
    internal sealed record Answer(string? Line, PackedFacts? Facts, Why Why);

    private readonly object _gate = new();
    private string _dayId = string.Empty;

    private string _for = string.Empty;
    private string _grid = string.Empty;
    private string _line = string.Empty;
    private PackedFacts? _facts;
    private DateTime _at;

    private int _written;
    private int _used;
    private int _results;
    private readonly Dictionary<Why, int> _wasted = new();

    /// <summary>A new race day. Nothing written for last week is wanted this week.</summary>
    internal void NewMeeting(string? dayId)
    {
        lock (_gate)
        {
            var now = dayId ?? string.Empty;

            if (string.Equals(_dayId, now, StringComparison.Ordinal))
            {
                return;
            }

            _dayId = now;
            Clear();
            _written = 0;
            _used = 0;
            _wasted.Clear();
        }
    }

    /// <summary>
    /// What a line is being held for, or empty.
    ///
    /// The producer used to read this so it did not write the same round's
    /// intro twice; a second filing now throws the first words away
    /// instead (see ResultsChanged), because a correction is exactly when
    /// they stop being true.
    /// </summary>
    internal string HoldingFor
    {
        get
        {
            lock (_gate)
            {
                return _line.Length > 0 ? _for : string.Empty;
            }
        }
    }

    /// <summary>How many lines were written ahead tonight.</summary>
    internal int Written
    {
        get
        {
            lock (_gate)
            {
                return _written;
            }
        }
    }

    /// <summary>How many were still true when they were wanted.</summary>
    internal int Used
    {
        get
        {
            lock (_gate)
            {
                return _used;
            }
        }
    }

    /// <summary>How many were thrown away - a model call the club paid for and nobody heard.</summary>
    internal int Wasted
    {
        get
        {
            lock (_gate)
            {
                return _wasted.Values.Sum();
            }
        }
    }

    /// <summary>
    /// Holds a line for a round that has not been called to the grid yet.
    ///
    /// A blank line is not held: a model that answered nothing has written
    /// nothing, and an empty slot is the honest record of that. It is also
    /// not counted as written, because nothing was.
    /// </summary>
    /// <param name="forWhat">The round this is for - its id, as TakeFor will ask for it.</param>
    /// <param name="grid">The grid it was written for, in order. See Fingerprint.</param>
    /// <param name="line">The words.</param>
    /// <param name="facts">The pack they were built from, or null.</param>
    /// <param name="at">Now.</param>
    /// <param name="asOf">
    /// What ResultsChanged answered when the write started, or null for a
    /// caller with no results to go stale. A write overtaken by a later
    /// filing is not held - it is counted as written and wasted.
    /// </param>
    /// <returns>True when the line is now held.</returns>
    internal bool Hold(string? forWhat, IEnumerable<string>? grid, string? line, PackedFacts? facts, DateTime at, int? asOf = null)
    {
        if (forWhat is not { Length: > 0 } round || line is not { Length: > 0 } words || words.Trim().Length == 0)
        {
            return false;
        }

        lock (_gate)
        {
            if (asOf is { } started && started != _results)
            {
                _written++;
                Spoil(Why.ResultsChanged);

                return false;
            }

            // A SECOND HOLD REPLACES THE FIRST, and the first is counted.
            // The commonest way here is the programme being reordered
            // between races, and a club doing that all night should be able
            // to see what it is costing - see the class summary.
            if (_line.Length > 0)
            {
                Spoil(string.Equals(_for, round, StringComparison.Ordinal)
                    ? Why.WrittenAgain
                    : Why.NotThatRound);
            }

            _for = round;
            _grid = Fingerprint(grid);
            _line = words;
            _facts = facts;
            _at = at;
            _written++;

            return true;
        }
    }

    /// <summary>
    /// A result has been filed or corrected. Throws away whatever is held,
    /// because the points and the form in it were read from the results as
    /// they stood before, and answers a number for the write the caller
    /// starts next to hand back to Hold - so a write still in flight from
    /// before this is not held either.
    /// </summary>
    internal int ResultsChanged()
    {
        lock (_gate)
        {
            if (_line.Length > 0)
            {
                Spoil(Why.ResultsChanged);
                Clear();
            }

            return ++_results;
        }
    }

    /// <summary>
    /// The held line if it is still true, and otherwise why not.
    ///
    /// TAKES IT. A line is used once: on any answer at all the slot is
    /// emptied, because every answer other than Used means those words are
    /// wrong and holding them would only offer them again, and Used means
    /// they have been handed over. The caller speaks them or drops them,
    /// and either way writes nothing back.
    ///
    /// THE FACTS ARE NOT MARKED HERE. The pack goes back to the caller to
    /// mark once the line has actually been spoken - the rule every fact in
    /// this app is under, and the reason BuildRoundIntroAsync hands its pack
    /// out through a callback rather than marking it itself. A held line
    /// that is then dropped by a reset must cost the night nothing.
    /// </summary>
    /// <param name="forWhat">The round being called to the grid.</param>
    /// <param name="grid">Its grid now, in order.</param>
    /// <param name="said">What has already gone out tonight. Null skips the fact check, which is for a caller that has no ledger rather than a way of avoiding the rule.</param>
    /// <param name="now">Now.</param>
    internal Answer TakeFor(string? forWhat, IEnumerable<string>? grid, SaidTonight? said, DateTime now)
    {
        lock (_gate)
        {
            if (_line.Length == 0)
            {
                return No(Why.NothingHeld, count: false);
            }

            // A CALLER THAT NAMED NO ROUND IS NOT EVIDENCE THE HELD LINE
            // IS WRONG, so this one refusal keeps the words. Every other
            // answer empties the slot, because every other answer means
            // those words are actually wrong - but a round with no id
            // cannot be matched against anything, and throwing the line
            // away here would lose the write-ahead for the rest of the
            // night: the producer only writes when a result is filed, and
            // that moment has gone.
            if (forWhat is not { Length: > 0 } round)
            {
                return No(Why.NotThatRound, count: false);
            }

            if (!string.Equals(_for, round, StringComparison.Ordinal))
            {
                return No(Why.NotThatRound);
            }

            if (!string.Equals(_grid, Fingerprint(grid), StringComparison.Ordinal))
            {
                return No(Why.GridChanged);
            }

            // THE RULE THE PLAN SETTLED ON. Every key was unspent when the
            // line was held, because marking happens on speaking - so one
            // that is spent now means another segment said that fact in
            // between, and these words would say it again. See the class
            // summary.
            if (said is not null && _facts is { } pack)
            {
                foreach (var key in pack.Keys)
                {
                    if (key is { Length: > 0 } && said.Spent(key))
                    {
                        return No(Why.AFactWasSaid);
                    }
                }
            }

            if (now - _at > StaysFresh)
            {
                return No(Why.TooOld);
            }

            var answer = new Answer(_line, _facts, Why.Used);

            Clear();
            _used++;

            return answer;
        }
    }

    /// <summary>
    /// A grid as one comparable string, in the order it was given.
    ///
    /// IN ORDER, which is the point rather than a detail. A round intro
    /// names the drivers off the front of the entry list, so two grids with
    /// the same people in a different order are two different intros - and
    /// a sorted fingerprint would have called them the same and read out a
    /// front row that was not there.
    ///
    /// Blanks dropped and case folded, because an entry list is edited by
    /// hand and a transponder typed with a trailing space is the same car.
    /// </summary>
    internal static string Fingerprint(IEnumerable<string>? grid)
    {
        if (grid is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();

        foreach (var one in grid)
        {
            if (one is { Length: > 0 } entry && entry.Trim() is { Length: > 0 } trimmed)
            {
                parts.Add(trimmed.ToUpperInvariant());
            }
        }

        return string.Join("|", parts);
    }

    /// <summary>
    /// What the night's console says about writing ahead, or null when
    /// nothing was written ahead at all.
    ///
    /// Null rather than a line of noughts, which is NightAccount.Report's
    /// own rule: a report that lists what did not happen is a report nobody
    /// reads twice.
    /// </summary>
    internal string? Report()
    {
        lock (_gate)
        {
            if (_written == 0)
            {
                return null;
            }

            var wasted = _wasted.Values.Sum();

            var line = $"Wrote {Plural.Of(_written, "intro")} ahead and used "
                + $"{_used.ToString(System.Globalization.CultureInfo.InvariantCulture)}.";

            if (wasted == 0)
            {
                return line;
            }

            var reasons = _wasted
                .Where(one => one.Value > 0)
                .OrderByDescending(one => one.Value)
                .Select(one => $"{one.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)} {Because(one.Key)}");

            return line + $" Threw {wasted.ToString(System.Globalization.CultureInfo.InvariantCulture)} away: "
                + string.Join(", ", reasons) + ".";
        }
    }

    /// <summary>
    /// Why a held line was not used, in a club's words rather than a
    /// developer's. Said in the console at the moment it happens and
    /// counted in the report.
    /// </summary>
    internal static string Because(Why why) => why switch
    {
        Why.Used => "it was still right",
        Why.NothingHeld => "nothing had been written ahead",
        Why.NotThatRound => "the programme moved on to another round",
        Why.GridChanged => "the grid changed after it was written",
        Why.AFactWasSaid => "another segment had already said one of its facts",
        Why.TooOld => "it had been waiting too long to still be about tonight",
        Why.WrittenAgain => "it was written ahead twice for the same round",
        Why.ResultsChanged => "a result was filed or corrected after it was written",
        _ => "of something this version does not have a word for",
    };

    /// <summary>A refusal, counted unless there was nothing there to waste.</summary>
    private Answer No(Why why, bool count = true)
    {
        if (count)
        {
            Spoil(why);
            Clear();
        }

        return new Answer(null, null, why);
    }

    /// <summary>One line thrown away, under the reason it went for.</summary>
    private void Spoil(Why why) =>
        _wasted[why] = (_wasted.TryGetValue(why, out var many) ? many : 0) + 1;

    /// <summary>Empties the slot. Never touches the counts.</summary>
    private void Clear()
    {
        _for = string.Empty;
        _grid = string.Empty;
        _line = string.Empty;
        _facts = null;
        _at = default;
    }
}
