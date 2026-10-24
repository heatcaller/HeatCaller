namespace HeatCaller.Tests;

/// <summary>Driver experience P0 (22 Oct 2026): DriverFirsts - is tonight a driver's first night?</summary>
public sealed class DriverExp_P0_DriverFirstsTests
{
    private static readonly DateTime Tonight = new(2026, 10, 22, 20, 0, 0, DateTimeKind.Local);

    private static SessionRecord Session(DateTime startLocal, string transponder, bool demo = false, RaceSessionType type = RaceSessionType.Race)
    {
        var session = new SessionRecord
        {
            SessionType = type,
            StartedAtUtc = startLocal.ToUniversalTime(),
            EndedAtUtc = startLocal.AddMinutes(5).ToUniversalTime(),
            IsDemo = demo,
        };

        session.Results.Add(new SessionResultEntry { Transponder = transponder, DriverName = "X" });
        return session;
    }

    [Fact]
    public void Nobody_with_no_history_before_tonight_is_on_their_first_night()
    {
        Assert.True(DriverFirsts.IsFirstNight(Array.Empty<SessionRecord>(), "T1", Tonight));
    }

    [Fact]
    public void A_race_on_an_earlier_night_means_it_is_not()
    {
        var history = new[] { Session(Tonight.AddDays(-7), "t1") };

        Assert.False(DriverFirsts.IsFirstNight(history, "T1", Tonight));
    }

    [Fact]
    public void Practice_on_an_earlier_night_counts_too()
    {
        var history = new[] { Session(Tonight.AddDays(-7), "T1", type: RaceSessionType.Practice) };

        Assert.False(DriverFirsts.IsFirstNight(history, "T1", Tonight));
    }

    [Fact]
    public void Races_tonight_including_after_midnight_do_not_count_against_it()
    {
        var history = new[]
        {
            Session(Tonight.AddHours(-1), "T1"),
            Session(new DateTime(2026, 10, 23, 1, 0, 0, DateTimeKind.Local), "T1"),
        };

        Assert.True(DriverFirsts.IsFirstNight(history, "T1", Tonight));

        // Asked at 1am, it is still the same night.
        Assert.True(DriverFirsts.IsFirstNight(history, "T1", new DateTime(2026, 10, 23, 1, 30, 0, DateTimeKind.Local)));
    }

    [Fact]
    public void Demo_races_and_other_drivers_do_not_count()
    {
        var history = new[]
        {
            Session(Tonight.AddDays(-7), "T1", demo: true),
            Session(Tonight.AddDays(-7), "T2"),
        };

        Assert.True(DriverFirsts.IsFirstNight(history, "T1", Tonight));
        Assert.False(DriverFirsts.IsFirstNight(history, "T2", Tonight));
    }

    [Fact]
    public void The_night_can_be_given_as_its_date()
    {
        var history = new[] { Session(new DateTime(2026, 10, 21, 20, 0, 0, DateTimeKind.Local), "T1") };

        Assert.False(DriverFirsts.IsFirstNight(history, "T1", TonightAtTheTrack.NightOf(Tonight)));
        Assert.True(DriverFirsts.IsFirstNight(history, "T1", new DateTime(2026, 10, 21)));
    }

    [Fact]
    public void No_transponder_is_never_a_first_night()
    {
        Assert.False(DriverFirsts.IsFirstNight(Array.Empty<SessionRecord>(), " ", Tonight));
        Assert.False(DriverFirsts.IsFirstNight(null, null, Tonight));
    }
}
