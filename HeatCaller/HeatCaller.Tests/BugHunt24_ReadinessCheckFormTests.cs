namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): the Pre-race check.
/// #45 (F269) - a driver seated on their second car's transponder was called
/// "not heard at all" and listed again as a stranger on track.
/// #280 (F270) - with no list to check against, every car warming up was
/// called "on track who should not be".
/// </summary>
public sealed class BugHunt24_ReadinessCheckFormTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static DriverProfile Driver(string name, string main, params string[] alternates)
    {
        var profile = new DriverProfile { Transponder = main, DriverName = name };

        foreach (var alternate in alternates)
        {
            profile.Transponders.Add(new OwnedTransponder { Id = alternate });
        }

        return profile;
    }

    private static DirectLapEvent Pass(string id, int hits = 8) =>
        new() { TransponderId = id, SignalHits = hits, SignalStrength = 120 };

    /// <summary>Max: main 100, second car 200. The watch wired as MainForm wires it.</summary>
    private static (ProfileStore Profiles, TransponderWatch Watch) MaxWithTwoCars()
    {
        var profiles = ProfileStore.Load();
        Assert.True(profiles.TryAdd(Driver("Max", "100", "200"), out _));

        var watch = new TransponderWatch { AlternateTransponderOwner = profiles.PrimaryForAlternate };

        return (profiles, watch);
    }

    [Fact]
    public void A_driver_seated_on_their_second_cars_transponder_is_heard_and_is_not_a_stranger()
    {
        var (profiles, watch) = MaxWithTwoCars();

        for (var i = 0; i < 3; i++)
        {
            watch.Record(Pass("200"));
        }

        var lines = ReadinessCheckForm.Build(new[] { "200" }, watch, profiles, splitLoop: false);

        var line = Assert.Single(lines);
        Assert.Equal(ReadinessCheckForm.Verdict.Ready, line.Verdict);
        Assert.Equal("Max", line.Driver);
        Assert.Equal("200", line.Transponder);
    }

    [Fact]
    public void A_driver_listed_by_their_main_transponder_out_in_their_second_car_is_still_heard()
    {
        var (profiles, watch) = MaxWithTwoCars();
        watch.Record(Pass("200"));

        var lines = ReadinessCheckForm.Build(new[] { "100" }, watch, profiles, splitLoop: false);

        var line = Assert.Single(lines);
        Assert.Equal(ReadinessCheckForm.Verdict.Ready, line.Verdict);
    }

    [Fact]
    public void A_car_nobody_listed_is_still_a_stranger_and_a_silent_one_still_missing()
    {
        var (profiles, watch) = MaxWithTwoCars();
        watch.Record(Pass("999"));

        var lines = ReadinessCheckForm.Build(new[] { "200" }, watch, profiles, splitLoop: false);

        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.Verdict == ReadinessCheckForm.Verdict.Missing && l.Transponder == "200");
        Assert.Contains(lines, l => l.Verdict == ReadinessCheckForm.Verdict.Stranger && l.Transponder == "999");

        var (text, _) = ReadinessCheckForm.Headline(lines, "round", rosterEmpty: false);
        Assert.Contains("1 not heard at all", text, StringComparison.Ordinal);
        Assert.Contains("1 on track who should not be", text, StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_list_to_check_against_cars_warming_up_are_not_called_strangers()
    {
        var (profiles, watch) = MaxWithTwoCars();
        watch.Record(Pass("100"));
        watch.Record(Pass("555"));

        var lines = ReadinessCheckForm.Build(Array.Empty<string>(), watch, profiles, splitLoop: false);

        Assert.Equal(2, lines.Count);
        Assert.DoesNotContain(lines, l => l.Verdict == ReadinessCheckForm.Verdict.Stranger);
        Assert.DoesNotContain(lines, l => l.Detail.Contains("not in this race", StringComparison.Ordinal));
        Assert.All(lines, l => Assert.Equal(ReadinessCheckForm.Verdict.Ready, l.Verdict));

        var (text, tone) = ReadinessCheckForm.Headline(lines, "drivers already on the board", rosterEmpty: true);
        Assert.DoesNotContain("who should not be", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Put the cars that failed", text, StringComparison.Ordinal);
        Assert.Contains("No list to check against", text, StringComparison.Ordinal);
        Assert.Contains("2 cars are being heard", text, StringComparison.Ordinal);
        Assert.Equal(ReadinessCheckForm.HeadlineTone.Muted, tone);
    }

    [Fact]
    public void With_no_list_a_car_only_just_heard_is_still_warned_about()
    {
        var (profiles, watch) = MaxWithTwoCars();
        watch.Record(Pass("555", hits: 1));

        var lines = ReadinessCheckForm.Build(Array.Empty<string>(), watch, profiles, splitLoop: false);

        var line = Assert.Single(lines);
        Assert.Equal(ReadinessCheckForm.Verdict.Weak, line.Verdict);

        var (text, tone) = ReadinessCheckForm.Headline(lines, "drivers already on the board", rosterEmpty: true);
        Assert.Contains("1 only just being heard", text, StringComparison.Ordinal);
        Assert.Equal(ReadinessCheckForm.HeadlineTone.Warning, tone);
    }

    [Fact]
    public void With_no_list_and_nothing_heard_there_is_nothing_to_check()
    {
        var (text, tone) = ReadinessCheckForm.Headline(new List<ReadinessCheckForm.Line>(), "round", rosterEmpty: true);

        Assert.StartsWith("Nothing to check", text, StringComparison.Ordinal);
        Assert.Equal(ReadinessCheckForm.HeadlineTone.Muted, tone);
    }
}
