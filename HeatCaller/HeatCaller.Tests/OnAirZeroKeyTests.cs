namespace HeatCaller.Tests;

/// <summary>
/// On Air's key is 0, and skipping it is a hold (1 Nov 2026, asked for by
/// Asher: "i prefer it to be 0 instead of o as that's confusing and I think
/// you should have to press and hold to skip").
///
/// A tap of 0 starts On Air, as O did. While On Air runs, 0 has to be held
/// for a second - the same second as Red, Checkered and Reset - to skip to
/// Stage; let go early and nothing happens. The press that starts On Air
/// never turns into the skip, however long it is held.
///
/// The timing is tested on its own (KeyHoldGesture); the wiring by source
/// checks, as the rest of Race Control's keys are - the window cannot be
/// built in a test.
/// </summary>
public sealed class OnAirZeroKeyTests
{
    private const long Second = 1000;

    // --- The timing --------------------------------------------------------

    [Fact]
    public void A_key_held_for_a_second_completes_once()
    {
        var key = new KeyHoldGesture(Second);

        Assert.True(key.Press(0, hold: true));
        Assert.False(key.TryComplete(999));
        Assert.True(key.TryComplete(1000));
        Assert.False(key.TryComplete(1500));
    }

    [Fact]
    public void Windows_repeating_the_key_does_not_restart_the_hold()
    {
        // A held key sends KeyDown over and over. Fed to HoldGesture.Press
        // directly, each would start the hold again and it would never end.
        var key = new KeyHoldGesture(Second);

        Assert.True(key.Press(0, hold: true));

        for (var at = 30L; at < 1000; at += 33)
        {
            Assert.False(key.Press(at, hold: true));
        }

        Assert.True(key.TryComplete(1000));
    }

    [Fact]
    public void Let_go_too_soon_calls_it_off_and_nothing_is_done()
    {
        var key = new KeyHoldGesture(Second);

        key.Press(0, hold: true);

        Assert.Equal(0.5f, key.ProgressAt(500));
        Assert.True(key.Release());
        Assert.False(key.TryComplete(2000));
        Assert.Null(key.ProgressAt(2000));
    }

    [Fact]
    public void Letting_go_after_it_completed_calls_nothing_off()
    {
        var key = new KeyHoldGesture(Second);

        key.Press(0, hold: true);
        Assert.True(key.TryComplete(1000));
        Assert.False(key.Release());
    }

    [Fact]
    public void The_tap_that_starts_On_Air_never_becomes_the_skip()
    {
        // 0 pressed with On Air not running is a tap: it starts On Air. Kept
        // down, its repeats arrive with On Air now running - they must not
        // start a hold, or holding the start key would skip the show it
        // just started.
        var key = new KeyHoldGesture(Second);

        Assert.True(key.Press(0, hold: false));
        Assert.False(key.Holding);
        Assert.False(key.Press(500, hold: true));
        Assert.False(key.TryComplete(5000));

        // A fresh press does.
        Assert.False(key.Release());
        Assert.True(key.Press(6000, hold: true));
        Assert.True(key.TryComplete(7000));
    }

    [Fact]
    public void A_dropped_hold_waits_for_the_key_to_come_up()
    {
        // On Air finished by itself mid-hold: the hold is dropped, and the
        // key - still down - starts nothing more until it is let go.
        var key = new KeyHoldGesture(Second);

        key.Press(0, hold: true);
        key.Drop();

        Assert.False(key.Holding);
        Assert.True(key.Down);
        Assert.False(key.Press(400, hold: true));
        Assert.False(key.TryComplete(2000));
    }

    // --- The wiring ----------------------------------------------------------

    [Fact]
    public void Zero_is_On_Airs_key_and_O_is_not()
    {
        var keyboard = WithoutComments(Source("MainForm.Keyboard.cs"));

        Assert.Contains("case Keys.D0 or Keys.NumPad0 when !e.Shift: rc.OnAirKeyDown(); break;", keyboard, StringComparison.Ordinal);
        Assert.DoesNotContain("case Keys.O when", keyboard, StringComparison.Ordinal);
        Assert.DoesNotContain("TriggerOnAir", keyboard, StringComparison.Ordinal);
    }

    [Fact]
    public void Letting_go_of_zero_reaches_the_hold_wherever_the_focus_is()
    {
        var keyboard = WithoutComments(MethodBody(Source("MainForm.Keyboard.cs"),
            "private void MainForm_KeyUp(", "private static void MarkHandled("));

        Assert.Contains("e.KeyCode is Keys.D0 or Keys.NumPad0", keyboard, StringComparison.Ordinal);
        Assert.Contains("_raceControlControl?.OnAirKeyUp();", keyboard, StringComparison.Ordinal);

        var form = Source("MainForm.cs");

        Assert.Contains("KeyUp += MainForm_KeyUp;", form, StringComparison.Ordinal);
        Assert.Contains("Deactivate += (_, _) => _raceControlControl?.OnAirKeyUp();", form, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tap_starts_On_Air_and_a_hold_is_only_timed_while_it_runs()
    {
        var down = WithoutComments(MethodBody(Source("RaceControlControl.OnAir.cs"),
            "internal void OnAirKeyDown()", "internal void OnAirKeyUp()"));

        Assert.Contains("var running = _onAirCts is not null;", down, StringComparison.Ordinal);
        Assert.Contains("if (!OnAirKey.KeyDown(hold: running))", down, StringComparison.Ordinal);
        Assert.Contains("if (!running)\n        {\n            PressOnAir();", down, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hold_skips_only_while_On_Air_is_still_running()
    {
        var onAir = Source("RaceControlControl.OnAir.cs");

        // The hold is On Air's own key, timed for HoldToAct's second, and
        // dropped once On Air has nothing left to skip.
        var make = WithoutComments(MethodBody(onAir, "private KeyHoldToAct MakeOnAirKey()", "internal void OnAirKeyDown()"));
        Assert.Contains("new KeyHoldToAct(OnAirHoldDoing, () => _onAirCts is not null", make, StringComparison.Ordinal);
        Assert.Contains("SkipOnAirToStage, Keys.D0, Keys.NumPad0)", make, StringComparison.Ordinal);
        Assert.Contains("_holdStrip.Follow(h)", make, StringComparison.Ordinal);
        Assert.Contains("_holdStrip.CalledOff()", make, StringComparison.Ordinal);

        Assert.Contains("KeyHoldGesture _gesture = new((long)HoldToAct.HoldFor.TotalMilliseconds)", Source("KeyHold.cs"), StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromSeconds(1), HoldToAct.HoldFor);

        // Completing after On Air ended must not start a new one.
        var skip = WithoutComments(MethodBody(onAir, "private void SkipOnAirToStage()", "internal void HintHoldToSkip()"));
        var guard = skip.IndexOf("if (_onAirCts is null", StringComparison.Ordinal);
        var press = skip.IndexOf("PressOnAir();", StringComparison.Ordinal);
        Assert.True(guard > 0 && press > guard, "The hold's skip must check On Air is still running before pressing it.");
    }

    [Fact]
    public void F1_no_longer_skips_On_Air_with_a_tap()
    {
        var keyboard = WithoutComments(Source("MainForm.Keyboard.cs"));

        Assert.Contains("if (rc.OnAirRunning) rc.HintHoldToSkip(); else rc.TriggerStart();", keyboard, StringComparison.Ordinal);
    }

    [Fact]
    public void The_bar_the_sheet_and_the_tooltip_all_say_hold_zero()
    {
        Assert.Equal("hold 0 to skip to Stage", RaceControlControl.OnAirSkipNote);

        var raceControl = ShortcutCatalog.Sections.Single(section => section.Title == "Race Control").Items;
        Assert.Contains(raceControl, item => item.Keys == "0" && item.Action.Contains("hold", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(raceControl, item => item.Keys == "O");

        var onAir = Source("RaceControlControl.OnAir.cs");
        Assert.Contains("hold 0 for a second to skip to Stage", onAir, StringComparison.Ordinal);
        Assert.DoesNotContain("O starts it by hand", onAir, StringComparison.Ordinal);

        Assert.Contains("hold 0 for a second to skip to Stage now.", Source("MainForm.RaceSetup.cs"), StringComparison.Ordinal);
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

    // Line endings made LF, so a test that names a line break works on a
    // checkout that turned them into CRLF.
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
