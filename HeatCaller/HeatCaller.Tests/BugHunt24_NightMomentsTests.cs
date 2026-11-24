namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, NightMoments (23 Nov 2026): a qualifying heat runs on each
/// car's own clock - every car's lap times start at its own first crossing,
/// so the start slot is zero for all of them. ForRace laid those laps on one
/// timeline, so every car "crossed" at nought, the lowest transponder became
/// the first leader by tie-break and the quickest first flying lap was
/// handed a lead change (and a late own-clock swap a last-lap lead) in a
/// heat where nobody passed anybody. Races on per-car clocks now make no
/// lead-change moments; a race from the green still does.
/// </summary>
public sealed class BugHunt24_NightMomentsTests : IDisposable
{
    private readonly TempAppData _appData = new();

    public void Dispose() => _appData.Dispose();

    private static readonly DateTime Green = new(2026, 11, 23, 19, 0, 0, DateTimeKind.Utc);

    private static SessionResultEntry Row(string transponder, int position, long[] laps, long clockZeroMs = 0, bool startLapCredited = false) =>
        new()
        {
            Transponder = transponder,
            DriverName = "Driver " + transponder,
            Position = position,
            Laps = laps.Length,
            LapTimesMs = laps.ToList(),
            ElapsedMs = laps.Sum(),
            ClockZeroMs = clockZeroMs,
            StartLapCredited = startLapCredited,
        };

    private static SessionRecord Race(params SessionResultEntry[] rows) =>
        new()
        {
            Id = "q1-heat-1",
            SessionType = RaceSessionType.Race,
            Label = "Q1 Heat 1",
            StartedAtUtc = Green,
            EndedAtUtc = Green.AddMinutes(5),
            Results = rows.ToList(),
        };

    private static bool AnyLead(IReadOnlyList<NightMoments.Moment> found) =>
        found.Any(one => one.What is NightMoments.Kind.LeadChange or NightMoments.Kind.LastLapLead);

    [Fact]
    public void A_qualifying_heat_on_each_cars_own_clock_has_no_lead_change()
    {
        var found = NightMoments.ForRace(Race(
            Row("2222", 1, new long[] { 0, 9000, 9000, 9000 }, clockZeroMs: 6000),
            Row("1111", 2, new long[] { 0, 10000, 10000, 10000 }, clockZeroMs: 2000)));

        Assert.False(AnyLead(found));
        Assert.Contains(found, one => one.What is NightMoments.Kind.FastestLap && one.Transponder == "2222");
    }

    [Fact]
    public void An_old_saved_qualifier_with_no_clock_zero_has_no_lead_change()
    {
        // Saved before ClockZeroMs was kept: every start slot is zero.
        var found = NightMoments.ForRace(Race(
            Row("2222", 1, new long[] { 0, 9000, 9000, 9000 }),
            Row("1111", 2, new long[] { 0, 10000, 10000, 10000 })));

        Assert.False(AnyLead(found));
    }

    [Fact]
    public void A_qualifier_swap_on_the_last_lap_is_not_a_last_lap_lead()
    {
        var found = NightMoments.ForRace(Race(
            Row("1111", 1, new long[] { 0, 9000, 9000, 9000, 8000 }, clockZeroMs: 1500),
            Row("2222", 2, new long[] { 0, 8900, 9000, 9000, 9500 }, clockZeroMs: 4000)));

        Assert.DoesNotContain(found, one => one.What is NightMoments.Kind.LastLapLead);
    }

    [Fact]
    public void A_race_from_the_green_still_has_its_last_lap_lead()
    {
        var found = NightMoments.ForRace(Race(
            Row("sam", 1, new long[] { 10500, 9000, 9000, 9000, 8500 }),
            Row("bill", 2, new long[] { 10000, 9000, 9000, 9000, 9500 })));

        Assert.Contains(found, one => one.What is NightMoments.Kind.LastLapLead && one.Transponder == "sam");
    }

    [Fact]
    public void Per_car_clocks_are_read_off_the_rows()
    {
        // Any clock started after the green.
        Assert.True(NightMoments.OnPerCarClocks(new[]
        {
            Row("a", 1, new long[] { 10000, 9000 }),
            Row("b", 2, new long[] { 0, 9000 }, clockZeroMs: 3000),
        }));

        // Every start slot zero, nobody credited the start lap.
        Assert.True(NightMoments.OnPerCarClocks(new[]
        {
            Row("a", 1, new long[] { 0, 9000 }),
            Row("b", 2, new long[] { 0, 9100 }),
        }));

        // A race from the green, one car credited the start lap: one clock.
        Assert.False(NightMoments.OnPerCarClocks(new[]
        {
            Row("a", 1, new long[] { 0, 4000, 9000 }, startLapCredited: true),
            Row("b", 2, new long[] { 10000, 9100 }),
        }));

        // A lone car proves nothing.
        Assert.False(NightMoments.OnPerCarClocks(new[]
        {
            Row("a", 1, new long[] { 0, 9000 }),
        }));
    }
}
