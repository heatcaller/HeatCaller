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
}
