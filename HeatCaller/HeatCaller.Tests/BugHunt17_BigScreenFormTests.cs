namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, windows the tests cannot drive, read from the source the way
/// BugHunt7BigscreenVideoTests reads the Big Screen: a commercial break steps
/// past a sponsor slide whose file has gone (62), Enter in the bulk
/// inspection's notes starts a new line (63), the "said they're coming" line
/// keeps up with every check-in (64), and the built-in model's weights are
/// swapped in before the pending load is cleared (180).
/// </summary>
public sealed class BugHunt17_BigScreenFormTests
{
    [Fact]
    public void A_break_steps_past_a_slide_whose_file_has_gone()
    {
        var show = MethodBody(
            Source("BigScreenForm.cs"), "private void ShowNextSponsorSlide(bool leadWithVideo = false)", "private int? NextVideoSlide()");

        var stepped = show.IndexOf("if (!stillsOnly && NextSlideThatIsThere() is { } there)", StringComparison.Ordinal);
        var chosen = show.IndexOf("var fileName = _sponsors.SlideFileNames[_sponsorIndex];", StringComparison.Ordinal);

        // Before the slide is picked, so the one picked is one that is there.
        Assert.True(stepped >= 0 && chosen > stepped);

        // And the slides stepped over count as slots the break has had, so
        // it still ends after one pass rather than showing a sponsor twice;
        // a step right round to a slide already shown ends the pass.
        var counted = show.IndexOf("_breakSlidesShown += (there - _sponsorIndex + slides) % slides;", stepped, StringComparison.Ordinal);
        var roundAgain = show.IndexOf("if (there != _sponsorIndex && _breakSlidesShown > slides)", stepped, StringComparison.Ordinal);
        var moved = show.IndexOf("_sponsorIndex = there;", stepped, StringComparison.Ordinal);

        Assert.True(counted > stepped && roundAgain > counted && moved > roundAgain && chosen > moved);

        var next = MethodBody(Source("BigScreenForm.cs"), "private int? NextSlideThatIsThere()", "private void ShowSponsorVideo(");

        Assert.Contains("var from = Math.Max(0, _sponsorIndex);", next, StringComparison.Ordinal);
        Assert.Contains("if (_sponsors.ResolvePath(names[at]) is not null)", next, StringComparison.Ordinal);
    }

    [Fact]
    public void Enter_in_the_bulk_inspection_notes_starts_a_new_line()
    {
        Assert.Contains("_txtNotes.AcceptsReturn = true;", Source("BulkInspectionForm.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_check_in_brings_the_coming_and_entries_closed_lines_up_to_date()
    {
        var summary = MethodBody(Source("CheckInControl.cs"), "private void UpdateSummary()", "\n    }");

        Assert.Contains("RefreshEntriesClosed();", summary, StringComparison.Ordinal);
        Assert.Contains("RefreshComing();", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_landing_load_swaps_the_weights_in_before_it_stops_being_pending()
    {
        var source = Source("BundledLlmClient.cs");

        var install = MethodBody(source, "private async Task<LLamaWeights> LoadSharedAsync(", "previous.Dispose();");

        var weights = install.IndexOf("Volatile.Write(ref _loadedWeights, weights);", StringComparison.Ordinal);
        var key = install.IndexOf("Volatile.Write(ref _loadedKey, key);", weights, StringComparison.Ordinal);
        var cleared = install.IndexOf("Volatile.Write(ref _loading, null);", key, StringComparison.Ordinal);

        Assert.True(weights >= 0 && key > weights && cleared > key);

        // And the ungated check reads them the other way round.
        var check = MethodBody(source, "private static LLamaWeights? HeldWeightsFor(", "return CanUseHeldWeights(");

        var pending = check.IndexOf("Volatile.Read(ref _loading)", StringComparison.Ordinal);
        var loadedKey = check.IndexOf("Volatile.Read(ref _loadedKey)", pending, StringComparison.Ordinal);
        var held = check.IndexOf("Volatile.Read(ref _loadedWeights)", loadedKey, StringComparison.Ordinal);

        Assert.True(pending >= 0 && loadedKey > pending && held > loadedKey);
    }

    // --- Helpers ----------------------------------------------------------------------------

    /// <summary>The folder the repo is checked out in, found by the file that is only ever at its root.</summary>
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
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName));

    /// <summary>The source from <paramref name="from"/> up to <paramref name="until"/>.</summary>
    private static string MethodBody(string source, string from, string until)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);

        Assert.True(start >= 0, $"\"{from}\" is not in the file any more - this test needs its name updated.");

        var rest = source[start..];
        var end = rest.IndexOf(until, StringComparison.Ordinal);

        Assert.True(end >= 0, $"\"{until}\" no longer follows \"{from}\" - this test needs its bounds updated.");

        return rest[..end];
    }
}
