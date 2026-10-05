namespace HeatCaller.Tests;

/// <summary>
/// A transponder changing hands after it has raced as a car of its own: the
/// races saved under its own id are found, so the operator can be told they
/// will count for the new owner before the transfer goes ahead.
/// </summary>
public sealed class BugHuntPointsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static SessionRecord Race(string label, DateTime startedUtc, params string[] transponders) => new()
    {
        SessionType = RaceSessionType.Race,
        Label = label,
        StartedAtUtc = startedUtc,
        Results = transponders.Select((t, i) => new SessionResultEntry { Position = i + 1, Transponder = t, DriverName = "D" + t }).ToList(),
    };

    [Fact]
    public void Races_where_a_spare_was_timed_as_its_own_car_are_found_oldest_first()
    {
        var now = DateTime.UtcNow;

        // Heat 2: both of Maya's cars out, so her spare 200 had a row of its
        // own. Heat 1: it was merged into her main 100, so only 100 is there.
        var heat1 = Race("Heat 1", now.AddHours(-2), "100", "500");
        var heat2 = Race("Heat 2", now.AddHours(-1), "100", " 200 ", "500");
        var heat3 = Race("Heat 3", now.AddHours(-3), "abc200");

        var races = TransponderTransferring.RacesFiledUnder("200", new[] { heat1, heat2, heat3 });

        Assert.Equal(new[] { "Heat 2" }, races.Select(r => r.Label));
    }

    [Fact]
    public void A_transponder_never_timed_on_its_own_has_no_races_and_a_blank_id_matches_nothing()
    {
        var heat = Race("Heat 1", DateTime.UtcNow, "100", "");

        Assert.Empty(TransponderTransferring.RacesFiledUnder("200", new[] { heat }));
        Assert.Empty(TransponderTransferring.RacesFiledUnder("  ", new[] { heat }));
    }

    [Fact]
    public void Matching_ignores_case_as_the_decoder_and_typed_ids_differ()
    {
        var older = Race("Heat 1", DateTime.UtcNow.AddHours(-1), "a1b2");
        var newer = Race("Heat 2", DateTime.UtcNow, "A1B2");

        var races = TransponderTransferring.RacesFiledUnder("A1b2", new[] { newer, older });

        Assert.Equal(new[] { "Heat 1", "Heat 2" }, races.Select(r => r.Label));
    }

    // ------------------------------------------------------------------
    //  The question before the transfer, and that it is reached
    // ------------------------------------------------------------------

    [Fact]
    public void No_races_of_its_own_means_nothing_to_ask()
    {
        Assert.Null(TransponderTransferring.AskFor("200", "Maya", "Sam", Array.Empty<SessionRecord>()));
    }

    [Fact]
    public void One_race_is_named_and_said_to_count_for_the_new_owner()
    {
        var heat = Race("Heat 2", DateTime.UtcNow, "100", "200");

        var ask = TransponderTransferring.AskFor("200", "Maya", "Sam", new[] { heat });

        Assert.NotNull(ask);
        Assert.Contains("in 1 race already run, so that race is saved under 200 itself rather than Maya's main transponder", ask);
        Assert.Contains("  Heat 2 - ", ask);
        Assert.Contains("Once 200 is Sam's, qualifying and the mains will count it for Sam, not Maya.", ask);
        Assert.EndsWith("Transfer it anyway?", ask);
    }

    [Fact]
    public void Past_eight_races_the_rest_are_counted_not_listed()
    {
        var start = DateTime.UtcNow.AddDays(-30);
        var races = Enumerable.Range(1, 12).Select(i => Race($"Heat {i}", start.AddDays(i), "200")).ToList();

        var ask = TransponderTransferring.AskFor("200", "Maya", "Sam", races)!;

        Assert.Contains("in 12 races already run, so those races are", ask);
        Assert.Contains("  Heat 8 - ", ask);
        Assert.DoesNotContain("  Heat 9 - ", ask);
        Assert.Contains("...and 4 more.", ask);
        Assert.Contains("will count them for Sam", ask);
    }

    /// <summary>
    /// The question was written on 4 Oct but never asked: Run took the race
    /// history as an optional last parameter, and Transfer on the
    /// Transponders tab - its only caller - left it out. Both ends now have
    /// to be handed it, so leaving it out is a build error, not a silent
    /// skip.
    /// </summary>
    [Fact]
    public void Transfer_cannot_be_called_without_saying_where_the_race_history_is()
    {
        var run = typeof(TransponderTransferring).GetMethod(nameof(TransponderTransferring.Run))!;
        var history = Assert.Single(run.GetParameters(), p => p.ParameterType == typeof(SessionHistoryStore));
        Assert.False(history.HasDefaultValue);

        var panel = Assert.Single(typeof(DriverTransponderPanel).GetConstructors());
        Assert.Contains(panel.GetParameters(), p => p.ParameterType == typeof(SessionHistoryStore));
    }
}
