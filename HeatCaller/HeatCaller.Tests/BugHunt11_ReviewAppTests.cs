using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Review of the bug hunt 11 fixes (5 Nov 2026):
///  - a disqualified car's fast lap no longer stops the legal driver's lap
///    counting as the race's record lap for Track Record Holder;
///  - a countdown pressed straight from Finished keeps showing the finished
///    race's rows in the car they were heard as, while still forgetting the
///    decisions the new race is filed by;
///  - a declined race the journal could not set aside is not called "the
///    race journal that could not be read at startup" when it is kept later.
/// </summary>
public sealed class BugHunt11_ReviewAppTests : IDisposable
{
    private readonly TempAppData _data = new();

    private static readonly FieldInfo KeepUnread =
        typeof(RaceJournal).GetField("_keepUnreadJournal", BindingFlags.Static | BindingFlags.NonPublic)!;

    public void Dispose()
    {
        // The journal's flag is static - never let one test's setting reach the next.
        KeepUnread.SetValue(null, false);
        _data.Dispose();
    }

    // --- Track Record Holder and a disqualified car ----------------------------

    [Fact]
    public void A_disqualified_cars_quicker_lap_does_not_stop_the_legal_record_lap_earning_Track_Record_Holder()
    {
        // The record before tonight: 10.000 on the Infield.
        var lastWeek = Race(Entry("EVE", 1, 10_000));
        lastWeek.StartedAtUtc = DateTime.UtcNow.AddDays(-7);
        lastWeek.EndedAtUtc = lastWeek.StartedAtUtc.AddMinutes(6);

        // Tonight Sam goes under it with 9.800; Jen ran 9.700 and was black-flagged.
        var jen = Entry("JEN", 3, 9_700);
        jen.Outcome = ResultEditor.Dq;
        jen.Laps = 0;

        var race = Race(Entry("SAM", 1, 9_800), Entry("DAVE", 2, 9_900), jen);

        var unlocks = AchievementEvaluator.EvaluateFinishedRace(race, new[] { lastWeek }, AchievementStore.Load());

        Assert.Contains(unlocks, u => u.Transponder == "SAM" && u.Badge.Id == AchievementCatalogue.TrackRecordHolder);
        Assert.DoesNotContain(unlocks, u => u.Transponder == "JEN" && u.Badge.Id == AchievementCatalogue.TrackRecordHolder);
    }

    // --- Who a finished race's rows were heard as, during the next countdown ---

    [Fact]
    public void A_countdown_from_finished_keeps_showing_the_finished_race_in_the_car_it_was_heard_as()
    {
        // Maya owns 100, with 200 as her spare.
        var race = new RaceControlSession
        {
            AlternateTransponderOwner = id => string.Equals(id, "200", StringComparison.OrdinalIgnoreCase) ? "100" : null,
        };

        // The last heat, run on her spare - filed under her main transponder.
        race.Stage();
        race.StartCountdown();
        race.GreenFlag(10_000);
        Seen(race, "200", 20_000);
        Seen(race, "200", 30_000);
        race.CheckeredFlag();
        Assert.Equal("200", race.HeardAs("100"));

        // Countdown pressed straight from Finished: the heat is still on the
        // board, and still in the spare.
        race.StartCountdown();
        Assert.Equal("200", race.HeardAs("100"));

        // The green clears the board, and who it was heard as with it.
        race.GreenFlag(100_000);
        Assert.Equal("100", race.HeardAs("100"));
    }

    // --- The journal's wording for a declined race -----------------------------

    [Fact]
    public void A_declined_race_kept_later_is_not_called_unreadable_at_startup()
    {
        var journal = _data.PathOf("race-in-progress.json");
        Directory.CreateDirectory(Path.GetDirectoryName(journal)!);
        File.WriteAllText(journal, "the crashed heat");

        using (new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(RaceJournal.Discard());
        }

        var said = new List<string>();
        var next = new RaceJournal();
        next.StatusChanged += said.Add;

        next.Clear();

        var message = Assert.Single(said);
        Assert.StartsWith("The unfinished race that was not restored was kept as", message, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be read at startup", message, StringComparison.Ordinal);
    }

    // --- Helpers ---------------------------------------------------------------

    private static void Seen(RaceControlSession race, string car, uint deviceTimeMs) =>
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = car,
            DeviceTimeMs = deviceTimeMs,
            SignalHits = 60,
            SignalStrength = 247,
        });

    private static SessionRecord Race(params SessionResultEntry[] results) => new()
    {
        Label = "A-Main",
        SessionType = RaceSessionType.Race,
        TrackLayoutName = "Infield",
        StartedAtUtc = DateTime.UtcNow.AddMinutes(-6),
        EndedAtUtc = DateTime.UtcNow,
        Results = results.ToList(),
    };

    private static SessionResultEntry Entry(string transponder, int position, long fast) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Position = position,
        StartPosition = position,
        Laps = 12,
        ElapsedMs = 300_000 + position * 1_000,
        FastLapMs = fast,
        LapTimesMs = Enumerable.Repeat(fast + 500, 11).Append(fast).Prepend(3_000L).ToList(),
    };
}
