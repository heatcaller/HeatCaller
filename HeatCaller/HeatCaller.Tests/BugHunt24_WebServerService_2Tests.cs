using System.Text.Json.Nodes;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the web server's pages: a junior's name
/// swapped inside other people's names on the stream (#327), the overlay's
/// and remote's heading in enum names (#328), and /manage's Undo last tap
/// forgetting a tap the laptop never took back (#329).
/// </summary>
public sealed class BugHunt24_WebServerService_2Tests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static WebServerService Server(RaceControlSession? race = null) =>
        new(new RaceTiming(race ?? new RaceControlSession()), ProfileStore.Load(), settings: new AppSettings { DisplayLook = Looks.MidnightKey });

    private static string Between(string page, string from, string to)
    {
        var start = page.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{from}' is not in the page.");
        var end = page.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"'{to}' does not follow '{from}' in the page.");
        return page[start..end];
    }

    // --- #327: whole names only --------------------------------------------

    [Fact]
    public void The_stream_pages_swap_juniors_names_as_whole_names_only()
    {
        var overlay = Server().BuildOverlayPage();
        var stream = Server().BuildBroadcastPage(OverlayLayouts.BuiltIn(OverlayLayouts.AutoId));

        foreach (var page in new[] { overlay, stream })
        {
            Assert.Contains(WebServerService.StreamNameSwapScript.Trim(), page);

            // The bare split/join is only the fallback when the pattern
            // cannot be built, never the swap itself.
            var swap = Between(page, "function snText(text){", "} catch (e) {");
            Assert.DoesNotContain("split(full)", swap);
            Assert.Contains("(?![\\\\p{L}\\\\p{N}])", swap);
            Assert.Contains("word.test(t.charAt(at - 1))", swap);

            // Everyone else the page knows is kept as themselves.
            Assert.Contains("snKeepNames(d);", page);
        }
    }

    [Fact]
    public void The_names_kept_as_themselves_are_the_boards_and_the_peoples()
    {
        var keep = Between(WebServerService.StreamNameSwapScript, "function snKeepNames(d){", "function snText(text){");

        Assert.Contains("d.people", keep);
        Assert.Contains("d.standings", keep);
        Assert.Contains("SN_SELF.filter(k => k && !has(k))", WebServerService.StreamNameSwapScript);
    }

    [Fact]
    public void The_call_sites_still_go_through_the_junior_rule()
    {
        var table = Server().BuildOverlayPage();

        Assert.Contains("esc(snText(c.text))", table);
        Assert.Contains("el.textContent = snText(newest.t);", table);
    }

    // --- #328: the heading in words ----------------------------------------

    [Theory]
    [InlineData(RaceState.Idle, FlagState.None, "Not racing")]
    [InlineData(RaceState.Idle, FlagState.WarmUp, "Warm-up - not counted")]
    [InlineData(RaceState.Finished, FlagState.WarmUp, "Warm-up - not counted")]
    [InlineData(RaceState.Racing, FlagState.Green, "Racing - Green flag")]
    [InlineData(RaceState.Finished, FlagState.Checkered, "Finished - Checkered flag")]
    [InlineData(RaceState.Racing, FlagState.Red, "Racing - Red flag")]
    public void The_state_and_flag_are_put_in_words(RaceState state, FlagState flag, string expected)
    {
        Assert.Equal(expected, WebServerService.StateWords(state, flag));
    }

    [Fact]
    public void A_warm_up_is_sent_in_words_and_the_flag_stays_the_wire_value()
    {
        var race = new RaceControlSession();
        Assert.True(race.BeginWarmUp(TimeSpan.FromMinutes(3)));

        var doc = JsonNode.Parse(Server(race).BuildStateJson())!;

        Assert.Equal("Warm-up - not counted", doc["stateWords"]!.GetValue<string>());
        Assert.Equal("WarmUp", doc["flag"]!.GetValue<string>());
        Assert.Equal("Idle", doc["state"]!.GetValue<string>());
    }

    [Fact]
    public void Between_races_is_not_racing_with_no_flag()
    {
        var doc = JsonNode.Parse(Server().BuildStateJson())!;

        Assert.Equal("Not racing", doc["stateWords"]!.GetValue<string>());
    }

    [Fact]
    public void The_overlay_and_the_remote_print_the_words_not_the_enum_names()
    {
        var overlay = Server().BuildOverlayPage();
        var remote = Server().BuildRemotePage();

        Assert.DoesNotContain("d.state + ' - ' + d.flag", overlay);
        Assert.DoesNotContain("d.state + ' - ' + d.flag", remote);
        Assert.Contains("(d.stateWords || '-') + prov", overlay);
        Assert.Contains("document.getElementById('state').textContent = d.stateWords || '-';", remote);
    }

    // --- #329: Undo last tap keeps a tap the laptop never took back ----------

    [Fact]
    public void Undo_last_tap_takes_the_tap_off_only_after_the_laptop_answers()
    {
        var page = Server().BuildManagePage();
        var undo = Between(page, "document.getElementById('btnHandUndo').addEventListener('click', function(){", "// --- Results");

        Assert.DoesNotContain("handTaps.pop()", undo);
        Assert.Contains("if (handUndoBusy || !handTaps.length) { return; }", undo);

        // The tap is taken off inside the answer, and only when the -1 was not
        // certainly turned away.
        var answer = undo.IndexOf("handLap(id, -1, function(d){", StringComparison.Ordinal);
        var splice = undo.IndexOf("if (!handUndoNotRun(d) && taps === handTaps && taps[at] === id) { taps.splice(at, 1); }", StringComparison.Ordinal);
        Assert.True(answer >= 0, "Undo no longer waits for the laptop's answer.");
        Assert.True(splice > answer, "The tap is taken off before the laptop has answered.");

        // handLap hands the answer back before anything else.
        Assert.Contains("function handLap(id, delta, onAnswer){", page);
        Assert.Contains("if (onAnswer) { onAnswer(d); }", page);
    }

    [Fact]
    public void A_minus_one_never_run_keeps_the_tap_and_a_real_refusal_drops_it()
    {
        var page = Server().BuildManagePage();
        var rule = Between(page, "function handUndoNotRun(d){", "document.getElementById('btnHandUndo')");

        Assert.Contains("if (!d || d.ok) { return false; }", rule);
        Assert.Contains("d.notRun === true", rule);
        Assert.Contains("typeof d._status === 'number' && d._status !== 200", rule);
        Assert.Contains("nothing was changed|is closing|still starting", rule);
    }

    [Fact]
    public void The_laptops_words_for_a_request_it_never_ran_are_the_ones_the_page_looks_for()
    {
        // The page knows a withdrawn request by RaceDayService's own words;
        // if they change, the page must change with them.
        var service = File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", "RaceDayService.cs"));

        Assert.Contains("The laptop didn't answer in time - nothing was changed. Try again.", service);
        Assert.Contains("Canceled - nothing was changed.", service);
        Assert.Contains("HeatCaller on the laptop is closing or still starting", service);
        Assert.Contains("HeatCaller on the laptop is closing - try again when it is back.", service);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Couldn't find the repo root (check.cmd).");
    }
}
