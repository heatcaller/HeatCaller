namespace HeatCaller.Tests;

/// <summary>
/// RoundPicker - "Start the next race automatically": which round a race run
/// with nothing in the Round box is filed against. The 25 Sep audit's case:
/// a C-Main left unrun last week sorts ahead of everything made since, and
/// with no race day open the next race was filed against it.
/// </summary>
public sealed class RoundPickerTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    private readonly RaceEventStore _events = RaceEventStore.Load();
    private readonly RoundDefinitionStore _rounds = RoundDefinitionStore.Load();
    private readonly List<string> _log = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void With_no_race_day_open_last_week_s_leftover_main_is_not_picked()
    {
        var lastWeek = OpenDay("Last week");
        AddRound("Stock", "C-Main");
        _events.Close(lastWeek.Id);

        var picked = Pick(RoundPicker.ByDrivers, "Stock");

        Assert.Null(picked);
    }

    [Fact]
    public void Tonight_s_heat_is_picked_not_the_leftover_that_sorts_ahead_of_it()
    {
        var lastWeek = OpenDay("Last week");
        AddRound("Stock", "C-Main");
        _events.Close(lastWeek.Id);

        OpenDay("Tonight");
        var q1 = AddRound("Stock", "Q1 Heat 1");

        Assert.Equal(q1.Id, Pick(RoundPicker.ByDrivers, "Stock")?.Id);
    }

    [Fact]
    public void By_drivers_takes_the_class_most_of_them_race_in()
    {
        OpenDay("Tonight");
        AddRound("Mod", "Q1 Heat 1");
        var stock = AddRound("Stock", "Q1 Heat 1");

        // One driver's profile says Mod, most say Stock, one says nothing.
        var picked = Pick(RoundPicker.ByDrivers, "Stock", "Mod", "Stock", null);

        Assert.Equal(stock.Id, picked?.Id);
    }

    [Fact]
    public void By_drivers_with_no_classes_known_falls_back_to_the_next_round_and_says_so()
    {
        OpenDay("Tonight");
        var first = AddRound("Mod", "Q1 Heat 1");
        AddRound("Stock", "Q1 Heat 1");

        var picked = Pick(RoundPicker.ByDrivers, null, " ");

        Assert.Equal(first.Id, picked?.Id);
        Assert.Contains(_log, line => line.Contains("Couldn't tell which class"));
    }

    [Fact]
    public void Pinned_to_a_class_with_nothing_left_picks_nothing_and_says_so()
    {
        OpenDay("Tonight");
        AddRound("Stock", "Q1 Heat 1");

        var picked = RoundPicker.Next(_rounds, _events, RoundPicker.ByClass, "Mod", Array.Empty<string?>(), _log.Add);

        Assert.Null(picked);
        Assert.Contains(_log, line => line.Contains("pinned to class \"Mod\""));
    }

    [Fact]
    public void A_race_saved_with_no_race_day_open_says_why_it_has_no_heat()
    {
        File.WriteAllText(_data.PathOf("race-in-progress.json"), "{}");
        var log = new List<string>();

        RaceFiling.Commit(
            new SessionRecord { Label = "Race", SessionType = RaceSessionType.Race },
            round: null,
            autoAdvanced: false,
            autoAdvanceWanted: true,
            SessionHistoryStore.Load(),
            _rounds,
            new RaceJournal(),
            () => { },
            () => { },
            log.Add,
            raceDayOpen: false);

        Assert.Contains(log, line => line.Contains("no race day is open"));
        Assert.DoesNotContain(log, line => line.Contains("no unrecorded rounds left"));
    }

    // --- Helpers ------------------------------------------------------------------

    private RaceEvent OpenDay(string name)
    {
        var day = new RaceEvent { Name = name };
        _events.Upsert(day);
        _events.SetActive(day.Id);
        return day;
    }

    /// <summary>An unrun round, on the open day if there is one - as the Program page adds it.</summary>
    private RoundDefinition AddRound(string className, string label)
    {
        var round = new RoundDefinition { ClassName = className, RoundLabel = label };
        _rounds.Add(round);
        _events.AddRoundsToActive(new[] { round.Id });
        return round;
    }

    private RoundDefinition? Pick(string scope, params string?[] classesOnTrack) =>
        RoundPicker.Next(_rounds, _events, scope, string.Empty, classesOnTrack, _log.Add);
}
