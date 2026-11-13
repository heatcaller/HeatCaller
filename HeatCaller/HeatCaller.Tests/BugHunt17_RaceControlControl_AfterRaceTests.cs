namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), RaceControlControl.AfterRace.cs.
///
/// Source checks, as the rest of Race Control's are (HandOffTests,
/// BugHunt11_AfterRaceATests): the page cannot be built in a test.
/// </summary>
public sealed class BugHunt17_RaceControlControl_AfterRaceTests
{
    /// <summary>
    /// A QUICK RACE WITH NOTHING TO SAVE KEPT ITS STAMP. The "Quick Race
    /// HH:mm" stamp was let go only from FilingFinished, which the filing
    /// service never reaches for a race with no laps or only ghosts - so the
    /// next freeform race was saved under it.
    /// </summary>
    [Fact]
    public void The_Quick_Race_stamp_is_let_go_on_every_way_out_of_the_filing()
    {
        var save = WithoutComments(MethodBody(Source("RaceControlControl.AfterRace.cs"),
            "private void SaveCompletedSessionToHistory()", "private void FileCompletedRace(bool wasQuickRace, bool wasRerun)"));

        var filed = save.IndexOf("FileCompletedRace(wasQuickRace, wasRerun);", StringComparison.Ordinal);
        var whatever = save.IndexOf("finally", StringComparison.Ordinal);
        var released = save.IndexOf("if (wasQuickRace && QuickRaceStampToRelease(_txtSessionLabel.Text) is not null)", StringComparison.Ordinal);

        Assert.True(filed > 0 && whatever > filed && released > whatever,
            "The Quick Race's stamp is not released in a finally around the filing.");
    }

    /// <summary>
    /// THE BUMPER INTO THE BREAK PLAYED ON OVER THE STAGING CALL. Talked up
    /// off the teaser and waited for here, it was left whole when the next
    /// race (or Skip, Hold) called the wait off - every other wait on an
    /// imaging piece fades it.
    /// </summary>
    [Fact]
    public void The_bumper_into_the_break_is_faded_when_its_wait_is_called_off()
    {
        var wayIn = WithoutComments(MethodBody(Source("RaceControlControl.AfterRace.cs"),
            "private async Task PlayTheWayIntoTheBreakAsync(", "if (BroadcastImaging.Has(ImagingCue.AfterRaceOver))"));

        var waited = wayIn.IndexOf("await started.AtPost.WaitAsync(nextRace);", StringComparison.Ordinal);
        var whatever = wayIn.IndexOf("finally", StringComparison.Ordinal);
        var faded = wayIn.IndexOf("started.CrossfadeOut();", StringComparison.Ordinal);

        Assert.True(waited > 0 && whatever > waited && faded > whatever,
            "The bumper is only faded when its wait runs to the end.");
    }

    [Fact]
    public void A_bumper_the_break_never_took_is_faded_when_the_next_race_was_called()
    {
        var sequence = WithoutComments(MethodBody(Source("RaceControlControl.AfterRace.cs"),
            "private void SpeakRaceSummary(", "private sealed record RecapPlan("));

        var held = sequence.IndexOf("await HoldTheCommercialBreakAsync(recapTask!, nextRace.Token, breakFollows);", StringComparison.Ordinal);
        var taken = sequence.IndexOf("var neverHeardOut = Interlocked.Exchange(ref _intoTheStopSet, null);", StringComparison.Ordinal);
        var faded = sequence.IndexOf("neverHeardOut?.CrossfadeOut();", StringComparison.Ordinal);
        var back = sequence.IndexOf("await BackFromTheBreakAsync();", StringComparison.Ordinal);

        Assert.True(held > 0 && taken > held && faded > taken && back > faded,
            "A bumper stashed for a break that did not open is not faded once the next race is called.");
        Assert.Contains("if (nextRace.IsCancellationRequested)", sequence[taken..faded], StringComparison.Ordinal);
    }

    /// <summary>
    /// A FAILURE IN THE RECAP SEQUENCE WAS SILENT AND LOST THE BADGES. The
    /// bare catch said nothing, and the badges held back for the achievements
    /// segment were only called one at a time after a Skip or Hold.
    /// </summary>
    [Fact]
    public void A_recap_sequence_that_fails_says_so_and_still_calls_its_held_badges()
    {
        var sequence = WithoutComments(MethodBody(Source("RaceControlControl.AfterRace.cs"),
            "private void SpeakRaceSummary(", "private sealed record RecapPlan("));

        var caught = MethodBody(sequence, "catch (Exception ex)\n            {\n                ReleaseDeepDuck();", "finally");

        Assert.Contains("if (ex is not OperationCanceledException)", caught, StringComparison.Ordinal);
        Assert.Contains("sequenceFailed = true;", caught, StringComparison.Ordinal);
        Assert.Contains("StatusMessage?.Invoke(", caught, StringComparison.Ordinal);
        Assert.Contains("{ex.Message}", caught, StringComparison.Ordinal);

        var badges = MethodBody(sequence,
            "else if (honours is { Started: false, Unlocks: { Count: > 0 } unheard } && sequenceFailed && !recapShow.NextRaceCalled)",
            "Interlocked.CompareExchange(ref _recapShow, null, recapShow);");

        Assert.Contains("_announcer.NotifyAchievement(unlock.Transponder, unlock.Badge.Name);", badges, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE WINNER'S NAME WAS READ OFF THE PROFILES ON THE THREAD POOL, by the
    /// throw to pit lane and the hand-over's brief, inside the sequence. Read
    /// once on the UI thread now, before the sequence starts, and handed in.
    /// </summary>
    [Fact]
    public void The_flag_throw_s_winner_is_read_before_the_sequence_goes_to_the_background()
    {
        var sequence = WithoutComments(MethodBody(Source("RaceControlControl.AfterRace.cs"),
            "private void SpeakRaceSummary(", "private sealed record RecapPlan("));

        var read = sequence.IndexOf("flagWinner = FlagResultRows(record).Podium is { Count: > 0 } flagPodium ? WinnerName(flagPodium[0]) : string.Empty;", StringComparison.Ordinal);
        var background = sequence.IndexOf("_ = Task.Run(async () =>", StringComparison.Ordinal);

        Assert.True(read > 0 && background > read, "The winner's name is not read before the sequence goes to the thread pool.");

        var inBackground = sequence[background..];

        Assert.Contains("FlagHandOffBrief(flagWinner, resultRead)", inBackground, StringComparison.Ordinal);
        Assert.Contains("RenderFlagThrowAsync(flagWinner, followOnWork.Token)", inBackground, StringComparison.Ordinal);
        Assert.DoesNotContain("WinnerName(", inBackground, StringComparison.Ordinal);
        Assert.DoesNotContain("SpokenDriverName(", inBackground, StringComparison.Ordinal);
    }

    [Fact]
    public void The_flag_throw_and_its_brief_take_the_winner_s_name_and_read_no_profile()
    {
        var source = Source("RaceControlControl.AfterRace.cs");

        var thrown = WithoutComments(MethodBody(source,
            "private async Task<SpokenLine?> RenderFlagThrowAsync(string winner,", "private string WinnerName("));
        var brief = WithoutComments(MethodBody(source,
            "private HandOff.Brief FlagHandOffBrief(string winner,", "private string? FlagResultFor("));

        Assert.DoesNotContain("WinnerName(", thrown, StringComparison.Ordinal);
        Assert.DoesNotContain("WinnerName(", brief, StringComparison.Ordinal);
        Assert.DoesNotContain("_profiles", thrown, StringComparison.Ordinal);
        Assert.DoesNotContain("_profiles", brief, StringComparison.Ordinal);
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
