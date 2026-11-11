using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// ESTIMATED START TIMES (11 Nov 2026): NightPlan's start for each round
/// still to run, as "about 20:42" on the heat sheet and the phones' Schedule.
/// </summary>
public sealed class EstimatedStartsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly DateTime T0 = new(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void About_rounds_to_the_nearest_minute_in_local_time()
    {
        var expected = "about " + AppTime.TimeOfDay(T0.AddMinutes(42).ToLocalTime());

        Assert.Equal(expected, NightPlan.About(T0.AddMinutes(41).AddSeconds(40)));
        Assert.Equal(expected, NightPlan.About(T0.AddMinutes(42).AddSeconds(20)));
    }

    [Fact]
    public void Each_round_still_to_run_has_its_start_and_a_run_round_has_none()
    {
        var run = new RoundDefinition { Kind = RoundKind.Qualifier, RoundLabel = "Q1 Heat 1", LinkedSessionId = "s1" };
        var next = new RoundDefinition { Kind = RoundKind.Qualifier, RoundLabel = "Q1 Heat 2" };
        var after = new RoundDefinition { Kind = RoundKind.Qualifier, RoundLabel = "Q1 Heat 3" };

        var plan = NightPlan.For(new[] { run, next, after }, _ => TimeSpan.FromMinutes(5), null, null, null, null, null, T0);
        var about = NightPlan.AboutOf(plan);

        Assert.Null(about(run.Id));
        Assert.Equal(NightPlan.About(T0), about(next.Id));
        Assert.Equal(NightPlan.About(T0.AddMinutes(5) + NightPlan.DefaultGap), about(after.Id));
        Assert.Null(NightPlan.AboutOf(null)(next.Id));
    }

    [Fact]
    public void The_heat_sheet_puts_the_time_after_the_round_title()
    {
        Assert.Equal("   -   about 20:42", HeatSheetRenderer.RoundTitleTime("about 20:42"));
        Assert.Equal(string.Empty, HeatSheetRenderer.RoundTitleTime(null));
    }

    [Fact]
    public void The_heat_sheet_still_renders_with_the_times()
    {
        var round = new RoundDefinition { ClassName = "Stock", RoundLabel = "Q1 Heat 1", AssignedTransponders = new List<string> { "1001" } };

        var pages = HeatSheetRenderer.RenderPages("Stock", new[] { round }, ProfileStore.Load(), Color.Red, _ => "about 20:42");

        Assert.Single(pages);
        pages[0].Dispose();
    }

    [Fact]
    public void The_online_schedule_carries_each_races_estimate()
    {
        var events = RaceEventStore.Load();
        var rounds = RoundDefinitionStore.Load();
        var schedule = DayScheduleStore.Load();
        var profiles = ProfileStore.Load();
        EventSpine.Bind(events, CheckInStore.Load(), TechInspectionStore.Load(), schedule, rounds);

        var day = new RaceEvent { Name = "Tonight" };
        events.Upsert(day);
        events.SetActive(day.Id);

        var heat = new RoundDefinition { ClassName = "Stock", RoundLabel = "Q1 Heat 1", Kind = RoundKind.Qualifier };
        rounds.Add(heat);
        events.AddRoundsToActive(new[] { heat.Id });
        schedule.SyncToProgram(EventSpine.Programme(events, rounds)
            .Select(r => new DayScheduleStore.ProgramLine(r.Id, r.ClassName, r.RoundLabel))
            .ToList());

        using var doc = JsonDocument.Parse(RelayDocuments.Programme(schedule, rounds, events, profiles, lineups: false, abbreviate: false,
            estimateOf: id => id == heat.Id ? "about 20:42" : null));
        Assert.Equal("about 20:42", doc.RootElement.GetProperty("lines")[0].GetProperty("estimate").GetString());

        // From an older caller, nothing.
        using var none = JsonDocument.Parse(RelayDocuments.Programme(schedule, rounds, events, profiles, lineups: false, abbreviate: false));
        Assert.Equal(JsonValueKind.Null, none.RootElement.GetProperty("lines")[0].GetProperty("estimate").ValueKind);
    }
}
