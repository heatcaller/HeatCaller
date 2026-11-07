using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// TAP A RACE FOR ITS GRID (6 Nov 2026, Asher): the online schedule's
/// programme carries each heat's grid - slot, name and car - so a race
/// opened on the relay's running order shows who starts where and in what.
/// </summary>
public sealed class RelayScheduleGridTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_heats_grid_goes_online_in_heat_sheet_order_with_each_car()
    {
        var events = RaceEventStore.Load();
        var rounds = RoundDefinitionStore.Load();
        var schedule = DayScheduleStore.Load();
        var profiles = ProfileStore.Load();
        EventSpine.Bind(events, CheckInStore.Load(), TechInspectionStore.Load(), schedule, rounds);

        var day = new RaceEvent { Name = "Tonight" };
        events.Upsert(day);
        events.SetActive(day.Id);

        profiles.Upsert(new DriverProfile { Transponder = "1001", DriverName = "Sam Smith", CarModel = "Associated B7" });
        profiles.Upsert(new DriverProfile { Transponder = "1002" });
        profiles.Upsert(new DriverProfile { Transponder = "1003", DriverName = "Bill Travers" });

        var heat = new RoundDefinition
        {
            ClassName = "Stock",
            RoundLabel = "Q1 Heat 1",
            Kind = RoundKind.Qualifier,
            AssignedTransponders = new List<string> { "1001", "1002", "1003" },
        };
        rounds.Add(heat);
        events.AddRoundsToActive(new[] { heat.Id });
        schedule.SyncToProgram(EventSpine.Programme(events, rounds)
            .Select(r => new DayScheduleStore.ProgramLine(r.Id, r.ClassName, r.RoundLabel))
            .ToList());

        using var doc = JsonDocument.Parse(RelayDocuments.Programme(schedule, rounds, events, profiles, lineups: true, abbreviate: false));
        var grid = doc.RootElement.GetProperty("lines")[0].GetProperty("grid").EnumerateArray().ToList();

        // The unnamed driver is left out but keeps slot 2, as on the sheet.
        Assert.Equal(2, grid.Count);
        Assert.Equal(1, grid[0].GetProperty("slot").GetInt32());
        Assert.Equal("Sam Smith", grid[0].GetProperty("name").GetString());
        Assert.Equal("Associated B7", grid[0].GetProperty("car").GetString());
        Assert.Equal(3, grid[1].GetProperty("slot").GetInt32());
        Assert.Equal("Bill Travers", grid[1].GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, grid[1].GetProperty("car").ValueKind);

        // With line-ups off there is no grid either.
        using var off = JsonDocument.Parse(RelayDocuments.Programme(schedule, rounds, events, profiles, lineups: false, abbreviate: false));
        Assert.Equal(JsonValueKind.Null, off.RootElement.GetProperty("lines")[0].GetProperty("grid").ValueKind);
    }
}
