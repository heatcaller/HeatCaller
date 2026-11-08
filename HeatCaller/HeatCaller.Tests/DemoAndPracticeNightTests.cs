using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// 3 Nov 2026 (asked for by Asher: "yes, marked as demo, and do the same for
/// practice. make a tick mark to remove both easily"): a driver's demo races
/// and practice runs go into their night's race list, marked with `kind`, so
/// the lap stats can be looked at after a rehearsal - counted for nothing
/// (no rating, no place for practice) and never instead of a real race.
/// </summary>
public sealed class DemoAndPracticeNightTests
{
    private const string Sam = "3012345";

    private static readonly DateTime Tonight = new(2026, 10, 22, 21, 0, 0, DateTimeKind.Local);
    private static readonly DateTime At = new(2026, 10, 22, 21, 30, 0, DateTimeKind.Utc);
    private static readonly long[] Laps = { 1400, 9000, 8800, 8900 };

    [Fact]
    public void Demo_and_practice_go_in_marked_in_running_order_after_nothing_changes_for_races()
    {
        var heat = Race("Heat 1", Tonight.AddHours(-2), Row(Sam, 2), Row("3099999", 1));
        var demo = Race("[DEMO] Heat 2", Tonight.AddHours(-1), Row(Sam, 1), Row("DEMO01", 2));
        demo.IsDemo = true;
        var practice = Race("", Tonight.AddHours(-3), Row(Sam, 1));
        practice.SessionType = RaceSessionType.Practice;

        var races = DriverNightExtras.Races(Sam, new[] { heat }, null, new[] { demo, practice });

        Assert.Equal(new[] { "Practice", "Heat 1", "Heat 2" }, races.Select(r => r.Label));
        Assert.Equal(new string?[] { "practice", null, "demo" }, races.Select(r => r.Kind));

        var run = races[0];
        Assert.Equal(0, run.Pos);
        Assert.Null(run.PosAt);
        Assert.Null(run.Rating);
        Assert.Equal(new long[] { 9000, 8800, 8900 }, run.LapsMs!);

        var rehearsal = races[2];
        Assert.Equal(1, rehearsal.Pos);
        Assert.Null(rehearsal.Rating);
        Assert.NotNull(races[1].Rating);

        // Without runs handed in, a night is the races it always was.
        Assert.Equal(new[] { "Heat 1" }, DriverNightExtras.Races(Sam, new[] { heat }, null).Select(r => r.Label));
    }

    [Fact]
    public void An_imported_result_never_goes_and_a_race_handed_in_twice_goes_once()
    {
        var heat = Race("Heat 1", Tonight.AddHours(-2), Row(Sam, 2));
        var imported = Race("Old main", Tonight.AddHours(-1), Row(Sam, 1));
        imported.IsDemo = true;
        imported.ImportedFrom = "LiveRC";

        var races = DriverNightExtras.Races(Sam, new[] { heat }, null, new[] { heat, imported });

        var only = Assert.Single(races);
        Assert.Null(only.Kind);
    }

    [Fact]
    public void A_night_of_only_demo_and_practice_still_has_a_document_with_one_plain_line()
    {
        var demo = Race("[DEMO] Heat", Tonight.AddHours(-1), Row(Sam, 1));
        demo.IsDemo = true;

        var parts = new NightParts("Sam Parker", Tonight, null, Facts(), At)
        {
            Races = DriverNightExtras.Races(Sam, Array.Empty<SessionRecord>(), null, new[] { demo }),
        };

        var night = JsonDocument.Parse(DriverNight.Document(parts)!).RootElement;

        Assert.Equal(new[] { DriverNight.OnlyRunsLine }, night.GetProperty("lines").EnumerateArray().Select(l => l.GetString()));
        Assert.Equal("demo", night.GetProperty("races")[0].GetProperty("kind").GetString());

        // A real race with no facts is still no night, as it always was.
        var heat = Race("Heat 1", Tonight.AddHours(-1), Row(Sam, 1));
        Assert.Null(DriverNight.Document(parts with { Races = DriverNightExtras.Races(Sam, new[] { heat }, null) }));
    }

    [Fact]
    public void A_race_carries_no_kind_in_the_document()
    {
        var heat = Race("Heat 1", Tonight.AddHours(-1), Row(Sam, 1));
        var night = JsonDocument.Parse(DriverNight.Document(new NightParts("Sam Parker", Tonight, null, Facts("Raced."), At)
        {
            Races = DriverNightExtras.Races(Sam, new[] { heat }, null),
        })!).RootElement;

        Assert.False(night.GetProperty("races")[0].TryGetProperty("kind", out _));
    }

    /// <summary>
    /// 8 Nov 2026 (asked for by Asher: Just me and Compare with on a demo and
    /// practice): a demo never has a public result for the phone to read the
    /// field from, so demo and practice runs carry it in the night - races
    /// still read theirs from the result, and a run alone carries none.
    /// </summary>
    [Fact]
    public void Demo_and_practice_carry_their_field_and_races_do_not()
    {
        var heat = Race("Heat 1", Tonight.AddHours(-2), Row(Sam, 2), Row("3099999", 1));
        var demo = Race("[DEMO] Heat 2", Tonight.AddHours(-1), Row(Sam, 1), Row("DEMO01", 2));
        demo.IsDemo = true;
        demo.Results[0].DriverName = "Sam Parker";
        demo.Results[1].DriverName = "Robo Racer";
        var practice = Race("", Tonight.AddHours(-3), Row(Sam, 1), Row("3099999", 2));
        practice.SessionType = RaceSessionType.Practice;
        var alone = Race("", Tonight.AddHours(-4), Row(Sam, 1));
        alone.SessionType = RaceSessionType.Practice;

        var races = DriverNightExtras.Races(Sam, new[] { heat }, null, new[] { demo, practice, alone });

        Assert.Null(races.Single(r => r.Kind is null).Field);
        Assert.Null(races[0].Field);

        var rehearsal = races.Single(r => r.Kind == "demo").Field!;
        Assert.Equal(new[] { "Sam Parker", "Robo Racer" }, rehearsal.Select(f => f.Name));
        Assert.Equal(new long[] { 9000, 8800, 8900 }, rehearsal[1].LapsMs!);
        Assert.Equal(2, rehearsal[1].Pos);
        Assert.NotNull(rehearsal[1].PosAt);

        var run = races[1].Field!;
        Assert.Equal("practice", races[1].Kind);
        Assert.Equal(2, run.Count);
        Assert.All(run, f => { Assert.Null(f.Pos); Assert.Null(f.PosAt); });

        var night = JsonDocument.Parse(DriverNight.Document(new NightParts("Sam Parker", Tonight, null, Facts("Raced."), At)
        {
            Races = races,
        })!).RootElement;
        var sent = night.GetProperty("races").EnumerateArray().Single(r => r.TryGetProperty("kind", out var k) && k.GetString() == "demo");
        Assert.Equal("Robo Racer", sent.GetProperty("field")[1].GetProperty("name").GetString());
        Assert.False(sent.GetProperty("field")[1].TryGetProperty("transponder", out _));
    }

    private static PackedFacts Facts(params string[] lines) =>
        new(string.Join('\n', lines), Array.Empty<string>(), 0);

    private static SessionResultEntry Row(string who, int pos) => new()
    {
        Transponder = who,
        DriverName = who,
        Position = pos,
        Laps = Laps.Length,
        LapTimesMs = Laps.ToList(),
        FastLapMs = 8800,
    };

    private static SessionRecord Race(string label, DateTime startLocal, params SessionResultEntry[] rows) => new()
    {
        SessionType = RaceSessionType.Race,
        Label = label,
        TrackLayoutName = "Club Oval",
        StartedAtUtc = startLocal.ToUniversalTime(),
        EndedAtUtc = startLocal.ToUniversalTime().AddMinutes(5),
        Results = rows.ToList(),
    };
}
