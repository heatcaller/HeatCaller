using System.Text.RegularExpressions;

namespace HeatCaller;

/// <summary>
/// The rest of the night, planned (11 Nov 2026, race-night build plan,
/// Phase 2): when each race still to run should start, when the night will
/// finish, and - when the race day has an "Out by" time - how far over it
/// runs and what would bring it back.
///
/// WHY: the research found that a director only learns the night is running
/// long once it already is. "Running 6 min behind" is real, but it arrives
/// after the fact, about one race. A director who sees "finishes about 22:40,
/// 40 minutes past the 22:00 curfew" before the first green can still drop a
/// round or shorten the heats.
///
/// THE MODEL, kept simple enough to say out loud:
///  - Every race still to run, in program order, takes its own length (the
///    format's time limit plus staging and countdown, or
///    <see cref="DefaultLength"/> for a lap race) plus the gap between races.
///  - The gap is tonight's own typical finish-to-green (NightPace) once
///    there are two gaps to go on, otherwise <see cref="DefaultGap"/>.
///  - A race on track now is taken as half done, the same assumption
///    RaceEta makes.
///  - On a race day, a class that qualifies but has no mains yet is given
///    the mains it will get (one per heat's worth of drivers), so the finish
///    covers the finals too.
///
/// Pure, so it can be tested without a window. Everything it gives is an
/// estimate, and every screen says "about".
/// </summary>
public static class NightPlan
{
    /// <summary>The gap between races before tonight has given two of its own: the low end of ROAR's 2-5 minutes, plus a little.</summary>
    public static readonly TimeSpan DefaultGap = TimeSpan.FromMinutes(3);

    /// <summary>What a race with no time limit (a lap race) is taken to last.</summary>
    public static readonly TimeSpan DefaultLength = TimeSpan.FromMinutes(5);

    /// <summary>A race at least this long can lose a minute without becoming pointless.</summary>
    public static readonly TimeSpan ShortestToShorten = TimeSpan.FromMinutes(3);

    /// <summary>Fewer races of tonight than this and the gap is not tonight's own yet.</summary>
    public const int RacesForMeasuredGap = 3;

    /// <summary>A class's mains that have not been built yet, for the finish to count.</summary>
    /// <param name="Format">The class's own format for its mains, before tonight's cut - so "finals shorter" can say what it really saves.</param>
    public sealed record UnbuiltMains(string ClassName, int Count, TimeSpan Length, RaceFormat? Format = null);

    /// <summary>One race still to run, and when it should start.</summary>
    /// <param name="Round">The round, or null for a main that has not been built yet.</param>
    public sealed record Slot(RoundDefinition? Round, string Label, string ClassName, RoundKind Kind, TimeSpan Length, DateTime StartUtc)
    {
        public DateTime EndUtc => StartUtc + Length;

        /// <summary>
        /// For a main not built yet: its class's own format, before tonight's cut. See <see cref="UnbuiltMains.Format"/>.
        /// For a built round, the format it runs to, when the plan was given one (23 Nov 2026, bug hunt 24 - see <c>formatOf</c> on <see cref="NightPlan.For"/>).
        /// </summary>
        public RaceFormat? Format { get; init; }
    }

    /// <summary>A change that would bring the finish forward, and by how much.</summary>
    /// <param name="DropsRound">For "drop a round", the qualifying round number it drops; otherwise null.</param>
    /// <param name="Kind">
    /// What sort of change it is (17 Nov 2026, Race Day run sheet), so Next
    /// Steps can offer the ones the app can make itself as tick boxes and
    /// leave the rest as advice. See RunSheet.ChoicesFor.
    /// </param>
    public sealed record Fix(string Text, TimeSpan Saves, int? DropsRound = null, FixKind Kind = FixKind.Advice);

    /// <summary>The sorts of change a <see cref="Fix"/> can be.</summary>
    public enum FixKind
    {
        /// <summary>Something only the director can do, such as putting two classes' heats together.</summary>
        Advice,

        /// <summary>Drop the last qualifying round still wholly to run.</summary>
        DropRound,

        /// <summary>A minute off every qualifier still to run that can spare one.</summary>
        ShorterQualifiers,

        /// <summary>One main fewer in each class with more than one still to come, while those mains are still to be built.</summary>
        OneMainFewer,
    }

    /// <summary>The rest of the night.</summary>
    /// <param name="Slots">Every race still to run, in order, with its start.</param>
    /// <param name="FinishUtc">When the last of them should finish.</param>
    /// <param name="Gap">The gap between races used.</param>
    /// <param name="GapIsTonights">True when <paramref name="Gap"/> is tonight's own, false for the default.</param>
    /// <param name="OutByUtc">The race day's "Out by" time as a moment, or null with none set.</param>
    /// <param name="Over">How far past it the finish is; null when it isn't, or there is none.</param>
    /// <param name="Fixes">When over: what would help, biggest saving first.</param>
    public sealed record Projection(
        IReadOnlyList<Slot> Slots,
        DateTime FinishUtc,
        TimeSpan Gap,
        bool GapIsTonights,
        DateTime? OutByUtc,
        TimeSpan? Over,
        IReadOnlyList<Fix> Fixes)
    {
        /// <summary>The planned breaks still ahead tonight, in all (12 Nov 2026, setup plan D5) - already counted in the starts and the finish.</summary>
        public TimeSpan BreakTime { get; init; }

        /// <summary>When the round should start, or null when it is not still to run.</summary>
        public DateTime? StartOf(string roundId) =>
            Slots.FirstOrDefault(s => s.Round is not null && string.Equals(s.Round.Id, roundId, StringComparison.Ordinal))?.StartUtc;

        /// <summary>
        /// The start to show drivers (12 Nov 2026, setup plan step 8): the
        /// estimate, or the time they were given when the night is running
        /// early - see StartPromise.Shown. Null when it is not still to run.
        /// </summary>
        public DateTime? ShownStartOf(string roundId) =>
            Slots.FirstOrDefault(s => s.Round is not null && string.Equals(s.Round.Id, roundId, StringComparison.Ordinal)) is { } slot
                ? StartPromise.Shown(slot.StartUtc, slot.Round!.PromisedStartUtc)
                : null;
    }

    /// <summary>
    /// How long a round takes on track: its format's time limit plus staging
    /// and countdown, or <see cref="DefaultLength"/> for a race with no time
    /// limit. The format is chosen as Race Control chooses it (the round's
    /// override, the class's qualifier format for a qualifier, the class's
    /// format, the app default).
    /// </summary>
    public static TimeSpan LengthOf(RoundDefinition round, RaceClass? raceClass, RaceFormat appDefault) =>
        LengthOf(FormatOf(round, raceClass, appDefault));

    /// <summary>
    /// The format a round runs to, chosen as Race Control chooses it (the
    /// round's override, the class's qualifier format for a qualifier, the
    /// class's format, the app default).
    /// </summary>
    /// <remarks>
    /// WHY (23 Nov 2026, bug hunt 24): pulled out of LengthOf so the plan can
    /// carry each round's format (see <c>formatOf</c> on <see cref="For"/>)
    /// and "qualifiers a minute shorter" can price what the change really
    /// takes off, from the same format the change is made to.
    /// </remarks>
    public static RaceFormat FormatOf(RoundDefinition round, RaceClass? raceClass, RaceFormat appDefault) =>
        round.FormatOverride
        ?? (round.Kind == RoundKind.Qualifier ? raceClass?.QualifierFormat : null)
        ?? raceClass?.Format
        ?? appDefault;

    /// <summary>
    /// The finals a class will still race when it qualifies but has none
    /// built yet: its Pole Shootout's one-car runs first, when it has one,
    /// then its mains - an Invitational's series of whole-field mains, or a
    /// ladder of heat-sized mains with the A-main (and, when chosen, the
    /// lower mains) run in legs.
    /// </summary>
    /// <remarks>
    /// WHY (20 Nov 2026, bug hunt 22): the plan counted every class as
    /// drivers / heat size mains, one race each. An Invitational of ten with
    /// a shootout - five runs and five mains - was planned as one race, so
    /// "Racing done" and the late strip came out about nine races early and
    /// "Get back on time" had nothing to offer.
    /// </remarks>
    /// <param name="length">One main's length, the night's finals cut already taken off.</param>
    public static List<UnbuiltMains> UnbuiltFor(string className, RaceClass? raceClass, int drivers, int heatSize,
        bool oneMainFewer, RaceFormat format, TimeSpan length)
    {
        var unbuilt = new List<UnbuiltMains>();

        if (raceClass is { PoleShootout: true }
            && Math.Min(PoleShootout.DriversFor(raceClass), drivers) is var runs and >= PoleShootout.MinDrivers)
        {
            var runFormat = PoleShootout.RunFormat(format, PoleShootout.TimedLapsFor(raceClass));
            unbuilt.Add(new UnbuiltMains(className, runs, LengthOf(runFormat), runFormat));
        }

        int mains;

        if (raceClass is { Invitational: true })
        {
            mains = Invitational.MainsFor(raceClass);
        }
        else
        {
            var ladder = (int)Math.Ceiling(drivers / (double)Math.Max(2, heatSize));

            // Tonight's finals cut (12 Nov 2026, setup plan step 8), chosen
            // in "We're Done After This Round...".
            if (oneMainFewer && ladder > 1)
            {
                ladder--;
            }

            var legs = Math.Clamp(raceClass?.AMainLegs ?? 1, 1, AMainLegs.MaxLegs);
            mains = raceClass is { LowerMainsInLegs: true } ? ladder * legs : ladder + (legs - 1);
        }

        unbuilt.Add(new UnbuiltMains(className, mains, length, format));
        return unbuilt;
    }

    /// <summary>A format's length on track. See <see cref="LengthOf(RoundDefinition, RaceClass?, RaceFormat)"/>.</summary>
    public static TimeSpan LengthOf(RaceFormat format) =>
        format.TimeLimitMinutes > 0
            ? TimeSpan.FromMinutes(format.TimeLimitMinutes) + TimeSpan.FromSeconds(Math.Max(0, format.StagingSeconds) + Math.Max(0, format.CountdownSeconds))
            : DefaultLength;

    /// <summary>
    /// The race day's rounds in the order Race Control will run them: the
    /// race it has as next (<paramref name="nextStart"/>) moved in front of
    /// the rounds still to run, the rest in program order after it. The
    /// list as it was when that race is already first, isn't in it, or has
    /// run.
    ///
    /// WHY (12 Nov 2026, the first night on step 8): Race Control's next
    /// race is the first unrun round that isn't the one in hand (NextRound),
    /// and the desk picks rounds out of order - a heat held for a driver, a
    /// practice before the first heat. For then matched the next start
    /// against the first unrun round in program order only, so on those
    /// nights the push-back and Auto start's count moved nothing, and the
    /// schedule said one race was next while the PA called another. Kept
    /// out of For, which plans the order it is given.
    /// </summary>
    public static IReadOnlyList<RoundDefinition> InRunningOrder(IReadOnlyList<RoundDefinition> dayRounds, RaceControlControl.NextStartFact? nextStart)
    {
        if (nextStart?.RoundId is not { Length: > 0 } nextId)
        {
            return dayRounds;
        }

        static bool ToRun(RoundDefinition? r) => r is not null && !r.IsRecorded && r.Kind != RoundKind.Practice;

        var at = -1;
        var firstToRun = -1;

        for (var i = 0; i < dayRounds.Count; i++)
        {
            if (!ToRun(dayRounds[i]))
            {
                continue;
            }

            if (firstToRun < 0)
            {
                firstToRun = i;
            }

            if (string.Equals(dayRounds[i].Id, nextId, StringComparison.Ordinal))
            {
                at = i;
                break;
            }
        }

        if (at <= firstToRun)
        {
            return dayRounds;
        }

        var ordered = dayRounds.ToList();
        var next = ordered[at];
        ordered.RemoveAt(at);
        ordered.Insert(firstToRun, next);
        return ordered;
    }

    /// <summary>
    /// The rest of the night, or null when nothing is left to run.
    /// </summary>
    /// <param name="dayRounds">The race day's rounds in program order (or running order - see InRunningOrder).</param>
    /// <param name="lengthOf">How long each round takes (see LengthOf).</param>
    /// <param name="unbuilt">Mains still to be built, run after everything in <paramref name="dayRounds"/>.</param>
    /// <param name="onTrackRoundId">The round on track now, if any.</param>
    /// <param name="lastFinishUtc">When tonight's last race finished, if any.</param>
    /// <param name="tonightsGap">Tonight's typical finish-to-green, or null when there isn't one yet.</param>
    /// <param name="outByLocal">The race day's "Out by" time of day, or null.</param>
    /// <param name="nowUtc">Now.</param>
    /// <param name="firstRaceLocal">
    /// The race day's "First race at" time of day (Setup tab), or null. Until
    /// a race of tonight has run, nothing is planned to start before it - a
    /// program built at 16:00 for an 18:00 start says 18:00, not 16:00. Once
    /// a race has run (or one is on track) the night's own timing takes over.
    /// </param>
    /// <param name="breaks">
    /// The race day's planned breaks (setup plan D5), or null. A break falls
    /// between the last race of the round it follows and the next race, and
    /// pushes everything after it back by its length.
    /// </param>
    /// <param name="formatOf">
    /// The format each round runs to (see <see cref="FormatOf"/>), or null.
    /// WHY (23 Nov 2026, bug hunt 24): with it, "qualifiers a minute shorter"
    /// counts only the heats the change can really shorten.
    /// </param>
    public static Projection? For(
        IReadOnlyList<RoundDefinition> dayRounds,
        Func<RoundDefinition, TimeSpan> lengthOf,
        IEnumerable<UnbuiltMains>? unbuilt,
        string? onTrackRoundId,
        DateTime? lastFinishUtc,
        TimeSpan? tonightsGap,
        TimeSpan? outByLocal,
        DateTime nowUtc,
        TimeSpan? firstRaceLocal = null,
        IReadOnlyList<PlannedBreak>? breaks = null,
        bool keepPromises = false,
        RaceControlControl.NextStartFact? nextStart = null,
        int? heatSize = null,
        Func<RoundDefinition, RaceFormat?>? formatOf = null)
    {
        var gap = tonightsGap is { } measured && measured > TimeSpan.Zero ? measured : DefaultGap;
        var slots = new List<Slot>();
        var breakTime = TimeSpan.Zero;

        // The race before the next one placed, for the breaks between them.
        RoundDefinition? previous;

        // The furthest round the night has got to, run or placed: a break
        // behind it was crossed already. A Q1 heat held and run after Q2
        // Heat 1 must not bring the after-Q1 break round again before Q2
        // Heat 2 (14 Nov 2026, bug hunt 18).
        int? reached = null;

        void Reach(RoundDefinition? round)
        {
            if (NightBreaks.PositionOf(round) is { } position && (reached is not { } furthest || position > furthest))
            {
                reached = position;
            }
        }

        foreach (var run in dayRounds)
        {
            if (run is not null && run.IsRecorded && run.Kind != RoundKind.Practice)
            {
                Reach(run);
            }
        }

        TimeSpan BreakBefore(RoundDefinition next)
        {
            var length = NightBreaks.Length(NightBreaks.Between(previous, next, breaks, reached));
            breakTime += length;
            return length;
        }

        var toRun = dayRounds.Where(r => r is not null && !r.IsRecorded && r.Kind != RoundKind.Practice).ToList();
        var onTrack = onTrackRoundId is null
            ? null
            : toRun.FirstOrDefault(r => string.Equals(r.Id, onTrackRoundId, StringComparison.Ordinal));

        DateTime cursor;

        if (onTrack is not null)
        {
            // Half done, on average - see the model above.
            var length = lengthOf(onTrack);
            var start = nowUtc - TimeSpan.FromTicks(length.Ticks / 2);
            slots.Add(new Slot(onTrack, onTrack.RoundLabel, onTrack.ClassName, onTrack.Kind, length, start) { Format = formatOf?.Invoke(onTrack) });
            cursor = start + length + gap;
            previous = onTrack;
            Reach(onTrack);
        }
        else
        {
            // The last race run, in program order: a break planned after it
            // is still to come, from its finish.
            previous = dayRounds.LastOrDefault(r => r is not null && r.IsRecorded && r.Kind != RoundKind.Practice);
            var firstToRun = toRun.FirstOrDefault();
            var pause = firstToRun is null ? TimeSpan.Zero : NightBreaks.Length(NightBreaks.Between(previous, firstToRun, breaks, reached));

            cursor = lastFinishUtc is { } finished && finished + gap + pause > nowUtc ? finished + gap + pause : nowUtc;

            // Only the part of this break still to come counts as break time
            // ahead: one already taken, or the minutes of it already gone,
            // are in no start and not in the finish. It runs from the last
            // finish plus the gap, so what is left is from now to its end.
            var breakAhead = TimeSpan.Zero;

            if (pause > TimeSpan.Zero && lastFinishUtc is { } ended)
            {
                var breakStarts = ended + gap;
                var left = breakStarts + pause - (nowUtc > breakStarts ? nowUtc : breakStarts);
                breakAhead = left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }

            if (firstToRun is not null)
            {
                previous = firstToRun;
                Reach(firstToRun);
            }

            // Nothing of tonight has raced yet: start at the time the club
            // set, when that is still ahead. NearestOccurrence picks the day
            // (so a 09:00 start set at 23:00 is tomorrow's), and a start
            // already passed is now - the night is late, not in the past.
            var nothingRunYet = !dayRounds.Any(r => r is not null && r.IsRecorded && r.Kind != RoundKind.Practice);

            if (nothingRunYet && firstRaceLocal is { } first && NearestOccurrence(first, nowUtc) is var firstUtc && firstUtc > cursor)
            {
                cursor = firstUtc;
            }

            // When Race Control will really start the next race (12 Nov
            // 2026): Auto start's count, which carries a driver's approved
            // "Need 5 minutes", the desk's +1 min and a Hold; or, with Auto
            // start off, how far the desk put it back. Everything after it
            // moves with it.
            if (nextStart is { } known && firstToRun is not null && string.Equals(known.RoundId, firstToRun.Id, StringComparison.Ordinal))
            {
                if (known.StartsAtUtc is { } startsAt)
                {
                    cursor = startsAt > nowUtc ? startsAt : nowUtc;

                    // The start is known, so no more of the break is left
                    // than the wait for it.
                    if (breakAhead > cursor - nowUtc)
                    {
                        breakAhead = cursor - nowUtc;
                    }
                }
                else if (known.PushedBackMinutes > 0)
                {
                    cursor += TimeSpan.FromMinutes(known.PushedBackMinutes);
                }
            }

            breakTime += breakAhead;
        }

        foreach (var round in toRun)
        {
            if (ReferenceEquals(round, onTrack))
            {
                continue;
            }

            // The first race to run had its break counted above.
            if (!ReferenceEquals(round, previous))
            {
                cursor += BreakBefore(round);
            }

            // Never more than 2 minutes early (12 Nov 2026, setup plan step
            // 8): Auto start waits for a race's promised time, so the races
            // after it wait too, and the finish says so.
            if (keepPromises && StartPromise.NotBefore(round, neverEarly: true) is { } notBefore && cursor < notBefore)
            {
                cursor = notBefore;
            }

            var length = lengthOf(round);
            slots.Add(new Slot(round, round.RoundLabel, round.ClassName, round.Kind, length, cursor) { Format = formatOf?.Invoke(round) });
            cursor += length + gap;
            previous = round;
            Reach(round);
        }

        // A stand-in for the mains still to be built, so a break before the
        // finals falls in front of them too.
        var unbuiltMain = new RoundDefinition { Kind = RoundKind.Main };

        foreach (var mains in unbuilt ?? Enumerable.Empty<UnbuiltMains>())
        {
            if (mains.Count > 0)
            {
                cursor += BreakBefore(unbuiltMain);
                previous = unbuiltMain;
                Reach(unbuiltMain);
            }

            // Lowest first, the order the mains run in.
            for (var tier = Math.Max(0, mains.Count) - 1; tier >= 0; tier--)
            {
                var letter = tier < 26 ? ((char)('A' + tier)).ToString() : (tier + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                slots.Add(new Slot(null, $"{letter}-Main", mains.ClassName, RoundKind.Main, mains.Length, cursor) { Format = mains.Format });
                cursor += mains.Length + gap;
            }
        }

        if (slots.Count == 0)
        {
            return null;
        }

        var finish = slots[^1].EndUtc;
        var outBy = outByLocal is { } time ? NearestOccurrence(time, finish) : (DateTime?)null;
        TimeSpan? over = outBy is { } limit && finish > limit ? finish - limit : null;

        return new Projection(
            slots,
            finish,
            gap,
            tonightsGap is { } g && g > TimeSpan.Zero,
            outBy,
            over,
            over is null ? Array.Empty<Fix>() : FixesFor(slots, gap, onTrack, dayRounds, heatSize))
        {
            BreakTime = breakTime,
        };
    }

    /// <summary>
    /// The "Out by" time of day on whichever day puts it nearest the finish,
    /// so 00:30 is after a 23:50 finish, not 23 hours before it - a club
    /// night running past midnight is most of them (see ScheduleDrift).
    /// </summary>
    internal static DateTime NearestOccurrence(TimeSpan timeOfDay, DateTime nearUtc)
    {
        var nearLocal = DateTime.SpecifyKind(nearUtc, DateTimeKind.Utc).ToLocalTime();

        var best = new[] { -1, 0, 1 }
            .Select(days => nearLocal.Date.AddDays(days).Add(timeOfDay))
            .MinBy(candidate => Math.Abs((nearLocal - candidate).Ticks));

        return DateTime.SpecifyKind(best, DateTimeKind.Local).ToUniversalTime();
    }

    /// <summary>
    /// What a minute off one qualifier really saves (23 Nov 2026, bug hunt
    /// 24): from its format, what <see cref="Shortened"/> takes off - a
    /// minute, or nothing for a lap race or a heat already at 3 minutes.
    /// Without its format, the old slot test stands in.
    /// </summary>
    public static TimeSpan QualifierMinuteSaves(Slot qualifier)
    {
        if (qualifier.Format is not { } format)
        {
            return qualifier.Length >= ShortestToShorten ? TimeSpan.FromMinutes(1) : TimeSpan.Zero;
        }

        var saves = LengthOf(format) - LengthOf(Shortened(format, 1) ?? format);
        return saves > TimeSpan.Zero ? saves : TimeSpan.Zero;
    }

    /// <summary>What would bring the finish forward, biggest saving first.</summary>
    private static List<Fix> FixesFor(List<Slot> slots, TimeSpan gap, RoundDefinition? onTrack, IReadOnlyList<RoundDefinition> dayRounds, int? heatSize)
    {
        var fixes = new List<Fix>();
        var qualifiers = slots.Where(s => s.Round is not null && s.Kind == RoundKind.Qualifier && !ReferenceEquals(s.Round, onTrack)).ToList();

        // 1. Drop the last qualifying round still wholly to run - never the
        //    first, and never one that has started.
        // Read against the whole program: a round with one heat run is not
        // whole, and the slots only hold what is still to run.
        var lastRound = LastWholeRoundToRun(dayRounds.Where(r => r is not null).ToList(), onTrack?.Id);

        if (lastRound is { } dropping)
        {
            var heats = qualifiers.Where(s => RoundNumber(s.Round!.RoundLabel) == dropping.Number).ToList();
            var saves = TimeSpan.FromTicks(heats.Sum(s => (s.Length + gap).Ticks));
            fixes.Add(new Fix(
                $"drop {dropping.Name} ({Plural.Of(heats.Count, "race")}) to save {Minutes(saves)}",
                saves,
                dropping.Number,
                FixKind.DropRound));
        }

        // 2. A minute off every qualifier still to run that can spare one.
        // WHY (23 Nov 2026, bug hunt 24): this counted a minute for every
        // qualifier whose SLOT was 3 minutes or more - staging and countdown
        // included, and a lap race counted as 5 - though the change itself
        // (Shortened, as Get back on time applies it) does nothing to a lap
        // race and never takes a time limit below 3 minutes. So a night of
        // 3-minute or lap-count heats was offered "save 12 min" that ticking
        // changed nothing, and the offer never went away after the heats
        // were down to 3. Now each heat counts what Shortened really takes off
        // its own format; the old slot test is kept only for a heat whose
        // format the plan was not given.
        var qualifierSaves = TimeSpan.FromTicks(qualifiers.Sum(s => QualifierMinuteSaves(s).Ticks));

        if (qualifierSaves > TimeSpan.Zero)
        {
            fixes.Add(new Fix($"run qualifiers a minute shorter to save {Minutes(qualifierSaves)}", qualifierSaves, Kind: FixKind.ShorterQualifiers));
        }

        // 3. One main fewer in each class with more than one still to run.
        var mainSaves = slots
            .Where(s => s.Kind == RoundKind.Main)
            .GroupBy(s => s.ClassName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.First())
            .ToList();

        if (mainSaves.Count > 0)
        {
            var saves = TimeSpan.FromTicks(mainSaves.Sum(s => (s.Length + gap).Ticks));
            fixes.Add(new Fix(
                mainSaves.Count == 1
                    ? $"run one main fewer in {mainSaves[0].ClassName} to save {Minutes(saves)}"
                    : $"run one main fewer in each class to save {Minutes(saves)}",
                saves,
                // The app can only take a main off the night's plan while it
                // is still to be built (RaceEvent.FinalsOneMainFewer, applied
                // as the finals are seeded). Built mains are the director's
                // to delete, so then it stays advice.
                Kind: slots.Where(s => s.Kind == RoundKind.Main).All(s => s.Round is null) ? FixKind.OneMainFewer : FixKind.Advice));
        }

        // 4. Two small classes sharing their heats (11 Nov 2026, Phase 4.1,
        //    mixed-class heats): one race fewer each round they both still
        //    have to run, and each class is still scored on its own. Only a
        //    suggestion - the director puts the heats together with Run
        //    Together, so nothing here changes the program. The cap is the
        //    biggest heat tonight or tonight's heat size, whichever is more:
        //    a field that big already runs here. Tonight's heat size, not
        //    the app's 8: a 6-per-heat club was offered an 8-car heat
        //    (15 Nov 2026, bug hunt 20).
        var cap = Math.Max(heatSize is > 0 ? heatSize.Value : RaceEventStore.DefaultDriversPerHeat,
            dayRounds.Where(r => r is { Kind: RoundKind.Qualifier }).Select(r => r.AssignedTransponders.Count).DefaultIfEmpty(0).Max());
        var shareable = MixedHeats.ShareableClasses(qualifiers.Select(s => s.Round!).ToList(), cap);

        if (shareable.Count > 0)
        {
            var (a, b, heatsSaved) = shareable[0];
            var bLength = qualifiers.Where(s => string.Equals(s.Round!.ClassName, b, StringComparison.OrdinalIgnoreCase)).Select(s => s.Length).DefaultIfEmpty(TimeSpan.Zero).Max();
            var saves = TimeSpan.FromTicks((bLength + gap).Ticks * heatsSaved);

            if (saves > TimeSpan.Zero)
            {
                fixes.Add(new Fix($"run {a} and {b} together (Run Together on the program) to save {Minutes(saves)}", saves));
            }
        }

        return fixes.OrderByDescending(f => f.Saves).ToList();
    }

    /// <summary>A qualifying round's number and what it is called: Q3, or Race 3 on a club night.</summary>
    public readonly record struct RoundName(int Number, string Name);

    private static readonly Regex QualifyingNumber = new(@"^\s*(?:Q(?<n>\d+)\b|Race\s+(?<n>\d+)\b|(?<single>Heat\s+\d+)\s*$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Which qualifying round a heat belongs to, from the label the program
    /// gave it: "Q2 Heat 3" is 2, "Race 4 Heat 1" is 4 on a club night, and
    /// a single round's "Heat 3" is 1. Null for a label renamed by hand.
    /// </summary>
    public static int? RoundNumber(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var match = QualifyingNumber.Match(label);

        if (!match.Success)
        {
            return null;
        }

        if (match.Groups["single"].Success)
        {
            return 1;
        }

        return int.TryParse(match.Groups["n"].Value, out var n) && n > 0 ? n : null;
    }

    private static string RoundNameOf(int number, IEnumerable<RoundDefinition> heats) =>
        heats.Any(h => h.RoundLabel.TrimStart().StartsWith("Race", StringComparison.OrdinalIgnoreCase))
            ? $"Race {number}"
            : $"Q{number}";

    /// <summary>
    /// The last qualifying round none of whose heats has run or is on track,
    /// as long as it isn't the only round: dropping it is a real choice.
    /// </summary>
    private static RoundName? LastWholeRoundToRun(List<RoundDefinition> rounds, string? onTrackId)
    {
        var numbered = rounds
            .Where(r => r.Kind == RoundKind.Qualifier && RoundNumber(r.RoundLabel) is not null)
            .GroupBy(r => RoundNumber(r.RoundLabel)!.Value)
            .Where(g => g.Key >= 2 && g.All(r => !r.IsRecorded && !string.Equals(r.Id, onTrackId, StringComparison.Ordinal)))
            .OrderByDescending(g => g.Key)
            .FirstOrDefault();

        return numbered is null ? null : new RoundName(numbered.Key, RoundNameOf(numbered.Key, numbered));
    }

    /// <summary>What "we're done after this round" would do.</summary>
    /// <param name="Current">The round being finished, e.g. Q2 - the one on track, or the next to run.</param>
    /// <param name="Dropped">The heats of every later qualifying round, none of them run.</param>
    public sealed record StopEarly(RoundName Current, IReadOnlyList<RoundDefinition> Dropped);

    /// <summary>
    /// "We're done after this round": finish the qualifying round now under
    /// way (the one on track, or else the next to run) and drop every later
    /// one. Null when there is no qualifying left, or nothing after this
    /// round to drop.
    /// </summary>
    public static StopEarly? AfterThisRound(IReadOnlyList<RoundDefinition> dayRounds, string? onTrackRoundId)
    {
        var qualifiers = dayRounds
            .Where(r => r is not null && r.Kind == RoundKind.Qualifier && RoundNumber(r.RoundLabel) is not null)
            .ToList();

        var current = qualifiers.FirstOrDefault(r => string.Equals(r.Id, onTrackRoundId, StringComparison.Ordinal))
                      ?? qualifiers.FirstOrDefault(r => !r.IsRecorded);

        if (current is null || RoundNumber(current.RoundLabel) is not { } number)
        {
            return null;
        }

        var dropped = qualifiers
            .Where(r => RoundNumber(r.RoundLabel) > number
                        && !r.IsRecorded
                        && !string.Equals(r.Id, onTrackRoundId, StringComparison.Ordinal))
            .ToList();

        return dropped.Count == 0
            ? null
            : new StopEarly(new RoundName(number, RoundNameOf(number, qualifiers.Where(q => RoundNumber(q.RoundLabel) == number))), dropped);
    }

    /// <summary>How much shorter "run the finals shorter" makes each main.</summary>
    public const int FinalsShorterBy = 2;

    /// <summary>
    /// What "We're done after this round" can also offer for the finals
    /// (12 Nov 2026, setup plan step 8), with the minutes each saves.
    /// </summary>
    /// <param name="MinutesShorter">How much shorter each main would be - <see cref="FinalsShorterBy"/>.</param>
    /// <param name="ShorterSaves">What running every main that much shorter saves; zero when no main is long enough to lose it.</param>
    /// <param name="FewerSaves">What one main fewer in each class with more than one saves; zero when none has.</param>
    /// <param name="FewerClasses">The classes that would lose a main.</param>
    public sealed record FinalsCut(int MinutesShorter, TimeSpan ShorterSaves, TimeSpan FewerSaves, IReadOnlyList<string> FewerClasses)
    {
        public bool CanShorten => ShorterSaves > TimeSpan.Zero;

        public bool CanDropOne => FewerSaves > TimeSpan.Zero;
    }

    /// <summary>
    /// The finals cuts "We're done after this round" can offer (setup plan
    /// step 8): only when the night has an out-by time, and only the ones that
    /// save something. Read off the same projection the Next Steps card uses,
    /// so the minutes agree with "To finish in time: ...". Null with no out-by
    /// time or no finals still to run.
    ///
    /// WHY: dropping a qualifying round is the usual answer to a curfew, but
    /// it isn't always enough - and the finals are where the time is. The
    /// director deciding at 21:15 to finish early wants the other two levers
    /// in the same breath, with what each buys, not a hunt through the
    /// class settings afterwards.
    ///
    /// 14 Nov 2026 (bug hunt 19): each main counts what <see cref="Shortened"/>
    /// really takes off its time limit - a 4-minute main loses 1 minute, a
    /// 3-minute main or a lap race nothing - not the full 2 minutes off a
    /// slot that includes staging and the countdown. And a cut already chosen
    /// tonight (<paramref name="minutesShorterTonight"/>,
    /// <paramref name="oneMainFewerTonight"/>) isn't offered again: choosing
    /// it a second time changes nothing.
    /// </summary>
    public static FinalsCut? FinalsCuts(Projection? plan, int minutesShorterTonight = 0, bool oneMainFewerTonight = false)
    {
        if (plan is not { OutByUtc: not null })
        {
            return null;
        }

        // Only mains not built yet: the cut lands when a class's finals are
        // seeded, so a class whose mains are already on the program keeps
        // them as they are and mustn't count towards what the cut saves.
        var mains = plan.Slots.Where(s => s.Kind == RoundKind.Main && s.Round is null).ToList();

        if (mains.Count == 0)
        {
            return null;
        }

        var shorter = TimeSpan.FromMinutes(FinalsShorterBy);

        // What the cut takes off one main. Without its format, the slot's
        // length stands in for the time limit.
        TimeSpan ShorterSaves(Slot s)
        {
            if (minutesShorterTonight >= FinalsShorterBy)
            {
                return TimeSpan.Zero;
            }

            if (s.Format is not { } format)
            {
                return TimeSpan.FromTicks(Math.Clamp((s.Length - ShortestToShorten).Ticks, 0, shorter.Ticks));
            }

            var now = Shortened(format, minutesShorterTonight) ?? format;
            var cut = Shortened(format, FinalsShorterBy) ?? format;
            return LengthOf(now) - LengthOf(cut);
        }

        var shorterSaves = TimeSpan.FromTicks(mains.Sum(s => Math.Max(0, ShorterSaves(s).Ticks)));

        var fewer = oneMainFewerTonight
            ? new List<(string Class, TimeSpan Saves)>()
            : mains
                .GroupBy(s => s.ClassName, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => (Class: g.Key, Saves: g.First().Length + plan.Gap))
                .ToList();

        return new FinalsCut(
            FinalsShorterBy,
            shorterSaves,
            TimeSpan.FromTicks(fewer.Sum(f => f.Saves.Ticks)),
            fewer.Select(f => f.Class).ToList());
    }

    /// <summary>
    /// A main's format with tonight's cut taken off (setup plan step 8): the
    /// time limit <paramref name="minutesShorter"/> minutes shorter, never
    /// below <see cref="ShortestToShorten"/>. Null when nothing changes - a
    /// lap race, no cut, or a main already as short as it goes - so the main
    /// keeps the class's format as usual.
    /// </summary>
    public static RaceFormat? Shortened(RaceFormat format, int minutesShorter)
    {
        if (minutesShorter <= 0 || format.TimeLimitMinutes <= 0)
        {
            return null;
        }

        var limit = Math.Max((int)ShortestToShorten.TotalMinutes, format.TimeLimitMinutes - minutesShorter);

        if (limit >= format.TimeLimitMinutes)
        {
            return null;
        }

        var shortened = format.Clone();
        shortened.TimeLimitMinutes = limit;
        return shortened;
    }

    /// <summary>
    /// The seeding with one main fewer (setup plan step 8): the lowest main of
    /// a class with more than one is left out - its drivers' night ends with
    /// qualifying. Unchanged with one main.
    /// </summary>
    public static MainsBuilder.SeedPlan OneMainFewer(MainsBuilder.SeedPlan plan) =>
        plan.TierCount <= 1
            ? plan
            : new MainsBuilder.SeedPlan(plan.TierCount - 1, plan.MainSize, plan.Tiers.Take(plan.TierCount - 1).ToList());

    /// <summary>
    /// What the announcer says once the later rounds are dropped: "Change of
    /// plan, everyone: qualifying finishes after Q2, and the finals are
    /// next." On a club night: "...tonight finishes after Race 2."
    /// </summary>
    public static string Announcement(StopEarly stop, bool clubNight) =>
        clubNight
            ? $"Change of plan, everyone: tonight finishes after {stop.Current.Name}."
            : $"Change of plan, everyone: qualifying finishes after {stop.Current.Name}, and the finals are next.";

    /// <summary>
    /// An estimated start as the heat sheet and the phones show it (11 Nov
    /// 2026): "about 20:42", local time as AppTime shows it, rounded to the
    /// nearest minute.
    /// </summary>
    public static string About(DateTime startUtc)
    {
        var rounded = new DateTime((startUtc.Ticks + TimeSpan.TicksPerMinute / 2) / TimeSpan.TicksPerMinute * TimeSpan.TicksPerMinute, DateTimeKind.Utc);

        return $"about {AppTime.TimeOfDay(rounded.ToLocalTime())}";
    }

    /// <summary>
    /// Each round's "about 20:42" from a plan, by round id - null for a round
    /// already run, or with no plan (no race day open, nothing left to run).
    /// </summary>
    /// <remarks>
    /// The promised time when the night is running early (12 Nov 2026, setup
    /// plan step 8 - see StartPromise): a heat that will wait for 20:42 is
    /// never shown as about 20:35.
    /// </remarks>
    public static Func<string, string?> AboutOf(Projection? plan) =>
        roundId => plan?.ShownStartOf(roundId) is { } start ? About(start) : null;

    /// <summary>
    /// The Big Screen's line of races to come, with their estimated starts
    /// (12 Nov 2026, setup plan step 8, build plan 2.2): "UP NEXT:  Stock Q2
    /// Heat 3 about 20:42  •  Mod Q2 Heat 1 about 20:49". A race with no
    /// estimate is listed without one. Empty with nothing to come.
    /// </summary>
    public static string UpNext(IEnumerable<(string ClassName, string Label, string? About)> races)
    {
        var parts = races
            .Select(r => string.Join(" ", new[] { r.ClassName, r.Label, r.About ?? string.Empty }.Where(s => !string.IsNullOrWhiteSpace(s))))
            .Where(s => s.Length > 0)
            .ToList();

        return parts.Count == 0 ? string.Empty : "UP NEXT:  " + string.Join("  •  ", parts);
    }

    /// <summary>A length in whole minutes, as a saving or an overrun is said: "40 min", "1 h 05 min".</summary>
    public static string Minutes(TimeSpan length)
    {
        var minutes = Math.Max(0, (int)Math.Round(length.TotalMinutes));

        return minutes < 60
            ? $"{minutes} min"
            : $"{minutes / 60} h {minutes % 60:00} min";
    }

    /// <summary>
    /// The plan as the Next Steps card says it, starting with a space:
    /// " Finishes about 22:40 - 40:00 past the 22:00 out-by. To finish in
    /// time: drop Q3 (6 races) to save 48:00; ..." Times are local, as
    /// AppTime shows them.
    /// </summary>
    public static string Note(Projection plan)
    {
        var finish = AppTime.TimeOfDay(plan.FinishUtc.ToLocalTime())
                     + (plan.BreakTime > TimeSpan.Zero ? $" (with {Minutes(plan.BreakTime)} of breaks)" : string.Empty);

        if (plan.OutByUtc is not { } outBy)
        {
            return $" Finishes about {finish}.";
        }

        var outByText = AppTime.TimeOfDay(outBy.ToLocalTime());

        if (plan.Over is not { } over)
        {
            return $" Finishes about {finish}, inside the {outByText} out-by time.";
        }

        var fixes = plan.Fixes.Count == 0
            ? string.Empty
            : $" To finish in time: {string.Join("; ", plan.Fixes.Select(f => f.Text))}.";

        return $" Finishes about {finish} - {Minutes(over)} past the {outByText} out-by time.{fixes}";
    }
}
