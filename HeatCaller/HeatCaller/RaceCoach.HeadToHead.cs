namespace HeatCaller;

/// <summary>
/// Head to Head's sums: who beats whom across the whole club, and what one
/// pair's record adds up to, in a sentence.
///
/// 13 Nov 2026, the rework's who-beats-whom grid (option D's Head to Head,
/// plans/race-analytics-rework-mockup.html). Kept here rather than in the
/// page so the rules can be tested without building a window.
///
/// The rules for which races count are the page's own, and have been since
/// Bug Hunt 8: demo races are left out (their results are simulated), ghost
/// cars are left out and the field closed up behind them (a ghost is a pace
/// reference, not an entrant), and a car that never turned a lap is not in
/// the race. Only races both drivers ran say anything about the two of them.
/// Drivers are matched by name, ignoring case, as the page always has.
/// </summary>
public static partial class RaceCoach
{
    /// <summary>What a cell of the grid counts.</summary>
    public enum Beat
    {
        /// <summary>Finished ahead in a race both ran.</summary>
        FinishedAhead,

        /// <summary>Set the quicker best lap in a race both ran. A tie counts for neither.</summary>
        QuickerBestLap,
    }

    /// <summary>How far back the page looks.</summary>
    public enum Since
    {
        AllTime,
        ThisYear,
        LastThreeMonths,
    }

    /// <summary>
    /// The races a driver needs, with at least one other real driver in
    /// them, to be a regular - a row in the grid. Fewer and a row is mostly
    /// blank squares and one lucky result.
    /// </summary>
    public const int RegularRaces = 3;

    /// <summary>The most drivers the grid shows. Past a dozen the squares are too small to read; the rest are picked by name.</summary>
    public const int MostInGrid = 12;

    /// <summary>A row driver who wins this share or more of their races with the column driver is tinted green.</summary>
    public const double WinningShare = 0.6;

    /// <summary>This share or less is tinted red. Between the two is amber.</summary>
    public const double LosingShare = 0.4;

    /// <summary>One real driver's result in a race, with their place once ghosts and cars that never moved are taken out.</summary>
    public sealed record Finisher(SessionResultEntry Entry, int Place)
    {
        public string Name => Entry.DriverName;
    }

    /// <summary>A race as Head to Head reads it: when, where, and the real drivers in finishing order.</summary>
    public sealed record SharedField(SessionRecord Session, IReadOnlyList<Finisher> Finishers)
    {
        public Finisher? Find(string name) =>
            Finishers.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>The quickest single lap anyone real set in this race, or 0.</summary>
        public long FastestLapMs =>
            Finishers.Where(f => f.Entry.FastLapMs > 0).Select(f => f.Entry.FastLapMs).DefaultIfEmpty(0).Min();
    }

    /// <summary>The earliest start a "since" choice lets in, in UTC, or null for all time.</summary>
    public static DateTime? SinceUtc(Since since, DateTime nowLocal) => since switch
    {
        Since.ThisYear => new DateTime(nowLocal.Year, 1, 1, 0, 0, 0, DateTimeKind.Local).ToUniversalTime(),
        Since.LastThreeMonths => DateTime.SpecifyKind(nowLocal.Date.AddMonths(-3), DateTimeKind.Local).ToUniversalTime(),
        _ => null,
    };

    /// <summary>
    /// Every race Head to Head counts, oldest first: not a demo race, from
    /// <paramref name="sinceUtc"/> on, each with its real drivers in order.
    /// </summary>
    public static List<SharedField> Fields(IEnumerable<SessionRecord> sessions, DateTime? sinceUtc = null)
    {
        var fields = new List<SharedField>();

        foreach (var session in sessions.OrderBy(s => s.StartedAtUtc))
        {
            if (session.IsNotScored() || sinceUtc is DateTime since && session.StartedAtUtc < since)
            {
                continue;
            }

            var real = session.Results
                .Where(r => !GhostService.IsGhost(r.Transponder) && r.Laps >= 1 && !string.IsNullOrWhiteSpace(r.DriverName))
                .OrderBy(r => r.Position)
                .Select((r, i) => new Finisher(r, i + 1))
                .ToList();

            if (real.Count > 0)
            {
                fields.Add(new SharedField(session, real));
            }
        }

        return fields;
    }

    /// <summary>
    /// Whether the row driver beat the column driver in one race, on the
    /// measure: true, false, or null when the race does not count for it
    /// (a missing lap time, a tied best lap).
    /// </summary>
    public static bool? RowBeat(Finisher row, Finisher column, Beat beat)
    {
        if (beat == Beat.FinishedAhead)
        {
            return row.Place < column.Place;
        }

        if (row.Entry.FastLapMs <= 0 || column.Entry.FastLapMs <= 0 || row.Entry.FastLapMs == column.Entry.FastLapMs)
        {
            return null;
        }

        return row.Entry.FastLapMs < column.Entry.FastLapMs;
    }

    /// <summary>One square of the grid: the row driver's wins and losses against the column driver.</summary>
    public readonly record struct Cell(int Wins, int Losses)
    {
        public int Races => Wins + Losses;

        /// <summary>The row driver's share of the races, or null with none.</summary>
        public double? Share => Races == 0 ? null : Wins / (double)Races;
    }

    /// <summary>How a square is tinted.</summary>
    public enum Tint
    {
        None,
        Winning,
        Even,
        Losing,
    }

    /// <summary>Green at 60% or more, red at 40% or less, amber between, nothing with no races.</summary>
    public static Tint TintOf(Cell cell) => cell.Share switch
    {
        null => Tint.None,
        >= WinningShare => Tint.Winning,
        <= LosingShare => Tint.Losing,
        _ => Tint.Even,
    };

    /// <summary>
    /// Who beats whom: the regulars, strongest first, and every pair's
    /// record. <see cref="Others"/> are the regulars past the cap, there to
    /// be picked by name.
    /// </summary>
    public sealed class Matrix
    {
        public required IReadOnlyList<string> Drivers { get; init; }

        public required Cell[,] Cells { get; init; }

        public required IReadOnlyList<string> Others { get; init; }

        public Cell this[int row, int column] => Cells[row, column];

        public int IndexOf(string name) =>
            Drivers.ToList().FindIndex(d => string.Equals(d, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The grid, from the races Head to Head counts (see <see cref="Fields"/>).
    ///
    /// A regular has raced at least <see cref="RegularRaces"/> times with
    /// somebody else. Drivers the club keeps out of the records in their
    /// profile - rentals, guests - are left out, by transponder, as the
    /// Overview's records leave them out (AchievementEvaluator.CanHoldTrackRecord).
    /// They still took their places in the race, so the drivers round them
    /// keep the order they finished in.
    ///
    /// Strongest is the best share of races won against the other regulars,
    /// on the measure; more races breaks a tie, then the name.
    /// </summary>
    public static Matrix BuildMatrix(IReadOnlyList<SharedField> fields, Beat beat, ProfileStore? profiles = null, int most = MostInGrid)
    {
        bool Shown(Finisher f) => AchievementEvaluator.CanHoldTrackRecord(f.Entry.Transponder, profiles);

        // Each name once, spelt as it was last raced.
        var spelling = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var races = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in fields)
        {
            if (field.Finishers.Count < 2)
            {
                continue;
            }

            foreach (var f in field.Finishers)
            {
                if (!Shown(f))
                {
                    hidden.Add(f.Name);
                    continue;
                }

                spelling[f.Name] = f.Name.Trim();
                races[f.Name] = races.GetValueOrDefault(f.Name) + 1;
            }
        }

        var regulars = races
            .Where(kv => kv.Value >= RegularRaces && !hidden.Contains(kv.Key))
            .Select(kv => spelling[kv.Key])
            .ToList();

        var all = Tally(fields, regulars, beat, Shown);

        var ranked = Enumerable.Range(0, regulars.Count)
            .Select(i =>
            {
                var wins = 0;
                var total = 0;

                for (var j = 0; j < regulars.Count; j++)
                {
                    wins += all[i, j].Wins;
                    total += all[i, j].Races;
                }

                return (Name: regulars[i], Share: total == 0 ? 0 : wins / (double)total, Total: total);
            })
            .OrderByDescending(x => x.Share)
            .ThenByDescending(x => x.Total)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Name)
            .ToList();

        var shown = ranked.Take(Math.Max(0, most)).ToList();

        return new Matrix
        {
            Drivers = shown,
            Cells = Tally(fields, shown, beat, Shown),
            Others = ranked.Skip(shown.Count).ToList(),
        };
    }

    /// <summary>Every pair's record among <paramref name="names"/>, row against column.</summary>
    private static Cell[,] Tally(IReadOnlyList<SharedField> fields, IReadOnlyList<string> names, Beat beat, Func<Finisher, bool> shown)
    {
        var wins = new int[names.Count, names.Count];
        var losses = new int[names.Count, names.Count];

        foreach (var field in fields)
        {
            var here = names.Select(n => field.Find(n) is { } f && shown(f) ? f : null).ToList();

            for (var i = 0; i < names.Count; i++)
            {
                for (var j = 0; j < names.Count; j++)
                {
                    if (i == j || here[i] is not { } row || here[j] is not { } column)
                    {
                        continue;
                    }

                    switch (RowBeat(row, column, beat))
                    {
                        case true: wins[i, j]++; break;
                        case false: losses[i, j]++; break;
                    }
                }
            }
        }

        var cells = new Cell[names.Count, names.Count];

        for (var i = 0; i < names.Count; i++)
        {
            for (var j = 0; j < names.Count; j++)
            {
                cells[i, j] = new Cell(wins[i, j], losses[i, j]);
            }
        }

        return cells;
    }

    /// <summary>
    /// One pair's record, A against B: the races both ran, and the
    /// measures that decide a matchup.
    /// </summary>
    public sealed record PairRecord
    {
        public required string A { get; init; }
        public required string B { get; init; }

        /// <summary>Races both ran.</summary>
        public int Shared { get; init; }

        public int AheadA { get; init; }
        public int AheadB { get; init; }

        /// <summary>Races where each set the quicker best lap of the two. Ties count for neither.</summary>
        public int QuickerA { get; init; }
        public int QuickerB { get; init; }

        /// <summary>Races where each set the fastest lap of the whole race.</summary>
        public int FastestOfRaceA { get; init; }
        public int FastestOfRaceB { get; init; }

        /// <summary>Finished ahead in the last five races both ran.</summary>
        public int LastFiveA { get; init; }
        public int LastFiveB { get; init; }

        /// <summary>Mistakes a race (RaceCoach.MistakeLaps), on average over the shared races.</summary>
        public double MistakesA { get; init; }
        public double MistakesB { get; init; }

        /// <summary>The track they shared most, and each one's best lap there in those races (0 for none).</summary>
        public string Track { get; init; } = string.Empty;
        public long BestA { get; init; }
        public long BestB { get; init; }

        /// <summary>The record on a measure, A's side first.</summary>
        public (int A, int B) On(Beat beat) => beat == Beat.QuickerBestLap ? (QuickerA, QuickerB) : (AheadA, AheadB);
    }

    /// <summary>A against B over the races Head to Head counts (see <see cref="Fields"/>).</summary>
    public static PairRecord Pair(IReadOnlyList<SharedField> fields, string a, string b)
    {
        int shared = 0, aheadA = 0, aheadB = 0, quickA = 0, quickB = 0, fastA = 0, fastB = 0;
        var mistakesA = 0;
        var mistakesB = 0;
        var aheadInOrder = new List<bool>();
        var tracks = new Dictionary<string, (int Races, long BestA, long BestB)>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in fields)
        {
            if (field.Find(a) is not { } fa || field.Find(b) is not { } fb || ReferenceEquals(fa, fb))
            {
                continue;
            }

            shared++;

            var aAhead = fa.Place < fb.Place;
            aheadInOrder.Add(aAhead);
            if (aAhead) { aheadA++; } else { aheadB++; }

            switch (RowBeat(fa, fb, Beat.QuickerBestLap))
            {
                case true: quickA++; break;
                case false: quickB++; break;
            }

            var fastest = field.FastestLapMs;
            if (fastest > 0 && fa.Entry.FastLapMs == fastest) { fastA++; }
            if (fastest > 0 && fb.Entry.FastLapMs == fastest) { fastB++; }

            mistakesA += MistakeLaps(Laps(fa.Entry)).Count;
            mistakesB += MistakeLaps(Laps(fb.Entry)).Count;

            var track = field.Session.TrackLayoutName?.Trim() ?? string.Empty;
            var (count, bestA, bestB) = tracks.GetValueOrDefault(track);

            tracks[track] = (count + 1, Quicker(bestA, fa.Entry.FastLapMs), Quicker(bestB, fb.Entry.FastLapMs));
        }

        var top = tracks
            .Where(kv => kv.Key.Length > 0)
            .OrderByDescending(kv => kv.Value.Races)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => (Name: kv.Key, kv.Value.BestA, kv.Value.BestB))
            .FirstOrDefault();

        var lastFive = aheadInOrder.TakeLast(5).ToList();

        return new PairRecord
        {
            A = a,
            B = b,
            Shared = shared,
            AheadA = aheadA,
            AheadB = aheadB,
            QuickerA = quickA,
            QuickerB = quickB,
            FastestOfRaceA = fastA,
            FastestOfRaceB = fastB,
            LastFiveA = lastFive.Count(x => x),
            LastFiveB = lastFive.Count(x => !x),
            MistakesA = shared == 0 ? 0 : mistakesA / (double)shared,
            MistakesB = shared == 0 ? 0 : mistakesB / (double)shared,
            Track = top.Name ?? string.Empty,
            BestA = top.BestA,
            BestB = top.BestB,
        };
    }

    private static long Quicker(long kept, long lap) => lap <= 0 ? kept : kept <= 0 ? lap : Math.Min(kept, lap);

    /// <summary>"Leo 11-3", "Level 7-7": who leads a measure, and by what. The leader's count first.</summary>
    public static string Leads(string a, string b, int forA, int forB)
    {
        var (shortA, shortB) = ShortNames(a, b);

        return forA == forB ? $"Level {forA}–{forB}"
            : forA > forB ? $"{shortA} {forA}–{forB}"
            : $"{shortB} {forB}–{forA}";
    }

    /// <summary>
    /// The best-lap gap on the track they shared most: who, by how much,
    /// and in car lengths when the layout has a length. Null without two
    /// best laps there.
    /// </summary>
    public static string? BestLapGap(PairRecord pair, double? trackFeet)
    {
        if (pair.BestA <= 0 || pair.BestB <= 0)
        {
            return null;
        }

        if (pair.BestA == pair.BestB)
        {
            return "Dead level";
        }

        var gap = Math.Abs(pair.BestA - pair.BestB);
        var (shortA, shortB) = ShortNames(pair.A, pair.B);
        var who = pair.BestA < pair.BestB ? shortA : shortB;
        var lengths = SayLengths(gap, Math.Min(pair.BestA, pair.BestB), trackFeet);

        return lengths is null
            ? $"{who} by {gap / 1000.0:0.000}s"
            : $"{who} by {gap / 1000.0:0.000}s, {lengths}";
    }

    /// <summary>
    /// What a matchup adds up to, said by the race engineer: who has the
    /// pace, who gets the results, and why, when the two disagree. By name
    /// rather than "you", since the club's laptop is read by both of them.
    /// </summary>
    public static string Reading(PairRecord pair, double? trackFeet = null)
    {
        var (a, b) = ShortNames(pair.A, pair.B);
        var n = pair.Shared;

        if (n == 0)
        {
            return $"{a} and {b} have not raced each other yet.";
        }

        if (n < RegularRaces)
        {
            return $"Only {n} {Races(n)} together so far. Too few to call.";
        }

        var lapped = pair.QuickerA + pair.QuickerB;
        var pace = pair.QuickerA == pair.QuickerB ? null : pair.QuickerA > pair.QuickerB ? a : b;
        var result = pair.AheadA == pair.AheadB ? null : pair.AheadA > pair.AheadB ? a : b;

        var paceWins = Math.Max(pair.QuickerA, pair.QuickerB);
        var resultWins = Math.Max(pair.AheadA, pair.AheadB);

        // The pace and the results point different ways: that is the story.
        if (pace is not null && result is not null && pace != result)
        {
            // A quarter of a mistake a race more is the least that reads
            // as a pattern rather than one bad night.
            var paceMistakes = pace == a ? pair.MistakesA : pair.MistakesB;
            var resultMistakes = result == a ? pair.MistakesA : pair.MistakesB;
            var why = paceMistakes > resultMistakes + 0.25
                ? $"because {pace}'s races have more mistakes in them."
                : $"so {result} is winning on racecraft, not raw speed.";

            return $"{pace} has had the quicker lap in {paceWins} of {n} races. {result} finished ahead in {resultWins}, {why}";
        }

        // Even at the flag.
        if (result is null || resultWins / (double)n < WinningShare)
        {
            var who = result is null ? "Dead level" : $"{result} just shades it";

            return pace is null
                ? $"{who} at the flag, {pair.AheadA}–{pair.AheadB}, and the pace is even too. Every race is a fresh fight."
                : $"{who} at the flag, {Math.Max(pair.AheadA, pair.AheadB)}–{Math.Min(pair.AheadA, pair.AheadB)}, but {pace} has had the quicker lap in {paceWins} of {lapped}.";
        }

        // One driver has the results, and the pace too or the pace is even.
        var loser = result == a ? b : a;
        var tail = pace == result && pair.Track.Length > 0 && Lengths(pair, trackFeet) is { } lengths
            ? $" On {pair.Track} the best laps are {lengths} apart."
            : string.Empty;

        return pace == result
            ? $"{result} has the pace and the results: quicker lap in {paceWins} of {lapped}, ahead in {resultWins} of {n}.{tail}"
            : $"{result} finished ahead in {resultWins} of {n}. The pace is even, so {loser} loses it in the race, not on the lap.";
    }

    private static string? Lengths(PairRecord pair, double? trackFeet) =>
        pair.BestA <= 0 || pair.BestB <= 0 || pair.BestA == pair.BestB
            ? null
            : SayLengths(Math.Abs(pair.BestA - pair.BestB), Math.Min(pair.BestA, pair.BestB), trackFeet);

    private static string Races(int n) => n == 1 ? "race" : "races";

    /// <summary>
    /// What a pair are called in a sentence: each by their first name, or
    /// both in full when the first names are the same (two Sams).
    /// </summary>
    public static (string A, string B) ShortNames(string a, string b)
    {
        var (firstA, firstB) = (FirstName(a), FirstName(b));

        return string.Equals(firstA, firstB, StringComparison.OrdinalIgnoreCase) ? (a.Trim(), b.Trim()) : (firstA, firstB);
    }

    /// <summary>The name a club calls a driver by: the first word.</summary>
    public static string FirstName(string name)
    {
        var trimmed = name.Trim();
        var space = trimmed.IndexOf(' ');

        return space > 0 ? trimmed[..space] : trimmed;
    }
}
