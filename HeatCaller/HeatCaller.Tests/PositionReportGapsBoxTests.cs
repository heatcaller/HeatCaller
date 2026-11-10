using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// 9 Nov 2026: with "Say each car's gap to the leader" unticked, the
/// position report still said "Bill one point two three four back" - the
/// box only moved the gaps between {gaps} and the order, it never turned
/// them off. Unticked now reads places alone.
/// </summary>
public sealed class PositionReportGapsBoxTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly MethodInfo WordRunningOrder = typeof(AnnouncementService)
        .GetMethod("WordRunningOrder", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("AnnouncementService.WordRunningOrder is gone - this test needs its name updated.");

    private static PositionReportOrder.Row Car(string name, int lapsDown, long? gapMs) =>
        new(new RaceStanding { Transponder = name, Laps = 10 - lapsDown },
            new RaceGap(lapsDown, gapMs), new RaceGap(lapsDown, gapMs));

    private string Say(string template, bool sayGaps, bool placesOnly)
    {
        var announcer = new AnnouncementService(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load()) { Muted = true };
        var rows = new List<PositionReportOrder.Row>
        {
            Car("Sam", 0, 0),
            Car("Bill", 0, 1234),
            Car("Frank", 1, null),
        };

        return (string)WordRunningOrder.Invoke(announcer, [rows, template, sayGaps, placesOnly])!;
    }

    [Fact]
    public void Unticked_the_order_is_read_as_places_with_no_gaps()
    {
        var said = Say("As it stands - {positions}.", sayGaps: false, placesOnly: true);

        Assert.Contains("Sam leads", said, StringComparison.Ordinal);
        Assert.Contains("Bill second", said, StringComparison.Ordinal);
        Assert.Contains("Frank third", said, StringComparison.Ordinal);
        Assert.DoesNotContain("back", said, StringComparison.Ordinal);
        Assert.DoesNotContain("lap down", said, StringComparison.Ordinal);
    }

    [Fact]
    public void Unticked_a_gaps_clause_in_the_line_drops_out()
    {
        var said = Say("As it stands - {positions}. Gaps: {gaps}.", sayGaps: false, placesOnly: true);

        Assert.DoesNotContain("Gaps", said, StringComparison.Ordinal);
        Assert.DoesNotContain("back", said, StringComparison.Ordinal);
    }

    [Fact]
    public void Ticked_with_no_gaps_placeholder_the_gaps_follow_each_name()
    {
        var said = Say("As it stands - {positions}.", sayGaps: false, placesOnly: false);

        Assert.Contains("back", said, StringComparison.Ordinal);
        Assert.Contains("a lap down", said, StringComparison.Ordinal);
    }

    [Fact]
    public void The_box_decides_places_only_for_every_wording_of_the_report()
    {
        var source = SourceFile("AnnouncementService.LapCalls.cs");

        Assert.Contains("var placesOnly = !config.PositionReportSayGaps;", source, StringComparison.Ordinal);
        Assert.Contains("WordRunningOrder(rows, template, sayGaps, placesOnly)", source, StringComparison.Ordinal);
        Assert.Contains("WordRunningOrder(now, template, sayGaps, placesOnly)", source, StringComparison.Ordinal);
        Assert.Contains("WordRunningOrder(again, template, sayGaps, placesOnly)", source, StringComparison.Ordinal);
    }

    private static string SourceFile(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", name));
    }
}
