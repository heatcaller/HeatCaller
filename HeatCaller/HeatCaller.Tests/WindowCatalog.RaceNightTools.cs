namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// Two race-night tools. The live qualifying projection
    /// (QualifyingProjectionForm), three ways: a class of 24 two rounds into
    /// qualifying, the second round's last heat nearly over - a late arrival
    /// racing in it, new to the order, and every fourth driver with the
    /// catalog's longest name; a class ranked on its best two heats of three
    /// consecutive laps, whose name and round run long enough to wrap the
    /// heading, the third round's last heat on track; and nothing running,
    /// the note in the order's place. And Fill in the times (FillTimesForm):
    /// ten o'clock and ten minutes a race, as a new day opens it, and the
    /// widest the two fields show - a minute to midnight and the longest gap.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> RaceNightToolWindows() => new (string, Func<Form>)[]
    {
        ("Qualifying projection", () => RaceNightToolProjection("Mini-Z RWD Stock", qualifyingRounds: 2)),
        ("Qualifying projection (best 2 of 3, consecutive laps)", () => RaceNightToolProjection(
            "Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Masters)",
            qualifyingRounds: 3,
            rules: new RaceClass
            {
                Name = "Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Masters)",
                QualifyingMethod = QualifyingMethod.BestConsecutiveLaps,
                ConsecutiveLapCount = 3,
                CountBestRounds = 2,
            })),
        ("Qualifying projection (nothing running)", () => new QualifyingProjectionForm(new QualifyingProjector(
            new RaceTiming(new RaceControlSession()),
            ProfileStore.Load(),
            RaceClassStore.Load(),
            SessionHistoryStore.FromSessions(new List<SessionRecord>()),
            RoundDefinitionStore.Load(),
            events: null,
            activeRound: () => null))),
        ("Fill in the times", () => new FillTimesForm(TimeSpan.FromHours(10), 10)),
        ("Fill in the times (longest gap)", () => new FillTimesForm(new TimeSpan(23, 59, 0), 240)),
    };

    /// <summary>
    /// The projection with a qualifier on track, for
    /// <paramref name="className"/>: 24 drivers dealt into three heats of
    /// eight in each of <paramref name="qualifyingRounds"/> rounds - from a
    /// different place each round, as a reseed does - and every heat
    /// recorded but the last round's last, which is out now on its last lap,
    /// with a driver who arrived after the heats were drawn racing in it
    /// too. One driver crashed out of the first round after two laps.
    /// <paramref name="rules"/> is the class as the club set it up, when it
    /// is ranked other than on laps.
    ///
    /// The projector reads the rounds and the class as the window is built -
    /// drawing the order is the first thing it does, and it is not asked
    /// again: its timer starts only once the window is shown - so they are on
    /// file then, and taken off again straight after, for a window built
    /// later in the same data folder to find what it would have found. The
    /// recorded heats are never saved (SessionHistoryStore.FromSessions); the
    /// drivers are added to the folder's profiles when they are not there
    /// already, as the other windows over them do.
    /// </summary>
    private static QualifyingProjectionForm RaceNightToolProjection(string className, int qualifyingRounds, RaceClass? rules = null)
    {
        const int heatsPerRound = 3;
        const int perHeat = 8;
        const int inHeats = heatsPerRound * perHeat;

        // The class, and the late arrival after it.
        var roster = Drivers(inHeats + 1);
        var profiles = ProfileStore.Load();
        var missing = roster.Where(driver => profiles.Get(driver.Transponder) is null).ToList();

        if (missing.Count > 0)
        {
            profiles.UpsertMany(missing);
        }

        var recorded = new List<RoundDefinition>();
        var sessions = new List<SessionRecord>();
        var startedAtUtc = new DateTime(2026, 9, 26, 18, 30, 0, DateTimeKind.Utc);

        RoundDefinition? onTrack = null;
        var onTrackField = new List<int>();
        var onTrackHeat = 0;

        for (var round = 1; round <= qualifyingRounds; round++)
        {
            for (var heat = 1; heat <= heatsPerRound; heat++)
            {
                // Places in the roster, and which heat of the night this is
                // - each driver's pace moves a little from heat to heat, so
                // the order changes between rounds.
                var field = Enumerable.Range(0, perHeat)
                    .Select(i => ((heat - 1) * perHeat + i + (round - 1) * 3) % inHeats)
                    .ToList();

                var heatOfNight = (round - 1) * heatsPerRound + heat;

                var definition = new RoundDefinition
                {
                    ClassName = className,
                    RoundLabel = $"Qualifier {round} Heat {heat}",
                    Kind = RoundKind.Qualifier,
                    AssignedTransponders = field.Select(driver => roster[driver].Transponder).ToList(),
                };

                if (round == qualifyingRounds && heat == heatsPerRound)
                {
                    // The late arrival, put in the heat still to run.
                    field.Add(inHeats);
                    definition.AssignedTransponders.Add(roster[inHeats].Transponder);

                    onTrack = definition;
                    onTrackField = field;
                    onTrackHeat = heatOfNight;
                    continue;
                }

                var results = field
                    .Select(driver =>
                    {
                        var laps = RaceNightToolLaps(driver, heatOfNight, untilMs: 300_000, stopAfter: round == 1 && driver == 5 ? 3 : int.MaxValue);

                        return new SessionResultEntry
                        {
                            Transponder = roster[driver].Transponder,
                            DriverName = roster[driver].DriverName,
                            CarModel = roster[driver].CarModel,
                            Laps = laps.Count,
                            ElapsedMs = laps.Sum(),
                            FastLapMs = laps.Count > 1 ? laps.Skip(1).Min() : 0,
                            LapTimesMs = laps,
                        };
                    })
                    .OrderByDescending(result => result.Laps)
                    .ThenBy(result => result.ElapsedMs)
                    .ToList();

                for (var place = 0; place < results.Count; place++)
                {
                    results[place].Position = place + 1;
                }

                var session = new SessionRecord
                {
                    SessionType = RaceSessionType.Race,
                    Label = definition.RoundLabel,
                    RoundId = definition.Id,
                    StartedAtUtc = startedAtUtc,
                    EndedAtUtc = startedAtUtc.AddMinutes(5),
                    Results = results,
                };

                definition.LinkedSessionId = session.Id;

                recorded.Add(definition);
                sessions.Add(session);

                startedAtUtc = startedAtUtc.AddMinutes(12);
            }
        }

        // The heat on track, its crossings in the order they came - as
        // ghosts' passes are fed in, timed from the green, so no decoder
        // clock is needed.
        var race = new RaceControlSession();
        race.Stage();
        race.GreenFlag(0);

        var passes = new List<(long At, string Transponder)>();

        foreach (var driver in onTrackField)
        {
            var at = 0L;

            foreach (var lap in RaceNightToolLaps(driver, onTrackHeat, untilMs: 295_000))
            {
                at += lap;
                passes.Add((at, roster[driver].Transponder));
            }
        }

        foreach (var (at, transponder) in passes.OrderBy(pass => pass.At))
        {
            race.ProcessLapEvent(new DirectLapEvent
            {
                TransponderId = transponder,
                DeviceTimeMs = (uint)at,
                SignalHits = 1,
                SignalStrength = 100,
                IsSynthetic = true,
            });
        }

        var rounds = RoundDefinitionStore.Load();
        var classes = RaceClassStore.Load();

        try
        {
            foreach (var definition in recorded)
            {
                rounds.Add(definition);
            }

            if (rules is not null)
            {
                classes.Upsert(rules);
            }

            return new QualifyingProjectionForm(new QualifyingProjector(
                new RaceTiming(race),
                profiles,
                classes,
                SessionHistoryStore.FromSessions(sessions),
                rounds,
                events: null,
                activeRound: () => onTrack));
        }
        finally
        {
            foreach (var definition in recorded)
            {
                rounds.Delete(definition.Id);
            }

            if (rules is not null)
            {
                classes.Delete(rules.Id);
            }
        }
    }

    /// <summary>
    /// One driver's crossings in one heat, as a result keeps them: the run
    /// from the grid to the loop first, then laps at the driver's pace for
    /// that heat of the night, each a little different, until
    /// <paramref name="untilMs"/> has gone - or <paramref name="stopAfter"/>
    /// crossings, for a driver who crashed out. The track gets quicker as
    /// the night goes on, as it rubbers in, so the heat on track moves some
    /// of its drivers up the order and the rest down.
    /// </summary>
    private static List<long> RaceNightToolLaps(int driver, int heatOfNight, long untilMs, int stopAfter = int.MaxValue)
    {
        var laps = new List<long> { 1_400 + driver % 4 * 150 };
        var total = laps[0];
        var pace = 11_900 - heatOfNight * 60 + (driver * 53 + heatOfNight * 211) % 900;

        while (total < untilMs && laps.Count < stopAfter)
        {
            var lap = pace + (laps.Count * 37 + driver * 11) % 400 - 200;

            laps.Add(lap);
            total += lap;
        }

        return laps;
    }
}
