namespace HeatCaller;

/// <summary>
/// The Personal Bests tab's reading: how real each PB is.
///
/// 13 Nov 2026, the Race Engineer rework (see RaceCoach). A best lap is one
/// lap, and one lap can be a tow, a lucky line or a fresh pack. What a
/// driver wants to know is whether it is their pace or a good day - and the
/// races since say so: a PB they keep coming back to is solid; one they
/// have never been near since is a one-off, and the easiest time on the
/// list to find. Worked out from the saved sessions, so it reads every
/// race already run.
/// </summary>
public static partial class RaceCoach
{
    /// <summary>How real a PB is.</summary>
    public enum PbReality
    {
        /// <summary>Too few races there to say.</summary>
        Fresh,

        /// <summary>Within <see cref="NearPbMs"/> of it in most recent races there.</summary>
        Solid,

        /// <summary>Close since, but not near it most of the time.</summary>
        InReach,

        /// <summary>Never within <see cref="CloseToPbMs"/> of it since it was set.</summary>
        OneOff,
    }

    /// <summary>A race whose best lap is this close to the PB was at PB pace: a tenth.</summary>
    public const long NearPbMs = 100;

    /// <summary>A race never this close to the PB since it was set says the PB was a one-off: three tenths.</summary>
    public const long CloseToPbMs = 300;

    /// <summary>The recent races looked at: the last five there.</summary>
    public const int PbWindow = 5;

    /// <summary>The fewest races at a track before a PB there can be read. Three, as PaceAnalysis needs three laps.</summary>
    public const int PbLeastRaces = 3;

    /// <summary>The fewest races since the PB before "never near it since" means anything.</summary>
    public const int OneOffLeastSince = 2;

    /// <summary>One race at a track: when it started and the driver's best lap in it.</summary>
    public readonly record struct PbRace(DateTime StartedUtc, long BestMs);

    /// <summary>
    /// How real one PB is: the verdict, the races it was read from, how
    /// many of the last <see cref="PbWindow"/> came near it, and what it
    /// means in a sentence said to the driver.
    /// </summary>
    public sealed record PbRead(PbReality Reality, int Races, int Near, int Window, long RealPaceMs, string Meaning)
    {
        /// <summary>The pill's word: "Solid", "One-off", "In reach", "Fresh".</summary>
        public string Label => PbLabel(Reality);
    }

    /// <summary>The word on a PB's pill.</summary>
    public static string PbLabel(PbReality reality) => reality switch
    {
        PbReality.Solid => "Solid",
        PbReality.OneOff => "One-off",
        PbReality.InReach => "In reach",
        _ => "Fresh",
    };

    /// <summary>
    /// A driver's races on a layout, oldest first, each with their best lap
    /// in it: the Personal Bests page's own rule - scored sessions, ghosts
    /// left out, the driver by name whatever its case - and none at or
    /// before a reset's cutoff (<paramref name="countingSinceUtc"/>).
    /// </summary>
    public static List<PbRace> RacesAt(IEnumerable<SessionRecord> sessions, string driver, string layout, DateTime? countingSinceUtc)
    {
        return sessions
            .Where(s => !s.IsNotScored()
                        && !string.IsNullOrWhiteSpace(s.TrackLayoutName)
                        && string.Equals(s.TrackLayoutName.Trim(), layout.Trim(), StringComparison.OrdinalIgnoreCase)
                        && (countingSinceUtc is not { } cut || s.StartedAtUtc > cut))
            .Select(s => (Session: s, Best: s.Results
                .Where(r => r.FastLapMs > 0
                            && !GhostService.IsGhost(r.Transponder)
                            && string.Equals(r.DriverName.Trim(), driver.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(r => r.FastLapMs)
                .DefaultIfEmpty(0)
                .Min()))
            .Where(x => x.Best > 0)
            .OrderBy(x => x.Session.StartedAtUtc)
            .Select(x => new PbRace(x.Session.StartedAtUtc, x.Best))
            .ToList();
    }

    /// <summary>
    /// How real a PB is, from the races at that track (oldest first; the
    /// race the PB was set in among them).
    ///
    ///   Fresh    fewer than <see cref="PbLeastRaces"/> races there.
    ///   One-off  <see cref="OneOffLeastSince"/> or more races since it, and
    ///            none within <see cref="CloseToPbMs"/> of it.
    ///   Solid    within <see cref="NearPbMs"/> of it in most of the last
    ///            <see cref="PbWindow"/> races (the PB's own race counts:
    ///            it was at PB pace).
    ///   In reach the rest - close sometimes, not most of the time.
    /// </summary>
    public static PbRead ReadPb(long pbMs, DateTime pbSetUtc, IReadOnlyList<PbRace> races)
    {
        var recent = races.Skip(Math.Max(0, races.Count - PbWindow)).ToList();
        var near = recent.Count(r => r.BestMs - pbMs <= NearPbMs);
        var since = races.Where(r => r.StartedUtc > pbSetUtc).ToList();

        // A driver's real pace there: the middle of their recent races'
        // bests, or of the races since the PB when it stands apart.
        var sample = since.Count > 0 ? since.Skip(Math.Max(0, since.Count - PbWindow)).ToList() : recent;
        var real = sample.Count == 0 ? pbMs : Middle(sample.Select(r => r.BestMs).ToList());

        if (races.Count < PbLeastRaces)
        {
            return new PbRead(PbReality.Fresh, races.Count, near, recent.Count, real,
                $"Only {Count(races.Count, "race")} here, too early to say.");
        }

        if (since.Count >= OneOffLeastSince && since.All(r => r.BestMs - pbMs > CloseToPbMs))
        {
            return new PbRead(PbReality.OneOff, races.Count, near, recent.Count, real,
                $"Never within 0.30s of it since {ShortDay(pbSetUtc)}. Treat {PaceAnalysis.Time(real)} as your real pace here.");
        }

        if (near * 2 > recent.Count)
        {
            return new PbRead(PbReality.Solid, races.Count, near, recent.Count, real,
                $"Within 0.10s of it in {near} of your last {recent.Count} races. This is your real pace.");
        }

        var close = recent.Count(r => r.BestMs - pbMs <= CloseToPbMs);

        return new PbRead(PbReality.InReach, races.Count, near, recent.Count, real,
            $"Within 0.30s in {close} of your last {recent.Count} races, but not near it every time yet.");
    }

    /// <summary>One PB as the headline and the drill read it: the track, the lap, when it was set, and its reading.</summary>
    public sealed record PbLine(string Track, long PbMs, DateTime SetUtc, PbRead Read);

    /// <summary>
    /// What a driver's PBs add up to, in a sentence or two said to them:
    /// which one is real, and which is soft - the one-off with the most
    /// time between it and their real pace, where the easiest time is.
    /// </summary>
    public static string PbHeadline(IReadOnlyList<PbLine> lines)
    {
        if (lines.Count == 0)
        {
            return "No personal bests yet. Race a lap on any track and it shows here.";
        }

        var solid = lines
            .Where(l => l.Read.Reality == PbReality.Solid)
            .OrderByDescending(l => l.Read.Near)
            .ThenByDescending(l => l.Read.Races)
            .FirstOrDefault();

        var soft = SoftestPb(lines);
        var reach = lines.FirstOrDefault(l => l.Read.Reality == PbReality.InReach);

        if (solid is not null && soft is not null)
        {
            return $"Your {solid.Track} PB is the real thing. Your {soft.Track} PB is a one-off you haven't been near since "
                   + $"{ShortDay(soft.SetUtc)}, so that is where the easiest time is.";
        }

        if (soft is not null)
        {
            return $"Your {soft.Track} PB is a one-off you haven't been near since {ShortDay(soft.SetUtc)}, so that is where the easiest time is.";
        }

        if (solid is not null)
        {
            return reach is null
                ? $"Your {solid.Track} PB is the real thing. You run close to it most nights."
                : $"Your {solid.Track} PB is the real thing. {reach.Track} is the one to firm up: you get close, but not every time.";
        }

        if (reach is not null)
        {
            return $"You get close to your {reach.Track} PB, but not every time. A few more races near it make it your real pace.";
        }

        return "Too few races on any track to say how real these PBs are yet. A few more and it will show.";
    }

    /// <summary>The one-off with the most time between it and the driver's real pace, or null.</summary>
    private static PbLine? SoftestPb(IReadOnlyList<PbLine> lines) =>
        lines
            .Where(l => l.Read.Reality == PbReality.OneOff)
            .OrderByDescending(l => l.Read.RealPaceMs - l.PbMs)
            .FirstOrDefault();

    /// <summary>One thing to try, keyed to the softest PB on the list.</summary>
    public static string? PbDrill(IReadOnlyList<PbLine> lines)
    {
        if (lines.Count == 0)
        {
            return null;
        }

        if (SoftestPb(lines) is { } soft)
        {
            // Halfway between the PB and their real pace, to the hundredth:
            // a lap they can run every lap, on the way to the PB.
            var target = (soft.PbMs + soft.Read.RealPaceMs) / 2 / 10 * 10;

            return $"Next {soft.Track} night, aim for {target / 1000.0:0.00} every lap in practice rather than chasing the "
                   + $"{soft.PbMs / 1000.0:0.00}. A repeatable {target / 1000.0:0.00} beats one {soft.PbMs / 1000.0:0.00}.";
        }

        if (lines.FirstOrDefault(l => l.Read.Reality == PbReality.InReach) is { } reach)
        {
            return $"Next {reach.Track} night, get within 0.10s of your {PaceAnalysis.Time(reach.PbMs)} in two races running. "
                   + "Then it is your pace, not a good day.";
        }

        if (lines.All(l => l.Read.Reality == PbReality.Fresh))
        {
            return "Race the same track a few nights running. Three races there is the least it takes to tell real pace from a good day.";
        }

        return "Your PBs are your real pace. The next step is setup or a new line, not trying harder.";
    }

    /// <summary>A date in a sentence: "3 Sep".</summary>
    private static string ShortDay(DateTime utc) => utc.ToLocalTime().ToString("d MMM");
}
