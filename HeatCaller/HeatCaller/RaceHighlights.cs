namespace HeatCaller;

/// <summary>
/// Works out, from the race the app has already saved, which seconds of the
/// recording belong to each driver - so a driver can be handed the bit of
/// the night where they were called and nothing else.
///
/// There is deliberately NO second log of "what the announcer said and
/// when". One was the obvious thing to build and it would have been wrong
/// twice over: it would have had to be written from the audio path (which
/// AudioBus exists to keep clear of anything that can block), and it would
/// have been a second account of the race that could drift from the real one
/// the moment anybody changed a rule about laps. Everything here is
/// arithmetic on the record the app already keeps.
///
/// The record is each driver's lap times. RaceControlSession.Laps adds one
/// entry to RacerStanding.LapTimesMs for every counted pass, and its own
/// comment says the sum of that list is the driver's time since the green
/// ("lapTimeMs = Math.Max(0, elapsedSinceStart - standing.LapTimesMs.Sum())"
/// is how it fills a gap). So the running total of the list IS the moment of
/// each crossing, in milliseconds after the green. The same list is written
/// to the crash journal (RaceJournal.StandingRecord.LapTimesMs) and to the
/// saved result (SessionResultEntry.LapTimesMs), so a race recovered after a
/// crash gives exactly the same moments as one that finished cleanly.
///
/// ElapsedMs is NOT used for any of this, although it looks like the obvious
/// field. It carries the driver's penalties - the session folds PenaltyMs
/// into it on every counted lap - so a ten-second penalty would move a
/// driver's whole timeline ten seconds down the recording. The lap times are
/// penalty-free.
///
/// Where a driver gained a place is worked out the same way, by putting
/// every driver's crossings on one timeline. That is the same question
/// SessionResultEntry.LapsLed already answers and its comment is worth
/// repeating: "the running order between crossings isn't stored, so 'were
/// they ahead at the moment they finished that lap' is the only question a
/// saved result can answer honestly". This asks exactly that question and no
/// more - it does not pretend to know about a pass made and given back in
/// the middle of a lap.
/// </summary>
internal static class RaceHighlights
{
    /// <summary>Why a stretch of the recording belongs to a driver.</summary>
    internal enum Moment
    {
        /// <summary>The crossing that ended their best lap of the race - the fastest-lap call comes just after it.</summary>
        FastestLap,

        /// <summary>The crossing on which they took the lead from somebody else.</summary>
        Lead,

        /// <summary>A crossing on which they were further up the order than they were at the last one.</summary>
        Pass,

        /// <summary>A warning or a penalty, at the time race control gave it - see RaceWarning.RaceTimeMs.</summary>
        Warning,

        /// <summary>The top three, from the flag onwards: the win, the podium, the interview.</summary>
        Podium,
    }

    /// <summary>One moment worth keeping, in milliseconds after the green.</summary>
    /// <param name="Transponder">Whose moment it is.</param>
    /// <param name="What">Why this stretch belongs to them - see <see cref="Moment"/>.</param>
    /// <param name="RaceTimeMs">When, in milliseconds after the green.</param>
    /// <param name="Lap">
    /// Which lap it was on, where the moment is a crossing. Nought where it
    /// is not one: a warning is given at a time rather than on a lap, and a
    /// podium is the flag.
    ///
    /// ADDED 12 Oct 2026 and defaulted, so nothing that builds a Mark had to
    /// change. The timeline walk below had the lap in hand all along and
    /// threw it away, and it is the one thing that tells a pass for the lead
    /// on the LAST lap - the best moment in racing - from one on lap three
    /// that was given back on lap four. See NightMoments.
    /// </param>
    internal sealed record Mark(string Transponder, Moment What, long RaceTimeMs, int Lap = 0);

    /// <summary>A stretch of the recording, in milliseconds from the start of the FILE (not from the green).</summary>
    internal sealed record Window(long StartMs, long EndMs)
    {
        public long LengthMs => Math.Max(0, EndMs - StartMs);
    }

    /// <summary>One driver's clip: the stretches of the recording that go into it, in order.</summary>
    internal sealed record DriverClip(
        string Transponder,
        string DriverName,
        int Position,
        List<Window> Windows,
        List<Moment> Moments)
    {
        public long LengthMs => Windows.Sum(w => w.LengthMs);
    }

    /// <summary>
    /// How long before a moment the clip starts.
    ///
    /// The call lands AFTER the crossing - the announcer has to be told a lap
    /// happened before it can say so - but a clip that starts on the call
    /// starts mid-sentence, because whatever was being said about the car in
    /// front is still finishing. Seven seconds back is the lap approaching.
    /// </summary>
    public const long LeadInMs = 7000;

    /// <summary>How long after a moment the clip runs: long enough for the call itself and the line after it.</summary>
    public const long TailMs = 11000;

    /// <summary>Two stretches closer together than this become one, rather than being cut apart and butted back together with a seam the ear hears.</summary>
    public const long JoinGapMs = 2000;

    /// <summary>From the flag, how much of the end of the broadcast the top three get: the finish call, the result and the interview.</summary>
    public const long PodiumTailMs = 60000;

    /// <summary>
    /// The most one driver's clip may run to.
    ///
    /// A driver who passed somebody on every lap of a twenty-minute main
    /// would otherwise be handed most of the race back, which is not a
    /// highlight and is twenty times the file. Past this, whole stretches
    /// are dropped from the back - never cut in the middle of one.
    /// </summary>
    public const long MaxPerDriverMs = 150_000;

    /// <summary>The fewest moments kept per driver before the length cap starts dropping them, strongest first - see <see cref="Rank"/>.</summary>
    public const int MaxMarksPerDriver = 8;

    /// <summary>A clip shorter than this is not written: it would be a fragment with no call in it.</summary>
    public const long MinClipMs = 2500;

    /// <summary>
    /// The running total of a driver's lap times: the moment of each
    /// crossing, in milliseconds after the green.
    ///
    /// <paramref name="offsetMs"/> is the driver's release offset, which is
    /// zero in an ordinary race and is how long after the green a staggered
    /// or qualifying start let this driver go (RacerStanding.ReleaseOffsetMs).
    /// A negative lap time - which should not happen, but a corrected result
    /// could carry one - is treated as zero rather than winding the timeline
    /// backwards.
    /// </summary>
    public static List<long> CrossingTimesMs(IReadOnlyList<long>? lapTimesMs, long offsetMs = 0)
    {
        var times = new List<long>();

        if (lapTimesMs is null)
        {
            return times;
        }

        var at = Math.Max(0, offsetMs);

        foreach (var lap in lapTimesMs)
        {
            at += Math.Max(0, lap);
            times.Add(at);
        }

        return times;
    }

    /// <summary>
    /// Which lap was the driver's best, as an index into their lap times, or
    /// -1 when they never set a timed one.
    ///
    /// Index 0 is skipped, and for the same reason the session skips it:
    /// "the first counted pass is NOT a lap", it runs from the green to the
    /// car's first crossing, and allowing it to win made a 1.4s dash off the
    /// grid the fastest lap of the race. Working it out here rather than
    /// reading FastLapMs means the ANSWER IS AN INDEX - which lap it was -
    /// and the index is what gives the moment.
    /// </summary>
    public static int BestLapIndex(IReadOnlyList<long>? lapTimesMs)
    {
        if (lapTimesMs is null)
        {
            return -1;
        }

        var best = -1;

        for (var i = 1; i < lapTimesMs.Count; i++)
        {
            if (lapTimesMs[i] <= 0)
            {
                continue;
            }

            if (best < 0 || lapTimesMs[i] < lapTimesMs[best])
            {
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// A driver's release offset, or zero.
    ///
    /// Zero in an ordinary race. In a staggered or qualifying start each
    /// driver is let go at their own moment and their lap times run from
    /// there, so without this every one of their moments would land that
    /// much too early in the recording - up to a minute for the back of a
    /// staggered grid. The offsets are not on the saved result, so the
    /// caller reads them off the board at the flag
    /// (RacerStanding.ReleaseOffsetMs, which the crash journal stores too)
    /// and hands them in.
    /// </summary>
    private static long OffsetOf(IReadOnlyDictionary<string, long>? releaseOffsetsMs, string transponder) =>
        releaseOffsetsMs is not null && releaseOffsetsMs.TryGetValue(transponder, out var offset) ? offset : 0;

    /// <summary>When the last car to be counted crossed, in ms after the green - where the finish call and everything after it begins. Zero when nobody completed a lap.</summary>
    public static long FlagAtMsFrom(SessionRecord record, IReadOnlyDictionary<string, long>? releaseOffsetsMs = null)
    {
        var flagAt = 0L;

        foreach (var row in record.Results)
        {
            var times = CrossingTimesMs(row.LapTimesMs, OffsetOf(releaseOffsetsMs, row.Transponder));

            if (times.Count > 0 && times[^1] > flagAt)
            {
                flagAt = times[^1];
            }
        }

        return flagAt;
    }

    /// <summary>Every moment in the race worth a driver's while, from the saved result alone.</summary>
    /// <param name="releaseOffsetsMs">Each driver's release offset - see <see cref="OffsetOf"/>. Null, or missing a driver, means they went at the green.</param>
    public static List<Mark> MarksFrom(SessionRecord record, IReadOnlyDictionary<string, long>? releaseOffsetsMs = null)
    {
        var marks = new List<Mark>();
        var crossings = new Dictionary<string, List<long>>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in record.Results)
        {
            if (string.IsNullOrWhiteSpace(row.Transponder))
            {
                continue;
            }

            var times = CrossingTimesMs(row.LapTimesMs, OffsetOf(releaseOffsetsMs, row.Transponder));
            crossings[row.Transponder] = times;

            var best = BestLapIndex(row.LapTimesMs);

            if (best >= 0 && best < times.Count)
            {
                marks.Add(new Mark(row.Transponder, Moment.FastestLap, times[best], best + 1));
            }

            // Already timestamped by whoever gave it, in the same clock -
            // RaceWarning.RaceTimeMs is "how far into the race it was given,
            // in ms", and zero means before the green, which is not a moment.
            foreach (var warning in row.Warnings)
            {
                if (warning.RaceTimeMs > 0)
                {
                    marks.Add(new Mark(row.Transponder, Moment.Warning, warning.RaceTimeMs));
                }
            }
        }

        marks.AddRange(PassesFrom(crossings));

        var flagAt = FlagAtMsFrom(record, releaseOffsetsMs);

        if (flagAt > 0)
        {
            foreach (var row in record.Results.Where(r => r.Position is >= 1 and <= 3))
            {
                if (!string.IsNullOrWhiteSpace(row.Transponder))
                {
                    marks.Add(new Mark(row.Transponder, Moment.Podium, flagAt));
                }
            }
        }

        return marks;
    }

    /// <summary>
    /// Every crossing on one timeline, and who moved up the order on it.
    ///
    /// The order after a crossing is the one the board shows: most laps
    /// first, and within the same lap count whoever got there first. That is
    /// RaceControlSession.GetOrderedStandings' rule with the penalties left
    /// out, on purpose - see the note at the top of this file.
    /// </summary>
    private static List<Mark> PassesFrom(Dictionary<string, List<long>> crossings)
    {
        var marks = new List<Mark>();

        var events = crossings
            .SelectMany(driver => driver.Value.Select((at, index) => (Transponder: driver.Key, Lap: index + 1, At: at)))
            .OrderBy(crossing => crossing.At)
            .ThenBy(crossing => crossing.Transponder, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var laps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var crossedAt = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var wasAt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var leader = string.Empty;

        foreach (var crossing in events)
        {
            laps[crossing.Transponder] = crossing.Lap;
            crossedAt[crossing.Transponder] = crossing.At;

            var order = laps.Keys
                .OrderByDescending(transponder => laps[transponder])
                .ThenBy(transponder => crossedAt[transponder])
                .ToList();

            var now = order.FindIndex(transponder =>
                string.Equals(transponder, crossing.Transponder, StringComparison.OrdinalIgnoreCase)) + 1;

            if (wasAt.TryGetValue(crossing.Transponder, out var before) && now > 0 && now < before)
            {
                marks.Add(new Mark(crossing.Transponder, Moment.Pass, crossing.At, crossing.Lap));
            }

            if (now == 1 && !string.Equals(leader, crossing.Transponder, StringComparison.OrdinalIgnoreCase))
            {
                // The FIRST leader is not a lead change. Being first across
                // on lap one is the grid doing what the grid does, and there
                // is no call for it; every leader after that is a call.
                if (leader.Length > 0)
                {
                    marks.Add(new Mark(crossing.Transponder, Moment.Lead, crossing.At, crossing.Lap));
                }

                leader = crossing.Transponder;
            }

            for (var i = 0; i < order.Count; i++)
            {
                wasAt[order[i]] = i + 1;
            }
        }

        return marks;
    }

    /// <summary>
    /// Strongest moment first, for when a driver has more than a clip's
    /// worth. A podium is never dropped; a warning is the first to go.
    /// </summary>
    private static int Rank(Moment what) => what switch
    {
        Moment.Podium => 0,
        Moment.Lead => 1,
        Moment.FastestLap => 2,
        Moment.Pass => 3,
        _ => 4,
    };

    /// <summary>
    /// One clip plan per driver who has anything worth keeping.
    /// </summary>
    /// <param name="greenAtMs">How far into the RECORDING the green dropped. The recording starts at the countdown, so this is usually a few seconds.</param>
    /// <param name="lengthMs">How long the recording actually is. Everything is clamped to it, so a race cut short never asks for audio that is not there.</param>
    public static List<DriverClip> ClipsFor(
        SessionRecord record,
        IReadOnlyList<Mark> marks,
        long greenAtMs,
        long lengthMs)
    {
        var clips = new List<DriverClip>();

        if (lengthMs <= 0)
        {
            return clips;
        }

        var byDriver = marks
            .Where(mark => !string.IsNullOrWhiteSpace(mark.Transponder))
            .GroupBy(mark => mark.Transponder, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var row in record.Results)
        {
            if (string.IsNullOrWhiteSpace(row.Transponder)
                || !byDriver.TryGetValue(row.Transponder, out var theirs)
                || theirs.Count == 0)
            {
                continue;
            }

            var kept = theirs
                .OrderBy(mark => Rank(mark.What))
                .ThenBy(mark => mark.RaceTimeMs)
                .Take(MaxMarksPerDriver)
                .ToList();

            var windows = Merge(kept.Select(mark => WindowFor(mark, greenAtMs, lengthMs)), JoinGapMs);
            windows = WithinBudget(windows, MaxPerDriverMs);

            var total = windows.Sum(window => window.LengthMs);

            if (total < MinClipMs)
            {
                continue;
            }

            clips.Add(new DriverClip(
                row.Transponder,
                string.IsNullOrWhiteSpace(row.DriverName) ? row.Transponder : row.DriverName,
                row.Position,
                windows,
                kept.Select(mark => mark.What).Distinct().OrderBy(Rank).ToList()));
        }

        return clips;
    }

    /// <summary>
    /// The stretch of the recording one moment asks for, already clamped to
    /// the file. A podium runs from the flag to the end of the broadcast
    /// rather than for a fixed tail, since that is where the interview is.
    /// </summary>
    private static Window WindowFor(Mark mark, long greenAtMs, long lengthMs)
    {
        var at = greenAtMs + mark.RaceTimeMs;

        var start = mark.What == Moment.Podium ? at : at - LeadInMs;
        var end = at + (mark.What == Moment.Podium ? PodiumTailMs : TailMs);

        return new Window(Math.Clamp(start, 0, lengthMs), Math.Clamp(end, 0, lengthMs));
    }

    /// <summary>Overlapping or near-touching stretches become one, in time order. Empty ones are dropped.</summary>
    public static List<Window> Merge(IEnumerable<Window> windows, long joinGapMs)
    {
        var merged = new List<Window>();

        foreach (var window in windows.Where(w => w.LengthMs > 0).OrderBy(w => w.StartMs).ThenBy(w => w.EndMs))
        {
            if (merged.Count > 0 && window.StartMs - merged[^1].EndMs <= joinGapMs)
            {
                if (window.EndMs > merged[^1].EndMs)
                {
                    merged[^1] = merged[^1] with { EndMs = window.EndMs };
                }

                continue;
            }

            merged.Add(window);
        }

        return merged;
    }

    /// <summary>
    /// As many whole stretches as fit the budget, earliest first. A stretch
    /// is never cut down to fit: half a call is worse than not having it, so
    /// the one that does not fit is dropped whole - unless it is the first,
    /// which is trimmed so a single very long stretch still gives something.
    /// </summary>
    public static List<Window> WithinBudget(IReadOnlyList<Window> windows, long budgetMs)
    {
        var kept = new List<Window>();
        var used = 0L;

        foreach (var window in windows)
        {
            if (used >= budgetMs)
            {
                break;
            }

            if (used + window.LengthMs <= budgetMs)
            {
                kept.Add(window);
                used += window.LengthMs;
                continue;
            }

            if (kept.Count == 0)
            {
                kept.Add(window with { EndMs = window.StartMs + budgetMs });
                used = budgetMs;
            }

            break;
        }

        return kept;
    }

    /// <summary>
    /// What every file from one race is called, before the extension:
    /// "2026-10-10 1942 - A Main - 2wd Stock".
    ///
    /// The date and the time of day first, so a folder sorts into the order
    /// the night ran, and so a file dragged out on its own still says when
    /// it is from without being opened - which is the whole requirement. The
    /// race's own name after it, through PlainFileName.SafePart, because a
    /// class name can contain a slash ("2wd/4wd") and Windows will not have
    /// one in a file name.
    /// </summary>
    public static string BaseNameFor(DateTime localStart, string? label) =>
        $"{localStart:yyyy-MM-dd HHmm} - {PlainFileName.SafePart(label, "Race")}";

    /// <summary>One driver's clip: the race's name, then their finishing position and name - "... - P1 Dave Smith".</summary>
    public static string ClipNameFor(string baseName, int position, string? driverName) =>
        $"{baseName} - P{Math.Max(1, position)} {PlainFileName.SafePart(driverName, "driver")}";

    /// <summary>
    /// A path in <paramref name="folder"/> that nothing is using yet.
    ///
    /// Two races can genuinely share a name and a minute - a race re-run
    /// straight away, or two heats of the same class started within the same
    /// sixty seconds - and the second must not write over the first.
    /// </summary>
    public static string UnusedPath(string folder, string name, string extension)
    {
        var path = Path.Combine(folder, name + extension);

        if (!File.Exists(path))
        {
            return path;
        }

        for (var attempt = 2; attempt <= 99; attempt++)
        {
            var next = Path.Combine(folder, $"{name} ({attempt}){extension}");

            if (!File.Exists(next))
            {
                return next;
            }
        }

        // Ninety-nine races of the same name in one minute is not a thing
        // that happens, but a name that cannot be found is, so this always
        // answers with something.
        return Path.Combine(folder, $"{name} ({DateTime.UtcNow:HHmmssfff}){extension}");
    }

    /// <summary>
    /// Where a moment in the recording is in the wav file's data, in bytes,
    /// rounded down to a whole sample - a byte offset halfway through a
    /// sample plays the file as noise from there on.
    /// </summary>
    public static long ByteOffsetFor(long ms, int sampleRate, int bytesPerSample)
    {
        if (ms <= 0)
        {
            return 0;
        }

        var bytes = ms * sampleRate * bytesPerSample / 1000;
        return bytes - (bytes % bytesPerSample);
    }
}
