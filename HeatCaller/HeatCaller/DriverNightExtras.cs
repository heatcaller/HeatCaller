using System.Globalization;
using System.Text.Json.Serialization;

namespace HeatCaller;

/// <summary>
/// Everything in a driver's own night beyond DriverCut's facts and their
/// report card: their races one by one (with the laps for a chart and the
/// reasons for any penalty), the badges they are nearest, their best lap
/// over the last few nights, how their pace has moved tonight, and whether
/// tonight is their first night at the club.
/// </summary>
/// <remarks>
/// 22 Oct 2026 (driver experience, items 1, 2, 5, 6, 7 and 8). Each is a
/// field of its own in the night document rather than a line added to
/// `lines`, which stay DriverCut's facts word for word (see DriverNight) -
/// this is arithmetic over results already kept, and the relay page shows
/// each under a heading of its own.
///
/// Pure: everything it needs is handed in, so it is tested without a form,
/// a store on disk or a relay. MainForm.Relay.PublishNightsFor builds the
/// parts on the UI thread, where the stores are safe to read.
///
/// PRIVATE TO THE DRIVER. The penalty reasons in `races[].pen` are free
/// text a race director typed about a named person; they go only here
/// (badge-only) and on the Wi-Fi follow view in the room, never into a
/// public document. Nothing here carries a transponder.
/// </remarks>
internal static class DriverNightExtras
{
    /// <summary>The most laps sent for one race's chart. A 5-minute heat is 20 to 40; this is a backstop for an endurance run.</summary>
    internal const int MostLaps = 200;

    /// <summary>The most badges listed as "within reach".</summary>
    internal const int MostReach = 3;

    /// <summary>The most nights in the progress chart, tonight last.</summary>
    internal const int MostProgressNights = 6;

    /// <summary>
    /// Their races tonight, oldest first, one per session they have a row
    /// in. Demo sessions and practice are left out: a night is the races
    /// they ran (TonightAtTheTrack.Races, which is what the caller hands in,
    /// already says the same).
    /// </summary>
    /// <param name="transponder">The driver. Used only to find their row - it is never written into what comes back.</param>
    /// <param name="tonight">Tonight's races, as MainForm.RacesTonight gives them.</param>
    /// <param name="roundOf">A round by id, for the label of a race saved without one.</param>
    /// <param name="runs">
    /// Tonight's demo races and practice runs (MainForm.NightRunsTonight),
    /// or null for none. 3 Nov 2026 (asked for by Asher: "yes, marked as
    /// demo, and do the same for practice"): each one theirs goes in the
    /// list too, marked with <see cref="NightRace.Kind"/>, so the lap stats
    /// can be looked at after a rehearsal or a practice run. They never
    /// carry a driver rating, and a practice run no finishing place - the
    /// page leaves them out of the night at a glance and the Driver score,
    /// and has a tick box to hide them. Imported results never go.
    /// </param>
    internal static IReadOnlyList<NightRace> Races(
        string? transponder,
        IEnumerable<SessionRecord>? tonight,
        Func<string, RoundDefinition?>? roundOf,
        IEnumerable<SessionRecord>? runs = null)
    {
        var who = transponder?.Trim();

        if (string.IsNullOrEmpty(who) || (tonight is null && runs is null))
        {
            return Array.Empty<NightRace>();
        }

        var counted = (tonight ?? Enumerable.Empty<SessionRecord>())
            .Where(s => s is not null && !s.IsNotScored() && s.SessionType != RaceSessionType.Practice)
            .Select(s => (Session: s, Kind: (string?)null));
        var marked = (runs ?? Enumerable.Empty<SessionRecord>())
            .Where(s => s is not null && !s.IsImported())
            .Select(s => (Session: s, Kind: KindOf(s)))
            .Where(x => x.Kind is not null);

        var races = new List<NightRace>();
        var counts = counted.ToList();
        var raceIds = new HashSet<string>(counts.Select(x => x.Session.Id ?? string.Empty), StringComparer.Ordinal);

        foreach (var (session, kind) in counts.Concat(marked)
            .OrderBy(x => RelayResultsIndex.EffectiveStart(x.Session)))
        {
            // A session handed in both ways goes once, as the race it is.
            if (kind is not null && !string.IsNullOrEmpty(session.Id) && raceIds.Contains(session.Id))
            {
                continue;
            }

            var results = session.Results ?? new List<SessionResultEntry>();
            var row = results.FirstOrDefault(r => r is not null && SameDriver(r.Transponder, who));

            if (row is null)
            {
                continue;
            }

            // WHY (23 Oct 2026, bug hunt 10): index 0 of LapTimesMs is the
            // start-lap slot (grid to line, often 1-2 s, or 0 for a jump
            // start), not a lap. Sent as-is it became the lap chart's fastest
            // point, squashing every real lap into a flat line, and the
            // caption counted it as a lap. Only the flying laps go now - the
            // same rule as CoachReport.FlyingLaps, null-guarded here.
            //
            // 28 Oct 2026 (asked for by Asher, matching LiveRC): with where
            // they were running after each of those laps (LapByLap, over the
            // whole field) for the Pos column of their lap list, kept in step
            // with the laps as they are filtered - so a lap the filter drops
            // takes its position with it.
            var placesAt = LapByLap.Positions(results)[results.IndexOf(row)];
            var flying = (row.LapTimesMs ?? new List<long>()).Skip(1).ToList();
            var kept = flying
                .Select((ms, i) => (Ms: ms, Pos: i < placesAt.Length ? placesAt[i] : 0))
                .Where(lap => lap.Ms > 0)
                .Take(MostLaps)
                .ToList();
            var laps = kept.Select(lap => lap.Ms).ToList();
            var pen = PenaltyWords.For(row, session);

            // 28 Oct 2026 (asked for by Asher): their driver rating for this
            // race, for the night page's Driver score next to the report
            // card. The same DriverRating the report card's line, Driver Hub
            // and the leaderboard use, so the numbers never disagree. Left
            // out when the race has no rating (a field of one).
            //
            // Never for a demo or a practice run (3 Nov 2026): a rehearsal
            // is not a result, and practice has no finishing order to rate.
            double? rating = kind is null
                && DriverRating.ForSession(session).TryGetValue(row.Transponder ?? string.Empty, out var rated) ? rated : null;
            var practice = kind == PracticeKind;

            // WHY (2026-11-05, Bug hunt 11: the grid place): StartPosition is
            // the raw grid slot, which counts no-shows on a stated roster and
            // ghosts dragged to the front, while PosAt (LapByLap) counts only
            // the real cars on track. Sent raw, the driver behind a slot-1
            // no-show who led lap 1 read "started 2nd" and "Gained 1 place"
            // having passed nobody, while the Charge badge and NightMoments
            // put them on pole. Now the same rule NightMoments' Charge moment
            // follows: the place among real cars that turned a lap and lined
            // up ahead (AchievementEvaluator.GridPlace, no ghosts, no DQs),
            // and a slot past the field - left over from a bigger race - is
            // no grid position at all.
            var gridField = results.Count(r => r is not null && !string.IsNullOrWhiteSpace(r.Transponder));
            var realCars = results
                .Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Transponder) && !Ghosts.IsGhost(r.Transponder))
                .Where(r => !string.Equals((r.Outcome ?? string.Empty).Trim(), ResultEditor.Dq, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var grid = row.StartPosition > 0 && row.StartPosition <= gridField
                ? AchievementEvaluator.GridPlace(realCars, row)
                : 0;

            races.Add(new NightRace
            {
                Sid = session.Id,
                Label = LabelOf(session, roundOf, kind),
                At = RelayResultsIndex.EffectiveStart(session).ToString("O", CultureInfo.InvariantCulture),
                Kind = kind,

                // Practice is not a race: no place, so the page says none.
                Pos = practice ? 0 : row.Position,

                // Ghosts are recordings racing alongside, not drivers, so
                // "4th of 6" counts the people.
                Of = results.Count(r => r is not null && !Ghosts.IsGhost(r.Transponder)),
                Start = !practice && grid > 0 ? grid : null,
                Laps = row.Laps,
                BestMs = row.FastLapMs,
                LapsMs = laps.Count > 0 ? laps : null,
                PosAt = !practice && laps.Count > 0 && kept.All(lap => lap.Pos > 0) ? kept.Select(lap => lap.Pos).ToList() : null,
                StartMs = laps.Count > 0 && row.LapTimesMs is { Count: > 1 } ? row.LapTimesMs[0] : null,
                LengthS = session.TimeLimitSeconds is > 0 ? session.TimeLimitSeconds : null,
                Pen = pen.Count > 0 ? pen.ToList() : null,
                Out = practice ? null : OutOf(row),
                Rating = rating,
            });
        }

        return races;
    }

    /// <summary>
    /// Their latest race tonight - whose layout the progress chart is on,
    /// and whose class their improvement is measured in. Null when they
    /// have no race tonight.
    /// </summary>
    internal static SessionRecord? LatestRace(string? transponder, IEnumerable<SessionRecord>? tonight)
    {
        var who = transponder?.Trim();

        if (string.IsNullOrEmpty(who) || tonight is null)
        {
            return null;
        }

        return tonight
            .Where(s => s is not null && !s.IsNotScored() && s.SessionType != RaceSessionType.Practice)
            .Where(s => (s.Results ?? new List<SessionResultEntry>()).Any(r => r is not null && SameDriver(r.Transponder, who)))
            .OrderBy(RelayResultsIndex.EffectiveStart)
            .LastOrDefault();
    }

    /// <summary>
    /// The badges they are nearest, as the night page lists them - at most
    /// <see cref="MostReach"/>, through AchievementReach so the phone and the
    /// badge window can never disagree.
    /// </summary>
    internal static IReadOnlyList<NightReach> Reach(DriverAchievements? driver, RacingSetting? offeredAt)
    {
        return AchievementReach.For(driver, MostReach, offeredAt)
            .Select(near => new NightReach
            {
                Id = near.Id,
                Name = near.Name,
                Have = near.Have,
                Need = near.Need,
                Words = near.Words,
            })
            .ToList();
    }

    /// <summary>
    /// "Your middle lap is 0.40 s quicker than in your first race tonight
    /// (Q1 Heat 2)" - or slower, or the same pace. Null before they have
    /// two measured races tonight.
    ///
    /// Their own change only. It never ranks them against anybody else,
    /// which keeps the rule SeasonPiece keeps: a driver's night is about
    /// them. Built from TonightAtTheTrack.PaceTonight, the same measurement
    /// the track-evolution figure is made from, so the two cannot disagree.
    /// </summary>
    internal static string? Improvement(string? transponder, IEnumerable<DriverPaceTonight>? paces)
    {
        var who = transponder?.Trim();

        if (string.IsNullOrEmpty(who) || paces is null)
        {
            return null;
        }

        var pace = paces.FirstOrDefault(p => p is not null && SameDriver(p.Transponder, who));

        if (pace is null || pace.Races < 2 || pace.FirstMs <= 0 || pace.LatestMs <= 0)
        {
            return null;
        }

        var first = string.IsNullOrWhiteSpace(pace.FirstRace) ? string.Empty : $" ({pace.FirstRace.Trim()})";

        // Under five thousandths shows as 0.00, and "0.00 s quicker" is not
        // a thing anybody says.
        if (Math.Abs(pace.DeltaMs) < 5)
        {
            return $"Your middle lap is the same pace as in your first race tonight{first}";
        }

        var by = (Math.Abs(pace.DeltaMs) / 1000.0).ToString("0.00", CultureInfo.InvariantCulture);

        return pace.DeltaMs < 0
            ? $"Your middle lap is {by} s quicker than in your first race tonight{first}"
            : $"Your middle lap is {by} s slower than in your first race tonight{first}";
    }

    /// <summary>
    /// Their best lap on <paramref name="layout"/> on each of their last few
    /// nights there, oldest first and tonight last, with the line that says
    /// what tonight's means - see <see cref="ProgressOver"/>. Honours the
    /// Personal Bests page's resets.
    /// </summary>
    internal static NightProgress? Progress(
        string? transponder,
        IEnumerable<SessionRecord>? history,
        string? layout,
        PersonalBestResetStore? resets,
        DateTime nowLocal) =>
        ProgressOver(
            transponder,
            history,
            layout,
            resets is null ? null : (name, track) => resets.EffectiveCutoff(name, track),
            nowLocal);

    /// <summary>
    /// The progress chart, with the reset rule handed in - so it is tested
    /// without the store on disk.
    ///
    /// Demo sessions and ghosts are left out. Practice counts: a best lap
    /// on the track is a best lap, and it is what a driver who only
    /// practises some weeks would want on the chart.
    ///
    /// A reset (PersonalBestResetStore) is looked up the way the Personal
    /// Bests page looks it up - by the name saved with the result and the
    /// layout - and a lap set at or before it does not count.
    ///
    /// `since`:
    ///   "Your best on Club Oval since 3 September 2026" - an earlier night
    ///   was quicker, and tonight beat every night after it;
    ///   "Your best ever on Club Oval" - two nights or more and none quicker;
    ///   left out otherwise, including when the last night was quicker,
    ///   because "your best since last week" would say nothing.
    ///
    /// Null without a best lap tonight, and with only one night to show: a
    /// chart of one bar is not progress over weeks.
    /// </summary>
    /// <param name="cutoffOf">The reset for (name saved with the result, layout), or null for none.</param>
    internal static NightProgress? ProgressOver(
        string? transponder,
        IEnumerable<SessionRecord>? history,
        string? layout,
        Func<string, string, DateTime?>? cutoffOf,
        DateTime nowLocal)
    {
        var who = transponder?.Trim();
        var track = (layout ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(who) || Ghosts.IsGhost(who) || track.Length == 0 || history is null)
        {
            return null;
        }

        var tonight = TonightAtTheTrack.NightOf(nowLocal);
        var bests = new Dictionary<DateTime, long>();

        foreach (var session in history)
        {
            if (session is null
                || session.IsNotScored()
                || !string.Equals((session.TrackLayoutName ?? string.Empty).Trim(), track, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var started = RelayResultsIndex.EffectiveStart(session);
            var night = TonightAtTheTrack.NightOf(started.ToLocalTime());

            if (night > tonight)
            {
                continue;
            }

            foreach (var row in session.Results ?? new List<SessionResultEntry>())
            {
                if (row is null || row.FastLapMs <= 0 || !SameDriver(row.Transponder, who))
                {
                    continue;
                }

                if (cutoffOf?.Invoke((row.DriverName ?? string.Empty).Trim(), session.TrackLayoutName ?? string.Empty) is { } cutoff
                    && session.StartedAtUtc <= cutoff)
                {
                    continue;
                }

                if (!bests.TryGetValue(night, out var best) || row.FastLapMs < best)
                {
                    bests[night] = row.FastLapMs;
                }
            }
        }

        if (!bests.TryGetValue(tonight, out var tonightMs) || bests.Count < 2)
        {
            return null;
        }

        var earlier = bests.Where(b => b.Key < tonight).OrderBy(b => b.Key).ToList();
        string? since = null;

        // The most recent night that was quicker than tonight.
        var quicker = earlier.LastOrDefault(b => b.Value < tonightMs);

        if (quicker.Value > 0)
        {
            // Only worth saying when tonight beat at least one night since it.
            if (earlier.Any(b => b.Key > quicker.Key))
            {
                since = $"Your best on {track} since {quicker.Key.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}";
            }
        }
        else
        {
            since = $"Your best ever on {track}";
        }

        var shown = bests
            .OrderBy(b => b.Key)
            .TakeLast(MostProgressNights)
            .Select(b => new NightBest
            {
                Night = b.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                BestMs = b.Value,
            })
            .ToList();

        return new NightProgress
        {
            Layout = track,
            Nights = shown,
            Since = since,
        };
    }

    /// <summary>
    /// Each car's races as one list for `races[]`, in the order they ran -
    /// every race run by a car other than the first tagged with that car's
    /// id, and the first car's left untagged, so a page from before cars
    /// existed still draws every card (22 Oct 2026, second transponder).
    ///
    /// Both cars in the same race is two cards with the same race id, one
    /// per car: the driver had two rows in it.
    /// </summary>
    internal static IReadOnlyList<NightRace> Merge(IReadOnlyList<(string CarId, IReadOnlyList<NightRace> Races)> cars)
    {
        var all = new List<(DateTime At, int Car, int Index, NightRace Race)>();

        for (var c = 0; c < cars.Count; c++)
        {
            var races = cars[c].Races ?? Array.Empty<NightRace>();

            for (var i = 0; i < races.Count; i++)
            {
                var race = races[i];

                if (race is null)
                {
                    continue;
                }

                var at = DateTime.TryParse(race.At, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when)
                    ? when.ToUniversalTime()
                    : DateTime.MaxValue;

                all.Add((at, c, i, c == 0 ? race : race with { Car = cars[c].CarId }));
            }
        }

        return all
            .OrderBy(r => r.At)
            .ThenBy(r => r.Car)
            .ThenBy(r => r.Index)
            .Select(r => r.Race)
            .ToList();
    }

    /// <summary><see cref="NightRace.Kind"/> for a demo race.</summary>
    internal const string DemoKind = "demo";

    /// <summary><see cref="NightRace.Kind"/> for a practice run.</summary>
    internal const string PracticeKind = "practice";

    /// <summary>"demo", "practice", or null for a race that counts (and for an imported result, which never goes).</summary>
    private static string? KindOf(SessionRecord session)
    {
        if (session.IsImported())
        {
            return null;
        }

        if (session.IsDemoSession())
        {
            return DemoKind;
        }

        return session.SessionType == RaceSessionType.Practice ? PracticeKind : null;
    }

    /// <summary>
    /// The race's own label, or its round's, or "Race" ("Practice" for a
    /// practice run). A demo's "[DEMO]" prefix is taken off: the page marks
    /// a demo with a chip of its own.
    /// </summary>
    private static string LabelOf(SessionRecord session, Func<string, RoundDefinition?>? roundOf, string? kind = null)
    {
        var own = (session.Label ?? string.Empty).Trim();

        if (kind == DemoKind && own.StartsWith(SessionRecord.DemoLabelPrefix, StringComparison.OrdinalIgnoreCase))
        {
            own = own[SessionRecord.DemoLabelPrefix.Length..].Trim();
        }

        if (own.Length > 0)
        {
            return own;
        }

        return !string.IsNullOrEmpty(session.RoundId)
            && roundOf?.Invoke(session.RoundId) is { } round
            && !string.IsNullOrWhiteSpace(round.RoundLabel)
            ? round.RoundLabel.Trim()
            : kind == PracticeKind ? "Practice" : "Race";
    }

    /// <summary>"DQ" or "DNF" when the result was one, otherwise null (left out).</summary>
    private static string? OutOf(SessionResultEntry row)
    {
        var outcome = (row.Outcome ?? string.Empty).Trim();

        if (string.Equals(outcome, ResultEditor.Dq, StringComparison.OrdinalIgnoreCase))
        {
            return ResultEditor.Dq;
        }

        return string.Equals(outcome, ResultEditor.Dnf, StringComparison.OrdinalIgnoreCase) ? ResultEditor.Dnf : null;
    }

    private static bool SameDriver(string? transponder, string who) =>
        string.Equals(transponder?.Trim(), who, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Everything that goes into one driver's night document - see
/// <see cref="DriverNight.Document(NightParts)"/>. The first five are what
/// the original overload took; the rest are the driver experience extras
/// (22 Oct 2026), each left out of the document when empty or false.
/// </summary>
internal sealed record NightParts(
    string? DriverName,
    DateTime NightLocal,
    string? DayName,
    PackedFacts? Facts,
    DateTime AtUtc)
{
    /// <summary>Their report card (CoachReport.Lines), or null.</summary>
    public IReadOnlyList<string>? Coach { get; init; }

    /// <summary>Their races tonight - DriverNightExtras.Races.</summary>
    public IReadOnlyList<NightRace>? Races { get; init; }

    /// <summary>Their pace change tonight - DriverNightExtras.Improvement.</summary>
    public string? Improve { get; init; }

    /// <summary>Their best lap over recent nights - DriverNightExtras.Progress.</summary>
    public NightProgress? Progress { get; init; }

    /// <summary>The badges they are nearest - DriverNightExtras.Reach.</summary>
    public IReadOnlyList<NightReach>? Reach { get; init; }

    /// <summary>Tonight is their first night at the club - DriverFirsts.IsFirstNight.</summary>
    public bool FirstNight { get; init; }

    /// <summary>
    /// Every car they raced tonight, when one was a transponder other than
    /// their main one (22 Oct 2026, second transponder) - see NightCar.
    /// </summary>
    public IReadOnlyList<NightCar>? Cars { get; init; }

    /// <summary>True when any extra would go into the document. Without one, the document is exactly what the original overload writes.</summary>
    public bool HasExtras =>
        Races is { Count: > 0 }
        || !string.IsNullOrWhiteSpace(Improve)
        || Progress is not null
        || Reach is { Count: > 0 }
        || FirstNight
        || Cars is { Count: > 0 };
}

/// <summary>One race in `races[]`: {sid, label, at, pos, of, start, laps, bestMs, lapsMs, posAt, startMs, lenS, pen, out, car, rating}.</summary>
internal sealed record NightRace
{
    [JsonPropertyName("sid")]
    public string Sid { get; init; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    /// <summary>When it started, UTC, round-trip format.</summary>
    [JsonPropertyName("at")]
    public string At { get; init; } = string.Empty;

    /// <summary>
    /// "demo" or "practice" for a run that does not count (3 Nov 2026), or
    /// left out for a race. The page marks it, leaves it out of the night
    /// at a glance and the Driver score, and can hide it.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; init; }

    [JsonPropertyName("pos")]
    public int Pos { get; init; }

    /// <summary>How many drivers were in it - ghosts not counted.</summary>
    [JsonPropertyName("of")]
    public int Of { get; init; }

    /// <summary>Where they started, 1-based. Left out when the grid wasn't known.</summary>
    [JsonPropertyName("start")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Start { get; init; }

    [JsonPropertyName("laps")]
    public int Laps { get; init; }

    [JsonPropertyName("bestMs")]
    public long BestMs { get; init; }

    /// <summary>Each flying lap (the start lap left out), at most DriverNightExtras.MostLaps. The first thing dropped when a night is over the relay's size limit.</summary>
    [JsonPropertyName("lapsMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<long>? LapsMs { get; init; }

    /// <summary>Their position after each lap in LapsMs, in step with it (LapByLap, 28 Oct 2026). Left out with LapsMs, or when any lap's position is not known.</summary>
    [JsonPropertyName("posAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<int>? PosAt { get; init; }

    /// <summary>The start-lap slot (green flag to the line) - so the page knows when each lap was crossed, for pace.</summary>
    [JsonPropertyName("startMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? StartMs { get; init; }

    /// <summary>The race's time limit in seconds, for pace. Left out for a race without one.</summary>
    [JsonPropertyName("lenS")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LengthS { get; init; }

    /// <summary>PenaltyWords lines - the reasons are private to this document.</summary>
    [JsonPropertyName("pen")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Pen { get; init; }

    /// <summary>"DQ" or "DNF", or left out.</summary>
    [JsonPropertyName("out")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Out { get; init; }

    /// <summary>
    /// Which car ran it - a <see cref="NightCar.Id"/> - on a race run by any
    /// car but the one the page leads with. Left out otherwise, so a night
    /// of one car is what it always was (22 Oct 2026, second transponder).
    /// </summary>
    [JsonPropertyName("car")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Car { get; init; }

    /// <summary>
    /// Their driver rating for this race, 0 to 150 to one decimal place -
    /// DriverRating.ForSession (28 Oct 2026, the night page's Driver score).
    /// Left out for a race with no rating.
    /// </summary>
    [JsonPropertyName("rating")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Rating { get; init; }
}

/// <summary>
/// One car in `cars[]`: {id, label, name?, lines?, coach?, improve?, progress?}.
///
/// 22 Oct 2026 (second transponder on the same night). Only in a night
/// where the driver raced a transponder other than their main one as a car
/// of its own. The FIRST car's lines, report card, improvement and progress
/// stay where they always were, at the top of the document, so a page from
/// before this still shows them; its entry here carries only its id, label
/// and name. Every other car carries its own, never added up with the
/// first's - a best lap or an improvement means something for one car.
/// </summary>
internal sealed record NightCar
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>"Main car", "Second car" ... - NightOwner.LabelFor.</summary>
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    /// <summary>The car's name as the results were filed with it, or left out.</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    /// <summary>DriverCut's facts for this car's races, word for word - DriverNight.Lines.</summary>
    [JsonPropertyName("lines")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Lines { get; init; }

    /// <summary>This car's report card.</summary>
    [JsonPropertyName("coach")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Coach { get; init; }

    [JsonPropertyName("improve")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Improve { get; init; }

    [JsonPropertyName("progress")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NightProgress? Progress { get; init; }
}

/// <summary>`progress`: {layout, nights:[{night, bestMs}], since?}.</summary>
internal sealed record NightProgress
{
    [JsonPropertyName("layout")]
    public string Layout { get; init; } = string.Empty;

    [JsonPropertyName("nights")]
    public List<NightBest> Nights { get; init; } = new();

    [JsonPropertyName("since")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Since { get; init; }
}

/// <summary>One bar of the progress chart: the night (yyyy-MM-dd) and their best lap on it.</summary>
internal sealed record NightBest
{
    [JsonPropertyName("night")]
    public string Night { get; init; } = string.Empty;

    [JsonPropertyName("bestMs")]
    public long BestMs { get; init; }
}

/// <summary>One entry of `reach`: {id, name, have, need, words}.</summary>
internal sealed record NightReach
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("have")]
    public int Have { get; init; }

    [JsonPropertyName("need")]
    public int Need { get; init; }

    [JsonPropertyName("words")]
    public string Words { get; init; } = string.Empty;
}

/// <summary>
/// The night document with extras, in the original's field order and with
/// the new fields before `at`. Every extra is left out when empty, and
/// `coach` too, exactly as the original overload leaves it out.
/// </summary>
internal sealed class NightDocument
{
    [JsonPropertyName("driver")]
    public string Driver { get; init; } = string.Empty;

    [JsonPropertyName("when")]
    public string When { get; init; } = string.Empty;

    [JsonPropertyName("night")]
    public string Night { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("lines")]
    public string[] Lines { get; init; } = Array.Empty<string>();

    [JsonPropertyName("coach")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Coach { get; init; }

    [JsonPropertyName("races")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<NightRace>? Races { get; init; }

    /// <summary>Every car they raced tonight - only when one was a second transponder raced as its own car.</summary>
    [JsonPropertyName("cars")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<NightCar>? Cars { get; init; }

    [JsonPropertyName("improve")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Improve { get; init; }

    [JsonPropertyName("progress")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NightProgress? Progress { get; init; }

    [JsonPropertyName("reach")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<NightReach>? Reach { get; init; }

    [JsonPropertyName("firstNight")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? FirstNight { get; init; }

    [JsonPropertyName("at")]
    public string At { get; init; } = string.Empty;
}
