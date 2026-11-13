using System.Collections;
using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, the winner at the flag.
///
/// A shared heat's class winners were built only with the Winner named call,
/// so a finish line that named the winner itself ("Checkered flag - {winner}
/// takes it!") left them unsaid - the Stock driver who won their class in
/// fourth overall was never named.
///
/// And a qualifier scored by best consecutive laps named the car with the
/// most laps as the winner, though the heat's top qualifier - the one called
/// on provisional pole - is whoever has the quickest run.
/// </summary>
public sealed class BugHunt17_AnnouncementService_WinnerTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly Dictionary<string, string> ModAndStock = new(StringComparer.OrdinalIgnoreCase)
    {
        ["201"] = "Mod",
        ["202"] = "Stock",
        ["203"] = "Stock",
    };

    /// <summary>An announcer at the flag of a three-car race Mo won, with the queue held as the finish call holds it.</summary>
    private static AnnouncementService AtTheFlag(IReadOnlyDictionary<string, string>? classes, bool winnerRowOn = true)
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "201", DriverName = "Mo" });
        profiles.Upsert(new DriverProfile { Transponder = "202", DriverName = "Sam" });
        profiles.Upsert(new DriverProfile { Transponder = "203", DriverName = "Bea" });

        var voice = VoiceSettingsStore.Load();
        voice.Current.Events[nameof(AnnouncementEventType.RaceCompleted)].Enabled = true;
        voice.Current.Events[nameof(AnnouncementEventType.WinnerCalled)].Enabled = winnerRowOn;

        var announcer = new AnnouncementService(profiles, voice, SoundEffectsStore.Load())
        {
            SharedHeatClasses = () => classes,
        };

        var standings = (Dictionary<string, RaceStanding>)Field("_standings").GetValue(announcer)!;
        standings["201"] = new RaceStanding { Transponder = "201", Name = "Mo", Laps = 10, ElapsedMs = 80_000 };
        standings["202"] = new RaceStanding { Transponder = "202", Name = "Sam", Laps = 10, ElapsedMs = 82_000 };
        standings["203"] = new RaceStanding { Transponder = "203", Name = "Bea", Laps = 9, ElapsedMs = 81_000 };
        Field("_currentLeaderTransponder").SetValue(announcer, "201");

        // Nothing is taken from the queue while the finish call's hold is up,
        // so what was queued can be read back.
        Method("BeginFinishCallHold").Invoke(announcer, null);

        return announcer;
    }

    private static FieldInfo Field(string name) => typeof(AnnouncementService).GetField(name, Private)!;

    private static MethodInfo Method(string name) => typeof(AnnouncementService).GetMethod(name, Private)!;

    /// <summary>Queues the winner's call as NotifyRaceCompleted does, and hands back what is in the queue, front first, as (on air, words).</summary>
    private static List<(string? OnAir, string? Text)> QueuedAtTheFlag(AnnouncementService announcer)
    {
        Method("QueueTheWinnersCall").Invoke(announcer, null);

        var queue = (IList)Field("_voiceQueue").GetValue(announcer)!;
        var queued = new List<(string?, string?)>();

        lock (Field("_voiceQueueLock").GetValue(announcer)!)
        {
            foreach (var item in queue)
            {
                var type = item!.GetType();

                queued.Add((
                    (string?)type.GetProperty("OnAir")!.GetValue(item),
                    (string?)type.GetProperty("ShownText")!.GetValue(item)));
            }

            // Read, not said: nothing here should reach a speaker.
            queue.Clear();
        }

        return queued;
    }

    // ------------------------------------------------------------ class winners

    [Fact]
    public void A_shared_heat_names_its_class_winners_after_the_winners_call()
    {
        var queued = QueuedAtTheFlag(AtTheFlag(ModAndStock));

        Assert.Equal(new[] { "Winner", "Class winners" }, queued.Select(q => q.OnAir));
        Assert.Contains("wins Mod", queued[1].Text);
        Assert.Contains("wins Stock", queued[1].Text);
    }

    [Fact]
    public void A_finish_that_named_the_winner_itself_still_names_the_class_winners()
    {
        var announcer = AtTheFlag(ModAndStock);
        Field("_finishNamedTheWinner").SetValue(announcer, true);

        var queued = QueuedAtTheFlag(announcer);

        // No Winner named call - the finish said the name - but the class winners, first.
        var only = Assert.Single(queued);
        Assert.Equal("Class winners", only.OnAir);
        Assert.Contains("wins Mod", only.Text);
        Assert.Contains("wins Stock", only.Text);
        Assert.False((bool)Field("_finishNamedTheWinner").GetValue(announcer)!);
    }

    [Fact]
    public void A_finish_that_named_the_winner_names_the_class_winners_with_the_Winner_named_row_off()
    {
        var announcer = AtTheFlag(ModAndStock, winnerRowOn: false);
        Field("_finishNamedTheWinner").SetValue(announcer, true);

        Assert.Equal("Class winners", Assert.Single(QueuedAtTheFlag(announcer)).OnAir);
    }

    [Fact]
    public void Nobody_is_named_at_the_flag_when_the_club_names_no_winner()
    {
        Assert.Empty(QueuedAtTheFlag(AtTheFlag(ModAndStock, winnerRowOn: false)));
    }

    [Fact]
    public void A_one_class_race_has_the_winners_call_alone()
    {
        Assert.Equal("Winner", Assert.Single(QueuedAtTheFlag(AtTheFlag(null))).OnAir);

        var announcer = AtTheFlag(null);
        Field("_finishNamedTheWinner").SetValue(announcer, true);

        Assert.Empty(QueuedAtTheFlag(announcer));
    }

    // ------------------------------------------------- best-consecutive qualifier

    [Theory]
    [InlineData(true, QualifyingMethod.BestConsecutiveLaps, true)]
    [InlineData(true, QualifyingMethod.TotalLaps, false)]
    [InlineData(false, QualifyingMethod.BestConsecutiveLaps, false)]
    public void Only_a_qualifier_scored_by_best_consecutive_laps_has_no_winner_to_name(bool qualifying, QualifyingMethod method, bool nobody)
    {
        Assert.Equal(nobody, AnnouncementService.LeaderIsNotTheTopQualifier(qualifying, method));
    }

    [Fact]
    public void A_best_consecutive_qualifier_names_no_winner_at_the_flag()
    {
        var announcer = AtTheFlag(ModAndStock);
        announcer.QualifyingModeProvider = () => true;
        announcer.SetQualifyingRule(QualifyingMethod.BestConsecutiveLaps, 3);

        Assert.Empty(QueuedAtTheFlag(announcer));

        // Nor does the finish call itself, however its lines are written.
        var finish = new AnnouncementEventConfig { Enabled = true, Templates = new List<string> { "Checkered flag - {winner} takes it!" } };

        Assert.False((bool)Method("TrySpeakTheFinishWithTheWinner").Invoke(announcer, new object[] { finish })!);
        Assert.False((bool)Field("_finishNamedTheWinner").GetValue(announcer)!);
    }

    [Fact]
    public void A_most_laps_qualifier_still_names_the_heats_winner()
    {
        var announcer = AtTheFlag(null);
        announcer.QualifyingModeProvider = () => true;
        announcer.SetQualifyingRule(QualifyingMethod.TotalLaps, 3);

        Assert.Equal("Winner", Assert.Single(QueuedAtTheFlag(announcer)).OnAir);
    }
}
