namespace HeatCaller.Tests;

/// <summary>
/// The flag row's Skip, held, skips On Air (16 Nov 2026, Asher: "the skip
/// flag button doesn't skip the playing ai segment. You hold it until it's
/// supposed to skip and it goes back to playing").
///
/// The hold only knew the recap show and commentary segment blocks. On Air's
/// opening, driver introductions and pit lane are neither, so the hold
/// logged "none is on air" and the opening played on. Now, while On Air
/// runs, the hold does what holding 0 does: skips On Air and stages the race.
///
/// The wiring is checked in the source, as OnAirZeroKeyTests checks 0's -
/// Race Control cannot be built in a test.
/// </summary>
public sealed class SkipHoldOnAirTests
{
    [Fact]
    public void Held_while_On_Air_runs_it_skips_On_Air_before_anything_else()
    {
        var body = WithoutComments(MethodBody(Source("RaceControlControl.BetweenRaces.cs"),
            "private void SkipWholeShow()", "internal string SkipHoldDoing"));

        var onAir = body.IndexOf("if (OnAirRunning)", StringComparison.Ordinal);
        var skipOnAir = body.IndexOf("SkipOnAirToStage();", StringComparison.Ordinal);
        var show = body.IndexOf("SkipWhatIsOnAir();", StringComparison.Ordinal);

        Assert.True(onAir >= 0 && skipOnAir > onAir, "While On Air runs the hold must skip it, as holding 0 does.");
        Assert.True(show > skipOnAir, "The recap show and segment blocks are what it ends when On Air is not running.");
        Assert.Contains("return;", body[skipOnAir..show], StringComparison.Ordinal);
    }

    [Fact]
    public void The_strip_says_what_the_hold_will_do_now()
    {
        var source = WithoutComments(Source("RaceControlControl.BetweenRaces.cs"));

        Assert.Contains("internal string SkipHoldDoing => OnAirRunning ? OnAirHoldDoing : \"end the recap show\";", source, StringComparison.Ordinal);
        Assert.Contains("new HoldToAct(_btnBarSkip, () => SkipHoldDoing, SkipWholeShow);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hold_asks_what_it_does_each_time_it_is_shown()
    {
        using var button = new Button();
        var doing = "end the recap show";
        using var hold = new HoldToAct(button, () => doing, () => { });

        Assert.Equal("end the recap show", hold.Doing);

        doing = RaceControlControl.OnAirHoldDoing;
        Assert.Equal("skip On Air and stage now", hold.Doing);
    }

    [Fact]
    public void A_fixed_hold_still_says_its_one_thing()
    {
        using var button = new Button();
        using var hold = new HoldToAct(button, "throw the red flag", () => { });

        Assert.Equal("throw the red flag", hold.Doing);
    }

    // ------------------------------------------------------------------

    private static string RepoRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory);
        }
    }

    private static string Source(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName)).Replace("\r\n", "\n");

    private static string WithoutComments(string body) =>
        string.Join(
            "\n",
            body.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string MethodBody(string source, string from, string until)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);

        Assert.True(start >= 0, $"\"{from}\" is not in the file any more - this test needs its name updated.");

        var rest = source[start..];
        var end = rest.IndexOf(until, from.Length, StringComparison.Ordinal);

        Assert.True(end >= 0, $"\"{until}\" no longer follows \"{from}\" - this test needs its bounds updated.");

        return rest[..end];
    }
}
