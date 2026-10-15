namespace HeatCaller;

/// <summary>
/// How close a driver's RUNNING PACE is to the layout record, biggest
/// last.
///
/// Only ever goes up for a given driver in a given race - see
/// <see cref="RecordWatch"/>. A record watch that could go backwards would
/// be an announcer saying the record is in danger, then that it is not,
/// then that it is.
/// </summary>
public enum RecordThreatLevel
{
    /// <summary>Nothing worth saying.</summary>
    None = 0,

    /// <summary>Their recent laps are in the right area and getting quicker.</summary>
    Closing = 1,

    /// <summary>Two of their last few laps are within a hair of the record.</summary>
    OnTheDoorstep = 2,

    /// <summary>Their best this race is as good as the record. One more like it and it goes.</summary>
    OneMoreLikeThat = 3,
}

/// <summary>
/// A driver's pace measured against the record that still stands.
/// </summary>
/// <param name="DriverId">Whose pace.</param>
/// <param name="Level">How close it has got.</param>
/// <param name="BestThisRace">Their quickest lap of this race so far.</param>
/// <param name="Record">The layout record they are measured against.</param>
/// <param name="Holder">Who holds it, where that is known.</param>
/// <param name="Needed">
/// How much more they have to find, in seconds. Never negative: once the
/// record has actually gone this class stops having an opinion and
/// <see cref="PaceWatch"/> calls the break.
/// </param>
/// <param name="InsideTheMargin">
/// How many of their last few laps are inside the band this level needed.
/// The number that makes it PACE rather than one lap.
/// </param>
/// <param name="LapsSeen">How many laps of theirs this race has counted.</param>
/// <param name="Improving">Whether their recent laps are trending quicker.</param>
public sealed record RecordThreat(
    string DriverId,
    RecordThreatLevel Level,
    double BestThisRace,
    double Record,
    string? Holder,
    double Needed,
    int InsideTheMargin,
    int LapsSeen,
    bool Improving);

/// <summary>
/// Watches every car's running pace against the record that still stands,
/// and says so BEFORE it falls.
///
/// WHY THIS IS THE ONE THING ONLY A COMPUTER CAN DO (14 Oct 2026)
///
/// A human commentator calls a record after it happens, because that is
/// when they find out. They are watching the racing, not sixteen rolling
/// averages. This app has every lap of every car in memory and can see a
/// record coming four laps out - and until now it did not say so, which
/// means the most broadcast-sounding line available to it was the one it
/// was throwing away.
///
/// "The record has gone" is a result. "Two of Dave's last three laps are
/// inside five point one, the record is five point oh four, and he is
/// finding a few hundredths every time round" is COMMENTARY, and it is
/// also the only kind of anticipation this app is allowed to make - see
/// the line below.
///
/// IT NEVER PREDICTS, and the rule comes from NightPromises, which states
/// it for the night's storyline and refuses to hold a claim. "Dave will
/// break the record" is falsifiable and this app must never say it. What
/// this class produces is ARITHMETIC: what they have done, what the mark
/// is, and the subtraction between the two. Every one of those is a fact
/// at the moment it is said, and stays a fact if the next lap is a spin.
/// The announcer's own wording carries the same discipline - see
/// <see cref="Say"/>.
///
/// WHAT IT WILL NOT SAY
///
///  - Anything at all without a record to measure against. A layout with
///    no mark standing on it has nothing to be close to.
///  - Anything off ONE lap. That call already exists and is a different
///    thing: AnnouncementEventType.RecordPace fires when a single lap
///    lands within two percent of the record. One lap is a fluke, a tow
///    or a short-cut; what makes a record fall is PACE, which is why
///    every level here needs at least two laps inside the band. See
///    <see cref="InsideToCount"/>.
///  - Anything before a driver has turned enough laps to have a pace.
///  - The same level twice, or a lower one after a higher. A watch only
///    ever escalates - see <see cref="Saw"/>.
///  - Anything once the record has actually gone. From that moment the
///    story is PaceWatch's, and two classes calling the same record would
///    be the database sound this layer exists to avoid.
///
/// History comes in as a function rather than a store, exactly as
/// PaceWatch takes it, so the whole class is a table test.
/// </summary>
public sealed class RecordWatch
{
    private readonly Dictionary<string, List<double>> _recent = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _bestThisRace = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RecordThreatLevel> _said = new(StringComparer.Ordinal);
    private readonly HashSet<string> _recordGone = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);

    /// <summary>The fastest lap ever set on this layout, and who holds it. From the layout's records.</summary>
    public Func<(double Seconds, string Holder)?>? LayoutRecord { get; set; }

    /// <summary>
    /// How many of a driver's most recent laps make up their "pace".
    ///
    /// Three. Two is a pair of laps and could still be one good stint
    /// either side of a mistake; four starts reaching back far enough that
    /// a driver who has genuinely found something has their old pace
    /// counted against them.
    /// </summary>
    public int RollingLaps { get; set; } = 3;

    /// <summary>
    /// How many of those laps have to be inside the band before it counts
    /// as pace rather than a lap.
    ///
    /// Two, which against a rolling window of three means a driver may
    /// have a mistake in the middle of it and still be on the record's
    /// pace - because they are. Requiring all three would mean one
    /// marshalled lap silenced the whole watch.
    /// </summary>
    public int InsideToCount { get; set; } = 2;

    /// <summary>
    /// How many laps a driver turns before this says anything about them,
    /// whatever their times.
    ///
    /// The reason PaceWatch gives for its own warm-up: a towed lap, a
    /// short-cut or a decoder reading one car's transponder as another
    /// produces one impossible time, and a record watch that fires off it
    /// has told the room the record is going when nobody is near it.
    /// </summary>
    public int WarmUpLaps { get; set; } = 2;

    /// <summary>
    /// Within this fraction of the record is "closing" - five percent.
    ///
    /// A fraction rather than a number of seconds, because a five
    /// hundredths margin is nothing on a twenty second lap and most of the
    /// difference between first and last on a five second one. Every band
    /// here is proportional for that reason.
    /// </summary>
    public double ClosingWithin { get; set; } = 0.05;

    /// <summary>And within this fraction is "on the doorstep" - one and a half percent.</summary>
    public double DoorstepWithin { get; set; } = 0.015;

    /// <summary>
    /// How near their best has to be to the record before it is "one more
    /// like that" - a quarter of a percent, which on a five second lap is
    /// about a hundredth.
    ///
    /// Deliberately tighter than <see cref="PaceWatch.MeaningfulMargin"/>
    /// is loose: that margin exists so a few thousandths do not count as
    /// beating a record, and this one so "one more like that" is only ever
    /// said when it is literally true.
    /// </summary>
    public double LevelWithin { get; set; } = 0.0025;

    /// <summary>A sanity bound - a lap this long is a car that stopped, not a lap.</summary>
    public double LongestCredibleLap { get; set; } = 600;

    /// <summary>
    /// A lap has come in. Returns the watch worth saying, or null - which
    /// is almost always.
    ///
    /// ESCALATION ONLY. The highest level said about a driver this race is
    /// kept, and a new one is returned only when it is strictly higher. So
    /// a driver who comes within a hair, drops off, then comes back is not
    /// announced twice, and the three levels read as one story getting
    /// closer rather than a meter twitching.
    /// </summary>
    public RecordThreat? Saw(LapSeen lap)
    {
        if (string.IsNullOrWhiteSpace(lap.DriverId) || lap.LapSeconds <= 0 || lap.LapSeconds > LongestCredibleLap)
        {
            return null;
        }

        var record = LayoutRecord?.Invoke();

        // Every lap is still REMEMBERED without a record standing, so that
        // a record arriving mid-race (a layout picked late, a store that
        // was mid-save on the first lap) finds a pace already built rather
        // than starting from nothing.
        Remember(lap);

        if (record is not { Seconds: > 0 } mark)
        {
            return null;
        }

        // The record has gone. PaceWatch calls that, and from here on this
        // driver has nothing left to be close to.
        if (lap.LapSeconds < mark.Seconds)
        {
            _recordGone.Add(lap.DriverId);

            return null;
        }

        if (_recordGone.Contains(lap.DriverId))
        {
            return null;
        }

        var laps = _recent[lap.DriverId];
        var seen = Seen(lap.DriverId);

        if (seen <= WarmUpLaps)
        {
            return null;
        }

        var best = _bestThisRace[lap.DriverId];
        var level = Level(laps, best, mark.Seconds, out var inside);

        if (level == RecordThreatLevel.None)
        {
            return null;
        }

        if (_said.TryGetValue(lap.DriverId, out var already) && already >= level)
        {
            return null;
        }

        _said[lap.DriverId] = level;

        return new RecordThreat(
            lap.DriverId,
            level,
            best,
            mark.Seconds,
            string.IsNullOrWhiteSpace(mark.Holder) ? null : mark.Holder,

            // Never negative - the record-has-gone case returned above, and
            // a rounding that put this a hair under nought would otherwise
            // produce "he needs minus nothing".
            Math.Max(0, best - mark.Seconds),
            inside,
            seen,
            Improving(laps));
    }

    /// <summary>
    /// Which band this driver's pace is in, and how many of their recent
    /// laps are inside it.
    ///
    /// Tested biggest first, so a driver who is level with the record is
    /// told about that and not about closing on it - the same rule
    /// PaceNoteKind's ordering exists for: one lap can be several true
    /// things and the announcer says the largest.
    /// </summary>
    private RecordThreatLevel Level(List<double> laps, double best, double record, out int inside)
    {
        var need = Math.Max(1, InsideToCount);

        var atTheMark = Inside(laps, record, LevelWithin);

        // LEVEL WITH IT needs their BEST to be there as well as the pace.
        // Pace alone within a quarter of a percent is the doorstep; "one
        // more like that" is a claim about a lap that actually happened.
        if (best <= record * (1 + LevelWithin) && atTheMark >= need)
        {
            inside = atTheMark;

            return RecordThreatLevel.OneMoreLikeThat;
        }

        var doorstep = Inside(laps, record, DoorstepWithin);

        if (doorstep >= need)
        {
            inside = doorstep;

            return RecordThreatLevel.OnTheDoorstep;
        }

        var closing = Inside(laps, record, ClosingWithin);

        // CLOSING ALSO NEEDS THEM TO BE GETTING QUICKER, and the other two
        // do not. Five percent off a record is a lot of cars on a good
        // night; what makes it worth a word is a driver who is still
        // finding time. Level with the record is worth saying whether they
        // improve again or not.
        if (closing >= need && Improving(laps))
        {
            inside = closing;

            return RecordThreatLevel.Closing;
        }

        inside = 0;

        return RecordThreatLevel.None;
    }

    private static int Inside(List<double> laps, double record, double within)
    {
        var ceiling = record * (1 + within);
        var inside = 0;

        foreach (var lap in laps)
        {
            if (lap <= ceiling)
            {
                inside++;
            }
        }

        return inside;
    }

    /// <summary>
    /// Whether the rolling window is trending quicker - its last lap
    /// against its first.
    ///
    /// The two ends rather than a fitted line, because the window is three
    /// laps long and a line through three points is a slope with no more
    /// information in it than the ends have. A window with fewer than two
    /// laps in it is not improving; it is one lap.
    /// </summary>
    private static bool Improving(List<double> laps) =>
        laps.Count >= 2 && laps[^1] < laps[0];

    private void Remember(LapSeen lap)
    {
        if (!_recent.TryGetValue(lap.DriverId, out var laps))
        {
            laps = new List<double>();
            _recent[lap.DriverId] = laps;
        }

        laps.Add(lap.LapSeconds);

        while (laps.Count > Math.Max(1, RollingLaps))
        {
            laps.RemoveAt(0);
        }

        _seen[lap.DriverId] = Seen(lap.DriverId) + 1;

        if (!_bestThisRace.TryGetValue(lap.DriverId, out var best) || lap.LapSeconds < best)
        {
            _bestThisRace[lap.DriverId] = lap.LapSeconds;
        }
    }

    /// <summary>How many laps this driver has turned this race. For the console and the tests.</summary>
    public int Seen(string driverId) => _seen.GetValueOrDefault(driverId);

    /// <summary>
    /// A new race. Everything about the session goes; what the layout
    /// holds does not, because that is read fresh every time and belongs
    /// to the store behind it - the same split PaceWatch.Reset makes.
    /// </summary>
    public void Reset()
    {
        _recent.Clear();
        _bestThisRace.Clear();
        _said.Clear();
        _recordGone.Clear();
        _seen.Clear();
    }

    /// <summary>
    /// The line itself, so the wording cannot drift between the places
    /// that say it - internal on purpose, which is the argument
    /// DriverCut.Describe makes for itself.
    ///
    /// EVERY ONE OF THESE IS A SUBTRACTION AND NOT A FORECAST. Read them
    /// against the rule at the top of this class: there is no sentence
    /// here that is false if the driver's next lap is a spin.
    /// </summary>
    internal static string Say(RecordThreat threat, string named)
    {
        var record = RaceNumbers.LapSpoken((long)Math.Round(threat.Record * 1000));
        var best = RaceNumbers.LapSpoken((long)Math.Round(threat.BestThisRace * 1000));
        var holder = threat.Holder is { Length: > 0 } who ? $"{who}'s " : string.Empty;

        return threat.Level switch
        {
            RecordThreatLevel.OneMoreLikeThat =>
                $"{named} has just matched the record. {holder}mark is {record}, {named} has been round in "
                + $"{best} - one more like that and it is gone.",

            RecordThreatLevel.OnTheDoorstep =>
                $"{named} is on the record's doorstep - {Words(threat.InsideTheMargin)} of the last few laps "
                + $"within a whisker of {holder}{record}, and {RaceNumbers.GapSpoken(threat.Needed)} still to "
                + "find.",

            RecordThreatLevel.Closing =>
                $"Keep an eye on {named} - {best} best so far against {holder}record of {record}, and the laps "
                + "are still coming down.",

            _ => string.Empty,
        };
    }

    /// <summary>
    /// A count in words for <see cref="Say"/>, floored at one.
    ///
    /// Floored because every level needs at least <see cref="InsideToCount"/>
    /// laps inside the band to be reached at all, so a nought here would
    /// mean the level and the count disagreed - and the sentence a room
    /// would hear is "nought of the last few laps within a whisker of the
    /// record", which is the announcer contradicting itself mid-line.
    /// </summary>
    private static string Words(int laps) => RaceNumbers.Words(Math.Max(1, laps));
}

