namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): the Invitational's call and recap fact named
/// how many mains were filed, not which main was just run.
/// </summary>
public sealed class BugHunt24_InvitationalTests
{
    [Fact]
    public void A_skipped_main_is_called_by_its_own_number()
    {
        // Five mains: main 1 filed, main 2 never run, main 3 just finished.
        var series = Invitational.BuildMains("Stock", new[] { "a", "b", "c" }, 5, "Club");
        series[0].LinkedSessionId = "m1";
        var sessions = new Dictionary<string, SessionRecord> { ["m1"] = Finish("a", "b", "c") };

        var table = Invitational.Standings(series, id => sessions.GetValueOrDefault(id), new RaceClass(),
            justRun: (series[2], Finish("a", "c", "b")))!;

        Assert.Equal(2, table.MainsRun);
        Assert.Equal(3, table.MainNumber);
        Assert.StartsWith("After main 3 of 5,", Invitational.Call(table, _ => "Al"));
        Assert.StartsWith("This was main 3 of 5 ", Invitational.Fact(table, s => s.Transponder));
    }

    [Fact]
    public void A_re_run_earlier_main_is_called_by_its_own_number()
    {
        // Mains 1 and 3 filed, main 2 run late: the call is after main 2.
        var series = Invitational.BuildMains("Stock", new[] { "a", "b" }, 5, "Club");
        series[0].LinkedSessionId = "m1";
        series[2].LinkedSessionId = "m3";
        var sessions = new Dictionary<string, SessionRecord> { ["m1"] = Finish("a", "b"), ["m3"] = Finish("b", "a") };

        var table = Invitational.Standings(series, id => sessions.GetValueOrDefault(id), new RaceClass(),
            justRun: (series[1], Finish("a", "b")))!;

        Assert.Equal(2, table.MainNumber);
        Assert.StartsWith("After main 2 of 5,", Invitational.Call(table, _ => "Al"));
    }

    [Fact]
    public void Without_a_main_just_run_the_last_filed_main_is_named()
    {
        var series = Invitational.BuildMains("Stock", new[] { "a", "b" }, 5, "Club");
        series[0].LinkedSessionId = "m1";
        series[3].LinkedSessionId = "m4";
        var sessions = new Dictionary<string, SessionRecord> { ["m1"] = Finish("a", "b"), ["m4"] = Finish("a", "b") };

        var table = Invitational.Standings(series, id => sessions.GetValueOrDefault(id), new RaceClass())!;

        Assert.StartsWith("After main 4 of 5,", Invitational.Call(table, _ => "Al"));
    }

    private static SessionRecord Finish(params string[] order) => new()
    {
        SessionType = RaceSessionType.Race,
        Results = order.Select((t, i) => new SessionResultEntry
        {
            Position = i + 1,
            Transponder = t,
            DriverName = t,
            Laps = 20 - i,
            ElapsedMs = 300_000,
        }).ToList(),
    };
}
