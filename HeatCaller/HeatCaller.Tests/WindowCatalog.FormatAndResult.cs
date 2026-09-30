namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// A race format (RaceFormatForm) in each way it is opened: the app
    /// default from Race Control's Next Up bar, with no "use the default"
    /// tick; a round's own format with the default to fall back to, every
    /// number at its largest; a class following the default, from Race
    /// Control and the setup wizard with the default's numbers and from
    /// Classes without them; and a class's format still set to the hand
    /// release no longer offered, which puts an extra start type in its list.
    /// And a saved race's result (ResultEditForm): an A-main of ten as the
    /// race filed it, and again with penalties, a DNF and a DQ already on it.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> FormatAndResultWindows()
    {
        // Every number at the most the dialog allows, a staggered start with
        // its gap and clock, and the longest warm-up that is not a whole
        // number of minutes - the widest each field has to be, and, as the
        // default being followed, the longest sentence about it.
        static RaceFormat Largest() => new()
        {
            LapLimit = 9999,
            TimeLimitMinutes = 180,
            MinLapTimeMs = 60000,
            StreakWindowPercent = 200,
            StreakFasterBreaks = false,
            StartType = RaceStartType.StaggeredReverse,
            StaggerGapSeconds = 120,
            StaggerClockMode = StaggerClock.SharedGreen,
            AutoAdvance = true,
            StagingSeconds = 300,
            CountdownSeconds = 60,
            WarmUpSeconds = RaceFormat.MaxWarmUpSeconds - 30,
            FinishOnNextCrossing = false,
        };

        // A finished A-main of ten: the run from the grid to the first
        // crossing, then laps a little slower the further down the field,
        // two drivers on each lap count - filed as Race Control files it.
        // Corrected, it has what the real editor makes of a time penalty, a
        // phantom crossing taken off, a lap penalty, a DNF and a DQ.
        static SessionRecord AMain(bool corrected)
        {
            var drivers = Drivers(10);
            var started = new DateTime(2026, 9, 26, 20, 40, 0, DateTimeKind.Utc);

            var session = new SessionRecord
            {
                SessionType = RaceSessionType.Race,
                Label = "Stock Buggy - A-Main",
                TrackLayoutName = "Club Oval",
                StartedAtUtc = started,
                EndedAtUtc = started.AddMinutes(8).AddSeconds(30),
                RecordsIncidents = true,
            };

            for (var i = 0; i < drivers.Count; i++)
            {
                var laps = 40 - i / 2;
                var lapTimes = new List<long> { 1_600 + i * 40 };

                lapTimes.AddRange(Enumerable.Range(1, laps - 1).Select(lap => 11_800L + i * 95 + lap % 4 * 70));

                session.Results.Add(new SessionResultEntry
                {
                    Position = i + 1,
                    Transponder = drivers[i].Transponder,
                    DriverName = drivers[i].DriverName,
                    CarModel = drivers[i].CarModel,
                    Laps = laps,
                    FastLapMs = lapTimes.Skip(1).Min(),
                    ElapsedMs = lapTimes.Sum(),
                    Status = "active",
                    LapTimesMs = lapTimes,
                    StartPosition = i + 1,
                });
            }

            if (corrected)
            {
                ResultEditor.Apply(
                    session,
                    new List<ResultAdjustment>
                    {
                        new ResultAdjustment(drivers[0].Transponder, AddedLaps: 0, TimePenaltyMs: 5_000, LapPenalty: 0, Outcome: string.Empty),
                        new ResultAdjustment(drivers[3].Transponder, AddedLaps: -1, TimePenaltyMs: 0, LapPenalty: 0, Outcome: string.Empty),
                        new ResultAdjustment(drivers[5].Transponder, AddedLaps: 0, TimePenaltyMs: 0, LapPenalty: 1, Outcome: string.Empty),
                        new ResultAdjustment(drivers[6].Transponder, AddedLaps: 0, TimePenaltyMs: 0, LapPenalty: 0, Outcome: ResultEditor.Dnf),
                        new ResultAdjustment(drivers[8].Transponder, AddedLaps: 0, TimePenaltyMs: 0, LapPenalty: 0, Outcome: ResultEditor.Dq),
                    },
                    "Jump start, a double read on lap 12, a cut corner and a car that never came back from the pits",
                    "Laptop");
            }

            return session;
        }

        return new (string, Func<Form>)[]
        {
            ("Race Format", () => new RaceFormatForm(
                RaceFormat.FromSettings(new AppSettings()), "App Default Race Format", allowUseDefault: false)),
            ("Race Format (a round's own, largest numbers)", () => new RaceFormatForm(
                Largest(), "Race Format - Qualifier 1 Heat 2 (round override)", allowUseDefault: true,
                fallback: RaceFormat.FromSettings(new AppSettings()))),
            ("Race Format (following the default)", () => new RaceFormatForm(
                null, "Race Format - Stock Buggy", allowUseDefault: true, fallback: Largest())),
            ("Race Format (following the default, no numbers)", () => new RaceFormatForm(
                null, "Race Format - Stock Buggy", allowUseDefault: true)),
            ("Race Format (hand released)", () => new RaceFormatForm(
                new RaceFormat { StartType = RaceStartType.HandReleased, TimeLimitMinutes = 5 },
                "Qualifier Format - Stock Buggy", allowUseDefault: true)),
            ("Edit Result", () => new ResultEditForm(AMain(corrected: false))),
            ("Edit Result (already corrected)", () => new ResultEditForm(AMain(corrected: true))),
        };
    }
}
