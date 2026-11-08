namespace HeatCaller;

/// <summary>
/// One change at the front, as <see cref="LeadTracker"/> saw it.
/// </summary>
/// <param name="NewLeader">The transponder now in front.</param>
/// <param name="Previous">The transponder that was in front.</param>
/// <param name="Number">Which lead change of the race this is: 1 for the first time the lead changes hands.</param>
/// <param name="Leaders">How many different drivers have led the race, the new leader included.</param>
/// <param name="TimesLed">How many separate times the new leader has now led, this one included. 1 the first time they get there.</param>
public sealed record LeadChange(string NewLeader, string Previous, int Number, int Leaders, int TimesLed);

/// <summary>
/// Counts the changes at the front of one race - how many times the lead has
/// changed hands, and among how many drivers - for the broadcast's
/// "that's the fourth lead change among three drivers".
///
/// WHY IT COUNTS ITS OWN AND DOES NOT READ THE BOARD
///
/// The running order only ever says who leads NOW. How many times that has
/// changed is a history, and the only honest place to keep it is where the
/// change is noticed - the same place the lead change call is made
/// (AnnouncementService.HandleStandingUpdated) - so the count the voice says
/// and the count on the broadcast tower can never disagree.
///
/// The first car to lead is not a lead change. NASCAR does not count the
/// polesitter leading lap one, and nobody in the room would either: the
/// lead has to be taken from somebody.
///
/// Laps led are not kept here. RaceControlSession counts them at each
/// crossing (RacerStanding.LapsLed), which is the only moment the question
/// has an honest answer, and a second count here would sooner or later
/// disagree with the board.
///
/// Locked, because the announcer feeds it on the UI thread and the web
/// server's state document reads it; both are the UI thread today, but
/// "today" is not a promise anyone made.
///
/// Pure: transponder ids in, counts out. No stores, no UI, no clock.
/// </summary>
public sealed class LeadTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _timesLed = new(StringComparer.OrdinalIgnoreCase);
    private string? _leader;
    private int _changes;

    /// <summary>Who is in front, as last observed. Null before anyone has led.</summary>
    public string? Leader
    {
        get
        {
            lock (_gate)
            {
                return _leader;
            }
        }
    }

    /// <summary>How many times the lead has changed hands this race. The first leader does not count.</summary>
    public int Changes
    {
        get
        {
            lock (_gate)
            {
                return _changes;
            }
        }
    }

    /// <summary>How many different drivers have led this race.</summary>
    public int Leaders
    {
        get
        {
            lock (_gate)
            {
                return _timesLed.Count;
            }
        }
    }

    /// <summary>
    /// The car in front now. Returns the change when it is a different car
    /// from the one in front last time, and null otherwise - the first leader
    /// of the race included, since nobody has had the lead taken from them.
    /// </summary>
    public LeadChange? Observe(string? leader)
    {
        if (string.IsNullOrWhiteSpace(leader))
        {
            return null;
        }

        lock (_gate)
        {
            if (_leader is null)
            {
                _leader = leader;
                _timesLed[leader] = Math.Max(1, _timesLed.GetValueOrDefault(leader));
                return null;
            }

            if (string.Equals(_leader, leader, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var previous = _leader;
            _leader = leader;
            _changes++;
            _timesLed[leader] = _timesLed.GetValueOrDefault(leader) + 1;

            return new LeadChange(leader, previous, _changes, _timesLed.Count, _timesLed[leader]);
        }
    }

    /// <summary>
    /// The car that was in front has gone - a ghost switched off. The next
    /// car to be seen in front has not taken the lead from anybody, so it is
    /// not counted as a change; the counts so far stand.
    /// </summary>
    public void Forget(string transponder)
    {
        lock (_gate)
        {
            if (string.Equals(_leader, transponder, StringComparison.OrdinalIgnoreCase))
            {
                _leader = null;
            }
        }
    }

    /// <summary>
    /// The car in front after a correction by hand - a penalty, or a lap
    /// added or taken away. Remembered as the leader, so the next crossing
    /// is not seen as a change, but not counted as one: the lead was not
    /// taken from anybody on track.
    /// </summary>
    /// <remarks>
    /// WHY (8 Nov 2026): a penalty went through Observe like a pass, so it
    /// put up a lead change and added to the count on the broadcast tower and
    /// in "that's the fourth lead change". Not observing it at all was no
    /// better - the new leader's next crossing then counted it, a lap late.
    /// </remarks>
    public void Correct(string? leader)
    {
        if (string.IsNullOrWhiteSpace(leader))
        {
            return;
        }

        lock (_gate)
        {
            _leader = leader;
            _timesLed[leader] = Math.Max(1, _timesLed.GetValueOrDefault(leader));
        }
    }

    /// <summary>A new race: nobody has led and nothing has changed hands.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _leader = null;
            _changes = 0;
            _timesLed.Clear();
        }
    }

    /// <summary>
    /// "That's the fourth lead change among three drivers." - or nothing,
    /// for the first change of a race, where a count of one tells the room
    /// nothing the call itself did not.
    ///
    /// Spoken words rather than numerals ("fourth", "three"), for the reason
    /// RaceNumbers gives: a numeral is read by the voice engine however it
    /// likes, and "4th" has been heard as "four tee aitch".
    /// </summary>
    public static string Tail(LeadChange change)
    {
        if (change.Number < 2)
        {
            return string.Empty;
        }

        var nth = SpokenOrdinal(change.Number);

        return change.Leaders >= 2
            ? $" That's the {nth} lead change, among {SpokenCount(change.Leaders)} drivers."
            : $" That's the {nth} lead change.";
    }

    /// <summary>"Back in front for the second time" for a driver retaking it, empty otherwise.</summary>
    public static string BackInFront(LeadChange change) =>
        change.TimesLed >= 2 ? $"back in front for the {SpokenOrdinal(change.TimesLed)} time" : string.Empty;

    /// <summary>first, second ... twentieth, then 21st and so on - past twenty the words stop helping.</summary>
    internal static string SpokenOrdinal(int n) => n switch
    {
        1 => "first",
        2 => "second",
        3 => "third",
        4 => "fourth",
        5 => "fifth",
        6 => "sixth",
        7 => "seventh",
        8 => "eighth",
        9 => "ninth",
        10 => "tenth",
        11 => "eleventh",
        12 => "twelfth",
        13 => "thirteenth",
        14 => "fourteenth",
        15 => "fifteenth",
        16 => "sixteenth",
        17 => "seventeenth",
        18 => "eighteenth",
        19 => "nineteenth",
        20 => "twentieth",
        _ => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" }),
    };

    /// <summary>"three" - RaceNumbers' words, so a count is said the way every other number the announcer says is.</summary>
    internal static string SpokenCount(int n) => RaceNumbers.Words(n);
}
