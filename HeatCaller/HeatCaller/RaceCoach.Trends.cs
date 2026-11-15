namespace HeatCaller;

/// <summary>
/// The Trends tab's reading (13 Nov 2026, the Race Engineer rework): is the
/// driver still getting quicker on this track, how fast, and how that
/// compares with earlier - said in a sentence, with the curve the points are
/// following and where on it the driver is.
///
/// The rates come from the points themselves, a straight line through the
/// earlier half and another through the later half, not from the curve. A
/// fitted curve always flattens, because that is the shape it is; a driver
/// who is speeding up again would be told they are slowing down. The curve is
/// drawn for the eye, and the words are what the laps say.
/// </summary>
public static partial class RaceCoach
{
    /// <summary>The fewest sessions a trend is read from. Fewer is a handful of points, not a direction.</summary>
    public const int TrendLeastSessions = 4;

    /// <summary>Gains of at least this, in seconds a week, are still the big early drops.</summary>
    public const double LearningRate = 0.05;

    /// <summary>Gains under this, in seconds a week, are flat for a club racer: a hundredth a week is noise.</summary>
    public const double PlateauRate = 0.02;

    /// <summary>Sessions in a row without a new best before a flat line is called a plateau.</summary>
    public const int PlateauDrought = 3;

    /// <summary>One session's best lap, for the trend.</summary>
    public sealed record TrendSample(DateTime When, long BestLapMs);

    /// <summary>Where a driver is on the improvement curve.</summary>
    public enum CurveStage
    {
        /// <summary>Big drops from week to week: laps are still the quickest teacher.</summary>
        Learning,

        /// <summary>Still finding time, a little at a time.</summary>
        Refining,

        /// <summary>No new best for a while, and the line is flat.</summary>
        Plateau,
    }

    /// <summary>
    /// The curve through the points: best lap = A + B x ln(1 + days since the
    /// first session). A log curve because that is how learning a track goes
    /// - big drops at first, then less and less - and it has two numbers to
    /// fit, which is about all a dozen points can honestly support.
    /// </summary>
    public sealed record TrendFit(DateTime Origin, double A, double B)
    {
        /// <summary>The curve's lap time, in ms, at a moment.</summary>
        public double At(DateTime when) => A + B * Math.Log(1 + Math.Max(0, (when - Origin).TotalDays));
    }

    /// <summary>
    /// What a run of sessions on one track adds up to. Rates are seconds a
    /// week found (positive is quicker), or null when the stretch they cover
    /// is too short to call a week.
    /// </summary>
    public sealed record TrendReading(
        int Sessions,
        double? EarlyRate,
        double? RecentRate,
        string EarlyWhen,
        int SessionsSinceBest,
        DateTime BestWhen,
        CurveStage? Stage,
        TrendFit? Fit,
        int RecentSessions = 0);

    /// <summary>Reads a driver's sessions on one track, oldest first or not - they are put in order here.</summary>
    public static TrendReading ReadTrend(IEnumerable<TrendSample> samples)
    {
        var points = samples.Where(p => p.BestLapMs > 0).OrderBy(p => p.When).ToList();

        if (points.Count == 0)
        {
            return new TrendReading(0, null, null, string.Empty, 0, default, null, null);
        }

        var best = points.Min(p => p.BestLapMs);
        var bestIndex = points.FindLastIndex(p => p.BestLapMs == best);
        var sinceBest = points.Count - 1 - bestIndex;

        if (points.Count < TrendLeastSessions)
        {
            return new TrendReading(points.Count, null, null, string.Empty, sinceBest, points[bestIndex].When, null, null);
        }

        // Halves, so each rate rests on at least two points and an odd
        // middle one counts towards both. A half that falls inside one week
        // - every heat of last night, say - is widened until it spans one, so
        // eight heats over two nights a week apart still read as a trend.
        // Only when every session is within a week do the rates stay null.
        var half = (points.Count + 1) / 2;
        var earlyCount = half;
        while (earlyCount < points.Count && (points[earlyCount - 1].When - points[0].When).TotalDays < 7)
        {
            earlyCount++;
        }

        var recentCount = half;
        while (recentCount < points.Count && (points[^1].When - points[points.Count - recentCount].When).TotalDays < 7)
        {
            recentCount++;
        }

        var early = points.Take(earlyCount).ToList();
        var recent = points.Skip(points.Count - recentCount).ToList();

        var earlyRate = RatePerWeek(early);
        var recentRate = RatePerWeek(recent);

        return new TrendReading(
            points.Count,
            earlyRate,
            recentRate,
            Months(early),
            sinceBest,
            points[bestIndex].When,
            StageOf(recentRate, sinceBest),
            FitCurve(points),
            recent.Count);
    }

    /// <summary>
    /// Seconds a week found over a stretch of sessions: the slope of a
    /// straight line through them, turned round so quicker is positive. Null
    /// when the stretch is under a week, where a "per week" means nothing.
    /// </summary>
    public static double? RatePerWeek(IReadOnlyList<TrendSample> points)
    {
        if (points.Count < 2)
        {
            return null;
        }

        var origin = points[0].When;
        var days = points.Select(p => (p.When - origin).TotalDays).ToList();

        if (days.Max() - days.Min() < 7)
        {
            return null;
        }

        var slope = Slope(days, points.Select(p => (double)p.BestLapMs).ToList());

        // ms a day, to seconds a week, quicker positive.
        return -slope * 7 / 1000.0;
    }

    /// <summary>Learning, refining or on a plateau, from the recent rate and how long since the last best.</summary>
    public static CurveStage? StageOf(double? recentRate, int sessionsSinceBest)
    {
        if (recentRate is not double rate)
        {
            return null;
        }

        if (rate < PlateauRate && sessionsSinceBest >= PlateauDrought)
        {
            return CurveStage.Plateau;
        }

        return rate >= LearningRate ? CurveStage.Learning : CurveStage.Refining;
    }

    /// <summary>The log curve through the points (see TrendFit), or null for too few, or all on one day.</summary>
    public static TrendFit? FitCurve(IReadOnlyList<TrendSample> points)
    {
        if (points.Count < TrendLeastSessions)
        {
            return null;
        }

        var origin = points.Min(p => p.When);
        var x = points.Select(p => Math.Log(1 + (p.When - origin).TotalDays)).ToList();

        if (x.Max() - x.Min() <= 0)
        {
            return null;
        }

        var y = points.Select(p => (double)p.BestLapMs).ToList();
        var b = Slope(x, y);
        var a = y.Average() - b * x.Average();

        return new TrendFit(origin, a, b);
    }

    /// <summary>The headline for a trend: still improving, and how fast now against earlier.</summary>
    public static string TrendHeadline(TrendReading reading)
    {
        if (reading.Sessions == 0)
        {
            return "No sessions here yet.";
        }

        if (reading.Sessions < TrendLeastSessions)
        {
            return $"Only {Plural.Of(reading.Sessions, "session")} here so far. It takes {TrendLeastSessions} to see which way you're heading.";
        }

        if (reading.RecentRate is not double now)
        {
            return "These sessions are all within a week, too close together to call a weekly trend.";
        }

        var then = reading.EarlyRate;
        var when = string.IsNullOrEmpty(reading.EarlyWhen) ? "at first" : $"in {reading.EarlyWhen}";
        var next = reading.Stage switch
        {
            CurveStage.Learning => " Laps are still the quickest way to find time.",
            CurveStage.Refining => " The next tenth will come from setup more than from laps.",
            CurveStage.Plateau => $" No new best in {reading.SessionsSinceBest} sessions.",
            _ => string.Empty,
        };

        if (now < -PlateauRate)
        {
            return $"Slower lately: losing about {Rate(-now)} a week.{(reading.SessionsSinceBest > 0 ? $" Your best was {reading.SessionsSinceBest} sessions ago." : string.Empty)}";
        }

        if (now < PlateauRate)
        {
            // A hundredth a week is still a hundredth, and a driver who set
            // a best last week is not told they have stopped.
            if (then is double t && t >= PlateauRate)
            {
                return now >= 0.005
                    ? $"Still improving, but only just: {Rate(t)} a week {when}, {Rate(now)} a week now.{next}"
                    : $"Levelled off: {Rate(t)} a week {when}, next to nothing now.{next}";
            }

            return $"Flat: about the same pace all along.{next}";
        }

        if (then is not double earlier || earlier < PlateauRate)
        {
            return $"Getting quicker now: {Rate(now)} a week, after a flat start.{next}";
        }

        if (now < earlier * 0.7)
        {
            return $"Still improving, but slower: {Rate(earlier)} a week {when}, {Rate(now)} a week now.{next}";
        }

        if (now > earlier * 1.4)
        {
            return $"Improving faster than before: {Rate(earlier)} a week {when}, {Rate(now)} a week now.{next}";
        }

        return $"Still improving at a steady {Rate(now)} a week.{next}";
    }

    /// <summary>One thing to try, picked by where the driver is on the curve.</summary>
    public static string TrendDrill(TrendReading reading)
    {
        if (reading.Sessions < TrendLeastSessions || reading.RecentRate is null)
        {
            return "Race this track a few more nights. Four sessions is enough to see which way you're heading.";
        }

        if (reading.RecentRate < -PlateauRate)
        {
            return "Check the car before the driver. A tired motor, old tyres or a pack past its best can cost this much.";
        }

        return reading.Stage switch
        {
            CurveStage.Learning => "Keep the setup the same and get laps in. While the drops are this big, seat time is worth more than tuning.",
            CurveStage.Plateau => "Change one thing: a new line through your slowest corner, or one setup change. More laps of the same won't move this line.",
            _ => "One setup change per race night, no more, and let this line tell you which ones work.",
        };
    }

    /// <summary>What each stage means, as the "Where you are on the curve" card says it.</summary>
    public static string StageNote(CurveStage stage) => stage switch
    {
        CurveStage.Learning => $"Big drops each week, {Rate(LearningRate)} a week or more.",
        CurveStage.Refining => $"Still finding time, under {Rate(LearningRate)} a week.",
        _ => $"{PlateauDrought} sessions with no new best, under {Rate(PlateauRate)} a week.",
    };

    /// <summary>"0.11 s", for a rate in seconds.</summary>
    private static string Rate(double seconds) => $"{seconds:0.00} s";

    /// <summary>The month or months a stretch covers: "August", "August and September".</summary>
    private static string Months(IReadOnlyList<TrendSample> points)
    {
        var first = points[0].When;
        var last = points[^1].When;
        var culture = System.Globalization.CultureInfo.CurrentCulture;

        if (first.Year == last.Year && first.Month == last.Month)
        {
            return first.ToString("MMMM", culture);
        }

        var months = (last.Year - first.Year) * 12 + last.Month - first.Month;

        return months == 1
            ? $"{first.ToString("MMMM", culture)} and {last.ToString("MMMM", culture)}"
            : $"{first.ToString("MMMM", culture)} to {last.ToString("MMMM", culture)}";
    }

    /// <summary>The least-squares slope of y on x.</summary>
    private static double Slope(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        var mx = x.Average();
        var my = y.Average();
        var top = 0.0;
        var bottom = 0.0;

        for (var i = 0; i < x.Count; i++)
        {
            top += (x[i] - mx) * (y[i] - my);
            bottom += (x[i] - mx) * (x[i] - mx);
        }

        return bottom <= 0 ? 0 : top / bottom;
    }
}
