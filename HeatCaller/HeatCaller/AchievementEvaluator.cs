namespace HeatCaller;

/// <summary>One badge, just earned, by one driver.</summary>
public sealed record AchievementUnlock(string Transponder, string DriverName, AchievementDefinition Badge, string Detail);

/// <summary>
/// Works out what a finished race earned everyone in it.
///
/// Runs once, after the result is saved, rather than live during the race.
/// Almost every badge here needs the finished result to be knowable at all -
/// a win, a podium, the fastest lap of the race - and the ones that don't
/// would still be announced over the racing. The wrap-up is where they
/// belong.
///
/// Deliberately pure: it is handed the record, the history and the stores,
/// and hands back a list. Nothing in here speaks, draws or saves, so it can
/// be reasoned about - and eventually tested - without a race running.
/// </summary>
public static partial class AchievementEvaluator
{
    /// <summary>A driver has to complete this many laps for a race to count towards anything.</summary>
    private const int MinimumLapsToCount = 1;

    /// <summary>How close every lap must be to a driver's own average to count as machine-like.</summary>
    private const double ConsistencyTolerance = 0.10;

    /// <summary>The same test, five times stricter - the Swiss Watch band.</summary>
    private const double TightConsistencyTolerance = 0.05;

    public static List<AchievementUnlock> EvaluateFinishedRace(
        SessionRecord record,
        IReadOnlyList<SessionRecord> history,
        AchievementStore store,
        RivalryStore? rivalries = null,
        ProfileStore? profiles = null,
        AchievementContext? context = null)
    {
        var unlocks = new List<AchievementUnlock>();
        profiles ??= context?.Profiles;

        // Practice isn't a race, and nothing here should be earnable by
        // lapping alone on a quiet evening.
        if (record.SessionType != RaceSessionType.Race || record.Results.Count == 0)
        {
            return unlocks;
        }

        // Nor is a saved demo race: every lap in it was made up by the
        // simulator, so a win, a fast lap or a streak there was earned by
        // nobody.
        if (record.IsDemoSession())
        {
            return unlocks;
        }

        // The race as the achievements must see it: real drivers only.
        //
        // A ghost is a replay of a lap time, not a competitor, and leaving
        // it in the field did two things - the second worse than the first.
        // It collected badges of its own, and it took them off everybody
        // else. A ghost that finished ahead of you put you second, so your
        // win never counted. A ghost's fast lap beat yours, so the
        // fastest-lap badge went to nobody. Three ghosts padded a two-car
        // race into a "full house".
        //
        // The stored record is left exactly as it was - it is history, and
        // it is not rewritten to suit a badge. Position is taken from this
        // list's ORDER instead, which closes the places up behind whatever
        // was removed.
        var results = record.Results
            .Where(entry => !GhostService.IsGhost(entry.Transponder))
            .OrderBy(entry => entry.Position)
            .ToList();

        if (results.Count == 0)
        {
            return unlocks;
        }

        int PlaceOf(SessionResultEntry entry) => results.IndexOf(entry) + 1;

        // How many cars actually raced: the field every "not alone" test
        // below is judged against. The saved results carry a row for every
        // car on the board - a hand-released grid files the whole roster at
        // the green, and a black flag leaves the DQ'd car in with no laps -
        // so counting rows let cars that never crossed the line make up the
        // numbers. One driver lapping a hand-released heat while five sat on
        // the line was awarded Winner, On the Box, Quickest of the Day, A Lap
        // Clear (over a car on no laps) and Full House "6 on track", and had
        // a win and a podium added to totals nothing rebuilds. Those rows
        // always sort to the bottom, so PlaceOf is unchanged for anyone who
        // raced.
        var field = results.Count(r => r.Laps >= MinimumLapsToCount);

        // Where a driver started, counted among the real cars the same way
        // PlaceOf counts where they finished - zero when the grid was never
        // known. The stored slot is the roster's, ghosts included, so a
        // ghost dragged to the top of the A-main made pole "P2": the winner
        // from the front of the real field lost Pole Converted and Grand
        // Slam, and a driver who started fourth was credited with a place
        // made up that was really the ghost's (25 Sep audit).
        int GridOf(SessionResultEntry entry) =>
            entry.StartPosition <= 0
                ? 0
                : 1 + results.Count(o => o.StartPosition > 0 && o.StartPosition < entry.StartPosition);

        // Every race this driver has run BEFORE this one - the history is
        // the source for anything historical, and this record may or may not
        // already be in it depending on call order, so it is excluded by id
        // rather than by assuming.
        var earlier = history
            // Demo races are left out for the same reason - a simulated lap
            // must not stand as the track record a real one has to beat.
            .Where(r => r.SessionType == RaceSessionType.Race
                        && !r.IsDemoSession()
                        && !string.Equals(r.Id, record.Id, StringComparison.Ordinal))
            .ToList();

        // What a personal best is measured against. At a club, races only,
        // as ever; at a home track, practice too - a lap there is just as
        // much the driver's own, and a race "best" that practice already
        // beat would be no best at all.
        var bestsPool = context?.AtHome == true ? EarlierSessions(history, record) : earlier;

        var fastestOfRace = results
            .Where(r => r.FastLapMs > 0)
            .Select(r => r.FastLapMs)
            .DefaultIfEmpty(0)
            .Min();

        // The quickest lap of the race among the cars allowed to hold a
        // track record - which is not every real car. Race Control's live
        // record (CheckAndAnnounceTrackRecord) turns away ghosts, rental
        // transponders and drivers whose profile says "Exclude from track
        // records", and the badge has to follow the same rule or the two
        // disagree: an excluded guest who ran 9.700 under a 10.000 record
        // was announced as Record Holder and had the layout counted towards
        // Record Collector, while the club driver on 9.800 - who the Track
        // Layouts page rightly names as the new holder - got nothing,
        // because his lap was not the quickest in the race. fastestOfRace
        // itself is left alone: the Fastest Lap badge is about tonight's
        // race, not the club's records, and a guest can earn it.
        var recordLapOfRace = results
            .Where(r => r.FastLapMs > 0 && CanHoldTrackRecord(r.Transponder, profiles))
            .Select(r => r.FastLapMs)
            .DefaultIfEmpty(0)
            .Min();

        // The quickest opening lap - green to first crossing - among real
        // drivers who went on to race at least one more lap. Zero on a
        // jump start (see RaceControlSession), which is left out rather
        // than counted as the quickest getaway there has ever been. Three
        // cars at least, or "quickest start" is just "not last".
        var openingLaps = results
            .Where(r => r.LapTimesMs.Count >= 2 && r.LapTimesMs[0] > 0)
            .Select(r => r.LapTimesMs[0])
            .ToList();

        var quickestOpeningLap = openingLaps.Count >= 3 ? openingLaps.Min() : 0;

        // The club's top race winner(s) before tonight, for Giant Killer.
        // Worked out once for the race rather than once per driver - it
        // walks the whole of history. Only races with two or more real cars
        // count as wins here, so nobody becomes the giant by lapping alone.
        var winsByDriver = earlier
            .Select(WinnerOf)
            .Where(t => t is not null)
            .GroupBy(t => t!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var topWins = winsByDriver.Count == 0 ? 0 : winsByDriver.Values.Max();

        var giants = winsByDriver
            .Where(pair => pair.Value == topWins)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Each driver's own earlier races, worked out once per driver per
        // race. Streak Breaker asks it of every car a driver beat, so
        // without this a full grid walked the whole of history dozens of
        // times over after every race.
        var racesEnteredCache = new Dictionary<string, List<SessionRecord>>(StringComparer.OrdinalIgnoreCase);

        List<SessionRecord> RacesEnteredBy(string transponder)
        {
            if (!racesEnteredCache.TryGetValue(transponder, out var races))
            {
                races = RacesEntered(earlier, transponder);
                racesEnteredCache[transponder] = races;
            }

            return races;
        }

        foreach (var entry in results)
        {
            if (string.IsNullOrWhiteSpace(entry.Transponder) || entry.Laps < MinimumLapsToCount)
            {
                continue;
            }

            var driver = store.For(entry.Transponder);
            var name = string.IsNullOrWhiteSpace(entry.DriverName) ? entry.Transponder : entry.DriverName;

            // --- Running totals, updated once per race ---
            driver.RacesFinished++;
            driver.TotalLaps += entry.Laps;

            // Only against somebody: alone on track, first is not a win
            // and the box is not a podium. It used to count both, so one
            // driver lapping a Quick Race on their own was awarded Winner
            // and On the Box - and every race alone after that walked them
            // on towards Serial Winner.
            if (PlaceOf(entry) == 1 && field > 1)
            {
                driver.Wins++;
            }

            if (PlaceOf(entry) is >= 1 and <= 3 && field > 1)
            {
                driver.Podiums++;
            }

            void Award(string id, string detail = "")
            {
                if (store.Award(entry.Transponder, id, record.Id, detail)
                    && AchievementCatalogue.Get(id) is { } badge)
                {
                    unlocks.Add(new AchievementUnlock(entry.Transponder, name, badge, detail));
                }
            }

            // --- Single race ---
            var streak = LongestCleanRun(entry.LapTimesMs);

            if (streak >= 50)
            {
                Award(AchievementCatalogue.CleanFifty, $"{streak} laps");
            }
            else if (streak >= 25)
            {
                Award(AchievementCatalogue.CleanTwentyFive, $"{streak} laps");
            }
            else if (streak >= 10)
            {
                Award(AchievementCatalogue.CleanTen, $"{streak} laps");
            }

            var setFastestLap = entry.FastLapMs > 0 && entry.FastLapMs == fastestOfRace && field > 1;

            if (setFastestLap)
            {
                Award(AchievementCatalogue.FastestLap, Seconds(entry.FastLapMs));
            }

            // A long race in its own right, whatever the result was.
            if (entry.Laps >= 30)
            {
                Award(AchievementCatalogue.DistanceRunner, $"{entry.Laps} laps");
            }

            // Won by a clear lap. Measured against whoever finished second
            // rather than against the field, since that is the gap that was
            // actually raced.
            if (PlaceOf(entry) == 1 && field > 1)
            {
                var second = results.Count > 1 ? results[1] : null;

                if (second is not null && entry.Laps >= second.Laps + 1)
                {
                    Award(AchievementCatalogue.LapClear, $"{Plural.Of(entry.Laps - second.Laps, "lap")}");
                }

                // The two ends of the same gap, and only when both cars
                // covered the same distance - see ClosestGapMs for why
                // elapsed times on different lap counts can't be compared.
                if (second is not null && second.Laps == entry.Laps && entry.ElapsedMs > 0 && second.ElapsedMs > 0)
                {
                    var marginMs = second.ElapsedMs - entry.ElapsedMs;

                    if (marginMs >= 0 && marginMs < 200)
                    {
                        Award(AchievementCatalogue.NailBiter, $"by {marginMs / 1000.0:0.000}s");
                    }
                    else if (marginMs >= 5000)
                    {
                        Award(AchievementCatalogue.CountryMile, $"by {marginMs / 1000.0:0.0}s");
                    }
                }

                // The tortoise and the hare: won it, though at least two
                // others had a quicker lap in them. Somebody who kept it on
                // the track while the quicker cars didn't.
                if (entry.FastLapMs > 0)
                {
                    var quicker = results.Count(o => !ReferenceEquals(o, entry) && o.FastLapMs > 0 && o.FastLapMs < entry.FastLapMs);

                    if (quicker >= 2)
                    {
                        Award(AchievementCatalogue.SlowAndSteady, $"{quicker} had quicker laps");
                    }
                }

                if (field >= 6)
                {
                    Award(AchievementCatalogue.FullHouse, $"{field} on track");
                }
            }

            // Within a second of the car in front or behind ON THE SAME LAP.
            // Comparing elapsed times across different lap counts would call
            // every lapped car a photo finish.
            if (ClosestGapMs(results, entry) is { } gapMs && gapMs <= 1000)
            {
                Award(AchievementCatalogue.PhotoFinish, $"{gapMs / 1000.0:0.000}s");
            }

            // Faster over the back half than the front half - somebody who
            // found the track rather than faded on it.
            if (FinishedStrong(entry.LapTimesMs) is { } improvementMs)
            {
                Award(AchievementCatalogue.FinishedStrong, $"{improvementMs / 1000.0:0.000}s a lap quicker");
            }

            // These three need facts a result only carries if it was
            // recorded by a version that keeps them. An older race has
            // zeroes in those fields, which reads identically to a spotless,
            // lights-to-flag victory - so the flag, not the numbers, decides
            // whether to even look.
            if (record.RecordsIncidents)
            {
                // Long enough to be an achievement. Two clean laps is not a
                // spotless race, it's a short one.
                if (entry.RejectedLaps == 0 && entry.Laps >= 5)
                {
                    Award(AchievementCatalogue.Flawless, $"{entry.Laps} laps");
                }

                // Led at the end of every lap they completed, and won.
                // Leading throughout and then losing it on the last lap is a
                // different story, and not this badge.
                if (PlaceOf(entry) == 1 && entry.Laps > 1 && entry.LapsLed == entry.Laps && field > 1)
                {
                    Award(AchievementCatalogue.WireToWire, $"{entry.LapsLed} laps led");
                }

                // Won from fourth or worse. Start position is zero when the
                // grid was never known, which is not the same as starting at
                // the back, so it has to be a real number.
                if (PlaceOf(entry) == 1 && GridOf(entry) > 3)
                {
                    Award(AchievementCatalogue.ComeFromBehind, $"from P{GridOf(entry)}");
                }

                // Pole converted. The opposite story to the one above, and
                // the one that is quietly harder to do than it sounds.
                if (PlaceOf(entry) == 1 && GridOf(entry) == 1 && field > 1)
                {
                    Award(AchievementCatalogue.PoleConverted);
                }

                // Won having led almost none of it - the late pass, which is
                // the race everybody remembers.
                if (PlaceOf(entry) == 1 && entry.Laps >= 4 && entry.LapsLed <= 1 && field > 1)
                {
                    Award(AchievementCatalogue.LateShow, entry.LapsLed == 0 ? "led none of it" : "led one lap");
                }

                // Quickest AND clean. Either alone is a badge already; doing
                // both in the same race is a different thing.
                if (setFastestLap && entry.RejectedLaps == 0 && entry.Laps >= 5)
                {
                    Award(AchievementCatalogue.QuickAndTidy, Seconds(entry.FastLapMs));
                }

                // Everything at once: pole, the win, the fastest lap, and in
                // front at every crossing. Needs laps led and the grid, which
                // is why it lives inside this check.
                if (PlaceOf(entry) == 1 && field > 1 && GridOf(entry) == 1
                    && setFastestLap && entry.Laps > 1 && entry.LapsLed == entry.Laps)
                {
                    Award(AchievementCatalogue.GrandSlam, $"{entry.Laps} laps led");
                }
            }

            // --- The five added later ---

            // The double: won it AND was quickest in it. Two badges already
            // exist for the halves; this is for doing both in one race,
            // which is a different thing from doing each on a different day.
            if (PlaceOf(entry) == 1 && setFastestLap)
            {
                Award(AchievementCatalogue.Double, Seconds(entry.FastLapMs));
            }

            // Three in a row, this one included. Checked against the two
            // races before it that this driver actually appeared in, so a
            // night off does not break the run - only a race they entered
            // and did not win.
            if (PlaceOf(entry) == 1 && field > 1 && WonTheLastTwo(earlier, entry.Transponder))
            {
                Award(AchievementCatalogue.HatTrick);
            }

            // Places made up. StartPosition is only recorded on races run
            // since it was added, and is zero before that - which would read
            // as a heroic charge from nowhere, so it has to be positive.
            if (entry.StartPosition > 0)
            {
                var gained = GridOf(entry) - PlaceOf(entry);

                if (gained >= 4)
                {
                    Award(AchievementCatalogue.Charge, $"P{GridOf(entry)} to P{PlaceOf(entry)}");
                }

                // From the very back of a proper field to the podium. Last
                // is judged against the real cars in the race - no real car
                // lined up behind - rather than by slot number: ghosts on
                // the grid push real slots back, and a slot number would
                // then call the middle of the field the back.
                if (field >= 5 && PlaceOf(entry) <= 3
                    && !results.Any(o => o.StartPosition > entry.StartPosition))
                {
                    Award(AchievementCatalogue.ComebackKid, $"P{GridOf(entry)} to P{PlaceOf(entry)}");
                }

                // The quickest getaway from third on the grid or further
                // back. The front two are left out: from there, the quickest
                // opening lap is mostly just the shortest one. Counted among
                // the real cars, for the same reason as above.
                if (quickestOpeningLap > 0
                    && results.Count(o => o.StartPosition > 0 && o.StartPosition < entry.StartPosition) >= 2
                    && entry.LapTimesMs.Count >= 2 && entry.LapTimesMs[0] == quickestOpeningLap)
                {
                    Award(AchievementCatalogue.RocketStart, $"from P{GridOf(entry)}");
                }
            }

            // Last across the line, but still there at the flag and still
            // on the winner's lap or one behind it. For the driver who kept
            // at it when it would have been easy to park - never for a
            // two-car race, where last is also second.
            if (field >= 4 && PlaceOf(entry) == field && entry.Laps >= 5
                && results[0].Laps - entry.Laps <= 1)
            {
                Award(AchievementCatalogue.NeverSayDie, $"{entry.Laps} laps");
            }

            // This driver's earlier races, newest first - the ones they
            // actually turned a lap in. Several of the badges below are
            // about a run of races or a count across them, and all of them
            // use this same list so they agree on what "your last race"
            // means.
            var myEarlier = RacesEnteredBy(entry.Transponder);
            var myPlaces = myEarlier.Select(r => PlaceInRace(r, entry.Transponder)).ToList();

            // Quickest in three races running - this one and the two this
            // driver entered before it. A night off doesn't break the run.
            if (setFastestLap && myEarlier.Count >= 2
                && myEarlier.Take(2).All(r => SetFastestLapIn(r, entry.Transponder)))
            {
                Award(AchievementCatalogue.PaceSetter, Seconds(entry.FastLapMs));
            }

            // Quickest lap saved for last. Three laps minimum, because in a
            // two-lap race "the last one" is half the race and means
            // nothing.
            //
            // Flying laps only. LapTimesMs[0] is the run from the green to
            // the first crossing - a second or two off a grid behind the
            // loop - and it sat in the minimum, so no real last lap could
            // ever be the quickest and the award could not be won. It also
            // counted towards the three, which made the minimum two laps.
            var flyingLaps = entry.LapTimesMs.Skip(1).Where(l => l > 0).ToList();

            if (flyingLaps.Count >= 3)
            {
                var last = entry.LapTimesMs[^1];

                if (last > 0 && last == flyingLaps.Min())
                {
                    Award(AchievementCatalogue.LastLapHero, Seconds(last));
                }
            }

            // --- Career milestones ---
            Award(AchievementCatalogue.FirstRace);

            if (driver.RacesFinished >= 10) { Award(AchievementCatalogue.TenRaces); }
            if (driver.RacesFinished >= 25) { Award(AchievementCatalogue.TwentyFiveRaces, $"{driver.RacesFinished} races"); }
            if (driver.RacesFinished >= 50) { Award(AchievementCatalogue.FiftyRaces); }
            if (driver.RacesFinished >= 100) { Award(AchievementCatalogue.HundredRaces); }
            if (driver.TotalLaps >= 1000) { Award(AchievementCatalogue.ThousandLaps, $"{driver.TotalLaps} laps"); }
            if (driver.TotalLaps >= 5000) { Award(AchievementCatalogue.FiveThousandLaps, $"{driver.TotalLaps} laps"); }
            if (driver.TotalLaps >= 10000) { Award(AchievementCatalogue.TenThousandLaps, $"{driver.TotalLaps} laps"); }
            if (driver.Wins >= 1) { Award(AchievementCatalogue.FirstWin); }
            if (driver.Wins >= 10) { Award(AchievementCatalogue.TenWins); }
            if (driver.Wins >= 25) { Award(AchievementCatalogue.TwentyFiveWins, $"{driver.Wins} wins"); }
            if (driver.Wins >= 50) { Award(AchievementCatalogue.FiftyWins, $"{driver.Wins} wins"); }
            if (driver.Podiums >= 1) { Award(AchievementCatalogue.FirstPodium); }
            if (driver.Podiums >= 3) { Award(AchievementCatalogue.PodiumRegular); }
            if (driver.Podiums >= 10) { Award(AchievementCatalogue.TenPodiums, $"{driver.Podiums} podiums"); }
            if (driver.Podiums >= 25) { Award(AchievementCatalogue.TwentyFivePodiums, $"{driver.Podiums} podiums"); }

            // Racing late. Judged in LOCAL time, because "after ten at
            // night" is about the club night, not about UTC.
            var localStart = record.StartedAtUtc == default
                ? DateTime.Now
                : record.StartedAtUtc.ToLocalTime();

            if (localStart.Hour >= 22 || localStart.Hour < 4)
            {
                Award(AchievementCatalogue.NightOwl, localStart.ToString("HH:mm"));
            }

            // Still turning up a year later. Taken from the earliest race
            // this driver actually appears in, not from when their profile
            // was created - a profile can be made months before anyone uses
            // it.
            var firstSeen = earlier
                .Where(r => r.Results.Any(e => string.Equals(e.Transponder, entry.Transponder, StringComparison.OrdinalIgnoreCase)))
                .Select(r => r.StartedAtUtc)
                .Where(d => d != default)
                .DefaultIfEmpty(default)
                .Min();

            if (firstSeen != default && record.StartedAtUtc != default
                && record.StartedAtUtc - firstSeen >= TimeSpan.FromDays(365))
            {
                Award(AchievementCatalogue.OneYearOn, $"since {firstSeen.ToLocalTime():MMM yyyy}");
            }

            // A win early in a career. Checked against history AND the
            // running total: history alone would call a veteran a rookie on
            // a fresh install, and the total alone would do the same for
            // anyone who raced before badges existed.
            if (PlaceOf(entry) == 1 && field > 1 && myEarlier.Count <= 4 && driver.RacesFinished <= 5)
            {
                Award(AchievementCatalogue.RookieSensation, $"in race {myEarlier.Count + 1}");
            }

            // --- The calendar ---
            //
            // All in LOCAL time, like Night Owl above: a Friday the 13th or
            // an early start is about the clock on the clubhouse wall.
            // Nothing here is judged when the start time was never known.
            if (record.StartedAtUtc != default)
            {
                var today = localStart.Date;
                var racedToday = myEarlier.Where(r => r.StartedAtUtc != default && LocalDay(r) == today).ToList();

                if (racedToday.Count + 1 >= 5)
                {
                    Award(AchievementCatalogue.IronDriver, $"{racedToday.Count + 1} races in a day");
                }

                if (PlaceOf(entry) == 1 && field > 1)
                {
                    var winsToday = racedToday.Count(r => WonOutright(r, entry.Transponder)) + 1;

                    if (winsToday >= 2)
                    {
                        Award(AchievementCatalogue.DoubleTrouble, $"{winsToday} wins in a day");
                    }
                }

                if (localStart.DayOfWeek == DayOfWeek.Sunday
                    && myEarlier.Any(r => r.StartedAtUtc != default && LocalDay(r) == today.AddDays(-1)))
                {
                    Award(AchievementCatalogue.WeekendWarrior, $"{today.AddDays(-1):d MMM} and {today:d MMM}");
                }

                // From four, so a very late night that runs past midnight
                // stays a Night Owl rather than becoming an early bird.
                if (localStart.Hour >= 4 && localStart.Hour < 9)
                {
                    Award(AchievementCatalogue.EarlyBird, localStart.ToString("HH:mm"));
                }

                if (localStart.DayOfWeek == DayOfWeek.Friday && localStart.Day == 13)
                {
                    Award(AchievementCatalogue.FridayThe13th, localStart.ToString("MMM yyyy"));
                }

                var festive = (localStart.Month == 12 && localStart.Day is 24 or 25 or 26 or 31)
                              || (localStart.Month == 1 && localStart.Day == 1);

                if (festive)
                {
                    Award(AchievementCatalogue.FestiveSpirit, localStart.ToString("d MMM yyyy"));
                }
            }

            // --- Variety, and finishing positions over a career ---
            var layouts = myEarlier
                .Select(r => r.TrackLayoutName)
                .Append(record.TrackLayoutName)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            if (layouts >= 3)
            {
                Award(AchievementCatalogue.Globetrotter, $"{layouts} layouts");
            }

            // The car is whatever the driver's profile said at the time of
            // each race - the result keeps its own copy - so changing car
            // and updating the profile is what counts it.
            var cars = myEarlier
                .Select(r => EntryOf(r, entry.Transponder)?.CarModel ?? string.Empty)
                .Append(entry.CarModel)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            if (cars >= 3)
            {
                Award(AchievementCatalogue.CarCollector, $"{cars} cars");
            }

            if (PlaceOf(entry) == 1 && field > 1 && !string.IsNullOrWhiteSpace(record.TrackLayoutName))
            {
                var winsHere = myEarlier.Count(r =>
                    string.Equals(r.TrackLayoutName, record.TrackLayoutName, StringComparison.OrdinalIgnoreCase)
                    && WonOutright(r, entry.Transponder)) + 1;

                if (winsHere >= 10)
                {
                    Award(AchievementCatalogue.KingOfTheHill, $"{winsHere} wins at {record.TrackLayoutName}");
                }
            }

            // Counted from history rather than kept as a running total, so
            // places from before this badge existed count as well. Only
            // judged in a race that adds to the count, so the badge lands
            // on the night it was actually earned.
            if (PlaceOf(entry) == 2)
            {
                var seconds = myPlaces.Count(p => p == 2) + 1;

                if (seconds >= 5)
                {
                    Award(AchievementCatalogue.Bridesmaid, $"{seconds} second places");
                }
            }

            if (PlaceOf(entry) == 4)
            {
                var fourths = myPlaces.Count(p => p == 4) + 1;

                if (fourths >= 3)
                {
                    Award(AchievementCatalogue.FourLeafClover, $"{fourths} fourth places");
                }
            }

            // --- Personal progress ---
            //
            // Judged per LAYOUT. A best lap from a different track says
            // nothing about whether this one was an improvement, and
            // comparing across layouts would hand out "New Heights" for
            // simply moving to a shorter circuit.
            var previousBest = BestLapOnLayout(bestsPool, entry.Transponder, record.TrackLayoutName);

            if (entry.FastLapMs > 0 && previousBest > 0 && entry.FastLapMs < previousBest)
            {
                Award(AchievementCatalogue.PersonalBest, Seconds(entry.FastLapMs));

                // A quarter of a second is an enormous step at this scale -
                // usually a setup change or a line that finally worked,
                // rather than a tidier lap.
                if (previousBest - entry.FastLapMs >= 250)
                {
                    Award(AchievementCatalogue.BigStep, $"{(previousBest - entry.FastLapMs) / 1000.0:0.000}s quicker");
                }

                if (HasImprovedThreeRunning(bestsPool, record, entry.Transponder))
                {
                    Award(AchievementCatalogue.ThreeMeetingsImproving);
                }

                var layoutsImproved = LayoutsImprovedOn(bestsPool, record, entry.Transponder);

                if (layoutsImproved >= 3)
                {
                    Award(AchievementCatalogue.Sharpshooter, $"{layoutsImproved} layouts");
                }
            }

            // The personal best broken, then broken again, then again, all
            // in the same race. Walked lap by lap against the best from
            // BEFORE tonight, so the first lap under it counts once and
            // each lap quicker than that counts again. Flying laps only -
            // the opening lap is not a lap (see LastLapHero above).
            if (previousBest > 0)
            {
                var running = previousBest;
                var bestsBroken = 0;

                foreach (var lap in entry.LapTimesMs.Skip(1))
                {
                    if (lap > 0 && lap < running)
                    {
                        running = lap;
                        bestsBroken++;
                    }
                }

                if (bestsBroken >= 3)
                {
                    Award(AchievementCatalogue.HotStreak, $"{bestsBroken} bests in one race");
                }
            }

            // An all-time best across every recorded race on this layout,
            // by anyone - which is what a track record is.
            // Not on a race with no layout: every such race would share one
            // blank "track", and its record went into TrackRecords as an
            // empty name that counted towards Three Track Records.
            //
            // The lap must also be the quickest of THIS race. History only
            // holds the races before tonight, so on its own the test let
            // every car that went under the old record through: when the
            // record was 10.000 and A ran 9.800 while B ran 9.900, both were
            // announced as the record holder and both had the layout counted
            // towards Three Track Records - yet B never held it, because A's
            // lap beat B's in the very same race. fastestOfRace is worked out
            // among the real cars only, so a ghost cannot take the record
            // away from a driver. Two drivers who tie on the quickest lap
            // both still get it, as they would share any other record.
            //
            // "Quickest" here means quickest among the cars that may hold a
            // record at all (see recordLapOfRace), and the driver must be
            // one of them - a rental or an excluded guest never takes the
            // badge, and never stops the eligible driver behind them from
            // taking it.
            if (entry.FastLapMs > 0 && entry.FastLapMs == recordLapOfRace
                && CanHoldTrackRecord(entry.Transponder, profiles)
                && !string.IsNullOrWhiteSpace(record.TrackLayoutName)
                && IsTrackRecord(earlier, record.TrackLayoutName, entry.FastLapMs, profiles))
            {
                if (!driver.TrackRecords.Contains(record.TrackLayoutName, StringComparer.OrdinalIgnoreCase))
                {
                    driver.TrackRecords.Add(record.TrackLayoutName);
                }

                Award(AchievementCatalogue.TrackRecordHolder, record.TrackLayoutName);

                if (driver.TrackRecords.Count >= 3)
                {
                    Award(AchievementCatalogue.ThreeTrackRecords, $"{driver.TrackRecords.Count} layouts");
                }
            }

            if (IsWithinTolerance(entry.LapTimesMs, ConsistencyTolerance))
            {
                Award(AchievementCatalogue.Consistent);

                // The same test, held to a fifth of the tolerance. Rare
                // enough on a real track to be worth its own badge.
                if (IsWithinTolerance(entry.LapTimesMs, TightConsistencyTolerance))
                {
                    Award(AchievementCatalogue.SwissWatch);
                }
            }

            // A whole race without the clean run ever breaking - every lap
            // inside the lap-to-lap window, start to finish.
            if (entry.LapTimesMs.Count >= 10 && LongestCleanRun(entry.LapTimesMs) >= entry.LapTimesMs.Count - 1)
            {
                Award(AchievementCatalogue.Unbroken, $"{entry.LapTimesMs.Count - 1} laps");
            }

            // A scrappy race put right in the very next one. Both races
            // have to have been recorded by a version that counts rejected
            // laps - see RecordsIncidents - or an old race's zero would be
            // taken for a clean one.
            if (record.RecordsIncidents && entry.RejectedLaps == 0 && entry.Laps >= 5
                && myEarlier.FirstOrDefault() is { RecordsIncidents: true } previousRace
                && EntryOf(previousRace, entry.Transponder) is { RejectedLaps: >= 3 } previousEntry)
            {
                Award(AchievementCatalogue.CleanSlate, $"after {previousEntry.RejectedLaps} rejected");
            }

            // Back on the box after a dry spell. Four or more on track, or
            // third place is simply where everybody finishes.
            if (PlaceOf(entry) <= 3 && field >= 4)
            {
                var drought = myPlaces.TakeWhile(p => p is > 3).Count();

                if (drought >= 5)
                {
                    Award(AchievementCatalogue.BounceBack, $"after {drought} races");
                }
            }

            // --- Rivalry and social ---
            if (rivalries is not null && BeatARival(results, entry, rivalries))
            {
                Award(AchievementCatalogue.BeatARival);
            }

            var opponents = DistinctOpponents(earlier, record, entry.Transponder);

            if (opponents >= 10)
            {
                Award(AchievementCatalogue.RacedEveryone, $"{opponents} drivers");
            }

            if (opponents >= 20)
            {
                Award(AchievementCatalogue.TwentyOpponents, $"{opponents} drivers");
            }

            if (opponents >= 50)
            {
                Award(AchievementCatalogue.FiftyOpponents, $"{opponents} drivers");
            }

            // Everyone this driver finished ahead of tonight. The details
            // below are numbers only, never the other driver's name - the
            // detail line is published to the club website.
            var beaten = results
                .Where(o => PlaceOf(o) > PlaceOf(entry) && !string.IsNullOrWhiteSpace(o.Transponder))
                .ToList();

            // Ahead of whoever has won the most races at the club - five at
            // least, or the "giant" is whoever happened to win once.
            if (topWins >= 5 && beaten.Any(o => giants.Contains(o.Transponder)))
            {
                Award(AchievementCatalogue.GiantKiller, $"beat a {topWins}-time winner");
            }

            // Ahead of somebody who came in on three straight wins.
            if (beaten.Any(o => WonLastThree(RacesEnteredBy(o.Transponder), o.Transponder)))
            {
                Award(AchievementCatalogue.StreakBreaker, "ended a winning run");
            }

            // The winner of this driver's last race, beaten the very next
            // time out. Not if this driver won that one too - there is
            // nothing to pay back.
            if (myEarlier.FirstOrDefault() is { } lastRace
                && WinnerOf(lastRace) is { } lastWinner
                && !string.Equals(lastWinner, entry.Transponder, StringComparison.OrdinalIgnoreCase)
                && beaten.Any(o => string.Equals(o.Transponder, lastWinner, StringComparison.OrdinalIgnoreCase)))
            {
                Award(AchievementCatalogue.Payback);
            }

            // Twenty races shared with one person. Checked only against the
            // people in THIS race, so it lands on the twentieth.
            // One pass over this driver's races, counting everybody met in
            // each, rather than one pass per person on the grid tonight.
            var racesWith = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var race in myEarlier)
            {
                foreach (var met in race.Results
                             .Select(e => e.Transponder)
                             .Where(t => !string.IsNullOrWhiteSpace(t))
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    racesWith[met] = racesWith.TryGetValue(met, out var n) ? n + 1 : 1;
                }
            }

            var mostShared = results
                .Where(o => !ReferenceEquals(o, entry) && !string.IsNullOrWhiteSpace(o.Transponder))
                .Select(o => (racesWith.TryGetValue(o.Transponder, out var n) ? n : 0) + 1)
                .DefaultIfEmpty(0)
                .Max();

            if (mostShared >= 20)
            {
                Award(AchievementCatalogue.OldFriends, $"{mostShared} races together");
            }

            // --- Home and garage (Oct 2026) ---
            EvaluateHomeAndGarage(entry, record, bestsPool, context, Award, practice: false);
        }

        // Nothing is announced that wasn't kept. When the write fails the
        // badges are not on anyone's record, and the next race - loading the
        // file as it still is - finds them unearned and unlocks them again,
        // so announcing them here meant the PA called "First Timer" for the
        // same drivers after every race of the night. The console already
        // carries SafeFile's reason for the failed write.
        if (!store.Save())
        {
            return new List<AchievementUnlock>();
        }

        // Rarest first. Race Control reads out only the first few, and in
        // the order they were worked out a Grand Slam came after the
        // Bronze badges the same race also earned - so the one worth
        // shouting about was the one left unsaid. Stable, so badges of the
        // same tier keep the order they were found in.
        return unlocks
            .OrderByDescending(unlock => (int)unlock.Badge.Tier)
            .ToList();
    }

    /// <summary>
    /// The longest run of laps within 25% of the one before it.
    ///
    /// Recomputed from the saved lap times rather than read back from the
    /// race: the live streak count isn't stored in a result, and this has to
    /// work on a race loaded from history months later. It uses the same
    /// lap-to-lap window the live rule does, so the two agree.
    ///
    /// The first lap is skipped - it runs from the green to the line rather
    /// than being a lap at all, and using it as a baseline would break every
    /// run on lap two.
    /// </summary>
    private static int LongestCleanRun(IReadOnlyList<long> lapTimesMs)
    {
        if (lapTimesMs.Count < 3)
        {
            return 0;
        }

        var best = 0;
        var current = 0;

        for (var i = 2; i < lapTimesMs.Count; i++)
        {
            var previous = lapTimesMs[i - 1];
            var allowance = previous * 0.25;

            if (previous > 0 && Math.Abs(lapTimesMs[i] - previous) <= allowance)
            {
                current = current == 0 ? 2 : current + 1;
                best = Math.Max(best, current);
            }
            else
            {
                current = 0;
            }
        }

        return best;
    }

    /// <summary>Every flying lap within the given fraction of this driver's own average for the race.</summary>
    private static bool IsWithinTolerance(IReadOnlyList<long> lapTimesMs, double tolerance)
    {
        // Enough laps for it to mean something. Three tidy laps is not a
        // machine, it's a short race.
        var flying = lapTimesMs.Skip(1).Where(t => t > 0).ToList();

        if (flying.Count < 8)
        {
            return false;
        }

        var average = flying.Average();
        return flying.All(t => Math.Abs(t - average) <= average * tolerance);
    }

    /// <summary>
    /// The gap to whoever finished immediately in front or behind, when
    /// they completed the same number of laps - null when there is nobody
    /// on the same lap either side.
    ///
    /// Same lap count matters: elapsed times are only comparable between
    /// two drivers who covered the same distance, and without that check a
    /// car two laps down "finishes" half a second behind the winner.
    /// </summary>
    private static long? ClosestGapMs(List<SessionResultEntry> results, SessionResultEntry entry)
    {
        if (entry.Laps <= 0 || entry.ElapsedMs <= 0)
        {
            return null;
        }

        long? closest = null;

        var place = results.IndexOf(entry);

        foreach (var other in results)
        {
            // Adjacent in the FILTERED order. Two cars either side of a
            // ghost are next to each other once it is gone, and the gap
            // between them is the one that was actually raced.
            if (ReferenceEquals(other, entry)
                || other.Laps != entry.Laps
                || other.ElapsedMs <= 0
                || Math.Abs(results.IndexOf(other) - place) != 1)
            {
                continue;
            }

            var gap = Math.Abs(other.ElapsedMs - entry.ElapsedMs);

            if (closest is null || gap < closest)
            {
                closest = gap;
            }
        }

        return closest;
    }

    /// <summary>
    /// How much quicker this driver's second half of the race was than
    /// their first, per lap - null when they were not quicker, or when
    /// there were too few laps for the halves to mean anything.
    ///
    /// The start lap is dropped first: it runs from the green to the line,
    /// so leaving it in loads the first half with something that is not a
    /// lap and would hand this badge to everybody.
    /// </summary>
    private static long? FinishedStrong(IReadOnlyList<long> lapTimesMs)
    {
        var flying = lapTimesMs.Skip(1).Where(t => t > 0).ToList();

        if (flying.Count < 8)
        {
            return null;
        }

        var half = flying.Count / 2;

        var first = flying.Take(half).Average();
        var second = flying.Skip(flying.Count - half).Average();

        // A meaningful margin rather than any margin at all - a hundredth
        // of a lap either way is noise, not a story.
        var gain = first - second;

        return gain >= first * 0.02 ? (long)Math.Round(gain) : null;
    }

    private static long BestLapOnLayout(IEnumerable<SessionRecord> history, string transponder, string layout) =>
        history
            .Where(r => string.Equals(r.TrackLayoutName, layout, StringComparison.OrdinalIgnoreCase))
            .SelectMany(r => r.Results)
            .Where(e => string.Equals(e.Transponder, transponder, StringComparison.OrdinalIgnoreCase) && e.FastLapMs > 0)
            .Select(e => e.FastLapMs)
            .DefaultIfEmpty(0)
            .Min();

    /// <summary>
    /// Whether this car's laps may stand as a track record - the same rule
    /// Race Control's live record uses (CheckAndAnnounceTrackRecord). A
    /// ghost never may, checked by transponder so it holds even without
    /// the profiles. Rentals and drivers marked "Exclude from track records"
    /// may not either; that part needs the profiles, and without them
    /// (an older caller) every real car is allowed, as before.
    ///
    /// The profile is today's, not the one the driver had on the night of
    /// an old race. That is deliberate and matches the live record, which
    /// also asks the profile as it stands: ticking the box on a guest is
    /// a statement that their laps are not the club's records, past ones
    /// included.
    /// </summary>
    private static bool CanHoldTrackRecord(string transponder, ProfileStore? profiles)
    {
        if (GhostService.IsGhost(transponder))
        {
            return false;
        }

        return profiles?.Get(transponder) is not ({ ExcludeFromRecords: true } or { IsRental: true });
    }

    private static bool IsTrackRecord(IEnumerable<SessionRecord> history, string layout, long lapMs, ProfileStore? profiles)
    {
        // Only laps that could ever have been the record count as the bar
        // to beat. Older saved races still carry their ghosts' rows, and a
        // ghost's - or an excluded guest's - quicker lap left there set a
        // bar no real driver could get under, so nobody ever earned the
        // badge on that layout even when the Track Layouts page said they
        // held the record.
        var bestEver = history
            .Where(r => string.Equals(r.TrackLayoutName, layout, StringComparison.OrdinalIgnoreCase))
            .SelectMany(r => r.Results)
            .Where(e => e.FastLapMs > 0 && CanHoldTrackRecord(e.Transponder, profiles))
            .Select(e => e.FastLapMs)
            .DefaultIfEmpty(0)
            .Min();

        // Nothing to beat isn't a record. The first lap ever timed on a new
        // layout is quickest by default, which is not an achievement.
        return bestEver > 0 && lapMs < bestEver;
    }

    /// <summary>Three races in a row on this layout, each quicker than the last.</summary>
    private static bool HasImprovedThreeRunning(IReadOnlyList<SessionRecord> earlier, SessionRecord current, string transponder)
    {
        var run = earlier
            .Where(r => string.Equals(r.TrackLayoutName, current.TrackLayoutName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.StartedAtUtc)
            .Select(r => r.Results.FirstOrDefault(e =>
                string.Equals(e.Transponder, transponder, StringComparison.OrdinalIgnoreCase) && e.FastLapMs > 0))
            .Where(e => e is not null)
            .Take(2)
            .Select(e => e!.FastLapMs)
            .ToList();

        // Needs two previous races plus this one. Newest first, so each
        // entry should be SLOWER than the one before it in this list for
        // the driver to have been improving.
        if (run.Count < 2)
        {
            return false;
        }

        var thisRace = current.Results.FirstOrDefault(e =>
            string.Equals(e.Transponder, transponder, StringComparison.OrdinalIgnoreCase))?.FastLapMs ?? 0;

        return thisRace > 0 && thisRace < run[0] && run[0] < run[1];
    }

    /// <summary>
    /// Whether this driver finished ahead of someone they're marked as
    /// rivals with.
    ///
    /// Matched on driver NAME, because that is what a rivalry line stores -
    /// see RivalryLine, which pairs DriverA with DriverB. A driver with no
    /// name on their profile can't be in a rivalry, so they're skipped
    /// rather than matched on an empty string against everyone else who
    /// also has none.
    /// </summary>
    private static bool BeatARival(List<SessionResultEntry> results, SessionResultEntry entry, RivalryStore rivalries)
    {
        if (string.IsNullOrWhiteSpace(entry.DriverName))
        {
            return false;
        }

        var place = results.IndexOf(entry);

        var behind = results
            .Where(e => results.IndexOf(e) > place && !string.IsNullOrWhiteSpace(e.DriverName))
            .Select(e => e.DriverName)
            .ToList();

        if (behind.Count == 0)
        {
            return false;
        }

        return rivalries.All.Any(rivalry =>
        {
            if (!rivalry.Enabled)
            {
                return false;
            }

            if (string.Equals(rivalry.DriverA, entry.DriverName, StringComparison.OrdinalIgnoreCase))
            {
                return behind.Contains(rivalry.DriverB, StringComparer.OrdinalIgnoreCase);
            }

            if (string.Equals(rivalry.DriverB, entry.DriverName, StringComparison.OrdinalIgnoreCase))
            {
                return behind.Contains(rivalry.DriverA, StringComparer.OrdinalIgnoreCase);
            }

            return false;
        });
    }

    /// <summary>
    /// Where a driver actually finished in a past race - among real drivers
    /// only, with the places closed up behind any ghost.
    ///
    /// The same rule the live race is judged by, applied to history, so a
    /// run of wins is not broken by a race a ghost happened to "win".
    /// </summary>
    private static int? PlaceInRace(SessionRecord race, string transponder)
    {
        var ranked = race.Results
            .Where(e => !GhostService.IsGhost(e.Transponder))
            .OrderBy(e => e.Position)
            .ToList();

        var index = ranked.FindIndex(e => string.Equals(e.Transponder, transponder, StringComparison.OrdinalIgnoreCase));

        return index < 0 ? null : index + 1;
    }

    /// <summary>
    /// Whether this driver won both of the last two races they entered.
    ///
    /// Races they ENTERED, not the last two races on the calendar. Missing
    /// a club night should not end a winning run; turning up and finishing
    /// second should.
    /// </summary>
    private static bool WonTheLastTwo(IReadOnlyList<SessionRecord> earlier, string transponder)
    {
        var recent = earlier
            .Where(r => r.Results.Any(e => string.Equals(e.Transponder, transponder, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(r => r.StartedAtUtc)
            .Take(2)
            .ToList();

        // Won outright - with somebody else in it. First in a race alone is
        // not a win, so two of those are not two thirds of a hat-trick.
        return recent.Count == 2 && recent.All(r => WonOutright(r, transponder));
    }

    /// <summary>
    /// How many different people this driver has shared a race with.
    ///
    /// Ghosts are excluded for the obvious reason: racing your own replay
    /// fifty times is not knowing fifty drivers, and every ghost carries a
    /// transponder of its own, so they counted as fifty different ones.
    /// </summary>
    private static int DistinctOpponents(IReadOnlyList<SessionRecord> earlier, SessionRecord current, string transponder) =>
        earlier
            .Append(current)
            .Where(r => r.Results.Any(e => string.Equals(e.Transponder, transponder, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(r => r.Results)
            .Select(e => e.Transponder)
            .Where(t => !string.IsNullOrWhiteSpace(t)
                        && !GhostService.IsGhost(t)
                        && !string.Equals(t, transponder, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    /// <summary>
    /// A driver's own line in a past race, or null if they weren't in it.
    /// Ghosts never match - a ghost carries its own transponder.
    /// </summary>
    private static SessionResultEntry? EntryOf(SessionRecord race, string transponder) =>
        race.Results.FirstOrDefault(e => string.Equals(e.Transponder, transponder, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Every earlier race this driver turned at least one lap in, newest
    /// first. Standing on the grid and never crossing the line is not
    /// having raced, for a run or a count.
    /// </summary>
    private static List<SessionRecord> RacesEntered(IReadOnlyList<SessionRecord> earlier, string transponder) =>
        earlier
            .Where(r => EntryOf(r, transponder) is { } mine && mine.Laps >= MinimumLapsToCount)
            .OrderByDescending(r => r.StartedAtUtc)
            .ToList();

    /// <summary>The calendar day a race started on, in local time.</summary>
    private static DateTime LocalDay(SessionRecord race) => race.StartedAtUtc.ToLocalTime().Date;

    /// <summary>
    /// Who won a past race among real cars - null when fewer than two real
    /// cars took part, since a race alone is not a race won. Only cars that
    /// turned a lap count, as for the field tonight: a grid row that never
    /// left the line used to make a lone run a win in history, feeding
    /// Hat-trick, Payback and the rest.
    /// </summary>
    private static string? WinnerOf(SessionRecord race)
    {
        var ranked = race.Results
            .Where(e => !GhostService.IsGhost(e.Transponder) && !string.IsNullOrWhiteSpace(e.Transponder)
                        && e.Laps >= MinimumLapsToCount)
            .OrderBy(e => e.Position)
            .ToList();

        return ranked.Count >= 2 ? ranked[0].Transponder : null;
    }

    /// <summary>Whether this driver won a past race that had somebody else in it.</summary>
    private static bool WonOutright(SessionRecord race, string transponder) =>
        WinnerOf(race) is { } winner && string.Equals(winner, transponder, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this driver won each of the last three races they entered
    /// before tonight. Takes their races as <see cref="RacesEntered"/> gives
    /// them - newest first.
    /// </summary>
    private static bool WonLastThree(IReadOnlyList<SessionRecord> racesEntered, string transponder)
    {
        var recent = racesEntered.Take(3).ToList();
        return recent.Count == 3 && recent.All(r => WonOutright(r, transponder));
    }

    /// <summary>
    /// Whether this driver set the fastest lap of a past race, among real
    /// cars, with at least one other real car setting a time to beat.
    /// </summary>
    private static bool SetFastestLapIn(SessionRecord race, string transponder)
    {
        var timed = race.Results
            .Where(e => !GhostService.IsGhost(e.Transponder) && e.FastLapMs > 0)
            .ToList();

        if (timed.Count < 2)
        {
            return false;
        }

        var mine = timed.FirstOrDefault(e => string.Equals(e.Transponder, transponder, StringComparison.OrdinalIgnoreCase));
        return mine is not null && mine.FastLapMs == timed.Min(e => e.FastLapMs);
    }

    /// <summary>
    /// How many layouts this driver has improved their own best on at least
    /// once - a race there quicker than every race of theirs there before
    /// it. The first race on a layout sets the mark; it doesn't beat one.
    /// </summary>
    private static int LayoutsImprovedOn(IReadOnlyList<SessionRecord> earlier, SessionRecord current, string transponder) =>
        earlier
            .Append(current)
            .Where(r => !string.IsNullOrWhiteSpace(r.TrackLayoutName))
            .Select(r => (Race: r, Lap: EntryOf(r, transponder)?.FastLapMs ?? 0))
            .Where(x => x.Lap > 0)
            .GroupBy(x => x.Race.TrackLayoutName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Count(layout =>
            {
                var best = 0L;

                foreach (var (_, lap) in layout.OrderBy(x => x.Race.StartedAtUtc))
                {
                    if (best > 0 && lap < best)
                    {
                        return true;
                    }

                    best = best == 0 ? lap : Math.Min(best, lap);
                }

                return false;
            });

    private static string Seconds(long ms) => $"{ms / 1000.0:0.000}";
}
